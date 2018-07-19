using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    internal partial class Sudo
    {
        [Group("bot")]
        [Description("Commands to manage the bot instance")]
        public sealed class Bot: BaseCommandModule
        {

            [Command("version")]
            [Description("Returns currently checked out bot commit")]
            public async Task Version(CommandContext ctx)
            {
                var typingTask = ctx.TriggerTypingAsync();
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
                await typingTask.ConfigureAwait(false);
            }

            [Command("restart"), Aliases("update")]
            [Description("Restarts bot and pulls newest commit")]
            public async Task Restart(CommandContext ctx)
            {
                var typingTask = ctx.TriggerTypingAsync();
                if (Monitor.TryEnter(updateObj))
                {
                    try
                    {
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
                                await ctx.RespondAsync("```" + stdout + "```").ConfigureAwait(false);
                        }
                        await ctx.RespondAsync("Restarting...").ConfigureAwait(false);
                        using (var self = new Process
                        {
#if DEBUG
                            StartInfo = new ProcessStartInfo("dotnet", $"run -- {Config.Token} {ctx.Channel.Id}")
#else
                            StartInfo = new ProcessStartInfo("dotnet", $"run -c Release -- {Config.Token} {ctx.Channel.Id}")
#endif
                        })
                        {
                            self.Start();
                            Config.Cts.Cancel();
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        await ctx.RespondAsync("Updating failed: " + e.Message).ConfigureAwait(false);
                    }
                    finally
                    {
                        Monitor.Exit(updateObj);
                    }
                }
                else
                    await ctx.RespondAsync("Update is already in progress").ConfigureAwait(false);
                await typingTask.ConfigureAwait(false);
            }

            [Command("stop"), Aliases("exit", "shutdown", "terminate")]
            [Description("Stops the bot. Useful if you can't find where you left one running")]
            public async Task Stop(CommandContext ctx)
            {
                await ctx.RespondAsync(ctx.Channel.IsPrivate
                    ? $"Shutting down bot instance on {Environment.MachineName}..."
                    : "Shutting down the bot..."
                ).ConfigureAwait(false);
                Config.Cts.Cancel();
            }

        }
    }
}
