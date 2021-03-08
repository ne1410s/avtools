// <copyright file="DemuxerGlobalOptions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Common
{
    using System;

    /// <summary>
    /// The libavformat library provides some generic global options, which can
    /// be set on all the muxers and demuxers.
    /// For additional information, please see: https://ffmpeg.org/ffmpeg-formats.html#Format-Options
    /// Geek Stuff: https://github.com/FFmpeg/FFmpeg/blob/a0ac49e38ee1d1011c394d7be67d0f08b2281526/libavformat/options_table.h#L36.
    /// </summary>
    internal sealed class DemuxerGlobalOptions
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="DemuxerGlobalOptions"/>
        /// class.
        /// </summary>
        internal DemuxerGlobalOptions()
        {
        }

        /// <summary>
        /// Gets or sets a value indicating whether to enable reduced buffering.
        /// </summary>
        public bool EnableReducedBuffering { get; set; }

        /// <summary>
        /// Gets or sets probing size in bytes, i.e. the size of the data to
        /// analyze to get stream information. A higher value will enable
        /// detecting more information in case it is dispersed into the stream,
        /// but will increase latency. Must be an integer not lesser than 32.
        /// It is 5000000 by default.
        /// </summary>
        public int ProbeSize { get; set; }

        /// <summary>
        /// Gets or sets the packet size.
        /// </summary>
        public int PacketSize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to flag ignore index.
        /// Port of ffflags.
        /// </summary>
        public bool FlagIgnoreIndex { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enable fast, but
        /// inaccurate seeks for some formats.
        /// Port of ffflags.
        /// </summary>
        public bool FlagEnableFastSeek { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to generate PTS.
        /// Port of genpts.
        /// </summary>
        public bool FlagGeneratePts { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to not fill in missing
        /// values that can be exactly calculated.
        /// Port of ffflags.
        /// </summary>
        public bool FlagEnableNoFillIn { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to ignore DTS.
        /// Port of ffflags.
        /// </summary>
        public bool FlagIgnoreDts { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to discard corrupted frames.
        /// Port of ffflags.
        /// </summary>
        public bool FlagDiscardCorrupt { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to tTry to interleave output
        /// packets by DTS.
        /// Port of ffflags.
        /// </summary>
        public bool FlagSortDts { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to not merge side data.
        /// Port of ffflags.
        /// </summary>
        public bool FlagKeepSideData { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enable RTP MP4A-LATM
        /// payload.
        /// Port of ffflags.
        /// </summary>
        public bool FlagEnableLatmPayload { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to reduce the latency
        /// introduced by optional buffering
        /// Port of ffflags.
        /// </summary>
        public bool FlagNoBuffer { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to stop muxing at the end of
        /// the shortest stream. It may be needed to increase
        /// max_interleave_delta to avoid flushing the longer streams before EOF.
        /// Port of ffflags.
        /// </summary>
        public bool FlagStopAtShortest { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to allow seeking to
        /// non-keyframes on demuxer level when supported if set to 1.
        /// Default is 0.
        /// </summary>
        public bool SeekToAny { get; set; }

        /// <summary>
        /// Gets or sets the maximum duration to be analyzed before identifying
        /// stream information. In realtime streams this can be reduced to
        /// reduce latency (i.e. TimeSpan.Zero).
        /// </summary>
        public TimeSpan MaxAnalyzeDuration { get; set; }

        /// <summary>
        /// Gets or sets the protocol whitelist. The values must be separated by
        /// comma. Example: file,http,https,tcp,tls.
        /// </summary>
        public string ProtocolWhitelist { get; set; }
    }
}
