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
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed class CompatList : BaseCommandModuleCustom
    {
        private static readonly Client client = new Client();
        private static SemaphoreSlim updateCheck = new SemaphoreSlim(1, 1);
        private static string lastUpdateInfo = null;

        [Command("compat"), Aliases("c")]
        [Description("Searches the compatibility database, USE: !compat searchterm")]
        public async Task Compat(CommandContext ctx, [RemainingText, Description("Game title to look up")] string title)
        {
            try
            {
                var requestBuilder = RequestBuilder.Start().SetSearch(title?.Truncate(40));
                await DoRequestAndRespond(ctx, requestBuilder).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                ctx.Client.DebugLogger.LogMessage(LogLevel.Error, "asdf", e.Message, DateTime.Now);
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

        [Command("latest"), Aliases("download"), TriggersTyping]
        [Description("Provides links to the latest RPCS3 build")]
        [Cooldown(1, 30, CooldownBucketType.Channel)]
        public Task Latest(CommandContext ctx)
        {
            return CheckForRpcs3Updates(ctx.Client, ctx.Channel);
        }

        public static async Task CheckForRpcs3Updates(DiscordClient discordClient, DiscordChannel channel)
        {
            var info = await client.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
            var embed = await info.AsEmbedAsync().ConfigureAwait(false);
            if (channel != null)
                await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
            var updateLinks = info?.LatestBuild?.Windows?.Download + info?.LatestBuild?.Linux?.Download;
            if (lastUpdateInfo != updateLinks && updateCheck.Wait(0))
                try
                {
                    var compatChannel = await discordClient.GetChannelAsync(Config.BotChannelId).ConfigureAwait(false);
                    lastUpdateInfo = updateLinks;
                    await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                }
                finally
                {
                    updateCheck.Release(1);
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
            Console.WriteLine(requestBuilder.Build());
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
                await channel.SendAutosplitMessageAsync(msg).ConfigureAwait(false);
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
                var footer = $"Retrieved from: *<{request.Build(false).ToString().Replace(' ', '+')}>* in {compatResult.RequestDuration.TotalMilliseconds:0} milliseconds!";

                if (returnCode.displayResults)
                {
                    result.Append("```");
                    foreach (var resultInfo in compatResult.Results.Take(request.amountRequested))
                    {
                        var info = resultInfo.AsString();
                        result.AppendLine(info);
                    }
                    result.Append("```");
                    yield return result.ToString();
                    yield return footer;
                }
                else if (returnCode.displayFooter)
                    yield return footer;
            }
        }
    }
}