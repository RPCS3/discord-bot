using System;
using System.IO;

namespace CompatBot.Utils
{
    internal class BufferCopyStream : Stream, IDisposable
    {
        private readonly Stream baseStream;
        private bool usedForReads;
        private bool usedForWrites;
        private long position;
        private readonly int bufSize;
        private readonly byte[] buf;
        private int bufStart, bufLength;

        public BufferCopyStream(Stream baseStream, int bufferSize = 4096)
        {
            this.baseStream = baseStream;
            bufSize = bufferSize;
            buf = new byte[bufSize];
        }

        public override void Flush()
        {
            baseStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            usedForReads = true;
            if (usedForWrites)
                throw new InvalidOperationException("Stream was used for writes before");

            var result = baseStream.Read(buffer, offset, count);
            position += result;
            CopyToBuf(buffer, offset, result);
            return result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            usedForWrites = true;
            if (usedForReads)
                throw new InvalidOperationException("Stream was used for reads before");

            baseStream.Write(buffer, offset, count);
            position += count;
            CopyToBuf(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            baseStream?.Dispose();
            base.Dispose(disposing);
        }

        void IDisposable.Dispose()
        {
            baseStream?.Dispose();
            base.Dispose();
        }

        private void CopyToBuf(byte[] buffer, int offset, int count)
        {
            if (count >= bufSize)
            {
                bufStart = 0;
                bufLength = bufSize;
                Buffer.BlockCopy(buffer, offset + count - bufSize, buf, bufStart, bufLength);
            }
            else
            {
                // copy as much data as we can to the end of the buffer
                bufStart = (bufStart + bufLength) % bufSize;
                bufLength = Math.Min(bufSize - bufStart, count);
                Buffer.BlockCopy(buffer, offset, buf, bufStart, bufLength);
                // if there's still more data, loop it around to the beginning
                if (bufLength < count)
                {
                    Buffer.BlockCopy(buffer, offset + bufLength, buf, 0, count-bufLength);
                    bufLength = count;
                }
            }
        }

        public byte[] GetBufferedBytes()
        {
            var result = new byte[bufLength];
            var partLength = Math.Min(bufSize - bufStart, bufLength);
            Buffer.BlockCopy(buf, bufStart, result, 0, partLength);
            if (partLength < bufLength)
                Buffer.BlockCopy(buf, 0, result, partLength, bufLength - partLength);
            return result;
        }

        public override bool CanRead => baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => baseStream.CanWrite;
        public override long Length => baseStream.Length;
        public override long Position
        {
            get => position;
            set => throw new InvalidOperationException();
        }
    }
}