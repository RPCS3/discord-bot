using System.Text.RegularExpressions;
using CompatApiClient.Utils;
using CompatBot.Commands;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

internal static partial class TableFlipMonitor
{
    [GeneratedRegex(@"(🎲|\s)+")]
    private static partial Regex DiceRoll();
    private static readonly char[] OpenParen = ['(', '（', 'ʕ'];

    public static async Task OnMessageCreated(DiscordClient _, MessageCreatedEventArgs args)
    {
        if (DefaultHandlerFilter.IsFluff(args.Message))
            return;

        if (!args.Channel.IsSpamChannel() && !args.Channel.IsOfftopicChannel())
            return;

        /*
         * (╯°□°）╯︵ ┻━┻
         * (ノ ゜Д゜)ノ ︵ ┻━┻
         * (ノಠ益ಠ)ノ彡┻━┻
         * ‎(ﾉಥ益ಥ）ﾉ ┻━┻
         * (ﾉಥДಥ)ﾉ︵┻━┻･/
         * (ノ^_^)ノ┻━┻
         * (/¯◡ ‿ ◡)/¯ ~ ┻━┻
         *
         * this might look the same, but only because of the font choice
         *
         * ┻━┻
         * ┻━┻
         */
        try
        {
            var content = args.Message.Content;
            if (content.Contains("🎲") && DiceRoll().IsMatch(content))
            {
                var count = 1;
                var idx = content.IndexOf("🎲");
                while (idx < content.Length && (idx = content.IndexOf("🎲", idx + 1)) > 0)
                    count++;
                EmpathySimulationHandler.Throttling.Set(args.Channel.Id, new List<DiscordMessage> {args.Message}, EmpathySimulationHandler.ThrottleDuration);
                await args.Message.RespondAsync(Misc.Roll($"{count}d6")).ConfigureAwait(false);
                return;
            }
                
            if (content.Trim() == "🥠")
            {
                EmpathySimulationHandler.Throttling.Set(args.Channel.Id, new List<DiscordMessage> {args.Message}, EmpathySimulationHandler.ThrottleDuration);
                if (await Fortune.GetFortuneAsync(args.Author).ConfigureAwait(false) is {Length: >0} fortune)
                    await args.Message.RespondAsync(fortune).ConfigureAwait(false);
                return;
            }

            if (!(content.Contains("┻━┻") ||
                  content.Contains("┻━┻")))
                return;

            var tableIdx = content.IndexOf("┻━┻", StringComparison.Ordinal);
            if (tableIdx < 0)
                tableIdx = content.IndexOf("┻━┻", StringComparison.Ordinal);
            var faceIdx = content[..tableIdx].LastIndexOfAny(OpenParen);
            var face = content[faceIdx..tableIdx];
            if (face.Length > 30)
                return;

            var reverseFace = face
                .Replace("(╯", "╯(").Replace("(ﾉ", "ﾉ(").Replace("(ノ", "ノ(").Replace("(/¯", @"\_/(")
                .Replace(")╯", "╯)").Replace(")ﾉ", "ﾉ)").Replace(")ノ", "ノ)").Replace(")/¯", @"\_/)")

                .Replace("（╯", "╯（").Replace("（ﾉ", "ﾉ（").Replace("（ノ", "ノ（").Replace("（/¯", @"\_/（")
                .Replace("）╯", "╯）").Replace("）ﾉ", "ﾉ）").Replace("）ノ", "ノ）").Replace("）/¯", @"\_/）")

                .Replace("ʕ╯", "╯ʕ").Replace("ʕﾉ", "ﾉʕ").Replace("ʕノ", "ノʕ").Replace("ʕ/¯", @"\_/ʕ")
                .Replace("ʔ╯", "╯ʔ").Replace("ʔﾉ", "ﾉʔ").Replace("ʔノ", "ノʔ").Replace("ʔ/¯", @"\_/ʔ")

                .TrimEnd('︵', '彡', ' ', '　', '~', '～');
            if (reverseFace == face)
                return;

            var faceLength = reverseFace.Length;
            if (faceLength > 5 + 4)
                reverseFace = $"{reverseFace[..2]}ಠ益ಠ{reverseFace[^2..]}";
            await args.Channel.SendMessageAsync("┬─┬﻿ " + reverseFace.Sanitize()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }
}