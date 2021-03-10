// <copyright file="MediaSessionInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using AV.Core.Internal.Container;
    using AV.Core.Internal.Utilities;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Session information.
    /// </summary>
    public record MediaSessionInfo
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="MediaSessionInfo"/>
        /// class.
        /// </summary>
        /// <param name="container">The loaded container.</param>
        internal MediaSessionInfo(MediaContainer container)
        {
            var videoStream = container.MediaInfo.BestStreams[AVMediaType.AVMEDIA_TYPE_VIDEO];
            var videoComponent = container.Components.Video;

            this.Dimensions = new Size(videoStream.PixelWidth, videoStream.PixelHeight);
            this.Duration = videoComponent.Duration;
            this.Format = container.MediaFormatName;
            this.FrameCount = videoComponent.FrameCount;
            this.HasAudio = container.MediaInfo.BestStreams.ContainsKey(AVMediaType.AVMEDIA_TYPE_AUDIO);
            this.HasSubtitles = container.MediaInfo.BestStreams.ContainsKey(AVMediaType.AVMEDIA_TYPE_SUBTITLE);
            this.Metadata = container.Metadata;
            this.StreamMetadata = videoStream.Metadata;
            this.StreamUri = container.MediaSource;
            this.StartTime = TimeUtilities.Max(TimeSpan.Zero, videoComponent.StartTime);
            this.EndTime = videoComponent.EndTime;
        }

        /// <summary>
        /// Gets the original pixel dimensions.
        /// </summary>
        public Size Dimensions { get; }

        /// <summary>
        /// Gets the duration.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Gets the start time.
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Gets the end time.
        /// </summary>
        public TimeSpan EndTime { get; }

        /// <summary>
        /// Gets the format name.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets the frame count.
        /// </summary>
        public long FrameCount { get; }

        /// <summary>
        /// Gets a value indicating whether there is audio.
        /// </summary>
        public bool HasAudio { get; }

        /// <summary>
        /// Gets a value indicating whether there are subtitles.
        /// </summary>
        public bool HasSubtitles { get; }

        /// <summary>
        /// Gets the metadata associated with the parent format.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Gets the metadata associated with the video stream.
        /// </summary>
        public IReadOnlyDictionary<string, string> StreamMetadata { get; }

        /// <summary>
        /// Gets the stream uri.
        /// </summary>
        public string StreamUri { get; }
    }
}
