using IrdLibraryClient;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class UriFormattingTests
{
    [TestCase("file with spaces.ird")]
    [TestCase("file (with parenthesis).ird")]
    [TestCase("file/with/segments.ird")]
    public void IrdLinkFormatTest(string filename)
    {
        var uri = IrdClient.GetEscapedDownloadLink(filename);
        Assert.Multiple(() =>
        {
            Assert.That(uri, Does.Not.Contains(" "));
            Assert.That(uri, Does.Not.Contains("("));
            Assert.That(uri, Does.Not.Contains(")"));
            Assert.That(uri, Does.Not.Contains("%2F"));
            Assert.That(uri, Does.EndWith(".ird"));
        });
    }
}