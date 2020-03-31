using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using PsnClient.POCOs;

namespace CompatBot.Commands
{
    internal sealed partial class Psn
    {
        [Group("check")]
        [Description("Commands to check for various stuff on PSN")]
        public sealed class Check: BaseCommandModuleCustom
        {
            private static string latestFwVersion = null;

            [Command("updates"), Aliases("update")]
            [Description("Checks if specified product has any updates")]
            public async Task Updates(CommandContext ctx, [RemainingText, Description("Product code such as `BLUS12345`")] string productCode)
            {

                var id = ProductCodeLookup.GetProductIds(productCode).FirstOrDefault();
                if (string.IsNullOrEmpty(id))
                {
                    var botMsg = await ctx.RespondAsync("Please specify a valid product code (e.g. BLUS12345 or NPEB98765):").ConfigureAwait(false);
                    var interact = ctx.Client.GetInteractivity();
                    var msg = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
                    await botMsg.DeleteAsync().ConfigureAwait(false);

                    if (string.IsNullOrEmpty(msg.Result?.Content))
                        return;

                    if (msg.Result.Content.StartsWith(Config.CommandPrefix) || msg.Result.Content.StartsWith(Config.AutoRemoveCommandPrefix))
                        return;

                    id = ProductCodeLookup.GetProductIds(msg.Result.Content).FirstOrDefault();
                    if (string.IsNullOrEmpty(id))
                    {
                        await ctx.ReactWithAsync(Config.Reactions.Failure, $"`{msg.Result.Content.Trim(10).Sanitize(replaceBackTicks: true)}` is not a valid product code").ConfigureAwait(false);
                        return;
                    }
                }

                List<DiscordEmbedBuilder> embeds;
                try
                {
                    var updateInfo = await Client.GetTitleUpdatesAsync(id, Config.Cts.Token).ConfigureAwait(false);
                    embeds = await updateInfo.AsEmbedAsync(ctx.Client, id).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to get title update info");
                    embeds = new List<DiscordEmbedBuilder>
                    {
                        new DiscordEmbedBuilder
                        {
                            Color = Config.Colors.Maintenance,
                            Title = "Service is unavailable",
                            Description = "There was an error communicating with the service. Try again in a few minutes.",
                        }
                    };
                }

                if (!ctx.Channel.IsPrivate
                    && ctx.Message.Author.Id == 197163728867688448
                    && (
                        embeds[0].Title.Contains("africa", StringComparison.InvariantCultureIgnoreCase) ||
                        embeds[0].Title.Contains("afrika", StringComparison.InvariantCultureIgnoreCase)
                    ))
                {
                    foreach (var embed in embeds)
                    {
                        var newTitle = "(๑•ิཬ•ั๑)";
                        var partStart = embed.Title.IndexOf(" [Part");
                        if (partStart > -1)
                            newTitle += embed.Title[partStart..];
                        embed.Title = newTitle;
                        if (!string.IsNullOrEmpty(embed.ThumbnailUrl))
                            embed.ThumbnailUrl = "https://cdn.discordapp.com/attachments/417347469521715210/516340151589535745/onionoff.png";
                    }
                    var sqvat = ctx.Client.GetEmoji(":sqvat:", Config.Reactions.No);
                    await ctx.Message.ReactWithAsync(sqvat).ConfigureAwait(false);
                }
                if (embeds.Count > 1 || embeds[0].Fields?.Count > 0)
                    embeds[^1] = embeds.Last().WithFooter("Note that you need to install ALL listed updates, one by one");
                foreach (var embed in embeds)
                    await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
            }

            [Command("content")]
            [Description("Adds PSN content id to the scraping queue")]
            public async Task Content(CommandContext ctx, [RemainingText, Description("Content IDs to scrape, such as `UP0006-NPUB30592_00-MONOPOLYPSNNA000`")] string contentIds)
            {
                if (string.IsNullOrEmpty(contentIds))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "No IDs were specified").ConfigureAwait(false);
                    return;
                }

                var matches = PsnScraper.ContentIdMatcher.Matches(contentIds.ToUpperInvariant());
                var itemsToCheck = matches.Select(m => m.Groups["content_id"].Value).ToList();
                if (itemsToCheck.Count == 0)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "No IDs were specified").ConfigureAwait(false);
                    return;
                }

                foreach (var id in itemsToCheck)
                    PsnScraper.CheckContentIdAsync(ctx, id, Config.Cts.Token);

                await ctx.ReactWithAsync(Config.Reactions.Success, $"Added {itemsToCheck.Count} ID{StringUtils.GetSuffix(itemsToCheck.Count)} to the scraping queue").ConfigureAwait(false);
            }

            [Command("firmware"), Aliases("fw")]
            [Cooldown(1, 10, CooldownBucketType.Channel)]
            [Description("Checks for latest PS3 firmware version")]
            public Task Firmware(CommandContext ctx) => GetFirmwareAsync(ctx);

            internal static async Task GetFirmwareAsync(CommandContext ctx)
            {
                var fwList = await Client.GetHighestFwVersionAsync(Config.Cts.Token).ConfigureAwait(false);
                var embed = fwList.ToEmbed();
                await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
            }

            internal static async Task CheckFwUpdateForAnnouncementAsync(DiscordClient client, List<FirmwareInfo> fwList = null)
            {
                fwList ??= await Client.GetHighestFwVersionAsync(Config.Cts.Token).ConfigureAwait(false);
                if (fwList.Count == 0)
                    return;

                var newVersion = fwList[0].Version;
                using var db = new BotDb();
                var fwVersionState = db.BotState.FirstOrDefault(s => s.Key == "Latest-Firmware-Version");
                latestFwVersion ??= fwVersionState?.Value;
                if (latestFwVersion is null
                    || (Version.TryParse(newVersion, out var newFw)
                        && Version.TryParse(latestFwVersion, out var oldFw)
                        && newFw > oldFw))
                {
                    var embed = fwList.ToEmbed().WithTitle("New PS3 Firmware Information");
                    var announcementChannel = await client.GetChannelAsync(Config.BotChannelId).ConfigureAwait(false);
                    await announcementChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                    latestFwVersion = newVersion;
                    if (fwVersionState == null)
                        db.BotState.Add(new BotState {Key = "Latest-Firmware-Version", Value = latestFwVersion});
                    else
                        fwVersionState.Value = latestFwVersion;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            internal static async Task MonitorFwUpdates(DiscordClient client, CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await CheckFwUpdateForAnnouncementAsync(client).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
