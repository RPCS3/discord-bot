using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace CompatBot.Utils
{
    internal class PoorMansTaskScheduler<T>
    {
        private readonly int queueLimit;
        private readonly ConcurrentDictionary<Task, T> taskQueue = new ConcurrentDictionary<Task, T>();
        public PoorMansTaskScheduler() : this(Math.Max(1, Environment.ProcessorCount / 2)) { }

        public PoorMansTaskScheduler(int simultaneousTaskCountLimit)
        {
            if (simultaneousTaskCountLimit < 1)
                throw new ArgumentException();

            queueLimit = simultaneousTaskCountLimit;
        }

        public async Task AddAsync(T tag, Task task)
        {
            if (task == null)
                return;

            if (taskQueue.Count < queueLimit)
            {
                taskQueue.TryAdd(task, tag);
                return;
            }

            var completedTasks = taskQueue.Keys.Where(t => t.IsCompleted).ToList();
            if (completedTasks.Count > 0)
                foreach (var t in completedTasks)
                    taskQueue.TryRemove(t, out _);

            if (taskQueue.Count < queueLimit)
            {
                taskQueue.TryAdd(task, tag);
                return;
            }

            await Task.WhenAny(taskQueue.Keys).ConfigureAwait(false);
            taskQueue.TryAdd(task, tag);
        }

        public async Task WaitForClearTagAsync(T tag)
        {
            var tasksToWait = taskQueue.Where(kvp => tag.Equals(kvp.Value)).Select(kvp => kvp.Key).ToList();
            await Task.WhenAll(tasksToWait).ConfigureAwait(false);
            foreach (var t in tasksToWait)
                taskQueue.TryRemove(t, out _);
        }
    }
}
