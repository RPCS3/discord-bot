using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus.EventArgs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.EventHandlers
{
    internal sealed class MediaScreenshotMonitor
    {
        private static readonly ComputerVisionClient client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};

        public static async Task OnMessageCreated(MessageCreateEventArgs e)
        {
            var message = e.Message;
            if (message == null)
                return;

#if !DEBUG
            if (message.Author.IsBotSafeCheck())
                return;
#endif

            if (!message.Attachments.Any())
                return;

            var images = Vision.GetImageAttachment(message).ToList();
            var tasks = new List<Task<BatchReadFileHeaders>>(images.Count);
            foreach (var img in images)
                tasks.Add(client.BatchReadFileAsync(img.Url, Config.Cts.Token));
            foreach (var t in tasks)
            {
                var headers = await t.ConfigureAwait(false);
                Config.Log.Trace($"Read result location url: {headers.OperationLocation}");
                ReadOperationResult result;
                do
                {
                    result = await client.GetReadOperationResultAsync(new Uri(headers.OperationLocation).Segments.Last(), Config.Cts.Token).ConfigureAwait(false);
                    if (result.Status == TextOperationStatusCodes.Succeeded)
                    {
                        foreach (var r in result.RecognitionResults)
                        foreach (var l in r.Lines)
                        {
                            Config.Log.Debug($"{message.Id} text: {l.Text}");
                            if (await ContentFilter.FindTriggerAsync(FilterContext.Log, l.Text).ConfigureAwait(false) is Piracystring hit)
                            {
                                await e.Client.ReportAsync("🖼 Screenshot of a pirated game", message, hit.String, l.Text, ReportSeverity.Medium).ConfigureAwait(false);
                            }
                        }
                    }
                } while (result.Status == TextOperationStatusCodes.Running || result.Status == TextOperationStatusCodes.NotStarted);
            }
        }
    }
}
