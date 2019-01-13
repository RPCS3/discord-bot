using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using CompatBot.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace CompatBot.EventHandlers
{
    internal static class FakeEmulatorFilter
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Multiline;
        private static readonly Regex fakeEmulatorLink = new Regex(@"(https?://)?(www\.)?pcsx4\.com", DefaultOptions); 
    
        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            args.Handled = !await CheckMessageForFakesAsync(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task OnMessageUpdated(MessageUpdateEventArgs args)
        {
            args.Handled = !await CheckMessageForFakesAsync(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task CheckBacklogAsync(DiscordClient client, DiscordGuild guild)
        {
            try
            {
                var after = DateTime.UtcNow - Config.ModerationTimeThreshold;
                foreach (var channel in guild.Channels.Where(ch => !ch.IsCategory))
                {
                    var messages = await channel.GetMessagesAsync(500).ConfigureAwait(false);
                    var messagesToCheck = from msg in messages
                                          where msg.CreationTimestamp > after
                                          select msg;
                    foreach (var message in messagesToCheck)
                        await CheckMessageForFakesAsync(client, message).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static async Task<bool> CheckMessageForFakesAsync(DiscordClient client, DiscordMessage message)
        {
            if (message.Channel.IsPrivate)
                return true;

            if (message.Author.IsBot)
                return true;

            if (message.Author.IsWhitelisted(client, message.Channel.Guild))
                return true;

            if (message.Reactions.Any(r => r.Emoji == Config.Reactions.Moderated && r.IsMe))
                return true;

            var fakes = GetFakes(message.Content);
            if (fakes == 0)
                return true;

            try
            {
                await message.DeleteAsync("link to fake emulator").ConfigureAwait(false);
                await message.Channel.SendMessageAsync($"{message.Author.Mention} linked emulator is proven to be fake and used for malicious purposes. Please avoid it in the future.").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                await message.ReactWithAsync(
                    client,
                    Config.Reactions.Moderated,
                    $"{message.Author.Mention} please delete this link to fake emulator. It was proven to be fake and used for malicious purposes. Please avoid it in the future."
                    ).ConfigureAwait(false);
            }
            return false;
        }

        public static int GetFakes( string message)
        {
            var fakeLinks = fakeEmulatorLink.Matches(message).Select(m => m.Groups["link"]?.Value).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();
            return fakeLinks.Count;
        }
    }
}
