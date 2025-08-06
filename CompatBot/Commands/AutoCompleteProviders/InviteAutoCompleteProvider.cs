using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class InviteAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        if (!ModProvider.IsMod(context.User.Id))
            return [new($"{Config.Reactions.Denied} You are not authorized to use this command.", -1)];
        
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        db.WithNoCase();
        IQueryable<WhitelistedInvite> result;
        if (context.UserInput is not { Length: > 0 } input)
            result = db.WhitelistedInvites
                .OrderByDescending(e => e.Id);
        else
        {
            input = input.ToLowerInvariant();
            var prefixMatches = db.WhitelistedInvites
                .OrderBy(i => i.Id)
                .Where(
                    i => i.Id.ToString().StartsWith(input)
                         || i.GuildId.ToString().StartsWith(input)
                         || (i.Name != null && i.Name.StartsWith(input))
                ).Take(25);
            var substringMatches= db.WhitelistedInvites
                .OrderBy(i => i.Id)
                .Where(
                    i => i.Id.ToString().Contains(input)
                         || i.GuildId.ToString().Contains(input)
                         || (i.Name != null && i.Name.Contains(input))
                ).Take(50);
            result = prefixMatches
                .Concat(substringMatches)
                .Distinct();
        }
        return result
            .Take(25)
            .AsNoTracking()
            .AsEnumerable()
            .Select(i => new DiscordAutoCompleteChoice($"{i.Id}: {i.Name} ({i.GuildId})".Trim(100), i.Id))
            .ToList();
    }
}