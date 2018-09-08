using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CompatApiClient.POCOs
{
    public class CompatResult
    {
        [JsonProperty(PropertyName = "return_code")]
        public int ReturnCode;
        [JsonProperty(PropertyName = "search_term")]
        public string SearchTerm;
        public Dictionary<string, TitleInfo> Results;

        [JsonIgnore]
        public TimeSpan RequestDuration;
        [JsonIgnore]
        public RequestBuilder RequestBuilder;
    }

    public class TitleInfo
    {
        public static readonly TitleInfo Maintenance = new TitleInfo { Status = "Maintenance" };
        public static readonly TitleInfo CommunicationError = new TitleInfo { Status = "Error" };
        public static readonly TitleInfo Unknown = new TitleInfo { Status = "Unknown" };

        public string Title;
        public string AlternativeTitle;
        public string WikiTitle;
        public string Status;
        public string Date;
        public int Thread;
        public string Commit;
        public int? Pr;
    }
}