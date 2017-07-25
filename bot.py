import discord
import asyncio
import urllib.parse
import requests
import threading
import re
from bs4 import BeautifulSoup
from discord.ext.commands import Bot

### Made by Roberto Anic Banic
### 03-15-2017
### Glad to help

channelid = "291679908067803136"
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
	#print(message.author.name)
	codelist = []
	for matcher in re.finditer(pattern, message.content):
		code = matcher.group(0)
		if code not in codelist:
			codelist.append(code)
			print(code)
	for code in codelist:
		info = await getCode(code)
		if not info == "None":
			await rpcs3Bot.send_message(message.channel, info)
		
@rpcs3Bot.command()
async def credits(*args):
	"""Author Credit"""
	return await rpcs3Bot.say("```\nMade by Roberto Anic Banic aka nicba1010!\n```")
	
@rpcs3Bot.command(pass_context=True)
async def c(ctx, *args):
	"""Searches the compatibility database, USE: !c searchterm """
	await compatSearch(ctx, *args)
	
@rpcs3Bot.command(pass_context=True)
async def compat(ctx, *args):
	"""Searches the compatibility database, USE: !compat searchterm"""
	await compatSearch(ctx, *args)

async def getCode(code):
	url = "https://rpcs3.net/compatibility?g={}&r=1".format(code)
	soup = BeautifulSoup(requests.get(url).content, "lxml")
	compatConContainer = soup.find("table", {"class": "compat-con-container"})
	trs = compatConContainer.findAll("tr")[1:]
	if soup.find("p", {"class": "compat-tx1-criteria"}) != None:
		return "None"
	if len(trs) == 1:
		tr = trs[0]
		tds = tr.findAll("td")
		gameid = tds[0].findAll("a")[1].text
		format = "PSN" if tds[1].find("img")["src"].split("/")[-1].split(".")[0] == "psn" else "Retail"
		title = tds[1].find_all("a")[1].text
		if len(title) > 40:
			title = "{}...".format(title[:37])
		bused = tds[3].find_all("a")[-1].text
		status = tds[2].find("div").text
		lupdated = tds[3].find("a").text
		result = "```\nID:{:9s} Format:{:6s} Title:{:40s} Build:{:8s} Status:{:8s} Updated:{:10s}\n```".format(gameid, format, title, bused, status, lupdated)
		return result
	return "None"

async def compatSearch(ctx, *args):
	escapedSearch = ""
	unescapedSearch = ""
	for arg in args:
		escapedSearch += "+{}".format(urllib.parse.quote(arg))
	for arg in args:
		unescapedSearch += " {}".format(arg)
	escapedSearch = escapedSearch[1:]
	unescapedSearch = unescapedSearch[1:]
	if len(unescapedSearch) < 3:
		return await rpcs3Bot.send_message(discord.Object(id=channelid), "{} please use 3 or more characters!".format(ctx.message.author.mention))
	url = "https://rpcs3.net/compatibility?g={}&r=1".format(escapedSearch)
	soup = BeautifulSoup(requests.get(url).content, "lxml")
	
	#totalInDb = int(soup.find("div", {"id": "header-tx2-body-b"}).find("p").text.split("currently")[1].split("games")[0].strip())
	#totalHere = int(soup.find("a", {"title": "Show games from all statuses"}).text.split("(")[1][:-1])
	
	#if totalHere == totalInDb:
	#	return await rpcs3Bot.send_message(discord.Object(id=channelid), "{} invalid search: {}".format(ctx.message.author.mention, unescapedSearch))
	if soup.find("p", {"class": "compat-tx1-criteria"}) != None:
		await rpcs3Bot.send_message(discord.Object(id=channelid), "{} no result found, displaying alternatives for {}!".format(ctx.message.author.mention, unescapedSearch))	

	compatConContainer = soup.find("table", {"class": "compat-con-container"})
	await rpcs3Bot.send_message(discord.Object(id=channelid), "{} searched for: {}".format(ctx.message.author.mention, unescapedSearch))
	results = "```"
	trs = compatConContainer.findAll("tr")[1:]
	if len(trs) == 0:
		return await rpcs3Bot.send_message(discord.Object(id=channelid), "No results found")
	for tr in trs:
		tds = tr.findAll("td")
		gameid = tds[0].findAll("a")[1].text
		format = "PSN" if tds[1].find("img")["src"].split("/")[-1].split(".")[0] == "psn" else "Retail"
		title = tds[1].find_all("a")[1].text
		if len(title) > 40:
			title = "{}...".format(title[:37])
		#print(tds[3])
		bused = tds[3].find_all("a")[-1].text
		status = tds[2].find("div").text
		lupdated = tds[3].find("a").text
		results += "\nID:{:9s} Format:{:6s} Title:{:40s} Build:{:8s} Status:{:8s} Updated:{:10s}".format(gameid, format, title, bused, status, lupdated)
		if (len(results) + 124) > 2000 and not (trs.index(tr) == (len(trs) - 1)):
			await rpcs3Bot.send_message(discord.Object(id=channelid), results + "\n```")
			results = "```"
	results += "\n```"
	await rpcs3Bot.send_message(discord.Object(id=channelid), results)
	return await rpcs3Bot.send_message(discord.Object(id=channelid), "Retrieved from: {}".format(url))
	
rpcs3Bot.run("MjkxNTk4Njk1NjA1MjA3MDQx.C6r8aw.8PfoVoC5Je_RJpU_kLpgTfIVYaM")

#https://discordapp.com/oauth2/authorize?client_id=291598695605207041&scope=bot&permissions=0
