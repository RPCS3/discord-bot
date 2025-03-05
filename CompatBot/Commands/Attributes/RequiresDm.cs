using System.IO;
using System.Net.Http;

namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresDm: CheckBaseAttribute
{
    private static readonly Lazy<byte[]> Poster = new(() =>
    {
        using var client = HttpClientFactory.Create();
        return client.GetByteArrayAsync(Config.ImgSrcNotInPublic).ConfigureAwait(true).GetAwaiter().GetResult();
    });

    public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
    {
        if (ctx.Channel.IsPrivate || help)
            return true;

        await using var stream = new MemoryStream(Poster.Value);
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("senpai_plz.jpg", stream)).ConfigureAwait(false);
        return false;
    }
}