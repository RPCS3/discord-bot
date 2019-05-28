using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal partial class Sudo
    {
        private static readonly SemaphoreSlim lockObj = new SemaphoreSlim(1, 1);

        [Group("bot")]
        [Description("Commands to manage the bot instance")]
        public sealed partial class Bot: BaseCommandModuleCustom
        {
            [Command("version")]
            [Description("Returns currently checked out bot commit")]
            public async Task Version(CommandContext ctx)
            {
                using (var git = new Process
                {
                    StartInfo = new ProcessStartInfo("git", "log -1 --oneline")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8,
                    },
                })
                {
                    git.Start();
                    var stdout = await git.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    git.WaitForExit();
                    if (!string.IsNullOrEmpty(stdout))
                        await ctx.RespondAsync("```" + stdout + "```").ConfigureAwait(false);
                }
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
                        msg = await ctx.RespondAsync("Saving state...").ConfigureAwait(false);
                        await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Checking for updates...").ConfigureAwait(false);
                        using (var git = new Process
                        {
                            StartInfo = new ProcessStartInfo("git", "pull")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                StandardOutputEncoding = Encoding.UTF8,
                            },
                        })
                        {
                            git.Start();
                            var stdout = await git.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                            git.WaitForExit();
                            if (!string.IsNullOrEmpty(stdout))
                            {
                                await ctx.SendAutosplitMessageAsync("```" + stdout + "```").ConfigureAwait(false);
                                if (stdout.Contains("Already up to date", StringComparison.InvariantCultureIgnoreCase))
                                    return;
                            }
                        }
                        msg = await ctx.RespondAsync("Restarting...").ConfigureAwait(false);
                        Restart(ctx.Channel.Id);
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
                        Restart(ctx.Channel.Id);
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

            internal static void Restart(ulong channelId)
            {
                Config.Log.Info("Restarting...");
                using (var self = new Process
                {
#if DEBUG
                    StartInfo = new ProcessStartInfo("dotnet", $"run -- {Config.Token} {channelId}")
#else
                    StartInfo = new ProcessStartInfo("dotnet", $"run -c Release -- {Config.Token} {channelId}")
#endif
                })
                {
                    self.Start();
                    Config.inMemorySettings["shutdown"] = "true";
                    Config.Cts.Cancel();
                    return;
                }
            }
        }
    }
}
