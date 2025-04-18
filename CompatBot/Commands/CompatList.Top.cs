using CompatApiClient.Utils;
using CompatBot.Commands.ChoiceProviders;
using CompatBot.Database;
using CompatBot.Utils.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static partial class CompatList
{
    public static class Top
    {
        [Command("top")]
        [Description("Top game lists based on Metacritic scores and compatibility status")]
        public static async ValueTask Show(SlashCommandContext ctx,
            [Description("Number of entries in the list")] int number = 10,
            [Description("Filter by compatibility status"), SlashChoiceProvider<CompatListStatusChoiceProvider>] string status = "playable",
            [Description("Listing type"), SlashChoiceProvider<ScoreTypeChoiceProvider>] string type = "both")
        {
            var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
            await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
            
            type = type.ToLowerInvariant();
            number = number.Clamp(1, 100);
            var (exactStatus, s) = Utils.Extensions.Converters.ParseStatus(status);
            await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
            var queryBase = db.Thumbnail
                .AsNoTracking()
                .WithStatus(s, exactStatus)
                .Where(g => g.Metacritic != null)
                .Include(t => t.Metacritic);
            var query = type switch
            {
                "critic" => queryBase.Where(t => t.Metacritic!.CriticScore > 0).AsEnumerable().Select(t =>
                    (title: t.Metacritic!.Title, score: t.Metacritic!.CriticScore!.Value,
                        second: t.Metacritic.UserScore ?? t.Metacritic.CriticScore.Value)),
                "user" => queryBase.Where(t => t.Metacritic!.UserScore > 0).AsEnumerable().Select(t =>
                    (title: t.Metacritic!.Title, score: t.Metacritic!.UserScore!.Value,
                        second: t.Metacritic.CriticScore ?? t.Metacritic.UserScore.Value)),
                _ => queryBase.AsEnumerable().Select(t => (title: t.Metacritic!.Title,
                    score: Math.Max(t.Metacritic.CriticScore ?? 0, t.Metacritic.UserScore ?? 0), second: (byte)0)),
            };
            var resultList = query.Where(i => i.score > 0)
                .OrderByDescending(i => i.score)
                .ThenByDescending(i => i.second)
                .Distinct()
                .Take(number)
                .ToList();
            if (resultList.Count > 0)
            {
                var result = new StringBuilder($"Best {s.ToString().ToLower()}");
                if (exactStatus)
                    result.Append(" only");
                result.Append(" games");
                if (type is "critic" or "user")
                    result.Append($" according to {type}s");
                result.AppendLine(":");
                foreach (var (title, score, _) in resultList)
                    result.AppendLine($"`{score:00}` {title}");
                var formattedResults = AutosplitResponseHelper.AutosplitMessage(result.ToString(), blockStart: null, blockEnd: null);
                await ctx.RespondAsync(formattedResults[0], ephemeral).ConfigureAwait(false);
            }
            else
                await ctx.RespondAsync("Failed to generate list", ephemeral).ConfigureAwait(false);
        }
    }
}