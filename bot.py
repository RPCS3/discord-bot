import json
import os
import re
import subprocess
import sys
from datetime import datetime, timedelta
from random import randint, choice
from typing import List

import requests
from discord import Message, Member, TextChannel, DMChannel, Forbidden, Reaction, User, Role, Embed, Emoji
from discord.ext.commands import Bot, Context, UserConverter
from discord.utils import get
from requests import Response
from peewee import fn

from api import newline_separator, directions, regions, statuses, release_types, trim_string
from api.request import ApiRequest
from api.utils import sanitize_string, trim_string
from bot_config import *
from bot_utils import get_code
from database import Moderator, init, PiracyString, Warning, Explanation
from log_analyzer import LogAnalyzer
from math_parse import NumericStringParser
from math_utils import limit_int
from stream_handlers import stream_text_log, stream_gzip_decompress, stream_zip_decompress, Deflate64Exception

bot = Bot(command_prefix="!")
id_pattern = '(?P<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][ABSM])[ \\-]?(?P<numbers>\\d{5})'  # see http://www.psdevwiki.com/ps3/Productcode
nsp = NumericStringParser()

bot_channel: TextChannel = None
rules_channel: TextChannel = None
bot_log: TextChannel = None

reaction_confirm: Emoji = None
reaction_failed: Emoji = None
reaction_deny: Emoji = None

file_handlers = (
    {
        'ext': '.zip',
        'handler': stream_zip_decompress
    },
    {
        'ext': '.log',
        'handler': stream_text_log
    },
    {
        'ext': '.log.gz',
        'handler': stream_gzip_decompress
    },
    # {
    #     'ext': '.7z',
    #     'handler': stream_7z_decompress
    # }
)


async def generic_error_handler(ctx: Context, error):
    await react_with(ctx, reaction_failed)
    await ctx.send(str(error))


async def react_with(ctx: Context, reaction: Emoji):
    try:
        msg = ctx if isinstance(ctx, Message) else ctx.message
        await msg.add_reaction(reaction)
    except Exception as e:
        print("Couldn't add a reaction: " + str(e))


async def is_private_channel(ctx: Context, gay=True):
    if isinstance(ctx.channel, DMChannel):
        return True
    else:
        if gay:
            message: Message = ctx.message
            author: Member = message.author
            await ctx.channel.send('{mention} https://i.imgflip.com/24qx11.jpg'.format(mention=author.mention))
        return False


@bot.event
async def on_ready():
    print('Logged in as:')
    print(bot.user.name)
    print(bot.user.id)
    print('------')
    global bot_channel
    global bot_log
    global rules_channel
    global reaction_confirm
    global reaction_failed
    global reaction_deny
    bot_channel = bot.get_channel(bot_channel_id)
    rules_channel = bot.get_channel(bot_rules_channel_id)
    bot_log = bot.get_channel(bot_log_id)
    reaction_confirm = '👌'
    reaction_failed = '⛔'
    reaction_deny = '👮'
    refresh_piracy_cache()
    await refresh_moderated_messages()
    print("Bot is ready to serve!")


async def refresh_moderated_messages():
    print("Checking moderated channels for missed new messages...")
    for channel_id in user_moderatable_channel_ids:
        channel = bot.get_channel(channel_id)
        if channel is not None:
            try:
                async for msg in channel.history(after=(datetime.utcnow()-timedelta(hours=12))):
                    for reaction in msg.reactions:
                        if reaction.emoji == user_moderation_character:
                            usr = (await reaction.users(limit=1).flatten())[0]
                            await on_reaction_add(reaction, usr)
            except Exception as e:
                print("Uh oh: " + str(e))
                pass


@bot.event
async def on_reaction_add(reaction: Reaction, user: User):
    message: Message = reaction.message
    if message.author == bot.user or user == bot.user or await is_private_channel(message, gay=False):
        #print("Author is bot or this is in DMs")
        return

    role: Role
    for role in message.author.roles:
        if role.name.strip() in user_moderation_excused_roles:
            #print(role.name + " is excused")
            return

    #print("Checking for starbucks...")
    if message.channel.id in user_moderatable_channel_ids:
        #print("Checking for message expiration date...")
        if (datetime.now() - message.created_at).total_seconds() < 12 * 60 * 60:
            #print("Checking for " + user_moderation_character + " count...")
            if reaction.emoji == user_moderation_character:
                #print(reaction.count)
                reporters = []
                user: Member
                async for user in reaction.users():
                    if user == bot.user:
                        #print("Found bot reaction, bailing out")
                        return
                    role: Role
                    for role in user.roles:
                        #print(role.name)
                        if role.name != '@everyone':
                            reporters.append(user)
                            break
                    #print(len(reporters))

                if len(reporters) >= user_moderation_count_needed:
                    await react_with(message, user_moderation_character)
                    # noinspection PyTypeChecker
                    await report("User moderation report ⭐💵", trigger=None, trigger_context=None, message=message, reporters=reporters,
                                 attention=True)


