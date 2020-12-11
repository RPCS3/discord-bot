Commands
========

The potatoes and gravy of the bot. All of them _should_ be inherited from the `BaseCommandModuleCustom` class for uniform command management and permission model.

`BotMath`
---------
This command is intended to parse and evaluate various math expressions.
The implementation is based on [mXparser](https://mathparser.org/).

`BotStats`
----------
This command is intended to get general bot status information with some fun statistics thrown in.

`CommandsManagement`
--------------------
This command group is intended to manage other command policies. The purpose of which is to be able disable and enable specific commands or groups of commands at runtime to prevent bot abuse and exploitation by regular users until the issue could be properly fixed.

`CompatList`
------------
Command group dedicated for [game compatibility API](../../Clients/CompatApiClient).
Game listing is heavily relied on [fuzzy string matching](../../CompatBot/Utils/Extensions#StringUtils) to sort by similarity score to the original request string.

One fun feature is to prevent bot abuse by one specific user, that's just a home grown meme.

`ContentFilters` aka `Antipiracy`
---------------------------------
One of the more useful moderation command groups that allows content filtering in Discord text channels, as well as for screening uploaded user log files for support purposes.

For the most part it is straightforward, except for the filter editor logic in `EditFilterPropertiesAsync()`.
It is structured as a simple state machine with labels denoting the nodes.

The gist of it is to have a wizard-like experience with multiple stages to edit the object model in a manner that would help moderators to correctly fill out all the necessary data. ~~It's also a fun abuse of Discord embeds.~~

`EventsBaseCommand`, `Events`, `E3`, `Cyberpunk2077`
----------------------------------------------------
`EventsBaseCommand` implements simple calendar/scheduling system to track various events. It is mostly intended to show various gaming-related events and link the associated livestreams.
It also implements the event object model editor in a similar fashion as `ContentFilters`.

`Events` command group then implements general event management commands, and `E3` and `Cyberpunk2077` are simple aliases for easier use.

`Explain`
---------
One of the more useful command groups, mainly used for tech support.
It is providing the means to define and show short explanation, a sort of FAQ or a wiki.

Each explanation has a `term` that defines the entry, and is how you can recall the explanation body. When searching for an explanation, if no direct match to term is found, some [fuzzy text match logic](../../CompatBot/Utils/Extensions#StringUtils) is applied.

There's also an ability to attach a file to each explain. Media files are automatically recognized by the clients, and shown inline. This includes spoiler marks (which is implemented by simply prepending `SPOILER_` to the filename).

`Invites`
---------
This command group is dedicated to management of Discord invite whitelist. By default any invite link will be removed, but moderators can add target servers to the whitelist to allow invite sharing by regular users.

`Ird`
-----
Simple command to look up available items from the [IRD Library](../../Clients/IrdLibraryClient).

`Misc`
------
A fun pile of random commands, including:
* `!about` to show people contributed to the bot in some way
* `!roll` to generate random number from 1 to specified value, or to cast a bunch of [dice](https://en.wikipedia.org/wiki/Dice#Polyhedral_dice)
* `!8ball` to randomly pick one of the predefined answers for a yes/no question
* `!rate` to randomly pick one of the predefined quality answers, using data-driven seed to produce consistent reply for the same question
* `!download` is a meme/bait command that is aliased to `!psn search`

`Moderation`
------------
Command group for various moderation-related commands, including:
* `!report` to link any message to the moderation queue
* `!analyze` to force log analysis in case it is needed again
* `!badupdate` to mark update announcement as not good for general consumption

Audit commands are used to check various suspicious stuff like users trying to impersonate staff, or users abusing Unicode combining characters to produce [zalgo](https://www.urbandictionary.com/define.php?term=Zalgo).

`Pr`
----
This command is used to query GitHub for any open pull request that meets the specified text filter.
First word in the query is additionally treated as a PR author.
In case only one items is returned, it is show as an embed with the link to the latest available artifact download from AppVeyor.

`Psn`
-----
Various [PSN-related](../../Clients/PsnClient) commands.
Most commonly used is the `!psn check updates` that is using PSN game update API to show links for the game updates.

Same kind of anti-abuse measures is implemented here, as for the [`CompatList`](#CompatList).

`Sudo`
------
Commands for the bot management ~~and abuse by mods~~.
`say` and `react` will make the bot to post a specified message or reaction, `log` will attach current log file for easier access.

`mod` subgroup is for managing bot roles, which are currently separated from Discord roles.

`fix` subgroup is a technical group to help fix various data in bot state.

`bot` subgroup is for managing the bot instance itself: `stop`, `restart`, `update` are self-explanatory; `status` can set Discord status that will show up on bot profile.

`Syscall`
---------
Command to query information about what games used what functions from the PS3 kernel and firmware modules.

`Warnings`
----------
Warning system management commands. You can look at various warning lists, as well ass issue and retract warnings for specific users.
