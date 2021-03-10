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
        /// <param name="resizeHeight">Optionally resizes image height.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        /// <returns>Image frame information.</returns>
        public ImageFrameInfo Snap(
            TimeSpan position,
            int? resizeHeight = null,
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

            if (resizeHeight != null && resizeHeight != this.SessionInfo.Dimensions.Height)
            {
                rawImage = rawImage.Resize(resizeHeight.Value);
            }

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
        /// <param name="resizeHeight">Optionally resizes image height.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        /// <param name="positions">The array of positions.</param>
        public void SnapMany(
            FrameReceived onReceived,
            int? resizeHeight = null,
            bool? forceStrive = null,
            params TimeSpan[] positions)
        {
            for (var i = 0; i < positions.Length; i++)
            {
                var data = this.Snap(positions[i], resizeHeight, forceStrive);
                onReceived?.Invoke(data, i + 1);
            }
        }

        /// <summary>
        /// Obtains images from evenly-distributed positions.
        /// </summary>
        /// <param name="onReceived">The on received callback.</param>
        /// <param name="totalImages">The number of images.</param>
        /// <param name="resizeHeight">Optionally resizes image height.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        public void SnapMany(
            FrameReceived onReceived,
            int totalImages = 24,
            int? resizeHeight = null,
            bool? forceStrive = null)
        {
            var positions = this.GetDistributed(totalImages);
            this.SnapMany(onReceived, resizeHeight, forceStrive, positions);
        }

        /// <summary>
        /// Collates an image comprising those obtained from evenly-distributed
        /// positions.
        /// </summary>
        /// <param name="columns">The number of columns to use.</param>
        /// <param name="imageHeight">The image height.</param>
        /// <param name="marginX">The margin between rows.</param>
        /// <param name="marginY">The margin between columns.</param>
        /// <param name="header">The header height.</param>
        /// <param name="footer">The footer height.</param>
        /// <param name="timestamp">Whether to use a timestamp on each.</param>
        /// <param name="framestamp">Whether to use a framestamp.</param>
        /// <param name="border">Whether to use a border on each image.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        /// <param name="positions">The array of positions.</param>
        /// <returns>A collated image.</returns>
        public Bitmap Collate(
            int columns = 4,
            int imageHeight = 200,
            int marginX = 10,
            int marginY = 10,
            int header = 100,
            int footer = 20,
            bool timestamp = true,
            bool framestamp = true,
            bool border = true,
            bool? forceStrive = null,
            params TimeSpan[] positions)
        {
            var rows = (int)Math.Ceiling(positions.Length / 4d);
            var imageWidth = (int)Math.Round(imageHeight * this.SessionInfo.AspectRatio);
            var canvasWidth = (columns * imageWidth) + ((columns - 1) * marginX);
            var canvasHeight = (rows * imageHeight) + ((rows - 1) * marginY) + header + footer;
            var retVal = new Bitmap(canvasWidth, canvasHeight);

            var yellowBrush = new SolidBrush(Color.Yellow);
            var whiteBrush = new SolidBrush(Color.White);
            var blackBrush = new SolidBrush(Color.Black);
            var blackPen = new Pen(blackBrush);
            var font = new Font("Consolas", 12, FontStyle.Bold);
            var rightAlign = new StringFormat { Alignment = StringAlignment.Far };
            var boxHeight = 18;
            var charWidth = 10;
            var fsFormat = $"D{this.SessionInfo.FrameCount.ToString().Length}";
            var tsFormat = this.SessionInfo.Duration.TotalHours >= 1
                ? @"h\:mm\:ss\.f"
                : this.SessionInfo.Duration.TotalMinutes >= 1
                    ? @"mm\:ss\.f"
                    : @"ss\.f";

            using (var g = Graphics.FromImage(retVal))
            {
                g.FillRectangle(whiteBrush, 0, 0, canvasWidth, canvasHeight);
                this.SnapMany(
                    (frame, n) =>
                    {
                        var x = (imageWidth + marginX) * ((n - 1) % columns);
                        var y = header + ((imageHeight + marginY) * (int)Math.Floor((n - 1) / 4d));
                        g.DrawImage(frame.Image, x, y);

                        if (border)
                        {
                            g.DrawRectangle(blackPen, x, y, imageWidth, imageHeight);
                        }

                        if (framestamp)
                        {
                            var tX1 = x + imageWidth;
                            var fs = frame.FrameNumber.ToString(fsFormat);
                            var tW = charWidth * fs.Length;
                            g.FillRectangle(blackBrush, tX1 - tW, y, tW, boxHeight);
                            g.DrawString(fs, font, whiteBrush, tX1, y, rightAlign);
                        }

                        if (timestamp)
                        {
                            var tX1 = x + imageWidth;
                            var tY0 = y + imageHeight - boxHeight;
                            var ts = frame.StartTime.ToString(tsFormat);
                            var tW = charWidth * ts.Length;
                            g.FillRectangle(blackBrush, tX1 - tW, tY0, tW, boxHeight);
                            g.DrawString(ts, font, yellowBrush, tX1, tY0 - 1, rightAlign);
                        }
                    },
                    imageHeight,
                    forceStrive,
                    positions);
            }

            return retVal;
        }

        /// <summary>
        /// Collates an image comprising those obtained from evenly-distributed
        /// positions.
        /// </summary>
        /// <param name="totalImages">The number of images.</param>
        /// <param name="columns">The number of columns to use.</param>
        /// <param name="imageHeight">The image height.</param>
        /// <param name="marginX">The margin between rows.</param>
        /// <param name="marginY">The margin between columns.</param>
        /// <param name="header">The header height.</param>
        /// <param name="footer">The footer height.</param>
        /// <param name="timestamp">Whether to use a timestamp on each.</param>
        /// <param name="framestamp">Whether to use a framestamp.</param>
        /// <param name="border">Whether to use a border on each image.</param>
        /// <param name="forceStrive">Can be used to force strive.</param>
        /// <returns>A collated image.</returns>
        public Bitmap Collate(
            int totalImages = 24,
            int columns = 4,
            int imageHeight = 200,
            int marginX = 10,
            int marginY = 10,
            int header = 100,
            int footer = 20,
            bool timestamp = true,
            bool framestamp = true,
            bool border = true,
            bool? forceStrive = null)
        {
            var positions = this.GetDistributed(totalImages);
            return this.Collate(columns, imageHeight, marginX, marginY, header, footer, timestamp, framestamp, border, forceStrive, positions);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.blockReference?.Dispose();
            this.container?.Dispose();
        }

        /// <summary>
        /// Gets an evenly-distributed array of times.
        /// </summary>
        /// <param name="total">The number.</param>
        /// <returns>An array of positions.</returns>
        private TimeSpan[] GetDistributed(int total)
        {
            var delta = this.SessionInfo.Duration / (total - 1);
            return Enumerable.Range(0, total)
                .Select(idx => this.SessionInfo.StartTime.Add(delta * idx))
                .ToArray();
        }
    }
}