@bot.event
async def on_message_edit(before: Message, after: Message):
    """
    OnMessageEdit event listener
    :param before: message
    :param after: message
    """
    await piracy_check(after)


@bot.event
async def on_message(message: Message):
    """
    OnMessage event listener
    :param message: message
    """
    # Self reply detect
    if message.author == bot.user:
        return
    # Piracy detect
    if await piracy_check(message):
        return
    # Command detect
    try:
        if message.content[0] == "!":
            return await bot.process_commands(message)
    except IndexError:
        print("Empty message! Could still have attachments.")

    # Code reply
    code_list = []
    for matcher in re.finditer(id_pattern, message.content, flags=re.IGNORECASE):
        letter_part = str(matcher.group('letters'))
        number_part = str(matcher.group('numbers'))
        code = (letter_part + number_part).upper()
        if code not in code_list:
            code_list.append(code)
            print(code)
    if len(code_list) > 0:
        max_len = 5
        if isinstance(message.channel, DMChannel):
            max_len = min(len(code_list), 50)
        for code in code_list[:(max_len)]:
            info = get_code(code)
            if info.status == "Maintenance":
                await message.channel.send(info.to_string())
                break
            else:
                await message.channel.send(embed=info.to_embed())
        return

    # Log Analysis!
    if len(message.attachments) > 0:
        log = LogAnalyzer()
        print("Attachments present, looking for log file...")
        for attachment in filter(lambda a: any(e['ext'] in a.url for e in file_handlers), message.attachments):
            for handler in file_handlers:
                if attachment.url.endswith(handler['ext']):
                    print("Found log attachment, name: {name}".format(name=attachment.filename))
                    with requests.get(attachment.url, stream=True) as response:
                        print("Opened request stream!")
                        # noinspection PyTypeChecker
                        try:
                            sent_log = False
                            result = None
                            for row in stream_line_by_line_safe(response, handler['handler']):
                                error_code = log.feed(row)
                                if error_code == LogAnalyzer.ERROR_SUCCESS:
                                    continue
                                elif error_code == LogAnalyzer.ERROR_PIRACY:
                                    await piracy_alert(message, log.get_trigger(), log.get_trigger_context())
                                    sent_log = True
                                    break
                                elif error_code == LogAnalyzer.ERROR_OVERFLOW:
                                    print("Possible Buffer Overflow Attack Detected!")
                                    if result is None:
                                        result = log.get_embed_report()
                                    result = result.add_field(name="Notes",
                                                              value="Log was too large, showing last processed run")
                                    break
                                elif error_code == LogAnalyzer.ERROR_STOP:
                                    # await message.channel.send(log.get_text_report(), embed=log.product_info.to_embed())
                                    result = log.get_embed_report()
                                elif error_code == LogAnalyzer.ERROR_FAIL:
                                    print("Log parsing failed")
                                    break
                            if not sent_log:
                                if result is None:
                                    await message.channel.send(
                                        "Log analysis failed, most likely cause is a truncated/invalid log."
                                    )
                                    print("Log analyzer didn't finish, probably a truncated/invalid log!")
                                else:
                                    await message.channel.send(embed=result)
                        except Deflate64Exception:
                            await message.channel.send(
                                "Unsupported compression algorithm used.\n"
                                "\tAlgorithm name: Deflate64\n"
                                "\tAlternative: Deflate\n"
                                "\tOther alternatives: Default .log.gz file, Raw .log file\n"
                            )
                    print("Stopping stream!")
        del log


