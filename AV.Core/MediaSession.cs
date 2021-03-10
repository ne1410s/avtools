// <copyright file="MediaSession.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Linq;
    using AV.Core.Internal;
    using AV.Core.Internal.Container;
    using AV.Core.Internal.Utilities;

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
        /// <param name="libFolder">Path to ffmpeg libraries.</param>
        public MediaSession(IMediaInputStream source, string libFolder = "ffmpeg")
        {
            Library.FFmpegDirectory = libFolder;
            Library.LoadFFmpeg();

            this.container = new MediaContainer(source);
            this.container.Initialize();
            this.container.Open();

            this.SessionInfo = new MediaSessionInfo(this.container);
            this.StriveExact = this.SessionInfo.FrameCount < StriveThreshold;
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
        /// Gets information regarding the loaded media.
        /// </summary>
        public MediaSessionInfo SessionInfo { get; }

        /// <summary>
        /// Obtains an image from a specified position. The accuracy is affected
        /// by the value of <see cref="StriveExact"/>.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        /// <returns>Image frame information.</returns>
        public ImageFrameInfo Snap(
            TimeSpan position,
            bool? forceStrive = null)
        {
            position = position.Confine(this.SessionInfo.StartTime, this.SessionInfo.EndTime);
            var doStrive = forceStrive ?? this.StriveExact;
            var frame = this.container.Seek(position);
            while (doStrive && !this.container.IsAtEndOfStream)
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
            var rawImage = new Bitmap(
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
                Image = rawImage,
            };
        }

        /// <summary>
        /// Obtains images from each of the supplied positions.
        /// </summary>
        /// <param name="onReceived">The on received callback.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        /// <param name="positions">The array of positions.</param>
        public void SnapMany(
            FrameReceived onReceived,
            bool? forceStrive = null,
            params TimeSpan[] positions)
        {
            for (var i = 0; i < positions.Length; i++)
            {
                var data = this.Snap(positions[i], forceStrive);
                onReceived?.Invoke(data, i + 1);
            }
        }

        /// <summary>
        /// Obtains images from evenly-distributed positions.
        /// </summary>
        /// <param name="onReceived">The on received callback.</param>
        /// <param name="totalImages">The number of images.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        public void SnapMany(
            FrameReceived onReceived,
            int totalImages = 24,
            bool? forceStrive = null)
        {
            var delta = this.SessionInfo.Duration / (totalImages - 1);
            var positions = Enumerable.Range(0, totalImages)
                .Select(idx => this.SessionInfo.StartTime.Add(delta * idx));
            this.SnapMany(onReceived, forceStrive, positions.ToArray());
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.blockReference?.Dispose();
            this.container?.Dispose();
        }
    }
}
