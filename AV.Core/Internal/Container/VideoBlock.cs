﻿// <copyright file="VideoBlock.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Container
{
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
        internal VideoBlock()
            : base(MediaType.Video)
        {
            // placeholder
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
        /// <param name="source">The source.</param>
        /// <param name="pixelFormat">The pixel format.</param>
        /// <returns>True if the allocation was successful.</returns>
        internal unsafe bool Allocate(VideoFrame source, AVPixelFormat pixelFormat)
        {
            // Ensure proper allocation of the buffer
            // If there is a size mismatch between the wanted buffer length and
            // the existing one, then let's reallocate the buffer and set the
            // new size (dispose of the existing one if any)
            var targetLength = ffmpeg.av_image_get_buffer_size(pixelFormat, source.Pointer->width, source.Pointer->height, 1);
            if (!this.Allocate(targetLength))
            {
                return false;
            }

            // Update related properties
            this.PictureBufferStride = ffmpeg.av_image_get_linesize(pixelFormat, source.Pointer->width, 0);
            this.PixelWidth = source.Pointer->width;
            this.PixelHeight = source.Pointer->height;

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
