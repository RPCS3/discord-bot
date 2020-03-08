using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.Commands
{
    internal sealed class Vision: BaseCommandModuleCustom
    {
        [Command("describe"), RequiresBotModRole]
        [Description("Generates an image description")]
        public async Task Describe(CommandContext ctx, string imageUrl)
        {
            try
            {
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
                        double v when v > 0.5 => "I'm not very smart, so my best guess it's",
                        _ => "Ugh, idk? Might be",
                    };
                    msg = $"{confidence} {captions[0].Text}";
#if DEBUG
                    if (captions.Count > 1)
                    {
                        msg += "\nHowever, here are more guesses:\n";
                        msg += string.Join('\n', captions.Skip(1).Select(c => c.Text));
                    }
#endif
                }
                else
                    msg = "An image so weird, I have no words to describe it";
                await ctx.RespondAsync(msg).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get image description");
            }
        }
    }
}
