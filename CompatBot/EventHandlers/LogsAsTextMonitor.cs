using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal class LogsAsTextMonitor
    {
        private static readonly Regex LogLine = new Regex(@"^·|^(\w|!) ({(rsx|PPU|SPU)|LDR:)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBot)
                return;

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

            if (!"help".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase))
                return;

            if ((args.Message.Author as DiscordMember)?.Roles.IsWhitelisted() ?? false)
                return;

            if (LogLine.IsMatch(args.Message.Content))
            {
                if (args.Message.Content.Contains("LDR:") && args.Message.Content.Contains("fs::file is null"))
                    await args.Channel.SendMessageAsync(
                        $"{args.Message.Author.Mention} this error usually indicates a missing `.rap` license file.{Environment.NewLine}" +
                        $"Please follow the quickstart guide to get a proper dump of a digital title.{Environment.NewLine}" +
                         "Also please upload full log file instead of pasting random bits that might or might not be relevant."
                    ).ConfigureAwait(false);
                else
                    await args.Channel.SendMessageAsync($"{args.Message.Author.Mention} please upload the full log file instead of pasting some random bits that might be completely irrelevant").ConfigureAwait(false);
            }
        }
    }
}
