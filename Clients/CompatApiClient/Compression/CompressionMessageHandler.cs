using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CompatApiClient.Compression;

public class CompressionMessageHandler : DelegatingHandler
{
    public ICollection<ICompressor> Compressors { get; }
    public static readonly string PostCompressionFlag = "X-Set-Content-Encoding";
    public static readonly string[] DefaultContentEncodings = ["gzip", "deflate"];
    public static readonly string DefaultAcceptEncodings = "gzip, deflate";

    private readonly bool isServer;
    private readonly bool isClient;

    public CompressionMessageHandler(bool isServer = false)
    {
        this.isServer = isServer;
        isClient = !isServer;
        Compressors = [new GZipCompressor(), new DeflateCompressor()];
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (isServer
            && request.Content?.Headers.ContentEncoding.FirstOrDefault() is string serverEncoding
            && Compressors.FirstOrDefault(c => c.EncodingType.Equals(serverEncoding, StringComparison.OrdinalIgnoreCase)) is ICompressor serverDecompressor)
        {
            request.Content = new DecompressedContent(request.Content, serverDecompressor);
        }
        else if (isClient
                 && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
                 && request.Content != null
                 && request.Headers.TryGetValues(PostCompressionFlag, out var compressionFlagValues)
                 && compressionFlagValues.FirstOrDefault() is string compressionFlag
                 && Compressors.FirstOrDefault(c => c.EncodingType.Equals(compressionFlag, StringComparison.OrdinalIgnoreCase)) is ICompressor clientCompressor)
        {
            request.Content = new CompressedContent(request.Content, clientCompressor);
        }
        request.Headers.Remove(PostCompressionFlag);
        //ApiConfig.Log.Trace($"{request.Method} {request.RequestUri}");
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        //ApiConfig.Log.Trace($"Response: {response.StatusCode} {request.RequestUri}");
        if (isClient
            && response.Content.Headers.ContentEncoding.FirstOrDefault() is string clientEncoding
            && Compressors.FirstOrDefault(c => c.EncodingType.Equals(clientEncoding, StringComparison.OrdinalIgnoreCase)) is ICompressor clientDecompressor)
        {
            response.Content = new DecompressedContent(response.Content, clientDecompressor);
        }
        else if (isServer
                 && request.Headers.AcceptEncoding.FirstOrDefault() is {} acceptEncoding
                 && Compressors.FirstOrDefault(c => c.EncodingType.Equals(acceptEncoding.Value, StringComparison.OrdinalIgnoreCase)) is ICompressor serverCompressor)
        {
            response.Content = new CompressedContent(response.Content, serverCompressor);
        }
        return response;
    }
}