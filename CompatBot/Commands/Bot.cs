using System.Diagnostics;
using System.Globalization;
using System.IO;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IO;
using NLog;
using SharpCompress.Common;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace CompatBot.Commands;

[Command("bot"), TextAlias("kot"), RequiresBotSudoerRole, AllowDMUsage]
[Description("Commands to manage the bot instance")]
internal static partial class Bot
{
    private static readonly SemaphoreSlim LockObj = new(1, 1);
    private static readonly SemaphoreSlim ImportLockObj = new(1, 1);
    private static readonly ProcessStartInfo RestartInfo = new("dotnet", $"run -c Release");

    [Command("log")]
    [Description("Upload log file as an attachment")]
    public static async ValueTask Log(
        SlashCommandContext ctx,
        [Description("Specific date (e.g. 2020-01-31)")]string date = ""
    )
    {
        var ephemeral = !ctx.Channel.IsPrivate;
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        try
        {
            LogManager.LogFactory.Flush();
            string[] logPaths = [Config.CurrentLogPath];
            if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var logDate))
            {
                var enumOptions = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, };
                logPaths = Directory.GetFiles(Config.LogPath, $"bot.{logDate:yyyyMMdd}.*.log", enumOptions);
            }
            if (logPaths.Length is 0)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Log files do not exist for specified day", ephemeral: true).ConfigureAwait(false);
                return;
            }
                
            await using var result = Config.MemoryStreamManager.GetStream();
            using var zip = new ZipWriter(
                result,
                new(CompressionType.LZMA) { DeflateCompressionLevel = CompressionLevel.Default }
            );
            foreach (var fname in logPaths)
            {
                await using var log = File.Open(fname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                zip.Write(Path.GetFileName(fname), log);
            }
            if (result.Length <= ctx.GetAttachmentSizeLimit())
            {
                result.Seek(0, SeekOrigin.Begin);
                var response = new DiscordInteractionResponseBuilder()
                    .AddFile(Path.GetFileName(logPaths[0]) + ".zip", result)
                    .AsEphemeral();
                await ctx.RespondAsync(response).ConfigureAwait(false);
            }
            else
                await ctx.RespondAsync($"{Config.Reactions.Failure} Compressed log size is too large, ask <@98072022709456896> for help :(", ephemeral: ephemeral).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to upload current log");
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to send the log: {e.Message}".Trim(EmbedPager.MaxMessageLength), ephemeral).ConfigureAwait(false);
        }
    }

    [Command("backup")]
    [Description("Upload bot database files as attachments")]
    public static async ValueTask DbBackup(
        SlashCommandContext ctx,
        [Description("Name of the database")]
        BackupDbType name = BackupDbType.All
    )
    {
        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        var maxSize = ctx.GetAttachmentSizeLimit();
        var response = new DiscordInteractionResponseBuilder().AsEphemeral();
        var msg = "";
        await using var thumbStream = Config.MemoryStreamManager.GetStream();
        if (name != BackupDbType.Hardware)
        {
            await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
            if (await BackupDbAsync(db, thumbStream, maxSize).ConfigureAwait(false) is { Length: > 0 } error)
                msg += error + '\n';
            else
                response.AddFile("thumbs.db.zip", thumbStream);
        }
        await using var hwStream = Config.MemoryStreamManager.GetStream();
        if (name != BackupDbType.Thumbs)
        {
            await using var db = await HardwareDb.OpenReadAsync().ConfigureAwait(false);
            if (await BackupDbAsync(db, hwStream, maxSize).ConfigureAwait(false) is { Length: > 0 } error)
                msg += error + '\n';
            else
                response.AddFile("hw.db.zip", hwStream);
        }
        if (msg.TrimEnd() is {Length: >0} errorMsg)
            response.WithContent(errorMsg);
        await ctx.RespondAsync(response).ConfigureAwait(false);
    }

    [Command("update"), TextAlias("upgrade", "pull", "pet")]
    [Description("Update the bot, and then restart")]
    public static ValueTask Update(SlashCommandContext ctx) => UpdateCheckAsync(ctx, Config.Cts.Token);

    [Command("restart"), TextAlias("reboot")]
    [Description("Restart the bot")]
    public static async ValueTask Restart(SlashCommandContext ctx)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        if (await LockObj.WaitAsync(0).ConfigureAwait(false))
        {
            try
            {
                await ctx.RespondAsync("Saving state…", ephemeral: ephemeral).ConfigureAwait(false);
                await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                await ctx.EditResponseAsync("Restarting…").ConfigureAwait(false);
                Restart(ctx.Channel.Id, "Restarted due to command request");
            }
            catch (Exception e)
            {
                await ctx.RespondAsync($"Restart failed: {e.Message}".Trim(EmbedPager.MaxMessageLength), ephemeral: true).ConfigureAwait(false);
            }
            finally
            {
                LockObj.Release();
            }
        }
        else
            await ctx.RespondAsync("Update is already in progress", ephemeral: true).ConfigureAwait(false);
    }

    [Command("status")]
    [Description("Set bot status to specified activity and message")]
    public static async ValueTask Status(SlashCommandContext ctx, DiscordActivityType activity, string message)
    {
        try
        {
            await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
            var status = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-activity").ConfigureAwait(false);
            var txt = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-text").ConfigureAwait(false);
            if (message is {Length: >0})
            {
                if (status is null)
                    await db.BotState.AddAsync(new() {Key = "bot-status-activity", Value = activity.ToString().ToLower()}).ConfigureAwait(false);
                else
                    status.Value = activity.ToString().ToLower();
                if (txt is null)
                    await db.BotState.AddAsync(new() {Key = "bot-status-text", Value = message}).ConfigureAwait(false);
                else
                    txt.Value = message;
                await ctx.Client.UpdateStatusAsync(new(message, activity), DiscordUserStatus.Online).ConfigureAwait(false);
            }
            else
            {
                if (status is not null)
                    db.BotState.Remove(status);
                await ctx.Client.UpdateStatusAsync(new()).ConfigureAwait(false);
            }
            await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    public enum BackupDbType
    {
        All,
        Thumbs,
        Hardware,
    }
    
    private static async ValueTask<string?> BackupDbAsync(DbContext db, RecyclableMemoryStream result, int maxSize)
    {
        string? dbName = null;
        try
        {
            await using var botDb = await BotDb.OpenReadAsync().ConfigureAwait(false);
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
            using (var zip = new ZipWriter(result, new(CompressionType.LZMA){DeflateCompressionLevel = CompressionLevel.Default}))
                foreach (var fname in Directory.EnumerateFiles(dbDir, $"{dbName}.*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, }))
                {
                    await using var dbData = File.Open(fname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    zip.Write(Path.GetFileName(fname), dbData);
                }
            result.Seek(0, SeekOrigin.Begin);
            if (result.Length <= maxSize)
                return null;
            return $"{Config.Reactions.Failure} Compressed {dbName}.db size is too large, ask <@98072022709456896> for help :(";
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to backup {(dbName is null? "DB": dbName + ".db")}");
            return $"{Config.Reactions.Failure} Failed to backup {(dbName is null? "DB": dbName + ".db")}";
        }
    }

    internal static async Task UpdateCheckScheduledAsync(CancellationToken cancellationToken)
    {
        do
        {
            await Task.Delay(TimeSpan.FromHours(6), cancellationToken).ConfigureAwait(false);
            await UpdateCheckAsync(null, cancellationToken).ConfigureAwait(false);
        } while (!cancellationToken.IsCancellationRequested);
    }
    
    private static async ValueTask UpdateCheckAsync(SlashCommandContext? ctx, CancellationToken cancellationToken)
    {
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
#pragma warning disable VSTHRD103
        if (!LockObj.Wait(0))
#pragma warning restore VSTHRD103
        {
            Config.Log.Info("Update check is already in progress");
            if (ctx is not null)
                await ctx.RespondAsync("Update is already in progress", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        var ephemeral = !(ctx?.Channel.IsSpamChannel() ?? false);
        try
        {
            Config.Log.Info("Checking for available bot updates…");
            if (ctx is not null)
                await ctx.RespondAsync("Checking for bot updates…", ephemeral: ephemeral).ConfigureAwait(false);
            var (updated, stdout) = await GitPullAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stdout))
            {
                Config.Log.Debug($"git pull output:\n{stdout}");
                if (ctx is { Channel: { } channel } && channel.IsSpamChannel())
                    await channel.SendAutosplitMessageAsync("```" + stdout + "```").ConfigureAwait(false);
            }
            if (!updated)
            {
                if (ctx is not null)
                    await ctx.EditResponseAsync("No updates were found.").ConfigureAwait(false);
                return;
            }

            if (ctx is not null)
                await ctx.EditResponseAsync("Saving state…").ConfigureAwait(false);
            await StatsStorage.SaveAsync(true).ConfigureAwait(false);
            if (ctx is not null)
                await ctx.EditResponseAsync("Restarting…").ConfigureAwait(false);
            Restart(ctx?.Channel.Id ?? Config.BotLogId, "Restarted after successful bot update");
        }
        catch (Exception e)
        {
            Config.Log.Warn($"Updating failed: {e.Message}");
            if (ctx is not null)
                await ctx.EditResponseAsync($"Update failed: {e.Message}").ConfigureAwait(false);
        }
        finally
        {
            LockObj.Release();
        }
    }
    
    internal static async ValueTask<(bool updated, string stdout)> GitPullAsync(CancellationToken cancellationToken)
    {

        var stdout = await GitRunner.Exec("pull", cancellationToken);
        if (string.IsNullOrEmpty(stdout))
            return (false, stdout);

        if (stdout.Contains("Already up to date", StringComparison.InvariantCultureIgnoreCase))
            return (false, stdout);

        return (true, stdout);
    }

    internal static void Restart(ulong channelId, string? restartMsg)
    {
        Config.Log.Info($"Saving channelId {channelId} into settings…");
        using var db = BotDb.OpenWrite();
        var ch = db.BotState.FirstOrDefault(k => k.Key == "bot-restart-channel");
        if (ch is null)
        {
            ch = new() {Key = "bot-restart-channel", Value = channelId.ToString()};
            db.BotState.Add(ch);
        }
        else
            ch.Value = channelId.ToString();
        var msg = db.BotState.FirstOrDefault(k => k.Key == "bot-restart-msg");
        if (msg is null)
        {
            msg = new() {Key = "bot-restart-msg", Value = restartMsg};
            db.BotState.Add(msg);
        }
        else
            msg.Value = restartMsg;
        db.SaveChanges();
        Config.TelemetryClient?.TrackEvent("Restart");
        RestartNoSaving();
    }

    internal static void RestartNoSaving()
    {
        if (SandboxDetector.Detect() != SandboxType.Docker)
        {
            Config.Log.Info("Restarting…");
            LogManager.LogFactory.Flush();
            using var self = new Process {StartInfo = RestartInfo};
            self.Start();
            Config.InMemorySettings["shutdown"] = "true";
            Config.Cts.Cancel();
        }
        Environment.Exit(-1);
    }
}
