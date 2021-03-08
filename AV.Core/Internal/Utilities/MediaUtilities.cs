// <copyright file="MediaUtilities.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Utilities
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Utilities.
    /// </summary>
    internal static class MediaUtilities
    {
        /// <summary>
        /// Computes the picture number.
        /// </summary>
        /// <param name="streamStartTime">The Stream Start time.</param>
        /// <param name="pictureStartTime">The picture Start Time.</param>
        /// <param name="frameRate">Average framerate (not time base).</param>
        /// <returns>
        /// The serial picture number.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long ComputePictureNumber(TimeSpan streamStartTime, TimeSpan pictureStartTime, AVRational frameRate)
        {
            var streamTicks = streamStartTime == TimeSpan.MinValue ? 0 : streamStartTime.Ticks;
            var frameTicks = pictureStartTime == TimeSpan.MinValue ? 0 : pictureStartTime.Ticks;

            if (frameTicks < streamTicks)
            {
                frameTicks = streamTicks;
            }

            return 1L + (long)Math.Round(TimeSpan.FromTicks(frameTicks - streamTicks).TotalSeconds * frameRate.num / frameRate.den, 0, MidpointRounding.ToEven);
        }

        /// <summary>
        /// Computes the smtpe time code.
        /// </summary>
        /// <param name="pictureNumber">The picture number.</param>
        /// <param name="frameRate">The frame rate.</param>
        /// <returns>The FFmpeg computed SMTPE Time code.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe string ComputeSmtpeTimeCode(long pictureNumber, AVRational frameRate)
        {
            var pictureIndex = pictureNumber - 1;
            var frameIndex = Convert.ToInt32(pictureIndex >= int.MaxValue ? pictureIndex % int.MaxValue : pictureIndex);
            var timeCodeInfo = (AVTimecode*)ffmpeg.av_malloc((ulong)Marshal.SizeOf(typeof(AVTimecode)));
            ffmpeg.av_timecode_init(timeCodeInfo, frameRate, 0, 0, null);
            var isNtsc = frameRate.num == 30000 && frameRate.den == 1001;
            var adjustedFrameNumber = isNtsc ?
                ffmpeg.av_timecode_adjust_ntsc_framenum2(frameIndex, Convert.ToInt32(timeCodeInfo->fps)) :
                frameIndex;

            var timeCode = ffmpeg.av_timecode_get_smpte_from_framenum(timeCodeInfo, adjustedFrameNumber);
            var timeCodeBuffer = (byte*)ffmpeg.av_malloc(ffmpeg.AV_TIMECODE_STR_SIZE);

            ffmpeg.av_timecode_make_smpte_tc_string(timeCodeBuffer, timeCode, 1);
            var result = Marshal.PtrToStringAnsi((IntPtr)timeCodeBuffer);

            ffmpeg.av_free(timeCodeInfo);
            ffmpeg.av_free(timeCodeBuffer);

            return result;
        }
    }
}
