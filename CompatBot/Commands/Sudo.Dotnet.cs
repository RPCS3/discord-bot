using System.Diagnostics;
using System.Text.RegularExpressions;
using CompatBot.Database.Providers;

namespace CompatBot.Commands;

internal partial class Sudo
{
    [Group("dotnet")]
    [Description("Commands to manage dotnet")]
    public sealed partial class Dotnet : BaseCommandModuleCustom
    {
        [GeneratedRegex(@"\.NET( Core)? (?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(-.+)?", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
        private static partial Regex DotnetVersionPattern();

        [Command("update"), Aliases("upgrade")]
        [Description("Updates dotnet, and then restarts the bot")]
        public async Task Update(CommandContext ctx, [Description("Dotnet SDK version (e.g. `5.1`)")] string version = "")
        {
            if (await LockObj.WaitAsync(0).ConfigureAwait(false))
            {
                DiscordMessage? msg = null;
                try
                {
                    Config.Log.Info("Checking for available dotnet updates...");
                    msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Checking for dotnet updates...").ConfigureAwait(false);
                    var (updated, stdout) = await UpdateAsync(version).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(stdout))
                        await ctx.SendAutosplitMessageAsync($"```{stdout}```").ConfigureAwait(false);
                    if (!updated)
                        return;

                    msg = await ctx.Channel.SendMessageAsync("Saving state...").ConfigureAwait(false);
                    await StatsStorage.SaveAsync(true).ConfigureAwait(false);
                    msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Restarting...").ConfigureAwait(false);
                    Bot.Restart(ctx.Channel.Id, "Restarted after successful dotnet update");
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

        private static async Task<(bool updated, string stdout)> UpdateAsync(string version)
        {
            using var aptUpdate = new Process
            {
                StartInfo = new("apt-get", "update")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                },
            };
            aptUpdate.Start();
            await aptUpdate.WaitForExitAsync().ConfigureAwait(false);

            if (string.IsNullOrEmpty(version))
            {
                var versionMatch = DotnetVersionPattern().Match(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                if (!versionMatch.Success)
                    throw new InvalidOperationException("Failed to resolve required dotnet sdk version");
                    
                version = $"{versionMatch.Groups["major"].Value}.{versionMatch.Groups["minor"].Value}";
            }
            using var aptUpgrade = new Process
            {
                StartInfo = new("apt-get", $"-y --allow-unauthenticated --only-upgrade install dotnet-sdk-{version}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                },
            };
            aptUpgrade.Start();
            var stdout = await aptUpgrade.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await aptUpgrade.WaitForExitAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(stdout))
                return (false, stdout);

            if (!stdout.Contains("dotnet-sdk-"))
                return (false, stdout);
                
            //var resultsMatch = Regex.Match(stdout, @"(?<upgraded>\d+) upgraded, (?<installed>\d+) newly installed");
            if (stdout.Contains("is already the newest version", StringComparison.InvariantCultureIgnoreCase))
                return (false, stdout);

            return (true, stdout);
        }
    }
}