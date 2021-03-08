// <copyright file="SourceExtensions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.LocalExtensions;

    /// <summary>
    /// Extensions for <see cref="IMediaInputStream"/>.
    /// </summary>
    public static class SourceExtensions
    {
        /// <summary>
        /// Obtains images from evenly-distributed positions in the source.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="count">The count.</param>
        /// <param name="striveForExact">Whether to use multiple reads after
        /// each seek in order to attempt to obtain the exact correct position.
        /// The default behaviour is to only do so for small files; where the
        /// differences are more likely to be noticable.</param>
        public static void AutoSnap(
            this IMediaInputStream source,
            Action<ImageFrameInfo, int> callback,
            int count = 24,
            bool? striveForExact = null)
        {
            using var container = new MediaContainer(source);
            container.Initialize();
            container.Open();

            striveForExact ??= container.Components.Video.FrameCount <= 2000;
            var delta = container.MediaInfo.Duration / (count - 1);
            var start = container.MediaInfo.StartTime > TimeSpan.Zero
                ? container.MediaInfo.StartTime
                : TimeSpan.Zero;

            var block = (MediaBlock)null;
            for (var i = 0; i < count; i++)
            {
                var data = container.TakeSnap(start.Add(delta * i), ref block, striveForExact.Value);
                callback?.Invoke(data, i + 1);
            }

            block?.Dispose();
        }
    }
}
