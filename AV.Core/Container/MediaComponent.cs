// <copyright file="MediaComponent.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Diagnostics;
    using AV.Core.Primitives;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents a media component of a given media type within a
    /// media container. Derived classes must implement frame handling
    /// logic.
    /// </summary>
    /// <seealso cref="IDisposable" />
    public abstract unsafe class MediaComponent : IDisposable, ILoggingSource
    {
        /// <summary>
        /// Related to issue 94, looks like FFmpeg requires exclusive access when calling avcodec_open2().
        /// </summary>
        private static readonly object CodecLock = new ();

        /// <summary>
        /// The logging handler.
        /// </summary>
        private readonly ILoggingHandler localLoggingHandler;

        /// <summary>
        /// Contains the packets pending to be sent to the decoder.
        /// </summary>
        private readonly PacketQueue packets = new ();

        /// <summary>
        /// The decode packet function.
        /// </summary>
        private readonly Func<MediaFrame> decodePacketFunction;

        /// <summary>
        /// Detects redundant, unmanaged calls to the Dispose method.
        /// </summary>
        private readonly AtomicBoolean localIsDisposed = new (false);

        /// <summary>
        /// Determines if packets have been fed into the codec and frames can be decoded.
        /// </summary>
        private readonly AtomicBoolean localHasCodecPackets = new (false);

        /// <summary>
        /// Holds a reference to the associated input context stream.
        /// </summary>
        private readonly IntPtr localStream;

        /// <summary>
        /// Holds a reference to the Codec Context.
        /// </summary>
        private IntPtr localCodecContext;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaComponent"/> class.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="streamIndex">Index of the stream.</param>
        /// <exception cref="ArgumentNullException">container.</exception>
        /// <exception cref="MediaContainerException">The container exception.</exception>
        protected MediaComponent(MediaContainer container, int streamIndex)
        {
            // Ported from: https://github.com/FFmpeg/FFmpeg/blob/master/fftools/ffplay.c#L2559
            this.Container = container ?? throw new ArgumentNullException(nameof(container));
            this.localLoggingHandler = ((ILoggingSource)this.Container).LoggingHandler;
            this.localCodecContext = new IntPtr(ffmpeg.avcodec_alloc_context3(null));
            RC.Current.Add(this.CodecContext);
            this.StreamIndex = streamIndex;
            this.localStream = new IntPtr(container.InputContext->streams[streamIndex]);
            this.StreamInfo = container.MediaInfo.Streams[streamIndex];

#pragma warning disable CS0618 // Type or member is obsolete

            // Set default codec context options from probed stream
            // var setCodecParamsResult = ffmpeg.avcodec_parameters_to_context(CodecContext, Stream->codecpar);
            var setCodecParamsResult = ffmpeg.avcodec_copy_context(this.CodecContext, Stream->codec);
#pragma warning restore CS0618 // Type or member is obsolete

            if (setCodecParamsResult < 0)
            {
                this.LogWarning(
                    Aspects.Component,
                    $"Could not set codec parameters. Error code: {setCodecParamsResult}");
            }

            // We set the packet timebase in the same timebase as the stream as opposed to the typical AV_TIME_BASE
            if (this is VideoComponent && this.Container.MediaOptions.VideoForcedFps > 0)
            {
                var fpsRational = ffmpeg.av_d2q(this.Container.MediaOptions.VideoForcedFps, 1000000);
                Stream->r_frame_rate = fpsRational;
                CodecContext->pkt_timebase = new AVRational { num = fpsRational.den, den = fpsRational.num };
            }
            else
            {
                CodecContext->pkt_timebase = Stream->time_base;
            }

            // Find the default decoder codec from the stream and set it.
            var defaultCodec = ffmpeg.avcodec_find_decoder(Stream->codecpar->codec_id);
            AVCodec* forcedCodec = null;

            // If set, change the codec to the forced codec.
            if (this.Container.MediaOptions.DecoderCodec.ContainsKey(this.StreamIndex) &&
                string.IsNullOrWhiteSpace(this.Container.MediaOptions.DecoderCodec[this.StreamIndex]) == false)
            {
                var forcedCodecName = this.Container.MediaOptions.DecoderCodec[this.StreamIndex];
                forcedCodec = ffmpeg.avcodec_find_decoder_by_name(forcedCodecName);
                if (forcedCodec == null)
                {
                    this.LogWarning(
                        Aspects.Component,
                        $"COMP {this.MediaType.ToString().ToUpperInvariant()}: " +
                        $"Unable to set decoder codec to '{forcedCodecName}' on stream index {this.StreamIndex}");
                }
            }

            // Check we have a valid codec to open and process the stream.
            if (defaultCodec == null && forcedCodec == null)
            {
                var errorMessage = $"Fatal error. Unable to find suitable decoder for {Stream->codecpar->codec_id}";
                this.CloseComponent();
                throw new MediaContainerException(errorMessage);
            }

            var codecCandidates = new[] { forcedCodec, defaultCodec };
            AVCodec* selectedCodec = null;
            var codecOpenResult = 0;

            foreach (var codec in codecCandidates)
            {
                if (codec == null)
                {
                    continue;
                }

                // Pass default codec stuff to the codec context
                CodecContext->codec_id = codec->id;

                // Process the decoder options
                {
                    var decoderOptions = this.Container.MediaOptions.DecoderParams;

                    // Configure the codec context flags
                    if (decoderOptions.EnableFastDecoding)
                    {
                        CodecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
                    }

                    if (decoderOptions.EnableLowDelayDecoding)
                    {
                        CodecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
                    }

                    // process the low res option
                    if (decoderOptions.LowResolutionIndex != VideoResolutionDivider.Full && codec->max_lowres > 0)
                    {
                        var lowResOption = Math.Min((byte)decoderOptions.LowResolutionIndex, codec->max_lowres)
                            .ToString(CultureInfo.InvariantCulture);
                        decoderOptions.LowResIndexOption = lowResOption;
                    }

                    // Ensure ref counted frames for audio and video decoding
                    if (CodecContext->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO || CodecContext->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    {
                        decoderOptions.RefCountedFrames = "1";
                    }
                }

                // Setup additional settings. The most important one is Threads -- Setting it to 1 decoding is very slow. Setting it to auto
                // decoding is very fast in most scenarios.
                var codecOptions = this.Container.MediaOptions.DecoderParams.GetStreamCodecOptions(Stream->index);

                // Enable Hardware acceleration if requested
                (this as VideoComponent)?.AttachHardwareDevice(container.MediaOptions.VideoHardwareDevice);

                // Open the CodecContext. This requires exclusive FFmpeg access
                lock (CodecLock)
                {
                    var codecOptionsRef = codecOptions.Pointer;
                    codecOpenResult = ffmpeg.avcodec_open2(this.CodecContext, codec, &codecOptionsRef);
                    codecOptions.UpdateReference(codecOptionsRef);
                }

                // Check if the codec opened successfully
                if (codecOpenResult < 0)
                {
                    this.LogWarning(
                        Aspects.Component,
                        $"Unable to open codec '{Utilities.PtrToStringUTF8(codec->name)}' on stream {streamIndex}");

                    continue;
                }

                // If there are any codec options left over from passing them, it means they were not consumed
                var currentEntry = codecOptions.First();
                while (currentEntry?.Key != null)
                {
                    this.LogWarning(
                        Aspects.Component,
                        $"Invalid codec option: '{currentEntry.Key}' for codec '{Utilities.PtrToStringUTF8(codec->name)}', stream {streamIndex}");
                    currentEntry = codecOptions.Next(currentEntry);
                }

                selectedCodec = codec;
                break;
            }

            if (selectedCodec == null)
            {
                this.CloseComponent();
                throw new MediaContainerException($"Unable to find suitable decoder codec for stream {streamIndex}. Error code {codecOpenResult}");
            }

            // Startup done. Set some options.
            Stream->discard = AVDiscard.AVDISCARD_DEFAULT;
            this.MediaType = (MediaType)CodecContext->codec_type;

            switch (this.MediaType)
            {
                case MediaType.Audio:
                case MediaType.Video:
                    this.BufferCountThreshold = 25;
                    this.BufferDurationThreshold = TimeSpan.FromSeconds(1);
                    this.decodePacketFunction = this.DecodeNextAVFrame;
                    break;
                case MediaType.Subtitle:
                    this.BufferCountThreshold = 0;
                    this.BufferDurationThreshold = TimeSpan.Zero;
                    this.decodePacketFunction = this.DecodeNextAVSubtitle;
                    break;
                default:
                    throw new NotSupportedException($"A component of MediaType '{this.MediaType}' is not supported");
            }

            var contentDisposition = this.StreamInfo.Disposition;
            this.IsStillPictures = this.MediaType == MediaType.Video &&
                ((contentDisposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0 ||
                (contentDisposition & ffmpeg.AV_DISPOSITION_STILL_IMAGE) != 0 ||
                (contentDisposition & ffmpeg.AV_DISPOSITION_TIMED_THUMBNAILS) != 0);

            if (this.IsStillPictures)
            {
                this.BufferCountThreshold = 0;
                this.BufferDurationThreshold = TimeSpan.Zero;
            }

            // Compute the start time
            this.StartTime = Stream->start_time == ffmpeg.AV_NOPTS_VALUE
                ? this.Container.MediaInfo.StartTime == TimeSpan.MinValue ? TimeSpan.Zero : this.Container.MediaInfo.StartTime
                : Stream->start_time.ToTimeSpan(Stream->time_base);

            // Compute the duration
            this.Duration = (Stream->duration == ffmpeg.AV_NOPTS_VALUE || Stream->duration <= 0)
                ? this.Container.MediaInfo.Duration
                : Stream->duration.ToTimeSpan(Stream->time_base);

            this.CodecName = Utilities.PtrToStringUTF8(selectedCodec->name);
            this.CodecId = CodecContext->codec_id;
            this.BitRate = CodecContext->bit_rate < 0 ? 0 : CodecContext->bit_rate;

            this.LogDebug(
                Aspects.Component,
                $"{this.MediaType.ToString().ToUpperInvariant()} - Start Time: {this.StartTime.Format()}; Duration: {this.Duration.Format()}");

            // Begin processing with a flush packet
            this.SendFlushPacket();
        }

        /// <inheritdoc />
        ILoggingHandler ILoggingSource.LoggingHandler => this.localLoggingHandler;

        /// <summary>
        /// Gets the pointer to the codec context.
        /// </summary>
        public AVCodecContext* CodecContext => (AVCodecContext*)this.localCodecContext;

        /// <summary>
        /// Gets a pointer to the component's stream.
        /// </summary>
        public AVStream* Stream => (AVStream*)this.localStream;

        /// <summary>
        /// Gets the media container associated with this component.
        /// </summary>
        public MediaContainer Container { get; }

        /// <summary>
        /// Gets the type of the media.
        /// </summary>
        public MediaType MediaType { get; }

        /// <summary>
        /// Gets the index of the associated stream.
        /// </summary>
        public int StreamIndex { get; }

        /// <summary>
        /// Gets the component's stream start timestamp as reported
        /// by the start time of the stream.
        /// Returns TimeSpan.MinValue when unknown.
        /// </summary>
        public TimeSpan StartTime { get; internal set; }

        /// <summary>
        /// Gets the duration of this stream component.
        /// If there is no such information it will return TimeSpan.MinValue.
        /// </summary>
        public TimeSpan Duration { get; internal set; }

        /// <summary>
        /// Gets the component's stream end timestamp as reported
        /// by the start and duration time of the stream.
        /// Returns TimeSpan.MinValue when unknown.
        /// </summary>
        public TimeSpan EndTime => (this.StartTime != TimeSpan.MinValue && this.Duration != TimeSpan.MinValue)
            ? TimeSpan.FromTicks(this.StartTime.Ticks + this.Duration.Ticks)
            : TimeSpan.MinValue;

        /// <summary>
        /// Gets the current length in bytes of the
        /// packet buffer. Limit your Reads to something reasonable before
        /// this becomes too large.
        /// </summary>
        public long BufferLength => this.packets.BufferLength;

        /// <summary>
        /// Gets the duration of the packet buffer.
        /// </summary>
        public TimeSpan BufferDuration => this.packets.GetDuration(this.StreamInfo.TimeBase);

        /// <summary>
        /// Gets the number of packets in the queue.
        /// Decode packets until this number becomes 0.
        /// </summary>
        public int BufferCount => this.packets.Count;

        /// <summary>
        /// Gets the number of packets to cache before <see cref="HasEnoughPackets"/> returns true.
        /// </summary>
        public int BufferCountThreshold { get; }

        /// <summary>
        /// Gets the packet buffer duration threshold before <see cref="HasEnoughPackets"/> returns true.
        /// </summary>
        public TimeSpan BufferDurationThreshold { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the packet queue contains enough packets.
        /// Port of ffplay.c stream_has_enough_packets.
        /// </summary>
        public bool HasEnoughPackets
        {
            get
            {
                // We want to return true when we can't really get a buffer.
                if (this.IsDisposed ||
                    this.BufferCountThreshold <= 0 ||
                    this.IsStillPictures ||
                    (this.Container?.IsReadAborted ?? false) ||
                    (this.Container?.IsAtEndOfStream ?? false))
                {
                    return true;
                }

                // Enough packets means we have a duration of at least 1 second (if the packets report duration)
                // and that we have enough of a packet count depending on the type of media
                return (this.BufferDuration <= TimeSpan.Zero || this.BufferDuration.Ticks >= this.BufferDurationThreshold.Ticks) &&
                    this.BufferCount >= this.BufferCountThreshold;
            }
        }

        /// <summary>
        /// Gets the ID of the codec for this component.
        /// </summary>
        public AVCodecID CodecId { get; }

        /// <summary>
        /// Gets the name of the codec for this component.
        /// </summary>
        public string CodecName { get; }

        /// <summary>
        /// Gets the bit rate of this component as reported by the codec context.
        /// Returns 0 for unknown.
        /// </summary>
        public long BitRate { get; }

        /// <summary>
        /// Gets the stream information.
        /// </summary>
        public StreamInfo StreamInfo { get; }

        /// <summary>
        /// Gets a value indicating whether this component contains still images as opposed to real video frames.
        /// Will always return false for non-video components.
        /// </summary>
        public bool IsStillPictures { get; }

        /// <summary>
        /// Gets whether packets have been fed into the codec and frames can be decoded.
        /// </summary>
        public bool HasPacketsInCodec
        {
            get => this.localHasCodecPackets.Value;
            private set => this.localHasCodecPackets.Value = value;
        }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed
        {
            get => this.localIsDisposed.Value;
            private set => this.localIsDisposed.Value = value;
        }

        /// <summary>
        /// Gets or sets the last frame PTS.
        /// </summary>
        internal long? LastFramePts { get; set; }

        /// <summary>
        /// Clears the pending and sent Packet Queues releasing all memory held by those packets.
        /// Additionally it flushes the codec buffered packets.
        /// </summary>
        /// <param name="flushBuffers">if set to <c>true</c> flush codec buffers.</param>
        public void ClearQueuedPackets(bool flushBuffers)
        {
            // Release packets that are already in the queue.
            this.packets.Clear();

            if (flushBuffers)
            {
                this.FlushCodecBuffers();
            }

            this.Container.Components.ProcessPacketQueueChanges(PacketQueueOp.Clear, null, this.MediaType);
        }

        /// <summary>
        /// Sends a special kind of packet (an empty/null packet)
        /// that tells the decoder to refresh the attached picture or enter draining mode.
        /// This is a port of packet_queue_put_nullpacket.
        /// </summary>
        public void SendEmptyPacket()
        {
            var packet = MediaPacket.CreateEmptyPacket(Stream->index);
            this.SendPacket(packet);
        }

        /// <summary>
        /// Pushes a packet into the decoding Packet Queue
        /// and processes the packet in order to try to decode
        /// 1 or more frames.
        /// </summary>
        /// <param name="packet">The packet.</param>
        public void SendPacket(MediaPacket packet)
        {
            if (packet == null)
            {
                this.SendEmptyPacket();
                return;
            }

            this.packets.Push(packet);
            this.Container.Components.ProcessPacketQueueChanges(PacketQueueOp.Queued, packet, this.MediaType);
        }

        /// <summary>
        /// Feeds the decoder buffer and tries to return the next available frame.
        /// </summary>
        /// <returns>The received Media Frame. It is null if no frame could be retrieved.</returns>
        public MediaFrame ReceiveNextFrame()
        {
            var frame = this.decodePacketFunction?.Invoke();

            // Check if we need to update the duration of this component.
            // This means we have decoded more frames than what was initially reported by the container.
            if (frame != null && this.Container.IsStreamSeekable && this.EndTime != TimeSpan.MinValue &&
                frame.HasValidStartTime && frame.EndTime.Ticks > this.EndTime.Ticks)
            {
                this.Duration = TimeSpan.FromTicks(frame.EndTime.Ticks - this.StartTime.Ticks);
            }

            return frame;
        }

        /// <summary>
        /// Converts decoded, raw frame data in the frame source into a a usable frame. <br />
        /// The process includes performing picture, samples or text conversions
        /// so that the decoded source frame data is easily usable in multimedia applications.
        /// </summary>
        /// <param name="input">The source frame to use as an input.</param>
        /// <param name="output">The target frame that will be updated with the source frame. If null is passed the frame will be instantiated.</param>
        /// <param name="previousBlock">The previous block from which to derive information in case the current frame contains invalid data.</param>
        /// <returns>
        /// Returns true of the operation succeeded. False otherwise.
        /// </returns>
        public abstract bool MaterializeFrame(MediaFrame input, ref MediaBlock output, MediaBlock previousBlock);

        /// <inheritdoc />
        public void Dispose() => this.Dispose(true);

        /// <summary>
        /// Creates a frame source object given the raw FFmpeg AVFrame or AVSubtitle reference.
        /// </summary>
        /// <param name="framePointer">The raw FFmpeg pointer.</param>
        /// <returns>The media frame.</returns>
        protected abstract MediaFrame CreateFrameSource(IntPtr framePointer);

        /// <summary>
        /// Releases the existing codec context and clears and disposes the packet queues.
        /// </summary>
        protected void CloseComponent()
        {
            if (this.localCodecContext == IntPtr.Zero)
            {
                return;
            }

            RC.Current.Remove(this.localCodecContext);
            var codecContext = this.CodecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            this.localCodecContext = IntPtr.Zero;

            // free all the pending and sent packets
            this.ClearQueuedPackets(true);
            this.packets.Dispose();
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool alsoManaged)
        {
            lock (CodecLock)
            {
                if (this.IsDisposed)
                {
                    return;
                }

                this.CloseComponent();
                this.IsDisposed = true;
            }
        }

        /// <summary>
        /// Sends a special kind of packet (a flush packet)
        /// that tells the decoder to flush it internal buffers
        /// This an encapsulation of flush_pkt.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendFlushPacket()
        {
            var packet = MediaPacket.CreateFlushPacket(Stream->index);
            this.SendPacket(packet);
        }

        /// <summary>
        /// Flushes the codec buffers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushCodecBuffers()
        {
            if (this.localCodecContext != IntPtr.Zero)
            {
                ffmpeg.avcodec_flush_buffers(this.CodecContext);
            }

            this.HasPacketsInCodec = false;
        }

        /// <summary>
        /// Feeds the packets to decoder.
        /// </summary>
        /// <param name="fillDecoderBuffer">if set to <c>true</c> fills the decoder buffer with packets.</param>
        /// <returns>The number of packets fed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FeedPacketsToDecoder(bool fillDecoderBuffer)
        {
            var packetCount = 0;
            int sendPacketResult;

            while (this.packets.Count > 0)
            {
                var packet = this.packets.Peek();
                if (packet.IsFlushPacket)
                {
                    this.FlushCodecBuffers();

                    // Dequeue the flush packet. We don't add to the decode
                    // count or call the OnPacketDequeued callback because the size is 0
                    packet = this.packets.Dequeue();

                    packet.Dispose();
                    continue;
                }

                // Send packet to the decoder but prevent null packets to be sent to it
                // Null packets have never been detected but it's just a safeguard
                sendPacketResult = packet.SafePointer != IntPtr.Zero
                    ? ffmpeg.avcodec_send_packet(this.CodecContext, packet.Pointer) : -ffmpeg.EINVAL;

                // EAGAIN means we have filled the decoder buffer
                if (sendPacketResult != -ffmpeg.EAGAIN)
                {
                    // Dequeue the packet and release it.
                    packet = this.packets.Dequeue();
                    this.Container.Components.ProcessPacketQueueChanges(PacketQueueOp.Dequeued, packet, this.MediaType);

                    packet.Dispose();
                    packetCount++;
                }

                if (sendPacketResult >= 0)
                {
                    this.HasPacketsInCodec = true;
                }

                if (fillDecoderBuffer && sendPacketResult >= 0)
                {
                    continue;
                }

                break;
            }

            return packetCount;
        }

        /// <summary>
        /// Receives the next available frame from decoder.
        /// </summary>
        /// <param name="receiveFrameResult">The receive frame result.</param>
        /// <returns>The frame or null if no frames could be decoded.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaFrame ReceiveFrameFromDecoder(out int receiveFrameResult)
        {
            MediaFrame managedFrame = null;
            var outputFrame = MediaFrame.CreateAVFrame();
            receiveFrameResult = ffmpeg.avcodec_receive_frame(this.CodecContext, outputFrame);

            if (receiveFrameResult >= 0)
            {
                managedFrame = this.CreateFrameSource(new IntPtr(outputFrame));
            }

            if (managedFrame == null)
            {
                MediaFrame.ReleaseAVFrame(outputFrame);
            }

            if (receiveFrameResult == ffmpeg.AVERROR_EOF)
            {
                this.FlushCodecBuffers();
            }

            if (receiveFrameResult == -ffmpeg.EAGAIN)
            {
                this.HasPacketsInCodec = false;
            }

            return managedFrame;
        }

        /// <summary>
        /// Decodes the next Audio or Video frame.
        /// Reference: https://www.ffmpeg.org/doxygen/4.0/group__lavc__encdec.html.
        /// </summary>
        /// <returns>A decoder result containing the decoder frames (if any).</returns>
        private MediaFrame DecodeNextAVFrame()
        {
            var frame = this.ReceiveFrameFromDecoder(out var receiveFrameResult);
            if (frame == null)
            {
                this.FeedPacketsToDecoder(false);
                frame = this.ReceiveFrameFromDecoder(out receiveFrameResult);
            }

            while (frame == null && this.FeedPacketsToDecoder(true) > 0)
            {
                frame = this.ReceiveFrameFromDecoder(out receiveFrameResult);
                if (receiveFrameResult < 0)
                {
                    break;
                }
            }

            if (frame == null || this.Container.Components.OnFrameDecoded == null)
            {
                return frame;
            }

            if (this.MediaType == MediaType.Audio && frame is AudioFrame audioFrame)
            {
                this.Container.Components.OnFrameDecoded?.Invoke((IntPtr)audioFrame.Pointer, this.MediaType);
            }
            else if (this.MediaType == MediaType.Video && frame is VideoFrame videoFrame)
            {
                this.Container.Components.OnFrameDecoded?.Invoke((IntPtr)videoFrame.Pointer, this.MediaType);
            }

            return frame;
        }

        /// <summary>
        /// Decodes the next subtitle frame.
        /// </summary>
        /// <returns>The managed frame.</returns>
        private MediaFrame DecodeNextAVSubtitle()
        {
            // For subtitles we use the old API (new API send_packet/receive_frame) is not yet available
            // We first try to flush anything we've already sent by using an empty packet.
            MediaFrame managedFrame = null;
            var packet = MediaPacket.CreateEmptyPacket(Stream->index);
            var gotFrame = 0;
            var outputFrame = MediaFrame.CreateAVSubtitle();
            var receiveFrameResult = ffmpeg.avcodec_decode_subtitle2(this.CodecContext, outputFrame, &gotFrame, packet.Pointer);

            // If we don't get a frame from flushing. Feed the packet into the decoder and try getting a frame.
            if (gotFrame == 0)
            {
                packet.Dispose();

                // Dequeue the packet and try to decode with it.
                packet = this.packets.Dequeue();

                if (packet != null)
                {
                    this.Container.Components.ProcessPacketQueueChanges(PacketQueueOp.Dequeued, packet, this.MediaType);
                    receiveFrameResult = ffmpeg.avcodec_decode_subtitle2(this.CodecContext, outputFrame, &gotFrame, packet.Pointer);
                }
            }

            // If we got a frame, turn into a managed frame
            if (gotFrame != 0)
            {
                this.Container.Components.OnSubtitleDecoded?.Invoke((IntPtr)outputFrame);
                managedFrame = this.CreateFrameSource((IntPtr)outputFrame);
            }

            // Free the packet if we have dequeued it
            packet?.Dispose();

            // deallocate the subtitle frame if we did not associate it with a managed frame.
            if (managedFrame == null)
            {
                MediaFrame.ReleaseAVSubtitle(outputFrame);
            }

            if (receiveFrameResult < 0)
            {
                this.HasPacketsInCodec = false;
            }

            return managedFrame;
        }
    }
}
