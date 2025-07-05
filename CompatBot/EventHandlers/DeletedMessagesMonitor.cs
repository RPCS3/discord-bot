﻿using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

internal static class DeletedMessagesMonitor
{
	public static readonly MemoryCache RemovedByBotCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(10) });
	public static readonly TimeSpan CacheRetainTime = TimeSpan.FromMinutes(1);
	private static readonly SemaphoreSlim PostLock = new(1);

	// when someone uploads nasty content and discord nukes that account
	// if somebody else is re-uploading the nuked content, they get nuked as well
	// which is not great when it happens to be the bot
	public static async Task OnMessageDeleted(DiscordClient c, MessageDeletedEventArgs e)
	{
		if (Config.DeletedMessagesLogChannelId is 0 || e.Channel.IsPrivate)
			return;

		var msg = e.Message;
		if (msg.Author is null)
			return;

		if (msg.Author.IsCurrent || msg.Author.IsBotSafeCheck())
			return;

		if (RemovedByBotCache.TryGetValue(msg.Id, out _))
			return;

		var usernameWithNickname = await msg.Author.GetUsernameWithNicknameAsync(c, e.Guild).ConfigureAwait(false);
		var logMsg = msg.Content;
		if (msg.Attachments.Any())
			logMsg += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, msg.Attachments.Select(a => $"📎 {a.FileName}"));
		Config.Log.Info($"Deleted message from {usernameWithNickname} ({msg.JumpLink}):{Environment.NewLine}{logMsg.TrimStart()}");

		var logChannel = await c.GetChannelAsync(Config.DeletedMessagesLogChannelId).ConfigureAwait(false);
		var embed = new DiscordEmbedBuilder()
			.WithAuthor($"{msg.Author.Username}#{msg.Author.Discriminator} in #{msg.Channel?.Name ?? "DM"}", iconUrl: msg.Author.AvatarUrl)
			.WithDescription(msg.JumpLink.ToString())
			.WithFooter($"Post date: {msg.Timestamp:yyyy-MM-dd HH:mm:ss} ({(DateTime.UtcNow - msg.Timestamp).AsTimeDeltaDescription()} ago)");
		if (msg.Attachments.Count > 0)
			embed.AddField("Deleted Attachments", string.Join('\n', msg.Attachments.Select(a => $"📎 {a.FileName}")));
		var color = await ThumbnailProvider.GetImageColorAsync(msg.Author.AvatarUrl).ConfigureAwait(false);
		if (color.HasValue)
			embed.WithColor(color.Value);
		await PostLock.WaitAsync().ConfigureAwait(false);
		try
		{
			await logChannel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed.Build())).ConfigureAwait(false);
			if (!string.IsNullOrEmpty(msg.Content))
				await logChannel.SendMessageAsync(new DiscordMessageBuilder().WithContent(
					msg.Content
						.Replace(".", $"{StringUtils.InvisibleSpacer}.")
						.Trim(EmbedPager.MaxMessageLength)
				)).ConfigureAwait(false);
		}
		finally
		{
			PostLock.Release();
		}
	}
}