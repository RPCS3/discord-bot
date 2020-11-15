using System;
using System.Diagnostics;

namespace GithubClient.POCOs
{
    [DebuggerDisplay("{Body}", Name = "#{Number}")]
    public sealed class PrInfo
    {
        public string? HtmlUrl;
        public int Number;
        public string? State;
        public string? Title;
        public GithubUser? User;
        public string? Body;
        public DateTime CreatedAt;
        public DateTime? UpdatedAt;
        public DateTime? ClosedAt;
        public DateTime? MergedAt;
        public string? MergeCommitSha;
        public string? StatusesUrl;
        public RefInfo? Head;
        public RefInfo? Base;
        public int Additions;
        public int Deletions;
        public int ChangedFiles;
        public string? Message;
    }

    public sealed class IssueInfo
    {
        public string? HtmlUrl;
        public int Number;
        public string? State;
        public string? Title;
        public GithubUser? User;
        public DateTime CreatedAt;
        public DateTime? UpdatedAt;
        public DateTime? ClosedAt;
        public DateTime? MergedAt;
        public string? Body;
        public PullRequestReference? PullRequest;
    }

    public sealed class GithubUser
    {
        public string? Login;
    }

    public sealed class PullRequestReference
    {
        public string? Url;
        public string? HtmlUrl;
        public string? DiffUrl;
        public string? PatchUrl;
    }

    public sealed class RefInfo
    {
        public string? Label;
        public string? Ref;
        public GithubUser? User;
        public string? Sha;
    }
}
