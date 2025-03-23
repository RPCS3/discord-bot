using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Interactivity;

namespace CompatBot.Commands;

internal static class MessageMenuCommands
{
    /*
    [Command("🗨️ message")]
    [Description("12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async ValueTask AnalyzerTest(){}
    */
    
    // anyone can use this
    [Command("💬 Explain"), SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask ShowToUser(MessageCommandContext ctx, DiscordMessage replyTo)
    {
        if (ctx.Extension.ServiceProvider.GetService<InteractivityExtension>() is not {} interactivity)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
            return;
        }
        
        var placeholder = StatsStorage.GetExplainStats().FirstOrDefault().name ?? "rule 1";
        var modal = new DiscordInteractionResponseBuilder()
            .WithTitle("Explain Prompt")
            .AddComponents(new DiscordTextInputComponent("Term to explain", "term", placeholder, min_length: 1))
            .WithCustomId($"modal:explain:{Guid.NewGuid():n}");

        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        InteractivityResult<ModalSubmittedEventArgs> modalResult;
        string? term = null;
        (Explanation? explanation, string? fuzzyMatch, double score) result;
        do
        {
            modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
            if (modalResult.TimedOut)
                return;
        } while (!modalResult.Result.Values.TryGetValue("term", out term)
                 || (result = await Explain.LookupTerm(term).ConfigureAwait(false)) is not { score: >0.5 } );

        await modalResult.Result.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
            .WithContent($" {Config.Reactions.Success} Found term `{result.explanation?.Keyword ?? result.fuzzyMatch}` to send")
            .AsEphemeral()
        ).ConfigureAwait(false);
        var canPing = ModProvider.IsMod(ctx.User.Id);
        await Explain.SendExplanationAsync(result, term, replyTo, true, canPing).ConfigureAwait(false);
    }

    // non-whitenames can use these
    [Command("👮 Report to mods"), RequiresWhitelistedRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
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

            var modal = new DiscordInteractionResponseBuilder()
                .AsEphemeral()
                .WithCustomId($"modal:report:{Guid.NewGuid():n}")
                .WithTitle("Message Report")
                .AddComponents(
                    new DiscordTextInputComponent(
                        "Comment",
                        "comment",
                        "Describe why you report this message",
                        required: false,
                        style: DiscordTextInputStyle.Paragraph,
                        max_length: EmbedPager.MaxFieldLength
                    )
                );
            await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);
            var modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
            if (modalResult.TimedOut)
                return;
            
            modalResult.Result.Values.TryGetValue("comment", out var comment);
            await ctx.Client.ReportAsync("👀 Message report", message, [ctx.Member], comment, ReportSeverity.Medium).ConfigureAwait(false);
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

    [Command("🔍 Analyze log"), RequiresWhitelistedRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
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
        */
        
        try
        {
            await message.DeleteAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"{Config.Reactions.Success} Message removed", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to remove bot message");
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to remove bot message: {e.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
  
    // only bot mods can use this
    [Command("👎 Toggle bad update"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask BadUpdate(MessageCommandContext ctx, DiscordMessage message)
    {
        if (message.Embeds is not [DiscordEmbed embed]
            || message.Channel?.Id != Config.BotChannelId)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Invalid update announcement message", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var result = new DiscordEmbedBuilder(embed);
        const string warningTitle = "Warning!";
        if (embed.Color?.Value == Config.Colors.UpdateStatusGood.Value)
        {
            result = result.WithColor(Config.Colors.UpdateStatusBad);
            result.ClearFields();
            var warned = false;
            foreach (var f in embed.Fields!)
            {
                if (!warned && f.Name!.EndsWith("download"))
                {
                    result.AddField(warningTitle, "This build is known to have severe problems, please avoid downloading.");
                    warned = true;
                }
                result.AddField(f.Name!, f.Value!, f.Inline);
            }
        }
        else if (embed.Color?.Value == Config.Colors.UpdateStatusBad.Value)
        {
            result = result.WithColor(Config.Colors.UpdateStatusGood);
            result.ClearFields();
            foreach (var f in embed.Fields!)
            {
                if (f.Name is warningTitle)
                    continue;

                result.AddField(f.Name!, f.Value!, f.Inline);
            }
        }
        await message.UpdateOrCreateMessageAsync(message.Channel!, embed: result).ConfigureAwait(false);
        await ctx.RespondAsync($"{Config.Reactions.Success} Done", ephemeral: true).ConfigureAwait(false);
    }
}