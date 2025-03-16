using CompatBot.Database;

namespace CompatBot.Commands.ChoiceProviders;

public class FilterContextChoiceProvider : IChoiceProvider
{
    private static readonly IReadOnlyList<DiscordApplicationCommandOptionChoice> contextType =
    [
        new("Default", 0),
        new("Chat", (int)FilterContext.Chat),
        new("Logs", (int)FilterContext.Log),
        new("Both", (int)(FilterContext.Chat | FilterContext.Log)),
    ];

    public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        => ValueTask.FromResult<IEnumerable<DiscordApplicationCommandOptionChoice>>(contextType);
}