using System.IO;
using System.Net.Http;
using CompatApiClient.Compression;
using CompatBot.Commands.Converters;
using CompatBot.Database.Providers;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot.Commands;

[Command("sudo"), RequiresBotSudoerRole]
[Description("Used to manage bot moderators and sudoers")]
internal static partial class Sudo
{
    [Command("say"), RequiresDm]
    [Description("Make bot say things. Specify #channel or put message link in the beginning to specify where to reply")]
    public static async ValueTask Say(
        TextCommandContext ctx,
        [Description("Message text to send"), RemainingText] string message
    )
    {
        var channel = ctx.Channel;
        DiscordMessage? ogMsg = null;
        if (message.Split(' ', 2, StringSplitOptions.TrimEntries) is [{Length: >0} chOrLink, {Length: >0} msg])
        {
            if (await ctx.GetMessageAsync(chOrLink).ConfigureAwait(false) is DiscordMessage lnk)
            {
                ogMsg = lnk;
                channel = ogMsg.Channel;
                message = msg;
            }
            else
            {
                if (await ctx.ParseChannelNameAsync(chOrLink).ConfigureAwait(false) is {} ch)
                {
                    channel = ch;
                    message = msg;
                }
            }
        }
        if (channel is null)
            return;
        
        var typingTask = channel.TriggerTypingAsync();
        // simulate bot typing the message at 300 cps
        await Task.Delay(message.Length * 10 / 3).ConfigureAwait(false);
        var msgBuilder = new DiscordMessageBuilder().WithContent(message);
        if (ogMsg is not null)
            msgBuilder.WithReply(ogMsg.Id);
        if (ctx.Message.Attachments.Count > 0)
        {
            try
            {
                await using var memStream = Config.MemoryStreamManager.GetStream();
                using var client = HttpClientFactory.Create(new CompressionMessageHandler());
                await using var requestStream = await client.GetStreamAsync(ctx.Message.Attachments[0].Url!).ConfigureAwait(false);
                await requestStream.CopyToAsync(memStream).ConfigureAwait(false);
                memStream.Seek(0, SeekOrigin.Begin);
                msgBuilder.AddFile(ctx.Message.Attachments[0].FileName!, memStream);
                await channel.SendMessageAsync(msgBuilder).ConfigureAwait(false);
            }
            catch { }
        }
        else
            await channel.SendMessageAsync(msgBuilder).ConfigureAwait(false);
        await typingTask.ConfigureAwait(false);
    }

    [Command("react"), RequiresDm]
    [Description("Add reactions to the specified message")]
    public static async ValueTask React(
        TextCommandContext ctx,
        [Description("Message link")] string messageLink,
        [Description("List of reactions to add"), RemainingText]string emojis
    )
    {
        try
        {
            var message = await ctx.GetMessageAsync(messageLink).ConfigureAwait(false);
            if (message is null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't find the message").ConfigureAwait(false);
                return;
            }

            string emoji = "";
            for (var i = 0; i < emojis.Length; i++)
            {
                try
                {
                    var c = emojis[i];
                    if (char.IsHighSurrogate(c))
                        emoji += c;
                    else
                    {
                        DiscordEmoji de;
                        if (c == '<')
                        {
                            var endIdx = emojis.IndexOf('>', i);
                            if (endIdx < i)
                                endIdx = emojis.Length;
                            emoji = emojis[i..endIdx];
                            i = endIdx - 1;
                            var emojiId = ulong.Parse(emoji[(emoji.LastIndexOf(':') + 1)..]);
                            de = DiscordEmoji.FromGuildEmote(ctx.Client, emojiId);
                        }
                        else
                            de = DiscordEmoji.FromUnicode(emoji + c);
                        emoji = "";
                        await message.ReactWithAsync(de).ConfigureAwait(false);
                    }
                }
                catch { }
            }
        }
        catch (Exception e)
        {
            Config.Log.Debug(e);
        }
    }

    [Command("salt")]
    [Description("Regenerate salt for data anonymization. This WILL affect Hardware DB deduplication.")]
    public static async ValueTask ResetCryptoSalt(
        SlashCommandContext ctx,
        [Description("Should be `I understand this will break hardware survey deduplication`")]
        string confirmation
    )
    {
        if (confirmation is not "I understand this will break hardware survey deduplication")
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Operation cancelled.", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        var salt = new byte[256 / 8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        await Bot.Configuration.Set(ctx, nameof(Config.CryptoSalt), Convert.ToBase64String(salt)).ConfigureAwait(false);
    }

    [Command("mod"), LimitedToSpamChannel]
    internal static class Mod
    {
        [Command("list")]
        [Description("List all bot moderators")]
        public static async ValueTask List(TextCommandContext ctx)
        {
            var table = new AsciiTable(
                new AsciiColumn( "Username", maxWidth: 32),
                new AsciiColumn("Sudo")
            );
            foreach (var mod in ModProvider.Mods.Values.OrderByDescending(m => m.Sudoer))
                table.Add(await ctx.GetUserNameAsync(mod.DiscordId), mod.Sudoer ? "✅" :"");
            await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
        }
    }
    
    private static async ValueTask<DiscordChannel?> ParseChannelNameAsync(this TextCommandContext ctx, string channelName)
    {
        await using var scope = ctx.Extension.ServiceProvider.CreateAsyncScope();
        if (await TextOnlyDiscordChannelConverter.ConvertAsync(new TextConverterContext()
            {
                User = ctx.User,
                Channel = ctx.Channel,
                Message = ctx.Message,
                Command = ctx.Command,
                RawArguments = channelName,

                PrefixLength = ctx.Prefix?.Length ?? 0,
                Splicer = DefaultTextArgumentSplicer.Splice,
                Extension = ctx.Extension,
                ServiceScope = scope,
            }).ConfigureAwait(false) is { HasValue: true } ch)
            return ch.Value;
        return null;
    }
}
