// <copyright file="VideoBlock.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Container
{
    using System.Drawing;
    using AV.Core.Internal.Common;
    using global::FFmpeg.AutoGen;

    /// <inheritdoc />
    /// <summary>
    /// A pre-allocated, scaled video block. The buffer is BGR, 24-bit format.
    /// </summary>
    internal sealed class VideoBlock : MediaBlock
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="VideoBlock"/> class.
        /// </summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        internal VideoBlock(int width = 0, int height = 0)
            : base(MediaType.Video)
        {
            this.PixelWidth = width;
            this.PixelHeight = height;
        }

        /// <summary>
        /// Gets the number of horizontal pixels in the image.
        /// </summary>
        public int PixelWidth { get; private set; }

        /// <summary>
        /// Gets the number of vertical pixels in the image.
        /// </summary>
        public int PixelHeight { get; private set; }

        /// <summary>
        /// Gets or sets the pixel aspect width.
        /// This is NOT the display aspect width.
        /// </summary>
        public int PixelAspectWidth { get; internal set; }

        /// <summary>
        /// Gets or sets the pixel aspect height.
        /// This is NOT the display aspect height.
        /// </summary>
        public int PixelAspectHeight { get; internal set; }

        /// <summary>
        /// Gets or sets the SMTPE time code.
        /// </summary>
        public string SmtpeTimeCode { get; internal set; }

        /// <summary>
        /// Gets or sets a value indicating whether this frame was decoded in a
        /// hardware context.
        /// </summary>
        public bool IsHardwareFrame { get; internal set; }

        /// <summary>
        /// Gets or sets the name of the hardware decoder if the frame was
        /// decoded in a hardware context.
        /// </summary>
        public string HardwareAcceleratorName { get; internal set; }

        /// <summary>
        /// Gets or sets the display picture number (frame number).
        /// If not set by the decoder, this attempts to obtain it by dividing
        /// the start time by the frame duration.
        /// </summary>
        public long DisplayPictureNumber { get; internal set; }

        /// <summary>
        /// Gets or sets the coded picture number set by the decoder.
        /// </summary>
        public long CodedPictureNumber { get; internal set; }

        /// <summary>
        /// Gets or sets the picture type.
        /// </summary>
        public AVPictureType PictureType { get; internal set; }

        /// <summary>
        /// Gets the picture buffer stride.
        /// </summary>
        public int PictureBufferStride { get; private set; }

        /// <summary>
        /// Allocates a block of memory suitable for a picture buffer
        /// and sets the corresponding properties.
        /// </summary>
        /// <param name="pixelFormat">The pixel format.</param>
        /// <param name="targetSize">The target size.</param>
        /// <returns>True if the allocation was successful.</returns>
        internal unsafe bool Allocate(AVPixelFormat pixelFormat, Size targetSize)
        {
            // Ensure proper allocation of the buffer
            // If there is a size mismatch between the wanted buffer length and
            // the existing one, then let's reallocate the buffer and set the
            // new size (dispose of the existing one if any)
            var targetLength = ffmpeg.av_image_get_buffer_size(pixelFormat, targetSize.Width, targetSize.Height, 1);
            if (!this.Allocate(targetLength))
            {
                return false;
            }

            // Update related properties
            this.PictureBufferStride = ffmpeg.av_image_get_linesize(pixelFormat, targetSize.Width, 0);
            this.PixelWidth = targetSize.Width;
            this.PixelHeight = targetSize.Height;

            return true;
        }

        /// <inheritdoc />
        protected override void Deallocate()
        {
            base.Deallocate();
            this.PictureBufferStride = 0;
            this.PixelWidth = 0;
            this.PixelHeight = 0;
        }
    }
}
