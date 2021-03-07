// <copyright file="ContainerExtensions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using AV.Core.Container;

    /// <summary>
    /// Extensions for <see cref="MediaContainer"/>.
    /// </summary>
    public static class ContainerExtensions
    {
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
