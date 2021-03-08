// <copyright file="FileSource.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Common.Sources
{
    using System;
    using System.IO;
    using AV.Core;

    /// <summary>
    /// A media source backed by file.
    /// </summary>
    public class FileSource : IMediaInputStream
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="FileSource"/> class.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="bufferLength">The buffer length.</param>
        public FileSource(string path, int bufferLength = 32768)
        {
            this.Uri = path;
            this.BufferLength = bufferLength;
            this.FileStream = File.OpenRead(path);
            this.Length = this.FileStream.Length;
        }

        /// ,<inheritdoc/>
        public string Uri { get; }

        /// <inheritdoc/>
        public int BufferLength { get; } = 32768;

        /// <inheritdoc/>
        public long Length { get; }

        /// <inheritdoc/>
        public bool CanSeek => true;

        /// <summary>
        /// Gets the file stream.
        /// </summary>
        protected FileStream FileStream { get; }

        /// <inheritdoc/>
        public virtual int Read(byte[] buffer) =>
            this.FileStream.Read(buffer, 0, buffer.Length);

        /// <inheritdoc/>
        public virtual long Seek(long offset) =>
            this.FileStream.Seek(offset, SeekOrigin.Begin);

        /// <inheritdoc/>
        public void Dispose()
        {
            this.FileStream?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
