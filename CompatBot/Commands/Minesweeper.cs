using CommunityToolkit.HighPerformance;

namespace CompatBot.Commands;

internal static class Minesweeper
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

	[Command("minesweeper")]
	[Description("Generate a minesweeper field with specified parameters")]
	public static async ValueTask Generate(
		SlashCommandContext ctx,
		[Description("Width of the field"), MinMaxValue(3)]
		int width = 14,
		[Description("Height of the field"), MinMaxValue(3, 98)]
		int height = 14,
		[Description("Number of mines"), MinMaxValue(1)]
		int mines = 30
	)
	{
		var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
		var header = $"{mines}x💣\n";
		var footer = "If something is cut off, blame Discord";
		var maxMineCount = (width - 1) * (height - 1) * 2 / 3;
		if (mines > maxMineCount)
		{
			await ctx.RespondAsync("Isn't this a bit too many mines 🤔", ephemeral: ephemeral).ConfigureAwait(false);
			return;
		}

		var msgLen = (4 * width * height - 4) + (height - 1) + mines * MaxBombLength + (width * height - mines) * Numbers[0].Length + header.Length;
		if (width * height > 198 || msgLen > EmbedPager.MaxMessageLength) // for some reason discord would cut everything beyond 198 cells even if the content length is well within the limits
		{
			await ctx.RespondAsync("Requested field size is too large for one message", ephemeral: ephemeral).ConfigureAwait(false);
			return;
		}

		await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
		var rng = new Random();
		var field = GenerateField(width, height, mines, rng);
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
		await ctx.RespondAsync(result.ToString(), ephemeral: ephemeral).ConfigureAwait(false);
	}

	private static byte[,] GenerateField(int width, int height, in int mineCount, in Random rng)
	{
		var len = width * height;
		Span<byte> cells = stackalloc byte[len];
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
		cells.CopyTo(result.AsSpan());

		//update mine indicators
		byte Get(int x, int y) => x < 0 || x >= width || y < 0 || y >= height ? (byte)0 : result[y, x];

		byte CountMines(int x, int y)
		{
			byte c = 0;
			for (var yy = y - 1; yy <= y + 1; yy++)
			for (var xx = x - 1; xx <= x + 1; xx++)
				if ((CellVal)Get(xx, yy) is CellVal.Mine)
					c++;
			return c;
		}

		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
			if ((CellVal)result[y, x] is not CellVal.Mine)
				result[y, x] = CountMines(x, y);
		return result;
	}
}