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
        /// <param name="count">The count.</param>
        public static void Snap(
            this MediaContainer container,
            Action<ThumbnailData, int> callback,
            int count = 24)
        {
            var gap = container.MediaInfo.Duration / count;
            var mediaBlock = (MediaBlock)null;
            for (var i = 0; i < count; i++)
            {
                var data = container.SnapAt(gap * i, ref mediaBlock);
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
        /// <param name="mediaBlock">A reference to a media block. It is fine to
        /// pass a null value on the first call. For subsequent calls it is
        /// recommended to re-use the same reference to aid performance.</param>
        /// <returns>Thumbnail data.</returns>
        public static ThumbnailData SnapAt(
            this MediaContainer container,
            TimeSpan position,
            ref MediaBlock mediaBlock)
        {
            // TODO: Frame position is typically off!
            // The comments on the Seek() method seem to indicate what to do
            var frame = container.Seek(position);
            container.Convert(frame, ref mediaBlock, true, null);

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
                TimeStamp = frame.StartTime,
            };
        }
    }
}
