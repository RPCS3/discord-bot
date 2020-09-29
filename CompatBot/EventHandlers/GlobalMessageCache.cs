using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    using TCache = ConcurrentDictionary<ulong, FixedLengthBuffer<ulong, DiscordMessage>>;

    internal static class GlobalMessageCache
    {
        private static readonly TCache MessageQueue = new TCache();
        private static readonly Func<DiscordMessage, ulong> KeyGen = (DiscordMessage m) => m.Id;

        public static Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return Task.CompletedTask;

            if (!MessageQueue.TryGetValue(args.Channel.Id, out var queue))
                MessageQueue[args.Channel.Id] = queue = new FixedLengthBuffer<ulong, DiscordMessage>(KeyGen);
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
    }
}