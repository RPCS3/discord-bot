﻿using System;

namespace CompatBot.Utils
{
    public static class DateTimeEx
    {
        public static DateTime AsUtc(this DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
                return dateTime;

            return new DateTime(dateTime.Ticks, DateTimeKind.Utc);
        }

        public static DateTime AsUtc(this long ticks)
        {
            return new DateTime(ticks, DateTimeKind.Utc);
        }
    }
}
