import json
import re
import sys
from random import randint, choice

import requests
from discord import Message, Member, Channel, PrivateChannel
from discord.ext.commands import Bot, Context
from requests import Response

from api import newline_separator, directions, regions, statuses, release_types
from api.request import ApiRequest
from bot_config import latest_limit, newest_header, invalid_command_text, oldest_header, bot_admin_id, \
    bot_channel_id
from bot_utils import get_code
from database import Moderator, init
from math_parse import NumericStringParser
from math_utils import limit_int
from log_analyzer import LogAnalyzer
from stream_handlers import stream_text_log, stream_gzip_decompress

bot = Bot(command_prefix="!")
id_pattern = '(?P<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][A-Z])[ \\-]?(?P<numbers>\\d{5})'  # see http://www.psdevwiki.com/ps3/Productcode
nsp = NumericStringParser()

bot_channel: Channel = None

file_handlers = (
    # {
    #     'ext': '.zip'
    # },
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


@bot.event
async def on_ready():
    print('Logged in as:')
    print(bot.user.name)
    print(bot.user.id)
    print('------')
    global bot_channel
    bot_channel = bot.get_channel(bot_channel_id)


@bot.event
async def on_message(message: Message):
    """
    OnMessage event listener
    :param ctx: context
    :param message: message
    """
    # Self reply detect
    if message.author.id == bot.user.id:
        return
    # Command detect
    try:
        if message.content[0] == "!":
            return await bot.process_commands(message)
    except IndexError:
        print("Empty message! Could still have attachments.")

    # Code reply
    code_list = []
    for matcher in re.finditer(id_pattern, message.content, flags=re.I):
        letter_part = str(matcher.group('letters'))
        number_part = str(matcher.group('numbers'))
        code = (letter_part + number_part).upper()
        if code not in code_list:
            code_list.append(code)
            print(code)
    if len(code_list) > 0:
        for code in code_list:
            info = get_code(code)
            if info is not None:
                await message.channel.send('```{}```'.format(info))
            else:
                await message.channel.send('```Serial not found in compatibility database, possibly untested!```')
        return

    # Log Analysis!
    if len(message.attachments) > 0:
        log = LogAnalyzer()
        sent_log = False
        print("Attachments present, looking for log file...")
        for attachment in filter(lambda a: any(e['ext'] in a['url'] for e in file_handlers), message.attachments):
            for handler in file_handlers:
                if attachment['url'].endswith(handler['ext']):
                    print("Found log attachment, name: {name}".format(name=attachment['filename']))
                    with requests.get(attachment['url'], stream=True) as response:
                        print("Opened request stream!")
                        # noinspection PyTypeChecker
                        for row in stream_line_by_line_safe(response, handler['handler']):
                            error_code = log.feed(row)
                            if error_code == LogAnalyzer.ERROR_SUCCESS:
                                continue
                            elif error_code == LogAnalyzer.ERROR_PIRACY:
                                await piracy_alert(message)
                                sent_log = True
                                break
                            elif error_code == LogAnalyzer.ERROR_OVERFLOW:
                                print("Possible Buffer Overflow Attack Detected!")
                                break
                            elif error_code == LogAnalyzer.ERROR_STOP:
                                await bot.send_message(message.channel, content=log.get_report())
                                sent_log = True
                                break
                            elif error_code == LogAnalyzer.ERROR_FAIL:
                                break
                        if not sent_log:
                            print("Log analyzer didn't finish, probably a truncated/invalid log!")
                    print("Stopping stream!")
        del log


async def piracy_alert(message: Message):
    await bot.send_message(message.channel, content=
        "Pirated release detected {author}!\n"
        "**You are being denied further support until you legally dump the game!**\n"
        "Please note that the RPCS3 community and its developers do not support piracy!\n"
        "Most of the issues caused by pirated dumps is because they have been tampered with in such a way "
        "and therefore act unpredictably on RPCS3.\n"
        "If you need help obtaining legal dumps please read <https://rpcs3.net/quickstart>\n".format(
            author=message.author.mention
            # trigger=mask(trigger),
            # bot_admin=message.server.get_member(bot_admin_id).mention
        )
    )


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
            elif len(buffer) > 1024 * 1024 or len(chunk_buffer) > 1024 * 1024:
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

    request = ApiRequest(ctx.message.author).set_search(search_string)
    response = request.request()
    await dispatch_message(response.to_string())


# noinspection PyMissingTypeHints
@bot.command(pass_context=True)
async def top(ctx: Context, *args):
    """
    Gets the x (default 10) top oldest/newest updated games
    Example usage:
        !top old 10
        !top new 10 ja
        !top old 10 all
        !top new 10 ja playable
        !top new 10 ja playable bluray
        !top new 10 ja loadable psn
    To see all filters do !filters
    """
    request = ApiRequest(ctx.message.author)
    if len(args) == 0 or args[0] not in ("new", "old"):
        print("Invalid command")
        return await bot_channel.send(invalid_command_text)

    if len(args) >= 1:
        if args[0] == "old":
            request.set_sort("date", "asc")
            request.set_custom_header(oldest_header)
        else:
            request.set_sort("date", "desc")
            request.set_custom_header(newest_header)
    if len(args) >= 2:
        request.set_amount(limit_int(int(args[1]), latest_limit))
    if len(args) >= 3:
        request.set_region(args[2])
    if len(args) >= 4:
        request.set_status(args[3])
    if len(args) >= 5:
        request.set_release_type(args[4])

    string = request.request().to_string()
    await dispatch_message(string)


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


async def dispatch_message(message: str):
    """
    Dispatches messages one by one divided by the separator defined in api.config
    :param message: message to dispatch
    """
    for part in message.split(newline_separator):
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
        "Of course not", "Seriously no", "Noooooooooo", "Most likely not", "Não", "Non", "Hell no", "Absolutely not"
    ]))


