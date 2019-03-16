using CompatBot.Database;

namespace CompatBot.Utils
{
    internal static class BotDbExtensions
    {
        public static bool IsComplete(this EventSchedule evt)
        {
            return evt.Start > 0
                   && evt.End > evt.Start
                   && evt.Year > 0
                   && !string.IsNullOrEmpty(evt.Name);
        }
    }
}
