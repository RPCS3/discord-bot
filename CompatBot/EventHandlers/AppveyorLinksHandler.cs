using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal sealed class AppveyorLinksHandler
    {
        // https://ci.appveyor.com/project/rpcs3/rpcs3/build/0.0.4-7952/artifacts
        // https://ci.appveyor.com/project/rpcs3/rpcs3/build/0.0.5-952c5c92/artifacts
        // https://ci.appveyor.com/project/rpcs3/rpcs3/builds/21496243/artifacts
        // https://ci.appveyor.com/api/buildjobs/08a2gmwttqo1j86r/artifacts/rpcs3-v0.0.5-8362ab1e_win64.7z

        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
        private static readonly Regex BuildLinks = new Regex(@"https?://ci\.appveyor\.com/(project/rpcs3/rpcs3/build(s/(?<build_number>\d+)|/(?<build_id>[0-5\.]+-[^/ ]+))|api/buildjobs/(?<job_id>[^/ ]+))", DefaultOptions);
        private static readonly GithubClient.Client GithubClient = new GithubClient.Client();
        private static readonly AppveyorClient.Client AppveyorClient = new AppveyorClient.Client();

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
                return;

            var matches = BuildLinks.Matches(args.Message.Content);
            if (matches.Count == 0)
                return;

            await args.Message.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);

            try
            {
                var lookedUp = 0;
                foreach (Match match in matches)
                {
                    if (lookedUp > 0 && global::GithubClient.Client.RateLimitRemaining < Math.Max(20, global::GithubClient.Client.RateLimit / 2))
                    {
                        await args.Message.RespondAsync("Further lookups are rate limited by github API").ConfigureAwait(false);
                        break;
                    }

                    if (lookedUp > 4)
                        break;

                    int? pr = null;
                    if (match.Groups["build_id"].Value is string buildId && !string.IsNullOrEmpty(buildId))
                    {
                        var buildInfo = await AppveyorClient.GetBuildInfoAsync("https://ci.appveyor.com/api/projects/rpcs3/rpcs3/build/" + buildId, Config.Cts.Token).ConfigureAwait(false);
                        pr = buildInfo?.Build?.PullRequestId;
                    } else if (match.Groups["build_number"].Value is string buildNum && int.TryParse(buildNum, out var build))
                    {
                        var buildInfo = await AppveyorClient.GetBuildInfoAsync(build, Config.Cts.Token).ConfigureAwait(false);
                        pr = buildInfo?.Build?.PullRequestId;
                    }
                    else if (match.Groups["job_id"].Value is string jobId && !string.IsNullOrEmpty(jobId))
                    {
                        using (var timeoutCts = new CancellationTokenSource(Config.LogParsingTimeout))
                        using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(Config.Cts.Token, timeoutCts.Token))
                        {
                            var buildInfo = await AppveyorClient.GetBuildAsync(jobId, combinedCts.Token).ConfigureAwait(false);
                            pr = buildInfo?.PullRequestId;
                        }
                    }
                    if (pr > 0)
                        await Commands.Pr.LinkPrBuild(args.Client, args.Message, pr.Value).ConfigureAwait(false);
                }
            }
            finally
            {
                await args.Message.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }
    }
}
