// <copyright file="Constants.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System.IO;
    using System.Reflection;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Defaults and constants of the Media Engine.
    /// </summary>
    internal static partial class Constants
    {
        /// <summary>
        /// Initialises static members of the <see cref="Constants"/> class.
        /// </summary>
        static Constants()
        {
            try
            {
                var entryAssemblyPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) ?? ".";
                FFmpegSearchPath = Path.GetFullPath(entryAssemblyPath);
                return;
            }
            catch
            {
                // ignore (we might be in winforms design time)
                // see issue #311
            }

            FFmpegSearchPath = Path.GetFullPath(".");
        }

        /// <summary>
        /// Gets the assembly location.
        /// </summary>
        public static string FFmpegSearchPath { get; }

        /// <summary>
        /// Gets the video pixel format. BGRA, 32bit.
        /// </summary>
        public static AVPixelFormat VideoPixelFormat => AVPixelFormat.AV_PIX_FMT_BGRA;
    }
}
