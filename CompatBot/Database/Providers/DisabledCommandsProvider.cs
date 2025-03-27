namespace CompatBot.Database.Providers;

internal static class DisabledCommandsProvider
{
    private static readonly HashSet<string> DisabledCommands = new(StringComparer.InvariantCultureIgnoreCase);

    static DisabledCommandsProvider()
    {
        lock (DisabledCommands)
        {
            using var db = BotDb.OpenRead();
            foreach (var cmd in db.DisabledCommands.ToList())
                DisabledCommands.Add(cmd.Command);
        }
    }

    public static HashSet<string> Get() => DisabledCommands;

    public static void Disable(string command)
    {
        lock (DisabledCommands)
            if (DisabledCommands.Add(command))
            {
                using var db = BotDb.OpenWrite();
                db.DisabledCommands.Add(new() {Command = command});
                db.SaveChanges();
            }
    }

    public static void Enable(string command)
    {
        lock (DisabledCommands)
            if (DisabledCommands.Remove(command))
            {
                using var db = BotDb.OpenWrite();
                var cmd = db.DisabledCommands.FirstOrDefault(c => c.Command == command);
                if (cmd == null)
                    return;

                db.DisabledCommands.Remove(cmd);
                db.SaveChanges();
            }
    }

    public static void Clear()
    {
        lock (DisabledCommands)
        {
            DisabledCommands.Clear();
            using var db = BotDb.OpenWrite();
            db.DisabledCommands.RemoveRange(db.DisabledCommands);
            db.SaveChanges();
        }
    }
}