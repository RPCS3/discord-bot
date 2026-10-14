using HomoglyphConverter;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class HomoglyphTests
{
    [TestCase("Роrnһub", ExpectedResult = "Pomhub")]
    public string CanonicalFormTest(string input)
        => input.ToCanonicalForm();

    [TestCase("Роrnһub", ExpectedResult = "Pornhub")]
    public string SkeletonStringTest(string input)
        => input.ToSkeletonString();
}