using System.IO;
using System.Threading.Tasks;

namespace CompatBot.Utils
{
    internal static class StreamExtensions
    {
        public static async Task<int> ReadBytesAsync(this Stream stream, byte[] buffer)
        {
            var result = 0;
            int read;
            do
            {
                var remaining = buffer.Length - result;
                read = await stream.ReadAsync(buffer, result, remaining).ConfigureAwait(false);
                result += read;
            } while (read > 0 && result < buffer.Length);
            return result;
        }
    }
}
