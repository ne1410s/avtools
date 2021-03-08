﻿// <copyright file="Source.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using AV.Common.Sources;
    using AV.Core;
    using AV.Core.Common;
    using AV.Core.Extensions;

    /// <summary>
    /// Extensions for <see cref="IMediaInputStream"/>.
    /// </summary>
    public static class Source
    {
        /// <summary>
        /// Creates a media source from file, making a guess as to whether to
        /// treat the file as 'secure'.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="key">The key.</param>
        /// <param name="bufferLength">The buffer length.</param>
        /// <returns>A media input stream.</returns>
        public static IMediaInputStream FromFile(
            string path,
            byte[] key = null,
            int bufferLength = 32768)
        {
            var fi = new FileInfo(path);
            var appearsSecure = Regex.IsMatch(fi.Name, @"^[a-f0-9]{64}\.");
            return appearsSecure
                ? new SecureFileSource(path, key ?? throw new ArgumentNullException(), bufferLength)
                : new FileSource(path, bufferLength);
        }

        /// <summary>
        /// Obtains images from evenly-distributed positions in the source while
        /// making a guess as to whether to treat the file as 'secure'.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="count">The count.</param>
        /// <param name="key">The key.</param>
        /// <param name="bufferLength">The buffer length.</param>
        /// <param name="striveForExact">Whether to use multiple reads after
        /// each seek in order to attempt to obtain the exact correct position.
        /// The default behaviour is to only do so for small files; where the
        /// differences are more likely to be noticable.</param>
        public static void AutoSnap(
            string path,
            Action<ImageFrameInfo, int> callback,
            int count = 24,
            byte[] key = null,
            int bufferLength = 32768,
            bool? striveForExact = null)
        {
            using var source = FromFile(path, key, bufferLength);
            source.AutoSnap(callback, count, striveForExact);
        }
    }
}
