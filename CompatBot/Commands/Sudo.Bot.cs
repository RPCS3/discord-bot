using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal partial class Sudo
{
    private static readonly SemaphoreSlim LockObj = new(1, 1);
    private static readonly SemaphoreSlim ImportLockObj = new(1, 1);
    private static readonly ProcessStartInfo RestartInfo = new("dotnet", $"run -c Release");

    [Group("bot"), Aliases("kot")]
    [Description("Commands to manage the bot instance")]
    public sealed partial class Bot: BaseCommandModuleCustom
    {
        [Command("version")]
        [Description("Returns currently checked out bot commit")]
        public async Task Version(CommandContext ctx)
        {
            using var git = new Process
            {
                StartInfo = new("git", "log -1 --oneline")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                },
            };
            git.Start();
            var stdout = await git.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await git.WaitForExitAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stdout))
                await ctx.Channel.SendMessageAsync("```" + stdout + "```").ConfigureAwait(false);
        }

        [Command("update"), Aliases("upgrade", "pull", "pet")]
        [Description("Updates the bot, and then restarts it")]
        public async Task Update(CommandContext ctx)
        {
            if (await LockObj.WaitAsync(0).ConfigureAwait(false))
            {
                DiscordMessage? msg = null;
                try
                {
                    Config.Log.Info("Checking for available bot updates...");
                    msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Checking for bot updates...").ConfigureAwait(false);
                    var (updated, stdout) = await UpdateAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(stdout))
                        await ctx.SendAutosplitMessageAsync("```" + stdout + "```").ConfigureAwait(false);
                    if (!updated)
                        return;

                    msg = await ctx.Channel.SendMessageAsync("Saving state...").ConfigureAwait(false);
                    await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                    msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Restarting...").ConfigureAwait(false);
                    Restart(ctx.Channel.Id, "Restarted after successful bot update");
                }
                catch (Exception e)
                {
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Updating failed: " + e.Message).ConfigureAwait(false);
                }
                finally
                {
                    LockObj.Release();
                }
            }
            else
                await ctx.Channel.SendMessageAsync("Update is already in progress").ConfigureAwait(false);
        }

        [Command("restart"), Aliases("reboot")]
        [Description("Restarts the bot")]
        public async Task Restart(CommandContext ctx)
        {
            if (await LockObj.WaitAsync(0).ConfigureAwait(false))
            {
                DiscordMessage? msg = null;
                try
                {
                    msg = await ctx.Channel.SendMessageAsync("Saving state...").ConfigureAwait(false);
                    await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                    msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Restarting...").ConfigureAwait(false);
                    Restart(ctx.Channel.Id, "Restarted due to command request");
                }
                catch (Exception e)
                {
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Restarting failed: " + e.Message).ConfigureAwait(false);
                }
                finally
                {
                    LockObj.Release();
                }
            }
            else
                await ctx.Channel.SendMessageAsync("Update is in progress").ConfigureAwait(false);
        }

        [Command("stop"), Aliases("exit", "shutdown", "terminate")]
        [Description("Stops the bot. Useful if you can't find where you left one running")]
        public async Task Stop(CommandContext ctx)
        {
            await ctx.Channel.SendMessageAsync(ctx.Channel.IsPrivate
                ? $"Shutting down bot instance on {Environment.MachineName}..."
                : "Shutting down the bot..."
            ).ConfigureAwait(false);
            Config.Log.Info($"Shutting down by request from {ctx.User.Username}#{ctx.User.Discriminator}");
            Config.InMemorySettings["shutdown"] = "true";
            Config.Cts.Cancel();
        }

        [Command("status")]
        [Description("Sets bot status with specified activity and message")]
        public async Task Status(CommandContext ctx, [Description("One of: None, Playing, Watching or ListeningTo")] string activity, [RemainingText] string message)
        {
            try
            {
                await using var db = new BotDb();
                var status = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-activity").ConfigureAwait(false);
                var txt = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-text").ConfigureAwait(false);
                if (Enum.TryParse(activity, true, out ActivityType activityType)
                    && !string.IsNullOrEmpty(message))
                {
                    if (status == null)
                        await db.BotState.AddAsync(new() {Key = "bot-status-activity", Value = activity}).ConfigureAwait(false);
                    else
                        status.Value = activity;
                    if (txt == null)
                        await db.BotState.AddAsync(new() {Key = "bot-status-text", Value = message}).ConfigureAwait(false);
                    else
                        txt.Value = message;
                    await ctx.Client.UpdateStatusAsync(new(message, activityType), UserStatus.Online).ConfigureAwait(false);
                }
                else
                {
                    if (status != null)
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

        [Command("import_metacritic"), Aliases("importmc", "imc"), TriggersTyping]
        [Description("Imports Metacritic database dump and links it to existing items")]
        public async Task ImportMc(CommandContext ctx)
        {
            if (await ImportLockObj.WaitAsync(0).ConfigureAwait(false))
                try
                {
                    await CompatList.ImportMetacriticScoresAsync().ConfigureAwait(false);
                    await using var db = new ThumbnailDb();
                    var linkedItems = await db.Thumbnail.CountAsync(i => i.MetacriticId != null).ConfigureAwait(false);
                    await ctx.Channel.SendMessageAsync($"Importing Metacritic info was successful, linked {linkedItems} items").ConfigureAwait(false);
                }
                finally
                {
                    ImportLockObj.Release();
                }
            else
                await ctx.Channel.SendMessageAsync("Another import operation is already in progress").ConfigureAwait(false);
        }

        internal static async Task<(bool updated, string stdout)> UpdateAsync()
        {
            using var git = new Process
            {
                StartInfo = new("git", "pull")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                },
            };
            git.Start();
            var stdout = await git.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await git.WaitForExitAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(stdout))
                return (false, stdout);

            if (stdout.Contains("Already up to date", StringComparison.InvariantCultureIgnoreCase))
                return (false, stdout);

            return (true, stdout);
        }

        internal static void Restart(ulong channelId, string? restartMsg)
        {
            Config.Log.Info($"Saving channelId {channelId} into settings...");
            using var db = new BotDb();
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
                Config.Log.Info("Restarting...");
                using var self = new Process {StartInfo = RestartInfo};
                self.Start();
                Config.InMemorySettings["shutdown"] = "true";
                Config.Cts.Cancel();
            }
            Environment.Exit(-1);
        }
    }
}