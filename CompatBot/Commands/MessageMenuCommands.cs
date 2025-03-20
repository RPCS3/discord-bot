using CompatBot.Database;
using CompatBot.Database.Providers;
using DSharpPlus.Commands.Processors.MessageCommands;
using DSharpPlus.Interactivity;

namespace CompatBot.Commands;

public class MessageMenuCommands
{
    /*
    [Command("🗨️ message")]
    [Description("12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async ValueTask AnalyzerTest(){}
    */
    
    [Command("💬 Explain"), SlashCommandTypes(DiscordApplicationCommandType.MessageContextMenu)]
    public static async ValueTask ShowToUser(MessageCommandContext ctx, DiscordMessage replyTo)
    {
        var interactivity = ctx.Extension.ServiceProvider.GetService<InteractivityExtension>();
        if (interactivity is null)
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
}