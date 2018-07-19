using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed class CompatList : BaseCommandModule
    {
        private static readonly Client client = new Client();

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
    !top 10 new jpn
    !top 10 playable
    !top 10 new ingame eu
    !top 10 old psn intro
    !top 10 old loadable us bluray")]
        public async Task Top(CommandContext ctx, [Description("To see all filters do !filters")] params string[] filters)
        {
            var requestBuilder = RequestBuilder.Start();
            var age = "new";
            var amount = 10;
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
                    case string reg when ApiConfig.ReverseRegions.ContainsKey(reg):
                        requestBuilder.SetRegion(reg);
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

        [Command("filters")]
        [Description("Provides information about available filters for the !top command")]
        public async Task Filters(CommandContext ctx)
        {
            var getDmTask = ctx.CreateDmAsync();
            if (ctx.Channel.IsPrivate)
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
            var embed = new DiscordEmbedBuilder {Description = "List of recognized tokens in each filter category", Color = Config.Colors.Help}
                .AddField("Regions", DicToDesc(ApiConfig.Regions))
                .AddField("Statuses", DicToDesc(ApiConfig.Statuses))
                .AddField("Release types", DicToDesc(ApiConfig.ReleaseTypes))
                .Build();
            var dm = await getDmTask.ConfigureAwait(false);
            await dm.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }

        [Command("latest"), Aliases("download")]
        [Description("Provides links to the latest RPCS3 build")]
        public async Task Latest(CommandContext ctx)
        {
            var getDmTask = ctx.CreateDmAsync();
            if (ctx.Channel.IsPrivate)
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
            var info = await client.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
            var embed = await info.AsEmbedAsync().ConfigureAwait(false);
            var dm = await getDmTask.ConfigureAwait(false);
            await dm.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
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
            var botChannelTask = ctx.Client.GetChannelAsync(Config.BotChannelId);
            Console.WriteLine(requestBuilder.Build());
            var result = await client.GetCompatResultAsync(requestBuilder, Config.Cts.Token).ConfigureAwait(false);
            var botChannel = await botChannelTask.ConfigureAwait(false);
            foreach (var msg in FormatSearchResults(ctx, result))
                await botChannel.SendAutosplitMessageAsync(msg).ConfigureAwait(false);
        }

        private IEnumerable<string> FormatSearchResults(CommandContext ctx, CompatResult compatResult)
        {
            var returnCode = ApiConfig.ReturnCodes[compatResult.ReturnCode];
            var request = compatResult.RequestBuilder;

            if (returnCode.overrideAll)
                yield return string.Format(returnCode.info, ctx.Message.Author.Mention);
            else
            {
                var result = new StringBuilder();
                if (string.IsNullOrEmpty(request.customHeader))
                    result.AppendLine($"{ctx.Message.Author.Mention} searched for: ***{request.search.Sanitize()}***");
                else
                {
                    string[] region = null, media = null;
                    if (request.region.HasValue) ApiConfig.Regions.TryGetValue(request.region.Value, out region);
                    if (request.releaseType.HasValue) ApiConfig.Regions.TryGetValue(request.releaseType.Value, out media);
                    var formattedHeader = string.Format(request.customHeader, ctx.Message.Author.Mention, request.amountRequested, region?.Last(), media?.Last());
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