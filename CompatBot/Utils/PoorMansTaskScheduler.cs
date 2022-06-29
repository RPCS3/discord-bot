using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace CompatBot.Utils;

internal class PoorMansTaskScheduler<T> where T: notnull
{
    private readonly int queueLimit;
    private readonly ConcurrentDictionary<Task, T> taskQueue = new();
    public PoorMansTaskScheduler() : this(Math.Max(1, Environment.ProcessorCount / 2)) { }

    public PoorMansTaskScheduler(int simultaneousTaskCountLimit)
    {
        if (simultaneousTaskCountLimit < 1)
            throw new ArgumentException("Task count can't be lower than 1", nameof(simultaneousTaskCountLimit));

        queueLimit = simultaneousTaskCountLimit;
    }

    public async Task AddAsync(T tag, Task task)
    {
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

        var result = await Task.WhenAny(taskQueue.Keys).ConfigureAwait(false);
        taskQueue.TryRemove(result, out _);
        taskQueue.TryAdd(task, tag);
    }

    public async Task WaitForClearTagAsync(T tag)
    {
        var tasksToWait = taskQueue.Where(kvp => tag.Equals(kvp.Value)).Select(kvp => kvp.Key).ToList();
        if (tasksToWait.Count == 0)
            return;

        await Task.WhenAll(tasksToWait).ConfigureAwait(false);
        foreach (var t in tasksToWait)
            taskQueue.TryRemove(t, out _);
    }
}