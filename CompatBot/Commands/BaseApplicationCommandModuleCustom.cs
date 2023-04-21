using System;
using System.Net;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace CompatBot.Commands;

internal class BaseApplicationCommandModuleCustom : ApplicationCommandModule
{
    private DateTimeOffset executionStart;

    public override async Task<bool> BeforeSlashExecutionAsync(InteractionContext ctx)
    {
        executionStart = DateTimeOffset.UtcNow;
        if (ctx is {Channel.Name: "media", Interaction: { Type: InteractionType.ApplicationCommand, Data.Name: not ("warn" or "report") } })
        {
            //todo: look what's available in data
            Config.Log.Info($"Ignoring slash command from {ctx.User.Username} (<@{ctx.User.Id}>) in #media: {ctx.Interaction.Data}");
            await ctx.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent($"Only `warn` and `report` are allowed in {ctx.Channel.Mention}").AsEphemeral()
            ).ConfigureAwait(false);
            Config.TelemetryClient?.TrackRequest(ctx.Interaction.Data.Name, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.Forbidden.ToString(), true);
            return false;
        }

        var disabledCmds = DisabledCommandsProvider.Get();
        if (disabledCmds.Contains(ctx.Interaction.Data.Name) && !disabledCmds.Contains("*"))
        {
            await ctx.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().WithContent($"Command `{ctx.Interaction.Data.Name}` is currently disabled").AsEphemeral()
            ).ConfigureAwait(false);
            Config.TelemetryClient?.TrackRequest(ctx.Interaction.Data.Name, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.Locked.ToString(), true);
            return false;
        }

        return await base.BeforeSlashExecutionAsync(ctx).ConfigureAwait(false);
    }

    public override Task AfterSlashExecutionAsync(InteractionContext ctx)
    {
        StatsStorage.IncCmdStat(ctx.Interaction.Data.Name);
        Config.TelemetryClient?.TrackRequest(ctx.Interaction.Data.Name, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.OK.ToString(), true);
        return base.AfterSlashExecutionAsync(ctx);
    }
}