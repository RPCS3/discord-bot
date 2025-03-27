using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class ContentFilterAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        if (!ModProvider.IsMod(context.User.Id))
            return [new($"{Config.Reactions.Denied} You are not authorized to use this command.", -1)];
        
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        IEnumerable<(int id, string trigger)> result;
        if (context.UserInput is not {Length: >0} prefix)
            result = db.Piracystring
                .OrderByDescending(e=>e.Id)
                .Take(25)
                .AsNoTracking()
                .AsEnumerable()
                .Select(i => (id: i.Id, trigger:i.String));
        else
        {
            prefix = prefix.ToLowerInvariant();
            var prefixMatches = db.Piracystring
                .Where(i => i.Id.ToString().StartsWith(prefix) || i.String.StartsWith(prefix))
                .Take(25);
            var substringMatches= db.Piracystring
                .Where(i => i.Id.ToString().Contains(prefix) || i.String.Contains(prefix))
                .Take(50);
            result = prefixMatches
                .Concat(substringMatches)
                .Distinct()
                .OrderBy(i => i.Id)
                .Take(25)
                .AsNoTracking()
                .AsEnumerable()
                .Select(i => (id: i.Id, trigger: i.String));
        }
        return result.Select(i => new DiscordAutoCompleteChoice($"{i.id}: {i.trigger}".Trim(100), i.id)).ToList();
    }
}