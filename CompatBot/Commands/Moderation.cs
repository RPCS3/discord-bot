using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed partial class Moderation: BaseCommandModuleCustom
    {
        [Command("report"), RequiresWhitelistedRole]
        [Description("Adds specified message to the moderation queue")]
        public async Task Report(CommandContext ctx, [Description("Message ID from current channel to report")] ulong messageId, [RemainingText, Description("Optional report comment")] string? comment = null)
        {
            try
            {
                var msg = await ctx.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
                await ReportMessage(ctx, comment, msg);
            }
            catch (Exception)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to report the message").ConfigureAwait(false);
            }
        }

        [Command("report"), RequiresWhitelistedRole]
        [Description("Adds specified message to the moderation queue")]
        public async Task Report(CommandContext ctx, [Description("Message link to report")] string messageLink, [RemainingText, Description("Optional report comment")] string? comment = null)
        {
            try
            {
                var msg = await ctx.GetMessageAsync(messageLink).ConfigureAwait(false);
                if (msg is null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Can't find linked message").ConfigureAwait(false);
                    return;
                }
                
                await ReportMessage(ctx, comment, msg);
            }
            catch (Exception)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to report the message").ConfigureAwait(false);
            }
        }

        [Command("analyze"), Aliases("reanalyze", "parse", "a")]
        [Description("Make bot to look at the attached log again")]
        public async Task Reanalyze(CommandContext ctx, [Description("Message ID from the same channel")]ulong messageId)
        {
            try
            {
                var msg = await ctx.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
                if (msg == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                else
                    LogParsingHandler.EnqueueLogProcessing(ctx.Client, ctx.Channel, msg, ctx.Member, true, true);
            }
            catch
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
            }
        }

        [Command("analyze")]
        public async Task Reanalyze(CommandContext ctx, [Description("Full message link")] string messageLink)
        {
            try
            {
                var msg = await ctx.GetMessageAsync(messageLink).ConfigureAwait(false);
                if (msg == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                else
                    LogParsingHandler.EnqueueLogProcessing(ctx.Client, ctx.Channel, msg, ctx.Member, true, true);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
            }
        }

        [Command("badupdate"), Aliases("bad", "recall"), RequiresBotModRole]
        [Description("Toggles new update announcement as being bad")]
        public async Task BadUpdate(CommandContext ctx, [Description("Link to the update announcement")] string updateMessageLink)
        {
            var msg = await ctx.GetMessageAsync(updateMessageLink).ConfigureAwait(false);
            var embed = msg?.Embeds?.FirstOrDefault();
            if (embed == null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid update announcement link").ConfigureAwait(false);
                return;
            }

            await ToggleBadUpdateAnnouncementAsync(msg).ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        }

        public static async Task ToggleBadUpdateAnnouncementAsync(DiscordMessage? message)
        {
            var embed = message?.Embeds?.FirstOrDefault();
            if (message is null || embed is null)
                return;

            var result = new DiscordEmbedBuilder(embed);
            const string warningTitle = "Warning!";
            if (embed.Color.Value.Value == Config.Colors.UpdateStatusGood.Value)
            {
                result = result.WithColor(Config.Colors.UpdateStatusBad);
                result.ClearFields();
                var warned = false;
                foreach (var f in embed.Fields)
                {
                    if (!warned && f.Name.EndsWith("download"))
                    {
                        result.AddField(warningTitle, "This build is known to have severe problems, please avoid downloading.");
                        warned = true;
                    }
                    result.AddField(f.Name, f.Value, f.Inline);
                }
            }
            else if (embed.Color.Value.Value == Config.Colors.UpdateStatusBad.Value)
            {
                result = result.WithColor(Config.Colors.UpdateStatusGood);
                result.ClearFields();
                foreach (var f in embed.Fields)
                {
                    if (f.Name == warningTitle)
                        continue;

                    result.AddField(f.Name, f.Value, f.Inline);
                }
            }
            await message.UpdateOrCreateMessageAsync(message.Channel, embed: result).ConfigureAwait(false);
        }

        private static async Task ReportMessage(CommandContext ctx, string? comment, DiscordMessage msg)
        {
            if (msg.Reactions.Any(r => r.IsMe && r.Emoji == Config.Reactions.Moderated))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Already reported").ConfigureAwait(false);
                return;
            }

            await ctx.Client.ReportAsync("👀 Message report", msg, new[] {ctx.Client.GetMember(ctx.Message.Author)}, comment, ReportSeverity.Medium).ConfigureAwait(false);
            await msg.ReactWithAsync(Config.Reactions.Moderated).ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success, "Message reported").ConfigureAwait(false);
        }
    }
}
