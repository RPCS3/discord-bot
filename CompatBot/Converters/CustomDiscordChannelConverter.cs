using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;

namespace CompatBot.Converters
{
    internal sealed class CustomDiscordChannelConverter : IArgumentConverter<DiscordChannel>
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
                ).FirstOrDefault(xc => xc.Id == cid);
                var ret = result == null ? Optional<DiscordChannel>.FromNoValue() : Optional<DiscordChannel>.FromValue(result);
                return ret;
            }

            var m = ChannelRegex.Match(value);
            if (m.Success && ulong.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cid))
            {
                var result = (
                    from g in guildList
                    from ch in g.Channels
                    select ch
                ).FirstOrDefault(xc => xc.Id == cid);
                var ret = result != null ? Optional<DiscordChannel>.FromValue(result) : Optional<DiscordChannel>.FromNoValue();
                return ret;
            }

            value = value.ToLowerInvariant();
            var chn = (
                from g in guildList
                from ch in g.Channels
                select ch
            ).FirstOrDefault(xc => xc.Name.ToLowerInvariant() == value);
            return chn != null ? Optional<DiscordChannel>.FromValue(chn) : Optional<DiscordChannel>.FromNoValue();
        }
    }
}