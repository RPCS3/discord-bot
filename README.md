# discord-bot

Dependencies:
* python3.6 or newer
* pip for python3
* `$ python3 -m pip install -U discord.py`
* pyparsing for python3 (distro package or through pip)
* requests for python3 (distro package or through pip)
* peewee for python3 (distro package or through pip)


Optional stuff for private testing:
* [create an app](https://discordapp.com/developers/applications/me)
* add a user bot to this new app (look at the bottom of the app page)
  * notice the Bot User Token
* [add your new bot to your private server](https://discordapp.com/oauth2/authorize?client_id=BOTCLIENTID&scope=bot)
* change IDs in `bot_config.py` for your channels and users

How to run:
* `$ python3 bot.py bot_user_token`