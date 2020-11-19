namespace MediafireClient.POCOs
{
    #nullable disable
    
    public sealed class LinksResult
    {
        public LinksResponse Response;
    }

    public sealed class LinksResponse
    {
        public string Action;
        public string Result;
        public string CurrentApiVersion;
        public Link[] Links;
    }

    public sealed class Link
    {
        public string Quickkey;
        public string NormalDownload;
        public string DirectDownload;
    }
    
    #nullable restore
}