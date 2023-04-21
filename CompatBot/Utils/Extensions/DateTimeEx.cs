using System;

namespace CompatBot.Utils;

public static class DateTimeEx
{
    public static DateTime AsUtc(this DateTime dateTime) => dateTime.Kind == DateTimeKind.Utc ? dateTime : new(dateTime.Ticks, DateTimeKind.Utc);
    public static DateTime AsUtc(this long ticks) => new(ticks, DateTimeKind.Utc);

    public static string AsShortTimespan(this TimeSpan timeSpan)
    {
        var totalSeconds = timeSpan.TotalSeconds;
        var totalSecondsInt = (int)totalSeconds;
        var totalMinutes = totalSeconds / 60;
        var totalMinutesInt = (int)totalMinutes;
        var totalHours = totalMinutes / 60;
        var totalHoursInt = (int)totalHours;
        var totalDays = totalHours / 24;
        var totalDaysInt = (int)totalDays;
        var totalWeeks = totalDays / 7;
        var totalWeeksInt = (int)totalWeeks;
        var totalMonths = totalDays / 30;
        var totalMonthsInt = (int)totalMonths;
        var totalYears = totalDays / 365.25;
        var totalYearsInt = (int)totalYears;

        var years = totalYearsInt;
        var months = totalMonthsInt - years * 12;
        var weeks = totalWeeksInt - years * 52 - months * 4;
        var days = totalDaysInt - totalWeeksInt * 7;
        var hours = totalHoursInt - totalDaysInt * 24;
        var minutes = totalMinutesInt - totalHoursInt * 60;
        var seconds = totalSecondsInt - totalMinutesInt * 60;

        var result = "";
        if (years > 0)
            result += years + "y ";
        if (months > 0)
            result += months + "m ";
        if (weeks > 0)
            result += weeks + "w ";
        if (days > 0)
            result += days + "d ";
        if (hours > 0)
            result += hours + "h ";
        if (minutes > 0)
            result += minutes + "m ";
        if (string.IsNullOrEmpty(result))
            result = seconds + "s";
        return result.TrimEnd();
    }

}