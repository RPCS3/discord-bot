using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class DiscordMessageExtensions
    {
        public static Task<DiscordMessage> UpdateOrCreateMessageAsync(this DiscordMessage message, DiscordChannel channel, string content = null, bool tts = false, DiscordEmbed embed = null)
        {
            try
            {
                if (message == null)
                    return channel.SendMessageAsync(content, tts, embed);
                return message.ModifyAsync(content, embed);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
            return Task.FromResult((DiscordMessage)null);
        }
    }
}
