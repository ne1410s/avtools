// <copyright file="ImageFrameInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System.Drawing;
    using AV.Core.Common;

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
    }
}
