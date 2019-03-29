PSN Client
==========

For obvious reasons, there's no official documentation on any Sony APIs. Everything was reverse-engineered using web store UI or from various wikis/forums.

PSN Store API
-------------

You can access web store at https://store.playstation.com/, which is working on top of their store API, which is convenient.

General workflow is as follows:
1. Get session (even for anonymous access)
2. Get storefront information
3. Call various controllers to search or to get additional item information by its ID

Some terminology:
* [product code](http://www.psdevwiki.com/ps3/Productcode) is the game ID, of the form `NPEB12345`.
* [content id](http://www.psdevwiki.com/ps3/Content_ID) is the unique PSN content ID that can be used to resolve its metadata. There's no straight way to map `product code` to any associated `content id`.
* `container id` is the PSN content aggregation ID that is used to organize the content (i.e. store navigation category like a menu entry, or sale event). Container can include other containers in it.
* `entitlement` is the content license granted to the account. You get this by purchasing the content, downloading free content, or by redeeming a PSN code.

At the startup we run a task that enumerates all available PSN storefronts, and then recursively scrapes every container on respective front page to collect all available `content id`s for any PS3 content that is still available.

Many de-listed or replaced titles are no longer available through anonymous API calls (they require authenticated session with respective `entitlement`s given to the account).

There are rare cases where resolving metadata by `content id` still works, but there are no links for it anywhere on the store. You can still find such content using the search API.

Game Update API
---------------

This is a [separate API](https://www.psdevwiki.com/ps3/Online_Connections#Game_Updating_Procedure) that can give title update information by `product code`.

One quirk of this endpoint is that Sony uses non-public root CAs for TLS certificates, only redistributing their public keys in the PS3 firmware updates.

In dotnet core there's no easy way to implement custom certificate pinning / chain validation.

There are two possible ways:
1. Importing root CA certificates to the Trusted Root CAs certificate store, so the default validation can work as expected.

   This, however, only works on Windows, _and_ will show a confirmation prompt for every certificate being imported.
   
   On Linux it's much worse and require black magic to work, and is inconsistent between different distros (google `SSL_CERT_DIR` and `SSL_CERT_FILE`). The main problem is that this _overrides_ the system/user certificate store to contain _only_ the certificates specified, so any request to any other resource will fail.

2. Manual certificate chain validation. As one might expect, this is not trivial or easy to implement.

   What we do right now, is to check certificate Issuer on every request, and if it matches the custom Sony CA, we do manual chain validation, and then cache the result for this specific server certificate. We explicitly ignore any revocation checks (as CAs are not public) and also ignore any errors due to untrusted root (as, again, CAs are not public).
   
   Otherwise we simply forward the validation call to the default handler that is using proper system/user certificate store.

Title Metadata API
------------------

[TMDB API](https://www.psdevwiki.com/ps3/Keys#TMDB_Key) is used by the PS3 dashboard / shell / UI to show some game information using `product code`. We mainly use this to get the game thumbnail for embeds, falling back to PSN metadata when it's not available.

The main quirk here is how the URL is constructed using a specific HMAC key and ID format.