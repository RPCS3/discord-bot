using System;

namespace GithubClient.POCOs
{
    public sealed class StatusInfo
    {
        public string? State; // success
        public string? Description;
        public string? TargetUrl;
        public string? Context; // continuous-integration/appveyor/pr
        public DateTime? CreatedAt;
        public DateTime? UpdatedAt;
    }
}