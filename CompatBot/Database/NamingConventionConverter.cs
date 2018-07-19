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
                entity.Relational().TableName = nameResolver(entity.Relational().TableName);
                foreach (var property in entity.GetProperties())
                    property.Relational().ColumnName = nameResolver(property.Name);
                foreach (var key in entity.GetKeys())
                    key.Relational().Name = nameResolver(key.Relational().Name);
                foreach (var key in entity.GetForeignKeys())
                    key.Relational().Name = nameResolver(key.Relational().Name);
                foreach (var index in entity.GetIndexes())
                    index.Relational().Name = nameResolver(index.Relational().Name);
            }
        }
    }
}
