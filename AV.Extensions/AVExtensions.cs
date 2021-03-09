// <copyright file="AVExtensions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using AV.Common;
    using AV.Core;

    /// <summary>
    /// Library and extensions methods for all things AV.
    /// </summary>
    public static class AVExtensions
    {
        /// <summary>
        /// Opens a media session from file, making a guess as to whether to
        /// treat the file as 'secure'.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="key">The key.</param>
        /// <param name="bufferLength">The buffer length.</param>
        /// <returns>The media session.</returns>
        public static MediaSession OpenSession(
            string path,
            byte[] key = null,
            int bufferLength = 32768)
        {
            var fi = new FileInfo(path);
            var source = Regex.IsMatch(fi.Name, @"^[a-f0-9]{64}\.")
                ? new SecureFileSource(path, key ?? throw new ArgumentNullException(), bufferLength)
                : new FileSource(path, bufferLength);
            return new MediaSession(source);
        }
    }
}
