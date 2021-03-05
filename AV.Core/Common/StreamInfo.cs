// <copyright file="StreamInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System;
    using System.Collections.Generic;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents media stream information.
    /// </summary>
    public class StreamInfo
    {
        /// <summary>
        /// Gets the stream identifier. This is different from the stream index.
        /// Typically this value is not very useful.
        /// </summary>
        public int StreamId { get; internal set; }

        /// <summary>
        /// Gets the index of the stream.
        /// </summary>
        public int StreamIndex { get; internal set; }

        /// <summary>
        /// Gets the type of the codec.
        /// </summary>
        public AVMediaType CodecType { get; internal set; }

        /// <summary>
        /// Gets the name of the codec type. Audio, Video, Subtitle, Data, etc.
        /// </summary>
        public string CodecTypeName { get; internal set; }

        /// <summary>
        /// Gets the codec identifier.
        /// </summary>
        public AVCodecID Codec { get; internal set; }

        /// <summary>
        /// Gets the name of the codec.
        /// </summary>
        public string CodecName { get; internal set; }

        /// <summary>
        /// Gets the codec profile. Only valid for H.264 or
        /// video codecs that use profiles. Otherwise empty.
        /// </summary>
        public string CodecProfile { get; internal set; }

        /// <summary>
        /// Gets the codec tag. Not very useful except for fixing bugs with
        /// some demuxer scenarios.
        /// </summary>
        public uint CodecTag { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this stream has closed captions.
        /// Typically this is set for video streams.
        /// </summary>
        public bool HasClosedCaptions { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this stream contains lossless
        /// compressed data.
        /// </summary>
        public bool IsLossless { get; internal set; }

        /// <summary>
        /// Gets the pixel format. Only valid for Video streams.
        /// </summary>
        public AVPixelFormat PixelFormat { get; internal set; }

        /// <summary>
        /// Gets the width of the video frames.
        /// </summary>
        public int PixelWidth { get; internal set; }

        /// <summary>
        /// Gets the height of the video frames.
        /// </summary>
        public int PixelHeight { get; internal set; }

        /// <summary>
        /// Gets the field order. This is useful to determine
        /// if the video needs de-interlacing.
        /// </summary>
        public AVFieldOrder FieldOrder { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the video frames are interlaced.
        /// </summary>
        public bool IsInterlaced { get; internal set; }

        /// <summary>
        /// Gets the video color range.
        /// </summary>
        public AVColorRange ColorRange { get; internal set; }

        /// <summary>
        /// Gets the number of audio channels.
        /// </summary>
        public int Channels { get; internal set; }

        /// <summary>
        /// Gets the audio sample rate.
        /// </summary>
        public int SampleRate { get; internal set; }

        /// <summary>
        /// Gets the audio sample format.
        /// </summary>
        public AVSampleFormat SampleFormat { get; internal set; }

        /// <summary>
        /// Gets the stream time base unit in seconds.
        /// </summary>
        public AVRational TimeBase { get; internal set; }

        /// <summary>
        /// Gets the sample aspect ratio.
        /// </summary>
        public AVRational SampleAspectRatio { get; internal set; }

        /// <summary>
        /// Gets the display aspect ratio.
        /// </summary>
        public AVRational DisplayAspectRatio { get; internal set; }

        /// <summary>
        /// Gets the reported bit rate. 9 for unavailable.
        /// </summary>
        public long BitRate { get; internal set; }

        /// <summary>
        /// Gets the maximum bit rate for variable bit rate streams. 0 if
        /// unavailable.
        /// </summary>
        public long MaxBitRate { get; internal set; }

        /// <summary>
        /// Gets the number of frames read to obtain the stream's information.
        /// </summary>
        public int InfoFrameCount { get; internal set; }

        /// <summary>
        /// Gets the number of reference frames.
        /// </summary>
        public int ReferenceFrameCount { get; internal set; }

        /// <summary>
        /// Gets the average FPS reported by the stream.
        /// </summary>
        public double FPS { get; internal set; }

        /// <summary>
        /// Gets the real (base) frame rate of the stream.
        /// </summary>
        public double TBR { get; internal set; }

        /// <summary>
        /// Gets the fundamental unit of time in 1/seconds used to represent
        /// timestamps in the stream, according to the stream data.
        /// </summary>
        public double TBN { get; internal set; }

        /// <summary>
        /// Gets the fundamental unit of time in 1/seconds used to represent
        /// timestamps in the stream ,according to the codec.
        /// </summary>
        public double TBC { get; internal set; }

        /// <summary>
        /// Gets the disposition flags.
        /// Please see ffmpeg.AV_DISPOSITION_* fields.
        /// </summary>
        public int Disposition { get; internal set; }

        /// <summary>
        /// Gets the start time.
        /// </summary>
        public TimeSpan StartTime { get; internal set; }

        /// <summary>
        /// Gets the duration.
        /// </summary>
        public TimeSpan Duration { get; internal set; }

        /// <summary>
        /// Gets the stream's metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; internal set; }

        /// <summary>
        /// Gets the compatible hardware devices for the stream's codec.
        /// </summary>
        public IReadOnlyList<HardwareDeviceInfo> HardwareDevices { get; internal set; }

        /// <summary>
        /// Gets a list of compatible hardware decoder names.
        /// </summary>
        public IReadOnlyList<string> HardwareDecoders { get; internal set; }

        /// <summary>
        /// Gets the language string from the stream's metadata.
        /// </summary>
        public string Language => this.Metadata.ContainsKey("language") ?
            this.Metadata["language"] : string.Empty;

        /// <summary>
        /// Gets a value indicating whether the stream contains data that is not
        /// considered to be audio, video, or subtitles.
        /// </summary>
        public bool IsNonMedia =>
            this.CodecType == AVMediaType.AVMEDIA_TYPE_DATA ||
            this.CodecType == AVMediaType.AVMEDIA_TYPE_ATTACHMENT ||
            this.CodecType == AVMediaType.AVMEDIA_TYPE_UNKNOWN;
    }
}
