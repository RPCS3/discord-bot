using CompatApiClient.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.UserCommands;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using ResultNet;

namespace CompatBot.Commands;

internal static class WarningsContextMenus
{
    [Command("❗ Warn"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static ValueTask WarnUser(UserCommandContext ctx, DiscordUser user)
        => Warn(ctx, null, user);

    [Command("❗ Warn user"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static ValueTask WarnMessageAuthor(MessageCommandContext ctx, DiscordMessage message)
        => Warn(ctx, message, null);

    [Command("🔍 Show warnings"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu), AllowDMUsage]
    public static ValueTask ShowWarnings(UserCommandContext ctx, DiscordUser user)
        => Warnings.ListGroup.List(ctx, user);
    
    private static async ValueTask Warn(SlashCommandContext ctx, DiscordMessage? message = null, DiscordUser? user = null)
    {
        var interactivity = ctx.Extension.ServiceProvider.GetService<InteractivityExtension>();
        if (interactivity is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
            return;
        }
        
        user ??= message?.Author;
        if (user is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} User was not provided in any argument").ConfigureAwait(false);
            return;
        }

        var interaction = ctx.Interaction;
        var modal = new DiscordModalBuilder()
            .WithCustomId($"modal:warn:{Guid.NewGuid():n}")
            .WithTitle("Issue new warning")
            .AddTextInput(new("warning", "Rule #2", min_length: 2), "Warning reason");

        if (Config.WarnRoleId > 0
            && ctx.Guild is DiscordGuild guild
            && await guild.GetRoleAsync(Config.WarnRoleId).ConfigureAwait(false) is DiscordRole role)
        {
            if (await ctx.Client.GetMemberAsync(guild, user).ConfigureAwait(false) is DiscordMember member)
                modal.AddCheckbox(new("add_role"), $"Add {role.Name} role for member {member.DisplayName}");
            else
                modal.AddCheckbox(new("add_role"), $"Add {role.Name} role for user {user.DisplayName}");
        }
        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        try
        {
            InteractivityResult<ModalSubmittedEventArgs> modalResult;
            IModalSubmission? value;
            do
            {
                modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
                if (modalResult.TimedOut)
                    return;
            } while (!modalResult.Result.Values.TryGetValue("warning", out value)
                     || value is not TextInputModalSubmission{Value.Length: >0 });

            interaction = modalResult.Result.Interaction;
            await interaction.CreateResponseAsync(
                DiscordInteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral()
            ).ConfigureAwait(false);
            var reason = ((TextInputModalSubmission)value).Value;
            var addRole = modalResult.Result.Values.TryGetValue("add_role", out var item)
                && item is CheckboxModalSubmission cbValue
                && cbValue.Value is true;
            var result = await Warnings.AddAsync(user.Id, ctx.User, reason, message?.Content.Sanitize(), addRole).ConfigureAwait(false);
            if (result.IsFailure())
            {
                var response = new DiscordInteractionResponseBuilder()
                    .WithContent($"{Config.Reactions.Failure} {result.Message ?? "Couldn't save the warning, please try again"}")
                    .AsEphemeral();
                await interaction.EditOriginalResponseAsync(new(response)).ConfigureAwait(false);
                return;
            }

            var(suppress, recent, total, assignRole) = result.Data;
            if (assignRole)
                await user.AddRoleAsync(Config.WarnRoleId, ctx.Client, ctx.Guild, reason).ConfigureAwait(false);
            if (!suppress)
            {
                var userMsgContent = await Warnings.GetDefaultWarningMessageAsync(ctx.Client, user, reason, recent, total, ctx.User).ConfigureAwait(false);
                var userMsg = new DiscordMessageBuilder()
                    .WithContent(userMsgContent)
                    .AddMention(new UserMention(user.Id));
                if (message is not null)
                    userMsg.WithReply(message.Id, mention: true);
                await ctx.Channel.SendMessageAsync(userMsg).ConfigureAwait(false);
            }
            await Warnings.ListUserWarningsAsync(ctx.Client, interaction, user.Id, user.Username.Sanitize()).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            var msg = new DiscordInteractionResponseBuilder()
                .AsEphemeral()
                .WithContent($"{Config.Reactions.Failure} Failed to save warning");
            await interaction.EditOriginalResponseAsync(new(msg)).ConfigureAwait(false);
        }
    }
}