using CompatBot.Utils;
using HomoglyphConverter;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class StringUtilTests
    {
        [Test]
        public void VisibleLengthTest()
        {
            Assert.That("🇭🇷".GetVisibleLength(), Is.EqualTo(2));
            Assert.That("\u200d".GetVisibleLength(), Is.EqualTo(0));
            Assert.That("㌀".GetVisibleLength(), Is.EqualTo(1));
            Assert.That("a\u0304\u0308bc\u0327".GetVisibleLength(), Is.EqualTo(3));
            Assert.That("Megamouse".GetVisibleLength(), Is.EqualTo(9));
        }

        [Test]
        public void VisibleTrimTest()
        {
            Assert.That("abc".TrimVisible(100), Is.EqualTo("abc"));
            Assert.That("abc".TrimVisible(3), Is.EqualTo("abc"));
            Assert.That("abc".TrimVisible(2), Is.EqualTo("a…"));
        }

        [TestCase("cockatrice", "сockаtrice")]
        [TestCase("cockatrice", "çöćķåťřĩĉȅ")]
        public void DiacriticsDetectionTest(string strA, string strB)
        {
            Assert.That(strA.EqualsIgnoringDiacritics(strB), Is.True);
        }

        [TestCase("cockatrice", "cockatrice")]
        [TestCase("сосkаtriсе", "cockatrice")]
        [TestCase("cocкatrice", "cockatrice")]
        [TestCase("c‎ockatrice", "cockatrice")]
        public void HomoglyphDetectionTest(string strA, string strB)
        {
            Assert.That(strA.StripInvisible().ToCanonicalForm(), Is.EqualTo(strB));
        }
    }
}
