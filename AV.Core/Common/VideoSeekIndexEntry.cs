﻿// <copyright file="VideoSeekIndexEntry.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System;
    using AV.Core.Container;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents a seek entry to a position within the stream.
    /// </summary>
    public sealed class VideoSeekIndexEntry
        : IComparable<VideoSeekIndexEntry>, IComparable<TimeSpan>, IEquatable<VideoSeekIndexEntry>
    {
        private static readonly char[] CommaSeparator = new[] { ',' };

        /// <summary>
        /// Initialises a new instance of the <see cref="VideoSeekIndexEntry"/> class.
        /// </summary>
        /// <param name="frame">The frame.</param>
        public VideoSeekIndexEntry(VideoFrame frame)
        {
            this.StreamIndex = frame.StreamIndex;
            this.StreamTimeBase = frame.StreamTimeBase;
            this.StartTime = frame.StartTime;
            this.PresentationTime = frame.PresentationTime;
            this.DecodingTime = frame.DecodingTime;
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="VideoSeekIndexEntry"/> class.
        /// </summary>
        /// <param name="streamIndex">Index of the stream.</param>
        /// <param name="timeBaseNum">The time base numerator.</param>
        /// <param name="timeBaseDen">The time base deonominator.</param>
        /// <param name="startTimeTicks">The start time ticks.</param>
        /// <param name="presentationTime">The presentation time.</param>
        /// <param name="decodingTime">The decoding time.</param>
        internal VideoSeekIndexEntry(int streamIndex, int timeBaseNum, int timeBaseDen, long startTimeTicks, long presentationTime, long decodingTime)
        {
            this.StreamIndex = streamIndex;
            this.StartTime = TimeSpan.FromTicks(startTimeTicks);
            this.PresentationTime = presentationTime;
            this.DecodingTime = decodingTime;
            this.StreamTimeBase = new AVRational { num = timeBaseNum, den = timeBaseDen };
        }

        /// <summary>
        /// Gets the stream index of this index entry.
        /// </summary>
        public int StreamIndex { get; }

        /// <summary>
        /// Gets the stream time base.
        /// </summary>
        public AVRational StreamTimeBase { get; }

        /// <summary>
        /// Gets the start time of the frame.
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Gets the original, unadjusted presentation time.
        /// </summary>
        public long PresentationTime { get; }

        /// <summary>
        /// Gets the original, unadjusted decoding time.
        /// </summary>
        public long DecodingTime { get; }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator ==(VideoSeekIndexEntry left, VideoSeekIndexEntry right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator !=(VideoSeekIndexEntry left, VideoSeekIndexEntry right) =>
            !(left == right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <(VideoSeekIndexEntry left, VideoSeekIndexEntry right) =>
            left == null ? right != null : left.CompareTo(right) < 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <=(VideoSeekIndexEntry left, VideoSeekIndexEntry right) =>
            left == null || left.CompareTo(right) <= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >(VideoSeekIndexEntry left, VideoSeekIndexEntry right) =>
            left != null && left.CompareTo(right) > 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >=(VideoSeekIndexEntry left, VideoSeekIndexEntry right) =>
            left == null ? right == null : left.CompareTo(right) >= 0;

        /// <inheritdoc />
        public int CompareTo(VideoSeekIndexEntry other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            return this.StartTime.Ticks.CompareTo(other.StartTime.Ticks);
        }

        /// <inheritdoc />
        public int CompareTo(TimeSpan other)
        {
            return this.StartTime.Ticks.CompareTo(other.Ticks);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is VideoSeekIndexEntry other)
            {
                return ReferenceEquals(this, other);
            }

            return false;
        }

        /// <inheritdoc />
        public bool Equals(VideoSeekIndexEntry other) =>
            ReferenceEquals(this, other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            this.PresentationTime.GetHashCode() ^
            this.StreamIndex.GetHashCode();

        /// <inheritdoc />
        public override string ToString() =>
            $"IX: {this.StreamIndex} | TB: {this.StreamTimeBase.num}/{this.StreamTimeBase.den} | ST: {this.StartTime.Format()} | PTS: {this.PresentationTime} | DTS: {this.DecodingTime}";

        /// <summary>
        /// Creates an entry based on a CSV string.
        /// </summary>
        /// <param name="line">The line.</param>
        /// <returns>An index entry or null if unsuccessful.</returns>
        internal static VideoSeekIndexEntry FromCsvString(string line)
        {
            var parts = line.Split(CommaSeparator);
            if (parts.Length >= 6 &&
                int.TryParse(parts[0], out var streamIndex) &&
                int.TryParse(parts[1], out var timeBaseNum) &&
                int.TryParse(parts[2], out var timeBaseDen) &&
                long.TryParse(parts[3], out var startTimeTicks) &&
                long.TryParse(parts[4], out var presentationTime) &&
                long.TryParse(parts[5], out var decodingTime))
            {
                return new VideoSeekIndexEntry(
                    streamIndex, timeBaseNum, timeBaseDen, startTimeTicks, presentationTime, decodingTime);
            }

            return null;
        }

        /// <summary>
        /// Converts values of this instance to a line of CSV text.
        /// </summary>
        /// <returns>The comma-separated values.</returns>
        internal string ToCsvString() =>
            $"{this.StreamIndex},{this.StreamTimeBase.num},{this.StreamTimeBase.den},{this.StartTime.Ticks},{this.PresentationTime},{this.DecodingTime}";
    }
}
