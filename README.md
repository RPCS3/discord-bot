RPCS3 Compatibility Bot
=======================

This is a tech support / moderation / crowd entertainment bot for the [RPCS3 discord server](https://discord.me/rpcs3) [![RPCS3 discord server](https://discordapp.com/api/guilds/272035812277878785/widget.png)](https://discord.me/rpcs3)

Development Requirements
------------------------
* [.NET Core 2.1 SDK](https://www.microsoft.com/net/download/windows) or newer
* Any text editor, but here are some recommends:
  * [Visual Studio](https://visualstudio.microsoft.com/) (Windows and Mac only, has free Community edition)
  * [Visual Studio Code](https://code.visualstudio.com/) (cross-platform, free)
  * [JetBrains Rider](https://www.jetbrains.com/rider/) (cross-platform)

Runtime Requirements
--------------------
* [.NET Core 2.1 SDK](https://www.microsoft.com/net/download/windows) or newer to run from sources
  * needs `dotnet` command available (i.e. alias for the Snap package)
* [.NET Core 2.1 Runtime](https://www.microsoft.com/net/download/windows) or newer for compiled version
* Optionally Google API credentials to access Google Drive:
  * Create new project in the [Google Cloud Resource Manager](https://console.developers.google.com/cloud-resource-manager)
  * Select the project and enable [Google Drive API](https://console.developers.google.com/apis/library/drive.googleapis.com)
  * Open [API & Services Credendials](https://console.developers.google.com/apis/credentials)
  * Create new credentials:
    * **Service account** credentials
    * New service account
    * Role select **Project > Viewer**
    * Key type **JSON**
    * **Create** will generate a configuration file
  * Save said configuration file as `CompatBot/Properties/credentials.json`

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
  * use `$ dotnet user-secrets set Token <your_token_here>`
  * for more information 
* Put `bot.db` in `CompatBot/`
* `$ cd CompatBot`
* `$ dotnet run -c Release [token]`

External resources that need manual updates
-------------------------------------------
* [Unicode confusables](http://www.unicode.org/Public/security/latest/confusables.txt) gzipped, for Homoglyph checks
