using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Utils.Extensions;

public static class StackTraceExtensions
{
    public static string GetCaller<T>(this StackTrace trace) where T: DbContext
    {
        var st = trace.ToString();
        var lines = st.Split(Environment.NewLine);
        var openMethodName = typeof(T).Namespace + "." + typeof(T).Name + ".Open";
        try
        {
            var (idx, openLine) = lines.Index().LastOrDefault(i => i.Item.Contains(openMethodName));
            if (openLine is not null)
                return lines[idx + 1].TrimStart()[3..];
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to get the caller from stacktrace");
        }
        return st;
    }
}