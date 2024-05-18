﻿using System;
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

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed partial class GoogleDriveHandler: BaseSourceHandler
{
    [GeneratedRegex(@"(?<gdrive_link>(https?://)?drive\.google\.com/(open\?id=|file/d/)(?<gdrive_id>[^/>\s]+))", DefaultOptions)]
    private static partial Regex ExternalLink();
    private static readonly string[] Scopes = [DriveService.Scope.DriveReadonly];
    private static readonly string ApplicationName = "RPCS3 Compatibility Bot 2.0";

    public override async Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
    {
        if (string.IsNullOrEmpty(message.Content))
            return (null, null);

        if (string.IsNullOrEmpty(Config.GoogleApiCredentials))
            return (null, null);

        var matches = ExternalLink().Matches(message.Content);
        if (matches.Count == 0)
            return (null, null);

        var client = GetClient();
        foreach (Match m in matches)
        {
            try
            {
                if (m.Groups["gdrive_id"].Value is not { Length: > 0 } fid)
                    continue;
                
                var fileInfoRequest = client.Files.Get(fid);
                fileInfoRequest.Fields = "name, size, kind";
                var fileMeta = await fileInfoRequest.ExecuteAsync(Config.Cts.Token).ConfigureAwait(false);
                if (fileMeta is not { Kind: "drive#file", Size: > 0 })
                    continue;
                
                var buf = BufferPool.Rent(SnoopBufferSize);
                try
                {
                    await using var stream = new MemoryStream(buf, true);
                    var limit = Math.Min(SnoopBufferSize, fileMeta.Size.Value) - 1;
                    var progress = await fileInfoRequest.DownloadRangeAsync(stream, new RangeHeaderValue(0, limit), Config.Cts.Token).ConfigureAwait(false);
                    if (progress.Status != DownloadStatus.Completed)
                        continue;

                    var read = (int)progress.BytesDownloaded;
                    foreach (var handler in handlers)
                    {
                        var (canHandle, reason) = handler.CanHandle(fileMeta.Name, (int)fileMeta.Size, buf.AsSpan(0, read));
                        if (canHandle)
                            return (new GoogleDriveSource(client, fileInfoRequest, fileMeta, handler), null);
                        else if (!string.IsNullOrEmpty(reason))
                            return(null, reason);
                    }
                }
                finally
                {
                    BufferPool.Return(buf);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Error sniffing {m.Groups["gdrive_link"].Value}");
            }
        }
        return (null, null);
    }

    private static DriveService GetClient(string? json = null)
    {
        var credential = GoogleCredential.FromJson(json ?? Config.GoogleApiCredentials).CreateScoped(Scopes);
        var service = new DriveService(new()
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
        return service;
    }

    internal static bool ValidateCredentials(string? json = null)
    {
        try
        {
            using var _ = GetClient(json);
            return true;
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            return false;
        }
    }

    private sealed class GoogleDriveSource : ISource
    {
        public string SourceType => "Google Drive";
        public string FileName => fileMeta.Name;
        public long SourceFileSize => fileMeta.Size ?? 0;
        public long SourceFilePosition => handler.SourcePosition;
        public long LogFileSize => handler.LogSize;

        private readonly DriveService driveService;
        private readonly FilesResource.GetRequest fileInfoRequest;
        private readonly FileMeta fileMeta;
        private readonly IArchiveHandler handler;

        public GoogleDriveSource(DriveService driveService, FilesResource.GetRequest fileInfoRequest, FileMeta fileMeta, IArchiveHandler handler)
        {
            this.driveService = driveService;
            this.fileInfoRequest = fileInfoRequest;
            this.fileMeta = fileMeta;
            this.handler = handler;
        }

        public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                var pipe = new Pipe();
                await using var pushStream = pipe.Writer.AsStream();
                var progressTask = fileInfoRequest.DownloadAsync(pushStream, cancellationToken);
                await using var pullStream = pipe.Reader.AsStream();
                var pipingTask = handler.FillPipeAsync(pullStream, writer, cancellationToken);
                var result = await progressTask.ConfigureAwait(false);
                if (result.Status != DownloadStatus.Completed)
                    Config.Log.Error(result.Exception, "Failed to download file from Google Drive: " + result.Status);
                await pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
                await pipingTask.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to download file from Google Drive");
            }
        }

        public void Dispose() => driveService.Dispose();
    }
}