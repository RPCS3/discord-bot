using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class CommandContextExtensions
    {
        internal static readonly Regex MessageLinkRegex = new Regex(@"(?:https?://)?discordapp.com/channels/(?<guild>\d+)/(?<channel>\d+)/(?<message>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static async Task<DiscordChannel> CreateDmAsync(this CommandContext ctx)
        {
            return ctx.Channel.IsPrivate ? ctx.Channel : await ctx.Member.CreateDmChannelAsync().ConfigureAwait(false);
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