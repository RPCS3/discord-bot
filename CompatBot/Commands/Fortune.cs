using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    [Group("fortune"), Aliases("fortunes")]
    [Description("Gives you a fortune once a day")]
    internal sealed class Fortune : BaseCommandModuleCustom
    {
        private static readonly SemaphoreSlim ImportCheck = new(1, 1);

        [GroupCommand]
        [Cooldown(2, 60, CooldownBucketType.User)]
        [Cooldown(1, 3, CooldownBucketType.Channel)]
        public Task ShowFortune(CommandContext ctx)
            => ShowFortune(ctx.Message, ctx.User);
        
        public static async Task ShowFortune(DiscordMessage message, DiscordUser user)
        {
            var prefix = DateTime.UtcNow.ToString("yyyyMMdd")+ user.Id.ToString("x16");
            var rng = new Random(prefix.GetStableHash());
            await using var db = new ThumbnailDb();
            Database.Fortune fortune;
            do
            {
                var totalFortunes = await db.Fortune.CountAsync().ConfigureAwait(false);
                if (totalFortunes == 0)
                {
                    await message.ReactWithAsync(Config.Reactions.Failure, "There are no fortunes to tell", true).ConfigureAwait(false);
                    return;
                }
                
                var selectedId = rng.Next(totalFortunes);
                fortune = await db.Fortune.AsNoTracking().Skip(selectedId).FirstOrDefaultAsync().ConfigureAwait(false);
            } while (fortune is null);

            var msg = fortune.Content.FixTypography();
            var msgParts = msg.Split('\n');
            var tmp = new StringBuilder();
            var quote = true;
            foreach (var l in msgParts)
            {
                quote &= !l.StartsWith("    ");
                if (quote)
                    tmp.Append("> ");
                tmp.Append(l).Append('\n');
            }
            msg = tmp.ToString().TrimEnd().FixSpaces();
            await message.Channel.SendMessageAsync($"{user.Mention}, your fortune for today:\n{msg}").ConfigureAwait(false);
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

        [Command("import"), Aliases("append"), RequiresBotModRole, TriggersTyping]
        [Description("Imports new fortunes from specified URL or attachment. Data should be formatted as standard UNIX fortune source file.")]
        public async Task Import(CommandContext ctx, string? url = null)
        {
            var msg = await ctx.Channel.SendMessageAsync("Please wait...").ConfigureAwait(false);
            if (!await ImportCheck.WaitAsync(0).ConfigureAwait(false))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "There is another import in progress already").ConfigureAwait(false);
                return;
            }
            
            try
            {
                if (string.IsNullOrEmpty(url))
                    url = ctx.Message.Attachments.FirstOrDefault()?.Url;

                if (string.IsNullOrEmpty(url))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    return;
                }

                var stopwatch = Stopwatch.StartNew();
                await using var db = new ThumbnailDb();
                using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await httpClient.SendAsync(request, Config.Cts.Token).ConfigureAwait(false);
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                var buf = new StringBuilder();
                string? line;
                int count = 0, skipped = 0;
                while (!Config.Cts.IsCancellationRequested
                       && ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null
                           || buf.Length > 0)
                       && !Config.Cts.IsCancellationRequested)
                {
                    if (line == "%" || line is null)
                    {
                        var content = buf.ToString().Replace("\r\n", "\n").Trim();
                        if (content.Length > 1900)
                        {
                            buf.Clear();
                            skipped++;
                            continue;
                        }

                        if (db.Fortune.Any(f => f.Content == content))
                        {
                            buf.Clear();
                            skipped++;
                            continue;
                        }

                        var duplicate = false;
                        foreach (var fortune in db.Fortune.AsNoTracking())
                        {
                            if (fortune.Content.GetFuzzyCoefficientCached(content) >= 0.95)
                            {
                                duplicate = true;
                                break;
                            }

                            if (Config.Cts.Token.IsCancellationRequested)
                                break;
                        }
                        if (duplicate)
                        {
                            buf.Clear();
                            skipped++;
                            continue;
                        }

                        await db.Fortune.AddAsync(new() {Content = content}).ConfigureAwait(false);
                        await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                        buf.Clear();
                        count++;
                    }
                    else
                        buf.AppendLine(line);
                    if (line is null)
                        break;

                    if (stopwatch.ElapsedMilliseconds > 10_000)
                    {
                        var progressMsg = $"Imported {count} fortune{(count == 1 ? "" : "s")}";
                        if (skipped > 0)
                            progressMsg += $", skipped {skipped}";
                        if (response.Content.Headers.ContentLength is long len && len > 0)
                            progressMsg += $" ({stream.Position * 100.0 / len:0.##}%)";
                        await msg.UpdateOrCreateMessageAsync(ctx.Channel, progressMsg).ConfigureAwait(false);
                        stopwatch.Restart();
                    }
                }
                var result = $"Imported {count} fortune{(count == 1 ? "" : "s")}";
                if (skipped > 0)
                    result += $", skipped {skipped}";
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, result).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Failed to import data: " + e.Message).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
            }
            finally
            {
                ImportCheck.Release();
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
                await using var writer = new StreamWriter(outputStream);
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
                outputStream.Seek(0, SeekOrigin.Begin);
                var builder = new DiscordMessageBuilder()
                    .WithContent($"Exported {count} fortune{(count == 1 ? "": "s")}")
                    .WithFile("fortunes.txt", outputStream);
                await ctx.Channel.SendMessageAsync(builder).ConfigureAwait(false);
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