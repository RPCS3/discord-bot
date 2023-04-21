using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class LimitedToHelpChannel: CheckBaseAttribute
{
    public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
    {
        if (ctx.Channel.IsPrivate || help)
            return true;

        if (IsHelpChannel(ctx.Channel))
            return true;

        await ctx.Channel.SendMessageAsync($"`{ctx.Prefix}{ctx.Command?.QualifiedName ?? ctx.RawArgumentString}` is limited to help channel and DMs").ConfigureAwait(false);
        return false;
    }
    
    internal static bool IsHelpChannel(DiscordChannel channel)
    {
        return channel.IsPrivate
               || channel.Name.Contains("help", StringComparison.OrdinalIgnoreCase)
               || channel.Name.Equals("donors", StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<DiscordChannel?> GetHelpChannelAsync(DiscordClient client, DiscordChannel channel, DiscordUser user)
    {
        var guild = channel.Guild;
        if (await client.GetMemberAsync(guild, user).ConfigureAwait(false) is {} member
            && await member.IsSupporterAsync(client, guild).ConfigureAwait(false)
            && guild.Channels.Values.FirstOrDefault(ch => ch.Type == ChannelType.Text && "donors".Equals(ch.Name, StringComparison.OrdinalIgnoreCase)) is {} donorsCh)
            return donorsCh;
        
        return guild.Channels.Values.FirstOrDefault(ch => ch.Type == ChannelType.Text && "help".Equals(ch.Name, StringComparison.OrdinalIgnoreCase));
    }
}