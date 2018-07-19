using System;
using System.Collections.Generic;
using System.Globalization;

namespace CompatApiClient
{
    public class RequestBuilder
    {
        public string customHeader { get; private set; }
        public string search { get; private set; }
        private int? status;
        private string start;
        private string sort;
        private string date;
        public char? releaseType { get; private set; }
        public char? region { get; private set; }
        private int amount = ApiConfig.ResultAmount[0];
        public int amountRequested { get; private set; } = ApiConfig.ResultAmount[0];

        private RequestBuilder()
        {
        }

        public static RequestBuilder Start()
        {
            return new RequestBuilder();
        }

        public RequestBuilder SetSearch(string search)
        {
            this.search = search;
            return this;
        }

        public RequestBuilder SetHeader(string header)
        {
            this.customHeader = header;
            return this;
        }

        public RequestBuilder SetStatus(string status)
        {
            if (ApiConfig.Statuses.TryGetValue(status, out var statusCode))
                this.status = statusCode;
            return this;
        }

        public RequestBuilder SetStartsWith(string prefix)
        {
            if (prefix == "num" || prefix == "09")
                start = "09";
            else if (prefix == "sym" || prefix == "#")
                start = "sym";
            else if (prefix?.Length == 1)
                start = prefix;
            return this;
        }

        public RequestBuilder SetSort(string type, string direction)
        {
            if (ApiConfig.SortTypes.TryGetValue(type, out var sortType) && ApiConfig.ReverseDirections.TryGetValue(direction, out var dir))
                sort = sortType.ToString() + dir;
            return this;
        }

        public RequestBuilder SetDate(string date)
        {
            if (DateTime.TryParseExact(date, ApiConfig.DateInputFormat, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out var parsedDate))
                this.date = parsedDate.ToString(ApiConfig.DateQueryFormat);
            return this;
        }

        public RequestBuilder SetReleaseType(string type)
        {
            if (ApiConfig.ReverseReleaseTypes.TryGetValue(type, out var releaseType))
                this.releaseType = releaseType;
            return this;
        }

        public RequestBuilder SetRegion(string region)
        {
            if (ApiConfig.ReverseRegions.TryGetValue(region, out var regionCode))
                this.region = regionCode;
            return this;
        }

        public RequestBuilder SetAmount(int amount)
        {
            if (amount < 1)
                return this;

            foreach (var bracket in ApiConfig.ResultAmount)
            {
                if (amount <= bracket)
                {
                    this.amount = bracket;
                    this.amountRequested = amount;
                    return this;
                }
            }
            return this;
        }

        public Uri Build(bool apiCall = true)
        {
            var parameters = new Dictionary<string, string>
            {
                {"g", search},
                {"s", status?.ToString()},
                {"c", start},
                {"o", sort},
                {"d", date},
                {"t", releaseType?.ToString()},
                {"f", region?.ToString()},
                {"r", amount.ToString()},
            };
            if (apiCall)
                parameters["api"] = "v" + ApiConfig.Version;
            return ApiConfig.BaseUrl.SetQueryParameters(parameters);
        }
    }
}