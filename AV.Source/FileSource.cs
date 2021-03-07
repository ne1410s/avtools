// <copyright file="FileSource.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Source
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using AV.Core.Common;

    /// <summary>
    /// A media source backed by file.
    /// </summary>
    public class FileSource : IMediaInputStream
    {
        private const int SeekSize = FFmpeg.AutoGen.ffmpeg.AVSEEK_SIZE;
        private static readonly int EOF = FFmpeg.AutoGen.ffmpeg.AVERROR_EOF;

        private readonly object readLock = new object();
        private readonly byte[] readBuffer;

        /// <summary>
        /// Initialises a new instance of the <see cref="FileSource"/> class.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="bufferLength">The buffer length.</param>
        /// <param name="protocol">The protocol name.</param>
        public FileSource(string path, int bufferLength = 32768, string protocol = "file")
        {
            this.ReadBufferLength = bufferLength;
            this.readBuffer = new byte[bufferLength];
            this.FileStream = File.OpenRead(path);
            this.StreamUri = new Uri($"{protocol}://{path}");
        }

        /// <inheritdoc/>
        public Uri StreamUri { get; }

        /// <inheritdoc/>
        public bool CanSeek => true;

        /// <inheritdoc/>
        public int ReadBufferLength { get; }

        /// <summary>
        /// Gets the file stream.
        /// </summary>
        protected FileStream FileStream { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.FileStream?.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public unsafe int Read(void* opaque, byte* buffer, int bufferLength) =>
            this.TryManipulateStream(EOF, () =>
            {
                var readCount = this.ReadNext(this.readBuffer);
                if (readCount > 0)
                {
                    Marshal.Copy(this.readBuffer, 0, (IntPtr)buffer, readCount);
                }

                return readCount;
            });

        /// <inheritdoc/>
        public unsafe long Seek(void* opaque, long offset, int whence) =>
            this.TryManipulateStream(EOF, () => whence == SeekSize
                ? this.FileStream.Length
                : this.FileStream.Seek(
                    this.NormaliseOffset(offset), SeekOrigin.Begin));

        /// <summary>
        /// Reads the stream at its current position into the supplied buffer up
        /// to the maximum according to the buffer physical size.
        /// </summary>
        /// <param name="buf">The buffer.</param>
        /// <returns>The number of bytes read.</returns>
        protected virtual int ReadNext(byte[] buf) => this.FileStream.Read(buf, 0, buf.Length);

        /// <summary>
        /// Obtains a normalised offset; pre-seek adjustment.
        /// </summary>
        /// <param name="offset">The initial offset.</param>
        /// <returns>The normalised value.</returns>
        protected virtual long NormaliseOffset(long offset) => offset;

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
