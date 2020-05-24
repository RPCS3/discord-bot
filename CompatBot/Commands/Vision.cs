using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ColorThiefDotNet;
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

namespace CompatBot.Commands
{
    [Cooldown(1, 5, CooldownBucketType.Channel)]
    internal sealed class Vision: BaseCommandModuleCustom
    {
        private static readonly Color[] DefaultColors = {Color.DeepSkyBlue, Color.DarkOliveGreen, Color.OrangeRed, };

        private static readonly Dictionary<string, string[]> Reactions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cat"] = BotStats.GoodKot,
            ["dog"] = BotStats.GoodDog,
            ["hedgehog"] = new[] {"🦔"},
            ["flower"] = new[] {"🌷", "🌸", "🌹", "🌺", "🌼", "🥀", "💐", "🌻", "💮",},
            ["lizard"] = new[] {"🦎",},
            ["bird"] = new[] {"🐦", "🕊", "🦜", "🦆", "🦅", "🐓", "🐤", "🦩",},
            ["duck"] = new[] {"🦆",},
            ["eagle"] = new[] {"🦅",},
            ["turkey"] = new[] {"🦃",},
            ["turtle"] = new[] {"🐢",},
            ["bear"] = new[] {"🐻", "🐼",},
            ["panda"] = new[] {"🐼",},
            ["fox"] = new[] {"🦊",},
            ["pig"] = new[] {"🐷", "🐖", "🐽", "🐗",},
            ["primate"] = new[] {"🐵", "🐒", "🙊", "🙉", "🙈",},
            ["fish"] = new[] {"🐟", "🐠", "🐡", "🦈",},
            ["car"] = new[] {"🚗", "🏎", "🚙", "🚓", "🚘", "🚔",},
            ["banana"] = new[] {"🍌"},
            ["fruit"] = new[] {"🍇", "🍈", "🍉", "🍊", "🍍", "🍑", "🍒", "🍓", "🍋", "🍐", "🍎", "🍏", "🥑", "🥝", "🥭", "🍅",},
            ["vegetable"] = new[] {"🍠", "🍅", "🍆", "🥔", "🥕", "🥒",},
            ["watermelon"] = new[] {"🍉",},
            ["strawberry"] = new[] {"🍓",},
        };

        [Command("describe")]
        [Description("Generates an image description from the attached image, or from the url")]
        public async Task Describe(CommandContext ctx, [RemainingText] string imageUrl = null)
        {
            try
            {
                if (imageUrl?.StartsWith("tag") ?? false)
                {
                    await Tag(ctx, imageUrl[3..].TrimStart()).ConfigureAwait(false);
                    return;
                }

                imageUrl = await GetImageUrlAsync(ctx, imageUrl).ConfigureAwait(false);
                if (string.IsNullOrEmpty(imageUrl))
                    return;

                var client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
                var result = await client.AnalyzeImageAsync(imageUrl, new List<VisualFeatureTypes> {VisualFeatureTypes.Description}, cancellationToken: Config.Cts.Token).ConfigureAwait(false);
                var description = GetDescription(result.Description);
                await ReactToTagsAsync(ctx.Message, result.Description.Tags).ConfigureAwait(false);
                await ctx.RespondAsync(description).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get image description");
                await ctx.RespondAsync("Failed to generate image description, probably because image is too large (dimensions or file size)").ConfigureAwait(false);
            }
        }

