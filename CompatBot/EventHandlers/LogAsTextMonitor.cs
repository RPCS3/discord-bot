﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers;

internal static partial class LogAsTextMonitor
{
    [GeneratedRegex(@"^[`""]?(·|(\w|!)) ({(rsx|PPU|SPU)|LDR:)|E LDR:", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LogLine();

    public static async Task OnMessageCreated(DiscordClient _, MessageCreateEventArgs args)
    {
        if (DefaultHandlerFilter.IsFluff(args.Message))
            return;

        if (!LimitedToHelpChannel.IsHelpChannel(args.Channel))
            return;

        if ((args.Message.Author as DiscordMember)?.Roles.Any() ?? false)
            return;

        if (LogLine().IsMatch(args.Message.Content))
        {
            var brokenDump = false;
            string msg = "";
            if (args.Message.Content.Contains("LDR:"))
            {
                brokenDump = true;
                if (args.Message.Content.Contains("fs::file is null"))
                    msg = $"{args.Message.Author.Mention} this error usually indicates a missing `.rap` license file.\n";
                else if (args.Message.Content.Contains("Invalid or unsupported file format"))
                    msg = $"{args.Message.Author.Mention} this error usually indicates an encrypted or corrupted game dump.\n";
                else
                    brokenDump = false;
            }
            var logUploadExplain = await PostLogHelpHandler.GetExplanationAsync("log").ConfigureAwait(false);
            if (brokenDump)
                msg += "Please follow the quickstart guide to get a proper dump of a digital title.\n" +
                       "Also please upload the full RPCS3 log instead of pasting only a section which may be completely irrelevant.\n" +
                       logUploadExplain.Text;
            else
                msg = $"{args.Message.Author.Mention} please upload the full RPCS3 log instead of pasting only a section which may be completely irrelevant." +
                      logUploadExplain.Text;
            await args.Channel.SendMessageAsync(msg, logUploadExplain.Attachment, logUploadExplain.AttachmentFilename).ConfigureAwait(false);
        }
    }
}