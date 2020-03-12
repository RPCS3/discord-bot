using CompatBot.Database;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class FlagTests
    {
        [Test]
        public void MultipleFlagTest()
        {
            var testVal = FilterAction.IssueWarning | FilterAction.MuteModQueue;
            Assert.That(testVal.HasFlag(FilterAction.IssueWarning), Is.True);
            Assert.That(testVal.HasFlag(FilterAction.IssueWarning | FilterAction.MuteModQueue), Is.True);
            Assert.That(testVal.HasFlag(FilterAction.IssueWarning | FilterAction.MuteModQueue | FilterAction.RemoveContent), Is.False);
            Assert.That(testVal.HasFlag(FilterAction.IssueWarning | FilterAction.SendMessage), Is.False);
        }
    }
}
