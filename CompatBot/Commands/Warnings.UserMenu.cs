using CompatApiClient.Utils;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.UserCommands;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot.Commands;

internal static class WarningsContextMenus
{
    [Command("❗ Warn user"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [Description("Give user a warning")]
    public static async ValueTask Warn(UserCommandContext ctx, DiscordUser user)
    {
        var interactivity = ctx.Extension.ServiceProvider.GetService<InteractivityExtension>();
        if (interactivity is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
            return;
        }

        var interaction = ctx.Interaction;
        var modal = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithCustomId($"modal:warn:{Guid.NewGuid():n}")
            .WithTitle("Issue new warning")
            .AddComponents(
                new DiscordTextInputComponent(
                    "Warning reason",
                    "warning",
                    "Rule #2",
                    min_length: 2
                )
            );
        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        try
        {
            InteractivityResult<ModalSubmittedEventArgs> modalResult;
            string reason;
            do
            {
                modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
                if (modalResult.TimedOut)
                    return;
            } while (!modalResult.Result.Values.TryGetValue("warning", out reason!));

            interaction = modalResult.Result.Interaction;
            await interaction.CreateResponseAsync(
                DiscordInteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral()
            ).ConfigureAwait(false);
            var (saved, suppress, recent, total) = await Warnings.AddAsync(user.Id, ctx.User, reason).ConfigureAwait(false);
            if (!saved)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't save the warning, please try again", ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (!suppress)
            {
                var userMsgContent = $"{Config.Reactions.Success} User warning saved, {user.Mention} has {recent} recent warning{StringUtils.GetSuffix(recent)} ({total} total)";
                var userMsg = new DiscordMessageBuilder()
                    .WithContent(userMsgContent)
                    .AddMention(UserMention.All);
                await ctx.Channel.SendMessageAsync(userMsg).ConfigureAwait(false);
            }
            await Warnings.ListUserWarningsAsync(ctx.Client, ctx.Interaction, user.Id, user.Username.Sanitize()).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            var msg = new DiscordInteractionResponseBuilder()
                .AsEphemeral()
                .WithContent($"{Config.Reactions.Failure} Failed to change nickname, check bot's permissions");
            await interaction.EditOriginalResponseAsync(new(msg)).ConfigureAwait(false);
        }
    }
    
    [Command("❗ Warn user"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    [Description("Give user a warning")]
    public static async ValueTask Warn(MessageCommandContext ctx, DiscordMessage message)
    {
        var interactivity = ctx.Extension.ServiceProvider.GetService<InteractivityExtension>();
        if (interactivity is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
            return;
        }

        var interaction = ctx.Interaction;
        var modal = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithCustomId($"modal:warn:{Guid.NewGuid():n}")
            .WithTitle("Issue new warning")
            .AddComponents(
                new DiscordTextInputComponent(
                    "Warning reason",
                    "warning",
                    "Rule #2",
                    min_length: 2
                )
            );
        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        try
        {
            InteractivityResult<ModalSubmittedEventArgs> modalResult;
            string reason;
            do
            {
                modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
                if (modalResult.TimedOut)
                    return;
            } while (!modalResult.Result.Values.TryGetValue("warning", out reason!));

            interaction = modalResult.Result.Interaction;
            await interaction.CreateResponseAsync(
                DiscordInteractionResponseType.DeferredChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral()
            ).ConfigureAwait(false);
            var user = message.Author!;
            var (saved, suppress, recent, total) = await Warnings.AddAsync(user.Id, ctx.User, reason, message.Content.Sanitize()).ConfigureAwait(false);
            if (!saved)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't save the warning, please try again", ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (!suppress)
            {
                var userMsgContent = $"{Config.Reactions.Success} User warning saved, {user.Mention} has {recent} recent warning{StringUtils.GetSuffix(recent)} ({total} total)";
                var userMsg = new DiscordMessageBuilder()
                    .WithContent(userMsgContent)
                    .AddMention(UserMention.All);
                await ctx.Channel.SendMessageAsync(userMsg).ConfigureAwait(false);
            }
            await Warnings.ListUserWarningsAsync(ctx.Client, ctx.Interaction, user.Id, user.Username.Sanitize()).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            var msg = new DiscordInteractionResponseBuilder()
                .AsEphemeral()
                .WithContent($"{Config.Reactions.Failure} Failed to change nickname, check bot's permissions");
            await interaction.EditOriginalResponseAsync(new(msg)).ConfigureAwait(false);
        }
    }
}