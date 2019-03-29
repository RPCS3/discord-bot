Homoglyph Converter
===================

This is a straight up implementation of the recommended [confusable detection algorithm](http://www.unicode.org/reports/tr39/#Confusable_Detection). It is mainly used to check for mod impersonation.

You can get the latest version of the mappings from the [Unicode.org](http://www.unicode.org/Public/security/latest/confusables.txt). You'll need to manually gzip it for embedding in the resources.

Code is split in two parts:
* Builder will load the mapping file from the resources and will build the mapping dictionary that can be used to quickly substitute the character sequences.

  > One gotcha is that a lot of the characters are from the extended planes and require use of [surrogate pairs](https://en.wikipedia.org/wiki/UTF-16#U+010000_to_U+10FFFF), so we convert them to UTF32 and store as `uint`.

* Normalizer implements the mapping and reducing steps of the algorithm