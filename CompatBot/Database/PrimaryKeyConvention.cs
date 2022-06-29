using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database;

internal static class PrimaryKeyConvention
{
    public static void ConfigureDefaultPkConvention(this ModelBuilder modelBuilder, string keyProperty = "Id")
    {
        if (string.IsNullOrEmpty(keyProperty))
            throw new ArgumentException("Key property name is mandatory", nameof(keyProperty));

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var pk = entity.GetKeys().FirstOrDefault(k => k.IsPrimaryKey());
            pk?.SetName(keyProperty);
        }
    }

    public static void ConfigureNoPkConvention(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var pk = entity.GetKeys().FirstOrDefault(k => k.IsPrimaryKey());
            if (pk != null)
                entity.RemoveKey(pk.Properties);
        }
    }
}