async def report(report_kind: str, trigger: str, trigger_context: str, message: Message, reporters: List[Member], attention=False):
    author: Member = message.author
    channel: TextChannel = message.channel
    user: User = author._user
    offending_content = message.content

    if len(message.attachments) > 0:
        if offending_content is not None and offending_content != "":
            offending_content += "\n"
        for att in message.attachments:
            offending_content += "\n📎 " + att.filename
    if offending_content is None or offending_content == "":
        offending_content = "🤔 something fishy is going on here, there was no message or attachment"
    report_text = ("Triggered by: `" + trigger + "`\n") if trigger is not None else ""
    report_text += ("Triggered in: ```" + trigger_context + "```\n") if trigger_context is not None else ""
    report_text += "Not deleted/requires attention: @here" if attention else "Deleted/Doesn't require attention"
    e = Embed(
        title="Report for {}".format(report_kind),
        description=report_text,
        color=0xe74c3c if attention else 0xf9b32f
    )
    e.add_field(name="Violator", value=message.author.mention)
    e.add_field(name="Channel", value=channel.mention)
    e.add_field(name="Time", value=message.created_at)
    e.add_field(name="Contents of offending item", value=offending_content, inline=False)
    if attention:
        e.add_field(
            name="Search query (since discord doesnt allow jump links <:panda_face:436991868547366913>)",
            value="`from: {nick} during: {date} in: {channel} {text}`".format(
                nick=user.name + "#" + user.discriminator,
                date=message.created_at.strftime("%Y-%m-%d"),
                channel=channel.name,
                text=message.content.split(" ")[-1]
            ),
            inline=False
        )
    if reporters is not None:
        e.add_field(name="Reporters", value='\n'.join([x.mention for x in reporters]))
    await bot_log.send("", embed=e)


# noinspection PyTypeChecker
async def piracy_check(message: Message):
    for trigger in piracy_strings:
        if trigger.lower() in message.content.lower():  # we should .lower() on trigger add ideally
            try:
                await message.delete()
            except Forbidden:
                print("Couldn't delete the moderated message")
                await report("Piracy", trigger, None, message, None, attention=True)
                return
            await message.channel.send(
                "{author} Please follow the {rules} and do not discuss "
                "piracy on this server. Repeated offence may result in a ban.".format(
                    author=message.author.mention,
                    rules=rules_channel.mention
                ))
            await report("Piracy", trigger, None, message, None, attention=False)
            await add_warning_for_user(message.channel, message.author.id, bot.user.id,
                                       'Pirated Phrase Mentioned',
                                       str(message.created_at) + ' - ' + message.content)
            return True


# noinspection PyTypeChecker
async def piracy_alert(message: Message, trigger: str, trigger_context: str):
    try:
        await message.delete()
        await report("Pirated Release", trigger, trigger_context, message, None, attention=False)
    except Forbidden:
        print("Couldn't delete the moderated log attachment")
        await report("Pirated Release", trigger, trigger_context, message, None, attention=True)

    await message.channel.send(
        "Pirated release detected {author}!\n"
        "**You are being denied further support until you legally dump the game!**\n"
        "Please note that the RPCS3 community and its developers do not support piracy!\n"
        "Most of the issues caused by pirated dumps is because they have been tampered with in such a way "
        "and therefore act unpredictably on RPCS3.\n"
        "If you need help obtaining legal dumps please read <https://rpcs3.net/quickstart>\n".format(
            author=message.author.mention
        )
    )
    await add_warning_for_user(message.channel, message.author.id, bot.user.id,
                               'Pirated Release Detected',
                               str(message.created_at) + ' - ' + message.content + ' - ' + trigger)


def mask(string: str):
    return ''.join("*" if i % 1 == 0 else char for i, char in enumerate(string, 1))


def stream_line_by_line_safe(stream: Response, func: staticmethod):
    buffer = ''
    chunk_buffer = b''
    for chunk in func(stream):
        try:
            chunk_buffer += chunk
            message = chunk_buffer.decode('UTF-8')
            chunk_buffer = b''
            if '\n' in message:
                parts = message.split('\n')
                yield buffer + parts[0]
                buffer = ''
                for part in parts[1:-1]:
                    yield part
                buffer += parts[-1]
            elif len(buffer) > overflow_threshold or len(chunk_buffer) > overflow_threshold:
                print('Possible overflow intended, piss off!')
                break
            else:
                buffer += message
        except UnicodeDecodeError as ude:
            if ude.end == len(chunk_buffer):
                pass
            else:
                print("{}\n{} {} {} {}".format(chunk_buffer, ude.reason, ude.start, ude.end, len(chunk_buffer)))
                break
        del chunk
    del buffer


@bot.command()
async def math(ctx: Context, *args):
    """Math, here you go Juhn"""
    return await ctx.send(nsp.eval(''.join(map(str, args))))


# noinspection PyShadowingBuiltins
@bot.command()
async def credits(ctx: Context):
    """Author Credit"""
    return await ctx.send("```\nMade by Roberto Anic Banic aka nicba1010!\n```")


