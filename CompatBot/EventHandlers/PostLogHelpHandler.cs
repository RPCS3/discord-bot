using System.Text.RegularExpressions;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers;

internal static partial class PostLogHelpHandler
{
    [GeneratedRegex(
        @"\b((?<vulkan>(vul[ck][ae]n(-?1)?))|(?<help>(post|upload|send|give)(ing)?\s+((a|the|rpcs3('s)?|your|you're|ur|my|full|game)\s+)*\blogs?))\b",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Singleline
    )]
    private static partial Regex UploadLogMention();
    private static readonly SemaphoreSlim TheDoor = new(1, 1);
    private static readonly TimeSpan ThrottlingThreshold = TimeSpan.FromSeconds(5);
    private static readonly Dictionary<string, Explanation> DefaultExplanation = new()
    {
        ["log"] = new() { Text = "To upload log, run the game, then completely close RPCS3, then drag and drop rpcs3.log.gz from the RPCS3 folder into Discord. The file may have a zip or rar icon." },
        ["vulkan-1"] = new() { Text = "Please remove all the traces of video drivers using DDU, and then reinstall the latest driver version for your GPU." },
    };
    private static DateTime lastMention = DateTime.UtcNow.AddHours(-1);

    public static async Task OnMessageCreated(DiscordClient _, MessageCreateEventArgs args)
    {
        if (DefaultHandlerFilter.IsFluff(args.Message))
            return;

        if (!LimitedToHelpChannel.IsHelpChannel(args.Channel))
            return;

        if (DateTime.UtcNow - lastMention < ThrottlingThreshold)
            return;

        var match = UploadLogMention().Match(args.Message.Content);
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
                    || msg.Author.IsCurrent && msg.Content == explanation.Text)
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
        await using var db = new BotDb();
        var result = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
        return result ?? DefaultExplanation[term];
    }
}