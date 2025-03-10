using CompatBot.Commands.Attributes;
using DSharpPlus.Commands.ContextChecks;

namespace CompatBot.Commands.Checks;

internal class LimitedToSpecificChannelsCheck:
    IContextCheck<LimitedToHelpChannelAttribute>,
    IContextCheck<LimitedToOfftopicChannelAttribute>,
    IContextCheck<LimitedToSpamChannelAttribute>,
    IContextCheck<RequiresDmAttribute>,
    IContextCheck<RequiresNotMediaAttribute>
{
    internal static bool IsHelpChannel(DiscordChannel channel)
        => channel.IsPrivate
           || channel.Name.Contains("help", StringComparison.OrdinalIgnoreCase)
           || channel.Name.Equals("donors", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOfftopicChannel(DiscordChannel channel)
        => channel.Name.Contains("off-topic", StringComparison.InvariantCultureIgnoreCase)
           || channel.Name.Contains("offtopic", StringComparison.InvariantCultureIgnoreCase);

    internal static bool IsSpamChannel(DiscordChannel channel)
        => channel.IsPrivate
           || channel.Name.Contains("spam", StringComparison.OrdinalIgnoreCase)
           || channel.Name.Equals("testers", StringComparison.OrdinalIgnoreCase);

    internal static async Task<DiscordChannel?> GetHelpChannelAsync(DiscordClient client, DiscordChannel channel, DiscordUser user)
    {
        var guild = channel.Guild;
        if (await client.GetMemberAsync(guild, user).ConfigureAwait(false) is {} member
            && await member.IsSupporterAsync(client, guild).ConfigureAwait(false)
            && guild.Channels.Values.FirstOrDefault(ch => ch.Type == DiscordChannelType.Text && "donors".Equals(ch.Name, StringComparison.OrdinalIgnoreCase)) is {} donorsCh)
            return donorsCh;
        return guild.Channels.Values.FirstOrDefault(ch => ch.Type == DiscordChannelType.Text && "help".Equals(ch.Name, StringComparison.OrdinalIgnoreCase));
    }
    
    public ValueTask<string?> ExecuteCheckAsync(LimitedToHelpChannelAttribute attr, CommandContext ctx)
    {
        if (ctx.Channel.IsPrivate || IsHelpChannel(ctx.Channel))
            return ValueTask.FromResult<string?>(null);
        return ValueTask.FromResult("This command is limited to help channel and DMs")!;
    }

    public ValueTask<string?> ExecuteCheckAsync(LimitedToOfftopicChannelAttribute attr, CommandContext ctx)
    {
        if (IsSpamChannel(ctx.Channel) || IsOfftopicChannel(ctx.Channel))
            return ValueTask.FromResult<string?>(null);
        return ValueTask.FromResult("This command is limited to off-topic channels and DMs")!;
    }

    public async ValueTask<string?> ExecuteCheckAsync(LimitedToSpamChannelAttribute attr, CommandContext ctx)
    {
        if (IsSpamChannel(ctx.Channel))
            return null;

        var spamChannel = await ctx.Client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
        return $"This command is limited to {spamChannel.Mention} and DMs";
    }

    public ValueTask<string?> ExecuteCheckAsync(RequiresDmAttribute attr, CommandContext ctx)
    {
        if (ctx.Channel.IsPrivate)
            return ValueTask.FromResult<string?>(null);
        return ValueTask.FromResult("Not in public, senpai o(≧∀≦)o")!;
    }

    public ValueTask<string?> ExecuteCheckAsync(RequiresNotMediaAttribute attr, CommandContext ctx)
    {
        if (ctx.Channel.Name != "media")
            return ValueTask.FromResult<string?>(null);
        return ValueTask.FromResult<string?>("");
    }
}