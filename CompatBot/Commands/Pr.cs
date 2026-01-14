using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;

namespace CompatBot.Commands;

[Command("pr")]
[Description("Commands to list opened pull requests information")]
internal sealed class Pr
{
    private static readonly GithubClient.Client GithubClient = new(Config.GithubToken);

    [Command("search")]
    [Description("Search for open pull requests")]
    public static async ValueTask List(
        SlashCommandContext ctx,
        [Description("Pull request author username on GitHub")]
        string? author = null,
        [Description("Search for text in the pull request description")]
        string? search = null)
    {
        if (author is not { Length: > 0 } && search is not { Length: > 0 })
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} At least one argument must be provided", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        if (await GithubClient.GetOpenPrsAsync(Config.Cts.Token).ConfigureAwait(false) is not {} openPrList)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't retrieve open pull requests list, try again later", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (openPrList.Count is 0)
        {
            await ctx.RespondAsync("It looks like there are no open pull requests at the moment", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (author is {Length: >0} && search is {Length: >0})
        {
            openPrList = openPrList.Where(
                pr => pr.User?.Login?.Contains(author, StringComparison.InvariantCultureIgnoreCase) is true
                     && pr.Title?.Contains(search, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
        }
        else if (author is { Length: > 0 })
        {
            openPrList = openPrList.Where(
                pr => pr.User?.Login?.Contains(author, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
        }
        else if (search is {Length: >0})
        {
            openPrList = openPrList.Where(
                pr => pr.Title?.Contains(search, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
        }
        if (openPrList.Count is 0)
        {
            await ctx.RespondAsync("No open pull requests were found", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (openPrList is [{}item])
        {
            var msg = await PrUpdateInfoFormatter.GetPrBuildMessageAsync(ctx.Client, item.Number).ConfigureAwait(false);
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder(msg).AsEphemeral(ephemeral)).ConfigureAwait(false);
            return;
        }

        const int maxTitleLength = 80;
        var maxNum = openPrList.Max(i => i.Number).ToString().Length + 1;
        var maxAuthor = openPrList.Max(i => (i.User?.Login).GetVisibleLength());
        var maxTitle = Math.Min(openPrList.Max(i => i.Title.GetVisibleLength()), maxTitleLength);
        var result = new StringBuilder($"There are {openPrList.Count} open pull requests:\n");
        foreach (var pr in openPrList)
            result.Append('`').Append($"{("#" + pr.Number).PadLeft(maxNum)} by {pr.User?.Login?.PadRightVisible(maxAuthor)}: {pr.Title?.Trim(maxTitleLength).PadRightVisible(maxTitle)}".FixSpaces()).AppendLine($"` <{pr.HtmlUrl}>");
        var pages = AutosplitResponseHelper.AutosplitMessage(result.ToString(), blockStart: null, blockEnd: null);
        await ctx.RespondAsync(pages[0], ephemeral: ephemeral).ConfigureAwait(false);
    }

    [Command("build")]
    [Description("Link the latest available PR build")]
    public static async ValueTask Build(SlashCommandContext ctx, [Description("Pull request number")] int pr)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        var response = await PrUpdateInfoFormatter.GetPrBuildMessageAsync(ctx.Client, pr).ConfigureAwait(false);
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder(response).AsEphemeral(ephemeral)).ConfigureAwait(false);
    }

    [Command("merge")]
    [Description("Link to the official binary build produced after the specified PR was merged")]
    public static async ValueTask Link(SlashCommandContext ctx, [Description("Pull request number")] int pr)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        var msg = await PrUpdateInfoFormatter.GetPrBuildMessageAsync(ctx.Client, pr, true).ConfigureAwait(false);
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder(msg).AsEphemeral(ephemeral)).ConfigureAwait(false);
    }

#if DEBUG
    [Command("stats"), RequiresBotModRole]
    public static async ValueTask Stats(SlashCommandContext ctx)
    {
        var azureClient = Config.GetAzureDevOpsClient();
        var duration = await azureClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false);
        await ctx.RespondAsync(
            $"Expected pipeline duration (using {duration.BuildCount} builds): \n" +
            $"95%: {duration.Percentile95} ({duration.Percentile95.TotalMinutes})\n" +
            $"90%: {duration.Percentile90} ({duration.Percentile90.TotalMinutes})\n" +
            $"85%: {duration.Percentile85} ({duration.Percentile85.TotalMinutes})\n" +
            $"80%: {duration.Percentile80} ({duration.Percentile80.TotalMinutes})\n" +
            $"Avg: {duration.Mean} ({duration.Mean.TotalMinutes})\n" +
            $"Dev: {duration.StdDev} ({duration.StdDev.TotalMinutes})"
        ).ConfigureAwait(false);
    }
#endif

    public static async ValueTask<DiscordMessageBuilder?> GetIssueLinkMessageAsync(DiscordClient client, int issue)
    {
        var issueInfo = await GithubClient.GetIssueInfoAsync(issue, Config.Cts.Token).ConfigureAwait(false);
        if (issueInfo is null or {Number: 0})
            return null;

        if (issueInfo.PullRequest is not null)
            return await PrUpdateInfoFormatter.GetPrBuildMessageAsync(client, issue).ConfigureAwait(false);

        return new DiscordMessageBuilder().AddEmbed(issueInfo.AsEmbed());
    }
}
