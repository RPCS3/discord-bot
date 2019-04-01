Compatibility API Client
========================

There is no documentation, but the [source code is available](https://github.com/AniLeo/rpcs3-compatibility).

This project also contains all of the Web API infrastructure to facilitate the automatic serialization/deserialization of data.

Some terminology:
* `POCO` - plain old C# object, is a barebones class with fields/properties only, that is used for automatic [de]serialization.

General advise on web client implementation and usage:
* Do use `HttpClientFactory.Create()` instead of `new HttpClient()`, as every instance will reserve an outgoing port number, and factory keeps a pool.
* Do reuse the same client instance whenever possible, it's thread-safe and there's no reason not to keep a single copy of it.

[Compression](Compression/) contains handler implementation that provides support for transparent http request compression (`Content-Encoding` header), and implements standard gzip/deflate types.

[Formatters](Formatters/) contain JSON contract resolver that handles popular naming conventions for [de]serialization (`dashed-style`, `underscore_style`, and `PascalStyle`).

[Utils](Utils/) have some handy `Uri` extension methods for easy query parameters manipulation.

Game Compatibility Status
-------------------------

Does game status lookup by `product code`, `game title` (English or using romaji for Japanese titles), or `game title abbreviation`. We use this for most embeds, including log parsing results, standalone game information embeds, compatibility lists, etc.

RPCS3 Update Information
------------------------

Accepts current build commit hash as an argument. Provides information about the build requested, as well as information about the latest build available.