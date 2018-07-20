using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal class LogsAsTextMonitor
    {
        private static readonly Regex LogLine = new Regex(@"^·|^\w {(rsx|PPU|SPU)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBot)
                return;

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

            if (!"help".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase))
                return;

            if ((args.Message.Author as DiscordMember)?.Roles?.Any() ?? false)
                return;

            if (LogLine.IsMatch(args.Message.Content))
                await args.Channel.SendMessageAsync($"{args.Message.Author.Mention} please upload the full log file instead of pasting some random bits that might be completely irrelevant").ConfigureAwait(false);
        }
    }
}
