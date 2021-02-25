// <copyright file="SubtitleBlock.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System.Collections.Generic;
    using AV.Core.Common;
    using FFmpeg.AutoGen;

    /// <summary>
    /// A subtitle frame container. Simply contains text lines.
    /// </summary>
    internal sealed class SubtitleBlock : MediaBlock
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="SubtitleBlock"/> class.
        /// </summary>
        internal SubtitleBlock()
            : base(MediaType.Subtitle)
        {
            // placeholder
        }

        /// <summary>
        /// Gets the lines of text for this subtitle frame with all formatting stripped out.
        /// </summary>
        public IList<string> Text { get; } = new List<string>(16);

        /// <summary>
        /// Gets the original text in SRT or ASS format.
        /// </summary>
        public IList<string> OriginalText { get; } = new List<string>(16);

        /// <summary>
        /// Gets the type of the original text.
        /// Returns None when it's a bitmap or when it's None.
        /// </summary>
        public AVSubtitleType OriginalTextType { get; internal set; }
    }
}
