using System;

namespace CompatApiClient.Utils
{
    public static class Utils
    {
        private const long UnderKB = 1000;
        private const long UnderMB = 1000 * 1024;
        private const long UnderGB = 1000 * 1024 * 1024;

        public static string Trim(this string str, int maxLength)
        {
            const int minSaneLimit = 4;

            if (maxLength < minSaneLimit)
                throw new ArgumentException("Argument cannot be less than " + minSaneLimit, nameof(maxLength));

            if (string.IsNullOrEmpty(str))
                return str;

            if (str.Length > maxLength)
                return str.Substring(0, maxLength - 3) + "...";

            return str;
        }

        public static string Truncate(this string str, int maxLength)
        {
            if (maxLength < 1)
                throw new ArgumentException("Argument must be positive, but was " + maxLength, nameof(maxLength));

            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;

            return str.Substring(0, maxLength);
        }

        public static string Sanitize(this string str, bool breakLinks = true)
        {
            var result = str?.Replace("`", "`\u200d").Replace("@", "@\u200d");
            if (breakLinks)
                result = result.Replace(".", ".\u200d").Replace(":", ":\u200d");
            return result;
        }

        public static int Clamp(this int amount, int low, int high)
        {
            return Math.Min(high, Math.Max(amount, low));
        }

        public static string AsStorageUnit(this long bytes)
        {
            if (bytes < UnderKB)
                return $"{bytes} byte{(bytes == 1 ? "" : "s")}";
            if (bytes < UnderMB)
                return $"{bytes / 1024.0:0.##} KB";
            if (bytes < UnderGB)
                return $"{bytes / 1024.0 / 1024:0.##} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
        }
    }
}
