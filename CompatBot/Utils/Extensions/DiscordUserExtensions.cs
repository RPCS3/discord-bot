using System;
using System.Collections.Generic;
using DSharpPlus.Entities;
using PsnClient.Utils;

namespace CompatBot.Utils.Extensions
{
    public static class DiscordUserExtensions
    {
        public static bool IsBotSafeCheck(this DiscordUser user)
        {
            try
            {
                return user.IsBot;
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

        public static string ToSaltedSha256(this DiscordUser user)
            => BitConverter.GetBytes(user.Id).GetSaltedHash().ToHexString();
    }
}
