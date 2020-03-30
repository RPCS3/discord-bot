using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class NewBuildsMonitor
    {
        private static readonly Regex BuildResult = new Regex("[rpcs3] Build .+ succeeded", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly TimeSpan PassiveCheckInterval = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan ActiveCheckInterval = TimeSpan.FromSeconds(20);
        public static TimeSpan CheckInterval { get; private set; } = PassiveCheckInterval;
        public static DateTime? RapidStart { get; private set; }

        public static Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBotSafeCheck()
                && !args.Author.IsCurrent
                && "github".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase)
                && args.Message.Embeds.FirstOrDefault() is DiscordEmbed embed
                && !string.IsNullOrEmpty(embed.Title)
                && BuildResult.IsMatch(embed.Title)
            )
            {
                Activate();
            }
            return Task.CompletedTask;
        }

        public static async Task MonitorAsync(DiscordClient client)
        {
            var lastCheck = DateTime.UtcNow.AddDays(-1);
            var resetThreshold = TimeSpan.FromMinutes(10);
            Exception lastException = null;
            while (!Config.Cts.IsCancellationRequested)
            {
                if (DateTime.UtcNow - lastCheck > CheckInterval)
                {
                    try
                    {
                        await CompatList.UpdatesCheck.CheckForRpcs3Updates(client, null).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        if (e.GetType() != lastException?.GetType())
                        {
                            Config.Log.Debug(e);
                            lastException = e;
                        }
                    }
                    lastCheck = DateTime.UtcNow;
                    if (DateTime.UtcNow - resetThreshold > RapidStart)
                        Reset();
                }
                await Task.Delay(1000, Config.Cts.Token).ConfigureAwait(false);
            }
        }

        public static void Reset()
        {
            CheckInterval = PassiveCheckInterval;
            RapidStart = null;
        }

        private static void Activate()
        {
            CheckInterval = ActiveCheckInterval;
            RapidStart = DateTime.UtcNow;
        }
    }
}
