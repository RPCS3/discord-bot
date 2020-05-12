using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
	internal static class DeletedMessagesMonitor
	{
		public static async Task OnMessageDeleted(MessageDeleteEventArgs e)
		{
			if (e.Channel.IsPrivate)
				return;

			var msg = e.Message;
			if (msg?.Author == null)
				return;

			if (msg.Author.IsCurrent || msg.Author.IsBotSafeCheck())
				return;

			var usernameWithNickname = msg.Author.GetUsernameWithNickname(e.Client, e.Guild);
			var logMsg = msg.Content;
			if (msg.Attachments.Any())
				logMsg += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, msg.Attachments.Select(a => $"📎 {a.FileName}"));
			Config.Log.Info($"Deleted message from {usernameWithNickname} ({msg.JumpLink}):{Environment.NewLine}{logMsg.TrimStart()}");

			var logChannel = await e.Client.GetChannelAsync(Config.DeletedMessagesLogChannelId).ConfigureAwait(false);
			if (logChannel == null)
				return;

			var (attachmentContent, attachmentFilenames) = await msg.DownloadAttachmentsAsync().ConfigureAwait(false);
			try
			{
				var embed = new DiscordEmbedBuilder()
					.WithAuthor($"Deleted message from {msg.Author.Username}#{msg.Author.Discriminator} in #{msg.Channel.Name}", iconUrl: msg.Author.AvatarUrl)
					.WithDescription(msg.JumpLink.ToString())
					.WithFooter($"Post date: {msg.Timestamp:yyyy-MM-dd HH:mm:ss} ({(DateTime.UtcNow - msg.Timestamp).AsTimeDeltaDescription()} ago)");
				if (attachmentFilenames?.Count > 0)
					embed.AddField("Deleted Attachments", string.Join('\n', msg.Attachments.Select(a => $"📎 {a.FileName}")));
				if (attachmentContent?.Count > 0)
					await logChannel.SendMultipleFilesAsync(attachmentContent, content: msg.Content, embed: embed).ConfigureAwait(false);
				else
					await logChannel.SendMessageAsync(msg.Content, embed: embed).ConfigureAwait(false);
			}
			finally
			{
				if (attachmentContent?.Count > 0)
					foreach (var f in attachmentContent.Values)
						f.Dispose();
			}
		}
	}
}