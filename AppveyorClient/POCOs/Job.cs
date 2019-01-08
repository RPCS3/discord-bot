using System;

namespace AppveyorClient.POCOs
{
    public class Job
    {
        public int ArtifactsCount;
        public int CompilationErrorsCount;
        public DateTime? Created;
        public DateTime? Started;
        public DateTime? Updated;
        public DateTime? Finished;
        public string OsType;
        public string Status;
        public string JobId;
    }
}