        [Command("tag")]
        [Description("Tags recognized objects in the image")]
        public async Task Tag(CommandContext ctx, string imageUrl = null)
        {
            try
            {
                imageUrl = await GetImageUrlAsync(ctx, imageUrl).ConfigureAwait(false);
                if (string.IsNullOrEmpty(imageUrl))
                    return;

                using var imageStream = Config.MemoryStreamManager.GetStream();
                using (var httpClient = HttpClientFactory.Create())
                using (var stream = await httpClient.GetStreamAsync(imageUrl).ConfigureAwait(false))
                    await stream.CopyToAsync(imageStream).ConfigureAwait(false);
                imageStream.Seek(0, SeekOrigin.Begin);
                using var img = Image.Load(imageStream, out var imgFormat);
                imageStream.Seek(0, SeekOrigin.Begin);

                //resize and shrink file size to get under azure limits
                var quality = 90;
                var resized = false;
                if (img.Width > 4000 || img.Height > 4000)
                {
                    img.Mutate(i => i.Resize(new ResizeOptions {Size = new Size(3840, 2160), Mode = ResizeMode.Min,}));
                    resized = true;
                }
                if (resized || imgFormat.Name != JpegFormat.Instance.Name)
                {
                    imageStream.SetLength(0);
                    img.Save(imageStream, new JpegEncoder { Quality = 90 });
                    imageStream.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    try
                    {
                        quality = img.Metadata.GetJpegMetadata().Quality;
                    }
                    catch(Exception ex)
                    {
                        Config.Log.Warn(ex);
                    }
                }
                if (imageStream.Length > 4 * 1024 * 1024)
                {
                    quality -= 5;
                    imageStream.SetLength(0);
                    img.Save(imageStream, new JpegEncoder {Quality = quality});
                    imageStream.Seek(0, SeekOrigin.Begin);
                }

                var client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) { Endpoint = Config.AzureComputerVisionEndpoint };
                var result = await client.AnalyzeImageInStreamAsync(
                    imageStream,
                    new List<VisualFeatureTypes>
                    {
                        VisualFeatureTypes.Objects,
                        VisualFeatureTypes.Description
                    },
                    cancellationToken: Config.Cts.Token
                ).ConfigureAwait(false);
                var description = GetDescription(result.Description);
                var objects = result.Objects
                    .OrderBy(c => c.Rectangle.Y)
                    .ThenBy(c => c.Confidence)
                    .ToList();
                var scale = Math.Max(1.0f, img.Width / 400.0f);
                if (objects.Count > 0)
                {
                    //List<Color> palette = new List<Color> { Color.DeepSkyBlue, Color.Magenta, Color.GreenYellow, };
                    
                    var analyzer = new ColorThief();
                    List<Color> palette;
                    using (var tmpStream = Config.MemoryStreamManager.GetStream())
                    {
                        img.SaveAsBmp(tmpStream);
                        tmpStream.Seek(0, SeekOrigin.Begin);
                        using (var b = new Bitmap(tmpStream))
                            palette = analyzer.GetPalette(b, Math.Max(objects.Count, 5), ignoreWhite: false).Select(c => c.Color.ToStandardColor()).ToList();
                    }
                    palette.AddRange(DefaultColors);
                    var complementaryPalette = palette.Select(c => c.GetComplementary()).ToList();
                    var tmpP = new List<Color>();
                    var tmpCp = new List<Color>();
                    var uniqueCp = new HashSet<Color>();
                    for (var i=0; i<complementaryPalette.Count; i++)
                        if (uniqueCp.Add(complementaryPalette[i]))
                        {
                            tmpP.Add(palette[i]);
                            tmpCp.Add(complementaryPalette[i]);
                        }
                    palette = tmpP;
                    complementaryPalette = tmpCp;

                    Config.Log.Debug($"Palette      : {string.Join(' ', palette.Select(c => $"#{c.ToHex()}"))}");
                    Config.Log.Debug($"Complementary: {string.Join(' ', complementaryPalette.Select(c => $"#{c.ToHex()}"))}");

                    if (!SystemFonts.TryFind("Roboto", out var fontFamily)
                        && !SystemFonts.TryFind("Droid Sans", out fontFamily)
                        && !SystemFonts.TryFind("DejaVu Sans", out fontFamily)
                        && !SystemFonts.TryFind("Sans Serif", out fontFamily)
                        && !SystemFonts.TryFind("Calibri", out fontFamily)
                        && !SystemFonts.TryFind("Verdana", out fontFamily))
                    {
                        Config.Log.Warn("Failed to find any suitable font. Available system fonts:\n" + string.Join(Environment.NewLine, SystemFonts.Families.Select(f => f.Name)));
                        fontFamily = SystemFonts.Families.FirstOrDefault(f => f.Name.Contains("sans", StringComparison.OrdinalIgnoreCase))
                                  ?? SystemFonts.Families.First();

                    }
                    var font = fontFamily.CreateFont(10 * scale, FontStyle.Regular);
                    var textRendererOptions = new RendererOptions(font);
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
                    var shapeOptions = new ShapeOptions();
                    var shapeGraphicsOptions = new ShapeGraphicsOptions(graphicsOptions, shapeOptions);
                    var bgSgo = new ShapeGraphicsOptions(bgGop, shapeOptions);
                    var drawnBoxes = new List<RectangleF>(objects.Count);
                    for (var i = 0; i < objects.Count; i++)
                    {
                        var obj = objects[i];
                        var label = $"{obj.ObjectProperty.FixKot()} ({obj.Confidence:P1})";
                        var r = obj.Rectangle;
                        var color = palette[i % palette.Count];
                        var complementaryColor = complementaryPalette[i % complementaryPalette.Count];
                        var textOptions = new TextOptions
                        {
                            ApplyKerning = true,
#if LABELS_INSIDE
                            WrapTextWidth = r.W - 10,
#endif
                        };
                        var textGraphicsOptions = new TextGraphicsOptions(fgGop, textOptions);
                        //var brush = Brushes.Solid(Color.Black);
                        //var pen = Pens.Solid(color, 2);
                        var textBox = TextMeasurer.Measure(label, textRendererOptions);
#if LABELS_INSIDE
                        var textHeightScale = (int)Math.Ceiling(textBox.Width / Math.Min(img.Width - r.X - 10 - 4 * scale, r.W - 4 * scale));
#else
                        var textHeightScale = 1;
#endif
                        // object bounding box
                        try
                        {
                            img.Mutate(i => i.Draw(shapeGraphicsOptions, complementaryColor, scale, new RectangleF(r.X, r.Y, r.W, r.H)));
                            img.Mutate(i => i.Draw(shapeGraphicsOptions, color, scale, new RectangleF(r.X + scale, r.Y + scale, r.W - 2 * scale, r.H - 2 * scale)));
                        }
                        catch (Exception ex)
                        {
                            Config.Log.Error(ex, "Failed to draw object bounding box");
                        }

                        // label bounding box
                        var bboxBorder = scale;

#if LABELS_INSIDE
                        var bgBox = new RectangleF(r.X + 2 * scale, r.Y + 2 * scale, Math.Min(textBox.Width + 2 * (bboxBorder + scale), r.W - 4 * scale), textBox.Height * textHeightScale + 2 * (bboxBorder + scale));
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
                        try
                        {
                            img.Mutate(i => i.Fill(bgSgo, complementaryColor, bgBox));
                            img.Mutate(i => i.GaussianBlur(10 * scale, Rectangle.Round(bgBox)));
                        }
                        catch (Exception ex)
                        {
                            Config.Log.Error(ex, "Failed to draw label bounding box");
                        }

                        // label text
                        try
                        {
                            img.Mutate(i => i.DrawText(textGraphicsOptions, label, font, complementaryColor, new PointF(bgBox.X + bboxBorder, bgBox.Y + bboxBorder)));
                            //img.Mutate(i => i.DrawText(textGraphicsOptions, $"{obj.ObjectProperty} ({obj.Confidence:P1})", font, brush, pen, new PointF(r.X + 5, r.Y + 5))); // throws exception
                        }
                        catch (Exception ex)
                        {
                            Config.Log.Error(ex, "Failed to generate tag label");
                        }
                    }
                    using var resultStream = Config.MemoryStreamManager.GetStream();
                    quality = 95;
                    do
                    {
                        resultStream.SetLength(0);
                        img.Save(resultStream, new JpegEncoder {Quality = 95});
                        resultStream.Seek(0, SeekOrigin.Begin);
                        quality--;
                    } while (resultStream.Length > Config.AttachmentSizeLimit);
                    var respondMsg = await ctx.RespondWithFileAsync(Path.GetFileNameWithoutExtension(imageUrl) + "_tagged.jpg", resultStream, description).ConfigureAwait(false);
                    await ReactToTagsAsync(respondMsg, result.Objects.Select(o => o.ObjectProperty).Concat(result.Description.Tags)).ConfigureAwait(false);
                }
                else
                {
                    await ctx.RespondAsync(description).ConfigureAwait(false);
                    await ReactToTagsAsync(ctx.Message, result.Description.Tags).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to tag objects in an image");
                await ctx.RespondAsync("Can't doo anything with this image").ConfigureAwait(false);
            }
        }

        internal static IEnumerable<DiscordAttachment> GetImageAttachment(DiscordMessage message)
            => message.Attachments.Where(a =>
                                             a.FileName.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                                             || a.FileName.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                                             || a.FileName.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
                //|| a.FileName.EndsWith(".webp", StringComparison.InvariantCultureIgnoreCase)
            );

        private static string GetDescription(ImageDescriptionDetails description)
        {
            var captions = description.Captions.OrderByDescending(c => c.Confidence).ToList();
            string msg;
            if (captions.Any())
            {
                var confidence = captions[0].Confidence switch
                {
                    double v when v > 0.98 => "It is",
                    double v when v > 0.95 => "I'm pretty sure it is",
                    double v when v > 0.9 => "I'm quite sure it is",
                    double v when v > 0.8 => "I think it's",
                    double v when v > 0.5 => "I'm not very smart, so my best guess is that it's",
                    _ => "Ugh, idk? Might be",
                };
                msg = $"{confidence} {captions[0].Text.FixKot()}";
#if DEBUG
                msg += $" [{captions[0].Confidence * 100:0.00}%]";
                if (captions.Count > 1)
                {
                    msg += "\nHowever, here are more guesses:\n";
                    msg += string.Join('\n', captions.Skip(1).Select(c => $"{c.Text} [{c.Confidence * 100:0.00}%]"));
                }
#endif
            }
            else
                msg = "An image so weird, I have no words to describe it";
            return msg;
        }

        private static async Task ReactToTagsAsync(DiscordMessage reactMsg, IEnumerable<string> tags)
        {
            foreach (var t in tags.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Reactions.TryGetValue(t, out var emojiList))
                    await reactMsg.ReactWithAsync(DiscordEmoji.FromUnicode(emojiList[new Random().Next(emojiList.Length)])).ConfigureAwait(false);
        }

        private static async Task<string> GetImageUrlAsync(CommandContext ctx, string imageUrl)
        {
            var reactMsg = ctx.Message;
            if (GetImageAttachment(reactMsg).FirstOrDefault() is DiscordAttachment attachment)
                imageUrl = attachment.Url;
            imageUrl = imageUrl?.Trim();
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
                    && ctx.Channel.PermissionsFor(ctx.Client.GetMember(ctx.Guild, ctx.Client.CurrentUser)).HasPermission(Permissions.ReadMessageHistory))
                    try
                    {
                        var previousMessages = await ctx.Channel.GetMessagesBeforeAsync(ctx.Message.Id, 10).ConfigureAwait(false);
                        var (selectedMsg, selectedAttachment) = (
                            from m in previousMessages
                            where m.Attachments?.Count > 0
                            from a in GetImageAttachment(m)
                            select (m, a)
                        ).FirstOrDefault();
                        if (selectedMsg != null)
                            reactMsg = selectedMsg;
                        imageUrl = selectedAttachment?.Url;
                        if (string.IsNullOrEmpty(imageUrl))
                        {

                            var (selectedMsg2, selectedUrl) = (
                                from m in previousMessages
                                where m.Embeds?.Count > 0
                                from e in m.Embeds
                                let url = e.Image?.Url ?? e.Image?.ProxyUrl ?? e.Thumbnail?.Url ?? e.Thumbnail?.ProxyUrl
                                select (m, url)
                            ).FirstOrDefault();
                            if (selectedMsg2 != null)
                                reactMsg = selectedMsg2;
                            imageUrl = selectedUrl?.ToString();
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, "Failed to get link to the previously posted image");
                        await ctx.RespondAsync("Sorry chief, can't find any images in the recent posts").ConfigureAwait(false);
                    }
            }
            if (string.IsNullOrEmpty(imageUrl) || !Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
                await ctx.ReactWithAsync(Config.Reactions.Failure, "No proper image url was found").ConfigureAwait(false);
            return imageUrl;
        }
    }
}
