﻿using System.Globalization;
using System.Text.RegularExpressions;
using DSharpPlus.Commands.Converters;
using DSharpPlus.Commands.Processors.TextCommands;

namespace CompatBot.Commands.Converters;

internal sealed partial class TextOnlyDiscordChannelConverter : ITextArgumentConverter<DiscordChannel>
{
    [GeneratedRegex(@"^<#(\d+)>$", RegexOptions.ECMAScript)]
    private static partial Regex ChannelRegex();

    public string ReadableName => "Discord Channel Converter (custom)";
    public ConverterInputType RequiresText => ConverterInputType.Always;

    Task<Optional<DiscordChannel>> IArgumentConverter<DiscordChannel>.ConvertAsync(ConverterContext context)
        => ConvertAsync(context);
        
    public static async Task<Optional<DiscordChannel>> ConvertAsync(ConverterContext ctx)
    {
        var guildList = new List<DiscordGuild>(ctx.Client.Guilds.Count);
        if (ctx.Guild is null)
            foreach (var g in ctx.Client.Guilds.Keys)
                guildList.Add(await ctx.Client.GetGuildAsync(g).ConfigureAwait(false));
        else
            guildList.Add(ctx.Guild);

        if (ctx.Argument is ulong cid
            || ctx.Argument is string strArg && ulong.TryParse(strArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out cid))
        {
            var result = (
                from g in guildList
                from ch in g.Channels
                select ch
            ).FirstOrDefault(xc => xc.Key == cid && xc.Value?.Type == DiscordChannelType.Text);
            var ret = result.Value == null!
                ? Optional.FromNoValue<DiscordChannel>()
                : Optional.FromValue(result.Value);
            return ret;
        }

        if (ctx.Argument is not string value)
            return Optional.FromNoValue<DiscordChannel>();

        var m = ChannelRegex().Match(value);
        if (m.Success && ulong.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cid))
        {
            var result = (
                from g in guildList
                from ch in g.Channels
                select ch
            ).FirstOrDefault(xc => xc.Key == cid && xc.Value?.Type == DiscordChannelType.Text);
            var ret = result.Value == null!
                ? Optional.FromNoValue<DiscordChannel>()
                : Optional.FromValue(result.Value);
            return ret;
        }

        if (value.StartsWith('#'))
            value = value[1..];
        value = value.ToLowerInvariant();
        var chn = (
            from g in guildList
            from ch in g.Channels
            select ch
        ).FirstOrDefault(xc => xc.Value?.Name.ToLowerInvariant() == value && xc.Value?.Type == DiscordChannelType.Text);
        return chn.Value == null! ? Optional.FromNoValue<DiscordChannel>() : Optional.FromValue(chn.Value);
    }
}