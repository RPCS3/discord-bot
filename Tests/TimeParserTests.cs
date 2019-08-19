using System;
using CompatBot.Utils;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class TimeParserTests
    {
        [TestCase("2019-8-19 6:00 PT", "2019-08-19 13:00")]
        [TestCase("2019-8-19 17:00 bst", "2019-08-19 16:00")]
        public void TimeZoneConverterTest(string input, string utcInput)
        {
            var utc = DateTime.Parse(utcInput).Normalize();
            Assert.That(TimeParser.TryParse(input, out var result), Is.True);
            Assert.That(result, Is.EqualTo(utc));
            Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
        }
    }
}
