using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;
using Google.Apis.Download;
using Google.Apis.Drive.v3;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class GoogleDriveHandler: BaseSourceHandler
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture;
        private static readonly Regex ExternalLink = new Regex(@"(?<gdrive_link>(https?://)?drive.google.com/open?id=(?<gdrive_id>[^/>\s]+))", DefaultOptions);

        public override async Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            if (string.IsNullOrEmpty(message.Content))
                return null;

            var matches = ExternalLink.Matches(message.Content);
            if (matches.Count == 0)
                return null;

            return null;
/*
            var driveClient = new DriveService();
            var downloader = new MediaDownloader(driveClient) { ChunkSize = 1024, Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1023) };
            var buf = new byte[1024];
            foreach (Match m in matches)
            {
                if (m.Groups["gdrive_id"].Value is string fid && !string.IsNullOrEmpty(fid))
                {
                    using (var metaStream = new MemoryStream())
                    {
                        var status = await driveClient.Files.Get(fid).DownloadAsync(metaStream, Config.Cts.Token);
                        if (status.Status == DownloadStatus.Completed)
                        {

                        }
                    }
                }
            }
*/
        }
    }
}
