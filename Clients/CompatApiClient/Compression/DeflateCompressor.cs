using System.IO;
using System.IO.Compression;

namespace CompatApiClient.Compression
{
    public class DeflateCompressor : Compressor
    {
        public override string EncodingType => "deflate";

        protected override Stream CreateCompressionStream(Stream output)
            => new DeflateStream(output, CompressionMode.Compress, true);

        protected override Stream CreateDecompressionStream(Stream input)
            => new DeflateStream(input, CompressionMode.Decompress, true);
    }
}