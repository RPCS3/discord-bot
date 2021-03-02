using System;
using System.Collections.Generic;
using System.Linq;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DuoVia.FuzzyStrings;
using HomoglyphConverter;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class StringUtilTests
    {
        [TestCase("🇭🇷", ExpectedResult = 2)]
        [TestCase("\u200d", ExpectedResult = 0)]
        [TestCase("㌀", ExpectedResult = 1)]
        [TestCase("a\u0304\u0308bc\u0327", ExpectedResult = 3)]
        [TestCase("Megamouse", ExpectedResult = 9)]
        public int VisibleLengthTest(string input) => input.GetVisibleLength();

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
        [TestCase("соc͏katrice", "cockatrice")]
        [TestCase("çöćķåťřĩĉȅ", "cockatrice")]
        [TestCase("с⁪◌ck⁬åťřĩĉȅ", "cockatrice")]
        [TestCase("jò̵͗s̷̑͠ẻ̵͝p̸̆̂h̸͐̿", "joseph")]
        public void HomoglyphDetectionTest(string strA, string strB)
        {
            Assert.That(strA.StripInvisibleAndDiacritics().ToCanonicalForm(), Is.EqualTo(strB));
        }

        [TestCase("jò̵͗s̷̑͠ẻ̵͝p̸̆̂h̸͐̿", "joseph")]
        public void StripZalgoTest(string input, string expected)
        {
            var stripped = UsernameZalgoMonitor.StripZalgo(input, 0ul);
            Assert.That(stripped, Is.EqualTo(expected));
        }

        [Test]
        public void SubstringTest()
        {
            var contentId = "UP2611-NPUB31848_00-HDDBOOTPERSONA05";
            var productCode = "NPUB31848";
            Assert.That(contentId[7..16], Is.EqualTo(productCode));
        }

        [TestCase("a grey and white cat sitting in front of a window", ExpectedResult = "a grey and white kot sitting in front of a window")]
        public string FixKotTest(string input) => input.FixKot();

        [TestCase("minesweeeper", "minesweeper")]
        [TestCase("minesweeeeeeeeeeeeeeeeeeper", "minesweeper")]
        [TestCase("ee", "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee")]
        [TestCase("aaaaaaaaa", "aaaaaaaaa")]
        [TestCase("South Fort Union", "West Fort Union")]
        public void DiceCoefficientRangeTest(string strA, string strB)
        {
            var coef = DiceCoefficient(strA, strB);
            Assert.That(coef, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(1.0));
            //Assert.That(DiceCoefficientOptimized.DiceCoefficient(strA, strB), Is.EqualTo(coef));
            //Assert.That(DiceCoefficientExtensions.DiceCoefficient(strA, strB), Is.EqualTo(coef));

            (strB, strA) = (strA, strB);
            var coefB = DiceCoefficient(strA, strB);
            Assert.That(coefB, Is.EqualTo(coef));
            //Assert.That(DiceCoefficientOptimized.DiceCoefficient(strA, strB), Is.EqualTo(coef));
            //Assert.That(DiceCoefficientExtensions.DiceCoefficient(strA, strB), Is.EqualTo(coef));
        }

        [Test]
        public void DistanceTest()
        {
            var strA = @"
""Beware of the man who works hard to learn something, learns it, and finds
himself no wiser than before,"" Bokonon tells us. ""He is full of murderous
resentment of people who are ignorant without having come by their
ignorance the hard way.""
        ― Kurt Vonnegut, ""Cat's Cradle""
".Trim();
            var strB = @"
""Beware of the man who works hard to learn something, learns it, and finds himself no wiser than before,"" Bokonon tells us. ""He is full of murderous resentment of people who are ignorant without having come by their ignorance the hard way.""
                -- Kurt Vonnegut, ""Cat's Cradle""
".Trim();

            var coef = DiceCoefficientOptimized.DiceIshCoefficientIsh(strA, strB);
            Assert.That(coef, Is.GreaterThan(0.95), "Dice Coefficient");
        }

        public static double DiceCoefficient(string input, string comparedTo)
        {
            var ngrams = input.ToBiGrams()[1..^1];
            var compareToNgrams = comparedTo.ToBiGrams()[1..^1];
            return DiceCoefficient(ngrams, compareToNgrams);
        }

        public static double DiceCoefficient(string[] nGrams, string[] compareToNGrams)
        {
            var nGramMap = new Dictionary<string, int>(nGrams.Length);
            var compareToNGramMap = new Dictionary<string, int>(compareToNGrams.Length);
            var nGramSet = new HashSet<string>();
            var compareToNGramSet = new HashSet<string>();
            foreach (var nGram in nGrams)
            {
                if (nGramSet.Add(nGram))
                    nGramMap[nGram] = 1;
                else
                    nGramMap[nGram]++;
            }
            foreach (var nGram in compareToNGrams)
            {
                if (compareToNGramSet.Add(nGram))
                    compareToNGramMap[nGram] = 1;
                else
                    compareToNGramMap[nGram]++;
            }
            nGramSet.IntersectWith(compareToNGramSet);
            if (nGramSet.Count == 0)
                return 0.0d;

            var matches = 0;
            foreach (var nGram in nGramSet)
                matches += Math.Min(nGramMap[nGram], compareToNGramMap[nGram]);
			
            double totalBigrams = nGrams.Length + compareToNGrams.Length;
            return (2 * matches) / totalBigrams;
        }
    }
}
