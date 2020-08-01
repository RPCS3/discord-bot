using System;
using System.Linq;

namespace CompatBot.Utils.Extensions
{
	public static class DiceCoefficientOptimized
	{
		/// <summary>
		///     Dice Coefficient based on bigrams. <br />
		///     A good value would be 0.33 or above, a value under 0.2 is not a good match, from 0.2 to 0.33 is iffy.
		/// </summary>
		/// <param name="input"></param>
		/// <param name="comparedTo"></param>
		/// <returns></returns>
		public static double DiceCoefficient(this string input, in string comparedTo)
		{
			const int maxStackAllocSize = 256;
			const int maxInputLengthToFit = maxStackAllocSize / 2 + 1;
			Span<char> inputBiGrams = input.Length > maxStackAllocSize ? new char[2 * (input.Length - 1)] : stackalloc char[2 * (input.Length - 1)];
			Span<char> compareToBiGrams = comparedTo.Length > maxStackAllocSize ? new char[2 * (comparedTo.Length - 1)] : stackalloc char[2 * (comparedTo.Length - 1)];
			ToBiGrams(input, inputBiGrams);
			ToBiGrams(comparedTo, compareToBiGrams);
			return DiceCoefficient(inputBiGrams, compareToBiGrams);
		}

		/// <summary>
		///     Dice Coefficient used to compare biGrams arrays produced in advance.
		/// </summary>
		/// <param name="biGrams"></param>
		/// <param name="compareToBiGrams"></param>
		/// <returns></returns>
		private static double DiceCoefficient(in Span<char> biGrams, in Span<char> compareToBiGrams)
		{
			var bgCount1 = biGrams.Length / 2;
			var bgCount2 = compareToBiGrams.Length / 2;
			Span<char> smaller, larger;
			if (biGrams.Length <= compareToBiGrams.Length)
			{
				smaller = biGrams;
				larger = compareToBiGrams;
			}
			else
			{
				smaller = compareToBiGrams;
				larger = biGrams;
			}
			var matches = 0;
			for (var i = 0; i < smaller.Length; i += 2)
				for (var j = 0; j < larger.Length; j +=2)
			{
				if (smaller[i] == larger[j] && smaller[i + 1] == larger[j + 1])
				{
					matches++;
					break;
				}
			}
			if (matches == 0)
				return 0.0d;

			return 2.0 * matches / (bgCount1 + bgCount2);
		}

		private static void ToBiGrams(in string input, Span<char> nGrams)
		{
			var str = input.AsSpan();
			for (var i = 0; i < nGrams.Length; i++)
			{
				str.Slice(i, 2).CopyTo(nGrams);
				nGrams = nGrams.Slice(2);
			}
		}
	}
}