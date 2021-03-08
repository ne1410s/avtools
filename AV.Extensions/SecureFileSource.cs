// <copyright file="SecureFileSource.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using AV.Common.Sources;
    using FullStack.Crypto;

    /// <summary>
    /// A media source backed by a cryptographically secure file.
    /// </summary>
    public class SecureFileSource : FileSource
    {
        private readonly AesGcm aes;
        private readonly byte[] srcBuffer;
        private readonly byte[] macBuffer = CryptExtensions.GenerateMacBuffer();

        /// <summary>
        /// Initialises a new instance of the <see cref="SecureFileSource"/>
        /// class.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="key">The decryption key.</param>
        /// <param name="bufferLength">The buffer length.</param>
        public SecureFileSource(string path, byte[] key, int bufferLength = 32768)
            : base(path, bufferLength)
        {
            var fi = new FileInfo(path);
            this.srcBuffer = new byte[bufferLength];
            this.aes = key.GenerateAes(fi.GenerateSalt(), this.FileStream.GeneratePepper());
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer) =>
            this.aes.DecryptBlock(this.FileStream, false, this.srcBuffer, this.macBuffer, buffer);

        /// <inheritdoc/>
        public override long Seek(long offset)
        {
            var chunkSize = this.BufferLength;
            var bufferMultiple = chunkSize * (long)Math.Floor((double)offset / chunkSize);
            return base.Seek(bufferMultiple);
        }
    }
}
