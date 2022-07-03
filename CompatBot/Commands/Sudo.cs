using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Commands.Converters;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace CompatBot.Commands;

[Group("sudo"), RequiresBotSudoerRole]
[Description("Used to manage bot moderators and sudoers")]
internal sealed partial class Sudo : BaseCommandModuleCustom
{
    [Command("say")]
    [Description("Make bot say things. Specify #channel or put message link in the beginning to specify where to reply")]
    public async Task Say(CommandContext ctx, [RemainingText, Description("Message text to send")] string message)
    {
        var msgParts = message.Split(' ', 2, StringSplitOptions.TrimEntries);

        var channel = ctx.Channel;
        DiscordMessage? ogMsg = null;
        if (msgParts.Length > 1)
        {
            if (await ctx.GetMessageAsync(msgParts[0]).ConfigureAwait(false) is DiscordMessage lnk)
            {
                ogMsg = lnk;
                channel = ogMsg.Channel;
                message = msgParts[1];
            }
            else if (await TextOnlyDiscordChannelConverter.ConvertAsync(msgParts[0], ctx).ConfigureAwait(false) is {HasValue: true} ch)
            {
                channel = ch.Value;
                message = msgParts[1];
            }
        }

        var typingTask = channel.TriggerTypingAsync();
        // simulate bot typing the message at 300 cps
        await Task.Delay(message.Length * 10 / 3).ConfigureAwait(false);
        var msgBuilder = new DiscordMessageBuilder().WithContent(message);
        if (ogMsg is not null)
            msgBuilder.WithReply(ogMsg.Id);
        if (ctx.Message.Attachments.Any())
        {
            try
            {
                await using var memStream = Config.MemoryStreamManager.GetStream();
                using var client = HttpClientFactory.Create(new CompressionMessageHandler());
                await using var requestStream = await client.GetStreamAsync(ctx.Message.Attachments[0].Url!).ConfigureAwait(false);
                await requestStream.CopyToAsync(memStream).ConfigureAwait(false);
                memStream.Seek(0, SeekOrigin.Begin);
                msgBuilder.WithFile(ctx.Message.Attachments[0].FileName, memStream);
                await channel.SendMessageAsync(msgBuilder).ConfigureAwait(false);
            }
            catch { }
        }
        else
            await channel.SendMessageAsync(msgBuilder).ConfigureAwait(false);
        await typingTask.ConfigureAwait(false);
    }

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
            Config.Log.Factory.Flush();
            var logPath = Config.CurrentLogPath;
            if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var logDate))
                logPath = Path.Combine(Config.LogPath, $"bot.{logDate:yyyyMMdd}.*.log");
            if (!File.Exists(logPath))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Log file does not exist for specified day", true).ConfigureAwait(false);
                return;
            }
                
            await using var result = Config.MemoryStreamManager.GetStream();
            using (var zip = new ZipWriter(result, new(CompressionType.LZMA){DeflateCompressionLevel = CompressionLevel.Default}))
                foreach (var fname in Directory.EnumerateFiles(Config.LogPath, Path.GetFileName(logPath), new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, }))
                {
                    await using var log = File.Open(fname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    zip.Write(Path.GetFileName(fname), log);
                }

            if (result.Length <= ctx.GetAttachmentSizeLimit())
            {
                result.Seek(0, SeekOrigin.Begin);
                await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().WithFile(Path.GetFileName(logPath) + ".zip", result)).ConfigureAwait(false);
            }
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Compressed log size is too large, ask 13xforever for help :(", true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to upload current log");
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to send the log\n{e}".Trim(EmbedPager.MaxMessageLength), true).ConfigureAwait(false);
        }
    }

    [Command("dbbackup"), Aliases("dbb"), TriggersTyping]
    [Description("Uploads current Thumbs.db and Hardware.db files as an attachments")]
    public async Task DbBackup(CommandContext ctx, [Description("Name of the database")]string name = "")
    {
        name = name.ToLower();
        if (name.EndsWith(".db"))
            name = name[..^3];
        if (name != "hw")
            await using (var db = new ThumbnailDb())
                await BackupDb(ctx, db).ConfigureAwait(false);
        if (name != "thumbs")
            await using (var db = new HardwareDb())
                await BackupDb(ctx, db).ConfigureAwait(false);
    }
    
    private static async Task BackupDb(CommandContext ctx, DbContext db)
    {
        string? dbName = null;
        try
        {
            await using var botDb = new BotDb();
            string dbPath, dbDir;
            await using (var connection = db.Database.GetDbConnection())
            {
                dbPath = connection.DataSource;
                dbDir = Path.GetDirectoryName(dbPath) ?? ".";
                dbName = Path.GetFileNameWithoutExtension(dbPath);

                var tsName = "db-vacuum-" + dbName;
                var vacuumTs = await botDb.BotState.FirstOrDefaultAsync(v => v.Key == tsName).ConfigureAwait(false);
                if (vacuumTs?.Value is null
                    || (long.TryParse(vacuumTs.Value, out var vtsTicks)
                        && vtsTicks < DateTime.UtcNow.AddDays(-30).Ticks))
                {
                    await db.Database.ExecuteSqlRawAsync("VACUUM;").ConfigureAwait(false);
                    
                    var newTs = DateTime.UtcNow.Ticks.ToString();
                    if (vacuumTs is null)
                        botDb.BotState.Add(new() { Key = tsName, Value = newTs });
                    else
                        vacuumTs.Value = newTs;
                    await botDb.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            await using var result = Config.MemoryStreamManager.GetStream();
            using (var zip = new ZipWriter(result, new(CompressionType.LZMA){DeflateCompressionLevel = CompressionLevel.Default}))
                foreach (var fname in Directory.EnumerateFiles(dbDir, $"{dbName}.*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, }))
                {
                    await using var dbData = File.Open(fname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    zip.Write(Path.GetFileName(fname), dbData);
                }
            if (result.Length <= ctx.GetAttachmentSizeLimit())
            {
                result.Seek(0, SeekOrigin.Begin);
                await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().WithFile(Path.GetFileName(dbName) + ".zip", result)).ConfigureAwait(false);
            }
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Compressed {dbName}.db size is too large, ask 13xforever for help :(", true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to upload {(dbName is null? "DB": dbName + ".db")} backup");
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to send {(dbName is null? "DB": dbName + ".db")} backup", true).ConfigureAwait(false);
        }
    }

    [Command("gen-salt")]
    [Description("Regenerates salt for data anonymization purposes. This WILL affect Hardware DB deduplication.")]
    public async Task ResetCryptoSalt(CommandContext ctx)
    {
        var btnYes = new DiscordButtonComponent(ButtonStyle.Danger, "gen-salt:yes", "Yes, regenerate salt");
        var btnNo = new DiscordButtonComponent(ButtonStyle.Primary, "gen-salt:no", "No, I do not fully understand the consequences");
        var b = new DiscordMessageBuilder()
            .WithContent("This will affect hardware DB data deduplication. Are you sure?")
            .AddComponents(btnYes, btnNo);
        var msg = await ctx.RespondAsync(b).ConfigureAwait(false);
        var interactivity = ctx.Client.GetInteractivity();
        var (txt, reaction) = await interactivity.WaitForMessageOrButtonAsync(msg, ctx.User, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        if (txt?.Content?.ToLowerInvariant() is "y" or "yes" || reaction?.Id == btnYes.CustomId)
        {
            var salt = new byte[256 / 8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
            await new Bot.Configuration().Set(ctx, nameof(Config.CryptoSalt), Convert.ToBase64String(salt)).ConfigureAwait(false);
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Regenerated salt.").ConfigureAwait(false);
        }
        else
        {
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Operation cancelled.").ConfigureAwait(false);
        }
    }
}