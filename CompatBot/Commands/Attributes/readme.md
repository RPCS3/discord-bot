Command Attributes
==================

This folder contains various custom attributes that could be used with command groups (classes) and commands themselves (functions).

They're intended for easier management of said commands, such as implementing access rights management and spam reduction policies.

`CheckBaseAttributeWithReactions`
---------------------------------

This is a base class that implements an ability to create Discord reactions in case the check has passed or failed, and also logs the check itself for audit purposes.

`LimitedToXyzChannel`, `RequiresDm`, `RequiresNotMedia`
-------------------------------------------------------

These attributes are intended to limit potential impact of command usage outside of specific channels. There are some allowances for users with "trusted" roles sometimes, but in general it's an easy way to make sure bot won't reply when it is not needed.

`RequiresXyzRole`
-----------------

Similarly implement command access rights management to prevent their use by regular users. As a bonus, every user will only get the list of commands that they can actually use when using `!help`.

`TriggersTyping`
---------------

This is a legacy attribute that will trigger `Typing...` message at the bottom of the chat window. It was used to indicate that the bot is working on the command, but it has several issues that prevents it from being very useful (you can't control how long it will be shown, and it eats up an API call).

Generally speaking bot replies too fast to have any special indicator for most commands. Whenever it is needed, it is better to create a reaction, or post and then update an explicit message.