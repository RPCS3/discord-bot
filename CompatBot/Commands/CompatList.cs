using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    internal sealed class CompatList : BaseCommandModuleCustom
    {
        private static readonly Client client = new Client();
        private static SemaphoreSlim updateCheck = new SemaphoreSlim(1, 1);
        private static string lastUpdateInfo = null;
        private const string Rpcs3UpdateStateKey = "Rpcs3UpdateState";
        private static UpdateInfo CachedUpdateInfo = null;

        static CompatList()
        {
            using (var db = new BotDb())
                lastUpdateInfo = db.BotState.FirstOrDefault(k => k.Key == Rpcs3UpdateStateKey)?.Value;
        }

        [Command("compat"), Aliases("c")]
        [Description("Searches the compatibility database, USE: !compat search term")]
        public async Task Compat(CommandContext ctx, [RemainingText, Description("Game title to look up")] string title)
        {
            title = title?.TrimEager().Truncate(40);
            if (string.IsNullOrEmpty(title))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "You should specify what you're looking for").ConfigureAwait(false);
                return;
            }

            try
            {
                var requestBuilder = RequestBuilder.Start().SetSearch(title);
                await DoRequestAndRespond(ctx, requestBuilder).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to get compat list info");
            }
        }

        [Command("top")]
        [Description(@"
Gets the x (default is 10 new) top games by specified criteria; order is flexible
Example usage:
    !top 10 new
    !top 10 playable
    !top 10 new ingame
    !top 10 old loadable bluray")]
        public async Task Top(CommandContext ctx, [Description("To see all filters do !filters")] params string[] filters)
        {
            var requestBuilder = RequestBuilder.Start();
            var age = "new";
            var amount = ApiConfig.ResultAmount[0];
            foreach (var term in filters.Select(s => s.ToLowerInvariant()))
            {
                switch (term)
                {
                    case "old": case "new":
                        age = term;
                        break;
                    case string status when ApiConfig.Statuses.ContainsKey(status):
                        requestBuilder.SetStatus(status);
                        break;
                    case string rel when ApiConfig.ReverseReleaseTypes.ContainsKey(rel):
                        requestBuilder.SetReleaseType(rel);
                        break;
                    case string num when int.TryParse(num, out var newAmount):
                        amount = newAmount.Clamp(1, Config.TopLimit);
                        break;
                }
            }
            requestBuilder.SetAmount(amount);
            if (age == "old")
            {
                requestBuilder.SetSort("date", "asc");
                requestBuilder.SetHeader("{0} requested top {1} oldest {2} {3} updated games");
            }
            else
            {
                requestBuilder.SetSort("date", "desc");
                requestBuilder.SetHeader("{0} requested top {1} newest {2} {3} updated games");
            }
            await DoRequestAndRespond(ctx, requestBuilder).ConfigureAwait(false);
        }

        [Command("filters"), TriggersTyping(InDmOnly = true)]
        [Description("Provides information about available filters for the !top command")]
        public async Task Filters(CommandContext ctx)
        {
            var getDmTask = ctx.CreateDmAsync();
            var embed = new DiscordEmbedBuilder {Description = "List of recognized tokens in each filter category", Color = Config.Colors.Help}
                .AddField("Statuses", DicToDesc(ApiConfig.Statuses))
                .AddField("Release types", DicToDesc(ApiConfig.ReleaseTypes))
                .Build();
            var dm = await getDmTask.ConfigureAwait(false);
            await dm.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }

        [Group("latest"), Aliases("download"), TriggersTyping]
        [Description("Provides links to the latest RPCS3 build")]
        [Cooldown(1, 30, CooldownBucketType.Channel)]
        public sealed class UpdatesCheck: BaseCommandModuleCustom
        {
            [GroupCommand]
            public Task Latest(CommandContext ctx)
            {
                return CheckForRpcs3Updates(ctx.Client, ctx.Channel);
            }

            [Command("cached")]
            [Description("Gets the latest known update links, without checking the API")]
            public async Task Cached(CommandContext ctx)
            {
                var tmp = CachedUpdateInfo;
                if (tmp is UpdateInfo info)
                {
                    var embed = await info.AsEmbedAsync().ConfigureAwait(false);
                    await ctx.RespondAsync(embed: embed.Build()).ConfigureAwait(false);
                }
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "No update information was cached yet").ConfigureAwait(false);
            }

            public static async Task<bool> CheckForRpcs3Updates(DiscordClient discordClient, DiscordChannel channel)
            {
                var info = await client.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
                var embed = await info.AsEmbedAsync().ConfigureAwait(false);
                if (channel != null)
                    await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                var updateLinks = info?.LatestBuild?.Pr;
                if (!string.IsNullOrEmpty(updateLinks) && lastUpdateInfo != updateLinks && updateCheck.Wait(0))
                    try
                    {
                        var compatChannel = await discordClient.GetChannelAsync(Config.BotChannelId).ConfigureAwait(false);
                        await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                        lastUpdateInfo = updateLinks;
                        CachedUpdateInfo = info;
                        using (var db = new BotDb())
                        {
                            var currentState = await db.BotState.FirstOrDefaultAsync(k => k.Key == Rpcs3UpdateStateKey).ConfigureAwait(false);
                            if (currentState == null)
                                db.BotState.Add(new BotState {Key = Rpcs3UpdateStateKey, Value = updateLinks});
                            else
                                currentState.Value = updateLinks;
                            await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                            return true;
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, "Failed to check for RPCS3 update info");
                    }
                    finally
                    {
                        updateCheck.Release(1);
                    }
                return false;
            }
        }

        private static string DicToDesc(Dictionary<char, string[]> dictionary)
        {
            var result = new StringBuilder();
            foreach (var lst in dictionary.Values)
                result.AppendLine(string.Join(", ", lst.Reverse()));
            return result.ToString();
        }

        private static string DicToDesc(Dictionary<string, int> dictionary)
        {
            return string.Join(", ", dictionary.Keys);
        }

        private async Task DoRequestAndRespond(CommandContext ctx, RequestBuilder requestBuilder)
        {
            Config.Log.Info(requestBuilder.Build());
            CompatResult result;
            try
            {
                result = await client.GetCompatResultAsync(requestBuilder, Config.Cts.Token).ConfigureAwait(false);
            }
            catch
            {
                await ctx.RespondAsync(embed: TitleInfo.CommunicationError.AsEmbed(null)).ConfigureAwait(false);
                return;
            }

            var channel = ctx.Channel.IsPrivate ? ctx.Channel : await ctx.Client.GetChannelAsync(Config.BotChannelId).ConfigureAwait(false);
            foreach (var msg in FormatSearchResults(ctx, result))
                await channel.SendAutosplitMessageAsync(msg, blockStart:"", blockEnd:"").ConfigureAwait(false);
        }

        private IEnumerable<string> FormatSearchResults(CommandContext ctx, CompatResult compatResult)
        {
            var returnCode = ApiConfig.ReturnCodes[compatResult.ReturnCode];
            var request = compatResult.RequestBuilder;

            if (returnCode.overrideAll)
                yield return string.Format(returnCode.info, ctx.Message.Author.Mention);
            else
            {
                var authorMention = ctx.Channel.IsPrivate ? "You" : ctx.Message.Author.Mention;
                var result = new StringBuilder();
                if (string.IsNullOrEmpty(request.customHeader))
                {
                    result.AppendLine($"{authorMention} searched for: ***{request.search.Sanitize()}***");
                    if (request.search.Contains("persona", StringComparison.InvariantCultureIgnoreCase))
                        result.AppendLine("Did you try searching for ***Unnamed*** instead?");
                }
                else
                {
                    var formattedHeader = string.Format(request.customHeader, authorMention, request.amountRequested, null, null);
                    result.AppendLine(formattedHeader.Replace("   ", " ").Replace("  ", " "));
                }
                result.AppendFormat(returnCode.info, compatResult.SearchTerm);
                yield return result.ToString();

                result.Clear();

                if (returnCode.displayResults)
                {
                    foreach (var resultInfo in compatResult.Results.Take(request.amountRequested))
                    {
                        var info = resultInfo.AsString();
                        result.AppendLine(info);
                    }
                    yield return result.ToString();
                }
            }
        }
    }
}