# noinspection PyMissingTypeHints
@bot.command(pass_context=True)
async def c(ctx, *args):
    """Searches the compatibility database, USE: !c searchterm """
    await compat_search(ctx, *args)


# noinspection PyMissingTypeHints
@bot.command(pass_context=True)
async def compat(ctx, *args):
    """Searches the compatibility database, USE: !compat searchterm"""
    await compat_search(ctx, *args)


# noinspection PyMissingTypeHints,PyMissingOrEmptyDocstring
async def compat_search(ctx, *args):
    search_string = ""
    for arg in args:
        search_string += (" " + arg) if len(search_string) > 0 else arg

    search_string = trim_string(search_string, 40)
    request = ApiRequest(ctx.message.author).set_search(search_string)
    response = request.request()
    await dispatch_message(response.to_string())


# noinspection PyMissingTypeHints
@bot.command(pass_context=True)
async def top(ctx: Context, *args):
    """
    Gets the x (default is 10 new) top games by specified criteria; order is flexible
    Example usage:
        !top 10 new
        !top 10 new jpn
        !top 10 playable
        !top 10 new ingame eu
        !top 10 old psn intro
        !top 10 old loadable us bluray

    To see all filters do !filters
    """
    request = ApiRequest(ctx.message.author)
    age = "new"
    amount = 10
    for arg in args:
        arg = arg.lower()
        if arg in ["old", "new"]:
            age = arg
        elif arg in ["nothing", "loadable", "intro", "ingame", "playable"]:
            request.set_status(arg)
        elif arg in ["bluray", "blu-ray", "disc", "psn", "b", "d", "n", "p"]:
            request.set_release_type(arg.replace("-", ""))
        elif arg.isdigit():
            amount = limit_int(int(arg), latest_limit)
        else:
            request.set_region(arg)
    request.set_amount(amount)
    if age == "old":
        request.set_sort("date", "asc")
        request.set_custom_header(oldest_header)
    else:
        request.set_sort("date", "desc")
        request.set_custom_header(newest_header)
    string = request.request().to_string()
    await dispatch_message(string, True)


@bot.command()
async def filters(ctx: Context):
    message = "**Sorting directions (not used in top command)**\n"
    message += "Ascending\n```" + str(directions["a"]) + "```\n"
    message += "Descending\n```" + str(directions["d"]) + "```\n"
    message += "**Regions**\n"
    message += "Japan\n```" + str(regions["j"]) + "```\n"
    message += "US\n```" + str(regions["u"]) + "```\n"
    message += "EU\n```" + str(regions["e"]) + "```\n"
    message += "Asia\n```" + str(regions["a"]) + "```\n"
    message += "Korea\n```" + str(regions["k"]) + "```\n"
    message += "Hong-Kong\n```" + str(regions["h"]) + "```\n"
    message += "**Statuses**\n"
    message += "All\n```" + str(statuses["all"]) + "```\n"
    message += "Playable\n```" + "playable" + "```\n"
    message += "Ingame\n```" + "ingame" + "```\n"
    message += "Intro\n```" + "intro" + "```\n"
    message += "Loadable\n```" + "loadable" + "```\n"
    message += "Nothing\n```" + "nothing" + "```\n"
    message += "**Sort Types (not used in top command)**\n"
    message += "ID\n```" + "id" + "```\n"
    message += "Title\n```" + "title" + "```\n"
    message += "Status\n```" + "status" + "```\n"
    message += "Date\n```" + "date" + "```\n"
    message += "**Release Types**\n"
    message += "Blu-Ray\n```" + str(release_types["b"]) + "```\n"
    message += "PSN\n```" + str(release_types["n"]) + "```\n"
    await ctx.author.send(message)


async def dispatch_message(message: str, clean_up_first_line=False):
    """
    Dispatches messages one by one divided by the separator defined in api.config
    :param message: message to dispatch
    """
    message_parts = message.split(newline_separator)
    if clean_up_first_line:
        message_parts[0] = message_parts[0].replace("  ", " ").replace("  ", " ")
    for part in message_parts:
        await bot_channel.send(part)


@bot.command()
async def latest(ctx: Context):
    """Get the latest RPCS3 build link"""
    latest_build = json.loads(requests.get("https://update.rpcs3.net/?c=somecommit").content)['latest_build']
    return await ctx.author.send(
        "PR: {pr}\nWindows:\n\tTime: {win_time}\n\t{windows_url}\nLinux:\n\tTime: {linux_time}\n\t{linux_url}".format(
            pr=latest_build['pr'],
            win_time=latest_build['windows']['datetime'],
            windows_url=latest_build['windows']['download'],
            linux_time=latest_build['windows']['datetime'],
            linux_url=latest_build['linux']['download']
        )
    )


