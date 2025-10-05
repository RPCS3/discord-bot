﻿using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot.Commands;

internal static class MessageMenuCommands
{
    /*
    [Command("🗨️ message command with very long name")]
    [Description("12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async ValueTask AnalyzerTest(){}
    */

    // limited to 5 commands per menu

    // anyone can use this
    [Command("💬 Explain"), RequiresSupporterRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask ShowToUser(MessageCommandContext ctx, DiscordMessage replyTo)
    {
        if (ctx.Extension.ServiceProvider.GetService<InteractivityExtension>() is not {} interactivity)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
            return;
        }
        
        var placeholder = StatsStorage.GetExplainStats().FirstOrDefault().name ?? "rule 1";
        var modal = new DiscordModalBuilder()
            .WithTitle("Explain Prompt")
            .AddTextInput(
                new("term", placeholder, min_length: 1),
                "Term to explain"
            ).WithCustomId($"modal:explain:{Guid.NewGuid():n}");
        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        InteractivityResult<ModalSubmittedEventArgs> modalResult;
        IModalSubmission? value;
        (Explanation? explanation, string? fuzzyMatch, double score) result;
        do
        {
            modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
            if (modalResult.TimedOut)
                return;
        } while (!modalResult.Result.Values.TryGetValue("term", out value)
                 || value is not TextInputModalSubmission {Value: {Length: >0} textValue}
                 || (result = await Explain.LookupTerm(textValue).ConfigureAwait(false)) is not { score: >0.5 } );

        await modalResult.Result.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
            .WithContent($" {Config.Reactions.Success} Found term `{result.explanation?.Keyword ?? result.fuzzyMatch}` to send")
            .AsEphemeral()
        ).ConfigureAwait(false);
        var canPing = ModProvider.IsMod(ctx.User.Id);
        var term = ((TextInputModalSubmission)value).Value;
        await Explain.SendExplanationAsync(result, term, replyTo, true, canPing).ConfigureAwait(false);
    }

    // non-whitenames can use these
    [Command("👮 Report to mods"), RequiresSupporterRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask Report(MessageCommandContext ctx, DiscordMessage message)
    {
        try
        {
            if (message.Reactions.Any(r => r.IsMe && r.Emoji == Config.Reactions.Moderated))
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Message was already reported", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            if (ctx.Extension.ServiceProvider.GetService<InteractivityExtension>() is not {} interactivity)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
                return;
            }

            var modal = new DiscordModalBuilder()
                .WithCustomId($"modal:report:{Guid.NewGuid():n}")
                .WithTitle("Message Report")
                .AddTextInput(new(
                    "comment",
                    required: false,
                    style: DiscordTextInputStyle.Paragraph,
                    max_length: EmbedPager.MaxFieldLength
                ),
                "Comment",
                "Describe why you are reporting this message");
            await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);
            var modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
            if (modalResult.TimedOut)
                return;
            
            modalResult.Result.Values.TryGetValue("comment", out var comment);
            await ctx.Client.ReportAsync("👀 Message report", message, [ctx.Member], ((TextInputModalSubmission?)comment)?.Value, ReportSeverity.Medium).ConfigureAwait(false);
            await message.ReactWithAsync(Config.Reactions.Moderated).ConfigureAwait(false);
            await modalResult.Result.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"{Config.Reactions.Success} Message was reported")
                    .AsEphemeral()
            ).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to report the message", ephemeral: true).ConfigureAwait(false);
        }
    }

    [Command("🔍 Analyze log"), RequiresSupporterRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask Reanalyze(MessageCommandContext ctx, DiscordMessage message)
    {
        try
        {
            LogParsingHandler.EnqueueLogProcessing(ctx.Client, ctx.Channel, message, ctx.Member, true, true);
        }
        catch
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to enqueue log analysis", ephemeral: true).ConfigureAwait(false);
            return;
        }
        await ctx.RespondAsync($"{Config.Reactions.Success} Message was enqueued for analysis", ephemeral: true).ConfigureAwait(false);

    }

    /*
    [Command("🔇 Shut up bot"), RequiresWhitelistedRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask Shutup(MessageCommandContext ctx, DiscordMessage message)
    {
        if (!message.Author.IsBotSafeCheck()
            || message.Author != ctx.Client.CurrentUser)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} You can only remove bot messages", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        /*
        if (message.CreationTimestamp.Add(Config.ShutupTimeLimitInMin) < DateTime.UtcNow)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Message is too old to remove", ephemeral: true).ConfigureAwait(false);
            return;
        }
        #1#
        
        try
        {
            await message.DeleteAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"{Config.Reactions.Success} Message removed", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to remove bot message");
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to remove bot message: {e.Message}".Trim(EmbedPager.MaxMessageLength), ephemeral: true).ConfigureAwait(false);
        }
    }
    */
  
    // only bot mods can use this
    /*
    [Command("👎 Toggle bad update"), RequiresSmartlistedRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask BadUpdate(MessageCommandContext ctx, DiscordMessage message)
    {
        if (message.Embeds is not [DiscordEmbed embed]
            || message.Channel?.Id != Config.BotChannelId)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Invalid update announcement message", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        await Starbucks.ToggleBadUpdateAnnouncementAsync(message).ConfigureAwait(false);
        await ctx.RespondAsync($"{Config.Reactions.Success} Done", ephemeral: true).ConfigureAwait(false);
    }
    */
}