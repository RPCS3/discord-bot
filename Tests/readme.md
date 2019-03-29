Tests
=====

I am using [NUnit](https://github.com/nunit/docs/wiki/NUnit-Documentation), mostly because I'm most familiar with this test framework. There's not a lot of tests for the code itself, it is mostly used for testing things out before implementation.

You can use the regular `$ dotnet test` command to run the tests without any additional tools.

If you want to contribute new test code, I have a couple of preferences:
* Do use `Assert.That(expr, Is/Does/etc)` format instead of deprecated `Assert.AreEqual()` and similar.

* Try to write the code in the way that does not require the use of `InternalsVisibleTo` attribute.

* Tests that require any external data that must be manually supplied, should be disabled by default.