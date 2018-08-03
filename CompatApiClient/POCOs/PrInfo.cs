﻿namespace CompatApiClient.POCOs
{
    public class PrInfo
    {
        public int Number;
        public string Title;
        public GithubUser User;
        public int Additions;
        public int Deletions;
        public int ChangedFiles;
    }

    public class GithubUser
    {
        public string Login;
    }
}
