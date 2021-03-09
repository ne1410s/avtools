// <copyright file="MediaSession.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using AV.Core.Internal.Container;

    /// <summary>
    /// Called when an image frame has been fully-populated.
    /// </summary>
    /// <param name="info">Frame data.</param>
    /// <param name="number">The sequential number from a set.</param>
    public delegate void FrameReceived(ImageFrameInfo info, int number);

    /// <summary>
    /// A media session.
    /// </summary>
    public class MediaSession : IDisposable
    {
        /// <summary>
        /// The number of frames below which striving is enabled by default.
        /// </summary>
        private const int StriveThreshold = 10000;

        private readonly MediaContainer container;

        private MediaBlock blockReference = null;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaSession"/> class.
        /// </summary>
        /// <param name="source">The source.</param>
        public MediaSession(IMediaInputStream source)
        {
            this.container = new MediaContainer(source);
            this.container.Initialize();
            this.container.Open();

            this.StriveExact = this.FrameCount < StriveThreshold;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to follow-up seek operations
        /// with multiple read-and-decode cycles in order to best arrive at the
        /// requested position. If false, then we are at the mercy of the
        /// distribution of 'I' frames within the stream. For larger files this
        /// is not expected to be an issue, assuming accuracy is not paramount.
        /// By default, this is set to true if the stream frame count is less
        /// than the <see cref="StriveThreshold"/>.
        /// </summary>
        public bool StriveExact { get; set; }

        /// <summary>
        /// Gets the format name.
        /// </summary>
        public string Format => this.container.MediaFormatName;

        /// <summary>
        /// Gets the metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata => this.container.Metadata;

        /// <summary>
        /// Gets the duration.
        /// </summary>
        public TimeSpan Duration => this.container.Components.Video.Duration;

        /// <summary>
        /// Gets the frame count.
        /// </summary>
        public long FrameCount => this.container.Components.Video.FrameCount;

        /// <summary>
        /// Gets the stream uri.
        /// </summary>
        public string StreamUri => this.container.MediaSource;

        /// <summary>
        /// Obtains images from evenly-distributed positions.
        /// </summary>
        /// <param name="onReceived">The on received callback.</param>
        /// <param name="count">The count.</param>
        public void AutoSnap(FrameReceived onReceived, int count = 24)
        {
            var delta = this.container.MediaInfo.Duration / (count - 1);
            var start = this.container.MediaInfo.StartTime > TimeSpan.Zero
                ? this.container.MediaInfo.StartTime
                : TimeSpan.Zero;

            for (var i = 0; i < count; i++)
            {
                var data = this.TakeSnap(start.Add(delta * i));
                onReceived?.Invoke(data, i + 1);
            }
        }

        /// <summary>
        /// Obtains an image from a specified position. The accuracy is affected
        /// by the value of <see cref="StriveExact"/>.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>Image frame information.</returns>
        public ImageFrameInfo TakeSnap(TimeSpan position)
        {
            var frame = this.container.Seek(position);
            while (this.StriveExact && !this.container.IsAtEndOfStream)
            {
                this.container.Read();
                var receivedFrame = this.container.Components.Video.ReceiveNextFrame();
                if (receivedFrame != null && receivedFrame.StartTime >= position)
                {
                    frame?.Dispose();
                    frame = receivedFrame;
                    break;
                }

                receivedFrame?.Dispose();
            }

            this.container.Convert(frame, ref this.blockReference, true, null);
            var videoBlock = (VideoBlock)this.blockReference;
            var bitmap = new Bitmap(
                videoBlock.PixelWidth,
                videoBlock.PixelHeight,
                videoBlock.PictureBufferStride,
                PixelFormat.Format32bppRgb,
                videoBlock.Buffer);

            return new ImageFrameInfo
            {
                FrameNumber = videoBlock.DisplayPictureNumber,
                PresentationTime = videoBlock.PresentationTime,
                StartTime = frame.StartTime,
                Image = bitmap,
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.blockReference?.Dispose();
            this.container?.Dispose();
        }
    }
}
