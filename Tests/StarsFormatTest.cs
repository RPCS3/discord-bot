using CompatBot.Utils;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class StarsFormatTest
{
    [TestCase(0.0, "🌑🌑🌑🌑🌑")]
    [TestCase(0.1, "🌑🌑🌑🌑🌑")]

    [TestCase(0.2, "🌘🌑🌑🌑🌑")]
    [TestCase(0.3, "🌘🌑🌑🌑🌑")]

    [TestCase(0.4, "🌗🌑🌑🌑🌑")]
    [TestCase(0.5, "🌗🌑🌑🌑🌑")]
    [TestCase(0.6, "🌗🌑🌑🌑🌑")]

    [TestCase(0.7, "🌖🌑🌑🌑🌑")]
    [TestCase(0.8, "🌖🌑🌑🌑🌑")]

    [TestCase(0.9, "🌕🌑🌑🌑🌑")]
    [TestCase(1.0, "🌕🌑🌑🌑🌑")]
    public void FormatTest(decimal score, string expectedValue)
    {
        Assert.That(StringUtils.GetMoons(score, false), Is.EqualTo(expectedValue), "Failed for " + score);
    }
}