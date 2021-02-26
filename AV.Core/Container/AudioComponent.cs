// <copyright file="AudioComponent.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Diagnostics;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Provides audio sample extraction, decoding and scaling functionality.
    /// </summary>
    /// <seealso cref="MediaComponent" />
    public sealed unsafe class AudioComponent : MediaComponent
    {
        /// <summary>
        /// Holds a reference to the audio re-sampler
        /// This re-sampler gets disposed upon disposal of this object.
        /// </summary>
        private SwrContext* scaler;

        /// <summary>
        /// Used to determine if we have to reset the scaler parameters.
        /// </summary>
        private FFAudioParams lastSourceSpec;

        private AVFilterGraph* filterGraph;
        private AVFilterContext* sourceFilter;
        private AVFilterContext* sinkFilter;
        private AVFilterInOut* sinkInput;
        private AVFilterInOut* sourceOutput;

        private string appliedFilterString;
        private string currentFilterArguments;

        /// <summary>
        /// Initialises a new instance of the <see cref="AudioComponent"/> class.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="streamIndex">Index of the stream.</param>
        internal AudioComponent(MediaContainer container, int streamIndex)
            : base(container, streamIndex)
        {
            this.Channels = CodecContext->channels;
            this.SampleRate = CodecContext->sample_rate;
            this.BitsPerSample = ffmpeg.av_samples_get_buffer_size(null, 1, 1, CodecContext->sample_fmt, 1) * 8;
        }

        /// <summary>
        /// Gets the number of audio channels.
        /// </summary>
        public int Channels { get; }

        /// <summary>
        /// Gets the audio sample rate.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Gets the bits per sample.
        /// </summary>
        public int BitsPerSample { get; }

        /// <summary>
        /// Gets access to the AudioFilter string of the container's MediaOptions.
        /// </summary>
        private string FilterString => this.Container?.MediaOptions?.AudioFilter;

        /// <inheritdoc />
        public override bool MaterializeFrame(MediaFrame input, ref MediaBlock output, MediaBlock previousBlock)
        {
            if (output == null)
            {
                output = new AudioBlock();
            }

            if (input is AudioFrame == false || output is AudioBlock == false)
            {
                throw new ArgumentNullException($"{nameof(input)} and {nameof(output)} are either null or not of a compatible media type '{this.MediaType}'");
            }

            var source = (AudioFrame)input;
            var target = (AudioBlock)output;

            // Create the source and target audio specs. We might need to scale from
            // the source to the target
            var sourceSpec = FFAudioParams.CreateSource(source.Pointer);
            var targetSpec = FFAudioParams.CreateTarget(source.Pointer);

            // Initialize or update the audio scaler if required
            if (this.scaler == null || this.lastSourceSpec == null || FFAudioParams.AreCompatible(this.lastSourceSpec, sourceSpec) == false)
            {
                this.scaler = ffmpeg.swr_alloc_set_opts(
                    this.scaler,
                    targetSpec.ChannelLayout,
                    targetSpec.Format,
                    targetSpec.SampleRate,
                    sourceSpec.ChannelLayout,
                    sourceSpec.Format,
                    sourceSpec.SampleRate,
                    0,
                    null);

                RC.Current.Add(this.scaler);
                ffmpeg.swr_init(this.scaler);
                this.lastSourceSpec = sourceSpec;
            }

            // Allocate the unmanaged output buffer and convert to stereo.
            int outputSamplesPerChannel;
            if (target.Allocate(targetSpec.BufferLength) &&
                target.TryAcquireWriterLock(out var writeLock))
            {
                using (writeLock)
                {
                    var outputBufferPtr = (byte*)target.Buffer;

                    // Execute the conversion (audio scaling). It will return the number of samples that were output
                    outputSamplesPerChannel = ffmpeg.swr_convert(
                        this.scaler,
                        &outputBufferPtr,
                        targetSpec.SamplesPerChannel,
                        source.Pointer->extended_data,
                        source.Pointer->nb_samples);
                }
            }
            else
            {
                return false;
            }

            // Compute the buffer length
            var outputBufferLength =
                ffmpeg.av_samples_get_buffer_size(null, targetSpec.ChannelCount, outputSamplesPerChannel, targetSpec.Format, 1);

            // Flag the block if we have to
            target.PresentationTime = source.PresentationTime;
            target.IsStartTimeGuessed = source.HasValidStartTime == false;

            // Try to fix the start time, duration and End time if we don't have valid data
            if (source.HasValidStartTime == false && previousBlock != null)
            {
                // Get timing information from the previous block
                target.StartTime = TimeSpan.FromTicks(previousBlock.EndTime.Ticks + 1);
                target.Duration = source.Duration.Ticks > 0 ? source.Duration : previousBlock.Duration;
                target.EndTime = TimeSpan.FromTicks(target.StartTime.Ticks + target.Duration.Ticks);
            }
            else
            {
                // We set the target properties directly from the source
                target.StartTime = source.StartTime;
                target.Duration = source.Duration;
                target.EndTime = source.EndTime;
            }

            target.CompressedSize = source.CompressedSize;
            target.SamplesBufferLength = outputBufferLength;
            target.ChannelCount = targetSpec.ChannelCount;

            target.SampleRate = targetSpec.SampleRate;
            target.SamplesPerChannel = outputSamplesPerChannel;
            target.StreamIndex = input.StreamIndex;

            return true;
        }

        /// <inheritdoc />
        protected override MediaFrame CreateFrameSource(IntPtr framePointer)
        {
            // Validate the audio frame
            var frame = (AVFrame*)framePointer;
            if (framePointer == IntPtr.Zero || frame->channels <= 0 || frame->nb_samples <= 0 || frame->sample_rate <= 0)
            {
                return null;
            }

            // Init the filter graph for the frame
            this.InitializeFilterGraph(frame);

            AVFrame* outputFrame;

            // Filter Graph can be changed by issuing a ChangeMedia command
            if (this.filterGraph != null)
            {
                // Allocate the output frame
                outputFrame = MediaFrame.CloneAVFrame(frame);

                var result = ffmpeg.av_buffersrc_add_frame(this.sourceFilter, outputFrame);
                while (result >= 0)
                {
                    result = ffmpeg.av_buffersink_get_frame_flags(this.sinkFilter, outputFrame, 0);
                }

                if (outputFrame->nb_samples <= 0)
                {
                    // If we don't have a valid output frame simply release it and
                    // return the original input frame
                    MediaFrame.ReleaseAVFrame(outputFrame);
                    outputFrame = frame;
                }
                else
                {
                    // the output frame is the new valid frame (output frame).
                    // theretofore, we need to release the original
                    MediaFrame.ReleaseAVFrame(frame);
                }
            }
            else
            {
                outputFrame = frame;
            }

            // Check if the output frame is valid
            if (outputFrame->nb_samples <= 0)
            {
                return null;
            }

            var frameHolder = new AudioFrame(outputFrame, this);
            return frameHolder;
        }

        /// <inheritdoc />
        protected override void Dispose(bool alsoManaged)
        {
            RC.Current.Remove(this.scaler);
            if (this.scaler != null)
            {
                var scalerRef = this.scaler;
                ffmpeg.swr_free(&scalerRef);
                this.scaler = null;
            }

            this.DestroyFilterGraph();
            base.Dispose(alsoManaged);
        }

        /// <summary>
        /// Destroys the filter graph releasing unmanaged resources.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DestroyFilterGraph()
        {
            try
            {
                if (this.filterGraph == null)
                {
                    return;
                }

                RC.Current.Remove(this.filterGraph);
                var filterGraphRef = this.filterGraph;
                ffmpeg.avfilter_graph_free(&filterGraphRef);

                this.filterGraph = null;
                this.sinkInput = null;
                this.sourceOutput = null;
            }
            finally
            {
                this.appliedFilterString = null;
                this.currentFilterArguments = null;
            }
        }

        /// <summary>
        /// Computes the frame filter arguments that are appropriate for the audio filtering chain.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The base filter arguments.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ComputeFilterArguments(AVFrame* frame)
        {
            var hexChannelLayout = BitConverter.ToString(
                BitConverter.GetBytes(frame->channel_layout).Reverse().ToArray()).ReplaceOrdinal("-", string.Empty);

            var channelLayout = $"0x{hexChannelLayout}";

            var arguments =
                 $"time_base={Stream->time_base.num}/{Stream->time_base.den}:" +
                 $"sample_rate={frame->sample_rate:0}:" +
                 $"sample_fmt={ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format)}:" +
                 $"channel_layout={channelLayout}";

            return arguments;
        }

        /// <summary>
        /// If necessary, disposes the existing filter graph and creates a new one based on the frame arguments.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <exception cref="MediaContainerException">
        /// avfilter_graph_create_filter
        /// or
        /// avfilter_graph_create_filter
        /// or
        /// avfilter_link
        /// or
        /// avfilter_graph_parse
        /// or
        /// avfilter_graph_config.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeFilterGraph(AVFrame* frame)
        {
            // References: https://www.ffmpeg.org/doxygen/2.0/doc_2examples_2filtering_audio_8c-example.html
            const string SourceFilterName = "abuffer";
            const string SourceFilterInstance = "audio_buffer";
            const string SinkFilterName = "abuffersink";
            const string SinkFilterInstance = "audio_buffersink";

            // Get a snapshot of the FilterString
            var filterString = this.FilterString;

            // For empty filter strings ensure filtegraph is destroyed
            if (string.IsNullOrWhiteSpace(filterString))
            {
                this.DestroyFilterGraph();
                return;
            }

            // Recreate the filtergraph if we have to
            if (filterString != this.appliedFilterString)
            {
                this.DestroyFilterGraph();
            }

            // Ensure the filtergraph is compatible with the frame
            var filterArguments = this.ComputeFilterArguments(frame);
            if (filterArguments != this.currentFilterArguments)
            {
                this.DestroyFilterGraph();
            }
            else
            {
                return;
            }

            this.filterGraph = ffmpeg.avfilter_graph_alloc();
            RC.Current.Add(this.filterGraph);

            try
            {
                AVFilterContext* sourceFilterRef = null;
                AVFilterContext* sinkFilterRef = null;

                var result = ffmpeg.avfilter_graph_create_filter(
                    &sourceFilterRef, ffmpeg.avfilter_get_by_name(SourceFilterName), SourceFilterInstance, filterArguments, null, this.filterGraph);
                if (result != 0)
                {
                    throw new MediaContainerException(
                        $"{nameof(ffmpeg.avfilter_graph_create_filter)} ({SourceFilterInstance}) failed. Error {result}: {FFInterop.DecodeMessage(result)}");
                }

                result = ffmpeg.avfilter_graph_create_filter(
                    &sinkFilterRef, ffmpeg.avfilter_get_by_name(SinkFilterName), SinkFilterInstance, null, null, this.filterGraph);
                if (result != 0)
                {
                    throw new MediaContainerException(
                        $"{nameof(ffmpeg.avfilter_graph_create_filter)} ({SinkFilterInstance}) failed. Error {result}: {FFInterop.DecodeMessage(result)}");
                }

                this.sourceFilter = sourceFilterRef;
                this.sinkFilter = sinkFilterRef;

                if (string.IsNullOrWhiteSpace(filterString))
                {
                    result = ffmpeg.avfilter_link(this.sourceFilter, 0, this.sinkFilter, 0);
                    if (result != 0)
                    {
                        throw new MediaContainerException($"{nameof(ffmpeg.avfilter_link)} failed. Error {result}: {FFInterop.DecodeMessage(result)}");
                    }
                }
                else
                {
                    var initFilterCount = filterGraph->nb_filters;

                    this.sourceOutput = ffmpeg.avfilter_inout_alloc();
                    sourceOutput->name = ffmpeg.av_strdup("in");
                    sourceOutput->filter_ctx = this.sourceFilter;
                    sourceOutput->pad_idx = 0;
                    sourceOutput->next = null;

                    this.sinkInput = ffmpeg.avfilter_inout_alloc();
                    sinkInput->name = ffmpeg.av_strdup("out");
                    sinkInput->filter_ctx = this.sinkFilter;
                    sinkInput->pad_idx = 0;
                    sinkInput->next = null;

                    result = ffmpeg.avfilter_graph_parse(this.filterGraph, filterString, this.sinkInput, this.sourceOutput, null);
                    if (result != 0)
                    {
                        throw new MediaContainerException($"{nameof(ffmpeg.avfilter_graph_parse)} failed. Error {result}: {FFInterop.DecodeMessage(result)}");
                    }

                    // Reorder the filters to ensure that inputs of the custom filters are merged first
                    for (var i = 0; i < filterGraph->nb_filters - initFilterCount; i++)
                    {
                        var sourceAddress = filterGraph->filters[i];
                        var targetAddress = filterGraph->filters[i + initFilterCount];
                        filterGraph->filters[i] = targetAddress;
                        filterGraph->filters[i + initFilterCount] = sourceAddress;
                    }
                }

                result = ffmpeg.avfilter_graph_config(this.filterGraph, null);
                if (result != 0)
                {
                    throw new MediaContainerException($"{nameof(ffmpeg.avfilter_graph_config)} failed. Error {result}: {FFInterop.DecodeMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                this.LogError(Aspects.Component, $"Audio filter graph could not be built: {filterString}.", ex);
                this.DestroyFilterGraph();
            }
            finally
            {
                this.currentFilterArguments = filterArguments;
                this.appliedFilterString = filterString;
            }
        }
    }
}