// <copyright file="AudioFrame.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using AV.Core.Common;
    using FFmpeg.AutoGen;

    /// <inheritdoc />
    /// <summary>
    /// Represents a wrapper from an unmanaged FFmpeg audio frame.
    /// </summary>
    /// <seealso cref="MediaFrame" />
    /// <seealso cref="IDisposable" />
    internal sealed unsafe class AudioFrame : MediaFrame
    {
        private readonly object disposeLock = new object();
        private bool isDisposed;

        /// <summary>
        /// Initialises a new instance of the <see cref="AudioFrame"/> class.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="component">The component.</param>
        internal AudioFrame(AVFrame* frame, MediaComponent component)
            : base(frame, component, MediaType.Audio)
        {
            // Compute the start time.
            frame->pts = frame->best_effort_timestamp;
            this.HasValidStartTime = frame->pts != ffmpeg.AV_NOPTS_VALUE;
            this.StartTime = frame->pts == ffmpeg.AV_NOPTS_VALUE ?
                TimeSpan.FromTicks(0) :
                TimeSpan.FromTicks(frame->pts.ToTimeSpan(this.StreamTimeBase).Ticks);

            // Compute the audio frame duration
            this.Duration = frame->pkt_duration > 0 ?
                frame->pkt_duration.ToTimeSpan(this.StreamTimeBase) :
                TimeSpan.FromTicks(Convert.ToInt64(TimeSpan.TicksPerMillisecond * 1000d * frame->nb_samples / frame->sample_rate));

            // Compute the audio frame end time
            this.EndTime = TimeSpan.FromTicks(this.StartTime.Ticks + this.Duration.Ticks);
        }

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
