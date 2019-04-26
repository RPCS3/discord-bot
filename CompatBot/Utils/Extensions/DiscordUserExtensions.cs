using System;
using System.Collections.Generic;
using DSharpPlus.Entities;

namespace CompatBot.Utils.Extensions
{
    public static class DiscordUserExtensions
    {
        public static bool IsBotSafeCheck(this DiscordUser user)
        {
            try
            {
                return user?.IsBot ?? false;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                return false;
            }
        }
    }
}
