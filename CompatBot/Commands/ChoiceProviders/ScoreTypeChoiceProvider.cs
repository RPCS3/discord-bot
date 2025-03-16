namespace CompatBot.Commands.ChoiceProviders;

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