# User requests
# noinspection PyMissingTypeHints,PyMissingOrEmptyDocstring
@bot.command()
async def roll(ctx: Context, *args):
    """Generates a random number between 0 and n (default 10)"""
    n = 10
    if len(args) >= 1:
        try:
            n = int(args[0])
        except ValueError:
            pass
    await ctx.channel.send("You rolled a {}!".format(randint(0, n)))


# noinspection PyMissingTypeHints,PyMissingOrEmptyDocstring
@bot.command(name="8ball")
async def eight_ball(ctx: Context):
    """Generates a random answer to your question"""
    await ctx.send(choice([
        "Nah mate", "Ya fo sho", "Fo shizzle mah nizzle", "Yuuuup", "Nope", "Njet", "Da", "Maybe", "I don't know",
        "I don't care", "Affirmative", "Sure", "Yeah, why not", "Most likely", "Sim", "Oui", "Heck yeah!", "Roger that",
        "Aye!", "Yes without a doubt m8!", "Who cares", "Maybe yes, maybe not", "Maybe not, maybe yes", "Ugh",
        "Probably", "Ask again later", "Error 404: answer not found", "Don't ask me that again",
        "You should think twice before asking", "You what now?", "Bloody hell, answering that ain't so easy",
        "Of course not", "Seriously no", "Noooooooooo", "Most likely not", "Não", "Non", "Hell no", "Absolutely not",
        "Ask Neko", "Ask Ani", "I'm pretty sure that's illegal!", "<:cell_ok_hand:324618647857397760>",
        "Don't be an idiot. YES.",
        "What do *you* think?", "Only on Wednesdays"
    ]))


async def is_sudo(ctx: Context):
    message: Message = ctx.message
    author: Member = message.author
    sudo_user: Moderator = Moderator.get_or_none(
        Moderator.discord_id == author.id, Moderator.sudoer == True
    )
    if sudo_user is not None:
        print("User " + author.display_name + " is sudoer, allowed!")
        return True
    else:
        await react_with(ctx, reaction_deny)
        await ctx.channel.send(
            "{mention} is not a sudoer, this incident will be reported!".format(mention=author.mention)
        )
        return False


async def is_mod(ctx: Context, report: bool = True):
    message: Message = ctx.message
    author: Member = message.author
    mod_user: Moderator = Moderator.get_or_none(
        Moderator.discord_id == author.id
    )
    if mod_user is not None:
        print("User " + author.display_name + " is moderator, allowed!")
        return True
    else:
        if report:
            await react_with(ctx, reaction_deny)
            await ctx.channel.send("{mention} is not a mod, this incident will be reported!".format(mention=author.mention))
        return False


@bot.group()
async def sudo(ctx: Context):
    """Sudo command group, used to manage moderators and sudoers."""
    if not await is_sudo(ctx):
        ctx.invoked_subcommand = None
        return
    if ctx.invoked_subcommand is None:
        await ctx.send('Invalid !sudo command passed...')


@sudo.command()
async def say(ctx: Context, *args):
    """Basically says whatever you want it to say in a channel."""
    channel: TextChannel = bot.get_channel(int(args[0][2:-1])) \
        if args[0][:2] == '<#' and args[0][-1] == '>' \
        else ctx.channel
    await channel.send(' '.join(args if channel.id == ctx.channel.id else args[1:]))


@sudo.command()
async def restart(ctx: Context, *args):
    """Restarts bot and pulls newest commit."""
    process = subprocess.Popen(["git", "pull"], stdout=subprocess.PIPE)
    await ctx.send(str(process.communicate()[0], "utf-8"))
    await ctx.send('Restarting...')
    os.execl(sys.executable, sys.argv[0], *sys.argv)


@sudo.group()
async def mod(ctx: Context):
    """Mod subcommand for sudo mod group."""
    if ctx.invoked_subcommand is None:
        await ctx.send('Invalid !sudo mod command passed...')


@mod.command()
async def add(ctx: Context, user: Member):
    """Adds a new moderator."""
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is None:
        Moderator(discord_id=user.id).save()
        await ctx.send(
            "{mention} successfully added as moderator, you now have access to editing the piracy trigger list "
            "and other useful things! I will send you the available commands to your message box!".format(
                mention=user.mention
            )
        )
    else:
        await ctx.send(
            "{mention} is already a moderator!".format(
                mention=user.mention
            )
        )


