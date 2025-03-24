using System.Linq.Expressions;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class WarningAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        var authorized = await context.User.IsWhitelistedAsync(context.Client, context.Guild).ConfigureAwait(false);
        if (!authorized)
            return [new($"{Config.Reactions.Denied} You are not authorized to use this command.", -1)];

        //var user = context.Arguments.FirstOrDefault(kvp => kvp.Key.Name.Equals("user") && kvp.Value is DiscordUser).Value as DiscordUser;
        Expression<Func<Warning, bool>> filter = context.Command.Name is nameof(Warnings.Revert)
            ? w => w.Retracted
            : w => !w.Retracted;
        await using var db = new BotDb();
        IEnumerable<Warning> result;
        if (context.UserInput is not { Length: > 0 } prefix)
            result = db.Warning
                .OrderByDescending(w => w.Id)
                .Where(filter)
                .Take(25)
                .AsNoTracking()
                .AsEnumerable();
        else
        {
            prefix = prefix.ToLowerInvariant();
            var prefixMatches = db.Warning
                .Where(filter)
                .Where(w => w.Id.ToString().StartsWith(prefix) || w.Reason.StartsWith(prefix))
                .Take(25);
            var substringMatches= db.Warning
                .Where(filter)
                .Where(w => w.Id.ToString().Contains(prefix) || w.Reason.Contains(prefix))
                .Take(50);
            result = prefixMatches
                .Concat(substringMatches)
                .Distinct()
                .OrderByDescending(i => i.Id)
                .Take(25)
                .AsNoTracking()
                .AsEnumerable();
        }
        return result.Select(
            w => new DiscordAutoCompleteChoice($"{w.Id}: {w.Timestamp?.AsUtc():O}: {w.Reason}", w.Id)
        ).ToList();
    }
}