// <copyright file="VideoFrame.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using AV.Core.Common;
    using AV.Core.Utilities;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents a wrapper for an unmanaged ffmpeg video frame.
    /// </summary>
    /// <seealso cref="MediaFrame" />
    public sealed unsafe class VideoFrame : MediaFrame
    {
        private readonly object disposeLock = new object();
        private bool isDisposed;

        /// <summary>
        /// Initialises a new instance of the <see cref="VideoFrame"/> class.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="component">The video component.</param>
        internal VideoFrame(AVFrame* frame, VideoComponent component)
            : base(frame, component, MediaType.Video)
        {
            var frameRate = ffmpeg.av_guess_frame_rate(component.Container.InputContext, component.Stream, frame);
            var frameTimeBase = new AVRational { num = frameRate.den, den = frameRate.num };
            var repeatFactor = 1d + (0.5d * frame->repeat_pict);

            this.Duration = frame->pkt_duration <= 0 ?
                repeatFactor.ToTimeSpan(frameTimeBase) :
                Convert.ToInt64(repeatFactor * frame->pkt_duration).ToTimeSpan(this.StreamTimeBase);

            // for video frames, we always get the best effort timestamp as dts
            // and pts might contain different times.
            frame->pts = frame->best_effort_timestamp;
            var previousFramePts = component.LastFramePts;
            component.LastFramePts = frame->pts;

            if (previousFramePts != null && previousFramePts.Value == frame->pts)
            {
                this.HasValidStartTime = false;
            }
            else
            {
                this.HasValidStartTime = frame->pts != ffmpeg.AV_NOPTS_VALUE;
            }

            this.StartTime = frame->pts == ffmpeg.AV_NOPTS_VALUE ?
                TimeSpan.FromTicks(0) :
                frame->pts.ToTimeSpan(this.StreamTimeBase);

            this.EndTime = TimeSpan.FromTicks(this.StartTime.Ticks + this.Duration.Ticks);

            // Picture Type, Number and SMTPE TimeCode
            this.PictureType = frame->pict_type;
            this.DisplayPictureNumber = frame->display_picture_number == 0 ?
                MediaUtilities.ComputePictureNumber(component.StartTime, this.StartTime, frameRate) :
                frame->display_picture_number;

            this.CodedPictureNumber = frame->coded_picture_number;
            this.SmtpeTimeCode = MediaUtilities.ComputeSmtpeTimeCode(this.DisplayPictureNumber, frameRate);
            this.IsHardwareFrame = component.IsUsingHardwareDecoding;
            this.HardwareAcceleratorName = component.HardwareAccelerator?.Name;
        }

        /// <summary>
        /// Gets the display picture number (frame number).
        /// If not set by the decoder, this attempts to obtain it by dividing
        /// the start time by the frame duration.
        /// </summary>
        public long DisplayPictureNumber { get; }

        /// <summary>
        /// Gets the video picture type. I frames are key frames.
        /// </summary>
        public AVPictureType PictureType { get; }

        /// <summary>
        /// Gets the coded picture number set by the decoder.
        /// </summary>
        public long CodedPictureNumber { get; }

        /// <summary>
        /// Gets the SMTPE time code.
        /// </summary>
        public string SmtpeTimeCode { get; }

        /// <summary>
        /// Gets a value indicating whether this frame was decoded in a hardware
        /// context.
        /// </summary>
        public bool IsHardwareFrame { get; }

        /// <summary>
        /// Gets the name of the hardware decoder if the frame was decoded in a
        /// hardware context.
        /// </summary>
        public string HardwareAcceleratorName { get; }

        /// <summary>
        /// Gets the pointer to the unmanaged frame.
        /// </summary>
        internal AVFrame* Pointer => (AVFrame*)this.InternalPointer;

        /// <inheritdoc />
        public override void Dispose()
        {
            lock (this.disposeLock)
            {
                if (this.isDisposed)
                {
                    return;
                }

                if (this.InternalPointer != IntPtr.Zero)
                {
                    ReleaseAVFrame(this.Pointer);
                }

                this.InternalPointer = IntPtr.Zero;
                this.isDisposed = true;
            }
        }
    }
}
