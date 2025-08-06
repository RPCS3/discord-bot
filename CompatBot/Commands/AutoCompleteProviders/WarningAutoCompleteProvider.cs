using System.Linq.Expressions;
using CompatApiClient.Utils;
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

        Expression<Func<Warning, bool>> filter = context.Command.Name is "revert"
            ? w => w.Retracted
            : w => !w.Retracted;
        if (context.Options.FirstOrDefault(o => o is { Name: "user", Value: ulong })?.Value is ulong userId)
            filter = context.Command.Name is "revert"
                ? w => w.Retracted && w.DiscordId == userId
                : w => !w.Retracted && w.DiscordId == userId;
        
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        db.WithNoCase();
        List<Warning> result;
        if (context.UserInput is not { Length: > 0 } prefix)
            result = await db.Warning
                .OrderByDescending(w => w.Id)
                .Where(filter)
                .Take(25)
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);
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
            result = await prefixMatches
                .Concat(substringMatches)
                .Distinct()
                .OrderByDescending(i => i.Id)
                .Take(25)
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);
        }
        var userIds = result
            .Select(w => w.DiscordId)
            .Distinct()
            .ToList();
        var userNames = new Dictionary<ulong, string>(userIds.Count);
        foreach (var id in userIds)
            userNames[id] = await context.Client.GetUserNameAsync(context.Channel, id).ConfigureAwait(false);
        return result.Select(
            w => new DiscordAutoCompleteChoice(
                $"{w.Id}: {w.Timestamp?.AsUtc():yyyy-MM-dd HH:mmz}: {userNames[w.DiscordId]} - {w.Reason}".Trim(100),
                w.Id
            )
        ).ToList();
    }
}