using System;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class GlobalButtonHandler
    {
        public static async Task OnComponentInteraction(DiscordClient sender, ComponentInteractionCreateEventArgs e)
        {
            if (e.Interaction.Type != InteractionType.Component
                || e.Interaction.Data.ComponentType != ComponentType.Button
                || e.Interaction.Data.CustomId is not {Length: >0})
                return;

            const string replaceWithUpdatesPrefix = "replace with game updates:";
            var btnId = e.Interaction.Data.CustomId;
            if (btnId.StartsWith(replaceWithUpdatesPrefix))
            {
                var parts = btnId.Split(':');
                if (parts.Length != 4)
                {
                    Config.Log.Warn("Invalid interaction id: " + btnId);
                    return;
                }

                try
                {
                    var authorId = ulong.Parse(parts[1]);
                    var refMsgId = ulong.Parse(parts[2]);
                    var productCode = parts[3];
                    if (e.User.Id != authorId)
                        return;
                    
                    e.Handled = true;
                    await e.Interaction.CreateResponseAsync(InteractionResponseType.DefferedMessageUpdate).ConfigureAwait(false);
                    await e.Message.DeleteAsync().ConfigureAwait(false);
                    var refMsg = await e.Channel.GetMessageAsync(refMsgId).ConfigureAwait(false);
                    var cne = sender.GetCommandsNext();
                    var cmd = cne.FindCommand("psn check updates", out _);
                    var context = cne.CreateContext(refMsg, Config.CommandPrefix, cmd, productCode);
                    await cne.ExecuteCommandAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Config.Log.Warn(ex);
                }
            }
        }
    }
}