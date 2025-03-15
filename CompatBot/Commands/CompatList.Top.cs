using CompatApiClient.Utils;
using CompatBot.Database;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal sealed partial class CompatList
{
    public sealed class Top
    {
        [Command("top")]
        [Description("Top game lists based on Metacritic scores and compatibility status")]
        public async ValueTask Show(SlashCommandContext ctx,
            [Description("Number of entries in the list")] int number = 10,
            [Description("Filter by compatibility status"), SlashChoiceProvider<CompatListStatusChoiceProvider>] string status = "playable",
            [Description("Listing type"), SlashChoiceProvider<ScoreTypeChoiceProvider>] string type = "both")
        {
            var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
            await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
            
            status = status.ToLowerInvariant();
            type = type.ToLowerInvariant();
            number = number.Clamp(1, 100);
            var exactStatus = status.EndsWith("only");
            if (exactStatus)
                status = status[..^4];
            if (!Enum.TryParse(status, true, out CompatStatus s))
                s = CompatStatus.Playable;

            await using var db = new ThumbnailDb();
            var queryBase = db.Thumbnail.AsNoTracking();
            if (exactStatus)
                queryBase = queryBase.Where(g => g.CompatibilityStatus == s);
            else
                queryBase = queryBase.Where(g => g.CompatibilityStatus >= s);
            queryBase = queryBase.Where(g => g.Metacritic != null).Include(t => t.Metacritic);
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
        
        public class CompatListStatusChoiceProvider : IChoiceProvider
        {
            private static readonly IReadOnlyList<DiscordApplicationCommandOptionChoice> compatListStatus =
            [
                new("playable", "playable"),
                new("ingame or better", "ingame"),
                new("intro or better", "intro"),
                new("loadable or better", "loadable"),
                new("only ingame", "ingameOnly"),
                new("only intro", "introOnly"),
                new("only loadable", "loadableOnly"),
            ];

            public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
                => ValueTask.FromResult<IEnumerable<DiscordApplicationCommandOptionChoice>>(compatListStatus);
        }
        
        public class ScoreTypeChoiceProvider : IChoiceProvider
        {
            private static readonly IReadOnlyList<DiscordApplicationCommandOptionChoice> scoreType =
            [
                new("combined", "both"),
                new("critic score", "critic"),
                new("user score", "user"),
            ];

            public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
                => ValueTask.FromResult<IEnumerable<DiscordApplicationCommandOptionChoice>>(scoreType);
        }
    }
}