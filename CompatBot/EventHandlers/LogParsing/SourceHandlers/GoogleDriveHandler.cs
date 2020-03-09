using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using FileMeta = Google.Apis.Drive.v3.Data.File;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class GoogleDriveHandler: BaseSourceHandler
    {
        private static readonly Regex ExternalLink = new Regex(@"(?<gdrive_link>(https?://)?drive\.google\.com/(open\?id=|file/d/)(?<gdrive_id>[^/>\s]+))", DefaultOptions);
        private static readonly string[] Scopes = { DriveService.Scope.DriveReadonly };
        private static readonly string ApplicationName = "RPCS3 Compatibility Bot 2.0";

        public override async Task<(ISource source, string failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            if (string.IsNullOrEmpty(message.Content))
                return (null, null);

            if (!File.Exists(Config.GoogleApiConfigPath))
                return (null, null);

            var matches = ExternalLink.Matches(message.Content);
            if (matches.Count == 0)
                return (null, null);

            var client = GetClient();
            foreach (Match m in matches)
            {
                try
                {
                    if (m.Groups["gdrive_id"].Value is string fid
                        && !string.IsNullOrEmpty(fid))
                    {
                        var fileInfoRequest = client.Files.Get(fid);
                        fileInfoRequest.Fields = "name, size, kind";
                        var fileMeta = await fileInfoRequest.ExecuteAsync(Config.Cts.Token).ConfigureAwait(false);
                        if (fileMeta.Kind == "drive#file")
                        {
                            var buf = bufferPool.Rent(1024);
                            try
                            {
                                int read;
                                using (var stream = new MemoryStream(buf, true))
                                {
                                    var limit = Math.Min(1024, (int)fileMeta.Size) - 1;
                                    var progress = await fileInfoRequest.DownloadRangeAsync(stream, new RangeHeaderValue(0, limit), Config.Cts.Token).ConfigureAwait(false);
                                    if (progress.Status != DownloadStatus.Completed)
                                        continue;

                                    read = (int)progress.BytesDownloaded;
                                }
                                foreach (var handler in handlers)
                                {
                                    var (canHandle, reason) = handler.CanHandle(fileMeta.Name, (int)fileMeta.Size, buf.AsSpan(0, read));
                                    if (canHandle)
                                        return (new GoogleDriveSource(fileInfoRequest, fileMeta, handler), null);
                                    else if (!string.IsNullOrEmpty(reason))
                                        return(null, reason);
                                }
                            }
                            finally
                            {
                                bufferPool.Return(buf);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Error sniffing {m.Groups["gdrive_link"].Value}");
                }
            }
            return (null, null);
        }

        private DriveService GetClient()
        {
            var credential = GoogleCredential.FromFile(Config.GoogleApiConfigPath).CreateScoped(Scopes);
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
            return service;
        }

        private sealed class GoogleDriveSource : ISource
        {
            public string SourceType => "Google Drive";
            public string FileName => fileMeta.Name;
            public long SourceFileSize => fileMeta.Size ?? 0;
            public long SourceFilePosition => handler.SourcePosition;
            public long LogFileSize => handler.LogSize;

            private FilesResource.GetRequest fileInfoRequest;
            private FileMeta fileMeta;
            private IArchiveHandler handler;

            public GoogleDriveSource(FilesResource.GetRequest fileInfoRequest, FileMeta fileMeta, IArchiveHandler handler)
            {
                this.fileInfoRequest = fileInfoRequest;
                this.fileMeta = fileMeta;
                this.handler = handler;
            }

            public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
            {
                try
                {
                    var pipe = new Pipe();
                    using var pushStream = pipe.Writer.AsStream();
                    var progressTask = fileInfoRequest.DownloadAsync(pushStream, cancellationToken);
                    using var pullStream = pipe.Reader.AsStream();
                    var pipingTask = handler.FillPipeAsync(pullStream, writer, cancellationToken);
                    var result = await progressTask.ConfigureAwait(false);
                    if (result.Status != DownloadStatus.Completed)
                        Config.Log.Error(result.Exception, "Failed to download file from Google Drive: " + result.Status);
                    await pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    pipe.Writer.Complete();
                    await pipingTask.ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Failed to download file from Google Drive");
                }
            }
        }
    }
}
