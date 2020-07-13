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

        public static string ToSaltedSha256(this DiscordUser user)
        {
            var data = BitConverter.GetBytes(user.Id);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            sha256.TransformBlock(Config.CryptoSalt, 0, Config.CryptoSalt.Length, null, 0);
            sha256.TransformFinalBlock(data, 0, data.Length);
            return sha256.Hash.ToHexString();
        }
    }
}
