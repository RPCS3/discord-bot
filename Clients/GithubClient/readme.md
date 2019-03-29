GitHub Client
=============

[GitHub API documentation](https://developer.github.com/v3/) for reference. Anonymous API calls require `User-Agent` header, everything else is optional. Anonymous access is limited to 60 requests per hour, matched by client IP.

We only use GitHub API to get PR information, and optionally, links to CIs. CI status information is unreliable though, as it's often outdated and the history is often inconsistent, so we prefer to find matching builds manually instead.

As anonymous access is very limited, we try to cache every response. In the same vein, we try to limit GitHub API usage in general.