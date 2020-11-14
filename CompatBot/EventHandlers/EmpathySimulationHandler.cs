using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers
{
    using TCache = ConcurrentDictionary<ulong, ConcurrentQueue<DiscordMessage>>;

    internal static class EmpathySimulationHandler
    {
        private static readonly TCache MessageQueue = new();
        private static readonly TimeSpan ThrottleDuration = TimeSpan.FromHours(1);
        private static readonly MemoryCache Throttling = new(new MemoryCacheOptions {ExpirationScanFrequency = TimeSpan.FromMinutes(30)});

        public static async Task OnMessageCreated(DiscordClient _, MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
                return;

            if (args.Channel.IsPrivate)
                return;

            if (args.Author.IsCurrent)
                return;

            if (args.Author.Id == 197163728867688448ul)
                return;

            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                MessageQueue[args.Channel.Id] = queue = new ConcurrentQueue<DiscordMessage>();
            queue.Enqueue(args.Message);
            while (queue.Count > 10)
                queue.TryDequeue(out var _);
            var content = args.Message.Content;
            if (string.IsNullOrEmpty(content))
                return;

            if (Throttling.TryGetValue(args.Channel.Id, out List<DiscordMessage> mark) && content.Equals(mark.FirstOrDefault()?.Content, StringComparison.OrdinalIgnoreCase))
            {
                mark.Add(args.Message);
                Config.Log.Debug($"Bailed out of repeating '{content}' due to throttling");
                return;
            }

            var similarList = queue.Where(msg => content.Equals(msg.Content, StringComparison.OrdinalIgnoreCase)).ToList();
            if (similarList.Count > 2)
            {
                var uniqueUsers = similarList.Select(msg => msg.Author.Id).Distinct().Count();
                if (uniqueUsers > 2)
                {
                    Throttling.Set(args.Channel.Id, similarList, ThrottleDuration);
                    var msgContent = GetAvgContent(similarList.Select(m => m.Content).ToList());
                    var botMsg = await args.Channel.SendMessageAsync(msgContent, mentions: Config.AllowedMentions.UsersOnly).ConfigureAwait(false);
                    similarList.Add(botMsg);
                }
                else
                    Config.Log.Debug($"Bailed out of repeating '{content}' due to {uniqueUsers} unique users");
            }
        }

        public static Task OnMessageUpdated(DiscordClient _, MessageUpdateEventArgs e) => Backtrack(e.Channel, e.MessageBefore, false);
        public static Task OnMessageDeleted(DiscordClient _, MessageDeleteEventArgs e) => Backtrack(e.Channel, e.Message, true);

        private static async Task Backtrack(DiscordChannel channel, DiscordMessage message, bool removeFromQueue)
        {
            if (channel.IsPrivate)
                return;

            if (message.Author == null)
                return;

            if (message.Author.IsCurrent)
                return;

            if (!Throttling.TryGetValue(channel.Id, out List<DiscordMessage> msgList))
                return;

            if (msgList.Any(m => m.Id == message.Id))
            {
                var botMsg = msgList.Last();
                if (botMsg.Id == message.Id)
                    return;

                try
                {
                    await channel.DeleteMessageAsync(botMsg).ConfigureAwait(false);
                    if (removeFromQueue)
                        MessageQueue.TryRemove(message.Id, out _);
                }
                catch { }
            }
        }

        private static string GetAvgContent(List<string> samples)
        {
            var rng = new Random();
            var result = new StringBuilder(samples[0].Length);
            for (var i = 0; i < samples[0].Length; i++)
                result.Append(samples[rng.Next(samples.Count)][i]);
            return result.ToString();
        }
    }
}
