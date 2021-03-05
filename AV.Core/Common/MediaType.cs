// <copyright file="MediaType.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using FFmpeg.AutoGen;

    /// <summary>
    /// Enumerates the different Media Types compatible with AVMEDIATYPE_*
    /// constants defined by FFmpeg.
    /// </summary>
    public enum MediaType
    {
        /// <summary>
        /// Represents an un-existing media type (-1).
        /// </summary>
        None = AVMediaType.AVMEDIA_TYPE_UNKNOWN,

        /// <summary>
        /// The video media type (0).
        /// </summary>
        Video = AVMediaType.AVMEDIA_TYPE_VIDEO,
    }
}
