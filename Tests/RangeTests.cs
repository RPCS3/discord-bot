using CompatBot.Utils;
using NUnit.Framework;
// ReSharper disable RedundantRangeBound

namespace Tests
{
	[TestFixture]
	public class RangeTests
	{
		[Test]
		public void UniqueListRangeAccessorTest()
		{
			var testValue = new[] {0, 1, 2, 3, 4};
			var list = new UniqueList<int>(testValue);
			Assert.That(list, Is.EqualTo(testValue));

			Assert.That(list[0..], Is.EqualTo(testValue[0..]));
			Assert.That(list[1..], Is.EqualTo(testValue[1..]));
			Assert.That(list[4..], Is.EqualTo(testValue[4..]));
			Assert.That(list[5..], Is.EqualTo(testValue[5..]));

			Assert.That(list[0..1], Is.EqualTo(testValue[0..1]));
			Assert.That(list[0..4], Is.EqualTo(testValue[0..4]));
			Assert.That(list[0..5], Is.EqualTo(testValue[0..5]));
			Assert.That(list[1..4], Is.EqualTo(testValue[1..4]));
			Assert.That(list[1..5], Is.EqualTo(testValue[1..5]));
			
			Assert.That(list[..0], Is.EqualTo(testValue[..0]));
			Assert.That(list[..1], Is.EqualTo(testValue[..1]));
			Assert.That(list[..4], Is.EqualTo(testValue[..4]));
			Assert.That(list[..5], Is.EqualTo(testValue[..5]));

			Assert.That(list[^0..], Is.EqualTo(testValue[^0..]));
			Assert.That(list[^1..], Is.EqualTo(testValue[^1..]));
			Assert.That(list[^4..], Is.EqualTo(testValue[^4..]));
			Assert.That(list[^5..], Is.EqualTo(testValue[^5..]));

			Assert.That(list[^1..^0], Is.EqualTo(testValue[^1..^0]));
			Assert.That(list[^4..^0], Is.EqualTo(testValue[^4..^0]));
			Assert.That(list[^5..^0], Is.EqualTo(testValue[^5..^0]));
			Assert.That(list[^4..^1], Is.EqualTo(testValue[^4..^1]));
			Assert.That(list[^5..^1], Is.EqualTo(testValue[^5..^1]));

			Assert.That(list[..^0], Is.EqualTo(testValue[..^0]));
			Assert.That(list[..^1], Is.EqualTo(testValue[..^1]));
			Assert.That(list[..^4], Is.EqualTo(testValue[..^4]));
			Assert.That(list[..^5], Is.EqualTo(testValue[..^5]));
		}

		[Test]
		public void SubstringTests()
		{
			var str = "abc";
			Assert.That(str[1..], Is.EqualTo(str.Substring(1)));
			Assert.That("a"[1..], Is.EqualTo("a".Substring(1)));
			Assert.That(str[0], Is.EqualTo('a'));
			Assert.That(str[^1], Is.EqualTo('c'));
		}
	}
}