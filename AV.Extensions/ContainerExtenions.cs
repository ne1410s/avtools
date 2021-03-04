// <copyright file="ContainerExtenions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using AV.Core.Common;
    using AV.Core.Container;

    /// <summary>
    /// Extensions for the <see cref="MediaContainer"/> class.
    /// </summary>
    public static class ContainerExtenions
    {
        public static void BuildIndex(this MediaContainer container)
        {
            if (!container.IsOpen)
            {
                throw new ArgumentException("Container must be open");
            }

            var vComponent = container.Components.Video;

            // Reset the index and position
            vComponent.SeekIndex.Clear();
            container.Seek(TimeSpan.MinValue);

            var indices = new SortedDictionary<long, VideoSeekIndexEntry>();
            while (container.IsStreamSeekable)
            {
                container.Read();
                var frames = container.Decode();
                foreach (var frame in frames)
                {
                    try
                    {
                        if (frame.MediaType == MediaType.Video
                            && frame is VideoFrame vFrame
                            && vFrame.PictureType == FFmpeg.AutoGen.AVPictureType.AV_PICTURE_TYPE_I
                            && !indices.ContainsKey(frame.StartTime.Ticks))
                        {
                            indices[frame.StartTime.Ticks] = new VideoSeekIndexEntry(vFrame);
                        }
                    }
                    finally
                    {
                        frame.Dispose();
                    }
                }

                // We have reached the end of the stream.
                if (frames.Count <= 0 && container.IsAtEndOfStream)
                {
                    break;
                }
            }

            vComponent.SeekIndex.AddRange(indices.Values);
        }

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
            var workingFrame = container.Seek(position);

            //TODO! This produces a wonderfully linear frame dist...
            // BUT alas seems to be pissing memory...
            // Maybe a better seek index?? (VideoComponent)

            //while (true)
            //{
            //    container.Read();
            //    var receivedFrame = container.Components.Video.ReceiveNextFrame();
            //    workingFrame = receivedFrame ?? workingFrame;
            //    if (receivedFrame == null || container.IsAtEndOfStream || workingFrame.StartTime >= position)
            //    {
            //        break;
            //    }
            //}

            // Commenting-out is beautiful wrt memory, but wobbly dist


            // IDEA!: Can we do a Seek() pass with several x granular iterations
            // to build up a set of frame numbers and times.. so then a second
            // pass can simply re-seek?...

            container.Convert(workingFrame, ref mediaBlock, true, null);

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
                TimeStamp = workingFrame.StartTime,
            };
        }
    }
}
