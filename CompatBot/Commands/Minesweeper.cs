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

	[Flags]
	private enum CellVal : byte
	{
		Zero  = 0x00,
		One   = 0x01,
		Two   = 0x02,
		Three = 0x03,
		Four  = 0x04,
		Five  = 0x05,
		Six   = 0x06,
		Seven = 0x07,
		Eight = 0x08,
		Open  = 0x10,
		Mine  = 0x80,
	}

	[Command("minesweeper")]
	[Description("Generate a minesweeper field with specified parameters")]
	public static async ValueTask Generate(
		SlashCommandContext ctx,
		[Description("Width of the field"), MinMaxValue(3, 255)]
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
		var len = width * height;
		Span<CellVal> buff = stackalloc CellVal[len];
		GenerateField(buff, width, height, mines, rng);
		OpenZeroCells(buff, width, height, mines, rng);
		var result = new StringBuilder(msgLen).Append(header);
		var bomb = rng.NextDouble() > 0.9 ? Bombs[rng.Next(Bombs.Length)] : Bombs[0];
		var field = buff.AsSpan2D(height, width);
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var c = field[y, x] is CellVal.Mine ? bomb : Numbers[(byte)field[y, x] & 0x0f];
				if (field[y, x].HasFlag(CellVal.Open))
					result.Append(c);
				else
					result.Append("||").Append(c).Append("||");
			}
			result.Append('\n');
		}
		result.Append(footer);
		await ctx.RespondAsync(result.ToString(), ephemeral: ephemeral).ConfigureAwait(false);
	}

	private static void GenerateField(Span<CellVal> cells, int width, int height, in int mineCount, in Random rng)
	{
		var len = cells.Length;
		// put mines
		for (var i = 0; i < mineCount; i++)
			cells[i] = CellVal.Mine;

		//shuffle the board
		for (var i = 0; i < len - 1; i++)
		{
			var j = rng.Next(i, len);
			(cells[i], cells[j]) = (cells[j], cells[i]);
		}
		var result = cells.AsSpan2D(height, width);

		//update mine indicators
		CellVal Get(Span2D<CellVal> f, int x, int y) => x < 0 || x >= width || y < 0 || y >= height ? 0 : f[y, x];

		CellVal CountMines(Span2D<CellVal> f, int x, int y)
		{
			CellVal c = 0;
			for (var yy = y - 1; yy <= y + 1; yy++)
			for (var xx = x - 1; xx <= x + 1; xx++)
				if (Get(f, xx, yy) is CellVal.Mine)
					c++;
			return c;
		}

		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
			if (result[y, x] is not CellVal.Mine)
				result[y, x] = CountMines(result, x, y);
	}

	private static void OpenZeroCells(Span<CellVal> cells, in int width, in int height, in int mineCount,  in Random rng)
	{
		var field = cells.AsSpan2D(height, width);
		var len = cells.Length;
		int startPos;
		for (startPos = rng.Next(len); startPos < len; startPos = (startPos + 1) % len)
			if (cells[startPos] is 0)
				break;
		var sy = (byte)(startPos / width);
		var sx = (byte)(startPos - sy * width);
		Span<(byte x, byte y)> curWave = stackalloc (byte, byte)[len - mineCount];
		Span<(byte x, byte y)> nextWave = stackalloc (byte, byte)[len - mineCount];
		var curWaveSize = 0;
		var nextWaveSize = 0;

		void Push(Span2D<CellVal> f, int x, int y, Span<(byte x, byte y)> wave, ref int waveLen)
		{
			if (f[y, x] is 0)
				wave[waveLen++] = ((byte)x, (byte)y);
			f[y, x] |= CellVal.Open;
		}
		
		Push(field, sx, sy, curWave, ref curWaveSize);
		do
		{
			foreach (var (x, y) in curWave[..curWaveSize])
			{
				if (y > 0)
				{
					if (x > 0)
						Push(field, x - 1, y - 1, nextWave, ref nextWaveSize);
					Push(field, x, y - 1, nextWave, ref nextWaveSize);
					if (x < width - 1)
						Push(field, x + 1, y - 1, nextWave, ref nextWaveSize);
				}
				{
					if (x > 0)
						Push(field, x - 1, y, nextWave, ref nextWaveSize);
					Push(field, x, y, nextWave, ref nextWaveSize);
					if (x < width - 1)
						Push(field, x + 1, y, nextWave, ref nextWaveSize);
				}
				if (y < height - 1)
				{
					if (x > 0)
						Push(field, x - 1, y + 1, nextWave, ref nextWaveSize);
					Push(field, x, y + 1, nextWave, ref nextWaveSize);
					if (x < width - 1)
						Push(field, x + 1, y + 1, nextWave, ref nextWaveSize);
				}
			}
			(curWaveSize, nextWaveSize) = (nextWaveSize, 0);
			var tmp = curWave;
			curWave = nextWave;
			nextWave = tmp;
		} while (curWaveSize > 0);
	}
}