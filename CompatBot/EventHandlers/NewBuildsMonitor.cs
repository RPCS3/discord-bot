using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class NewBuildsMonitor
    {
        private static readonly Regex BuildResult = new Regex("build (succeed|pass)ed", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly TimeSpan PassiveCheckInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan ActiveCheckInterval = TimeSpan.FromSeconds(5);
        public static TimeSpan CheckInterval { get; private set; } = PassiveCheckInterval;
        public static DateTime? RapidStart { get; private set; }

        public static Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBot
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
