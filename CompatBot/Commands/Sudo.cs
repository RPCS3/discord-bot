using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Commands.Converters;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("sudo"), RequiresBotSudoerRole]
    [Description("Used to manage bot moderators and sudoers")]
    internal sealed partial class Sudo : BaseCommandModuleCustom
    {
        [Command("say"), Priority(10)]
        [Description("Make bot say things, optionally in a specific channel")]
        public async Task Say(CommandContext ctx, [Description("Discord channel (can use just #name in DM)")] DiscordChannel channel, [RemainingText, Description("Message text to send")] string message)
        {
            if (channel.Type != ChannelType.Text)
            {
                Config.Log.Warn($"Resolved a {channel.Type} channel again for #{channel.Name}");
                var channelResult = await new TextOnlyDiscordChannelConverter().ConvertAsync(channel.Name, ctx).ConfigureAwait(false);
                if (channelResult.HasValue && channelResult.Value.Type == ChannelType.Text)
                    channel = channelResult.Value;
                else
                {
                    await ctx.RespondAsync($"Resolved a {channel.Type} channel again").ConfigureAwait(false);
                    return;
                }
            }

            var typingTask = channel.TriggerTypingAsync();
            // simulate bot typing the message at 300 cps
            await Task.Delay(message.Length * 10 / 3).ConfigureAwait(false);
            await channel.SendMessageAsync(message).ConfigureAwait(false);
            await typingTask.ConfigureAwait(false);
        }

        [Command("say"), Priority(10)]
        [Description("Make bot say things, optionally in a specific channel")]
        public Task Say(CommandContext ctx, [RemainingText, Description("Message text to send")] string message)
            => Say(ctx, ctx.Channel, message);

        [Command("react")]
        [Description("Add reactions to the specified message")]
        public async Task React(
            CommandContext ctx,
            [Description("Message link")] string messageLink,
            [RemainingText, Description("List of reactions to add")]string emojis
        )
        {
            try
            {
                var message = await ctx.GetMessageAsync(messageLink).ConfigureAwait(false);
                if (message is null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't find the message").ConfigureAwait(false);
                    return;
                }

                string emoji = "";
                for (var i = 0; i < emojis.Length; i++)
                {
                    try
                    {
                        var c = emojis[i];
                        if (char.IsHighSurrogate(c))
                            emoji += c;
                        else
                        {
                            DiscordEmoji de;
                            if (c == '<')
                            {
                                var endIdx = emojis.IndexOf('>', i);
                                if (endIdx < i)
                                    endIdx = emojis.Length;
                                emoji = emojis[i..endIdx];
                                i = endIdx - 1;
                                var emojiId = ulong.Parse(emoji[(emoji.LastIndexOf(':') + 1)..]);
                                de = DiscordEmoji.FromGuildEmote(ctx.Client, emojiId);
                            }
                            else
                                de = DiscordEmoji.FromUnicode(emoji + c);
                            emoji = "";
                            await message.ReactWithAsync(de).ConfigureAwait(false);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Config.Log.Debug(e);
            }
        }

        [Command("log"), RequiresDm]
        [Description("Uploads current log file as an attachment")]
        public async Task Log(CommandContext ctx, [Description("Specific date")]string date = "")
        {
            try
            {
                var logPath = Config.CurrentLogPath;
                if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var logDate))
                    logPath = Path.Combine(Config.LogPath, $"bot.{logDate:yyyyMMdd}.0.log");
                if (!File.Exists(logPath))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Log file does not exist for specified day", true).ConfigureAwait(false);
                    return;
                }
                
                var attachmentSizeLimit = Config.AttachmentSizeLimit;
                await using var log = File.Open(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await using var result = Config.MemoryStreamManager.GetStream();
                await using var gzip = new GZipStream(result, CompressionLevel.Optimal, true);
                await log.CopyToAsync(gzip, Config.Cts.Token).ConfigureAwait(false);
                await gzip.FlushAsync().ConfigureAwait(false);
                if (result.Length <= attachmentSizeLimit)
                {
                    result.Seek(0, SeekOrigin.Begin);
                    await ctx.RespondAsync(new DiscordMessageBuilder().WithFile(Path.GetFileName(logPath) + ".gz", result)).ConfigureAwait(false);
                }
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Compressed log size is too large, ask 13xforever for help :(", true).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to upload current log");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to send the log", true).ConfigureAwait(false);
            }
        }

        [Command("dbbackup"), Aliases("thumbs", "dbb")]
        [Description("Uploads current Thumbs.db file as an attachment")]
        public async Task ThumbsBackup(CommandContext ctx)
        {
            try
            {
                string dbPath;
                await using (var db = new ThumbnailDb())
                await using (var connection = db.Database.GetDbConnection())
                    dbPath = connection.DataSource;
                var attachmentSizeLimit = Config.AttachmentSizeLimit;
                var dbDir = Path.GetDirectoryName(dbPath) ?? ".";
                var dbName = Path.GetFileNameWithoutExtension(dbPath);
                await using var result = Config.MemoryStreamManager.GetStream();
                using var zip = new ZipArchive(result, ZipArchiveMode.Create, true);
                foreach (var fname in Directory.EnumerateFiles(dbDir, $"{dbName}.*", new EnumerationOptions {IgnoreInaccessible = true, RecurseSubdirectories = false,}))
                {
                    await using var dbData = File.Open(fname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await using var entryStream = zip.CreateEntry(Path.GetFileName(fname), CompressionLevel.Optimal).Open();
                    await dbData.CopyToAsync(entryStream, Config.Cts.Token).ConfigureAwait(false);
                    await entryStream.FlushAsync().ConfigureAwait(false);
                }
                if (result.Length <= attachmentSizeLimit)
                {
                    result.Seek(0, SeekOrigin.Begin);
                    await ctx.RespondAsync(new DiscordMessageBuilder().WithFile(Path.GetFileName(dbName) + ".zip", result)).ConfigureAwait(false);
                }
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Compressed Thumbs.db size is too large, ask 13xforever for help :(", true).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to upload current Thumbs.db backup");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to send Thumbs.db backup", true).ConfigureAwait(false);
            }
        }

        [Command("gen-salt")]
        [Description("Regenerates salt for data anonymization purposes")]
        public Task ResetCryptoSalt(CommandContext ctx)
        {
            var salt = new byte[256 / 8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
            return new Bot.Configuration().Set(ctx, nameof(Config.CryptoSalt), Convert.ToBase64String(salt));
        }
    }
}
