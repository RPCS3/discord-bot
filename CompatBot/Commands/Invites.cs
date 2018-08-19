using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("invite"), Aliases("invites"), RequiresBotModRole, TriggersTyping]
    [Description("Used to manage Discord invites whitelist")]
    internal sealed class Invites: BaseCommandModuleCustom
    {
        [Command("list"), Aliases("show")]
        [Description("Lists all filters")]
        public async Task List(CommandContext ctx)
        {
            const string linkPrefix = "discord.gg/";
            using (var db = new BotDb())
            {
                var maxInviteLength = await db.WhitelistedInvites.Select(i => i.InviteCode).Where(c => c != null).MaxAsync(c => c.Length).ConfigureAwait(false);
                var linkLength = linkPrefix.Length + maxInviteLength;
                var header = $"ID   | {"Server ID",-18}";
                if (ctx.Channel.IsPrivate)
                    header += $" | {"Invite link".PadRight(linkLength)}";
                header += " | Server Name";
                var result = new StringBuilder("```")
                    .AppendLine(header)
                    .AppendLine("".PadRight(header.Length + 10, '-'));
                var whitelistedInvites = await db.WhitelistedInvites.ToListAsync().ConfigureAwait(false);
                if (whitelistedInvites.Count == 0)
                {
                    await ctx.RespondAsync("There are no whitelisted discord servers").ConfigureAwait(false);
                    return;
                }

                foreach (var item in whitelistedInvites)
                {
                    string guildName = null;
                    if (!string.IsNullOrEmpty(item.InviteCode))
                        try
                        {
                            var invite = await ctx.Client.GetInviteByCodeAsync(item.InviteCode).ConfigureAwait(false);
                            guildName = invite?.Guild.Name;
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
                    result.Append($"{item.Id:0000} | {item.GuildId,-18}");
                    if (ctx.Channel.IsPrivate)
                        result.Append(" | \u200d").Append(link.PadRight(linkLength));
                    result.Append(" | ").AppendLine(guildName.Sanitize());
                }
             await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
           }
        }

        [Command("whitelist"), Aliases("add", "allow"), Priority(10)]
        [Description("Adds a new guild to the whitelist")]
        public async Task Add(CommandContext ctx, [Description("A Discord server IDs to whitelist")] params ulong[] guildIds)
        {
            var errors = 0;
            foreach (var guildId in guildIds)
                if (!await InviteWhitelistProvider.AddAsync(guildId).ConfigureAwait(false))
                    errors++;
            await ctx.RespondAsync("command with the single ulong argument").ConfigureAwait(false);

            if (errors == 0)
                await ctx.ReactWithAsync(Config.Reactions.Success, "Invite whitelist was successfully updated!").ConfigureAwait(false);
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to add {errors} invite{StringUtils.GetSuffix(errors)} to the whitelist").ConfigureAwait(false);
            await List(ctx).ConfigureAwait(false);
        }

        [Command("whitelist"), Priority(0)]
        [Description("Adds a new guild to the whitelist")]
        public async Task Add(CommandContext ctx, [RemainingText, Description("An invite link or just an invite token")] string invite)
        {
            var (_, invites) = await ctx.Client.GetInvitesAsync(invite, true).ConfigureAwait(false);
            if (invites.Count == 0)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Need to specify an invite link or a server id").ConfigureAwait(false);
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

        [Command("rename"), Aliases("name")]
        [Description("Give a custom name for a Discord server")]
        public async Task Rename(CommandContext ctx, [Description("Filter ID to rename")] int id, [RemainingText, Description("Custom server name")] string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "A name must be provided").ConfigureAwait(false);
                return;
            }

            using (var db = new BotDb())
            {
                var invite = await db.WhitelistedInvites.FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
                if (invite == null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid filter ID").ConfigureAwait(false);
                    return;
                }

                invite.Name = name;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
            await List(ctx).ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete", "del")]
        [Description("Removes a piracy filter trigger")]
        public async Task Remove(CommandContext ctx, [Description("Filter IDs to remove, separated with spaces")] params int[] ids)
        {
            var failedIds = new List<int>();
            foreach (var id in ids)
                if (!await InviteWhitelistProvider.RemoveAsync(id).ConfigureAwait(false))
                    failedIds.Add(id);
            if (failedIds.Count > 0)
                await ctx.RespondAsync("Some IDs couldn't be removed: " + string.Join(", ", failedIds)).ConfigureAwait(false);
            else
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Invite{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
            await List(ctx).ConfigureAwait(false);
        }
    }
}
