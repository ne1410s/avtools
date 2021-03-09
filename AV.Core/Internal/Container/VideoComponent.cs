// <copyright file="VideoComponent.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Container
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Runtime.CompilerServices;
    using AV.Core.Internal.Common;
    using AV.Core.Internal.FFmpeg;
    using AV.Core.Internal.Primitives;
    using AV.Core.Internal.Utilities;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Performs video picture decoding, scaling and extraction logic.
    /// </summary>
    /// <seealso cref="MediaComponent" />
    internal sealed unsafe class VideoComponent : MediaComponent
    {
        private readonly AVRational baseFrameRateQ;

        private string appliedFilterString;
        private string currentFilterArguments;

        private SwsContext* scaler = null;
        private AVFilterGraph* filterGraph = null;
        private AVFilterContext* sourceFilter = null;
        private AVFilterContext* sinkFilter = null;
        private AVFilterInOut* sinkInput = null;
        private AVFilterInOut* sourceOutput = null;
        private AVBufferRef* hardwareDeviceContext = null;

        /// <summary>
        /// Initialises a new instance of the <see cref="VideoComponent"/> class.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="streamIndex">Index of the stream.</param>
        internal VideoComponent(MediaContainer container, int streamIndex)
            : base(container, streamIndex)
        {
            this.baseFrameRateQ = Stream->r_frame_rate;

            if (this.baseFrameRateQ.den == 0 || this.baseFrameRateQ.num == 0)
            {
                this.baseFrameRateQ = ffmpeg.av_guess_frame_rate(container.InputContext, this.Stream, null);
            }

            if (this.baseFrameRateQ.den == 0 || this.baseFrameRateQ.num == 0)
            {
                //TODO: Warn
                ////$"{nameof(VideoComponent)} was unable to extract valid frame rate. Will use 25fps (40ms)");

                this.baseFrameRateQ.num = 25;
                this.baseFrameRateQ.den = 1;
            }

            this.BaseFrameRate = this.baseFrameRateQ.ToDouble();
            this.FrameCount = this.Stream->nb_frames > 0
                ? this.Stream->nb_frames
                : (long)Math.Ceiling(this.BaseFrameRate * this.Duration.TotalSeconds);

            if (Stream->avg_frame_rate.den > 0 && Stream->avg_frame_rate.num > 0)
            {
                this.AverageFrameRate = Stream->avg_frame_rate.ToDouble();
            }
            else
            {
                this.AverageFrameRate = this.BaseFrameRate;
            }

            // Specify frame width and height
            this.FrameWidth = CodecContext->width;
            this.FrameHeight = CodecContext->height;

            // Retrieve Matrix Rotation
            var displayMatrixRef = ffmpeg.av_stream_get_side_data(this.Stream, AVPacketSideDataType.AV_PKT_DATA_DISPLAYMATRIX, null);
            this.DisplayRotation = ComputeRotation(displayMatrixRef);

            var aspectRatio = ffmpeg.av_d2q((double)this.FrameWidth / this.FrameHeight, int.MaxValue);
            this.DisplayAspectWidth = aspectRatio.num;
            this.DisplayAspectHeight = aspectRatio.den;
        }

        /// <summary>
        /// Gets or sets the video scaler flags used to perform color space
        /// conversion (if needed). Point / nearest-neighbor is the default and
        /// it is the cheapest. This is by design as we don't change the
        /// dimensions of the image. We only do color conversion.
        /// </summary>
        public static int ScalerFlags { get; internal set; } = ffmpeg.SWS_POINT;

        /// <summary>
        /// Gets the total number of frames.
        /// </summary>
        public long FrameCount { get; }

        /// <summary>
        /// Gets the base frame rate as reported by the stream component.
        /// All discrete timestamps can be represented in this frame rate.
        /// </summary>
        public double BaseFrameRate { get; }

        /// <summary>
        /// Gets the stream's average frame rate.
        /// </summary>
        public double AverageFrameRate { get; }

        /// <summary>
        /// Gets the width of the picture frame.
        /// </summary>
        public int FrameWidth { get; }

        /// <summary>
        /// Gets the height of the picture frame.
        /// </summary>
        public int FrameHeight { get; }

        /// <summary>
        /// Gets the display rotation.
        /// </summary>
        public double DisplayRotation { get; }

        /// <summary>
        /// Gets the display aspect width.
        /// This is NOT the pixel aspect width.
        /// </summary>
        public int DisplayAspectWidth { get; }

        /// <summary>
        /// Gets the display aspect height.
        /// This si NOT the pixel aspect height.
        /// </summary>
        public int DisplayAspectHeight { get; }

        /// <summary>
        /// Gets the hardware accelerator.
        /// </summary>
        public HardwareAccelerator HardwareAccelerator { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this component is using hardware
        /// assisted decoding.
        /// </summary>
        public bool IsUsingHardwareDecoding { get; private set; }

        /// <summary>
        /// Gets access to VideoFilter string of the container's MediaOptions.
        /// </summary>
        private string FilterString => this.Container?.MediaOptions?.VideoFilter;

        /// <summary>
        /// Attaches a hardware accelerator to this video component.
        /// </summary>
        /// <param name="selectedConfig">The selected configuration.</param>
        /// <returns>
        /// Whether or not the hardware accelerator was attached.
        /// </returns>
        public bool AttachHardwareDevice(HardwareDeviceInfo selectedConfig)
        {
            // Check for no device selection
            if (selectedConfig == null)
            {
                return false;
            }

            try
            {
                var accelerator = new HardwareAccelerator(this, selectedConfig);

                AVBufferRef* devContextRef = null;
                var initResultCode = ffmpeg.av_hwdevice_ctx_create(&devContextRef, accelerator.DeviceType, null, null, 0);
                if (initResultCode < 0)
                {
                    throw new MediaContainerException($"Unable to initialize hardware context for device {accelerator.Name}");
                }

                this.hardwareDeviceContext = devContextRef;
                this.HardwareAccelerator = accelerator;
                CodecContext->hw_device_ctx = ffmpeg.av_buffer_ref(this.hardwareDeviceContext);
                CodecContext->get_format = accelerator.GetFormatCallback;

                return true;
            }
            catch (Exception ex)
            {
                //TODO: Error
                ////"Could not attach hardware decoder.", ex);
                return false;
            }
        }

        /// <summary>
        /// Releases the hardware device context.
        /// </summary>
        public void ReleaseHardwareDevice()
        {
            if (this.hardwareDeviceContext == null)
            {
                return;
            }

            var context = this.hardwareDeviceContext;
            ffmpeg.av_buffer_unref(&context);
            this.hardwareDeviceContext = null;
            this.HardwareAccelerator = null;
        }

        /// <inheritdoc />
        public override bool MaterializeFrame(MediaFrame input, ref MediaBlock output, MediaBlock previousBlock)
        {
            if (output == null)
            {
                output = new VideoBlock();
            }

            if (input is VideoFrame == false || output is VideoBlock == false)
            {
                throw new ArgumentNullException($"{nameof(input)} and {nameof(output)} are either null or not of a compatible media type '{this.MediaType}'");
            }

            var source = (VideoFrame)input;
            var target = (VideoBlock)output;

            // Allow rescale dimensions to be passed
            var srcWidth = source.Pointer->width;
            var srcHeight = source.Pointer->height;
            var srcAspect = srcWidth / (double)srcHeight;
            var targetSize = new Size
            {
                Width = target.PixelWidth != 0
                    ? target.PixelWidth
                    : target.PixelHeight != 0
                        ? (int)Math.Round(target.PixelHeight * srcAspect)
                        : srcWidth,
                Height = target.PixelHeight != 0
                    ? target.PixelHeight
                    : target.PixelWidth != 0
                        ? (int)Math.Round(target.PixelWidth / srcAspect)
                        : srcHeight,
            };

            // Retrieve a suitable scaler or create it on the fly
            var newScaler = ffmpeg.sws_getCachedContext(
                this.scaler,
                srcWidth,
                srcHeight,
                NormalizePixelFormat(source.Pointer),
                targetSize.Width,
                targetSize.Height,
                Constants.VideoPixelFormat,
                ScalerFlags,
                null,
                null,
                null);

            // if it's the first time we set the scaler, simply assign it.
            if (this.scaler == null)
            {
                this.scaler = newScaler;
                RC.Current.Add(this.scaler);
            }

            // Reassign to the new scaler and remove the reference to the
            // existing one. The get cached context function automatically frees
            // the existing scaler.
            if (this.scaler != newScaler)
            {
                RC.Current.Remove(this.scaler);
                this.scaler = newScaler;
            }

            // Perform scaling and save the data to our unmanaged buffer pointer
            if (target.Allocate(Constants.VideoPixelFormat, targetSize)
                && target.TryAcquireWriterLock(out var writeLock))
            {
                using (writeLock)
                {
                    var targetStride = new[] { target.PictureBufferStride };
                    var targetScan = default(byte_ptrArray8);
                    targetScan[0] = (byte*)target.Buffer;

                    // The scaling is done here
                    var outputHeight = ffmpeg.sws_scale(
                        this.scaler,
                        source.Pointer->data,
                        source.Pointer->linesize,
                        0,
                        srcHeight,
                        targetScan,
                        targetStride);

                    if (outputHeight <= 0)
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }

            // After scaling, we need to copy and guess some of the block
            // properties. Flag the block if we have to
            target.IsStartTimeGuessed = source.HasValidStartTime == false;
            target.PresentationTime = source.PresentationTime;

            // Try fix start, duration and End time if we don't have valid data
            if (source.HasValidStartTime == false && previousBlock != null)
            {
                // Get timing information from the previous block
                target.StartTime = TimeSpan.FromTicks(previousBlock.EndTime.Ticks + 1);
                target.Duration = source.Duration.Ticks > 0 ? source.Duration : previousBlock.Duration;
                target.EndTime = TimeSpan.FromTicks(target.StartTime.Ticks + target.Duration.Ticks);

                // Guess picture number and SMTPE
                var frameRate = ffmpeg.av_guess_frame_rate(this.Container.InputContext, this.Stream, source.Pointer);
                target.DisplayPictureNumber = MediaUtilities.ComputePictureNumber(this.StartTime, target.StartTime, frameRate);
                target.SmtpeTimeCode = MediaUtilities.ComputeSmtpeTimeCode(target.DisplayPictureNumber, frameRate);
            }
            else
            {
                // We set the target properties directly from the source
                target.StartTime = source.StartTime;
                target.Duration = source.Duration;
                target.EndTime = source.EndTime;

                // Copy picture number and SMTPE
                target.DisplayPictureNumber = source.DisplayPictureNumber;
                target.SmtpeTimeCode = source.SmtpeTimeCode;
            }

            // Fill out other properties
            target.IsHardwareFrame = source.IsHardwareFrame;
            target.HardwareAcceleratorName = source.HardwareAcceleratorName;
            target.CompressedSize = source.CompressedSize;
            target.CodedPictureNumber = source.CodedPictureNumber;
            target.StreamIndex = source.StreamIndex;
            target.PictureType = source.PictureType;

            // Process the aspect ratio
            var aspectRatio = ffmpeg.av_guess_sample_aspect_ratio(this.Container.InputContext, this.Stream, source.Pointer);
            if (aspectRatio.num == 0 || aspectRatio.den == 0)
            {
                target.PixelAspectWidth = 1;
                target.PixelAspectHeight = 1;
            }
            else
            {
                target.PixelAspectWidth = aspectRatio.num;
                target.PixelAspectHeight = aspectRatio.den;
            }

            return true;
        }

        /// <inheritdoc />
        protected override MediaFrame CreateFrameSource(IntPtr framePointer)
        {
            // Validate the video frame
            var frame = (AVFrame*)framePointer;

            if (framePointer == IntPtr.Zero || frame->width <= 0 || frame->height <= 0)
            {
                return null;
            }

            // Move the frame from hardware (GPU) memory to RAM (CPU)
            if (this.HardwareAccelerator != null)
            {
                frame = this.HardwareAccelerator.ExchangeFrame(this.CodecContext, frame, out var isHardwareFrame);
                this.IsUsingHardwareDecoding = isHardwareFrame;
            }

            // Init the filter graph for the frame
            this.InitializeFilterGraph(frame);

            AVFrame* outputFrame;

            // Changes in the filter graph can be applied by calling the ChangeMedia command
            if (this.filterGraph != null)
            {
                // Allocate the output frame
                outputFrame = MediaFrame.CloneAVFrame(frame);

                var result = ffmpeg.av_buffersrc_add_frame(this.sourceFilter, outputFrame);
                while (result >= 0)
                {
                    result = ffmpeg.av_buffersink_get_frame_flags(this.sinkFilter, outputFrame, 0);
                }

                if (outputFrame->width <= 0 || outputFrame->height <= 0)
                {
                    // If we don't have a valid output frame simply release it
                    // and return the original input frame
                    MediaFrame.ReleaseAVFrame(outputFrame);
                    outputFrame = frame;
                }
                else
                {
                    // the output frame is the new valid frame (output frame).
                    // therefore, we need to release the original
                    MediaFrame.ReleaseAVFrame(frame);
                }
            }
            else
            {
                outputFrame = frame;
            }

            // Check if the output frame is valid
            if (outputFrame->width <= 0 || outputFrame->height <= 0)
            {
                return null;
            }

            // Create the frame holder object and return it.
            return new VideoFrame(outputFrame, this);
        }

        /// <inheritdoc />
        protected override void Dispose(bool alsoManaged)
        {
            if (this.scaler != null)
            {
                RC.Current.Remove(this.scaler);
                ffmpeg.sws_freeContext(this.scaler);
                this.scaler = null;
            }

            this.DestroyFilterGraph();
            this.ReleaseHardwareDevice();
            base.Dispose(alsoManaged);
        }

        /// <summary>
        /// Gets the pixel format replacing deprecated pixel formats.
        /// AV_PIX_FMT_YUVJ.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>A normalized pixel format.</returns>
        private static AVPixelFormat NormalizePixelFormat(AVFrame* frame)
        {
            var currentFormat = (AVPixelFormat)frame->format;
            return currentFormat switch
            {
                AVPixelFormat.AV_PIX_FMT_YUVJ411P => AVPixelFormat.AV_PIX_FMT_YUV411P,
                AVPixelFormat.AV_PIX_FMT_YUVJ420P => AVPixelFormat.AV_PIX_FMT_YUV420P,
                AVPixelFormat.AV_PIX_FMT_YUVJ422P => AVPixelFormat.AV_PIX_FMT_YUV422P,
                AVPixelFormat.AV_PIX_FMT_YUVJ440P => AVPixelFormat.AV_PIX_FMT_YUV440P,
                AVPixelFormat.AV_PIX_FMT_YUVJ444P => AVPixelFormat.AV_PIX_FMT_YUV444P,
                _ => currentFormat,
            };
        }

        /// <summary>
        /// Computes the Frame rotation property from side data.
        /// </summary>
        /// <param name="matrixArrayRef">The matrix array reference.</param>
        /// <returns>The angle to rotate.</returns>
        private static double ComputeRotation(byte* matrixArrayRef)
        {
            const int displayMatrixLength = 9;

            if (matrixArrayRef == null)
            {
                return 0;
            }

            var matrix = new List<int>(displayMatrixLength);

            double rotation;
            var scale = new double[2];

            for (var i = 0; i < displayMatrixLength * sizeof(int); i += sizeof(int))
            {
                matrix.Add(BitConverter.ToInt32(
                    new[]
                    {
                        matrixArrayRef[i + 0],
                        matrixArrayRef[i + 1],
                        matrixArrayRef[i + 2],
                        matrixArrayRef[i + 3],
                    },
                    0));
            }

            // port of av_display_rotation_get
            {
                scale[0] = ComputeHypotenuse(Convert.ToDouble(matrix[0]), Convert.ToDouble(matrix[3]));
                scale[1] = ComputeHypotenuse(Convert.ToDouble(matrix[1]), Convert.ToDouble(matrix[4]));

                scale[0] = Math.Abs(scale[0]) <= double.Epsilon ? 1 : scale[0];
                scale[1] = Math.Abs(scale[1]) <= double.Epsilon ? 1 : scale[1];

                rotation = Math.Atan2(
                    Convert.ToDouble(matrix[1]) / scale[1],
                    Convert.ToDouble(matrix[0]) / scale[0]) * 180 / Math.PI;
            }

            // port of double get_rotation(AVStream *st)
            {
                rotation -= 360 * Math.Floor((rotation / 360) + (0.9 / 360));
            }

            return rotation;
        }

        /// <summary>
        /// Computes the hypotenuse (right-angle triangles only).
        /// </summary>
        /// <param name="a">a.</param>
        /// <param name="b">The b.</param>
        /// <returns>The length of the hypotenuse.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ComputeHypotenuse(double a, double b) => Math.Sqrt((a * a) + (b * b));

        /// <summary>
        /// Computes the frame filter arguments that are appropriate for the
        /// video filtering chain.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The base filter arguments.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ComputeFilterArguments(AVFrame* frame)
        {
            var arguments =
                 $"video_size={frame->width}x{frame->height}:pix_fmt={frame->format}:" +
                 $"time_base={Stream->time_base.num}/{Stream->time_base.den}:" +
                 $"pixel_aspect={CodecContext->sample_aspect_ratio.num}/{Math.Max(CodecContext->sample_aspect_ratio.den, 1)}";

            if (this.baseFrameRateQ.num != 0 && this.baseFrameRateQ.den != 0)
            {
                arguments = $"{arguments}:frame_rate={this.baseFrameRateQ.num}/{this.baseFrameRateQ.den}";
            }

            return arguments;
        }

        /// <summary>
        /// If necessary, disposes the existing filter graph and creates a new
        /// one based on the frame arguments.
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
            /*
             * References:
             * http://libav-users.943685.n4.nabble.com/Libav-user-yadif-deinterlace-how-td3606561.html
             * https://www.ffmpeg.org/doxygen/trunk/filtering_8c-source.html
             * https://raw.githubusercontent.com/FFmpeg/FFmpeg/release/3.2/ffplay.c
             */

            const string SourceFilterName = "buffer";
            const string SourceFilterInstance = "video_buffer";
            const string SinkFilterName = "buffersink";
            const string SinkFilterInstance = "video_buffersink";

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
                // Get a couple of pointers for source and sink buffers
                AVFilterContext* sourceFilterRef = null;
                AVFilterContext* sinkFilterRef = null;

                // Create the source filter
                var result = ffmpeg.avfilter_graph_create_filter(
                    &sourceFilterRef, ffmpeg.avfilter_get_by_name(SourceFilterName), SourceFilterInstance, filterArguments, null, this.filterGraph);

                // Check filter creation
                if (result != 0)
                {
                    throw new MediaContainerException(
                        $"{nameof(ffmpeg.avfilter_graph_create_filter)} ({SourceFilterName}) failed. " +
                        $"Error {result}: {FFInterop.DecodeMessage(result)}");
                }

                // Create the sink filter
                result = ffmpeg.avfilter_graph_create_filter(
                    &sinkFilterRef, ffmpeg.avfilter_get_by_name(SinkFilterName), SinkFilterInstance, null, null, this.filterGraph);

                // Check filter creation
                if (result != 0)
                {
                    throw new MediaContainerException(
                        $"{nameof(ffmpeg.avfilter_graph_create_filter)} ({SinkFilterName}) failed. " +
                        $"Error {result}: {FFInterop.DecodeMessage(result)}");
                }

                // Save the filter references
                this.sourceFilter = sourceFilterRef;
                this.sinkFilter = sinkFilterRef;

                // TODO: from ffplay, ffmpeg.av_opt_set_int_list(sink, "pixel_formats", (byte*)&f0, 1, ffmpeg.AV_OPT_SEARCH_CHILDREN)
                if (string.IsNullOrWhiteSpace(filterString))
                {
                    result = ffmpeg.avfilter_link(this.sourceFilter, 0, this.sinkFilter, 0);
                    if (result != 0)
                    {
                        throw new MediaContainerException(
                            $"{nameof(ffmpeg.avfilter_link)} failed. " +
                            $"Error {result}: {FFInterop.DecodeMessage(result)}");
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
                //TODO: Error
                ////$"Video filter graph could not be built: {filterString}.", ex);
                this.DestroyFilterGraph();
            }
            finally
            {
                this.currentFilterArguments = filterArguments;
                this.appliedFilterString = filterString;
            }
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
    }
}
