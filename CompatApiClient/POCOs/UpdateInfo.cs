namespace CompatApiClient.POCOs
{
    public class UpdateInfo
    {
        public int ReturnCode;
        public BuildInfo LatestBuild;
        public BuildInfo CurrentBuild;
    }

    public class BuildInfo
    {
        public string Pr;
        public string Datetime;
        public BuildLink Windows;
        public BuildLink Linux;
    }

    public class BuildLink
    {
        public string Download;
    }
}