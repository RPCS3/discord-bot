using CompatBot.Database;

namespace CompatBot.Commands.ChoiceProviders;

public class FilterContextChoiceProvider : IChoiceProvider
{
    private static readonly IReadOnlyList<DiscordApplicationCommandOptionChoice> contextType =
    [
        new("Default", 0),
        new("Chat", (int)FilterContext.Chat),
        new("Logs", (int)FilterContext.Log),
        new("Invites", (int)FilterContext.Invite),
        new("Content", (int)(FilterContext.Chat | FilterContext.Log)),
        new("Everything", (int)(FilterContext.Chat | FilterContext.Log | FilterContext.Invite)),
    ];

    public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        => ValueTask.FromResult<IEnumerable<DiscordApplicationCommandOptionChoice>>(contextType);
}