using System;
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
        private static readonly Regex UploadLogMention = new Regex(@"\b(post|upload|send)(ing)?\s+((a|the|rpcs3('s)?|your|you're|ur|my|full)\s+)*\blogs?\b", DefaultOptions);
        private static readonly SemaphoreSlim TheDoor = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan ThrottlingThreshold = TimeSpan.FromSeconds(5);
        private static readonly Explanation DefaultLogUploadExplanation = new Explanation{ Text = "To upload log, run the game, then completely close RPCS3, then drag and drop rpcs3.log.gz from the RPCS3 folder into Discord. The file may have a zip or rar icon."};
        private static DateTime lastMention = DateTime.UtcNow.AddHours(-1);

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBot)
                return;

#if !DEBUG
            if (!args.Channel.Name.Equals("help", StringComparison.InvariantCultureIgnoreCase))
                return;

            if (DateTime.UtcNow - lastMention < ThrottlingThreshold)
                return;
#endif

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

            if (!UploadLogMention.IsMatch(args.Message.Content))
                return;

            if (!TheDoor.Wait(0))
                return;

            try
            {
                var explanation = await GetLogUploadExplanationAsync().ConfigureAwait(false);
                var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 10).ConfigureAwait(false);
                foreach (var msg in lastBotMessages)
                    if (BotShutupHandler.NeedToSilence(msg).needToChill
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

        public static async Task<Explanation> GetLogUploadExplanationAsync()
        {
            Explanation result;
            using (var db = new BotDb())
                result = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == "log").ConfigureAwait(false);
            return result ?? DefaultLogUploadExplanation;
        }
    }
}
