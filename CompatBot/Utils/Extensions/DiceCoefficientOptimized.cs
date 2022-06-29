namespace CompatBot.Utils.Extensions;

public static class DiceCoefficientOptimized
{
	/// <summary>
	///     Dice Coefficient based on bigrams. <br />
	///     A good value would be 0.33 or above, a value under 0.2 is not a good match, from 0.2 to 0.33 is iffy.
	/// </summary>
	/// <param name="input"></param>
	/// <param name="comparedTo"></param>
	/// <returns></returns>
	public static double DiceIshCoefficientIsh(this string input, string comparedTo)
	{
		var bgCount1 = input.Length - 1;
		var bgCount2 = comparedTo.Length - 1;
		if (comparedTo.Length < input.Length)
		{
			var tmp = input;
			input = comparedTo;
			comparedTo = tmp;
		}
		var matches = 0;
		for (var i = 0; i < input.Length - 1; i++)
		for (var j = 0; j < comparedTo.Length - 1; j++)
		{
			if (input[i] == comparedTo[j] && input[i + 1] == comparedTo[j + 1])
			{
				matches++;
				break;
			}
		}
		if (matches == 0)
			return 0.0d;

		return 2.0 * matches / (bgCount1 + bgCount2);
	}
}