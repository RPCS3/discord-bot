using System.Collections.Specialized;

namespace CompatBot.EventHandlers.LogParsing.POCOs
{
    internal class LogParseState
    {
        public NameValueCollection CompleteCollection = null;
        public NameValueCollection WipCollection = new NameValueCollection();
        public int Id = 0;
        public ErrorCode Error = ErrorCode.None;
        public string PiracyTrigger;
        public string PiracyContext;
        public long ReadBytes;
        public long TotalBytes;

        public enum ErrorCode
        {
            None = 0,
            PiracyDetected = 1,
            SizeLimit = 2,
        }
    }
}