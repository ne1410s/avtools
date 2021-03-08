// <copyright file="MediaInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using AV.Core.Internal.Container;
    using AV.Core.Internal.FFmpeg;
    using AV.Core.Internal.Utilities;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Holds media information about the input, its chapters, programs and
    /// individual stream components.
    /// </summary>
    internal unsafe class MediaInfo
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="MediaInfo"/> class.
        /// </summary>
        /// <param name="container">The container.</param>
        internal MediaInfo(MediaContainer container)
        {
            // The below logic was implemented using the same ideas conveyed by
            // the following code:
            // Reference: https://ffmpeg.org/doxygen/3.2/dump_8c_source.html
            var ic = container.InputContext;
            this.MediaSource = container.MediaSource;
            this.Format = GeneralUtilities.PtrToStringUTF8(ic->iformat->name);
            this.Metadata = container.Metadata;
            this.StartTime = ic->start_time != ffmpeg.AV_NOPTS_VALUE ? ic->start_time.ToTimeSpan() : TimeSpan.MinValue;
            this.Duration = ic->duration != ffmpeg.AV_NOPTS_VALUE ? ic->duration.ToTimeSpan() : TimeSpan.MinValue;
            this.BitRate = ic->bit_rate < 0 ? 0 : ic->bit_rate;
            this.Streams = ExtractStreams(ic).ToDictionary(k => k.StreamIndex, v => v);
            this.BestStreams = FindBestStreams(ic, this.Streams);
        }

        /// <summary>
        /// Gets the input URL used to access and create the media container.
        /// </summary>
        public string MediaSource { get; }

        /// <summary>
        /// Gets the name of the container format.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets the metadata for the input. This may include stuff like title,
        /// creation date, company name, etc. Individual stream components,
        /// chapters and programs may contain additional metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; }

        /// <summary>
        /// Gets the duration of the input as reported by the container format.
        /// Individual stream components may have different values. Returns
        /// TimeSpan.MinValue if unknown.
        /// </summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Gets the start of the input as reported by the container format.
        /// Individual stream components may have different values. Returns
        /// TimeSpan.MinValue if unknown.
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Gets a value as reported by the container format.
        /// </summary>
        public long BitRate { get; }

        /// <summary>
        /// Gets the dictionary of stream info components by stream index.
        /// </summary>
        public IReadOnlyDictionary<int, StreamInfo> Streams { get; }

        /// <summary>
        /// Gets access to the best streams of each media type found in the
        /// container. This uses some internal FFmpeg heuristics.
        /// </summary>
        public IReadOnlyDictionary<AVMediaType, StreamInfo> BestStreams { get; }

        /// <summary>
        /// Extracts the stream infos from the input.
        /// </summary>
        /// <param name="inputContext">The input context.</param>
        /// <returns>The list of stream infos.</returns>
        private static List<StreamInfo> ExtractStreams(AVFormatContext* inputContext)
        {
            var result = new List<StreamInfo>(32);
            if (inputContext->streams == null)
            {
                return result;
            }

            for (var i = 0; i < inputContext->nb_streams; i++)
            {
                var s = inputContext->streams[i];

                var codecContext = ffmpeg.avcodec_alloc_context3(null);

#pragma warning disable CS0618 // Type or member is obsolete

                // ffmpeg.avcodec_parameters_to_context(codecContext, s->codecpar);
                ffmpeg.avcodec_copy_context(codecContext, s->codec);
#pragma warning restore CS0618 // Type or member is obsolete

                var bitsPerSample = codecContext->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO ?
                    ffmpeg.av_get_bits_per_sample(codecContext->codec_id) : 0;

                var stream = new StreamInfo
                {
                    StreamId = s->id,
                    StreamIndex = s->index,
                    Metadata = FFDictionary.ToDictionary(s->metadata),
                    CodecType = codecContext->codec_type,
                    CodecTypeName = ffmpeg.av_get_media_type_string(codecContext->codec_type),
                    Codec = codecContext->codec_id,
                    CodecName = ffmpeg.avcodec_get_name(codecContext->codec_id),
                    CodecProfile = ffmpeg.avcodec_profile_name(codecContext->codec_id, codecContext->profile),
                    ReferenceFrameCount = codecContext->refs,
                    CodecTag = codecContext->codec_tag,
                    PixelFormat = codecContext->pix_fmt,
                    FieldOrder = codecContext->field_order,
                    IsInterlaced = codecContext->field_order != AVFieldOrder.AV_FIELD_PROGRESSIVE
                        && codecContext->field_order != AVFieldOrder.AV_FIELD_UNKNOWN,
                    ColorRange = codecContext->color_range,
                    PixelWidth = codecContext->width,
                    PixelHeight = codecContext->height,
                    HasClosedCaptions = (codecContext->properties & ffmpeg.FF_CODEC_PROPERTY_CLOSED_CAPTIONS) != 0,
                    IsLossless = (codecContext->properties & ffmpeg.FF_CODEC_PROPERTY_LOSSLESS) != 0,
                    Channels = codecContext->channels,
                    BitRate = bitsPerSample > 0 ?
                        bitsPerSample * codecContext->channels * codecContext->sample_rate :
                        codecContext->bit_rate,
                    MaxBitRate = codecContext->rc_max_rate,
                    InfoFrameCount = s->codec_info_nb_frames,
                    TimeBase = s->time_base,
                    SampleFormat = codecContext->sample_fmt,
                    SampleRate = codecContext->sample_rate,
                    DisplayAspectRatio = codecContext->height > 0 ?
                        ffmpeg.av_d2q((double)codecContext->width / codecContext->height, int.MaxValue) :
                        default,
                    SampleAspectRatio = codecContext->sample_aspect_ratio,
                    Disposition = s->disposition,
                    StartTime = s->start_time.ToTimeSpan(s->time_base),
                    Duration = s->duration.ToTimeSpan(s->time_base),
                    FPS = s->avg_frame_rate.ToDouble(),
                    TBR = s->r_frame_rate.ToDouble(),
                    TBN = 1d / s->time_base.ToDouble(),
                    TBC = 1d / codecContext->time_base.ToDouble(),
                };

                // Extract valid hardware configurations
                stream.HardwareDevices = HardwareAccelerator.GetCompatibleDevices(stream.Codec);
                stream.HardwareDecoders = GetHardwareDecoders(stream.Codec);

                ffmpeg.avcodec_free_context(&codecContext);

                result.Add(stream);
            }

            return result;
        }

        /// <summary>
        /// Finds the best streams for audio video, and subtitles.
        /// </summary>
        /// <param name="ic">The ic.</param>
        /// <param name="streams">The streams.</param>
        /// <returns>The star infos.</returns>
        private static Dictionary<AVMediaType, StreamInfo> FindBestStreams(
            AVFormatContext* ic,
            IReadOnlyDictionary<int, StreamInfo> streams)
        {
            // Initialize and clear all the stream indexes.
            var streamIndexes = new Dictionary<AVMediaType, int>();

            for (var i = 0; i < (int)AVMediaType.AVMEDIA_TYPE_NB; i++)
            {
                streamIndexes[(AVMediaType)i] = -1;
            }

            // Find best streams for each component. If we passed null instead
            // of the requestedCodec pointer, then find_best_stream would not
            // validate whether a valid decoder is registered.
            AVCodec* requestedCodec = null;

            streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO] =
                ffmpeg.av_find_best_stream(
                    ic,
                    AVMediaType.AVMEDIA_TYPE_VIDEO,
                    streamIndexes[(int)AVMediaType.AVMEDIA_TYPE_VIDEO],
                    -1,
                    &requestedCodec,
                    0);

            streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO] =
                ffmpeg.av_find_best_stream(
                    ic,
                    AVMediaType.AVMEDIA_TYPE_AUDIO,
                    streamIndexes[AVMediaType.AVMEDIA_TYPE_AUDIO],
                    streamIndexes[AVMediaType.AVMEDIA_TYPE_VIDEO],
                    &requestedCodec,
                    0);

            var result = new Dictionary<AVMediaType, StreamInfo>();
            foreach (var kvp in streamIndexes.Where(n => n.Value >= 0))
            {
                result[kvp.Key] = streams[kvp.Value];
            }

            return result;
        }

        /// <summary>
        /// Gets the available hardware decoder codecs for the given codec id.
        /// </summary>
        /// <param name="codecFamily">The codec family.</param>
        /// <returns>A list of hardware-enabled decoder codec names.</returns>
        private static List<string> GetHardwareDecoders(AVCodecID codecFamily)
        {
            var result = new List<string>(16);

            foreach (var c in Library.AllCodecs)
            {
                if (ffmpeg.av_codec_is_decoder(c) == 0)
                {
                    continue;
                }

                if (c->id != codecFamily)
                {
                    continue;
                }

                if ((c->capabilities & ffmpeg.AV_CODEC_CAP_HARDWARE) != 0
                    || (c->capabilities & ffmpeg.AV_CODEC_CAP_HYBRID) != 0)
                {
                    result.Add(GeneralUtilities.PtrToStringUTF8(c->name));
                }
            }

            return result;
        }
    }
}
