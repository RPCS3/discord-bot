using System;

namespace GithubClient.POCOs
{
    public class PrInfo
    {
        public string HtmlUrl;
        public int Number;
        public string State;
        public string Title;
        public GithubUser User;
        public DateTime CreatedAt;
        public DateTime? UpdatedAt;
        public DateTime? ClosedAt;
        public DateTime? MergedAt;
        public string MergeCommitSha;
        public string StatusesUrl;
        public int Additions;
        public int Deletions;
        public int ChangedFiles;
        public string Message;
    }

    public class GithubUser
    {
        public string Login;
    }
}
