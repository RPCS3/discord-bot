using CompatApiClient.POCOs;

namespace CompatBot.Utils
{
    public static class CompatResultExtensions
    {
        public static CompatResult Append(this CompatResult remote, CompatResult local)
        {
            if (remote.Results?.Count > 0)
            {
                foreach (var localItem in local.Results)
                {
                    if (remote.Results.ContainsKey(localItem.Key))
                        continue;

                    remote.Results[localItem.Key] = localItem.Value;
                }
                return remote;
            }
            return local;
        }
    }
}