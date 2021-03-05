// <copyright file="FrameInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    /// <summary>
    /// Frame info.
    /// </summary>
    public class FrameInfo
    {
        /// <summary>
        /// Gets the start time of the frame.
        /// </summary>
        public long StartTimeTicks { get; }

        /// <summary>
        /// Gets the original, unadjusted presentation time.
        /// </summary>
        public long PresentationTime { get; }
    }
}
