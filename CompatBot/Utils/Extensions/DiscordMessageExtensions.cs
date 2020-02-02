using System;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class DiscordMessageExtensions
    {
        public static Task<DiscordMessage> UpdateOrCreateMessageAsync(this DiscordMessage message, DiscordChannel channel, string content = null, bool tts = false, DiscordEmbed embed = null)
        {
            for (var i = 0; i<3; i++)
                try
                {
                    if (message == null)
                        return channel.SendMessageAsync(content, tts, embed);
                    return message.ModifyAsync(content, embed);
                }
                catch (Exception e)
                {
                    if (i == 2)
                        Config.Log.Error(e);
                    else
                        Task.Delay(100).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            return Task.FromResult((DiscordMessage)null);
        }
    }
}
