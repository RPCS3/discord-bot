namespace CompatBot.Utils;

internal class Utf8ToLatin1RegexPatternEncoderFallback : EncoderFallback
{
    private const int MaxBufferSize = 2+(2+2)*4; // (\xFF\xFF\xFF\xFF)

    public override EncoderFallbackBuffer CreateFallbackBuffer() => new CustomMapperFallbackBuffer();

    // This is the length of the largest possible replacement string we can
    // return for a single Unicode code point.
    public override int MaxCharCount => MaxBufferSize;

    public class CustomMapperFallbackBuffer : EncoderFallbackBuffer
    {
        private readonly byte[] buffer = new byte[MaxBufferSize];
        private int size, remaining;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        internal static readonly List<string> ByteToHex = new(256);

        static CustomMapperFallbackBuffer()
        {
            for (var i = 0; i < 256; i++) 
                ByteToHex.Add(i.ToString("X2"));
        }

        public CustomMapperFallbackBuffer()
        {
            // buffer will always look like this:
            // (\x??\x??\x??\x??\x??...)
            buffer[0] = (byte)'(';
            for (var i = 1; i < 30; i += 4)
            {
                buffer[i + 0] = (byte)'\\';
                buffer[i + 1] = (byte)'x';
            }
        }

        public override bool Fallback(char charUnknown, int index)
        {
            // Do the work of figuring out what sequence of characters should replace
            // charUnknown. index is the position in the original string of this character,
            // in case that's relevant.

            // If we end up generating a sequence of replacement characters, return
            // true, and the encoder will start calling GetNextChar. Otherwise return
            // false.

            // Alternatively, instead of returning false, you can simply extract
            // DefaultString from this.fb and return that for failure cases.
                
            Span<char> buf = stackalloc char[1];
            Span<byte> tmp = stackalloc byte[4];
            buf[0] = charUnknown;
            var count = Utf8.GetBytes(buf, tmp);
            for (var i = 0; i < count; i++)
            {
                ref var b = ref tmp[i];
                var s = ByteToHex[b];
                var offset = i * 4 + 1;
                buffer[offset + 0] = (byte)'\\';
                buffer[offset + 2] =(byte)s[0];
                buffer[offset + 3] = (byte)s[1];
            }
            buffer[count * 4 + 1] = (byte)')';
            size = count * 4 + 2;
            remaining = size;
            return true;
        }

        public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
        {
            Span<char> buf = stackalloc char[2];
            Span<byte> tmp = stackalloc byte[8];
            buf[0] = charUnknownHigh;
            buf[1] = charUnknownLow;
            var count = Utf8.GetBytes(buf, tmp);
            for (var i = 0; i < count; i++)
            {
                ref var b = ref tmp[i];
                var s = ByteToHex[b];
                var offset = i * 4 + 1;
                buffer[offset + 0] = (byte)'\\';
                buffer[offset + 2] =(byte)s[0];
                buffer[offset + 3] = (byte)s[1];
            }
            buffer[count * 4 + 1] = (byte)')';
            size = count * 4 + 2;
            remaining = size;
            return true;
        }

        public override char GetNextChar()
        {
            // Return the next character in our internal buffer of replacement
            // characters waiting to be put into the encoded byte stream. If
            // we're all out of characters, return '\u0000'.

            if (remaining == 0)
                return (char)0;

#if DEBUG
            Span<char> tmp = stackalloc char[1];
            Encoding.Latin1.GetChars(buffer.AsSpan(size - remaining, 1), tmp);
            if (tmp[0] != (char)buffer[size - remaining])
                Config.Log.Error("You fucked up, buddy");
            remaining--;
            return tmp[0];
#else
                remaining--;
                return (char)buffer[size - remaining - 1];
#endif
        }

        public override bool MovePrevious()
        {
            // Back up to the previous character we returned and get ready
            // to return it again. If that's possible, return true; if that's
            // not possible (e.g. we have no previous character) return false;
            if (remaining == size)
                return false;

            remaining++;
            return true;
        }

        public override int Remaining => remaining;

        public override void Reset()
        {
            remaining = 0;
            size = 0;
        }
    }
}