using System;
using CompatBot.Utils;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class TimeParserTests
{
    [TestCase("2019-8-19 6:00 PT", "2019-08-19T13:00Z")]
    [TestCase("2019-8-19 17:00 cest", "2019-08-19T15:00Z")]
    [TestCase("2019-9-1 22:00 jst", "2019-09-01T13:00Z")]
    public void TimeZoneConverterTest(string input, string utcInput)
    {
        var utc = DateTime.Parse(utcInput).Normalize();
        Assert.Multiple(() =>
        {
            Assert.That(TimeParser.TryParse(input, out var result), Is.True, $"{input} failed to parse\nSupported time zones: {string.Join(", ", TimeParser.GetSupportedTimeZoneAbbreviations())}");
            Assert.That(result, Is.EqualTo(utc));
            Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void TimeZoneInfoTest()
    {
        Assert.That(TimeParser.TimeZoneMap, Is.Not.Empty);
    }
}