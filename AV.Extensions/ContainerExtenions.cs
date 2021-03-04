// <copyright file="ContainerExtenions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using AV.Core.Container;

    /// <summary>
    /// Extensions for the <see cref="MediaContainer"/> class.
    /// </summary>
    public static class ContainerExtenions
    {
        /// <summary>
        /// Obtains images from evenly-distributed positions in the contained
        /// media. The container must have already been initialised and opened
        /// before calling this method.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="accuracy">If true, each seek is immediately
        /// followed by a series of read + decode cycles in order to make a slow
        /// but careful approach to the specified position.</param>
        /// <param name="count">The count.</param>
        public static void Snap(
            this MediaContainer container,
            Action<ThumbnailData, int> callback,
            bool accuracy,
            int count = 24)
        {
            var gap = container.MediaInfo.Duration / count;
            var mediaBlock = (MediaBlock)null;
            for (var i = 0; i < count; i++)
            {
                var data = container.SnapAt(gap * i, accuracy, ref mediaBlock);
                callback(data, i + 1);
            }
        }

        /// <summary>
        /// Obtains an image from a specified position in the contained media.
        /// The container must have already been initialised and opened before
        /// calling this method.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="position">The position.</param>
        /// <param name="accuracy">If true, each seek is immediately
        /// followed by a series of read + decode cycles in order to make a slow
        /// but careful approach to the specified position.</param>
        /// <param name="mediaBlock">A reference to a media block. It is fine to
        /// pass a null value on the first call. For subsequent calls it is
        /// recommended to re-use the same reference to aid performance.</param>
        /// <returns>Thumbnail data.</returns>
        public static ThumbnailData SnapAt(
            this MediaContainer container,
            TimeSpan position,
            bool accuracy,
            ref MediaBlock mediaBlock)
        {
            var curFrame = container.Seek(position);
            if (accuracy)
            {
                while (true)
                {
                    container.Read();
                    var receivedFrame = container.Components.Video.ReceiveNextFrame();
                    if (container.IsAtEndOfStream || receivedFrame?.StartTime >= position)
                    {
                        curFrame?.Dispose();
                        curFrame = receivedFrame;
                        break;
                    }

                    receivedFrame?.Dispose();
                }
            }

            container.Convert(curFrame, ref mediaBlock, true, null);
            var videoBlock = (VideoBlock)mediaBlock;
            var bitmap = new Bitmap(
                videoBlock.PixelWidth,
                videoBlock.PixelHeight,
                videoBlock.PictureBufferStride,
                PixelFormat.Format32bppArgb,
                videoBlock.Buffer);

            return new ThumbnailData
            {
                FrameNumber = videoBlock.DisplayPictureNumber,
                Image = bitmap,
                TimeStamp = curFrame.StartTime,
            };
        }
    }
}
