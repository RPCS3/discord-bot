using CompatBot.Commands;

namespace CompatBot.EventHandlers;

internal static class GlobalButtonHandler
{
    private const string ReplaceWithUpdatesPrefix = "replace with game updates:";

    public static async Task OnComponentInteraction(DiscordClient sender, ComponentInteractionCreateEventArgs e)
    {
        if (e.Interaction is not { Type: InteractionType.Component }
            or not { Data.ComponentType: ComponentType.Button }
            or not { Data.CustomId.Length: > 0 })
            return;

        var btnId = e.Interaction.Data.CustomId;
        if (btnId.StartsWith(ReplaceWithUpdatesPrefix))
            await Psn.Check.OnCheckUpdatesButtonClick(sender, e);
    }
}