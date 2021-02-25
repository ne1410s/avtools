// <copyright file="SubtitleFrame.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Collections.Generic;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents a wrapper for an unmanaged Subtitle frame.
    /// TODO: Only text (ASS and SRT) subtitles are supported currently.
    /// There is no support to bitmap subtitles.
    /// </summary>
    /// <seealso cref="MediaFrame" />
    internal sealed unsafe class SubtitleFrame : MediaFrame
    {
        private readonly object DisposeLock = new object();
        private bool IsDisposed;

        /// <summary>
        /// Initialises a new instance of the <see cref="SubtitleFrame"/> class.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="component">The component.</param>
        internal SubtitleFrame(AVSubtitle* frame, MediaComponent component)
            : base(frame, component)
        {
            // Extract timing information (pts for Subtitles is always in AV_TIME_BASE units)
            this.HasValidStartTime = frame->pts != ffmpeg.AV_NOPTS_VALUE;
            var timeOffset = frame->pts.ToTimeSpan(ffmpeg.AV_TIME_BASE);

            // start_display_time and end_display_time are relative to timeOffset
            this.StartTime = TimeSpan.FromMilliseconds(timeOffset.TotalMilliseconds + frame->start_display_time);
            this.EndTime = TimeSpan.FromMilliseconds(timeOffset.TotalMilliseconds + frame->end_display_time);
            this.Duration = TimeSpan.FromMilliseconds(frame->end_display_time - frame->start_display_time);

            // Extract text strings
            this.TextType = AVSubtitleType.SUBTITLE_NONE;

            for (var i = 0; i < frame->num_rects; i++)
            {
                var rect = frame->rects[i];

                if (rect->type == AVSubtitleType.SUBTITLE_TEXT)
                {
                    if (rect->text == null)
                    {
                        continue;
                    }

                    this.Text.Add(Utilities.PtrToStringUTF8(rect->text));
                    this.TextType = AVSubtitleType.SUBTITLE_TEXT;
                    break;
                }

                if (rect->type == AVSubtitleType.SUBTITLE_ASS)
                {
                    if (rect->ass == null)
                    {
                        continue;
                    }

                    this.Text.Add(Utilities.PtrToStringUTF8(rect->ass));
                    this.TextType = AVSubtitleType.SUBTITLE_ASS;
                    break;
                }

                this.TextType = rect->type;
            }
        }

        /// <summary>
        /// Gets lines of text that the subtitle frame contains.
        /// </summary>
        public IList<string> Text { get; } = new List<string>(16);

        /// <summary>
        /// Gets the type of the text.
        /// </summary>
        /// <value>
        /// The type of the text.
        /// </value>
        public AVSubtitleType TextType { get; }

        /// <summary>
        /// Gets the pointer to the unmanaged subtitle struct.
        /// </summary>
        internal AVSubtitle* Pointer => (AVSubtitle*)this.InternalPointer;

        /// <inheritdoc />
        public override void Dispose()
        {
            lock (this.DisposeLock)
            {
                if (this.IsDisposed)
                {
                    return;
                }

                if (this.InternalPointer != IntPtr.Zero)
                {
                    ReleaseAVSubtitle(this.Pointer);
                }

                this.InternalPointer = IntPtr.Zero;
                this.IsDisposed = true;
            }
        }
    }
}
