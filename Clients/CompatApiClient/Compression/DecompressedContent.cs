using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace CompatApiClient.Compression
{
    public class DecompressedContent : HttpContent
    {
        private readonly HttpContent content;
        private readonly ICompressor compressor;

        public DecompressedContent(HttpContent content, ICompressor compressor)
        {
            this.content = content;
            this.compressor = compressor;
            RemoveHeaders();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var contentStream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            var decompressedLength = await compressor.DecompressAsync(contentStream, stream).ConfigureAwait(false);
            Headers.ContentLength = decompressedLength;
        }

        private void RemoveHeaders()
        {
            foreach (var (key, value) in content.Headers)
                Headers.TryAddWithoutValidation(key, value);
            Headers.ContentEncoding.Clear();
            Headers.ContentLength = null;
        }
    }
}