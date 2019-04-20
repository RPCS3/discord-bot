using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;

namespace CompatBot.Commands.Converters
{
    internal sealed class TextOnlyDiscordChannelConverter : IArgumentConverter<DiscordChannel>
    {
        private static Regex ChannelRegex { get; } = new Regex(@"^<#(\d+)>$", RegexOptions.ECMAScript | RegexOptions.Compiled);

        public async Task<Optional<DiscordChannel>> ConvertAsync(string value, CommandContext ctx)
        {
            var guildList = new List<DiscordGuild>(ctx.Client.Guilds.Count);
            if (ctx.Guild == null)
                foreach (var g in ctx.Client.Guilds.Keys)
                    guildList.Add(await ctx.Client.GetGuildAsync(g).ConfigureAwait(false));
            else
                guildList.Add(ctx.Guild);

            if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cid))
            {
                var result = (
                    from g in guildList
                    from ch in g.Channels
                    select ch
                ).FirstOrDefault(xc => xc.Key == cid && xc.Value?.Type == ChannelType.Text);
                var ret = result.Value == null ? Optional.FromNoValue<DiscordChannel>() : Optional.FromValue(result.Value);
                return ret;
            }

            var m = ChannelRegex.Match(value);
            if (m.Success && ulong.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cid))
            {
                var result = (
                    from g in guildList
                    from ch in g.Channels
                    select ch
                ).FirstOrDefault(xc => xc.Key == cid && xc.Value?.Type == ChannelType.Text);
                var ret = result.Value == null ? Optional.FromNoValue<DiscordChannel>() : Optional.FromValue(result.Value);
                return ret;
            }

            if (value.StartsWith('#'))
                value = value.Substring(1);
            value = value.ToLowerInvariant();
            var chn = (
                from g in guildList
                from ch in g.Channels
                select ch
            ).FirstOrDefault(xc => xc.Value?.Name.ToLowerInvariant() == value && xc.Value?.Type == ChannelType.Text);
            return chn.Value == null ? Optional.FromNoValue<DiscordChannel>() : Optional.FromValue(chn.Value);
        }
    }
}