@mod.command(name="del")
async def delete(ctx: Context, user: Member):
    """Removes a moderator."""
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is not None:
        if moderator.discord_id != bot_admin_id:
            if moderator.delete_instance():
                await ctx.send(
                    "{mention} removed as moderator!".format(
                        mention=user.mention
                    )
                )
            else:
                await ctx.send(
                    "Something went wrong!".format(
                        mention=user.mention
                    )
                )
        else:
            await ctx.send(
                "{author_mention} why would you even try this! Alerting {mention}!".format(
                    author_mention=ctx.message.author_mention.mention,
                    mention=ctx.message.server.get_member(bot_admin_id).mention
                )
            )
    else:
        await ctx.send(
            "{mention} not found in moderators table!".format(
                mention=user.mention
            )
        )


# noinspection PyShadowingBuiltins
@mod.command(name="list")
async def list_mods(ctx: Context):
    """Lists all moderators."""
    buffer = '```\n'
    for moderator in Moderator.select():
        row = '{username:<32s} | {sudo}\n'.format(
            username=bot.get_user(moderator.discord_id).name,
            sudo=('sudo' if moderator.sudoer else 'not sudo')
        )
        if len(buffer) + len(row) + 3 > 2000:
            await ctx.send(buffer + '```')
            buffer = '```\n'
        buffer += row
    if len(buffer) > 4:
        await ctx.send(buffer + '```')


@mod.command()
async def sudo(ctx: Context, user: Member):
    """Makes a moderator a sudoer."""
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is not None:
        if moderator.sudoer is False:
            moderator.sudoer = True
            moderator.save()
            await ctx.send(
                "{mention} successfully granted sudo permissions!".format(
                    mention=user.mention
                )
            )
        else:
            await ctx.send(
                "{mention} already has sudo permissions!".format(
                    mention=user.mention
                )
            )
    else:
        await ctx.send(
            "{mention} does not exist in moderator list, please add as moderator with mod_add!".format(
                mention=user.mention
            )
        )


@mod.command()
async def unsudo(ctx: Context, user: Member):
    """Removes a moderator from sudoers."""
    message: Message = ctx.message
    author: Member = message.author
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is not None:
        if moderator.discord_id != bot_admin_id:
            if moderator.sudoer is True:
                moderator.sudoer = False
                moderator.save()
                await ctx.send(
                    "Successfully took away sudo permissions from {mention}".format(
                        mention=user.mention
                    )
                )
            else:
                await ctx.send(
                    "{mention} already doesn't have sudo permissions!".format(
                        mention=user.mention
                    )
                )
        else:
            await  ctx.send(
                "{author_mention} why would you even try this! Alerting {mention}!".format(
                    author_mention=author.mention,
                    mention=bot.get_user(bot_admin_id).mention
                )
            )
    else:
        await ctx.send(
            "{mention} does not exist in moderator list!".format(
                mention=user.mention
            )
        )


@bot.group()
async def piracy(ctx: Context):
    """Command used to manage piracy filters."""
    if not await is_mod(ctx):
        ctx.invoked_subcommand = None
        return

    if await is_private_channel(ctx):
        if ctx.invoked_subcommand is None:
            await ctx.send('Invalid piracy command passed...')
    else:
        ctx.invoked_subcommand = None


# noinspection PyShadowingBuiltins
@piracy.command(name="list")
async def list_piracy(ctx: Context):
    """Lists all filters."""
    buffer = '```\n'
    for piracy_string in PiracyString.select():
        row = str(piracy_string.id).zfill(4) + ' | ' + piracy_string.string + '\n'
        if len(buffer) + len(row) + 3 > 2000:
            await ctx.send(buffer + '```')
            buffer = '```\n'
        buffer += row
    if len(buffer) > 4:
        await ctx.send(buffer + '```')


@piracy.command()
async def add(ctx: Context, trigger: str):
    """Adds a filter."""
    piracy_string = PiracyString.get_or_none(PiracyString.string == trigger)
    if piracy_string is None:
        PiracyString(string=trigger).save()
        await ctx.send("Item successfully saved!")
        await list_piracy.invoke(ctx)
        refresh_piracy_cache()
    else:
        await ctx.send("Item already exists at id {id}!".format(id=piracy_string.id))


