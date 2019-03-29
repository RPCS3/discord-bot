Project file structure
======================

* [CompatBot](CompatBot/) contains the main bot logic, including all the commands and event handlers.
* [Clients](Clients/) contains implementation of various 3rd party service clients with their respective object models.
* [HomoglyphConverter](HomoglyphConverter/) is a library that implements Unicode text canonicalization and [homoglyph](https://en.wikipedia.org/wiki/Homoglyph) text comparison.
* [Tests](Tests/) contains miscellaneous tests and is useful to try out things in general.

High-level code structure overview
==================================

This version of the bot targets [dotnet core](https://docs.microsoft.com/en-us/dotnet/core/) 2.1+, using [DSharp+](https://dsharpplus.github.io/api/index.html) 4.0 discord client library. For settings and state persistance we use SQLite database engine accessed through [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/index).

Historically speaking, this is a version 2.0 of the bot. From [the beginning](https://github.com/RPCS3/discord-bot/tree/python) it was built using python and [discord-py](https://discordpy.readthedocs.io/en/rewrite/api.html), which left some legacy traces after complete rewrite to C#, particularly for database format compatibility and command invocation syntax.

On startup we check and run [database migrations](CompatBot/Database/Migrations/) to get to the expected table structure. Forward migration must always be lossless.

Next we register all the [commands](CompatBot/Commands/) and [event handlers](CompatBot/EventHandlers/), configure the client for specific discord server (test servers must override most settings).

Command dispatching and scheduling is done by the DSharp+ automatically. Event handlers run one by one and can terminate the call chain when necessary (e.g. if piracy was detected, we simply delete the message and abort every other check).

In case of network problems client will attempt to reconnect automatically, but after several failed retries it aborts, in which case our global error handler will restart the instance automatically.

General considerations
======================

* Please familiarize yourself with the [official Discord documentation](https://discordapp.com/developers/docs/reference). You'll see a lot of terms defined there (like guilds vs servers, users vs members, etc).

* Always keep in mind that users _will_ find and exploit everything they can, including, but not limited to: spamming through bot responses, abusing response wording with the user input, hit performance through specially crafted messages or data (speed, memory, task queue depth, etc), provoke denial of service in the same vein, etc.
  
  >  Never trust user input. Validate and sanitize everything. Always limit access to the management commands.

* This is not a big project, resources are limited, and shared with other services.

  > Use streaming processing whenever possible. Limit memory usage, don't keep caches in memory just because you can. If you write to the disk, try to remove the trash automatically when you're done. Limit queues for background tasks. Do run tasks asynchronously whenever possible.

* Bot gets special permissions on a case by case basis, don't assume it will have the required permissions at all times, in every context.

  > Check permissions when possible. Catch exceptions always, log them when it makes sense. Have contingency plans always. Ideally everything should be controllable at runtime, without updates.

* Functionality and helpfulness trump fun and memes.

  > This is a help / moderation bot first and foremost. Fun stuff is secondary, keep it out of the way.

* Please do go to the trouble of making or joining a test server, and check basic functionality before making a pull request and deploying to the main instance.

  > There's also an [Azure Pipelines](https://github.com/marketplace/azure-pipelines) config in this repo that you can set up on your fork to check for basic CI checks.

* Do use `dotnet user-secrets` to configure the bot and user-accessible [app data folders](https://docs.microsoft.com/en-us/dotnet/api/system.environment.specialfolder?view=netcore-2.1) do store any persistent data.

* Everything runs asynchronously. Please familiarize yourself with the [basics](https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/task-based-asynchronous-programming) and the [pitfalls](https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/potential-pitfalls-in-data-and-task-parallelism) of asynchronous programming.

  > Rule of thumb: use `.ConfigureAwait(false)` everywhere, don't use `async` function modifier for synchronous code (return `Task.FromResult()` or `Task.CompletedTask` instead). Avoid `async void` as a plague unless you know what you're doing, and always _always_ handle exceptions, if you must use it.