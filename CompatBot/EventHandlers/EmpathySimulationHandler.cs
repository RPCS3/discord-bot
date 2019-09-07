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

            if (queue.All(msg => content.Equals(msg.Content, StringComparison.InvariantCultureIgnoreCase))
                && queue.Select(msg => msg.Author.Id).Distinct().Count() > 2)
            {
                var msgList = queue.ToList();
                Throttling.Set(args.Channel.Id, msgList, ThrottleDuration);
                var botMsg = await args.Channel.SendMessageAsync(content.ToLowerInvariant()).ConfigureAwait(false);
                msgList.Add(botMsg);
            }
        }

        public static Task OnMessageUpdated(MessageCreateEventArgs e) => Backtrack(e.Channel, e.Message.Id);
        public static Task OnMessageDeleted(MessageCreateEventArgs e) => Backtrack(e.Channel, e.Message.Id);

        private static async Task Backtrack(DiscordChannel channel, ulong messageId)
        {
            if (channel.IsPrivate)
                return;

            if (!Throttling.TryGetValue(channel.Id, out List<DiscordMessage> msgList))
                return;

            if (msgList.Any(m => m.Id == messageId))
            {
                var botMsg = msgList.Last();
                if (botMsg.Id == messageId)
                    return;

                try
                {
                    await channel.DeleteMessageAsync(botMsg).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }
}
