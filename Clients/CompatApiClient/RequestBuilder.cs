using System;
using System.Collections.Generic;
using CompatApiClient.Utils;

namespace CompatApiClient
{
    public class RequestBuilder
    {
        public string? Search { get; private set; } = "";
        public int AmountRequested { get; } = ApiConfig.ResultAmount[0];

        private RequestBuilder() {}

        public static RequestBuilder Start() => new();

        public RequestBuilder SetSearch(string search)
        {
            Search = search;
            return this;
        }

        public Uri Build(bool apiCall = true)
        {
            var parameters = new Dictionary<string, string?>
            {
                {"g", Search},
                {"r", AmountRequested.ToString()},
            };
            if (apiCall)
                parameters["api"] = "v" + ApiConfig.Version;
            return ApiConfig.BaseUrl.SetQueryParameters(parameters);
        }
    }
}