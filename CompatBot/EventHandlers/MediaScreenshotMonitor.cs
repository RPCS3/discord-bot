using System.Threading.Channels;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Ocr;
using CompatBot.Utils.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

internal sealed class MediaScreenshotMonitor
{
    private static readonly Channel<(DiscordMessage msg, string imgUrl)> WorkQueue = Channel.CreateUnbounded<(DiscordMessage msg, string imgUrl)>();
    private static readonly MemoryCache RemovedMessages = new(new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromHours(1) });
    private static readonly TimeSpan MessageCachedTime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SignatureCachedTime = TimeSpan.FromMinutes(30);
    public DiscordClient Client { get; internal set; } = null!;
    public static int MaxQueueLength { get; private set; }

    public async Task OnMessageCreated(DiscordClient client, MessageCreatedEventArgs evt)
    {
        if (!OcrProvider.IsAvailable)
            return;
        
        if (evt.Message is not {} message)
            return;

//        if (!Config.Moderation.OcrChannels.Contains(evt.Channel.Id))
//            return;

        if (message.Author.IsBotSafeCheck())
            return;

#if !DEBUG
        if (await message.Author.IsSmartlistedAsync(client).ConfigureAwait(false))
            return;
#endif

        EnqueueOcrTask(evt.Message);
    }

    public static void EnqueueOcrTask(DiscordMessage message)
    {
        if (!message.Attachments.Any() && !message.Embeds.Any())
            return;

        var images = Vision.GetImageAttachments(message)
            .Concat(Vision.GetImagesFromEmbeds(message))
            .ToList();
        foreach (var url in images)
        {
            if (!WorkQueue.Writer.TryWrite((message, url)))
                Config.Log.Warn($"Failed to create a new text recognition task for message {message.JumpLink} / {url}");
        }
    }

    public async Task ProcessWorkQueue()
    {
        if (!OcrProvider.IsAvailable)
            return;

        do
        {
            MaxQueueLength = Math.Max(MaxQueueLength, WorkQueue.Reader.Count);
            var (msg, imgUrl) = await WorkQueue.Reader.ReadAsync(Config.Cts.Token).ConfigureAwait(false);
            if (Config.Cts.IsCancellationRequested)
                return;

            if (RemovedMessages.TryGetValue(msg.Id, out bool removed) && removed)
                continue;

            try
            {
                var signature = GetSignatureLoose(msg);
                var prefix = $"[{msg.Id % 100:00}]";
                if (RemovedMessages.TryGetValue(signature, out (Piracystring hit, DiscordMessage msg) previousItem))
                {
                    Config.Log.Debug($"{prefix} OCR result of message {msg.JumpLink} from user {msg.Author?.Username} ({msg.Author?.Id}):");
                    var ocrTextBuf = new StringBuilder($"OCR result of message <{msg.JumpLink}>:").AppendLine();
                    FilterAction suppressFlags = 0;
                    if ("media".Equals(msg.Channel?.Name))
                    {
                        suppressFlags = FilterAction.SendMessage | FilterAction.ShowExplain;
                    }
                    await ContentFilter.PerformFilterActions(
                        Client,
                        msg,
                        previousItem.hit,
                        suppressFlags,
                        $"Matched to previously removed message from {previousItem.msg.Author?.Mention}: {previousItem.msg.JumpLink}",
                        "🖼 Screenshot of an undesirable content",
                        "Screenshot of an undesirable content"
                    ).ConfigureAwait(false);
                    if (previousItem.hit.Actions.HasFlag(FilterAction.RemoveContent))
                        RemovedMessages.Set(msg.Id, true, MessageCachedTime);
                }
                else if (await OcrProvider.GetTextAsync(imgUrl, Config.Cts.Token).ConfigureAwait(false) is ({ Length: > 0 } result, var confidence))
                {
                    var cnt = true;
                    var duplicates = new HashSet<string>();
                    Config.Log.Debug($"""
                        {prefix} OCR result of message {msg.JumpLink} from user {msg.Author?.Username} ({msg.Author?.Id}) ({confidence * 100:0.00}%):
                        {result}
                        """
                    );
                    var ocrTextBuf = new StringBuilder($"OCR result of message <{msg.JumpLink}> ({confidence * 100:0.00}%):").AppendLine()
                        .AppendLine(result.Sanitize());
                    if (cnt
                        && confidence > 0.65
                        && await ContentFilter.FindTriggerAsync(FilterContext.Chat, result).ConfigureAwait(false) is Piracystring hit
                        && duplicates.Add(hit.String))
                    {
                        FilterAction suppressFlags = 0;
                        if ("media".Equals(msg.Channel?.Name))
                        {
                            suppressFlags = FilterAction.SendMessage | FilterAction.ShowExplain;
                        }
                        await ContentFilter.PerformFilterActions(
                            Client,
                            msg,
                            hit,
                            suppressFlags,
                            result,
                            "🖼 Screenshot of an undesirable content",
                            "Screenshot of an undesirable content"
                        ).ConfigureAwait(false);
                        if (hit.Actions.HasFlag(FilterAction.RemoveContent))
                        {
                            RemovedMessages.Set(msg.Id, true, MessageCachedTime);
                            if (signature is { Length: >0})
                                RemovedMessages.Set(signature, (hit, msg), SignatureCachedTime);
                        }
                        cnt &= !hit.Actions.HasFlag(FilterAction.RemoveContent) && !hit.Actions.HasFlag(FilterAction.IssueWarning);
                    }
                    var ocrText = ocrTextBuf.ToString();
                    var hasVkDiagInfo = ocrText.Contains("Vulkan Diagnostics Tool v")
                                        || ocrText.Contains("VkDiag Version:");
                    if (!cnt || hasVkDiagInfo)
                    {
                        try
                        {
                            var botSpamCh = await Client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                            await botSpamCh.SendAutosplitMessageAsync(ocrTextBuf, blockStart: "", blockEnd: "").ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Config.Log.Warn(ex);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        } while (!Config.Cts.IsCancellationRequested);
    }

    private static string GetSignaturePrecise(DiscordMessage msg)
    {
        var result = msg.Content ?? "";
        foreach (var att in msg.Attachments)
            result += $"📎 {att.FileName} ({att.FileSize})\n";
        return result.TrimEnd();
    }

    private static string GetSignatureLoose(DiscordMessage msg)
    {
        var result = msg.Content ?? "";
        foreach (var att in msg.Attachments.OrderByDescending(a => a.FileSize))
            result += $"📎 ({att.FileSize})\n";
        return result.TrimEnd();
    }
}