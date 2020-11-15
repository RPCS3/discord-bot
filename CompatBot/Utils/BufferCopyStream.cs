using System;
using System.Buffers;
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
        private readonly byte[] writeBuf;
        private readonly byte[] readBuf;
        private int bufStart, bufLength;
        private readonly object sync = new();
        private bool disposed;

        public BufferCopyStream(Stream? baseStream, int bufferSize = 4096)
        {
            if (baseStream == null)
                throw new ArgumentNullException(nameof(baseStream));

            if (bufferSize < 1)
                throw new ArgumentException("Buffer size cannot be non-positive", nameof(bufferSize));

            this.baseStream = baseStream;
            bufSize = bufferSize;
            writeBuf = ArrayPool<byte>.Shared.Rent(bufSize);
            readBuf = ArrayPool<byte>.Shared.Rent(16);
        }

        public override void Flush() => baseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            usedForReads = true;
            if (usedForWrites)
                throw new InvalidOperationException("Stream was used for writes before");

            var useTempBuf = count < 16;
            const int mask = ~0b1111;
            try
            {
                int result;  
                if (useTempBuf)
                {
                    result = baseStream.Read(readBuf, 0, count);
                    Buffer.BlockCopy(readBuf, 0, buffer, offset, result);
                }
                else
                    result = baseStream.Read(buffer, offset, count & mask); // make count divisible by 16 to workaround mega client issues
                position += result;
                CopyToBuf(buffer, offset, result);
                return result;
            }
            catch (Exception e)
            {
                Config.Log.Error(e, $"Failed to read from the base stream: {nameof(buffer)}.Length={buffer.Length}, {nameof(offset)}={offset}, {nameof(count)}={count}");
                throw;
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

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
            if (!disposed)
                lock (sync)
                {
                    if (!disposed)
                    {
                        ArrayPool<byte>.Shared.Return(writeBuf);
                        ArrayPool<byte>.Shared.Return(readBuf);
                        disposed = true;
                    }
                }
            baseStream.Dispose();
            base.Dispose(disposing);
        }

        void IDisposable.Dispose()
        {
            if (!disposed)
                lock (sync)
                {
                    if (!disposed)
                    {
                        ArrayPool<byte>.Shared.Return(writeBuf);
                        ArrayPool<byte>.Shared.Return(readBuf);
                        disposed = true;
                    }
                }
            baseStream.Dispose();
            base.Dispose();
        }

        private void CopyToBuf(byte[] buffer, int offset, int count)
        {
            if (count >= bufSize)
            {
                bufStart = 0;
                bufLength = bufSize;
                Buffer.BlockCopy(buffer, offset + count - bufSize, writeBuf, bufStart, bufLength);
            }
            else
            {
                // copy as much data as we can to the end of the buffer
                bufStart = (bufStart + bufLength) % bufSize;
                bufLength = Math.Min(bufSize - bufStart, count);
                Buffer.BlockCopy(buffer, offset, writeBuf, bufStart, bufLength);
                // if there's still more data, loop it around to the beginning
                if (bufLength < count)
                {
                    Buffer.BlockCopy(buffer, offset + bufLength, writeBuf, 0, count-bufLength);
                    bufLength = count;
                }
            }
        }

        public byte[] GetBufferedBytes()
        {
            var result = new byte[bufLength];
            var partLength = Math.Min(bufSize - bufStart, bufLength);
            Buffer.BlockCopy(writeBuf, bufStart, result, 0, partLength);
            if (partLength < bufLength)
                Buffer.BlockCopy(writeBuf, 0, result, partLength, bufLength - partLength);
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