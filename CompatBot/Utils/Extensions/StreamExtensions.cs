using System.IO;

namespace CompatBot.Utils;

internal static class StreamExtensions
{
    public static async Task<int> ReadBytesAsync(this Stream stream, byte[] buffer, int count = 0)
    {
        if (count < 1 || count > buffer.Length)
            count = buffer.Length;
        var result = 0;
        int read;
        do
        {
            var remaining = count - result;
            read = await stream.ReadAsync(buffer.AsMemory(result, remaining)).ConfigureAwait(false);
            result += read;
        } while (read > 0 && result < count);
        return result;
    }
}