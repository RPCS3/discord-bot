using System;
using System.Collections.Generic;

namespace AppveyorClient.POCOs
{
    public class Build
    {
        public string AuthorName;
        public string AuthorUsername;
        public string Branch;
        public bool IsTag;
        public int BuildId;
        public int BuildNumber;
        public DateTime? Created;
        public DateTime? Started;
        public DateTime? Updated;
        public DateTime? Finished;
        public List<Job> Jobs;
        public string Message;
        public string CommitId;
        public string PullRequestHeadBranch;
        public string PullRequestHeadCommitId;
        public string PullRequestHeadRepository;
        public int PullRequestId;
        public string PullRequestName;
        public string Status;
        public string Version;
    }
}