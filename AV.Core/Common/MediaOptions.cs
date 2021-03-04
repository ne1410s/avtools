// <copyright file="MediaOptions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System.Collections.Generic;

    /// <summary>
    /// Represents options that applied creating the individual media stream components.
    /// Once the container has created the media components, changing these options will have no effect.
    /// See: https://www.ffmpeg.org/ffmpeg-all.html#Main-options
    /// Partly a port of https://github.com/FFmpeg/FFmpeg/blob/master/fftools/ffmpeg_opt.c.
    /// </summary>
    public sealed class MediaOptions
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="MediaOptions"/> class.
        /// </summary>
        internal MediaOptions()
        {
        }

        /// <summary>
        /// Gets access to the global and per-stream decoder options
        /// See https://www.ffmpeg.org/ffmpeg-codecs.html#Codec-Options.
        /// </summary>
        public DecoderOptions DecoderParams { get; } = new DecoderOptions();

        /// <summary>
        /// Gets a dictionary of stream indexes and force decoder codec names.
        /// This is equivalent to the -codec Main option.
        /// See: https://www.ffmpeg.org/ffmpeg-all.html#Main-options (-codec option).
        /// </summary>
        public Dictionary<int, string> DecoderCodec { get; } = new Dictionary<int, string>(32);

        /// <summary>
        /// Use Stream's HardwareDevices property to get a list of
        /// compatible hardware accelerators.
        /// </summary>
        public HardwareDeviceInfo VideoHardwareDevice { get; set; }

        /// <summary>
        /// Allows for a custom video filter string.
        /// Please see: https://ffmpeg.org/ffmpeg-filters.html#Video-Filters.
        /// </summary>
        public string VideoFilter { get; set; } = string.Empty;

        /// <summary>
        /// Specifies a forced FPS value for the input video stream.
        /// </summary>
        public double VideoForcedFps { get; set; }

        /// <summary>
        /// Initially contains the best suitable video stream.
        /// Can be changed to a different stream reference.
        /// </summary>
        public StreamInfo VideoStream { get; set; }

        /// <summary>
        /// Only recommended for live streams. Gets or sets a value indicating whether each component needs to run
        /// its timing independently. This property is useful when for example when
        /// the audio and the video components of the stream have no timing relationship or when you don't need the
        /// components to be synchronized between them.
        /// </summary>
        public bool IsTimeSyncDisabled { get; set; }
    }
}
