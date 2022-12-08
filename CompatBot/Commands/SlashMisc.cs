using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using System.Threading.Tasks;

namespace CompatBot.Commands;

internal sealed class SlashMisc: BaseApplicationCommandModuleCustom
{
    [SlashCommand("about", "Bot information")]
    public async Task About(InteractionContext ctx)
    {
        var hcorion = ctx.Client.GetEmoji(":hcorion:", DiscordEmoji.FromUnicode("🍁"));
        var clienthax = ctx.Client.GetEmoji(":gooseknife:", DiscordEmoji.FromUnicode("🐱"));
        var embed = new DiscordEmbedBuilder
            {
                Title = "RPCS3 Compatibility Bot",
                Url = "https://github.com/RPCS3/discord-bot",
                Color = DiscordColor.Purple,
            }.AddField("Made by",
                $"""
                💮 13xforever
                🇭🇷 Roberto Anić Banić aka nicba1010
                {clienthax} clienthax
                """
            )
            .AddField("People who ~~broke~~ helped test the bot",
                $"""
                🐱 Juhn
                {hcorion} hcorion
                🙃 TGE
                🍒 Maru
                ♋ Tourghool
                """
            );
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AddEmbed(embed.Build()).AsEphemeral());
    }
}