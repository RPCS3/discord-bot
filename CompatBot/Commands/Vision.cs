using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
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
using ImageProcessor.Imaging;
using ImageProcessor.Imaging.Formats;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Color = System.Drawing.Color;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace CompatBot.Commands
{
    [Cooldown(1, 5, CooldownBucketType.Channel)]
    [Group("describe")]
    [Description("Generates an image description from the attached image, or from the url")]
    internal sealed class Vision: BaseCommandModuleCustom
    {
        [GroupCommand]
        public async Task Describe(CommandContext ctx, [RemainingText] string imageUrl = null)
        {
            try
            {
                imageUrl = await GetImageUrlAsync(ctx, imageUrl).ConfigureAwait(false);
                if (string.IsNullOrEmpty(imageUrl))
                    return;

                var client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
                var result = await client.AnalyzeImageAsync(imageUrl, new List<VisualFeatureTypes> {VisualFeatureTypes.Description}, cancellationToken: Config.Cts.Token).ConfigureAwait(false);
                var captions = result.Description.Captions.OrderByDescending(c => c.Confidence).ToList();
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
                        msg += string.Join('\n', captions.Skip(1).Select(c => $"{c.Text} [{c.Confidence*100:0.00}%]"));
                    }
#endif
                }
                else
                    msg = "An image so weird, I have no words to describe it";
                await ctx.RespondAsync(msg).ConfigureAwait(false);
                if (result.Description.Tags.Count > 0)
                {
                    if (result.Description.Tags.Any(t => t == "cat"))
                        await ctx.Message.ReactWithAsync(DiscordEmoji.FromUnicode(BotStats.GoodKot[new Random().Next(BotStats.GoodKot.Length)])).ConfigureAwait(false);
                    if (result.Description.Tags.Any(t => t == "dog"))
                        await ctx.Message.ReactWithAsync(DiscordEmoji.FromUnicode(BotStats.GoodDog[new Random().Next(BotStats.GoodDog.Length)])).ConfigureAwait(false);
                }
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

                using var imageStream = new MemoryStream();
                using (var httpClient = HttpClientFactory.Create())
                using (var stream = await httpClient.GetStreamAsync(imageUrl).ConfigureAwait(false))
                    await stream.CopyToAsync(imageStream).ConfigureAwait(false);
                imageStream.Seek(0, SeekOrigin.Begin);
                using var imgFactory = new ImageProcessor.ImageFactory();
                using var img = imgFactory.Load(imageStream);
                imageStream.Seek(0, SeekOrigin.Begin);
                if (!ImageFormat.Jpeg.Equals(img.CurrentImageFormat.ImageFormat))
                    img.Format(new JpegFormat {Quality = 90});

                //resize and shrink file size to get under azure limits
                if (img.Image.Width > 4000 || img.Image.Height > 4000)
                {
                    img.Resize(new ResizeLayer(new Size(3840, 2160), ResizeMode.Min));
                    imageStream.SetLength(0);
                    img.Save(imageStream);
                    imageStream.Seek(0, SeekOrigin.Begin);
                }
                if (imageStream.Length > 4 * 1024 * 1024)
                {
                    imageStream.SetLength(0);
                    img.Quality(85).Save(imageStream);
                    imageStream.Seek(0, SeekOrigin.Begin);
                }

                var client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) { Endpoint = Config.AzureComputerVisionEndpoint };
                var result = await client.AnalyzeImageInStreamAsync(imageStream, new List<VisualFeatureTypes> {VisualFeatureTypes.Objects}, cancellationToken: Config.Cts.Token).ConfigureAwait(false);
                var objects = result.Objects.OrderByDescending(c => c.Confidence).ToList();
                var scale = Math.Max(1.0f, img.Image.Width / 400.0f);
                if (objects.Count > 0)
                {
                    //List<Color> palette = new List<Color> {Color.DeepSkyBlue, Color.GreenYellow, Color.Magenta,};
                    var analyzer = new ColorThief();
                    List<Color> palette;
                    using (var b = new Bitmap(img.Image))
                        palette = analyzer.GetPalette(b, Math.Max(objects.Count, 5), ignoreWhite: false).Select(c => c.Color.ToStandardColor().GetComplementary()).ToList();
                    if (palette.Count == 0)
                        palette = new List<Color> {Color.DeepSkyBlue, Color.GreenYellow, Color.Magenta,};
                    for (var i = 0; i < objects.Count; i++)
                    {
                        var obj = objects[i];
                        using var graphics = Graphics.FromImage(img.Image);
                        var color = palette[i % palette.Count];
                        var pen = new Pen(color, 2 * scale);
                        var r = obj.Rectangle;
                        graphics.DrawRectangle(pen, r.X, r.Y, r.W, r.H);
                        var text = new TextLayer
                        {
                            DropShadow = false,
                            FontColor = color,
                            FontSize = (int)(16 * scale),
                            Style = FontStyle.Bold,
                            //FontFamily = new FontFamily("Yu Gothic", new InstalledFontCollection()),
                            Text = $"{obj.ObjectProperty} ({obj.Confidence:P1})",
                            Position = new Point(r.X + 5, r.Y + 5),
                            
                        };
                        img.Watermark(text);
                    }
                    using var resultStream = new MemoryStream();
                    img.Save(resultStream);
                    await ctx.RespondWithFileAsync(Path.GetFileNameWithoutExtension(imageUrl) + "_tagged.jpg", resultStream).ConfigureAwait(false);
                }
                else
                    await ctx.RespondAsync("No objects detected").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to tag objects in an image");
                await ctx.RespondAsync("Failed to tag objects in the image").ConfigureAwait(false);
            }
        }

        internal static IEnumerable<DiscordAttachment> GetImageAttachment(DiscordMessage message)
            => message.Attachments.Where(a =>
                                             a.FileName.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                                             || a.FileName.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                                             || a.FileName.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
                //|| a.FileName.EndsWith(".webp", StringComparison.InvariantCultureIgnoreCase)
            );

        private static async Task<string> GetImageUrlAsync(CommandContext ctx, string imageUrl)
        {
            var reactMsg = ctx.Message;
            if (GetImageAttachment(reactMsg).FirstOrDefault() is DiscordAttachment attachment)
                imageUrl = attachment.Url;
            if (!Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
            {
                var str = imageUrl.ToLowerInvariant();
                if ((str.StartsWith("this")
                     || str.StartsWith("that")
                     || str.StartsWith("last")
                     || str.StartsWith("previous"))
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
