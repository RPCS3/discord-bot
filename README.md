RPCS3 Compatibility Bot
=======================

Development Requirements
------------------------
* [.NET Core 2.1 SDK](https://www.microsoft.com/net/download/windows) or newer
* Any text editor, but Visual Studio 2017 or Visual Studio Code is recommended

Runtime Requirements
--------------------
* [.NET Core 2.1 Runtime](https://www.microsoft.com/net/download/windows) or newer for compiled version
* [.NET Core 2.1 SDK](https://www.microsoft.com/net/download/windows) or newer to run from sources

How to Build
------------
* Change configuration for test server in `CompatBot/Properties/launchSettings.json`
* Note that token could be set in the settings _or_ supplied as a launch argument (higher priority)
* If you've changed the database model, add a migration
	* `$ cd CompatBot`
	* `$ dotnet ef migrations add -c [BotDb|ThumbnailDb] MigrationName`
	* `$ cd ..`
* `$ cd CompatBot`
* `$ dotnet run [token]`

How to Run in Production
------------------------
* Change configuration if needed (probably just token)
* Put `bot.db` in `CompatBot/`
* `$ cd CompatBot`
* `$ dotnet run -c Release [token]`