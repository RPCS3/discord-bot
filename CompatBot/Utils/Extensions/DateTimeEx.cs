namespace CompatBot.Utils;

public static class DateTimeEx
{
    public static DateTime AsUtc(this DateTime dateTime) => dateTime.Kind == DateTimeKind.Utc ? dateTime : new(dateTime.Ticks, DateTimeKind.Utc);
    public static DateTime AsUtc(this long ticks) => new(ticks, DateTimeKind.Utc);

    public static string AsShortTimespan(this TimeSpan timeSpan)
    {
        var totalMinutesInt = (int)timeSpan.TotalMinutes;
        var totalHoursInt = (int)timeSpan.TotalHours;
        var totalDays = timeSpan.TotalDays;
        var totalDaysInt = (int)totalDays;
        var totalWeeksInt = (int)(totalDays / 7);
        var totalMonthsInt = (int)(totalDays / 30);
        var totalYearsInt = (int)(totalDays / 365.25);

        var years = totalYearsInt;
        var months = totalMonthsInt - years * 12;
        var weeks = totalWeeksInt - years * 52 - months * 4;
        var days = totalDaysInt - totalWeeksInt * 7;
        var hours = totalHoursInt - totalDaysInt * 24;
        var minutes = totalMinutesInt - totalHoursInt * 60;

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
        if (result is not { Length: >0})
            result = (int)timeSpan.TotalSeconds + "s";
        if (result is not { Length: > 0 })
            result = (int)timeSpan.TotalMilliseconds + "ms";
        return result.TrimEnd();
    }
}