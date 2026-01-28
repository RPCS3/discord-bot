using System.Diagnostics.CodeAnalysis;
using System.IO;
using CompatApiClient.Utils;
using CompatBot.EventHandlers;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Exceptions;

namespace CompatBot.Utils;

public static class DiscordClientExtensions
{
    public static async ValueTask<DiscordMember?> GetMemberAsync(this DiscordClient client, ulong? guildId, ulong userId)
    {
        try
        {
            var query = client.Guilds.AsEnumerable();
            if (guildId.HasValue)
                query = query.Where(g => g.Key == guildId.Value);
            var guildList = query.ToList();
            var result = guildList.SelectMany(g => g.Value.Members.Values).FirstOrDefault(m => m.Id == userId);
            if (result is not null)
                return result;

            var fetchTasks = guildList.Select(async g => await g.Value.GetMemberAsync(userId)).ToList();
            foreach (var task in fetchTasks)
            {
                result = await task.ConfigureAwait(false);
                if (result is not null)
                    return result;
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to fetch member data");
        }
        return null;
    }

    public static ValueTask<DiscordMember?> GetMemberAsync(this DiscordClient client, ulong userId) => GetMemberAsync(client, (ulong?)null, userId);
    public static ValueTask<DiscordMember?> GetMemberAsync(this DiscordClient client, DiscordUser user) => GetMemberAsync(client, user.Id);
    public static ValueTask<DiscordMember?> GetMemberAsync(this DiscordClient client, ulong guildId, DiscordUser user) => GetMemberAsync(client, guildId, user.Id);
    public static ValueTask<DiscordMember?> GetMemberAsync(this DiscordClient client, DiscordGuild? guild, DiscordUser user) => GetMemberAsync(client, guild, user.Id);
    public static ValueTask<DiscordMember?> GetMemberAsync(this DiscordClient client, DiscordGuild? guild, ulong userId)
        => guild is null ? GetMemberAsync(client, userId) : GetMemberAsync(client, guild.Id, userId);

    public static ValueTask<string> GetUserNameAsync(this CommandContext ctx, ulong userId, bool? forDmPurposes = null, string defaultName = "Unknown user")
        => GetUserNameAsync(ctx.Client, ctx.Channel, userId, forDmPurposes, defaultName);
    
    public static async ValueTask<string> GetUserNameAsync(this DiscordClient client, DiscordChannel channel, ulong userId, bool? forDmPurposes = null, string defaultName = "Unknown user")
    {
        var isPrivate = forDmPurposes ?? channel.IsPrivate;
        if (userId is 0)
            return "";

        try
        {
            return (await client.GetUserAsync(userId)).Username;
        }
        catch (NotFoundException)
        {
            return isPrivate ? $"@{userId}" : defaultName;
        }
    }

    public static async ValueTask RemoveReactionAsync(this DiscordMessage message, DiscordEmoji emoji)
    {
        try
        {
            await message.DeleteOwnReactionAsync(emoji).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    public static async ValueTask ReactWithAsync(this DiscordMessage message, DiscordEmoji emoji, string? fallbackMessage = null, bool? showBoth = null)
    {
        try
        {
            var isDm = message.Channel?.IsPrivate ?? true;
            showBoth ??= isDm;
            var canReact = isDm || (message.Channel?.PermissionsFor(message.Channel.Guild.CurrentMember).HasPermission(DiscordPermission.AddReactions) ?? false);
            if (canReact)
                await message.CreateReactionAsync(emoji).ConfigureAwait(false);
            if ((!canReact || showBoth.Value) && !string.IsNullOrEmpty(fallbackMessage) && message.Channel is not null)
                await message.Channel.SendMessageAsync(fallbackMessage).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    public static ValueTask RemoveReactionAsync(this TextCommandContext ctx, DiscordEmoji emoji) => RemoveReactionAsync(ctx.Message, emoji);

    public static ValueTask ReactWithAsync(this TextCommandContext ctx, DiscordEmoji emoji, string? fallbackMessage = null, bool? showBoth = null)
        => ReactWithAsync(ctx.Message, emoji, fallbackMessage, showBoth ?? (ctx.Prefix == Config.AutoRemoveCommandPrefix));

    public static async Task<IReadOnlyCollection<DiscordMessage>> GetMessagesBeforeAsync(this DiscordChannel channel, ulong beforeMessageId, int limit = 100, DateTime? timeLimit = null)
    {
        if (timeLimit > DateTime.UtcNow)
            throw new ArgumentException("Time limit can't be set in the future", nameof(timeLimit));

        var afterTime = timeLimit ?? DateTime.UtcNow.AddSeconds(-30);
        var messages = await channel.GetMessagesBeforeCachedAsync(beforeMessageId, limit).ConfigureAwait(false);
        return messages.TakeWhile(m => m.CreationTimestamp > afterTime).ToList().AsReadOnly();
    }

    public static async ValueTask<DiscordMessage?> ReportAsync(
        this DiscordClient client,
        string infraction,
        DiscordMessage message,
        string trigger,
        string? matchedOn,
        int? filterId,
        string? context,
        ReportSeverity severity,
        string? actionList = null,
        DateTime? timestamp = null)
    {
        var logChannel = await client.GetChannelAsync(Config.BotLogId).ConfigureAwait(false);
        if (logChannel is null)
            return null;

        var embedBuilder = await MakeReportTemplateAsync(client, infraction, filterId, message, severity, actionList, timestamp).ConfigureAwait(false);
        var reportText = string.IsNullOrEmpty(trigger) ? "" : $"Triggered by: `{matchedOn?.Trim(40) ?? trigger}`{Environment.NewLine}";
        if (!string.IsNullOrEmpty(context))
            reportText += $"Triggered in: ```{context.Sanitize()}```{Environment.NewLine}";
        embedBuilder.Description = reportText + embedBuilder.Description;
        return await logChannel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embedBuilder.Build())).ConfigureAwait(false);
    }

    public static async ValueTask<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, DiscordMessage message, IEnumerable<DiscordMember?> reporters, string? comment, ReportSeverity severity)
    {
        var getLogChannelTask = client.GetChannelAsync(Config.BotLogId);
        var embedBuilder = await MakeReportTemplateAsync(client, infraction, null, message, severity).ConfigureAwait(false);
        var reportText = string.IsNullOrEmpty(comment) ? "" : comment.Sanitize() + Environment.NewLine;
        embedBuilder.Description = (reportText + embedBuilder.Description).Trim(EmbedPager.MaxDescriptionLength);
        var mentions = reporters.Where(m => m is not null).Select(GetMentionWithNickname!);
        embedBuilder.AddField("Reporters", string.Join(Environment.NewLine, mentions));
        var logChannel = await getLogChannelTask.ConfigureAwait(false);
        return await logChannel.SendMessageAsync(
            new DiscordMessageBuilder()
                .AddEmbed(embedBuilder.Build())
        ).ConfigureAwait(false);
    }

    public static async ValueTask<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, string description, ICollection<DiscordMember>? potentialVictims, ReportSeverity severity)
    {
        var result = new DiscordEmbedBuilder
        {
            Title = infraction,
            Color = GetColor(severity),
            Description = description.Trim(EmbedPager.MaxDescriptionLength),
        };
        if (potentialVictims?.Count > 0)
            result.AddField("Potential Targets", string.Join(Environment.NewLine, potentialVictims.Select(GetMentionWithNickname)).Trim(EmbedPager.MaxFieldLength));
        var logChannel = await client.GetChannelAsync(Config.BotLogId).ConfigureAwait(false);
        return await logChannel.SendMessageAsync(
            new DiscordMessageBuilder()
                .AddEmbed(result.Build())
        ).ConfigureAwait(false);
    }

    public static string GetMentionWithNickname(this DiscordMember member)
        => string.IsNullOrEmpty(member.Nickname)
            ? $"<@{member.Id}> (`{member.Username.Sanitize()}#{member.Discriminator}`)"
            : $"<@{member.Id}> (`{member.Username.Sanitize()}#{member.Discriminator}`, shown as `{member.Nickname.Sanitize()}`)";

    public static async ValueTask<string> GetUsernameWithNicknameAsync(this DiscordUser user, DiscordClient client, DiscordGuild? guild = null)
        => (await client.GetMemberAsync(guild, user).ConfigureAwait(false)).GetUsernameWithNickname()
           ?? $"`{user.Username.Sanitize()}#{user.Discriminator}`";

    public static string? GetUsernameWithNickname(this DiscordMember? member)
        => member is null
            ? null
            : string.IsNullOrEmpty(member.Nickname)
                ? $"`{member.Username.Sanitize()}#{member.Discriminator}`"
                : $"`{member.Username.Sanitize()}#{member.Discriminator}` (shown as `{member.Nickname.Sanitize()}`)";

    [return: NotNullIfNotNull(nameof(fallbackEmoji))]
    public static DiscordEmoji? GetEmoji(this DiscordClient client, string? emojiName, string? fallbackEmoji = null)
        => GetEmoji(client, emojiName, fallbackEmoji == null ? null : DiscordEmoji.FromUnicode(fallbackEmoji));

    [return: NotNullIfNotNull(nameof(fallbackEmoji))]
    public static DiscordEmoji? GetEmoji(this DiscordClient client, string? emojiName, DiscordEmoji? fallbackEmoji)
    {
        if (string.IsNullOrEmpty(emojiName))
            return fallbackEmoji;

        if (DiscordEmoji.TryFromName(client, emojiName, true, out var result))
            return result;
        
        if (DiscordEmoji.TryFromName(client, $":{emojiName}:", true, out result))
            return result;
        
        if (DiscordEmoji.TryFromUnicode(emojiName, out result))
            return result;
        return fallbackEmoji;
    }

    public static Task SendMessageAsync(this DiscordChannel channel, string message, byte[]? attachment, string? filename)
    {
        if (!string.IsNullOrEmpty(filename) && attachment?.Length > 0)
            return channel.SendMessageAsync(new DiscordMessageBuilder().AddFile(filename, new MemoryStream(attachment)).WithContent(message));
        return channel.SendMessageAsync(message);
    }

    private static async ValueTask<DiscordEmbedBuilder> MakeReportTemplateAsync(
        DiscordClient client,
        string infraction,
        int? filterId,
        DiscordMessage message,
        ReportSeverity severity,
        string? actionList = null,
        DateTime? timestamp = null)
    {
        var content = message.Content;
        if (message.Channel.IsPrivate)
            severity = ReportSeverity.None;
        var needsAttention = severity > ReportSeverity.Low;
        if (message.Embeds?.Count > 0)
        {
            if (!string.IsNullOrEmpty(content))
                content += Environment.NewLine;

            var srcEmbed = message.Embeds.First();
            content += $"🔤 {srcEmbed.Title}";
            if (srcEmbed.Fields?.Any() ?? false)
                content += $"{Environment.NewLine}{srcEmbed.Description}{Environment.NewLine}+{srcEmbed.Fields.Count} fields";
        }
        if (message.Attachments?.Count > 0)
        {
            if (!string.IsNullOrEmpty(content))
                content += Environment.NewLine;
            content += string.Join(Environment.NewLine, message.Attachments.Select(a => "📎 " + a.FileName));
        }

        if (string.IsNullOrEmpty(content))
            content = "🤔 something fishy is going on here, there was no message or attachment";
        DiscordMember? author = null;
        try
        {
            author = await client.GetMemberAsync(message.Author).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to get the member info for user {message.Author.Id} ({message.Author.Username})");
        }
        var result = new DiscordEmbedBuilder
            {
                Title = infraction,
                Color = GetColor(severity),
            }.AddField("Violator", author is null ? message.Author.Mention : GetMentionWithNickname(author), true)
            .AddField("Channel", message.Channel.IsPrivate ? "Bot's DM" : message.JumpLink.ToString(), true);
        if (filterId is not null)
            result.AddField("Filter #", filterId.ToString(), true);
        result.AddField("Content of the offending item", content.Trim(EmbedPager.MaxFieldLength));
        if (actionList is { Length: >0})
            result.AddField("Filter Actions", actionList, true);
        if (timestamp is not null)
            result.AddField("Post to action lag", (timestamp.Value - message.Timestamp).AsShortTimespan(), true);
        if (needsAttention && !message.Channel.IsPrivate)
            result.AddField("Link to the message", message.JumpLink.ToString(), true);
#if DEBUG
        result.WithFooter("Test bot instance");
#endif
        return result;
    }

    private static DiscordColor GetColor(ReportSeverity severity)
        => severity switch
        {
            ReportSeverity.Low => Config.Colors.LogInfo,
            ReportSeverity.Medium => Config.Colors.LogNotice,
            ReportSeverity.High => Config.Colors.LogAlert,
            _ => Config.Colors.LogUnknown
        };
}