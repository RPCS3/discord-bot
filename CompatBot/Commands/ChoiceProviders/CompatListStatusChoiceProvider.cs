namespace CompatBot.Commands.ChoiceProviders;

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
