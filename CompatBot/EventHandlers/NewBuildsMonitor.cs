using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class NewBuildsMonitor
    {
        private static readonly Regex BuildResult = new Regex("build (succeed|pass)ed", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly TimeSpan PassiveCheckInterval = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan ActiveCheckInterval = TimeSpan.FromSeconds(20);
        public static TimeSpan CheckInterval { get; private set; } = PassiveCheckInterval;
        public static DateTime? RapidStart { get; private set; }

        public static Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBotSafeCheck()
                && !args.Author.IsCurrent
                && "github".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase)
                && !string.IsNullOrEmpty(args.Message.Content)
                && BuildResult.IsMatch(args.Message.Content)
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
            try
            {
                while (!Config.Cts.IsCancellationRequested)
                {
                    try
                    {
                        if (DateTime.UtcNow - lastCheck > CheckInterval)
                        {
                            await CompatList.UpdatesCheck.CheckForRpcs3Updates(client, null).ConfigureAwait(false);
			    lastCheck = DateTime.UtcNow;
                            if (DateTime.UtcNow - resetThreshold > RapidStart)
                                Reset();
                        }
                    }
                    catch
			  {
				  lastCheck = DateTime.UtcNow;
			  }
                    await Task.Delay(1000, Config.Cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e) { Config.Log.Error(e); }
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