# noinspection PyShadowingBuiltins
@piracy.command()
async def delete(ctx: Context, id: int):
    """Removes a filter."""
    piracy_string: PiracyString = PiracyString.get_or_none(PiracyString.id == id)  # Column actually exists but hidden
    if piracy_string is not None:
        piracy_string.delete_instance()
        await ctx.send("Item successfully deleted!")
        await list_piracy.invoke(ctx)
        refresh_piracy_cache()
    else:
        await ctx.send("Item does not exist!")


@bot.group()
async def warn(ctx: Context):
    """Command used to issue and manage warnings. USE: !warn @user reason"""
    if ctx.invoked_subcommand == bot.get_command("warn list") or await is_mod(ctx):
        if ctx.invoked_subcommand is None:
            args = ctx.message.content.split(' ')[1:]
            user_id = int(args[0][3:-1] if args[0][2] == '!' else args[0][2:-1])
            user: User = bot.get_user(user_id)
            reason: str = ' '.join(args[1:])
            if await add_warning_for_user(ctx, ctx.message.author.id, user_id, reason):
                await react_with(ctx, reaction_confirm)
                await list_warnings_for_user(ctx, user_id, user.name if user is not None else "unknown user")
            else:
                await react_with(ctx, reaction_failed)
                await list_warnings_for_user(ctx, user_id, user.name if user is not None else "")
    else:
        ctx.invoked_subcommand = None


async def add_warning_for_user(ctx, user_id, reporter_id, reason: str, full_reason: str = '') -> bool:
    if reason is None:
        await ctx.send("A reason needs to be provided...")
        return False

    Warning(discord_id=user_id, issuer_id=reporter_id, reason=reason, full_reason=full_reason).save()
    num_warnings: int = Warning.select().where(Warning.discord_id == user_id).count()
    await ctx.send("User warning saved! User currently has {} {}!".format(
        num_warnings,
        'warning' if num_warnings % 10 == 1 and num_warnings % 100 != 11 else "warnings"
    ))
    return True


# noinspection PyShadowingBuiltins
@warn.command(name="list")
async def list_warnings(ctx: Context, user: str = None):
    """Lists users with warnings, or all warnings for a given user."""
    if user is None:
        if await is_mod(ctx):
            await list_users_with_warnings(ctx)
    else:
        try:
            discord_user = await UserConverter().convert(ctx, user)
        except Exception:
            discord_user = None
        if discord_user is None:
            if await is_mod(ctx):
                await list_warnings_for_user(ctx, int(user[2:-1]), "unknown user")
        else:
            if discord_user == ctx.message.author or await is_mod(ctx):
                await list_warnings_for_user(ctx, discord_user.id, discord_user.name)


async def list_users_with_warnings(ctx: Context):
    is_private = await is_private_channel(ctx, gay=False)
    quotes = "```" # if not is_private else ""
    buffer = "Warning count per user:" + quotes + "\n"
    for user_row in Warning.select(Warning.discord_id, fn.COUNT(Warning.reason).alias('num')).group_by(Warning.discord_id):
        user_id = user_row.discord_id
        user: User = bot.get_user(user_id)
        user_name = user.display_name if user is not None else "unknown user"
        # if is_private:
        #     row = "<@{}>: {}\n".format(user_id, user_row.num)
        # else:
        row = str(sanitize_string(user_name.ljust(25))) + ' | ' + \
                ((str(user_id).ljust(18) + ' | ') if is_private else "") + \
                str(user_row.num).rjust(2) + '\n'
        if len(buffer) + len(row) + len(quotes) > 2000:
            await ctx.send(buffer + quotes)
            buffer = quotes + '\n'
        buffer += row
    if len(buffer) > 4:
        await ctx.send(buffer + quotes)

async def list_warnings_for_user(ctx: Context, user: User):
    if user is None:
        await ctx.send("A user to scan for needs to be provided...")
        return

    await list_warnings_for_user(ctx, user.id, user.display_name)

async def list_warnings_for_user(ctx: Context, user_id: int, user_name: str):
    if Warning.select().where(Warning.discord_id == user_id).count() == 0:
        await ctx.send(user_name + " has no warnings, is a standup citizen, and a pillar of this community")
        return

    is_private = await is_private_channel(ctx, gay=False) and await is_mod(ctx, report=False)
    buffer = 'Warning list for ' + sanitize_string(user_name) + ':\n```\n'
    for warning in Warning.select().where(Warning.discord_id == user_id):
        row = str(warning.id).zfill(5) + ' | ' + \
                (bot.get_user(warning.issuer_id).display_name if warning.issuer_id > 0 else "").ljust(25) + ' | ' + \
                warning.reason + \
                (' | ' + warning.full_reason if is_private else '') + '\n'
        if len(buffer) + len(row) + 3 > 2000:
            await ctx.send(buffer + '```')
            buffer = '```\n'
        buffer += row
    if len(buffer) > 4:
        await ctx.send(buffer + '```')


