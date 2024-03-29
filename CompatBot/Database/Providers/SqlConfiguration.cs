﻿using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

internal static class SqlConfiguration
{
    internal const string ConfigVarPrefix = "ENV-";

    public static async Task RestoreAsync()
    {
        await using var db = new BotDb();
        var setVars = await db.BotState.AsNoTracking().Where(v => v.Key.StartsWith(ConfigVarPrefix)).ToListAsync().ConfigureAwait(false);
        if (setVars.Any())
        {
            foreach (var stateVar in setVars)
                if (stateVar.Value is string value)
                    Config.InMemorySettings[stateVar.Key[ConfigVarPrefix.Length ..]] = value;
            if (!Config.InMemorySettings.TryGetValue(nameof(Config.GoogleApiCredentials), out var googleCreds) || string.IsNullOrEmpty(googleCreds))
            {
                if (Path.Exists(Config.GoogleApiConfigPath))
                {
                    Config.Log.Info("Migrating Google API credentials storage from file to db...");
                    try
                    {
                        googleCreds = await File.ReadAllTextAsync(Config.GoogleApiConfigPath).ConfigureAwait(false);
                        if (GoogleDriveHandler.ValidateCredentials(googleCreds))
                        {
                            Config.InMemorySettings[nameof(Config.GoogleApiCredentials)] = googleCreds;
                            Config.Log.Info("Successfully migrated Google API credentials");
                        }
                        else
                        {
                            Config.Log.Error("Failed to migrate Google API credentials");
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Error(e, "Failed to migrate Google API credentials");
                    }
                }
            }
            Config.RebuildConfiguration();
        }
    }
}