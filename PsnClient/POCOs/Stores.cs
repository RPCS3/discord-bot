﻿using Newtonsoft.Json;

namespace PsnClient.POCOs
{
    // https://store.playstation.com/kamaji/api/valkyrie_storefront/00_09_000/user/stores
    // requires session
    public class Stores
    {
        public StoresHeader Header;
        public StoresData Data;
    }

    public class StoresHeader
    {
        public string Details;
        [JsonProperty(PropertyName = "errorUUID")]
        public string ErrorUuid;

        public string MessageKey; // "success"
        public string StatusCode; // "0x0000"
    }

    public class StoresData
    {
        public string BaseUrl;
        public string RootUrl;
        public string SearchUrl;
        public string TumblerUrl;
    }
}
