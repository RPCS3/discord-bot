Event Handlers
==============

Some points to keep in mind:
* Handlers for the same event run one after another in the order they have been subscribed.
* Any handler can mark event as handled, which will cut the chain, so the subsequent handlers won't be invoked.
* Even though events are asynchronous, they still run on the same thread pool and the same task queue, which means they should return fast. Any prolonged task will stall the queue and will effectively cause denial of service.
  > If you require to run some heavy stuff on event, queue explicit background processing and return.
* _Every_ event triggers the handler, including the actions made by the bot itself. Remember to do proper checks for commands and bot's own actions to prevent loops / unintended spam messages / etc.

Antipiracy Monitor
------------------

Should be first in event queue. Checks every message for a possible piracy trigger, and removes it to prevent breaking Discord ToS / legal issues.

For specifics of the filter, see [PiracyStringProvider](../Database/Providers/) implementation.

AppVeyor Links
--------------

This handler checks messages for AppVeyor links and provides information about the associated PR build when available.

Bot Reactions
-------------

This is a fun silly feature to make bot react in some way to the user message. The idea behind it is to create random matching `reaction` or to send a matching random `message` when reactions are not permitted.

Triggers are a simple substring lookup, with no regards to word boundaries. Usually there's nothing wrong to react to seemingly random messages.

To reduce spam, some triggers have explicit checks that the message was addressed to bot. Also there's a mechanism to verbally mute the bot spam for a while that is also used for some other event handlers / commands. It is usually used when bot does something wrong or is being annoying in conversation.

Discord Invite Filter
---------------------

Monitors invite links to other servers, and checks them against the white list. As invite codes are random, we try to resolve the `guild` and match its id.

We also try to resolve discord.me links, as it's a popular 3rd party service, but it doesn't provide any API, and is often locked out by CloudFlare, so most of the time we simple block all their links.

For any link that is not white listed, we remove the message and verbally warn the user to ask mods first.

There is a fun feature where we detect an attempts of filter circumvention by posting the invite code alone, which is normally indistinguishable from normal text. It is done by caching the recent invite codes and doing explicit substring matches, which works ok in practice. Also giving a `warn` record for such an attempt, as it's very impolite to ignore friendly request to follow the rules.

GitHub Links
------------

Similar to AppVeyor links monitor, we're looking for possible GitHub issues or PR mentions, and link them for convenience.

As we currently do not authorize GitHub API client, to reduce its use, we simply construct the link and rely on default embed generator instead of doing something custom.

Every PR has a hidden issue associated with it, which automatically redirects to the appropriate PR page, so we always link to issues.

In practice, even with 60 requests per hour, we can do custom embeds, and only fallback to URL generation if we hit some kind of threshold on available calls.

Greeter
-------

A simple handler that sends a DM to every new `member`. For greater flexibility, the message is formed from the `motd` [explain](../Commands).

Is the Game Playable
--------------------

This is part fun, part hopeless attempt to do natural language processing with regular expressions.

The idea here is to answer the regular "Is the game X playable yet?" questions. Mainly new users who do not know about the bot, or the compatibility list.

The challenge here, of course, is that every user is different, and many do not speak proper English to begin with. We can't extract the intent without any form of AI training, so instead we have a ginormous regex to match most common forms of questions.

To reduce false positive hits, we only do game lookups in two channels (main and help), for users without any role that would imply basic bot knowledge, and we only show the result if the fuzzy matching score of result has a high confidence score.

Log as Text
-----------

This simple monitor looks for logs copy/pasted from UI, and prompts user to upload the full log file instead.

Log Parsing
-----------

The meat and potatoes of this bot. Looks up for potential RPCS3 logs, and queues a background [log analysis](LogParsing/) job.

