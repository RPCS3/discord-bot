using System.IO;
using CompatBot.Database.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace CompatBot.Database;

public static class DbImporter
{
    public static async Task<bool> UpgradeAsync(CancellationToken cancellationToken)
    {
        await using (var db = BotDb.OpenWrite())
            if (!await UpgradeAsync(db, cancellationToken).ConfigureAwait(false))
                return false;

        await using (var db = ThumbnailDb.OpenWrite())
        {
            if (!await UpgradeAsync(db,cancellationToken).ConfigureAwait(false))
                return false;

            if (!await ImportNamesPool(db, cancellationToken).ConfigureAwait(false))
                return false;
        }
            
        await using (var db = HardwareDb.OpenWrite())
            if (!await UpgradeAsync(db, cancellationToken).ConfigureAwait(false))
                return false;

        return true;
    }

    private static async Task<bool> UpgradeAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            Config.Log.Info($"Upgrading {dbContext.GetType().Name} database if needed…");
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException e)
        {
            Config.Log.Warn(e, "Database upgrade failed, probably importing an unversioned one.");
            if (dbContext is not BotDb botDb)
                return false;

            Config.Log.Info("Trying to apply a manual fixup…");
            try
            {
                await ImportAsync(botDb, cancellationToken).ConfigureAwait(false);
                Config.Log.Info("Manual fixup worked great. Let's try migrations again…");
                await botDb.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Config.Log.Fatal(ex, "Well shit, I hope you had backups, son. You'll have to figure this one out on your own.");
                return false;
            }
        }
        Config.Log.Info("Database is ready.");
        return true;
    }

    private static async Task ImportAsync(BotDb dbContext, CancellationToken cancellationToken)
    {
        var db = dbContext.Database;
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            // __EFMigrationsHistory table will be already created by the failed migration attempt
#pragma warning disable EF1001 // Internal EF Core API usage.
#pragma warning disable EF1002 // Using raw sql
            await db.ExecuteSqlRawAsync($"INSERT INTO `__EFMigrationsHistory`(`MigrationId`,`ProductVersion`) VALUES ({new InitialCreate().GetId()},'manual')", cancellationToken);
            await db.ExecuteSqlRawAsync($"INSERT INTO `__EFMigrationsHistory`(`MigrationId`,`ProductVersion`) VALUES ({new Explanations().GetId()},'manual')", cancellationToken);
#pragma warning restore EF1002
#pragma warning restore EF1001 // Internal EF Core API usage.
            // create constraints on moderator
            await db.ExecuteSqlRawAsync(@"CREATE TABLE `temp_new_moderator` (
                                                     `id`         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `discord_id` INTEGER NOT NULL,
                                                     `sudoer`     INTEGER NOT NULL
                                                 )", cancellationToken);
            await db.ExecuteSqlRawAsync("INSERT INTO `temp_new_moderator` SELECT `id`,`discord_id`,`sudoer` FROM `moderator`", cancellationToken);
            await db.ExecuteSqlRawAsync("DROP TABLE `moderator`", cancellationToken);
            await db.ExecuteSqlRawAsync("ALTER TABLE `temp_new_moderator` RENAME TO `moderator`", cancellationToken);
            await db.ExecuteSqlRawAsync("CREATE UNIQUE INDEX `moderator_discord_id` ON `moderator` (`discord_id`)", cancellationToken);
            // create constraints on piracystring
            await db.ExecuteSqlRawAsync(@"CREATE TABLE `temp_new_piracystring` (
                                                     `id`     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `string` varchar ( 255 ) NOT NULL
                                                 )", cancellationToken);
            await db.ExecuteSqlRawAsync("INSERT INTO `temp_new_piracystring` SELECT `id`,`string` FROM `piracystring`", cancellationToken);
            await db.ExecuteSqlRawAsync("DROP TABLE `piracystring`", cancellationToken);
            await db.ExecuteSqlRawAsync("ALTER TABLE `temp_new_piracystring` RENAME TO `piracystring`", cancellationToken);
            await db.ExecuteSqlRawAsync("CREATE UNIQUE INDEX `piracystring_string` ON `piracystring` (`string`)", cancellationToken);
            // create constraints on warning
            await db.ExecuteSqlRawAsync(@"CREATE TABLE `temp_new_warning` (
                                                     `id`          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `discord_id`  INTEGER NOT NULL,
                                                     `reason`      TEXT NOT NULL,
                                                     `full_reason` TEXT NOT NULL,
                                                     `issuer_id`   INTEGER NOT NULL DEFAULT 0
                                                 )", cancellationToken);
            await db.ExecuteSqlRawAsync("INSERT INTO `temp_new_warning` SELECT `id`,`discord_id`,`reason`,`full_reason`,`issuer_id` FROM `warning`", cancellationToken);
            await db.ExecuteSqlRawAsync("DROP TABLE `warning`", cancellationToken);
            await db.ExecuteSqlRawAsync("ALTER TABLE `temp_new_warning` RENAME TO `warning`", cancellationToken);
            await db.ExecuteSqlRawAsync("CREATE INDEX `warning_discord_id` ON `warning` (`discord_id`)", cancellationToken);
            // create constraints on explanation
            await db.ExecuteSqlRawAsync(@"CREATE TABLE `temp_new_explanation` (
                                                     `id`      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `keyword` TEXT NOT NULL,
                                                     `text`    TEXT NOT NULL
                                                 )", cancellationToken);
            await db.ExecuteSqlRawAsync("INSERT INTO `temp_new_explanation` SELECT `id`,`keyword`,`text` FROM `explanation`", cancellationToken);
            await db.ExecuteSqlRawAsync("DROP TABLE `explanation`", cancellationToken);
            await db.ExecuteSqlRawAsync("ALTER TABLE `temp_new_explanation` RENAME TO `explanation`", cancellationToken);
            await db.ExecuteSqlRawAsync("CREATE UNIQUE INDEX `explanation_keyword` ON `explanation` (`keyword`)", cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.CommitAsync(cancellationToken);
            throw;
        }
    }

    internal static string GetDbPath(string dbName, Environment.SpecialFolder desiredFolder)
    {
        if (SandboxDetector.Detect() == SandboxType.Docker)
            return Path.Combine("/bot-db/", dbName);
                
        var settingsFolder = Path.Combine(Environment.GetFolderPath(desiredFolder), "compat-bot");
        try
        {
            if (!Directory.Exists(settingsFolder))
                Directory.CreateDirectory(settingsFolder);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to create settings folder " + settingsFolder);
            settingsFolder = "";
        }

        var dbPath = Path.Combine(settingsFolder, dbName);
        if (settingsFolder != "")
            try
            {
                if (File.Exists(dbName))
                {
                    Config.Log.Info($"Found local {dbName}, moving…");
                    if (File.Exists(dbPath))
                    {
                        Config.Log.Error($"{dbPath} already exists, please reslove the conflict manually");
                        throw new InvalidOperationException($"Failed to move local {dbName} to {dbPath}");
                    }
                        
                    var dbFiles = Directory.GetFiles(".", Path.GetFileNameWithoutExtension(dbName) + ".*");
                    foreach (var file in dbFiles)
                        File.Move(file, Path.Combine(settingsFolder, Path.GetFileName(file)));
                    Config.Log.Info($"Using {dbPath}");
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, $"Failed to move local {dbName} to {dbPath}");
                throw;
            }
        return dbPath;
    }

    private static async Task<bool> ImportNamesPool(ThumbnailDb db, CancellationToken cancellationToken)
    {
        Config.Log.Debug("Importing name pool…");
        var rootDir = Environment.CurrentDirectory;
        while (rootDir is not null && !Directory.EnumerateFiles(rootDir, "names_*.txt", SearchOption.TopDirectoryOnly).Any())
            rootDir = Path.GetDirectoryName(rootDir);
        if (rootDir is null)
        {
            Config.Log.Error("Couldn't find any name sources");
            return db.NamePool.Any();
        }

        var resources = Directory.GetFiles(rootDir, "names_*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();
        if (resources.Count == 0)
        {
            Config.Log.Error("Couldn't find any name sources (???)");
            return db.NamePool.Any();
        }

        var timestamp = -1L;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            byte[] buf;
            foreach (var path in resources)
            {
                var fileInfo = new FileInfo(path);
                buf = BitConverter.GetBytes(fileInfo.Length);
                sha256.TransformBlock(buf, 0, buf.Length, null, 0);
            }
            buf = Encoding.UTF8.GetBytes(Config.RenameNameSuffix);
            sha256.TransformFinalBlock(buf, 0, buf.Length);
            timestamp = BitConverter.ToInt64(sha256.Hash!, 0);
        }

        const string renameStateKey = "rename-name-pool";
        var stateEntry = db.State.FirstOrDefault(n => n.Locale == renameStateKey);
        if (stateEntry?.Timestamp == timestamp)
        {
            Config.Log.Info("Name pool is up-to-date");
            return true;
        }

        Config.Log.Info("Updating name pool…");
        try
        {
            var names = new HashSet<string>();
            foreach (var resourcePath in resources)
            {
                await using var stream = File.Open(resourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
                {
                    if (line.Length < 2 || line.StartsWith("#"))
                        continue;

                    var commentPos = line.IndexOf(" (");
                    if (commentPos > 1)
                        line = line.Substring(0, commentPos);
                    line = line.Trim()
                        .Replace("  ", " ")
                        .Replace('`', '\'') // consider ’
                        .Replace("\"", "\\\"");
                    if (line.Length + Config.RenameNameSuffix.Length > 32)
                        continue;

                    if (line.Contains('@')
                        || line.Contains('#')
                        || line.Contains(':'))
                        continue;

                    names.Add(line);
                }
            }
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            db.NamePool.RemoveRange(db.NamePool);
            foreach (var name in names)
                await db.NamePool.AddAsync(new() {Name = name}, cancellationToken).ConfigureAwait(false);
            if (stateEntry is null)
                await db.State.AddAsync(new() {Locale = renameStateKey, Timestamp = timestamp}, cancellationToken).ConfigureAwait(false);
            else
                stateEntry.Timestamp = timestamp;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return names.Count > 0;
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            return false;
        }
    }
}