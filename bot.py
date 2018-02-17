import json
import re
import sys
from random import randint, choice

import discord
import requests
from discord import Message, Member, Server, Channel
from discord.ext.commands import Bot, Context
from requests import Response

from api import newline_separator, directions, regions, statuses, release_types
from api.request import ApiRequest
from bot_config import latest_limit, newest_header, invalid_command_text, oldest_header, boot_up_message, bot_admin_id, \
    bot_spam_id, bot_channel_id
from bot_utils import get_code
from database import Moderator, init
from math_parse import NumericStringParser
from math_utils import limit_int
from phases import LogAnalyzer
from stream_handlers import stream_text_log, stream_gzip_decompress

rpcs3Bot = Bot(command_prefix="!")
id_pattern = '(?P<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][A-Z])[ \\-]?(?P<numbers>\\d{5})'  # see http://www.psdevwiki.com/ps3/Productcode
nsp = NumericStringParser()

file_handlers = (
    # {
    #     'ext': '.zip'
    # },
    {
        'ext': '.log',
        'handler': stream_text_log
    },
    {
        'ext': '.gz',
        'handler': stream_gzip_decompress
    },
    # {
    #     'ext': '.7z',
    #     'handler': stream_7z_decompress
    # }
)


@rpcs3Bot.event
async def on_ready():
    print('Logged in as:')
    print(rpcs3Bot.user.name)
    print(rpcs3Bot.user.id)
    print('------')


@rpcs3Bot.event
async def on_message(message: Message):
    """
    OnMessage event listener
    :param message: message
    """
    # Self reply detect
    if message.author.id == rpcs3Bot.connection.user.id:
        return
    # Command detect
    try:
        if message.content[0] == "!":
            return await rpcs3Bot.process_commands(message)
    except IndexError as ie:
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
                await rpcs3Bot.send_message(message.channel, '```{}```'.format(info))
            else:
                await rpcs3Bot.send_message(message.channel, '```Serial not found in compatibility database, possibly '
                                                             'untested!```')
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
                                await piracy_alert(message, log.get_trigger())
                                break
                            elif error_code == LogAnalyzer.ERROR_OVERFLOW:
                                print("Possible Buffer Overflow Attack Detected!")
                                break
                            elif error_code == LogAnalyzer.ERROR_STOP:
                                await rpcs3Bot.send_message(
                                    message.channel,
                                    log.get_report()
                                )
                                sent_log = True
                                break
                            elif error_code == LogAnalyzer.ERROR_FAIL:
                                break
                        if not sent_log:
                            print("Log analyzer didn't finish, probably a truncated/invalid log!")
                            await rpcs3Bot.send_message(
                                message.channel,
                                log.get_report()
                            )
                    print("Stopping stream!")
        del log


