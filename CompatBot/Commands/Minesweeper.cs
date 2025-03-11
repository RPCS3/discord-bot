namespace CompatBot.Commands;

internal sealed class Minesweeper
{
	//private static readonly string[] Numbers = ["0️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣"];
	private static readonly string[] Numbers = ["０", "１", "２", "３", "４", "５", "６", "７", "８", "９"];
	private static readonly string[] Bombs = ["＊", "◎"];
	private static readonly int MaxBombLength;

	static Minesweeper()
	{
		MaxBombLength = Bombs.Select(b => b.Length).Max();
	}

	private enum CellVal: byte
	{
		Zero = 0,
		One = 1,
		Two = 2,
		Three = 3,
		Four = 4,
		Five = 5,
		Six = 6,
		Seven = 7,
		Eight = 8,
			
		OpenZero = 100,

		Mine = 255,
	}

	[Command("minesweeper"), TextAlias("msgen")]
	[LimitedToOfftopicChannel]
	//[Cooldown(1, 30, CooldownBucketType.Channel)]
	[Description("Generates a minesweeper field with specified parameters")]
	public async Task Generate(CommandContext ctx,
		[Description("Width of the field")] int width = 14,
		[Description("Height of the field")] int height = 14,
		[Description("Number of mines")] int mineCount = 30)
	{
		if (width < 3 || height < 3 || mineCount < 1)
		{
			await ctx.Channel.SendMessageAsync("Invalid generation parameters").ConfigureAwait(false);
			return;
		}

		var header = $"{mineCount}x💣\n";
		var footer = "If something is cut off, blame Discord";
		var maxMineCount = (width - 1) * (height - 1) * 2 / 3;
		if (mineCount > maxMineCount)
		{
			await ctx.Channel.SendMessageAsync("Isn't this a bit too many mines 🤔").ConfigureAwait(false);
			return;
		}

		if (height > 98)
		{
			await ctx.Channel.SendMessageAsync("Too many lines for one message, Discord would truncate the result randomly").ConfigureAwait(false);
			return;
		}

		var msgLen = (4 * width * height - 4) + (height - 1) + mineCount * MaxBombLength + (width * height - mineCount) * "0️⃣".Length + header.Length;
		if (width * height > 198 || msgLen > EmbedPager.MaxMessageLength) // for some reason discord would cut everything beyond 198 cells even if the content length is well within the limits
		{
			await ctx.Channel.SendMessageAsync("Requested field size is too large for one message").ConfigureAwait(false);
			return;
		}

		var rng = new Random();
		var field = GenerateField(width, height, mineCount, rng);
		var result = new StringBuilder(msgLen).Append(header);
		var bomb = rng.NextDouble() > 0.9 ? Bombs[rng.Next(Bombs.Length)] : Bombs[0];
		var needOneOpenCell = true;
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var c = (CellVal)field[y, x] == CellVal.Mine ? bomb : Numbers[field[y, x]];
				if (needOneOpenCell && field[y, x] == 0)
				{
					result.Append(c);
					needOneOpenCell = false;
				}
				else
					result.Append("||").Append(c).Append("||");
			}
			result.Append('\n');
		}
		result.Append(footer);
		await ctx.Channel.SendMessageAsync(result.ToString()).ConfigureAwait(false);
	}

	private static byte[,] GenerateField(int width, int height, in int mineCount, in Random rng)
	{
		var len = width * height;
		var cells = new byte[len];
		// put mines
		for (var i = 0; i < mineCount; i++)
			cells[i] = (byte)CellVal.Mine;

		//shuffle the board
		for (var i = 0; i < len - 1; i++)
		{
			var j = rng.Next(i, len);
			(cells[i], cells[j]) = (cells[j], cells[i]);
		}
		var result = new byte[height, width];
		Buffer.BlockCopy(cells, 0, result, 0, len);

		//update mine indicators
		byte get(int x, int y) => x < 0 || x >= width || y < 0 || y >= height ? (byte)0 : result[y, x];

		byte countMines(int x, int y)
		{
			byte c = 0;
			for (var yy = y - 1; yy <= y + 1; yy++)
			for (var xx = x - 1; xx <= x + 1; xx++)
				if ((CellVal)get(xx, yy) == CellVal.Mine)
					c++;
			return c;
		}

		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
			if ((CellVal)result[y, x] != CellVal.Mine)
				result[y, x] = countMines(x, y);
		return result;
	}
}