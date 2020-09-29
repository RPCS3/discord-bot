using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers
{
    internal static class PostLogHelpHandler
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
        private static readonly Regex UploadLogMention = new Regex(@"\b((?<vulkan>(vul[ck][ae]n(-?1)?))|(?<help>(post|upload|send|give)(ing)?\s+((a|the|rpcs3('s)?|your|you're|ur|my|full|game)\s+)*\blogs?))\b", DefaultOptions);
        private static readonly SemaphoreSlim TheDoor = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan ThrottlingThreshold = TimeSpan.FromSeconds(5);
        private static readonly Dictionary<string, Explanation> DefaultExplanation = new Dictionary<string, Explanation>
        {
            ["log"] = new Explanation { Text = "To upload log, run the game, then completely close RPCS3, then drag and drop rpcs3.log.gz from the RPCS3 folder into Discord. The file may have a zip or rar icon." },
            ["vulkan-1"] = new Explanation { Text = "Please remove all the traces of video drivers using DDU, and then reinstall the latest driver version for your GPU." },
        };
        private static DateTime lastMention = DateTime.UtcNow.AddHours(-1);

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args?.Message))
                return;

#if !DEBUG
            if (!"help".Equals(args?.Channel?.Name, StringComparison.InvariantCultureIgnoreCase))
                return;

            if (DateTime.UtcNow - lastMention < ThrottlingThreshold)
                return;
#endif

            var match = UploadLogMention.Match(args.Message.Content);
            if (!match.Success || string.IsNullOrEmpty(match.Groups["help"].Value))
                return;

            if (!await TheDoor.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                var explanation = await GetExplanationAsync(string.IsNullOrEmpty(match.Groups["vulkan"].Value) ? "log" : "vulkan-1").ConfigureAwait(false);
                var lastBotMessages = await args.Channel.GetMessagesBeforeCachedAsync(args.Message.Id, 10).ConfigureAwait(false);
                foreach (var msg in lastBotMessages)
                    if (BotReactionsHandler.NeedToSilence(msg).needToChill
                        || (msg.Author.IsCurrent && msg.Content == explanation.Text))
                        return;

                await args.Channel.SendMessageAsync(explanation.Text, explanation.Attachment, explanation.AttachmentFilename).ConfigureAwait(false);
                lastMention = DateTime.UtcNow;
            }
            finally
            {
                TheDoor.Release();
            }
        }

        public static async Task<Explanation> GetExplanationAsync(string term)
        {
            using var db = new BotDb();
            var result = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
            return result ?? DefaultExplanation[term];
        }
    }
}
