using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.VisualStudio.Services.Common;

namespace CompatBot.EventHandlers
{
    using TCache = ConcurrentDictionary<ulong, FixedLengthBuffer<ulong, DiscordMessage>>;

    internal static class GlobalMessageCache
    {
        private static readonly TCache MessageQueue = new TCache();
        private static readonly Func<DiscordMessage, ulong> KeyGen = m => m.Id;

        public static Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return Task.CompletedTask;

            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                lock (MessageQueue)
                {
                    if (!MessageQueue.TryGetValue(args.Channel.Id, out queue))
                        MessageQueue[args.Channel.Id] = queue = new FixedLengthBuffer<ulong, DiscordMessage>(KeyGen);
                }
            lock(queue.syncObj)
                queue.Add(args.Message);

            while (queue.Count > Config.ChannelMessageHistorySize)
                lock(queue.syncObj)
                    queue.TrimExcess();
            return Task.CompletedTask;
        }

        public static Task OnMessageDeleted(MessageDeleteEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return Task.CompletedTask;
            
            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                return Task.CompletedTask;

            lock (queue.syncObj)
                queue.Evict(args.Message.Id);
            return Task.CompletedTask;
        }

        public static Task OnMessagesBulkDeleted(MessageBulkDeleteEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return Task.CompletedTask;
            
            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                return Task.CompletedTask;

            lock (queue.syncObj)
                foreach (var m in args.Messages)
                    queue.Evict(m.Id);
            return Task.CompletedTask;
        }

        public static Task OnMessageUpdated(MessageUpdateEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return Task.CompletedTask;
            
            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                return Task.CompletedTask;
            
            lock(queue.syncObj)
                queue.AddOrReplace(args.Message);
            return Task.CompletedTask;
        }

        internal static Task<List<DiscordMessage>> GetMessagesCachedAsync(this DiscordChannel ch, int count = 100)
        {
            if (!MessageQueue.TryGetValue(ch.Id, out var queue))
                lock (MessageQueue)
                    if (!MessageQueue.TryGetValue(ch.Id, out queue))
                        MessageQueue[ch.Id] = queue = new FixedLengthBuffer<ulong, DiscordMessage>(KeyGen);
            List<DiscordMessage> result;
            lock(queue.syncObj)
                result = queue.Reverse().Take(count).ToList();
            var cacheCount = result.Count;
            var fetchCount = Math.Max(count - cacheCount, 0);
            if (fetchCount > 0)
            {
                IReadOnlyList<DiscordMessage> fetchedList;
                if (result.Any())
                    fetchedList = ch.GetMessagesBeforeAsync(result[0].Id, fetchCount).ConfigureAwait(false).GetAwaiter().GetResult();
                else
                    fetchedList = ch.GetMessagesAsync(fetchCount).ConfigureAwait(false).GetAwaiter().GetResult();
                result.AddRange(fetchedList);
                if (queue.Count < Config.ChannelMessageHistorySize)
                    lock (queue.syncObj)
                    {
                        // items in queue might've changed since the previous check at the beginning of this method
                        var freshCopy = queue.Reverse().ToList();
                        queue.Clear();
                        queue.AddRange(freshCopy.Concat(fetchedList).Distinct().Reverse());
                    }
            }
            return Task.FromResult(result);
        }

        internal static Task<List<DiscordMessage>> GetMessagesBeforeCachedAsync(this DiscordChannel ch, ulong msgId, int count = 100)
        {
            if (!MessageQueue.TryGetValue(ch.Id, out var queue))
                lock (MessageQueue)
                    if (!MessageQueue.TryGetValue(ch.Id, out queue))
                        MessageQueue[ch.Id] = queue = new FixedLengthBuffer<ulong, DiscordMessage>(KeyGen);
            List<DiscordMessage> result;
            lock(queue.syncObj)
                result = queue.Reverse().SkipWhile(m => m.Id >= msgId).Take(count).ToList();
            var cacheCount = result.Count;
            var fetchCount = Math.Max(count - cacheCount, 0);
            if (fetchCount > 0)
            {
                IReadOnlyList<DiscordMessage> fetchedList;
                if (result.Any())
                {
                    fetchedList = ch.GetMessagesBeforeAsync(result[0].Id, fetchCount).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (queue.Count < Config.ChannelMessageHistorySize)
                        lock (queue.syncObj)
                        {
                            // items in queue might've changed since the previous check at the beginning of this method
                            var freshCopy = queue.Reverse().ToList();
                            queue.Clear();
                            queue.AddRange(freshCopy.Concat(fetchedList).Distinct().Reverse());
                        }
                }
                else
                    fetchedList = ch.GetMessagesBeforeAsync(msgId, fetchCount).ConfigureAwait(false).GetAwaiter().GetResult();
                result.AddRange(fetchedList);
            }
            return Task.FromResult(result);

        }
    }
}