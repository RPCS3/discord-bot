using System;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database
{
    internal static class NamingConventionConverter
    {
        public static void ConfigureMapping(this ModelBuilder modelBuilder, Func<string, string> nameResolver)
        {
            if (nameResolver == null)
                throw new ArgumentNullException(nameof(nameResolver));

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                entity.SetTableName(nameResolver(entity.GetTableName()));
                foreach (var property in entity.GetProperties())
                    property.SetColumnName(nameResolver(property.Name));
                foreach (var key in entity.GetKeys())
                    key.SetName(nameResolver(key.GetName()));
                foreach (var key in entity.GetForeignKeys())
                    key.SetConstraintName(nameResolver(key.GetConstraintName()));
                foreach (var index in entity.GetIndexes())
                    index.SetName(nameResolver(index.GetName()));
            }
        }
    }
}
