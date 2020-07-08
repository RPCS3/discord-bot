using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
	[Group("minesweeper"), Aliases("msgen")]
	[LimitedToSpamChannel, Cooldown(1, 30, CooldownBucketType.Channel)]
	[Description("Generates a minesweeper field with specified parameters")]
	internal sealed class Minesweeper : BaseCommandModuleCustom
	{
		private static readonly string[] Numbers = {"0️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣",};
		private static readonly string[] Bombs = {"💥", "🤡",};
		private static readonly int MaxBombLength;

		static Minesweeper()
		{
			MaxBombLength = Bombs.Select(b => b.Length).Max();
		}

		[GroupCommand]
		public async Task Generate(CommandContext ctx,
			[Description("Width of the field")] int width = 14,
			[Description("Height of the field")] int height = 14,
			[Description("Number of mines")] int mineCount = 30)
		{
			if (width < 2 || height < 2 || mineCount < 1)
			{
				await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid generation parameters").ConfigureAwait(false);
				return;
			}

			var msgLen = 4 * width * height + (height - 1) + mineCount * MaxBombLength + (width * height - mineCount) * "0️⃣".Length;
			if (width * height > 198 || msgLen > 2000) // for some reason discord would cut everything beyond 198 cells even if the content length is well within the limits
			{
				await ctx.ReactWithAsync(Config.Reactions.Failure, "Requested field size is too large for one message", true).ConfigureAwait(false);
				return;
			}

			var rng = new Random();
			var field = GenerateField(width, height, mineCount, rng);
			var result = new StringBuilder(msgLen);
			var bomb = rng.NextDouble() > 0.9 ? Bombs[rng.Next(Bombs.Length)] : Bombs[0];
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var c = field[y, x] == 255 ? bomb : Numbers[field[y, x]];
					result.Append("||").Append(c).Append("||");
				}
				if (y < height - 1)
					result.Append('\n');
			}
			await ctx.RespondAsync(result.ToString()).ConfigureAwait(false);
		}

		private byte[,] GenerateField(int width, int height, in int mineCount, in Random rng)
		{
			var len = width * height;
			var cells = new byte[len];
			// put mines
			for (var i = 0; i < mineCount; i++)
				cells[i] = 255;

			//shuffle the board
			for (var i = 0; i < len - 1; i++)
			{
				var j = rng.Next(i, len);
				var tmp = cells[i];
				cells[i] = cells[j];
				cells[j] = tmp;
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
					if (get(xx, yy) == 255)
						c++;
				return c;
			}

			for (var y = 0; y < height; y++)
			for (var x = 0; x < width; x++)
				if (result[y, x] != 255)
					result[y, x] = countMines(x, y);
			return result;
		}
	}
}