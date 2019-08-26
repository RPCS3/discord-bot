Data Providers
==============

These classes should ease the data access and manipulation. If no special consideration is required, you should use database access directly.

`AmdDriverVersionProvider`
--------------------------
This class builds and keeps the AMD driver version mappings for Vulkan and OpenGL using [GPUOpen project](https://github.com/GPUOpen-Drivers) as a source.

AMD has many gotchas with their driver versioning, included, but not limited to:
* existing versioning information goes only as far back as 18.1.1 release
* OpenGL driver version may differ from actual driver version
  * `major` part (**m**.x.y.z) seems to be following the [Windows Driver versioning](https://docs.microsoft.com/en-us/windows-hardware/drivers/display/version-numbers-for-display-drivers) scheme
  * `build` part (x.y.z.**b**) may vary depending on different driver package (desktop vs mobile vs minor re-releases)
* Vulkan driver versioning is completely separate and may span several driver releases

`ContentFilter`
---------------
This class implements actual content filter using the pre-defined filter descriptions.

As you can probably guess, simply iterating over each filter trigger is not gonna cut it if you have more than 3 filters and a lot of text to moderate.
This is where [Aho-Corasick](https://en.wikipedia.org/wiki/Aho%E2%80%93Corasick_algorithm) algorithm comes into play. It allows for linear complexity over the _filtered text length_.

On bot start up and every change to the filter list, this provider will reconstruct the Aho-Corasick state machine that is used for content filtering.
As an implementation detail, we construct one state machine per filter context (Discord messages or log file).

The idea here is to feed the state machine with the text we want to check. It will invoke the callback on any trigger match, where we can decide if the filter should be applied or not.

As a convenience we have a static methods that will wrap all the details inside, and will perform all the required actions when necessary.

`DisabledCommandsProvider`
--------------------------
This provider wraps the complexity of bot command framework enumeration in case of a wildcard command matching, and also for synchronization of in-memory hash map and persistent list in the database.

`InviteWhitelistProvider`
-------------------------
This is mostly needed for maintenance of expired invite codes.

`ModProvider`
-------------
This is mostly a legacy provider for managing the bot roles.

`ScrapeStateProvider`
---------------------
This provider wraps up the logic of refreshing the crawler state for different PSN categories, thumbnail caches, etc.

`StatsStorage`
--------------
Contains logic for serialization and deserialization of in-memory stat tracking.
One fun part about serialization is that it is using reflection to access non-public member of the cache class to get the actual data.

`SyscallInfoProvider`
---------------------
This is mostly needed to wrap up the complexity of persisting the function call data gathered from the logs, as it must be aware of already existing data to insertion of non-unique keys.

`ThumbnailProvider`
-------------------
This one will:
* look up thumbnail image from multiple sources
* check if it's current
* check if it can be embedded by Discord (it requires a proper file extension)
  * re-upload it to a private channel for persistent caching if needed
* update the associated tables in the database

And at the end it will return the readily usable URL.