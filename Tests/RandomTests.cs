using System;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class RandomTests
{
    [Explicit]
    [TestCase(6, 100_000_000, 0.1)]
    public void DistributionUniformityTest(int dice, int sampleCount, double expectedDeviation)
    {
        var counterNaive = new int[dice];
        var counterRange = new int[dice];
        var rng = new Random();
        for (var i = 0; i < sampleCount; i++)
        {
            counterNaive[rng.Next(dice)]++;
            counterRange[rng.Next(1, dice + 1) - 1]++;
        }
        var expectedMedian = sampleCount / (double)dice;
        Console.WriteLine("Naive\tRange");
        Assert.Multiple(() =>
        {
            for (var i = 0; i < dice; i++)
            {
                var devNaive = GetPercent(counterNaive[i], expectedMedian);
                var devRange = GetPercent(counterRange[i], expectedMedian);
                Console.WriteLine($"{counterNaive[i]} ({devNaive:0.000})\t{counterRange[i]} ({devRange:0.000})");
                Assert.That(devNaive, Is.LessThan(expectedDeviation), $"Naive dice face {i + 1}");
                Assert.That(devRange, Is.LessThan(expectedDeviation), $"Range dice face {i + 1}");
            }
        });
    }

    private static double GetPercent(int actual, double expected)
        => Math.Abs(actual - expected) / expected * 100.0;
}