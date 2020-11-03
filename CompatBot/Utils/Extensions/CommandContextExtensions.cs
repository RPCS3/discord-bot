using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class CommandContextExtensions
    {
        internal static readonly Regex MessageLinkRegex = new Regex(@"(?:https?://)?discord(app)?\.com/channels/(?<guild>\d+)/(?<channel>\d+)/(?<message>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static async Task<DiscordMember> ResolveMemberAsync(this CommandContext ctx, string word)
        {
            try
            {
                var result = await ((IArgumentConverter<DiscordMember>)new DiscordMemberConverter()).ConvertAsync(word, ctx).ConfigureAwait(false);
                return result.HasValue ? result.Value : null;
            }
#pragma warning disable CS0168 // Variable is declared but never used
            catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
            {
#if DEBUG
                Config.Log.Warn(e, $"Failed to resolve member {word}");
#endif
                return null;
            }
        }

        public static async Task<DiscordChannel> CreateDmAsync(this CommandContext ctx)
        {
            return ctx.Channel.IsPrivate ? ctx.Channel : await ctx.Member.CreateDmChannelAsync().ConfigureAwait(false);
        }

        public static Task<DiscordChannel> GetChannelForSpamAsync(this CommandContext ctx)
        {
            return LimitedToSpamChannel.IsSpamChannel(ctx.Channel) ? Task.FromResult(ctx.Channel) : ctx.CreateDmAsync();
        }

        public static Task<string> GetUserNameAsync(this CommandContext ctx, ulong userId, bool? forDmPurposes = null, string defaultName = "Unknown user")
        {
            return ctx.Client.GetUserNameAsync(ctx.Channel, userId, forDmPurposes, defaultName);
        }

        public static Task<DiscordMessage> GetMessageAsync(this CommandContext ctx, string messageLink)
        {
            if (MessageLinkRegex.Match(messageLink) is Match m
                && ulong.TryParse(m.Groups["guild"].Value, out var guildId)
                && ulong.TryParse(m.Groups["channel"].Value, out var channelId)
                && ulong.TryParse(m.Groups["message"].Value, out var msgId)
                && ctx.Client.Guilds.TryGetValue(guildId, out var guild)
                && guild.GetChannel(channelId) is DiscordChannel channel)
                return channel.GetMessageAsync(msgId);
            return Task.FromResult<DiscordMessage>(null);
        }
    }
}