async def piracy_alert(message: Message, trigger: str):
    print(message.author.id)
    await rpcs3Bot.send_message(
        message.channel,
        "Pirated release detected {author}!\n"
        "Please note that the RPCS3 community and it's developers do not support piracy!\n"
        "Most of the issues caused by pirated dumps is because they have been tampered with in such a way "
        "and therefore act unpredictably on RPCS3.\n"
        "If you need help obtaining legal dumps please read <https://rpcs3.net/quickstart>\n"
        "The trigger phrase was `{trigger}`, if you believe this was detected wrongly please contact a mod "
        "or {bot_admin}".format(
            author=message.author.mention,
            trigger=mask(trigger),
            bot_admin=message.server.get_member(bot_admin_id).mention
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


@rpcs3Bot.command()
async def math(*args):
    """Math, here you go Juhn"""
    return await rpcs3Bot.say(nsp.eval(''.join(map(str, args))))


# noinspection PyShadowingBuiltins
@rpcs3Bot.command()
async def credits(*args):
    """Author Credit"""
    return await rpcs3Bot.say("```\nMade by Roberto Anic Banic aka nicba1010!\n```")


# noinspection PyMissingTypeHints
@rpcs3Bot.command(pass_context=True)
async def c(ctx, *args):
    """Searches the compatibility database, USE: !c searchterm """
    await compat_search(ctx, *args)


# noinspection PyMissingTypeHints
@rpcs3Bot.command(pass_context=True)
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
@rpcs3Bot.command(pass_context=True)
async def top(ctx, *args):
    """
    Gets the x (default 10) top oldest/newest updated games
    Example usage:
        !top old 10
        !top new 10 jap
        !top old 10 all
        !top new 10 jap playable
        !top new 10 jap playable bluray
        !top new 10 jap loadable psn
    To see all filters do !filters
    """
    request = ApiRequest(ctx.message.author)
    if len(args) == 0 or args[0] not in ("new", "old"):
        print("Invalid command")
        return await rpcs3Bot.send_message(discord.Object(id=bot_spam_id), invalid_command_text)

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


@rpcs3Bot.command(pass_context=True)
async def filters(ctx, *args):
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
    await rpcs3Bot.send_message(ctx.message.author, message)


async def dispatch_message(message: str):
    """
    Dispatches messages one by one divided by the separator defined in api.config
    :param message: message to dispatch
    """
    for part in message.split(newline_separator):
        await rpcs3Bot.send_message(discord.Object(id=bot_channel_id), part)


@rpcs3Bot.command(pass_context=True)
async def latest(ctx, *args):
    """Get the latest RPCS3 build link"""
    latest_build = json.loads(requests.get("https://update.rpcs3.net/?c=somecommit").content)['latest_build']
    return await rpcs3Bot.send_message(
        ctx.message.author,
        "PR: {pr}\nWindows:\n\tTime: {win_time}\n\t{windows_url}\nLinux:\n\tTime: {linux_time}\n\t{linux_url}".format(
            pr=latest_build['pr'],
            win_time=latest_build['windows']['datetime'],
            windows_url=latest_build['windows']['download'],
            linux_time=latest_build['windows']['datetime'],
            linux_url=latest_build['linux']['download']
        )
    )


async def greet():
    """
    Greets on boot!
    """
    await rpcs3Bot.wait_until_ready()
    return await rpcs3Bot.send_message(discord.Object(id=bot_spam_id), boot_up_message)


# User requests
# noinspection PyMissingTypeHints,PyMissingOrEmptyDocstring
@rpcs3Bot.command(pass_context=True)
async def roll(ctx, *args):
    """Generates a random number between 0 and n (default 10)"""
    n = 10
    if len(args) >= 1:
        try:
            n = int(args[0])
        except ValueError:
            pass
    await rpcs3Bot.send_message(discord.Object(id=bot_spam_id), "You rolled a {}!".format(randint(0, n)))


# noinspection PyMissingTypeHints,PyMissingOrEmptyDocstring
@rpcs3Bot.command(pass_context=True, name="8ball")
async def eight_ball(ctx, *args):
    """Generates a random answer to your question"""
    await rpcs3Bot.send_message(discord.Object(id=bot_spam_id), choice([
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
        await rpcs3Bot.send_message(
            message.channel,
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
        await rpcs3Bot.send_message(
            message.channel,
            "{mention} is not a mod, this incident will be reported!".format(mention=author.mention)
        )
        return False


async def is_private_channel(ctx: Context):
    message: Message = ctx.message
    author: Member = message.author
    channel: Channel = message.channel
    if channel.is_private:
        return True
    else:
        await rpcs3Bot.send_message(
            channel,
            '{mention} https://i.imgflip.com/24qx11.jpg'.format(
                mention=author.mention
            )
        )
        return False


@rpcs3Bot.group(pass_context=True)
async def sudo(ctx: Context):
    if not await is_sudo(ctx):
        ctx.invoked_subcommand = None
    if ctx.invoked_subcommand is None:
        await rpcs3Bot.say('Invalid !sudo command passed...')


@sudo.command(pass_context=True)
async def say(ctx: Context, *args):
    message: Message = ctx.message
    origin_channel: Channel = message.channel
    author: Member = message.author
    server: Server = message.server
    channel: Channel = server.get_channel(args[0][2:-1]) \
        if args[0][:2] == '<#' and args[0][-1] == '>' \
        else origin_channel
    await rpcs3Bot.send_message(
        channel,
        ' '.join(args if channel.id == origin_channel.id else args[1:])
    )


@sudo.group(pass_context=True)
async def mod(ctx: Context):
    if ctx.invoked_subcommand is None:
        await rpcs3Bot.say('Invalid !sudo mod command passed...')


@mod.command(pass_context=True)
async def add(ctx: Context, user: Member):
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is None:
        Moderator(discord_id=user.id).save()
        await rpcs3Bot.say(
            "{mention} successfully added as moderator, you now have access to editing the piracy trigger list "
            "and other useful things! I will send you the available commands to your message box!".format(
                mention=user.mention
            )
        )
    else:
        await rpcs3Bot.say(
            "{mention} is already a moderator!".format(
                mention=user.mention
            )
        )


@mod.command(pass_context=True, name="del")
async def delete(ctx: Context, user: Member):
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is not None:
        if moderator.discord_id != bot_admin_id:
            if moderator.delete_instance():
                await rpcs3Bot.say(
                    "{mention} removed as moderator!".format(
                        mention=user.mention
                    )
                )
            else:
                await  rpcs3Bot.say(
                    "Something went wrong!".format(
                        mention=user.mention
                    )
                )
        else:
            await  rpcs3Bot.say(
                "{author_mention} why would you even try this! Alerting {mention}!".format(
                    author_mention=ctx.message.author_mention.mention,
                    mention=ctx.message.server.get_member(bot_admin_id).mention
                )
            )
    else:
        await rpcs3Bot.say(
            "{mention} not found in moderators table!".format(
                mention=user.mention
            )
        )


@mod.command(pass_context=True)
async def sudo(ctx: Context, user: Member):
    message: Message = ctx.message
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is not None:
        if moderator.sudoer is False:
            moderator.sudoer = True
            moderator.save()
            await rpcs3Bot.say(
                "{mention} successfully granted sudo permissions!".format(
                    mention=user.mention
                )
            )
        else:
            await rpcs3Bot.say(
                "{mention} already has sudo permissions!".format(
                    mention=user.mention
                )
            )
    else:
        await rpcs3Bot.say(
            "{mention} does not exist in moderator list, please add as moderator with mod_add!".format(
                mention=user.mention
            )
        )


@mod.command(pass_context=True)
async def unsudo(ctx: Context, user: Member):
    message: Message = ctx.message
    author: Member = message.author
    server: Server = message.server
    moderator: Moderator = Moderator.get_or_none(Moderator.discord_id == user.id)
    if moderator is not None:
        if moderator.discord_id != bot_admin_id:
            if moderator.sudoer is True:
                moderator.sudoer = False
                moderator.save()
                await rpcs3Bot.say(
                    "Successfully took away sudo permissions from {mention}".format(
                        mention=user.mention
                    )
                )
            else:
                await rpcs3Bot.say(
                    "{mention} already doesn't have sudo permissions!".format(
                        mention=user.mention
                    )
                )
        else:
            await  rpcs3Bot.say(
                "{author_mention} why would you even try this! Alerting {mention}!".format(
                    author_mention=author.mention,
                    mention=server.get_member(bot_admin_id).mention
                )
            )
    else:
        await rpcs3Bot.say(
            "{mention} does not exist in moderator list!".format(
                mention=user.mention
            )
        )


@rpcs3Bot.group(pass_context=True)
async def piracy_filter(ctx: Context, *args):
    if await is_mod(ctx) and await is_private_channel(ctx):
        if ctx.invoked_subcommand is None:
            await rpcs3Bot.say('Invalid piracy_filter command passed...')


print(sys.argv[1])
init()
rpcs3Bot.run(sys.argv[1])
