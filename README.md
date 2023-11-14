RPCS3 Compatibility Bot
=======================

[![Build Status](https://github.com/RPCS3/discord-bot/actions/workflows/dotnet.yml/badge.svg)](https://github.com/RPCS3/discord-bot/actions/workflows/dotnet.yml) [![RPCS3 discord server](https://discordapp.com/api/guilds/272035812277878785/widget.png)](https://discord.me/rpcs3)

This is a tech support / moderation / crowd entertainment bot for the [RPCS3 discord server](https://discord.me/rpcs3).

You can read the design and implementation notes by visiting the folders in the web interface, or from the [architecture overview notes](architecture.md).

Development Requirements
------------------------
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or newer
* Any text editor, but here are some recommends:
  * [Visual Studio](https://visualstudio.microsoft.com/) (Windows and Mac only, has free Community edition)
  * [Visual Studio Code](https://code.visualstudio.com/) (cross-platform, free)
  * [JetBrains Rider](https://www.jetbrains.com/rider/) (cross-platform)

Runtime Requirements
--------------------
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or newer to run from sources
  * bot needs `dotnet` command to be available (i.e. alias for the Snap package)
* Optionally Google API credentials to access Google Drive:
  * Create new project in the [Google Cloud Resource Manager](https://console.developers.google.com/cloud-resource-manager)
  * Select the project and enable [Google Drive API](https://console.developers.google.com/apis/library/drive.googleapis.com)
  * Open [API & Services Credentials](https://console.developers.google.com/apis/credentials)
  * Create new credentials:
    * **Service account** credentials
    * New service account
      * if you select an existing account, **new** credentials will be generated **in addition** to previous any ones
    * Role **Project > Viewer**
    * Key type **JSON**
    * **Create** will generate a configuration file
  * Save said configuration file as `credentials.json` in [user secrets](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-5.0#how-the-secret-manager-tool-works) folder
    * e.g on Linux this will be `~/.microsoft/usersecrets/c2e6548b-b215-4a18-a010-958ef294b310/credentials.json`

How to Build
------------
* Change configuration for test server in `CompatBot/Properties/launchSettings.json`
* Note that token could be set in the settings _or_ supplied as a launch argument (higher priority)
* If you've changed the database model, add a migration
    * `$ dotnet tool install --global dotnet-ef` (if you have never installed the tools before)
	* `$ cd CompatBot`
	* `$ dotnet ef migrations add -c [BotDb|ThumbnailDb] MigrationName`
	* `$ cd ..`
* `$ cd CompatBot`
* `$ dotnet run [token]`

How to Run in Production
------------------------

### Running from source
* Change configuration if needed (probably just token):
  * use `$ dotnet user-secrets set Token <your_token_here>`
  * for available configuration variables, see [Config.cs](CompatBot/Config.cs#L31)
* Put `bot.db` in `CompatBot/` if you have one
* `$ cd CompatBot`
* `$ dotnet run -c Release`

### Running with Docker
* Official image is hosted on [Docker Hub](https://hub.docker.com/r/rpcs3/discord-bot).
* You should pull images tagged with `release-latest` (same thing as `latest`)
* Please take a look at the [docker-compose.yml](docker-compose.example.yml) for required configuration (bot token and mounting points for persistent data).

External resources that need manual updates
-------------------------------------------
* [Unicode Confusables](http://www.unicode.org/Public/security/latest/confusables.txt), for Homoglyph checks
* [Windows Error Codes](https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/), for error decoding on non-Windows host
* Optionally pool of names (one name per line), files named as `names_<category>.txt`
