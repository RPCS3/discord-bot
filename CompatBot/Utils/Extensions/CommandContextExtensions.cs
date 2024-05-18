using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;

namespace CompatBot.Utils;

public static partial class CommandContextExtensions
{
    [GeneratedRegex(
        @"(?:https?://)?discord(app)?\.com/channels/(?<guild>\d+)/(?<channel>\d+)/(?<message>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )]
    internal static partial Regex MessageLinkPattern();

    public static async Task<DiscordMember?> ResolveMemberAsync(this CommandContext ctx, string word)
    {
        try
        {
            var result = await ((IArgumentConverter<DiscordMember>)new DiscordMemberConverter()).ConvertAsync(word, ctx).ConfigureAwait(false);
            return result.HasValue ? result.Value : null;
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to resolve member {word}");
            return null;
        }
    }

    public static async Task<DiscordChannel> CreateDmAsync(this CommandContext ctx)
        => ctx.Channel.IsPrivate || ctx.Member is null ? ctx.Channel : await ctx.Member.CreateDmChannelAsync().ConfigureAwait(false);

    public static Task<DiscordChannel> GetChannelForSpamAsync(this CommandContext ctx)
        => LimitedToSpamChannel.IsSpamChannel(ctx.Channel) ? Task.FromResult(ctx.Channel) : ctx.CreateDmAsync();

    public static Task<string> GetUserNameAsync(this CommandContext ctx, ulong userId, bool? forDmPurposes = null, string defaultName = "Unknown user")
        => ctx.Client.GetUserNameAsync(ctx.Channel, userId, forDmPurposes, defaultName);

    public static Task<DiscordMessage?> GetMessageAsync(this CommandContext ctx, string messageLink)
    {
        if (MessageLinkPattern().Match(messageLink) is Match m
            && ulong.TryParse(m.Groups["guild"].Value, out var guildId)
            && ulong.TryParse(m.Groups["channel"].Value, out var channelId)
            && ulong.TryParse(m.Groups["message"].Value, out var msgId)
            && ctx.Client.Guilds.TryGetValue(guildId, out var guild)
            && guild.GetChannel(channelId) is DiscordChannel channel)
            return channel.GetMessageAsync(msgId);
        return Task.FromResult((DiscordMessage?)null);
    }
}