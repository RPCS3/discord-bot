using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils.ResultFormatters;
using PsnClient.POCOs;

namespace CompatBot.Commands;

internal static partial class Psn
{
    [Command("check")]
    [Description("Commands to check for various stuff on PSN")]
    internal static class Check
    {
        private static string? latestFwVersion;

        [Command("updates")]
        [Description("Check if game with the specified product code has any updates")]
        public static async ValueTask Updates(
            SlashCommandContext ctx,
            [Description("Product code such as `BLUS12345`"), MinMaxLength(9, 10)]
            [SlashAutoCompleteProvider<ProductCodeAutoCompleteProvider>]
            string productCode
        )
        {
            var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
            var id = ProductCodeLookup.GetProductIds(productCode).FirstOrDefault();
            if (id is not {Length: 9})
            {
                await ctx.RespondAsync($"`{productCode.Trim(10).Sanitize(replaceBackTicks: true)}` is not a valid product code", ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }
            
            await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
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
            await ctx.RespondAsync(embeds[0], ephemeral: ephemeral).ConfigureAwait(false);
            foreach (var embed in embeds.Skip(1).Take(5))
                await ctx.FollowupAsync(embed, ephemeral: ephemeral).ConfigureAwait(false);
        }

        [Command("firmware")]
        [Description("Get the latest PS3 firmware")]
        public static async ValueTask Firmware(SlashCommandContext ctx)
        {
            var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
            var embed = await GetFirmwareEmbedAsync().ConfigureAwait(false);
            await ctx.RespondAsync(embed, ephemeral: ephemeral).ConfigureAwait(false);
        }

        private static async ValueTask<DiscordEmbed> GetFirmwareEmbedAsync()
        {
            var fwList = await Client.GetHighestFwVersionAsync(Config.Cts.Token).ConfigureAwait(false);
            return fwList.ToEmbed();
        }

        private static async ValueTask CheckFwUpdateForAnnouncementAsync(DiscordClient client, List<FirmwareInfo>? fwList = null)
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

        internal static async ValueTask OnCheckUpdatesButtonClick(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            var btnId = e.Interaction.Data.CustomId;
            var parts = btnId.Split(':');
            if (parts is not [_, {Length: >6} authorIdStr, { Length: 9 } productCode]
                || !ulong.TryParse(authorIdStr, out var authorId))
            {
                Config.Log.Warn("Invalid interaction id: " + btnId);
                return;
            }

            try
            {
                if (e.User.Id != authorId)
                    return;
                    
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .AsEphemeral()
                        .WithContent($"Use application command `/psn check updates {productCode}` to search for game updates")
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Config.Log.Warn(ex);
            }
        }
    }
}
