using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers
{
    using TCache = ConcurrentDictionary<ulong, ConcurrentQueue<DiscordMessage>>;

    internal static class EmpathySimulationHandler
    {
        private static readonly TCache MessageQueue = new TCache();
        private static readonly TimeSpan ThrottleDuration = TimeSpan.FromMinutes(30);
        private static readonly MemoryCache Throttling = new MemoryCache(new MemoryCacheOptions {ExpirationScanFrequency = TimeSpan.FromMinutes(30)});

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return;

            if (args.Author.IsCurrent)
                return;

            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                MessageQueue[args.Channel.Id] = queue = new ConcurrentQueue<DiscordMessage>();
            queue.Enqueue(args.Message);
            while (queue.Count > 4)
                queue.TryDequeue(out _);
            var content = args.Message.Content;
            if (string.IsNullOrEmpty(content))
                return;

            if (Throttling.TryGetValue(args.Channel.Id, out object mark) && mark != null)
                return;

            var similarList = queue.Where(msg => content.Equals(msg.Content, StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (similarList.Count > 2 && similarList.Select(msg => msg.Author.Id).Distinct().Count() > 2)
            {
                Throttling.Set(args.Channel.Id, similarList, ThrottleDuration);
                var botMsg = await args.Channel.SendMessageAsync(content.ToLowerInvariant()).ConfigureAwait(false);
                similarList.Add(botMsg);
            }
        }

        public static Task OnMessageUpdated(MessageUpdateEventArgs e) => Backtrack(e.Channel, e.MessageBefore, false);
        public static Task OnMessageDeleted(MessageDeleteEventArgs e) => Backtrack(e.Channel, e.Message, true);

        private static async Task Backtrack(DiscordChannel channel, DiscordMessage message, bool removeFromQueue)
        {
            if (channel.IsPrivate)
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
    }
}
