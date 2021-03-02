using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("fortune")]
    [Description("Gives you a fortune once a day")]
    internal sealed class Fortune : BaseCommandModuleCustom
    {
        [GroupCommand]
        [Cooldown(2, 60, CooldownBucketType.User)]
        [Cooldown(1, 3, CooldownBucketType.Channel)]
        public async Task ShowFortune(CommandContext ctx)
        {
            var prefix = DateTime.UtcNow.ToString("yyyyMMdd");
            using var hmac = new System.Security.Cryptography.HMACSHA256();
            var data = Encoding.UTF8.GetBytes(prefix + ctx.User.Id.ToString("x16"));
            var hash = hmac.ComputeHash(data);
            var seed = BitConverter.ToInt32(hash, 0);
            var rng = new Random(seed);
            await using var db = new ThumbnailDb();
            Database.Fortune fortune;
            do
            {
                var totalFortunes = await db.Fortune.CountAsync().ConfigureAwait(false);
                if (totalFortunes == 0)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "There are no fortunes to tell", true).ConfigureAwait(false);
                    return;
                }
                
                var selectedId = rng.Next(totalFortunes);
                fortune = await db.Fortune.AsNoTracking().Skip(selectedId).FirstOrDefaultAsync().ConfigureAwait(false);
            } while (fortune is null);

            var msg = fortune.Content.FixTypography();
            var msgParts = msg.Split('\n');
            if (msgParts.Length > 1 && msgParts[^1].StartsWith("    "))
                msg = string.Join('\n', msgParts[..^2].Select(l => "> " + l)) + "\n" + msgParts[^1].FixSpaces();
            else
                msg = "> " + msg;
            await ctx.RespondAsync($"{ctx.User.Mention}, your fortune for today:\n{msg}").ConfigureAwait(false);
        }

        [Command("add"), RequiresBotModRole]
        [Description("Add a new fortune")]
        public async Task Add(CommandContext ctx, [RemainingText] string text)
        {
            text = text.Replace("\r\n", "\n").Trim();
            if (text.Length > 1800)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Fortune text is too long", true).ConfigureAwait(false);
                return;
            }
            
            await using var db = new ThumbnailDb();
            await db.Fortune.AddAsync(new() {Content = text}).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete"), RequiresBotModRole]
        [Description("Removes fortune with specified ID")]
        public async Task Remove(CommandContext ctx, int id)
        {
            await using var db = new ThumbnailDb();
            var fortune = await db.Fortune.FirstOrDefaultAsync(f => f.Id == id).ConfigureAwait(false);
            if (fortune is null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Fortune with id {id} wasn't found", true).ConfigureAwait(false);
                return;
            }

            db.Fortune.Remove(fortune);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        }

        [Command("import"), Aliases("append"), RequiresBotModRole]
        [Description("Imports new fortunes from specified URL or attachment. Data should be formatted as standard UNIX fortune source file.")]
        public async Task Import(CommandContext ctx, string? url = null)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) 
                    url = ctx.Message.Attachments.FirstOrDefault()?.Url;

                if (string.IsNullOrEmpty(url))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    return;
                }

                await using var db = new ThumbnailDb();
                using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                await using var stream = await httpClient.GetStreamAsync(url, Config.Cts.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                var buf = new StringBuilder();
                string? line;
                int count = 0, skipped = 0;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null && !Config.Cts.IsCancellationRequested)
                {
                    line = line.Trim();
                    if (line == "%")
                    {
                        var content = buf.ToString().Replace("\r\n", "\n").Trim();
                        if (content.Length > 1900)
                        {
                            skipped++;
                            continue;
                        }
                        
                        await db.Fortune.AddAsync(new() {Content = content}).ConfigureAwait(false);
                        buf.Clear();
                        count++;
                    }
                    else
                        buf.AppendLine(line);
                }
                if (buf.Length > 0)
                {
                    var content = buf.ToString().Replace("\r\n", "\n").Trim();
                    await db.Fortune.AddAsync(new() {Content = content}).ConfigureAwait(false);
                }
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                var result = $"Imported {count} fortune{(count == 1 ? "" : "s")}";
                if (skipped > 0)
                    result += $", skipped {skipped}";
                await ctx.ReactWithAsync(Config.Reactions.Success, result).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to import data: " + e.Message).ConfigureAwait(false);
            }
        }

        [Command("export"), RequiresBotModRole]
        [Description("Exports fortune database into UNIX fortune source format file")]
        public async Task Export(CommandContext ctx)
        {
            try
            {
                var count = 0;
                await using var outputStream = Config.MemoryStreamManager.GetStream();
                await using var compressor = new GZipStream(outputStream, CompressionLevel.Optimal, true);
                await using var writer = new StreamWriter(compressor);
                await using var db = new ThumbnailDb();
                foreach (var fortune in db.Fortune.AsNoTracking())
                {
                    if (Config.Cts.Token.IsCancellationRequested)
                        break;
                    
                    await writer.WriteAsync(fortune.Content).ConfigureAwait(false);
                    await writer.WriteAsync("\n%\n").ConfigureAwait(false);
                    count++;
                }
                await writer.FlushAsync().ConfigureAwait(false);
                await compressor.FlushAsync().ConfigureAwait(false);
                outputStream.Seek(0, SeekOrigin.Begin);
                var builder = new DiscordMessageBuilder()
                    .WithContent($"Exported {count} fortune{(count == 1 ? "": "s")}")
                    .WithFile("fortunes.txt", outputStream);
                await ctx.RespondAsync(builder).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to export data: " + e.Message).ConfigureAwait(false);
            }
        }

        [Command("clear"), RequiresBotModRole]
        [Description("Clears fortune database. Use with caution")]
        public async Task Clear(CommandContext ctx, [RemainingText, Description("Must be `with my blessing, I swear I exported the backup`")] string confirmation)
        {
            if (confirmation != "with my blessing, I swear I exported the backup")
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                return;
            }

            await using var db = new ThumbnailDb();
            db.Fortune.RemoveRange(db.Fortune);
            var count = await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success, $"Removed {count} fortune{(count == 1 ? "" : "s")}", true).ConfigureAwait(false);
        }
    }
}