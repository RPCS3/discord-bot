Clients
=======

Here we keep all the 3rd party service clients used by the bot. Most infrastructure is in the CompatApi client, and other clients reference it to use these classes, along with the configured `Log`.

* CompatApi is the [custom API](https://github.com/AniLeo/rpcs3-compatibility) provided by the [RPCS3 website](https://rpcs3.net/). It provides information about game compatibility and RPCS3 updates.

* [IRD Library](http://jonnysp.bplaced.net/) contains the largest public repository of [IRD files](http://www.psdevwiki.com/ps3/Bluray_disc#IRD_file). It has no official API, so everything is reverse-engineered from the website web UI.

  > Client implements automatic caching of the downloaded IRD files on the local filesystem for future uses.

* PSN Client is a result of reverse-engineering the JSON API of the [Playstation Store](https://store.playstation.com/). Currently it implements resolving metadata content by its ID, as well as full-text search.

* GitHub Client implements a barebone [set of requests](https://developer.github.com/v3/) to resolve pull-request information, along with some additional data about the CI states.

  > We do not use any form of authentication, and are limited by the regular rate of 60 API requests per hour.

* AppVeyor Client implements most of the [read-only calls](https://www.appveyor.com/docs/api/) to read the build history, job status, and artifact information.