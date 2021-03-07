// <copyright file="InternalSource.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Sources
{
    using System;
    using System.Runtime.InteropServices;
    using AV.Abstractions;

    /// <summary>
    /// Wraps <see cref="IMediaInputStream"/>, exposing the interface to the
    /// internal workings of ffmpeg.
    /// </summary>
    internal unsafe class InternalSource : IDisposable
    {
        private const int SeekSize = FFmpeg.AutoGen.ffmpeg.AVSEEK_SIZE;
        private static readonly int EOF = FFmpeg.AutoGen.ffmpeg.AVERROR_EOF;

        private readonly object readLock = new object();
        private readonly byte[] readBuffer;
        private readonly IMediaInputStream source;

        /// <summary>
        /// Initialises a new instance of the <see cref="InternalSource"/>
        /// class.
        /// </summary>
        /// <param name="source">The input stream.</param>
        public InternalSource(IMediaInputStream source)
        {
            this.source = source;
            this.readBuffer = new byte[source.BufferLength];
        }

        /// <summary>
        /// Gets the buffer length.
        /// </summary>
        public int BufferLength => this.source.BufferLength;

        /// <summary>
        /// Gets a value indicating whether the stream is seekable.
        /// </summary>
        public bool CanSeek => this.source.CanSeek;

        /// <summary>
        /// Reads from the underlying stream and writes up to
        /// <paramref name="bufferLength"/> bytes to the
        /// <paramref name="buffer"/>. Returns the number of bytes that
        /// were written.
        /// </summary>
        /// <param name="opaque">An FFmpeg provided opaque reference.</param>
        /// <param name="buffer">The target buffer.</param>
        /// <param name="bufferLength">The target buffer length.</param>
        /// <returns>The number of bytes that have been read.</returns>
        public int ReadUnsafe(void* opaque, byte* buffer, int bufferLength) =>
            this.TryManipulateStream(EOF, () =>
            {
                var readCount = this.source.Read(this.readBuffer);
                if (readCount > 0)
                {
                    Marshal.Copy(this.readBuffer, 0, (IntPtr)buffer, readCount);
                }

                return readCount;
            });

        /// <summary>
        /// Seeks to the specified offset. The offset can be in byte position or
        /// in time units. This is specified by the whence parameter which is
        /// one of the AVSEEK prefixed constants.
        /// </summary>
        /// <param name="opaque">The opaque.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="whence">The whence.</param>
        /// <returns>The position read; in bytes or time scale.</returns>
        public long SeekUnsafe(void* opaque, long offset, int whence) =>
            this.TryManipulateStream(EOF, () => whence == SeekSize
                ? this.source.Length
                : this.source.Seek(offset));

        /// <inheritdoc/>
        public void Dispose()
        {
            this.source?.Dispose();
        }

        private T TryManipulateStream<T>(T fallback, Func<T> operation)
        {
            lock (this.readLock)
            {
                try
                {
                    return operation();
                }
                catch
                {
                    return fallback;
                }
            }
        }
    }
}
