using System.Linq;
using System.Linq.Expressions;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.Services.Common;

namespace CompatBot.EventHandlers;

using TCache = ConcurrentDictionary<ulong, FixedLengthBuffer<ulong, DiscordMessage>>;

internal static class GlobalMessageCache
{
    private static readonly TCache MessageQueue = new();
    private static readonly Func<DiscordMessage, ulong> KeyGen = m => m.Id;

    public static Task OnMessageCreated(DiscordClient _, MessageCreatedEventArgs args)
    {
        if (args.Channel.IsPrivate)
            return Task.CompletedTask;

        if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
            lock (MessageQueue)
            {
                if (!MessageQueue.TryGetValue(args.Channel.Id, out queue))
                    MessageQueue[args.Channel.Id] = queue = new(KeyGen);
            }
        lock(queue.SyncObj)
            queue.Add(args.Message);

        while (queue.Count > Config.ChannelMessageHistorySize)
            lock(queue.SyncObj)
                queue.TrimExcess();
        return Task.CompletedTask;
    }

    public static Task OnMessageDeleted(DiscordClient _, MessageDeletedEventArgs args)
    {
        if (args.Channel?.IsPrivate ?? true)
            return Task.CompletedTask;
            
        if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
            return Task.CompletedTask;

        lock (queue.SyncObj)
            queue.Evict(args.Message.Id);
        return Task.CompletedTask;
    }

    public static Task OnMessagesBulkDeleted(DiscordClient _, MessagesBulkDeletedEventArgs args)
    {
        if (args.Channel.IsPrivate)
            return Task.CompletedTask;
            
        if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
            return Task.CompletedTask;

        lock (queue.SyncObj)
            foreach (var m in args.Messages)
                queue.Evict(m.Id);
        return Task.CompletedTask;
    }

    public static Task OnMessageUpdated(DiscordClient _, MessageUpdatedEventArgs args)
    {
        if (args.Channel.IsPrivate)
            return Task.CompletedTask;
            
        if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
            return Task.CompletedTask;
            
        lock(queue.SyncObj)
            queue.AddOrReplace(args.Message);
        return Task.CompletedTask;
    }

    internal static async Task<List<DiscordMessage>> GetMessagesCachedAsync(this DiscordChannel ch, int count = 100)
    {
        if (!MessageQueue.TryGetValue(ch.Id, out var queue))
            lock (MessageQueue)
                if (!MessageQueue.TryGetValue(ch.Id, out queue))
                    MessageQueue[ch.Id] = queue = new(KeyGen);
        List<DiscordMessage> result;
        lock(queue.SyncObj)
            result = queue.Reverse().Take(count).ToList();
        var cacheCount = result.Count;
        var fetchCount = Math.Max(count - cacheCount, 0);
        if (fetchCount > 0)
        {
            List<DiscordMessage> fetchedList;
            if (result.Count > 0)
                fetchedList = ch.GetMessagesBeforeAsync(result[0].Id, fetchCount).ToList();
            else
                fetchedList = ch.GetMessagesAsync(fetchCount).ToList();
            result.AddRange(fetchedList);
            if (queue.Count < Config.ChannelMessageHistorySize)
                lock (queue.SyncObj)
                {
                    // items in queue might've changed since the previous check at the beginning of this method
                    var freshCopy = queue.Reverse().ToList();
                    queue.Clear();
                    queue.AddRange(freshCopy.Concat(fetchedList).Distinct().Reverse());
                }
        }
        return result;
    }

    internal static async Task<List<DiscordMessage>> GetMessagesBeforeCachedAsync(this DiscordChannel ch, ulong msgId, int count = 100)
    {
        if (!MessageQueue.TryGetValue(ch.Id, out var queue))
            lock (MessageQueue)
                if (!MessageQueue.TryGetValue(ch.Id, out queue))
                    MessageQueue[ch.Id] = queue = new(KeyGen);
        List<DiscordMessage> result;
        lock(queue.SyncObj)
            result = queue.Reverse().SkipWhile(m => m.Id >= msgId).Take(count).ToList();
        var cacheCount = result.Count;
        var fetchCount = Math.Max(count - cacheCount, 0);
        if (fetchCount > 0)
        {
            IReadOnlyList<DiscordMessage> fetchedList;
            if (result.Any())
            {
                fetchedList = ch.GetMessagesBeforeAsync(result[0].Id, fetchCount).ToList();
                if (queue.Count < Config.ChannelMessageHistorySize)
                    lock (queue.SyncObj)
                    {
                        // items in queue might've changed since the previous check at the beginning of this method
                        var freshCopy = queue.Reverse().ToList();
                        queue.Clear();
                        queue.AddRange(freshCopy.Concat(fetchedList).Distinct().Reverse());
                    }
            }
            else
                fetchedList = ch.GetMessagesBeforeAsync(msgId, fetchCount).ToList();
            result.AddRange(fetchedList);
        }
        return result;

    }
}