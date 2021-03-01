// <copyright file="ThumbnailData.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.Drawing;

    /// <summary>
    /// Thumbnail data.
    /// </summary>
    public record ThumbnailData
    {
        /// <summary>
        /// Gets the frame number.
        /// </summary>
        public long FrameNumber { get; init; }

        /// <summary>
        /// Gets the image.
        /// </summary>
        public Bitmap Image { get; init; }

        /// <summary>
        /// Gets the timestamp.
        /// </summary>
        public TimeSpan TimeStamp { get; init; }
    }
}
