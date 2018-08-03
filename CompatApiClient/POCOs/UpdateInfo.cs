namespace CompatApiClient.POCOs
{
    public class UpdateInfo
    {
        public int ReturnCode;
        public BuildInfo LatestBuild;
    }

    public class BuildInfo
    {
        public string Pr;
        public BuildLink Windows;
        public BuildLink Linux;
    }

    public class BuildLink
    {
        public string Datetime;
        public string Download;
    }
}