// <copyright file="SnappingExtensions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using AV.Core.Common;
    using AV.Core.Container;

    /// <summary>
    /// Extensions for obtaining snaps.
    /// </summary>
    public static class SnappingExtensions
    {
        /// <summary>
        /// Obtains images from evenly-distributed positions in the contained
        /// media. The container must have already been initialised and opened
        /// before calling this method.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="count">The count.</param>
        /// <param name="striveForExact">If true, the container shall
        /// endeavour to obtain the closest frame to the requested position. If
        /// false, it shall not. If null (the default), then striving is only
        /// used where the media contains 2000 frames or fewer, where the offset
        /// may be more noticable.</param>
        public static void AutoSnap(
            this IMediaInputStream source,
            Action<ImageFrameInfo, int> callback,
            int count = 24,
            bool? striveForExact = null)
        {
            using var container = new MediaContainer(source);
            container.Initialize();
            container.Open();

            striveForExact ??= container.Components.Video.FrameCount <= 2000;
            var delta = container.MediaInfo.Duration / (count - 1);
            var start = container.MediaInfo.StartTime > TimeSpan.Zero
                ? container.MediaInfo.StartTime
                : TimeSpan.Zero;

            var block = (MediaBlock)null;
            for (var i = 0; i < count; i++)
            {
                var data = container.TakeSnap(start.Add(delta * i), ref block, striveForExact.Value);
                callback?.Invoke(data, i + 1);
            }

            block?.Dispose();
        }

        /// <summary>
        /// Obtains an image from a specified position in the contained media.
        /// The container must have already been initialised and opened before
        /// calling this method.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="position">The position.</param>
        /// <param name="mediaBlock">A reference to a media block. It is fine to
        /// pass a null value on the first call. For subsequent calls it is
        /// recommended to re-use the same reference to aid performance.</param>
        /// <param name="striveForExact">If true, the container shall endeavour
        /// to obtain the closest frame to the requested position. This does add
        /// a processing cost which one may not always need to incur.</param>
        /// <returns>Thumbnail data.</returns>
        public static ImageFrameInfo TakeSnap(
            this MediaContainer container,
            TimeSpan position,
            ref MediaBlock mediaBlock,
            bool striveForExact = true)
        {
            var frame = container.Seek(position);
            while (striveForExact && !container.IsAtEndOfStream)
            {
                container.Read();
                var receivedFrame = container.Components.Video.ReceiveNextFrame();
                if (receivedFrame != null && receivedFrame.StartTime >= position)
                {
                    frame?.Dispose();
                    frame = receivedFrame;
                    break;
                }

                receivedFrame?.Dispose();
            }

            container.Convert(frame, ref mediaBlock, true, null);
            var videoBlock = (VideoBlock)mediaBlock;
            var bitmap = new Bitmap(
                videoBlock.PixelWidth,
                videoBlock.PixelHeight,
                videoBlock.PictureBufferStride,
                PixelFormat.Format32bppRgb,
                videoBlock.Buffer);

            return new ImageFrameInfo
            {
                FrameNumber = videoBlock.DisplayPictureNumber,
                Image = bitmap,
                StartTime = frame.StartTime,
            };
        }
    }
}
