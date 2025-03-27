namespace CompatBot.Database.Providers;

internal static class DisabledCommandsProvider
{
    private static readonly HashSet<string> DisabledCommands = new(StringComparer.InvariantCultureIgnoreCase);
    private static SemaphoreSlim semaphore = new(1, 1);

    static DisabledCommandsProvider()
    {
        semaphore.Wait();
        try
        {
            using var db = BotDb.OpenRead();
            foreach (var cmd in db.DisabledCommands.ToList())
                DisabledCommands.Add(cmd.Command);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static HashSet<string> Get() => DisabledCommands;

    public static async ValueTask DisableAsync(string command)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (DisabledCommands.Add(command))
            {
                await using var db = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                db.DisabledCommands.Add(new() { Command = command });
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async ValueTask EnableAsync(string command)
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (DisabledCommands.Remove(command))
            {
                await using var db = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                var cmd = db.DisabledCommands.FirstOrDefault(c => c.Command == command);
                if (cmd == null)
                    return;

                db.DisabledCommands.Remove(cmd);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async ValueTask ClearAsync()
    {
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            DisabledCommands.Clear();
            await using var db = await BotDb.OpenWriteAsync().ConfigureAwait(false);
            db.DisabledCommands.RemoveRange(db.DisabledCommands);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}