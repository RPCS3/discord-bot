using System;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Utils;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class TableFlipMonitor
    {
        private static readonly char[] OpenParen = {'(', '（', 'ʕ'};

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
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
                if (!(content.Contains("┻━┻") ||
                      content.Contains("┻━┻")))
                    return;

                var tableIdx = content.IndexOf("┻━┻");
                if (tableIdx < 0)
                    tableIdx = content.IndexOf("┻━┻");
                var faceIdx = content[..tableIdx].LastIndexOfAny(OpenParen);
                var face = content.Substring(faceIdx, tableIdx - faceIdx);
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
}
