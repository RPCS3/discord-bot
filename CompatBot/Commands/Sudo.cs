using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Commands.Converters;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    [Group("sudo"), RequiresBotSudoerRole]
    [Description("Used to manage bot moderators and sudoers")]
    internal sealed partial class Sudo : BaseCommandModuleCustom
    {
        [Command("say"), Priority(10)]
        [Description("Make bot say things, optionally in a specific channel")]
        public async Task Say(CommandContext ctx, [Description("Discord channel (can use just #name in DM)")] DiscordChannel channel, [RemainingText, Description("Message text to send")] string message)
        {
            if (channel.Type != ChannelType.Text)
            {
                Config.Log.Warn($"Resolved a {channel.Type} channel again for #{channel.Name}");
                var channelResult = await new TextOnlyDiscordChannelConverter().ConvertAsync(channel.Name, ctx).ConfigureAwait(false);
                if (channelResult.HasValue && channelResult.Value.Type == ChannelType.Text)
                    channel = channelResult.Value;
                else
                {
                    await ctx.RespondAsync($"Resolved a {channel.Type} channel again").ConfigureAwait(false);
                    return;
                }
            }

            var typingTask = channel.TriggerTypingAsync();
            // simulate bot typing the message at 300 cps
            await Task.Delay(message.Length * 10 / 3).ConfigureAwait(false);
            await channel.SendMessageAsync(message).ConfigureAwait(false);
            await typingTask.ConfigureAwait(false);
        }

        [Command("say"), Priority(10)]
        [Description("Make bot say things, optionally in a specific channel")]
        public Task Say(CommandContext ctx, [RemainingText, Description("Message text to send")] string message)
        {
            return Say(ctx, ctx.Channel, message);
        }

        [Command("react")]
        [Description("Add reactions to the specified message")]
        public async Task React(
            CommandContext ctx,
            [Description("Message link")] string messageLink,
            [RemainingText, Description("List of reactions to add")]string emojis
        )
        {
            try
            {
                var message = await ctx.GetMessageAsync(messageLink).ConfigureAwait(false);
                if (message == null)
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
                                emoji = emojis.Substring(i, endIdx - i);
                                i = endIdx - 1;
                                var emojiId = ulong.Parse(emoji.Substring(emoji.LastIndexOf(':') + 1));
                                de = DiscordEmoji.FromGuildEmote(ctx.Client, emojiId);
                            }
                            else
                                de = DiscordEmoji.FromUnicode(emoji + c);
                            emoji = "";
                            await message.ReactWithAsync(de).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception e)
            {
                Config.Log.Debug(e);
            }
        }

        [Command("log"), RequiresDm]
        [Description("Uploads current log file as an attachment")]
        public async Task Log(CommandContext ctx)
        {
            try
            {
                using (var log = File.Open(Config.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var result = new MemoryStream((int)Math.Min(Config.AttachmentSizeLimit, log.Length)))
                {
                    using (var gzip = new GZipStream(result, CompressionLevel.Optimal, true))
                    {
                        await log.CopyToAsync(gzip, Config.Cts.Token).ConfigureAwait(false);
                        await gzip.FlushAsync().ConfigureAwait(false);
                    }
                    if (result.Length <= Config.AttachmentSizeLimit)
                    {
                        result.Seek(0, SeekOrigin.Begin);
                        await ctx.RespondWithFileAsync(Path.GetFileName(Config.LogPath) + ".gz", result).ConfigureAwait(false);
                    }
                    else
                        await ctx.ReactWithAsync(Config.Reactions.Failure, "Compressed log size is too large, ask Nicba for help :(", true).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to upload current log");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to send the log", true).ConfigureAwait(false);
            }
        }
    }
}
