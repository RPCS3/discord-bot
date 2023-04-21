using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

internal static class DeletedMessagesMonitor
{
	public static readonly MemoryCache RemovedByBotCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(10) });
	public static readonly TimeSpan CacheRetainTime = TimeSpan.FromMinutes(1);
	private static readonly SemaphoreSlim PostLock = new(1);

	public static async Task OnMessageDeleted(DiscordClient c, MessageDeleteEventArgs e)
	{
		if (e.Channel.IsPrivate)
			return;

		var msg = e.Message;
		if (msg?.Author == null)
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
		if (logChannel == null)
			return;

		var (attachmentContent, attachmentFilenames) = await msg.DownloadAttachmentsAsync().ConfigureAwait(false);
		try
		{
			var embed = new DiscordEmbedBuilder()
				.WithAuthor($"{msg.Author.Username}#{msg.Author.Discriminator} in #{msg.Channel.Name}", iconUrl: msg.Author.AvatarUrl)
				.WithDescription(msg.JumpLink.ToString())
				.WithFooter($"Post date: {msg.Timestamp:yyyy-MM-dd HH:mm:ss} ({(DateTime.UtcNow - msg.Timestamp).AsTimeDeltaDescription()} ago)");
			if (attachmentFilenames?.Count > 0)
				embed.AddField("Deleted Attachments", string.Join('\n', msg.Attachments.Select(a => $"📎 {a.FileName}")));
			var color = await ThumbnailProvider.GetImageColorAsync(msg.Author.AvatarUrl).ConfigureAwait(false);
			if (color.HasValue)
				embed.WithColor(color.Value);
			await PostLock.WaitAsync().ConfigureAwait(false);
			try
			{
				await logChannel.SendMessageAsync(new DiscordMessageBuilder().WithEmbed(embed.Build()).WithAllowedMentions(Config.AllowedMentions.Nothing)).ConfigureAwait(false);
				if (attachmentContent?.Count > 0)
					await logChannel.SendMessageAsync(new DiscordMessageBuilder().AddFiles(attachmentContent).WithContent(msg.Content).WithAllowedMentions(Config.AllowedMentions.Nothing)).ConfigureAwait(false);
				else if (!string.IsNullOrEmpty(msg.Content))
					await logChannel.SendMessageAsync(new DiscordMessageBuilder().WithContent(msg.Content).WithAllowedMentions(Config.AllowedMentions.Nothing)).ConfigureAwait(false);
			}
			finally
			{
				PostLock.Release();
			}
		}
		finally
		{
			if (attachmentContent?.Count > 0)
				foreach (var f in attachmentContent.Values)
					await f.DisposeAsync();
		}
	}
}