using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class NewBuildsMonitor
    {
        private static readonly Regex BuildResult = new Regex("build (succeed|pass)ed", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (!args.Author.IsBot)
                return;

            if (!"github".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase))
                return;

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

            if (BuildResult.IsMatch(args.Message.Content))
                if (!await CompatList.UpdatesCheck.CheckForRpcs3Updates(args.Client, null).ConfigureAwait(false))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await CompatList.UpdatesCheck.CheckForRpcs3Updates(args.Client, null).ConfigureAwait(false);
                }
        }
    }
}
