RPCS3 Compatibility Bot
=======================

Configuration
-------------

Currently all configurable tunables are stored in the [Config](Config.cs) static class that is initialized once on startup. This is mostly done like this because it's very easy to implement and is enough for current needs.

Some settings are grouped in additional static classes for easier use (e.g. `Reactions` and `Colors`).

Everything is initialized with default values that correspond to the main bot instance (except for sensitive tokens), and require overriding through configuration for any test instance.

Currently, configuration is possible through the `$ dotnet user-secrets` command. Configuration through the environment variables was disabled as it had some unintended consequences between bot restarts (preserved values; require complete manual shutdown and restart to update configuration).

> Be careful with input validation during configuration, as unhandled exceptions in static constructor will lead to `TypeInitializationException` and program termination that might be tricky to debug.

In addition to the configuration variables, `Config` contains the `Log` instance and the global `CancellationTokenSource`.

We're using [NLog](https://nlog-project.org/) for logging, configured to mimic the default Log4Net layout (mostly because I already have the [syntax highlighter](https://github.com/13xforever/kontur-logs) for Sublime Text 3 for it). It is also configured to ignore `TaskCancelledException`s that occur when `CancellationToken`s are being cancelled to reduce spam in logs.

> Do log exceptions as an argument to log methods, instead of calling `.ToString()` on them

Global `CancellationTokenSource` (`Config.Cts`) is used to signal the program shutdown.

> Do check it and abort whenever possible to reduce the restart time and prevent infinite code execution.



Program entry point
-------------------

On startup we check for other instances. We wait a bit for their shutdown, or shutdown ourselves otherwise. This is done through spinning a separate thread and using global [mutex](https://docs.microsoft.com/en-us/dotnet/api/system.threading.mutex?view=netcore-2.1). Unlike semaphores, mutex **must** be released in the same thread it was acquired, which is impossible to do in asynchronous code, thus the dedicated thread.

> Note that `Config` initialization will happen on first call to the class.

Next we open the databases and run [migrations](https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/) to upgrade their structure if needed. Currently we have two of them:
* BotDb is used to store all the settings and custom data for the bot.
* ThumbsDb is used to store PSN metadata and game thumbnail links.

When databases are ready, we immediately restore bot [runtime statistics](Databese/Providers/).

Next we start all the background tasks that will run periodically while the bot is up and running. This includes [thumbnail scraping](ThumbScraper/), AppVeyor build history scraper, AMD Driver version updater, etc.

Next we configure the discord client. This includes registering all the [commands](Commands/) and [event handlers](EventHandlers/). We use the built-in help formatter and hook up the built-in discord client logging to our own logger.

Of particular note is the `GuildAvailable` event, where we check and make sure the bot is running in the configured guild. There was one time when bot wasn't configured properly and someone quietly added it to their own private server, which caused crash on startup. That was fun.

We also fun backlog checks where it makes sense for moderation, in case something slipped past while the bot was unavailable.

On restart we try to gracefully wait for a bit to let any outstanding task to complete, but not too long. This is why it is important to check `Config.Cts` cancellation status whenever possible.