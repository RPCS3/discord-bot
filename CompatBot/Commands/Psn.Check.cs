using CompatApiClient;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using CompatBot.Utils.ResultFormatters;
using PsnClient.POCOs;

namespace CompatBot.Commands;

internal sealed partial class Psn
{
    [Command("check")]
    [Description("Commands to check for various stuff on PSN")]
    public sealed class Check
    {
        private static string? latestFwVersion;

        /*
        [Command("updates"), TextAlias("update"), LimitedToSpamChannel]
        [Description("Checks if specified product has any updates")]
        public async Task Updates(CommandContext ctx, [RemainingText, Description("Product code such as `BLUS12345`")] string productCode)
        {
            var providedId = productCode;
            var id = ProductCodeLookup.GetProductIds(productCode).FirstOrDefault();
            var askForId = true;
            DiscordMessage? botMsg = null;
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
                    var messageBuilder = new DiscordMessageBuilder()
                        .WithContent("Please select correct product code from the list or specify your own")
                        .WithReply(ctx.Message.Id);
                    foreach (var row in compatResult)
                        messageBuilder.AddComponents(row.Select(c => new DiscordButtonComponent(ButtonStyle.Secondary, "psn:check:updates:" + c, c)));
                    var interactivity = ctx.Client.GetInteractivity();
                    botMsg = await botMsg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
                    var reaction = await interactivity.WaitForMessageOrButtonAsync(botMsg, ctx.User, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                    if (reaction.reaction?.Id is {Length: >0} selectedId)
                        id = selectedId[^9..];
                    else if (reaction.text?.Content is {Length: >0} customId
                             && !customId.StartsWith(Config.CommandPrefix)
                             && !customId.StartsWith(Config.AutoRemoveCommandPrefix))
                    {
                        try{ await botMsg.DeleteAsync().ConfigureAwait(false); } catch {}
                        botMsg = null;
                        providedId = customId;
                        if (customId.Length > 8)
                            id = ProductCodeLookup.GetProductIds(customId).FirstOrDefault();
                    }
                }
            }
            if (string.IsNullOrEmpty(id) && askForId)
            {
                botMsg = await botMsg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify a valid product code (e.g. BLUS12345 or NPEB98765):").ConfigureAwait(false);
                var interact = ctx.Client.GetInteractivity();
                var msg = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);

                if (msg.Result?.Content is {Length: > 0} customId
                    && !customId.StartsWith(Config.CommandPrefix)
                    && !customId.StartsWith(Config.AutoRemoveCommandPrefix))
                {
                    try{ await botMsg.DeleteAsync().ConfigureAwait(false); } catch {}
                    botMsg = null;
                    providedId = customId;
                    if (customId.Length > 8)
                        id = ProductCodeLookup.GetProductIds(customId).FirstOrDefault();
                }
            }
            if (string.IsNullOrEmpty(id))
            {
                var msgBuilder = new DiscordMessageBuilder()
                    .WithContent($"`{providedId.Trim(10).Sanitize(replaceBackTicks: true)}` is not a valid product code")
                    .WithAllowedMentions(Config.AllowedMentions.Nothing);
                await botMsg.UpdateOrCreateMessageAsync(ctx.Channel, msgBuilder).ConfigureAwait(false);
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
                embeds =
                [
                    new()
                    {
                        Color = Config.Colors.Maintenance,
                        Title = "Service is unavailable",
                        Description = "There was an error communicating with the service. Try again in a few minutes.",
                    }
                ];
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
                        embed.WithThumbnail(Config.ImgSrcNoCompatAbuse);
                }
                var sqvat = ctx.Client.GetEmoji(":sqvat:", Config.Reactions.No)!;
                await ctx.Message.ReactWithAsync(sqvat).ConfigureAwait(false);
            }
            var resultMsgBuilder = new DiscordMessageBuilder()
                .AddEmbed(embeds[0])
                .WithReply(ctx.Message.Id);
            await botMsg.UpdateOrCreateMessageAsync(ctx.Channel, resultMsgBuilder).ConfigureAwait(false);
            foreach (var embed in embeds.Skip(1))
            {
                resultMsgBuilder = new DiscordMessageBuilder()
                    .AddEmbed(embed)
                    .WithReply(ctx.Message.Id);
                await ctx.Channel.SendMessageAsync(resultMsgBuilder).ConfigureAwait(false);
            }
        }

        [Command("content")]
        //[Hidden]
        [Description("Adds PSN content id to the scraping queue")]
        public async Task Content(CommandContext ctx, [RemainingText, Description("Content IDs to scrape, such as `UP0006-NPUB30592_00-MONOPOLYPSNNA000`")] string contentIds)
        {
            if (string.IsNullOrEmpty(contentIds))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "No IDs were specified").ConfigureAwait(false);
                return;
            }

            var matches = PsnScraper.ContentIdMatcher().Matches(contentIds.ToUpperInvariant());
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
        */

        [Command("firmware"), TextAlias("fw")]
        //[Cooldown(1, 10, CooldownBucketType.Channel)]
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

        internal static async Task OnCheckUpdatesButtonClick(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            var btnId = e.Interaction.Data.CustomId;
            var parts = btnId.Split(':');
            if (parts.Length != 4)
            {
                Config.Log.Warn("Invalid interaction id: " + btnId);
                return;
            }

            try
            {
                var authorId = ulong.Parse(parts[1]);
                var refMsgId = ulong.Parse(parts[2]);
                var productCode = parts[3];
                if (e.User.Id != authorId)
                    return;
                    
                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate).ConfigureAwait(false);
                await e.Message.DeleteAsync().ConfigureAwait(false);
                var refMsg = await e.Channel.GetMessageAsync(refMsgId).ConfigureAwait(false);
                /*
                var cne = client.GetCommandsNext();
                var cmd = cne.FindCommand("psn check updates", out _);
                var context = cne.CreateContext(refMsg, Config.CommandPrefix, cmd, productCode);
                await cne.ExecuteCommandAsync(context).ConfigureAwait(false);
                */
            }
            catch (Exception ex)
            {
                Config.Log.Warn(ex);
            }
        }
    }
}
