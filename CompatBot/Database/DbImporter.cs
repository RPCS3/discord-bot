using System;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Database.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace CompatBot.Database
{
    internal static class DbImporter
    {
        public static async Task<bool> UpgradeAsync(DbContext dbContext, CancellationToken cancellationToken)
        {
            try
            {
                Config.Log.Info($"Upgrading {dbContext.GetType().Name} database if needed...");
                await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException e)
            {
                Config.Log.Warn(e, "Database upgrade failed, probably importing an unversioned one.");
                if (!(dbContext is BotDb botDb))
                    return false;

                Config.Log.Info("Trying to apply a manual fixup...");
                try
                {
                    await ImportAsync(botDb, cancellationToken).ConfigureAwait(false);
                    Config.Log.Info("Manual fixup worked great. Let's try migrations again...");
                    await botDb.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Config.Log.Fatal(ex, "Well shit, I hope you had backups, son. You'll have to figure this one out on your own.");
                    return false;
                }
            }
            if (dbContext is BotDb botDb2 && !await botDb2.Moderator.AnyAsync(m => m.DiscordId == Config.BotAdminId, cancellationToken).ConfigureAwait(false))
            {
                await botDb2.Moderator.AddAsync(new Moderator {DiscordId = Config.BotAdminId, Sudoer = true}, cancellationToken).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            Config.Log.Info("Database is ready.");
            return true;
        }

        private static async Task ImportAsync(BotDb dbContext, CancellationToken cancellationToken)
        {
            var db = dbContext.Database;
            using (var tx = await db.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    // __EFMigrationsHistory table will be already created by the failed migration attempt
                    await db.ExecuteSqlCommandAsync($"INSERT INTO `__EFMigrationsHistory`(`MigrationId`,`ProductVersion`) VALUES ({new InitialCreate().GetId()},'manual')", cancellationToken);
                    await db.ExecuteSqlCommandAsync($"INSERT INTO `__EFMigrationsHistory`(`MigrationId`,`ProductVersion`) VALUES ({new Explanations().GetId()},'manual')", cancellationToken);
                    // create constraints on moderator
                    await db.ExecuteSqlCommandAsync(@"CREATE TABLE `temp_new_moderator` (
                                                     `id`         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `discord_id` INTEGER NOT NULL,
                                                     `sudoer`     INTEGER NOT NULL
                                                 )", cancellationToken);
                    await db.ExecuteSqlCommandAsync("INSERT INTO temp_new_moderator SELECT `id`,`discord_id`,`sudoer` FROM `moderator`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("DROP TABLE `moderator`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("ALTER TABLE `temp_new_moderator` RENAME TO `moderator`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("CREATE UNIQUE INDEX `moderator_discord_id` ON `moderator` (`discord_id`)", cancellationToken);
                    // create constraints on piracystring
                    await db.ExecuteSqlCommandAsync(@"CREATE TABLE `temp_new_piracystring` (
                                                     `id`     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `string` varchar ( 255 ) NOT NULL
                                                 )", cancellationToken);
                    await db.ExecuteSqlCommandAsync("INSERT INTO temp_new_piracystring SELECT `id`,`string` FROM `piracystring`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("DROP TABLE `piracystring`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("ALTER TABLE `temp_new_piracystring` RENAME TO `piracystring`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("CREATE UNIQUE INDEX `piracystring_string` ON `piracystring` (`string`)", cancellationToken);
                    // create constraints on warning
                    await db.ExecuteSqlCommandAsync(@"CREATE TABLE `temp_new_warning` (
                                                     `id`          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `discord_id`  INTEGER NOT NULL,
                                                     `reason`      TEXT NOT NULL,
                                                     `full_reason` TEXT NOT NULL,
                                                     `issuer_id`   INTEGER NOT NULL DEFAULT 0
                                                 )", cancellationToken);
                    await db.ExecuteSqlCommandAsync("INSERT INTO temp_new_warning SELECT `id`,`discord_id`,`reason`,`full_reason`,`issuer_id` FROM `warning`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("DROP TABLE `warning`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("ALTER TABLE `temp_new_warning` RENAME TO `warning`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("CREATE INDEX `warning_discord_id` ON `warning` (`discord_id`)", cancellationToken);
                    // create constraints on explanation
                    await db.ExecuteSqlCommandAsync(@"CREATE TABLE `temp_new_explanation` (
                                                     `id`      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                                     `keyword` TEXT NOT NULL,
                                                     `text`    TEXT NOT NULL
                                                 )", cancellationToken);
                    await db.ExecuteSqlCommandAsync("INSERT INTO temp_new_explanation SELECT `id`,`keyword`,`text` FROM `explanation`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("DROP TABLE `explanation`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("ALTER TABLE `temp_new_explanation` RENAME TO `explanation`", cancellationToken);
                    await db.ExecuteSqlCommandAsync("CREATE UNIQUE INDEX `explanation_keyword` ON `explanation` (`keyword`)", cancellationToken);
                    tx.Commit();
                }
                catch (Exception e)
                {
                    //tx.Rollback();
                    tx.Commit();
                    throw e;
                }
            }
        }
    }
}