// <copyright file="ImageExtensions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;

    /// <summary>
    /// Extensions for <see cref="Bitmap"/>.
    /// </summary>
    public static class ImageExtensions
    {
        /// <summary>
        /// Creates a new bitmap from an original, maintaining aspect ratio.
        /// </summary>
        /// <param name="source">The source image.</param>
        /// <param name="targetHeight">The target height.</param>
        /// <returns>The resized image.</returns>
        public static Bitmap Resize(this Image source, int targetHeight)
        {
            var aspect = source.Width / (double)source.Height;
            var targetWidth = (int)Math.Round(targetHeight * aspect);
            var target = new Bitmap(targetWidth, targetHeight);

            target.SetResolution(source.HorizontalResolution, source.VerticalResolution);
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using var wrapMode = new ImageAttributes();
                var targetRect = new Rectangle(0, 0, target.Width, targetHeight);
                wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source, targetRect, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrapMode);
            }

            return target;
        }
    }
}