async def is_sudo(ctx: Context):
    message: Message = ctx.message
    author: Member = message.author
    sudo_user: Moderator = Moderator.get_or_none(
        Moderator.discord_id == author.id, Moderator.sudoer == True
    )
    if sudo_user is not None:
        print("User is sudoer, allowed!")
        return True
    else:
        await ctx.channel.send(
            "{mention} is not a sudoer, this incident will be reported!".format(mention=author.mention)
        )
        return False


async def is_mod(ctx: Context):
    message: Message = ctx.message
    author: Member = message.author
    mod_user: Moderator = Moderator.get_or_none(
        Moderator.discord_id == author.id
    )
    if mod_user is not None:
        print("User is moderator, allowed!")
        return True
    else:
        await ctx.channel.send(
            "{mention} is not a mod, this incident will be reported!".format(mention=author.mention)
        )
        return False


async def is_private_channel(ctx: Context):
    message: Message = ctx.message
    author: Member = message.author
    if isinstance(ctx.channel, PrivateChannel):
        return True
    else:
        await ctx.channel.send(
            '{mention} https://i.imgflip.com/24qx11.jpg'.format(
                mention=author.mention
            )
        )
        return False


@bot.group()
async def sudo(ctx: Context):
    if not await is_sudo(ctx):
        ctx.invoked_subcommand = None
    if ctx.invoked_subcommand is None:
        await ctx.send('Invalid !sudo command passed...')


@sudo.command()
async def say(ctx: Context, *args):
    print(int(args[0][2:-1]))
    channel: Channel = bot.get_channel(int(args[0][2:-1])) \
        if args[0][:2] == '<#' and args[0][-1] == '>' \
        else ctx.channel
    await channel.send(' '.join(args if channel.id == ctx.channel.id else args[1:]))


@sudo.group()
async def mod(ctx: Context):
    if ctx.invoked_subcommand is None:
        await ctx.send('Invalid !sudo mod command passed...')


@mod.command()
async def add(ctx: Context, user: Member):
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


@mod.command()
async def sudo(ctx: Context, user: Member):
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
async def piracy_filter(ctx: Context):
    if await is_mod(ctx) and await is_private_channel(ctx):
        if ctx.invoked_subcommand is None:
            await ctx.send('Invalid piracy_filter command passed...')


print(sys.argv[1])
init()
bot.run(sys.argv[1])
