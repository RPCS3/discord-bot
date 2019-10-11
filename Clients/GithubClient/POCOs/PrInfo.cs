using Octokit;
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

        public static implicit operator PrInfo(PullRequest pr)
        {
            if (pr == null)
                return null;

            return new PrInfo
            {
                HtmlUrl = pr.HtmlUrl,
                Number = pr.Number,
                State = pr.State.StringValue,
                Title = pr.Title,
                User = pr.User,
                CreatedAt = pr.CreatedAt.UtcDateTime,
                UpdatedAt = pr.UpdatedAt.UtcDateTime,
                ClosedAt = pr.ClosedAt?.UtcDateTime,
                MergedAt = pr.MergedAt?.UtcDateTime,
                MergeCommitSha = pr.MergeCommitSha,
                StatusesUrl = pr.StatusesUrl,
                Additions = pr.Additions,
                Deletions = pr.Deletions,
                ChangedFiles = pr.ChangedFiles,
                Message = pr.Body
            };
        }
    }

    public class IssueInfo
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
        public string Body;
        public PullRequestReference PullRequest;

        public static implicit operator IssueInfo(Issue issue)
        {
            if (issue == null)
                return null;

            return new IssueInfo
            {
                HtmlUrl = issue.HtmlUrl,
                Number = issue.Number,
                State = issue.State.StringValue,
                Title = issue.Title,
                User = issue.User,
                CreatedAt = issue.CreatedAt.UtcDateTime,
                UpdatedAt = issue.UpdatedAt?.UtcDateTime,
                ClosedAt = issue.ClosedAt?.UtcDateTime,
                MergedAt = null,
                Body = issue.Body,
                PullRequest = issue.PullRequest
            };
        }
    }

    public class GithubUser
    {
        public string Login;

        public static implicit operator GithubUser(User user)
        {
            if (user == null)
                return null;

            return new GithubUser
            {
                Login = user.Login
            };
        }
    }

    public class PullRequestReference
    {
        public string Url;
        public string HtmlUrl;
        public string DiffUrl;
        public string PatchUrl;

        public static implicit operator PullRequestReference(PullRequest pr)
        {
            if (pr == null)
                return null;

            return new PullRequestReference
            {
                Url = pr.Url,
                HtmlUrl = pr.HtmlUrl,
                DiffUrl = pr.DiffUrl,
                PatchUrl = pr.PatchUrl
            };
        }
    }
}
