using System.IO;
using System.IO.Compression;

namespace CompatApiClient.Compression
{
    public class GZipCompressor : Compressor
    {
        public override string EncodingType => "gzip";

        protected override Stream CreateCompressionStream(Stream output)
            => new GZipStream(output, CompressionMode.Compress, true);

        protected override Stream CreateDecompressionStream(Stream input)
            => new GZipStream(input, CompressionMode.Decompress, true);
    }
}