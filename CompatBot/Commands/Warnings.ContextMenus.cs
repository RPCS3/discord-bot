using CompatApiClient.Utils;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Commands.Processors.UserCommands;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.DependencyInjection;

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

        var interaction = ctx.Interaction;
        var modal = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithCustomId($"modal:warn:{Guid.NewGuid():n}")
            .WithTitle("Issue new warning")
            .AddTextInputComponent(new(
                "Warning reason",
                "warning",
                "Rule #2",
                min_length: 2
            ));
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
            user ??= message?.Author!;
            var (saved, suppress, recent, total) = await Warnings.AddAsync(user.Id, ctx.User, reason, message?.Content.Sanitize()).ConfigureAwait(false);
            if (!saved)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't save the warning, please try again", ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (!suppress)
            {
                var userMsgContent = $"""
                      User warning saved, {user.Mention} has {recent} recent warning{StringUtils.GetSuffix(recent)} ({total} total)
                      Warned for: {reason} by {ctx.User.Mention}
                      """;
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
                .WithContent($"{Config.Reactions.Failure} Failed to change nickname, check bot's permissions");
            await interaction.EditOriginalResponseAsync(new(msg)).ConfigureAwait(false);
        }
    }
}