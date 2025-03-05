using System.IO;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Group("invite"), Aliases("invites"), RequiresBotModRole]
[Description("Used to manage Discord invites whitelist")]
internal sealed class Invites: BaseCommandModuleCustom
{
    [Command("list"), Aliases("show")]
    [Description("Lists all filters")]
    public async Task List(CommandContext ctx)
    {
        const string linkPrefix = "discord.gg/";
        await using var db = new BotDb();
        var whitelistedInvites = await db.WhitelistedInvites.ToListAsync().ConfigureAwait(false);
        if (whitelistedInvites.Count == 0)
        {
            await ctx.Channel.SendMessageAsync("There are no whitelisted discord servers").ConfigureAwait(false);
            return;
        }

        var table = new AsciiTable(
            new AsciiColumn("ID", alignToRight: true),
            new AsciiColumn("Server ID", alignToRight: true),
            new AsciiColumn("Invite", disabled: !ctx.Channel.IsPrivate),
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
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("invites.txt", output)).ConfigureAwait(false);
    }

    [Command("whitelist"), Aliases("add", "allow"), Priority(10)]
    [Description("Adds a new guild to the whitelist")]
    public async Task Add(CommandContext ctx, [Description("A Discord server IDs to whitelist")] params ulong[] guildIds)
    {
        var errors = 0;
        foreach (var guildId in guildIds)
            if (!await InviteWhitelistProvider.AddAsync(guildId).ConfigureAwait(false))
                errors++;

        if (errors == 0)
            await ctx.ReactWithAsync(Config.Reactions.Success, "Invite whitelist was successfully updated!").ConfigureAwait(false);
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to add {errors} invite{StringUtils.GetSuffix(errors)} to the whitelist").ConfigureAwait(false);
    }

    [Command("whitelist"), Priority(0)]
    [Description("Adds a new guild to the whitelist")]
    public async Task Add(CommandContext ctx, [RemainingText, Description("An invite link or just an invite token")] string invite)
    {
        var (_, _, invites) = await ctx.Client.GetInvitesAsync(invite, tryMessageAsACode: true).ConfigureAwait(false);
        if (invites.Count == 0)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Need to specify an invite link or token").ConfigureAwait(false);
            return;
        }

        var errors = 0;
        foreach (var i in invites)
            if (!await InviteWhitelistProvider.AddAsync(i).ConfigureAwait(false))
                errors++;

        if (errors == 0)
            await ctx.ReactWithAsync(Config.Reactions.Success, "Invite whitelist was successfully updated!").ConfigureAwait(false);
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to add {errors} invite{StringUtils.GetSuffix(errors)} to the whitelist").ConfigureAwait(false);
        await List(ctx).ConfigureAwait(false);
    }


    [Command("update")]
    [Description("Updates server invite code")]
    public async Task Update(CommandContext ctx, [RemainingText, Description("An invite link or an invite token")] string invite)
    {
        var (_, _, invites) = await ctx.Client.GetInvitesAsync(invite, tryMessageAsACode: true).ConfigureAwait(false);
        if (invites.Count == 0)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Need to specify an invite link or token").ConfigureAwait(false);
            return;
        }

        var errors = 0;
        foreach (var i in invites)
            if (!await InviteWhitelistProvider.IsWhitelistedAsync(i).ConfigureAwait(false))
                errors++;

        if (errors == 0)
            await ctx.ReactWithAsync(Config.Reactions.Success, "Invite whitelist was successfully updated!").ConfigureAwait(false);
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to update {errors} invite{StringUtils.GetSuffix(errors)}").ConfigureAwait(false);
        await List(ctx).ConfigureAwait(false);
    }

    [Command("rename"), Aliases("name")]
    [Description("Give a custom name for a Discord server")]
    public async Task Rename(CommandContext ctx, [Description("Filter ID to rename")] int id, [RemainingText, Description("Custom server name")] string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "A name must be provided").ConfigureAwait(false);
            return;
        }

        await using var db = new BotDb();
        var invite = await db.WhitelistedInvites.FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
        if (invite == null)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid filter ID").ConfigureAwait(false);
            return;
        }

        invite.Name = name;
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        await List(ctx).ConfigureAwait(false);
    }

    [Command("remove"), Aliases("delete", "del")]
    [Description("Removes server from whitelist")]
    public async Task Remove(CommandContext ctx, [Description("Filter IDs to remove, separated with spaces")] params int[] ids)
    {
        var failedIds = new List<int>();
        foreach (var id in ids)
            if (!await InviteWhitelistProvider.RemoveAsync(id).ConfigureAwait(false))
                failedIds.Add(id);
        if (failedIds.Count > 0)
            await ctx.Channel.SendMessageAsync("Some IDs couldn't be removed: " + string.Join(", ", failedIds)).ConfigureAwait(false);
        else
            await ctx.ReactWithAsync(Config.Reactions.Success, $"Invite{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
        await List(ctx).ConfigureAwait(false);
    }
}