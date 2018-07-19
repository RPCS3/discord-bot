using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal class LogsAsTextMonitor
    {
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

            if (args.Message.Content.Contains('·'))
                if (args.Message.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(l => l.StartsWith('·')))
                    await args.Channel.SendMessageAsync($"{args.Message.Author.Mention} please upload the full log file instead of pasting some random lines that might be completely irrelevant").ConfigureAwait(false);
        }
    }
}