# noinspection PyShadowingBuiltins
@warn.command()
async def remove(ctx: Context, id: int):
    """Removes a warning."""
    warning: Warning = Warning.get_or_none(Warning.id == id)  # Column actually exists but hidden
    if warning is not None:
        warning.delete_instance()
        await ctx.send("Warning successfully deleted!")
        user = bot.get_user(warning.discord_id)
        if user is not None:
            await list_warnings_for_user(ctx, user.id, user.display_name)
        else:
            await list_warnings_for_user(ctx, warning.discord_id, "unknown user")
    else:
        await ctx.send("Warning does not exist!")


@bot.group()
async def explain(ctx: Context):
    """Command used to show and manage explanations. USE: !explain term"""
    if ctx.invoked_subcommand is None:
        args = ctx.message.content.split(' ')[1:]
        if (len(args) == 0):
            await ctx.send("Use !explain term")
            return
        term = args[0]
        explanation = Explanation.get_or_none(Explanation.keyword == term)
        if explanation is None:
            await react_with(ctx, reaction_failed)
        else:
            await ctx.send(explanation.text)


@explain.command()
async def add(ctx: Context):
    """Add new term with specified explanation. USE: !explain add <term> <explanation>"""
    if not await is_mod(ctx):
        return

    args = ctx.message.content.split(maxsplit=3)
    if (len(args) != 4):
        await react_with(ctx, reaction_failed)
        return

    term = args[2]
    text = args[3]
    if Explanation.get_or_none(Explanation.keyword == term) is None:
        try:
            Explanation(keyword=term, text=text).save()
            await react_with(ctx, reaction_confirm)
        except Exception:
            await react_with(ctx, reaction_failed)
    else:
        await react_with(ctx, reaction_failed)
        await ctx.send("Term `" + term + "` already exists, use !explain update instead")


@add.error
async def add_error(ctx: Context, error):
    await generic_error_handler(ctx, error)


@explain.command()
async def list(ctx: Context):
    """List all known terms that could be used for !explain command"""
    buffer = 'Defined terms:\n'
    for explanation in Explanation.select(Explanation.keyword).order_by(Explanation.keyword):
        row = explanation.keyword + '\n'
        if len(buffer) + len(row) > 2000:
            await ctx.send(buffer)
            buffer = ''
        buffer += row
    if len(buffer) > 4:
        await ctx.send(buffer)


@explain.command()
async def update(ctx: Context):
    """Update explanation for a given term. USE: !explain update <term> <new explanation>"""
    if not await is_mod(ctx):
        return

    args = ctx.message.content.split(maxsplit=3)
    if (len(args) != 4):
        await react_with(ctx, reaction_failed)
        return

    term = args[2]
    text = args[3]
    if Explanation.get_or_none(Explanation.keyword == term) is None:
        await react_with(ctx, reaction_failed)
        await ctx.send("Term `" + term + "` has not been defined yet")
    else:
        try:
            (Explanation.update({Explanation.text: text})
                        .where(Explanation.keyword == term)
            ).execute()
            await react_with(ctx, reaction_confirm)
        except Exception:
            await react_with(ctx, reaction_failed)


@update.error
async def add_error(ctx: Context, error):
    await generic_error_handler(ctx, error)


@explain.command()
async def remove(ctx: Context, *, term: str):
    """Removes term explanation"""
    if not await is_mod(ctx):
        return

    if Explanation.get_or_none(Explanation.keyword == term) is None:
        await react_with(ctx, reaction_failed)
        await ctx.send("Term `" + term + "` is not defined")
    else:
        try:
            (Explanation.delete().where(Explanation.keyword == term)).execute()
            await react_with(ctx, reaction_confirm)
        except Exception:
            await react_with(ctx, reaction_failed)


@remove.error
async def remove_error(ctx: Context, error):
    await generic_error_handler(ctx, error)


def refresh_piracy_cache():
    print("Refreshing piracy cache!")
    piracy_strings.clear()
    for piracy_string in PiracyString.select():
        piracy_strings.append(piracy_string.string)


print(sys.argv[1])
init()
bot.run(sys.argv[1])
