// <copyright file="ImageFrameInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System.Drawing;

    /// <summary>
    /// Image frame info.
    /// </summary>
    public class ImageFrameInfo : FrameInfo
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
        /// Gets the source url.
        /// </summary>
        public string SourceUrl { get; init; }
    }
}
