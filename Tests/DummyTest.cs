using CompatBot.Utils;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class DummyTest
    {
        [Test]

        public void TestVariables()
        {
            Assert.That("Group".GetVisibleLength(), Is.EqualTo(5));
        }
    }
}