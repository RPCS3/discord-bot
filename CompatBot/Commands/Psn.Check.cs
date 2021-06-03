using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using PsnClient.POCOs;

namespace CompatBot.Commands
{
    internal sealed partial class Psn
    {
        [Group("check")]
        [Description("Commands to check for various stuff on PSN")]
        public sealed class Check: BaseCommandModuleCustom
        {
            private static string? latestFwVersion;

            [Command("updates"), Aliases("update"), LimitedToSpamChannel]
            [Description("Checks if specified product has any updates")]
            public async Task Updates(CommandContext ctx, [RemainingText, Description("Product code such as `BLUS12345`")] string productCode)
            {
                var providedId = productCode;
                var id = ProductCodeLookup.GetProductIds(productCode).FirstOrDefault();
                var askForId = true;
                if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(productCode))
                {
                    var requestBuilder = RequestBuilder.Start().SetSearch(productCode);
                    var compatResult = CompatList.GetLocalCompatResult(requestBuilder)
                        .GetSortedList()
                        .Where(i => i.score > 0.8)
                        .Take(25)
                        .Select(i => i.code)
                        .Batch(5)
                        .ToList();
                    if (compatResult.Count > 0)
                    {
                        askForId = false;
                        var messageBuilder = new DiscordMessageBuilder().WithContent("Please select correct product code from the list or specify your own:");
                        foreach (var row in compatResult)
                            messageBuilder.AddComponents(row.Select(c => new DiscordButtonComponent(ButtonStyle.Secondary, c, c)));
                        var interactivity = ctx.Client.GetInteractivity();
                        var botMsg = await ctx.Channel.SendMessageAsync(messageBuilder).ConfigureAwait(false);
                        var reaction = await interactivity.WaitForMessageOrButtonAsync(botMsg, ctx.User, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                        if (reaction.reaction?.Id is {Length: 9} selectedId)
                            id = selectedId;
                        else if (reaction.text?.Content is {Length: >= 9} customId)
                        {
                            if (customId.StartsWith(Config.CommandPrefix) || customId.StartsWith(Config.AutoRemoveCommandPrefix))
                                return;

                            providedId = customId;
                            id = ProductCodeLookup.GetProductIds(customId).FirstOrDefault();
                        }
                        await botMsg.DeleteAsync().ConfigureAwait(false);
                    }
                }
                if (string.IsNullOrEmpty(id) && askForId)
                {
                    var botMsg = await ctx.Channel.SendMessageAsync("Please specify a valid product code (e.g. BLUS12345 or NPEB98765):").ConfigureAwait(false);
                    var interact = ctx.Client.GetInteractivity();
                    var msg = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
                    await botMsg.DeleteAsync().ConfigureAwait(false);

                    if (string.IsNullOrEmpty(msg.Result?.Content))
                        return;

                    if (msg.Result.Content.StartsWith(Config.CommandPrefix) || msg.Result.Content.StartsWith(Config.AutoRemoveCommandPrefix))
                        return;

                    providedId = msg.Result.Content;
                    id = ProductCodeLookup.GetProductIds(msg.Result.Content).FirstOrDefault();
                }
                if (string.IsNullOrEmpty(id))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"`{providedId.Trim(10).Sanitize(replaceBackTicks: true)}` is not a valid product code").ConfigureAwait(false);
                    return;
                }
                List<DiscordEmbedBuilder> embeds;
                try
                {
                    var updateInfo = await TitleUpdateInfoProvider.GetAsync(id, Config.Cts.Token).ConfigureAwait(false);
                    embeds = await updateInfo.AsEmbedAsync(ctx.Client, id).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to get title update info");
                    embeds = new()
                    {
                        new()
                        {
                            Color = Config.Colors.Maintenance,
                            Title = "Service is unavailable",
                            Description = "There was an error communicating with the service. Try again in a few minutes.",
                        }
                    };
                }

                if (ctx.IsOnionLike()
                    && (embeds[0].Title.Contains("africa", StringComparison.InvariantCultureIgnoreCase)
                        || embeds[0].Title.Contains("afrika", StringComparison.InvariantCultureIgnoreCase)))
                {
                    foreach (var embed in embeds)
                    {
                        var newTitle = "(๑•ิཬ•ั๑)";
                        var partStart = embed.Title.IndexOf(" [Part", StringComparison.Ordinal);
                        if (partStart > -1)
                            newTitle += embed.Title[partStart..];
                        embed.Title = newTitle;
                        if (!string.IsNullOrEmpty(embed.Thumbnail?.Url))
                            embed.WithThumbnail("https://cdn.discordapp.com/attachments/417347469521715210/516340151589535745/onionoff.png");
                    }
                    var sqvat = ctx.Client.GetEmoji(":sqvat:", Config.Reactions.No)!;
                    await ctx.Message.ReactWithAsync(sqvat).ConfigureAwait(false);
                }
                if (embeds.Count > 1 || embeds[0].Fields.Count > 0)
                    embeds[^1] = embeds.Last().WithFooter("Note that you need to install ALL listed updates, one by one");
                foreach (var embed in embeds)
                    await ctx.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            }

            [Command("content"), Hidden]
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
                await ctx.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            }

            internal static async Task CheckFwUpdateForAnnouncementAsync(DiscordClient client, List<FirmwareInfo>? fwList = null)
            {
                fwList ??= await Client.GetHighestFwVersionAsync(Config.Cts.Token).ConfigureAwait(false);
                if (fwList.Count == 0)
                    return;

                var newVersion = fwList[0].Version;
                await using var db = new BotDb();
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
                        await db.BotState.AddAsync(new() {Key = "Latest-Firmware-Version", Value = latestFwVersion}).ConfigureAwait(false);
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
