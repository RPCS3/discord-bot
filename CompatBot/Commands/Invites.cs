using System.IO;
using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("invite"), RequiresBotModRole]
internal static class Invites
{
    [Command("list")]
    [Description("List all allowed server invites")]
    public static async ValueTask List(SlashCommandContext ctx)
    {
        const string linkPrefix = "discord.gg/";
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        var whitelistedInvites = await db.WhitelistedInvites.ToListAsync().ConfigureAwait(false);
        if (whitelistedInvites.Count is 0)
        {
            await ctx.RespondAsync("There are no whitelisted discord servers", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        var table = new AsciiTable(
            new AsciiColumn("ID", alignToRight: true),
            new AsciiColumn("Server ID", alignToRight: true),
            new AsciiColumn("Invite"),
            new AsciiColumn("Server Name")
        );
        foreach (var item in whitelistedInvites)
        {
            string? guildName = null;
            if (!string.IsNullOrEmpty(item.InviteCode))
                try
                {
                    var invite = await ctx.Client.GetInviteByCodeAsync(item.InviteCode).ConfigureAwait(false);
                    guildName = invite.Guild.Name;
                }
                catch { }
            if (string.IsNullOrEmpty(guildName))
                try
                {
                    var guild = await ctx.Client.GetGuildAsync(item.GuildId).ConfigureAwait(false);
                    guildName = guild.Name;
                }
                catch { }
            if (string.IsNullOrEmpty(guildName))
                guildName = item.Name ?? "";
            var link = "";
            if (!string.IsNullOrEmpty(item.InviteCode))
                link = linkPrefix + item.InviteCode;
            //discord expands invite links even if they're inside the code block for some reason
            table.Add(item.Id.ToString(), item.GuildId.ToString(), link /* + StringUtils.InvisibleSpacer*/, guildName.Sanitize());
        }
        var result = new StringBuilder()
            .AppendLine("Whitelisted discord servers:")
            .Append(table.ToString(false));

        await using var output = Config.MemoryStreamManager.GetStream();
        await using (var writer = new StreamWriter(output, leaveOpen: true))
            await writer.WriteAsync(result.ToString()).ConfigureAwait(false);
        output.Seek(0, SeekOrigin.Begin);
        var msg = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .AddFile("invites.txt", output);
        await ctx.RespondAsync(msg).ConfigureAwait(false);
    }

    [Command("whitelist")]
    [Description("Add a new guild to the invite whitelist")]
    public static async ValueTask Add(
        SlashCommandContext ctx,
        [Description("An invite link or an invite token")]
        string? invite = null,
        [Description("A Discord server ID to whitelist")]
        ulong? id = null
    )
    {
        if (invite is not {Length: >0} && id is null or 0)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} One of the arguments must be provided", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (id > 0)
        {
            if (await InviteWhitelistProvider.AddAsync(id.Value).ConfigureAwait(false))
                await ctx.RespondAsync($"{Config.Reactions.Success} Invite list was successfully updated", ephemeral: true).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to update the invite list", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        var (_, _, invites) = await ctx.Client.GetInvitesAsync(invite!, tryMessageAsACode: true).ConfigureAwait(false);
        if (invites.Count is 0)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to find any invite", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var errors = 0;
        foreach (var i in invites)
            if (!await InviteWhitelistProvider.AddAsync(i).ConfigureAwait(false))
                errors++;

        if (errors is 0)
            await ctx.RespondAsync($"{Config.Reactions.Success} Invite whitelist was successfully updated", ephemeral: true).ConfigureAwait(false);
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to add {errors} invite{StringUtils.GetSuffix(errors)} to the whitelist").ConfigureAwait(false);
    }


    [Command("update")]
    [Description("Update server invite code")]
    public static async ValueTask Update(SlashCommandContext ctx, [Description("An invite link or an invite token")] string invite)
    {
        var (_, _, invites) = await ctx.Client.GetInvitesAsync(invite, tryMessageAsACode: true).ConfigureAwait(false);
        if (invites.Count is 0)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to find any invites", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var errors = 0;
        foreach (var i in invites)
            if (!await InviteWhitelistProvider.IsWhitelistedAsync(i).ConfigureAwait(false))
                errors++;

        if (errors is 0)
            await ctx.RespondAsync($"{Config.Reactions.Success} Invite whitelist was successfully updated", ephemeral: true).ConfigureAwait(false);
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to update {errors} invite{StringUtils.GetSuffix(errors)}", ephemeral: true).ConfigureAwait(false);
    }

    [Command("rename")]
    [Description("Give a custom name to a Discord server")]
    public static async ValueTask Rename(
        SlashCommandContext ctx,
        [Description("Invite ID to rename"), SlashAutoCompleteProvider<InviteAutoCompleteProvider>] int id,
        [Description("Custom server name"), MinMaxLength(3)] string name
    )
    {
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var invite = await wdb.WhitelistedInvites.FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
        if (invite is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Invalid filter ID", ephemeral: true).ConfigureAwait(false);
            return;
        }

        invite.Name = name;
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync($"{Config.Reactions.Success} Renamed guild {invite.GuildId} to {name}", ephemeral: true).ConfigureAwait(false);
    }

    [Command("remove"), TextAlias("delete", "del")]
    [Description("Remove discord server invite from whitelist")]
    public static async ValueTask Remove(
        SlashCommandContext ctx,
        [Description("Invite to remove"), SlashAutoCompleteProvider<InviteAutoCompleteProvider>] int id
    )
    {
        if (await InviteWhitelistProvider.RemoveAsync(id).ConfigureAwait(false))
            await ctx.RespondAsync($"{Config.Reactions.Success} Invite successfully removed", ephemeral: true).ConfigureAwait(false);
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to remove invite", ephemeral: true).ConfigureAwait(false);
    }
}
