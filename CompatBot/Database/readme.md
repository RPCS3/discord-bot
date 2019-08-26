Databases
=========
Currently we use [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/) as an [O/RM](https://en.wikipedia.org/wiki/Object-relational_mapping) and SQLite as a storage.
Among easier code generation, ef core allows for automated [database schema migrations](https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/) that are persisted in the `Migrations` folder.

There are some [data provider classes](Providers) for easier access and management, but most of the time you'll see direct access an manipulation of the data throughout the code base.

After changing the classes you need to generate a new migration using dotnet ef tools
```
$ dotnet ef migrations add -c <DbClass> <MigrationName>
```
Note that ef tools come bundled with dotnet sdk 2.x, but require [manual installation](https://docs.microsoft.com/en-us/ef/core/miscellaneous/cli/dotnet) starting with dotnet sdk 3.0.

`BotDb`, `ThumbnailDb`
-----------------------
Two main database description classes used to store settings and PSN metadata respectively.

Note that due to legacy requirements, every table has a numeric primary key with auto increment value, even if it is never used.

`BotDb`
-------
Contains the following tables that hold bot runtime configuration:

### `bot-state`
A simple key-value table for saving any miscellaneous data that is needed.

### `moderator`
Stores bot roles (mods and sudoers).

### `piracystring`
Stores content moderation filter descriptions, including filter trigger string, trigger context, validating regex, custom message, and required action flags.

### `warning`
Stores user warning history, including user IDs, publicly visible comment on the reason, and privately visible context in case of automated warnings issued by the bot.

### `explanation`
Stores explanation descriptions and data: term as a key, text for an explanation, and optional file attachment as a binary blob.

### `disabled-commands`, `whitelisted-invites`
Simple lists of the associated data.

### `event-schedule`
Contains information about the events: date and time intervals, titles, etc.

### `stats`
This table is used for persisting stat caches in case of process restart.

`ThumbnailDb`
--------------
Contains mostly PSN-related metadata in the following tables:

### `state`
A key-value storage of the PSN crawling state, and some other named time stamps.

### `thumbnail`
Contains mapping for the [product code](http://www.psdevwiki.com/ps3/Productcode), [content ID](https://www.psdevwiki.com/ps3/Content_ID), source thumbnail URL, and re-uploaded cached thumbnail URL.

### `title-info`
??? Seems to duplicate `thumbnail` table, with added optional column for the embed color. TBH I don't remember how this happened. I need to remove this later.

### `syscall-info`
Contains information about what game used what function from what firmware or kernel module.

### `syscall-to-product-map`
This is a utility table that contains foreign keys that tie `thumbnail` and `syscall-info` tables together to allow required `JOIN`s for queries.

`DbImporter`
------------
This class is handling all the import logic from the legacy python version of the bot, and also applying all the available migrations on program start up.