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

namespace CompatBot.Commands
{
    internal partial class Sudo
    {
        private static readonly SemaphoreSlim lockObj = new SemaphoreSlim(1, 1);

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
                    StartInfo = new ProcessStartInfo("git", "log -1 --oneline")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8,
                    },
                };
                git.Start();
                var stdout = await git.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                git.WaitForExit();
                if (!string.IsNullOrEmpty(stdout))
                    await ctx.RespondAsync("```" + stdout + "```").ConfigureAwait(false);
            }

            [Command("update"), Aliases("upgrade", "pull")]
            [Description("Updates the bot, and then restarts it")]
            public async Task Update(CommandContext ctx)
            {
                if (await lockObj.WaitAsync(0).ConfigureAwait(false))
                {
                    DiscordMessage msg = null;
                    try
                    {
                        Config.Log.Info("Checking for available updates...");
                        msg = await ctx.RespondAsync("Saving state...").ConfigureAwait(false);
                        await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Checking for updates...").ConfigureAwait(false);
                        var (updated, stdout) = await UpdateAsync().ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(stdout))
                            await ctx.SendAutosplitMessageAsync("```" + stdout + "```").ConfigureAwait(false);
                        if (!updated)
                            return;

                        msg = await ctx.RespondAsync("Restarting...").ConfigureAwait(false);
                        Restart(ctx.Channel.Id, "Restarted after successful bot update");
                    }
                    catch (Exception e)
                    {
                        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Updating failed: " + e.Message).ConfigureAwait(false);
                    }
                    finally
                    {
                        lockObj.Release();
                    }
                }
                else
                    await ctx.RespondAsync("Update is already in progress").ConfigureAwait(false);
            }

            [Command("restart"), Aliases("reboot")]
            [Description("Restarts the bot")]
            public async Task Restart(CommandContext ctx)
            {
                if (await lockObj.WaitAsync(0).ConfigureAwait(false))
                {
                    DiscordMessage msg = null;
                    try
                    {
                        msg = await ctx.RespondAsync("Saving state...").ConfigureAwait(false);
                        await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Restarting...").ConfigureAwait(false);
                        Restart(ctx.Channel.Id, "Restarted due to command request");
                    }
                    catch (Exception e)
                    {
                        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Restarting failed: " + e.Message).ConfigureAwait(false);
                    }
                    finally
                    {
                        lockObj.Release();
                    }
                }
                else
                    await ctx.RespondAsync("Update is in progress").ConfigureAwait(false);
            }

            [Command("stop"), Aliases("exit", "shutdown", "terminate")]
            [Description("Stops the bot. Useful if you can't find where you left one running")]
            public async Task Stop(CommandContext ctx)
            {
                await ctx.RespondAsync(ctx.Channel.IsPrivate
                    ? $"Shutting down bot instance on {Environment.MachineName}..."
                    : "Shutting down the bot..."
                ).ConfigureAwait(false);
                Config.Log.Info($"Shutting down by request from {ctx.User.Username}#{ctx.User.Discriminator}");
                Config.inMemorySettings["shutdown"] = "true";
                Config.Cts.Cancel();
            }

            [Command("status")]
            [Description("Sets bot status with specified activity and message")]
            public async Task Status(CommandContext ctx, [Description("One of: None, Playing, Watching or ListeningTo")] string activity, [RemainingText] string message)
            {
                try
                {
                    using var db = new BotDb();
                    var status = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-activity").ConfigureAwait(false);
                    var txt = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-text").ConfigureAwait(false);
                    if (Enum.TryParse(activity, true, out ActivityType activityType)
                        && !string.IsNullOrEmpty(message))
                    {
                        if (status == null)
                            await db.BotState.AddAsync(new BotState {Key = "bot-status-activity", Value = activity}).ConfigureAwait(false);
                        else
                            status.Value = activity;
                        if (txt == null)
                            await db.BotState.AddAsync(new BotState {Key = "bot-status-text", Value = message}).ConfigureAwait(false);
                        else
                            txt.Value = message;
                        await ctx.Client.UpdateStatusAsync(new DiscordActivity(message, activityType), UserStatus.Online).ConfigureAwait(false);
                    }
                    else
                    {
                        if (status != null)
                            db.BotState.Remove(status);
                        await ctx.Client.UpdateStatusAsync(new DiscordActivity()).ConfigureAwait(false);
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
                await CompatList.ImportMetacriticScoresAsync().ConfigureAwait(false);
                using var db = new ThumbnailDb();
                var linkedItems = await db.Thumbnail.CountAsync(i => i.MetacriticId != null).ConfigureAwait(false);
                await ctx.RespondAsync($"Importing Metacritic info was successful, linked {linkedItems} items").ConfigureAwait(false);
            }

            internal static async Task<(bool updated, string stdout)> UpdateAsync()
            {
                using var git = new Process
                {
                    StartInfo = new ProcessStartInfo("git", "pull")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8,
                    },
                };
                git.Start();
                var stdout = await git.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                git.WaitForExit();
                if (string.IsNullOrEmpty(stdout))
                    return (false, stdout);

                if (stdout.Contains("Already up to date", StringComparison.InvariantCultureIgnoreCase))
                    return (false, stdout);

                return (true, stdout);
            }

            internal static void Restart(ulong channelId, string restartMsg)
            {
                Config.Log.Info($"Saving channelId {channelId} into settings...");
                using var db = new BotDb();
                var ch = db.BotState.FirstOrDefault(k => k.Key == "bot-restart-channel");
                if (ch is null)
                {
                    ch = new BotState {Key = "bot-restart-channel", Value = channelId.ToString()};
                    db.BotState.Add(ch);
                }
                else
                    ch.Value = channelId.ToString();
                var msg = db.BotState.FirstOrDefault(k => k.Key == "bot-restart-msg");
                if (msg is null)
                {
                    msg = new BotState {Key = "bot-restart-msg", Value = restartMsg};
                    db.BotState.Add(msg);
                }
                else
                    msg.Value = restartMsg;
                db.SaveChanges();
                RestartNoSaving();
            }

            private static void RestartNoSaving()
            {
                Config.Log.Info("Restarting...");
                using var self = new Process
                {
                    StartInfo = new ProcessStartInfo("dotnet", $"run -c Release")
                };
                self.Start();
                Config.inMemorySettings["shutdown"] = "true";
                Config.Cts.Cancel();
            }
        }
    }
}
