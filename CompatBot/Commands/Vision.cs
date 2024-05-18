﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ColorThiefDotNet;
using CompatBot.Commands.Attributes;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using FontStyle = SixLabors.Fonts.FontStyle;
using Image = SixLabors.ImageSharp.Image;
using PointF = SixLabors.ImageSharp.PointF;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using RectangleF = SixLabors.ImageSharp.RectangleF;
using Size = SixLabors.ImageSharp.Size;
using SystemFonts = SixLabors.Fonts.SystemFonts;

namespace CompatBot.Commands;

[Cooldown(1, 5, CooldownBucketType.Channel)]
internal sealed class Vision: BaseCommandModuleCustom
{
    static Vision()
    {
        var list = new StringBuilder("Available system fonts:").AppendLine();
        foreach (var fontFamily in SystemFonts.Families)
            list.AppendLine(fontFamily.Name);
        Config.Log.Debug(list.ToString());
    }

    private static readonly Dictionary<string, string[]> Reactions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cat"] = BotStats.GoodKot,
        ["dog"] = BotStats.GoodDog,
        ["hedgehog"] = ["🦔",],
        ["flower"] = ["🌷", "🌸", "🌹", "🌺", "🌼", "🥀", "💐", "🌻", "💮",],
        ["lizard"] = ["🦎",],
        ["bird"] = ["🐦", "🕊", "🦜", "🦆", "🦅", "🐓", "🐤", "🦩",],
        ["duck"] = ["🦆",],
        ["eagle"] = ["🦅",],
        ["turkey"] = ["🦃",],
        ["turtle"] = ["🐢",],
        ["bear"] = ["🐻", "🐼",],
        ["panda"] = ["🐼",],
        ["fox"] = ["🦊",],
        ["pig"] = ["🐷", "🐖", "🐽", "🐗",],
        ["primate"] = ["🐵", "🐒", "🙊", "🙉", "🙈",],
        ["fish"] = ["🐟", "🐠", "🐡", "🦈",],
        ["car"] = ["🚗", "🏎", "🚙", "🚓", "🚘", "🚔",],
        ["banana"] = ["🍌",],
        ["fruit"] = ["🍇", "🍈", "🍉", "🍊", "🍍", "🍑", "🍒", "🍓", "🍋", "🍐", "🍎", "🍏", "🥑", "🥝", "🥭", "🍅",],
        ["vegetable"] = ["🍠", "🍅", "🍆", "🥔", "🥕", "🥒",],
        ["watermelon"] = ["🍉",],
        ["strawberry"] = ["🍓",],
    };

    [Command("describe"), TriggersTyping]
    [Description("Generates an image description from the attached image, or from the url")]
    public Task Describe(CommandContext ctx, [RemainingText] string? imageUrl = null)
    {
        if (imageUrl?.StartsWith("tag") ?? false)
            return Tag(ctx, imageUrl[3..].TrimStart());
        return Tag(ctx, imageUrl);
    }

    [Command("tag"), TriggersTyping]
    [Description("Tags recognized objects in the image")]
    public async Task Tag(CommandContext ctx, string? imageUrl = null)
    {
        try
        {
            imageUrl = await GetImageUrlAsync(ctx, imageUrl).ConfigureAwait(false);
            if (string.IsNullOrEmpty(imageUrl) && ctx.Message.ReferencedMessage is { } msg)
            {
                msg = await msg.Channel.GetMessageAsync(msg.Id).ConfigureAwait(false);
                if (msg.Attachments.Any())
                    imageUrl = GetImageAttachments(msg).FirstOrDefault()?.Url;
                if (string.IsNullOrEmpty(imageUrl))
                    imageUrl = GetImagesFromEmbeds(msg).FirstOrDefault();
                if (string.IsNullOrEmpty(imageUrl))
                    imageUrl = await GetImageUrlAsync(ctx, msg.Content).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(imageUrl) || !Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "No proper image url was found").ConfigureAwait(false);
                return;
            }

            await using var imageStream = Config.MemoryStreamManager.GetStream();
            using (var httpClient = HttpClientFactory.Create())
            await using (var stream = await httpClient.GetStreamAsync(imageUrl).ConfigureAwait(false))
                await stream.CopyToAsync(imageStream).ConfigureAwait(false);
            imageStream.Seek(0, SeekOrigin.Begin);
            using var img = await Image.LoadAsync(imageStream).ConfigureAwait(false);
            imageStream.Seek(0, SeekOrigin.Begin);

            //resize and shrink file size to get under azure limits
            var quality = 90;
            var resized = false;
            if (img is {Width: >4000} or {Height: >4000})
            {
                img.Mutate(i => i.Resize(new ResizeOptions {Size = new(3840, 2160), Mode = ResizeMode.Min,}));
                resized = true;
            }
            img.Mutate(i => i.AutoOrient());
            if (resized || img.Metadata.DecodedImageFormat?.Name != JpegFormat.Instance.Name)
            {
                imageStream.SetLength(0);
                await img.SaveAsync(imageStream, new JpegEncoder {Quality = 90}).ConfigureAwait(false);
                imageStream.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                try
                {
                    quality = img.Metadata.GetJpegMetadata().Quality;
                }
                catch (Exception ex)
                {
                    Config.Log.Warn(ex);
                }
            }
            if (imageStream.Length > 4 * 1024 * 1024)
            {
                quality -= 5;
                imageStream.SetLength(0);
                await img.SaveAsync(imageStream, new JpegEncoder {Quality = quality}).ConfigureAwait(false);
                imageStream.Seek(0, SeekOrigin.Begin);
            }

            var client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
            var result = await client.AnalyzeImageInStreamAsync(
                imageStream,
                new List<VisualFeatureTypes?>
                {
                    VisualFeatureTypes.Objects, // https://docs.microsoft.com/en-us/azure/cognitive-services/computer-vision/concept-object-detection
                    VisualFeatureTypes.Description, // https://docs.microsoft.com/en-us/azure/cognitive-services/computer-vision/concept-describing-images
                    VisualFeatureTypes.Adult, // https://docs.microsoft.com/en-us/azure/cognitive-services/computer-vision/concept-detecting-adult-content
                },
                cancellationToken: Config.Cts.Token
            ).ConfigureAwait(false);
            var description = GetDescription(result.Description, result.Adult);
            var objects = result.Objects
                .OrderBy(c => c.Rectangle.Y)
                .ThenBy(c => c.Confidence)
                .ToList();
            var scale = Math.Max(1.0f, img.Width / 400.0f);
            if (objects.Count > 0 && !result.Adult.IsAdultContent && !result.Adult.IsGoryContent)
            {
                var analyzer = new ColorThief();
                List<Color> palette = new(objects.Count);
                foreach (var obj in objects)
                {
                    var r = obj.Rectangle;
                    await using var tmpStream = Config.MemoryStreamManager.GetStream();
                    using var boxCopy = img.Clone(i => i.Crop(new(r.X, r.Y, r.W, r.H)));
                    await boxCopy.SaveAsBmpAsync(tmpStream).ConfigureAwait(false);
                    tmpStream.Seek(0, SeekOrigin.Begin);

                    //using var b = new Bitmap(tmpStream);
                    var b = Image.Load<Rgba32>(tmpStream);
                    var dominantColor = ColorGetter.GetDominentColor(b);
                    palette.Add(dominantColor);
                }
                var complementaryPalette = palette.Select(c => c.GetComplementary()).ToList();
                var tmpP = new List<Color>();
                var tmpCp = new List<Color>();
                var uniqueCp = new HashSet<Color>();
                for (var i = 0; i < complementaryPalette.Count; i++)
                    if (uniqueCp.Add(complementaryPalette[i]))
                    {
                        tmpP.Add(palette[i]);
                        tmpCp.Add(complementaryPalette[i]);
                    }
                palette = tmpP;
                complementaryPalette = tmpCp;

                Config.Log.Debug($"Palette      : {string.Join(' ', palette.Select(c => $"#{c.ToHex()}"))}");
                Config.Log.Debug($"Complementary: {string.Join(' ', complementaryPalette.Select(c => $"#{c.ToHex()}"))}");

                if ((string.IsNullOrEmpty(Config.PreferredFontFamily) || !SystemFonts.TryGet(Config.PreferredFontFamily, out var fontFamily))
                    && !SystemFonts.TryGet("Roboto", out fontFamily)
                    && !SystemFonts.TryGet("Droid Sans", out fontFamily)
                    && !SystemFonts.TryGet("DejaVu Sans", out fontFamily)
                    && !SystemFonts.TryGet("Sans Serif", out fontFamily)
                    && !SystemFonts.TryGet("Calibri", out fontFamily)
                    && !SystemFonts.TryGet("Verdana", out fontFamily))
                {
                    Config.Log.Warn("Failed to find any suitable font. Available system fonts:\n" + string.Join(Environment.NewLine, SystemFonts.Families.Select(f => f.Name)));
                    fontFamily = SystemFonts.Families.FirstOrDefault(f => f.Name.Contains("sans", StringComparison.OrdinalIgnoreCase));
                }
                Config.Log.Debug($"Selected font: {fontFamily.Name}");
                var font = fontFamily.CreateFont(10 * scale, FontStyle.Regular);
                var graphicsOptions = new GraphicsOptions
                {
                    Antialias = true,
                    ColorBlendingMode = PixelColorBlendingMode.Normal,
                };
                var bgGop = new GraphicsOptions
                {
                    ColorBlendingMode = PixelColorBlendingMode.Screen,
                };
                var fgGop = new GraphicsOptions
                {
                    ColorBlendingMode = PixelColorBlendingMode.Multiply,
                };
                var shapeDrawingOptions = new DrawingOptions {GraphicsOptions = graphicsOptions};
                var bgDrawingOptions = new DrawingOptions {GraphicsOptions = bgGop,};
                var drawnBoxes = new List<RectangleF>(objects.Count);
                for (var i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    var label = $"{obj.ObjectProperty.FixKot()} ({obj.Confidence:P1})";
                    var r = obj.Rectangle;
                    var color = palette[i % palette.Count];
                    var complementaryColor = complementaryPalette[i % complementaryPalette.Count];
                    var textOptions = new TextOptions(font)
                    {
                        KerningMode = KerningMode.Standard,
#if LABELS_INSIDE
                            WrapTextWidth = r.W - 10,
#endif
                    };
                    var textDrawingOptions = new DrawingOptions {GraphicsOptions = fgGop/*, TextOptions = textOptions*/};
                    //var brush = Brushes.Solid(Color.Black);
                    //var pen = Pens.Solid(color, 2);
                    var textBox = TextMeasurer.MeasureBounds(label, textOptions);
#if LABELS_INSIDE
                        var textHeightScale = (int)Math.Ceiling(textBox.Width / Math.Min(img.Width - r.X - 10 - 4 * scale, r.W - 4 * scale));
#endif
                    // object bounding box
                    try
                    {
                        img.Mutate(ipc => ipc.Draw(shapeDrawingOptions, complementaryColor, scale, new RectangleF(r.X, r.Y, r.W, r.H)));
                        img.Mutate(ipc => ipc.Draw(shapeDrawingOptions, color, scale, new RectangleF(r.X + scale, r.Y + scale, r.W - 2 * scale, r.H - 2 * scale)));
                    }
                    catch (Exception ex)
                    {
                        Config.Log.Error(ex, "Failed to draw object bounding box");
                    }

                    // label bounding box
                    var bboxBorder = scale;

#if LABELS_INSIDE
                        var bgBox =
 new RectangleF(r.X + 2 * scale, r.Y + 2 * scale, Math.Min(textBox.Width + 2 * (bboxBorder + scale), r.W - 4 * scale), textBox.Height * textHeightScale + 2 * (bboxBorder + scale));
#else
                    var bgBox = new RectangleF(r.X, r.Y - textBox.Height - 2 * bboxBorder - scale, textBox.Width + 2 * bboxBorder, textBox.Height + 2 * bboxBorder);
#endif
                    while (drawnBoxes.Any(b => b.IntersectsWith(bgBox)))
                    {
                        var pb = drawnBoxes.First(b => b.IntersectsWith(bgBox));
                        bgBox.Y = pb.Bottom;
                    }
                    if (bgBox.Width < 20)
                        bgBox.Width = 20 * scale;
                    if (bgBox.Height < 20)
                        bgBox.Height = 20 * scale;
                    if (bgBox.X < 0)
                        bgBox.X = 0;
                    if (bgBox.Y < 0)
                        bgBox.Y = 0;
                    if (bgBox.X + bgBox.Width > img.Width)
                        bgBox.X = img.Width - bgBox.Width;
                    if (bgBox.Y + bgBox.Height > img.Height)
                        bgBox.Y = img.Height - bgBox.Height;
                    drawnBoxes.Add(bgBox);
                    var textBoxColor = complementaryColor;
                    var textColor = color;
                    try
                    {
                        img.Mutate(ipc => ipc.Fill(bgDrawingOptions, textBoxColor, bgBox));
                        img.Mutate(ipc => ipc.GaussianBlur(10 * scale, Rectangle.Round(bgBox)));
                    }
                    catch (Exception ex)
                    {
                        Config.Log.Error(ex, "Failed to draw label bounding box");
                    }

                    // label text
                    try
                    {
                        img.Mutate(ipc => ipc.DrawText(textDrawingOptions, label, font, textColor, new(bgBox.X + bboxBorder, bgBox.Y + bboxBorder)));
                    }
                    catch (Exception ex)
                    {
                        Config.Log.Error(ex, "Failed to generate tag label");
                    }
                }
                await using var resultStream = Config.MemoryStreamManager.GetStream();
                quality = 95;
                do
                {
                    resultStream.SetLength(0);
                    await img.SaveAsync(resultStream, new JpegEncoder {Quality = 95}).ConfigureAwait(false);
                    resultStream.Seek(0, SeekOrigin.Begin);
                    quality--;
                } while (resultStream.Length > ctx.GetAttachmentSizeLimit());
                var attachmentFname = Path.GetFileNameWithoutExtension(imageUrl) + "_tagged.jpg";
                if (result.Adult.IsRacyContent && !attachmentFname.StartsWith("SPOILER_"))
                    attachmentFname = "SPOILER_" + attachmentFname;
                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent(description)
                    .AddFile(attachmentFname, resultStream);
                if (ctx.Message.ReferencedMessage is { } ogRef)
                    messageBuilder.WithReply(ogRef.Id);
                var respondMsg = await ctx.Channel.SendMessageAsync(messageBuilder).ConfigureAwait(false);
                var tags = result.Objects.Select(o => o.ObjectProperty).Concat(result.Description.Tags).Distinct().ToList();
                Config.Log.Info(
                    $"Tags for image {imageUrl}: {string.Join(", ", tags)}. Adult info: a={result.Adult.AdultScore:0.000}, r={result.Adult.RacyScore:0.000}, g={result.Adult.GoreScore:0.000}");
                if (result.Adult.IsRacyContent)
                    await respondMsg.ReactWithAsync(DiscordEmoji.FromUnicode("😳")).ConfigureAwait(false);
                await ReactToTagsAsync(respondMsg, tags).ConfigureAwait(false);
            }
            else
            {
                var msgBuilder = new DiscordMessageBuilder()
                    .WithContent(description);
                if (ctx.Message.ReferencedMessage is { } ogRef)
                    msgBuilder.WithReply(ogRef.Id);
                await ctx.Channel.SendMessageAsync(msgBuilder).ConfigureAwait(false);
                if (result.Adult.IsAdultContent)
                    await ctx.Message.ReactWithAsync(DiscordEmoji.FromUnicode("🔞")).ConfigureAwait(false);
                if (result.Adult.IsRacyContent)
                    await ctx.Message.ReactWithAsync(DiscordEmoji.FromUnicode("😳")).ConfigureAwait(false);
                if (result.Adult.IsGoryContent)
                    await ctx.Message.ReactWithAsync(DiscordEmoji.FromUnicode("🆖")).ConfigureAwait(false);
                Config.Log.Info($"Adult info for image {imageUrl}: a={result.Adult.AdultScore:0.000}, r={result.Adult.RacyScore:0.000}, g={result.Adult.GoreScore:0.000}");
                await ReactToTagsAsync(ctx.Message, result.Description.Tags).ConfigureAwait(false);
            }
        }
        catch (ComputerVisionErrorResponseException cve) when (cve.Response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            Config.Log.Warn(cve, "Computer Vision is broken");
            await ctx.Channel.SendMessageAsync("Azure services are temporarily unavailable, please try in an hour or so").ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to tag objects in an image");
            await ctx.Channel.SendMessageAsync("Can't do anything with this image").ConfigureAwait(false);
        }
    }

    internal static IEnumerable<string> GetImagesFromEmbeds(DiscordMessage msg)
    {
        foreach (var embed in msg.Embeds)
        {
            if (embed.Image?.Url?.ToString() is string url)
                yield return url;
            else if (embed.Thumbnail?.Url?.ToString() is string thumbUrl)
                yield return thumbUrl;
        }
    }

    internal static IEnumerable<DiscordAttachment> GetImageAttachments(DiscordMessage message)
        => message.Attachments.Where(a =>
                a.FileName.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                || a.FileName.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                || a.FileName.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
            //|| a.FileName.EndsWith(".webp", StringComparison.InvariantCultureIgnoreCase)
        );

    private static string GetDescription(ImageDescriptionDetails description, AdultInfo adultInfo)
    {
        var captions = description.Captions.OrderByDescending(c => c.Confidence).ToList();
        string msg;
        if (captions.Any())
        {
            var confidence = captions[0].Confidence switch
            {
                > 0.98 => "It is",
                > 0.95 => "I'm pretty sure it is",
                > 0.9 => "I'm quite sure it is",
                > 0.8 => "I think it's",
                > 0.5 => "I'm not very smart, so my best guess is that it's",
                _ => "Ugh, idk? Might be",
            };
            msg = $"{confidence} {captions[0].Text.FixKot()}";
#if DEBUG
            msg += $" [{captions[0].Confidence * 100:0.00}%]";
            if (captions.Count > 1)
            {
                msg += "\nHowever, here are more guesses:\n";
                msg += string.Join('\n', captions.Skip(1).Select(c => $"{c.Text} [{c.Confidence * 100:0.00}%]"));
                msg += "\n";
            }
#endif
        }
        else
            msg = "An image so weird, I have no words to describe it";
#if DEBUG
        msg += $" (Adult: {adultInfo.AdultScore * 100:0.00}%, racy: {adultInfo.RacyScore * 100:0.00}%, gore: {adultInfo.GoreScore * 100:0.00}%)";
#endif
        return msg;
    }

    private static async Task ReactToTagsAsync(DiscordMessage reactMsg, IEnumerable<string> tags)
    {
        foreach (var t in tags.Distinct(StringComparer.OrdinalIgnoreCase))
            if (Reactions.TryGetValue(t, out var emojiList))
                await reactMsg.ReactWithAsync(DiscordEmoji.FromUnicode(emojiList[new Random().Next(emojiList.Length)])).ConfigureAwait(false);
    }

    private static async Task<string?> GetImageUrlAsync(CommandContext ctx, string? imageUrl)
    {
        var reactMsg = ctx.Message;
        if (GetImageAttachments(reactMsg).FirstOrDefault() is DiscordAttachment attachment)
            imageUrl = attachment.Url;
        imageUrl = imageUrl?.Trim() ?? "";
        if (!string.IsNullOrEmpty(imageUrl)
            && imageUrl.StartsWith('<')
            && imageUrl.EndsWith('>'))
            imageUrl = imageUrl[1..^1];
        if (!Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
        {
            var str = imageUrl.ToLowerInvariant();
            if ((str.StartsWith("this")
                 || str.StartsWith("that")
                 || str.StartsWith("last")
                 || str.StartsWith("previous")
                 || str.StartsWith("^"))
                && ctx.Channel.PermissionsFor(
                    await ctx.Client.GetMemberAsync(ctx.Guild, ctx.Client.CurrentUser).ConfigureAwait(false)
                ).HasPermission(Permissions.ReadMessageHistory))
                try
                {
                    var previousMessages = (await ctx.Channel.GetMessagesBeforeCachedAsync(ctx.Message.Id, 10).ConfigureAwait(false))!;
                    imageUrl = (
                        from m in previousMessages
                        where m.Attachments?.Count > 0
                        from a in GetImageAttachments(m)
                        select a.Url
                    ).FirstOrDefault();
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        var selectedUrl = (
                            from m in previousMessages
                            where m.Embeds?.Count > 0
                            from e in m.Embeds
                            let url = e.Image?.Url ?? e.Image?.ProxyUrl ?? e.Thumbnail?.Url ?? e.Thumbnail?.ProxyUrl
                            select url
                        ).FirstOrDefault();
                        imageUrl = selectedUrl?.ToString();
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to get link to the previously posted image");
                    //await ctx.Channel.SendMessageAsync("Sorry chief, can't find any images in the recent posts").ConfigureAwait(false);
                }
        }
        return imageUrl;
    }
}