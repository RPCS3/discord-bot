using System;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database;

internal static class NamingConventionConverter
{
    public static void ConfigureMapping(this ModelBuilder modelBuilder, Func<string, string> nameResolver)
    {
        if (nameResolver == null)
            throw new ArgumentNullException(nameof(nameResolver));

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is string tableName)
                entity.SetTableName(nameResolver(tableName));
            foreach (var property in entity.GetProperties())
                property.SetColumnName(nameResolver(property.Name));
            foreach (var key in entity.GetKeys())
                if (key.GetName() is string name)
                    key.SetName(nameResolver(name));
            foreach (var key in entity.GetForeignKeys())
                if (key.GetConstraintName() is string constraint)
                    key.SetConstraintName(nameResolver(constraint));
            foreach (var index in entity.GetIndexes())
                if (index.GetDatabaseName() is string dbName)
                    index.SetDatabaseName(nameResolver(dbName));
        }
    }
}