Log analysis queue is limited by utilizing a [Semaphore](https://docs.microsoft.com/en-us/dotnet/standard/threading/semaphore-and-semaphoreslim) and making an `async void` function call. One must be very careful with it, but you get the desired effect on the cheap.

New Builds Monitor
------------------

This one has a background task implementation that is continuously checking for new RPCS3 builds through [Compatibility API](../Clients/CompatApiClient/).

The main idea here is to do the check once in a while, mostly for the time when something goes wrong and we can't detect the new build trigger (Yappy / Discord are down).

The event handler comes in handy to check for the new successful build announcements in [#github](https://discordapp.com/channels/272035812277878785/272363592077017098) channel. If we see such a message, there's a good chance we'll get a new update information _soon_, so we _speed up_ the new updates check from once per however minutes/hours to once per few seconds/minutes.

Once we detect a new build, or if nothing happened after a while, we reset the check interval to default.

Post Log Help
-------------

This is another fun, but less complex attempt at natural language processing. The idea is to send instructions on how to upload full RPCS3 log in the [#help](https://discordapp.com/channels/272035812277878785/277227681836302338) channel.

For greater flexibility, it tries to use the `log` [explain](../Commands/).

Product Code Lookup
-------------------

This handler monitors for the `product code` mentions, and posts game compatibility embeds for them.

We limit it to 5 unique codes in public channels, and to a greater number in DMs to avoid Discord API throttling and general possibility of bot spam.

We also do `shut up` checks to reduce spam (see [Bot Reactions](#bot-reactions) for more info).

Starbucks
---------

This handler is a [#media](https://discordapp.com/channels/272035812277878785/272875751773306881) moderation handler to allow users help with the no-chatting rule enforcement.

People can react with the ☕ emoji, and if a certain threshold is met, a notice is generated to the moderation queue.

To prevent abuse, only users with certain roles are counted for this.

Table Flip Monitor
------------------

This is a pure fun handler that does nothing useful. It looks for the table flip [kaomoji](http://japaneseemoticons.me/) and sends a message with the matching reversed one.

For more fun, it is using pattern matching instead of hard coded samples, so in theory, it can find and generate appropriate response for any variation.

Thumbnail Cache Monitor
-----------------------

This is a management handler that is clearing the re-uploaded [thumbnail](../Database/Providers/) url for embeds when someone deletes said image from the appropriate channel.

Unknown Command
---------------

This handler is used when the user issues an unknown command. This happens _a lot_. Most users _do not understand_ how to use the bot.

So we try to guess what the intention was. We handle two most used cases: [explain](../Commands) and [compat](../Commands/) lookups.

To reduce spam and false positives, we only redirect the calls to the appropriate commands if we have high confidence score for fuzzy matched results.

If everything else fails, we show the help message that explains how to properly use the bot. And to reduce the spam further, we DM the instructions most of the time, to keep public channels clean.

There's also a fun part where you can mention the bot and ask the question (denoted by the question mark at the end of the message), in whih case we redirect the query tothe [8ball](../Commands/) command.

Username Spoof Monitor
----------------------

This was created after one accident when some user tried to impersonate a developer and asked for money from users in DMs.

To prevent such events in the future, we monitor every `username` and `nickname` change, and also check every new `member`.

For better results, we also employ [homoglyph](../../HomoglyphConverter/) disambiguation.

Username Zalgo Monitor
----------------------

In the same vein, this monitor checks every _display_ user name for [zalgo](https://knowyourmeme.com/memes/zalgo) and other Unicode abuses where the text can creep up on adjacent lines above or below.

As there's no sure way to check if some symbol will be drawn above or below the base line, and how it reacts with stacking, we do a simple check:
1. [Normalize](https://docs.microsoft.com/en-us/dotnet/api/system.string.normalize?view=netcore-2.1) the string to get rid of regular diacritics.
2. Iterate through the symbols and check their Unicode category (in a special way, because UTF-16 can't handle higher planes without [surrogate pairs](https://en.wikipedia.org/wiki/UTF-16#U+010000_to_U+10FFFF)).
3. Ignore visually invisible symbols, count the number of combining characters.
4. If there are more than 2 visually consecutive combining characters, it's a good indication of Unicode abuse.
5. On top of that, we check with a list of known normal characters that are often rendered above or below the base line.
