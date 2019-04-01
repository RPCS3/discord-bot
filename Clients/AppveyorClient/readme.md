AppVeyor Client
===============

[AppVeyor API documentation](https://www.appveyor.com/docs/api/) for reference. There are some inaccuracies and legacy quirks though. As we use the API for read-only queries, on a publicly available data, we don't need any form of authentication. `User-Agent` header is provided as a courtesy.

What AppVeyor client is used for, is to get the direct download links for PR builds. For that we need to get the job artifacts. And for that we need job id, which can only be associated with the corresponding GitHub PR through the build info, which we can find in the build history. Phew, easy.

`FindBuildAsync` is a general history search function that can be used to search through the history until some criteria is met. We use it at startup with the bogus predicate to get and cache the whole history to map job IDs to their build info, which is used for quick lookup when we want to show PR download for direct links.

`GetMasterBuildAsync` is mainly used to get the build time, as [CompatApi client](../CompatApiClient) provides merge time instead of the build time currently.

`GetPrDownloadAsync` that accepts `githubStatusTargetUrl` uses `string.Replace()` instead of constructing the url manually because of the legacy quirks. AppVeyor has changed the link format at some point, and there's not backwards compatibility there, so old direct links work only with the old link format.