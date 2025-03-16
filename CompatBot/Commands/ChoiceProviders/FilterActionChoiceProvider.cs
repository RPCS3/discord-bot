using CompatBot.Database;

namespace CompatBot.Commands.ChoiceProviders;

public class FilterActionChoiceProvider : IChoiceProvider
{
    private static readonly IReadOnlyList<DiscordApplicationCommandOptionChoice> actionType =
    [
        new("Default", 0),
        new("Remove content", (int)FilterAction.RemoveContent),
        new("Warn", (int)FilterAction.IssueWarning),
        new("Show explanation", (int)FilterAction.ShowExplain),
        new("Send message", (int)FilterAction.SendMessage),
        new("No mod log", (int)FilterAction.MuteModQueue),
        new("Kick user", (int)FilterAction.Kick),
    ];

    public ValueTask<IEnumerable<DiscordApplicationCommandOptionChoice>> ProvideAsync(CommandParameter parameter)
        => ValueTask.FromResult<IEnumerable<DiscordApplicationCommandOptionChoice>>(actionType);
}