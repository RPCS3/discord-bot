import json
import re
import sys
import urllib.parse

import discord
import requests
from bs4 import BeautifulSoup
from discord.ext.commands import Bot

from api import ApiRequest, newline_separator

channel_id = "291679908067803136"
rpcs3Bot = Bot(command_prefix="!")
pattern = '[A-z]{4}\\d{5}'


@rpcs3Bot.event
async def on_message(message):
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
		code = matcher.group(0)
		if code not in codelist:
			codelist.append(code)
			print(code)
	for code in codelist:
		info = await get_code(code)
		if not info == "None":
			await rpcs3Bot.send_message(message.channel, info)


@rpcs3Bot.command()
async def credits(*args):
	"""Author Credit"""
	return await rpcs3Bot.say("```\nMade by Roberto Anic Banic aka nicba1010!\n```")


@rpcs3Bot.command(pass_context=True)
async def c(ctx, *args):
	"""Searches the compatibility database, USE: !c searchterm """
	await compat_search(ctx, *args)


@rpcs3Bot.command(pass_context=True)
async def compat(ctx, *args):
	"""Searches the compatibility database, USE: !compat searchterm"""
	await compatSearch(ctx, *args)


async def compat_search(ctx, *args):
	search_string = ""
	for arg in args:
		search_string += arg

	request = ApiRequest()
	request.set_search(search_string)
	response = request.request()
	await dispatch_messages(response.to_string().format(
		requestor=ctx.message.author.mention,
		search_term=search_string,
		request_url=request.build_query()
	))


async def dispatch_messages(message: str):
	for part in message.split(newline_separator):
		await rpcs3Bot.send_message(discord.Object(id=channel_id), message)


@rpcs3Bot.command(pass_context=True)
async def latest(ctx, *args):
	"""Get the latest RPCS3 build link"""
	appveyor_url = BeautifulSoup(requests.get("https://rpcs3.net/download").content, "lxml").find("div", {
		"class": "div-download-left"}).parent['href']
	return await rpcs3Bot.send_message(ctx.message.author, appveyor_url)


@rpcs3Bot.command(pass_context=True)
async def newest(ctx, *args):
	"""Gets the 10 newest updated games"""
	limit = 10
	if len(args) == 1:
		limit = int(args[0]) if 0 < int(args[0]) < 11 else 10
	print(ctx.message.channel)
	if ctx.message.channel.id != "319224795785068545":
		return
	url = "https://rpcs3.net/compatibility?o=4d&r=1&api=v1"
	return await search(url, ctx, *args, limit=limit,
	                    search_title="{}: Top {} newest tested games!".format(ctx.message.author.mention, limit))


@rpcs3Bot.command(pass_context=True)
async def oldest(ctx, *args):
	"""Gets the 10 oldest updated games"""
	limit = 10
	if len(args) == 1:
		limit = int(args[0]) if 0 < int(args[0]) < 11 else 10
	print(ctx.message.channel)
	if ctx.message.channel.id != "319224795785068545":
		return
	url = "https://rpcs3.net/compatibility?o=4a&r=1&api=v1"
	return await search(url, ctx, *args, limit=limit,
	                    search_title="{}: Top {} oldest tested games!".format(ctx.message.author.mention, limit))


async def compatSearch(ctx, *args):
	print(ctx.message.channel)
	if ctx.message.channel.id != "319224795785068545":
		return
	escapedSearch = ""
	unescapedSearch = ""
	for arg in args:
		escapedSearch += "+{}".format(urllib.parse.quote(arg))
	for arg in args:
		unescapedSearch += " {}".format(arg)
	escapedSearch = escapedSearch[1:]
	unescapedSearch = unescapedSearch[1:]
	if len(unescapedSearch) < 3:
		return await rpcs3Bot.send_message(discord.Object(id=channel_id),
		                                   "{} please use 3 or more characters!".format(ctx.message.author.mention))
	url = "https://rpcs3.net/compatibility?g={}&r=1&api=v1".format(escapedSearch)
	return await search(url, ctx, *args, query=unescapedSearch)


async def get_code(code):
	url = "https://rpcs3.net/compatibility?g={}&r=1&api=v1".format(code)
	jsonn = requests.get(url).text
	data = json.loads(jsonn)
	if data["return_code"] == -2:
		return "None"
	# return await rpcs3Bot.send_message(discord.Object(id=channelid), "Please be patient API is in maintenance mode!")
	if data["return_code"] == 0:
		result_arr = data["results"]
		for id, info in result_arr.items():
			title = info["title"]
			if len(title) > 40:
				title = "{}...".format(title[:37])
			result = "```\nID:{:9s} Title:{:40s} PR:{:4s} Status:{:8s} Updated:{:10s}\n```".format(
				id, title,
				"????" if str(info["pr"]) == "0" else str(info["pr"]),
				info["status"],
				info["date"])
			return result
	return "None"


async def search(url, ctx, *args, limit=-1, search_title=None, query=None):
	jsonn = requests.get(url).text
	data = json.loads(jsonn)
	if data["return_code"] == -3:
		print(url)
		return await rpcs3Bot.send_message(discord.Object(id=channel_id),
		                                   "{}, Illegal search".format(ctx.message.author.mention))
	if data["return_code"] == -2:
		return await rpcs3Bot.send_message(discord.Object(id=channel_id),
		                                   "Please be patient API is in maintenance mode!")
	if data["return_code"] == -1:
		return await rpcs3Bot.send_message(discord.Object(id=channel_id), "API Internal Error")
	# if data["return_code"] == 2:
	#	await rpcs3Bot.send_message(discord.Object(id=channelid), ", no result found, displaying alternatives for {}!".format(ctx.message.author.mention, unescapedSearch))
	if data["return_code"] == 1:
		return await rpcs3Bot.send_message(discord.Object(id=channel_id),
		                                   "{} searched for {} no result found!".format(ctx.message.author.mention,
		                                                                                search))
	if search_title is None:
		await rpcs3Bot.send_message(discord.Object(id=channel_id),
		                            "{} searched for: {}{}".format(ctx.message.author.mention, query, " " if data[
			                                                                                                     "return_code"] == 0 else "\n\tNo results found! Displaying alternatives!"))
	else:
		await rpcs3Bot.send_message(discord.Object(id=channel_id), search_title)
	results = "```"
	result_arr = data["results"]

	count = 0
	for id, info in result_arr.items():
		print(id)
		count += 1
		title = info["title"]
		if len(title) > 40:
			title = "{}...".format(title[:37])
		results += "\nID:{:9s} Title:{:40s} PR:{:4s} Status:{:8s} Updated:{:10s}".format(id, title, "????" if str(
			info["pr"]) == "0" else str(info["pr"]), info["status"], info["date"])
		if count == limit:
			break
	results += "\n```"
	await rpcs3Bot.send_message(discord.Object(id=channel_id), results)
	return await rpcs3Bot.send_message(discord.Object(id=channel_id),
	                                   "Retrieved from: {}".format(url.replace("&api=v1", "")))


print(sys.argv[1])
rpcs3Bot.run(sys.argv[1])
