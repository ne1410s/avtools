﻿// <copyright file="TimeUtilities.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Utilities
{
    using System;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Utilities.
    /// </summary>
    internal static class TimeUtilities
    {
        /// <summary>
        /// Gets a timespan given a timestamp and a timebase.
        /// </summary>
        /// <param name="pts">The PTS.</param>
        /// <param name="timeBase">The time base.</param>
        /// <returns>The TimeSpan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan ToTimeSpan(this double pts, AVRational timeBase)
        {
            if (double.IsNaN(pts) || Math.Abs(pts - ffmpeg.AV_NOPTS_VALUE) <= double.Epsilon)
            {
                return TimeSpan.MinValue;
            }

            return TimeSpan.FromTicks(timeBase.den == 0 ?
                Convert.ToInt64(TimeSpan.TicksPerMillisecond * 1000 * pts / ffmpeg.AV_TIME_BASE) :
                Convert.ToInt64(TimeSpan.TicksPerMillisecond * 1000 * pts * timeBase.num / timeBase.den));
        }

        /// <summary>
        /// Converts a timespan to an AV_TIME_BASE compatible timestamp.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <param name="timeBase">The time base.</param>
        /// <returns>
        /// A long, ffmpeg compatible timestamp.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long ToLong(this TimeSpan ts, AVRational timeBase)
        {
            return Convert.ToInt64(ts.TotalSeconds * timeBase.den / timeBase.num); // (secs) * (units) / (secs) = (units)
        }

        /// <summary>
        /// Gets a timespan given a timestamp and a timebase.
        /// </summary>
        /// <param name="pts">The PTS.</param>
        /// <param name="timeBase">The time base.</param>
        /// <returns>The TimeSpan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan ToTimeSpan(this long pts, AVRational timeBase)
        {
            return Convert.ToDouble(pts).ToTimeSpan(timeBase);
        }

        /// <summary>
        /// Gets a timespan given a timestamp and a timebase.
        /// </summary>
        /// <param name="pts">The PTS in seconds.</param>
        /// <param name="timeBase">The time base.</param>
        /// <returns>The TimeSpan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan ToTimeSpan(this double pts, double timeBase)
        {
            if (double.IsNaN(pts) || Math.Abs(pts - ffmpeg.AV_NOPTS_VALUE) <= double.Epsilon)
            {
                return TimeSpan.MinValue;
            }

            return TimeSpan.FromTicks(Convert.ToInt64(TimeSpan.TicksPerMillisecond * 1000 * pts / timeBase));
        }

        /// <summary>
        /// Gets a timespan given a timestamp and a timebase.
        /// </summary>
        /// <param name="pts">The PTS.</param>
        /// <param name="timeBase">The time base.</param>
        /// <returns>The TimeSpan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan ToTimeSpan(this long pts, double timeBase)
        {
            return Convert.ToDouble(pts).ToTimeSpan(timeBase);
        }

        /// <summary>
        /// Gets a timespan given a timestamp (in AV_TIME_BASE units).
        /// </summary>
        /// <param name="pts">The PTS.</param>
        /// <returns>The TimeSpan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan ToTimeSpan(this double pts)
        {
            return ToTimeSpan(pts, ffmpeg.AV_TIME_BASE);
        }

        /// <summary>
        /// Gets a timespan given a timestamp (in AV_TIME_BASE units).
        /// </summary>
        /// <param name="pts">The PTS.</param>
        /// <returns>The TimeSpan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan ToTimeSpan(this long pts)
        {
            return Convert.ToDouble(pts).ToTimeSpan();
        }

        /// <summary>
        /// Converts a fraction to a double.
        /// </summary>
        /// <param name="rational">The rational.</param>
        /// <returns>The value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double ToDouble(this AVRational rational)
        {
            if (rational.den == 0)
            {
                return 0; // prevent overflows.
            }

            return Convert.ToDouble(rational.num) / Convert.ToDouble(rational.den);
        }

        /// <summary>
        /// Normalizes precision of the TimeSpan to the nearest whole millisecond.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>The normalized, whole-millisecond timespan.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TimeSpan Normalize(this TimeSpan source)
        {
            return TimeSpan.FromSeconds(source.TotalSeconds);
        }

        /// <summary>
        /// Returns a formatted timestamp string in Seconds.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <returns>The formatted string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string Format(this TimeSpan ts)
        {
            return ts == TimeSpan.MinValue ?
                $"{"N/A",10}" :
                $"{ts.TotalSeconds,10:0.000}";
        }

        /// <summary>
        /// Returns a formatted string with elapsed milliseconds between now and
        /// the specified date.
        /// </summary>
        /// <param name="dt">The dt.</param>
        /// <returns>The formatted string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string FormatElapsed(this DateTime dt)
        {
            return $"{DateTime.UtcNow.Subtract(dt).TotalMilliseconds,6:0}";
        }

        /// <summary>
        /// Gets a timespan confined within a range.
        /// </summary>
        /// <param name="source">The source value.</param>
        /// <param name="start">The earliest value in the range.</param>
        /// <param name="end">The latest value in the range.</param>
        /// <returns>The range-confined value.</returns>
        internal static TimeSpan Confine(this TimeSpan source, TimeSpan start, TimeSpan end)
        {
            if (start > end)
            {
                throw new ArgumentException("Start must not be after end");
            }

            return source < start ? start : source > end ? end : source;
        }

        /// <summary>
        /// Gets the time with the lowest value from an array.
        /// </summary>
        /// <param name="times">The times.</param>
        /// <returns>The lowest time.</returns>
        internal static TimeSpan Min(params TimeSpan[] times) => times.Min();

        /// <summary>
        /// Gets the time with the highest value from an array.
        /// </summary>
        /// <param name="times">The times.</param>
        /// <returns>The highest time.</returns>
        internal static TimeSpan Max(params TimeSpan[] times) => times.Max();
    }
}
