namespace CompatBot.Commands;

[Description("Manage users who has forced nickname.")]
internal static class ForcedNicknames
{
    /*
    [Command("cleanup"), TextAlias("clean", "fix"), RequiresBotModRole]
    [Description("Removes zalgo from specified user nickname")]
    public async Task Cleanup(CommandContext ctx, [Description("Discord user to clean up")] DiscordUser discordUser)
    {
        if (await ctx.Client.GetMemberAsync(discordUser).ConfigureAwait(false) is not DiscordMember member)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to resolve guild member for user {discordUser.Username}#{discordUser.Discriminator}").ConfigureAwait(false);
            return;
        }

        var name = member.DisplayName;
        var newName = UsernameZalgoMonitor.StripZalgo(name, discordUser.Username, discordUser.Id);
        if (name == newName)
            await ctx.Channel.SendMessageAsync("Failed to remove any extra symbols").ConfigureAwait(false);
        else
        {
            try
            {
                await member.ModifyAsync(m => m.Nickname = new(newName)).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Renamed user to {newName}", true).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Config.Log.Warn($"Failed to rename user {discordUser.Username}#{discordUser.Discriminator}");
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to rename user to {newName}").ConfigureAwait(false);
            }
        }
    }
    */
    
    /*
    [Command("🔍 Dump"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [Description("Print hexadecimal binary representation of an UTF-8 encoded user name for diagnostic purposes")]
    public static async ValueTask Dump(UserCommandContext ctx, DiscordUser discordUser)
    {
        var name = discordUser.Username;
        var nameBytes = StringUtils.Utf8.GetBytes(name);
        var hex = BitConverter.ToString(nameBytes).Replace('-', ' ');
        var result = $"User ID: {discordUser.Id}\nUsername: {hex}";
        var member = await ctx.Client.GetMemberAsync(ctx.Guild, discordUser).ConfigureAwait(false);
        if (member is { Nickname: { Length: > 0 } nickname })
        {
            nameBytes = StringUtils.Utf8.GetBytes(nickname);
            hex = BitConverter.ToString(nameBytes).Replace('-', ' ');
            result += "\nNickname: " + hex;
        }
        await ctx.RespondAsync(result, ephemeral: true).ConfigureAwait(false);
    }
    */

    /*
    [Command("list"), RequiresBotModRole]
    [Description("Lists all users who have restricted nickname.")]
    public async Task List(CommandContext ctx)
    {
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        var selectExpr = db.ForcedNicknames.AsNoTracking();
        if (ctx.Guild != null)
            selectExpr = selectExpr.Where(mem => mem.GuildId == ctx.Guild.Id);

        var forcedNicknames = (
            from m in selectExpr.AsEnumerable()
            orderby m.UserId, m.Nickname
            let result = new {m.UserId, m.Nickname}
            select result
        ).ToList();
        if (forcedNicknames.Count == 0)
        {
            await ctx.Channel.SendMessageAsync("No users with forced nicknames").ConfigureAwait(false);
            return;
        }

        var table = new AsciiTable(
            new AsciiColumn("ID", !ctx.Channel.IsPrivate || !await ctx.User.IsWhitelistedAsync(ctx.Client).ConfigureAwait(false)),
            new AsciiColumn("Username"),
            new AsciiColumn("Forced nickname")
        );
        var previousUser = 0ul;
        foreach (var forcedNickname in forcedNicknames.Distinct())
        {
            var sameUser = forcedNickname.UserId == previousUser;
            var username = sameUser ? "" : await ctx.GetUserNameAsync(forcedNickname.UserId).ConfigureAwait(false);
            table.Add( sameUser ? "" : forcedNickname.UserId.ToString(), username, forcedNickname.Nickname);
            previousUser = forcedNickname.UserId;
        }
        await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
    }
    */
}
