namespace PsnClient.Utils
{
    public static class LocaleUtils
    {
        public static (string language, string country) AsLocaleData(this string locale)
        {
            /*
                  "zh-Hans-CN" -> zh-CN
                  "zh-Hans-HK" -> zh-HK
                  "zh-Hant-HK" -> ch-HK
                  "zh-Hant-TW" -> ch-TW
             */
            locale = locale.Replace("zh-Hans", "zh").Replace("zh-Hant", "ch");
            var localeParts = locale.Split('-');
            return (localeParts[0], localeParts[1]);
        }
    }
}
