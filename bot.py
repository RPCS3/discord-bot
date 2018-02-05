import json
import re
import sys
from random import randint, choice

import discord
import requests
from discord import Message
from discord.ext.commands import Bot

from api import newline_separator, directions, regions, statuses, release_types
from api.request import ApiRequest
from bot_config import latest_limit, newest_header, invalid_command_text, oldest_header, boot_up_message
from math_parse import NumericStringParser
from utils import limit_int

channel_id = "291679908067803136"
bot_spam_id = "319224795785068545"
rpcs3Bot = Bot(command_prefix="!")
pattern = '[A-z]{4}\\d{5}'
nsp = NumericStringParser()


@rpcs3Bot.event
async def on_message(message: Message):
    """
    OnMessage event listener
    :param message: message
    """
    if message.author.name == "RPCS3 Bot":
        return
    try:
        if message.content[0] == "!":
            return await rpcs3Bot.process_commands(message)
    except IndexError as ie:
        print(message.content)
        return
    codelist = []
    for matcher in re.finditer(pattern, message.content):
        code = str(matcher.group(0)).upper()
        if code not in codelist:
            codelist.append(code)
            print(code)
    for code in codelist:
        info = await get_code(code)
        if not info == "None":
            await rpcs3Bot.send_message(message.channel, info)


@rpcs3Bot.command()
async def math(*args):
    """Math, here you go Juhn"""
    return await rpcs3Bot.say(nsp.eval(''.join(map(str, args))))


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
    if args[0] not in ("new", "old"):
        rpcs3Bot.send_message(discord.Object(id=bot_spam_id), invalid_command_text)

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
    message += "Korea\n```" + str(regions["h"]) + "```\n"
    message += "Hong-Kong\n```" + str(regions["k"]) + "```\n"
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
        await rpcs3Bot.send_message(discord.Object(id=channel_id), part)


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


async def get_code(code: str) -> object:
    """
    Gets the game data for a certain game code or returns None
    :param code: code to get data for
    :return: data or None
    """
    result = ApiRequest().set_search(code).set_amount(10).request()
    if len(result.results) == 1:
        for result in result.results:
            if result.game_id == code:
                return "```" + result.to_string() + "```"
    return None


async def greet():
    """
    Greets on boot!
    """
    await rpcs3Bot.wait_until_ready()
    await rpcs3Bot.send_message(discord.Object(id=bot_spam_id), boot_up_message)


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


print(sys.argv[1])
rpcs3Bot.run(sys.argv[1])
