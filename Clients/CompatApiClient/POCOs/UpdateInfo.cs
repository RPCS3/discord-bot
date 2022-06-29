namespace CompatApiClient.POCOs;
#nullable disable
    
public class UpdateInfo
{
    public int ReturnCode;
    public BuildInfo LatestBuild;
    public BuildInfo CurrentBuild;
}

public class BuildInfo
{
    public int? Pr;
    public string Datetime;
    public BuildLink Windows;
    public BuildLink Linux;
    public BuildLink Mac;
}

public class BuildLink
{
    public string Download;
}
    
#nullable restore