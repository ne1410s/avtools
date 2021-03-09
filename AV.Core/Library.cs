// <copyright file="Library.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using System.Diagnostics;
    using AV.Core.Internal.Common;
    using AV.Core.Internal.FFmpeg;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Provides access to the underlying FFmpeg library information.
    /// </summary>
    public static partial class Library
    {
        private static readonly string NotInitializedErrorMessage =
            $"{nameof(FFmpeg)} library not initialized. Set the {nameof(FFmpegDirectory)} and call {nameof(LoadFFmpeg)}";

        private static readonly object SyncLock = new ();
        private static string localFFmpegDirectory = Constants.FFmpegSearchPath;
        private static int localFFmpegLoadModeFlags = Constants.AllLibs;
        private static unsafe AVCodec*[] localAllCodecs;
        private static int localFFmpegLogLevel = Debugger.IsAttached ? ffmpeg.AV_LOG_VERBOSE : ffmpeg.AV_LOG_WARNING;

        /// <summary>
        /// Gets or sets the FFmpeg path from which to load the FFmpeg binaries.
        /// You must set this path before setting the Source property for the
        /// first time on any instance of this control. Setting this property
        /// when FFmpeg binaries have been registered will have no effect.
        /// </summary>
        public static string FFmpegDirectory
        {
            get => localFFmpegDirectory;
            set
            {
                if (FFInterop.IsInitialized)
                {
                    return;
                }

                localFFmpegDirectory = value;
            }
        }

        /// <summary>
        /// Gets the FFmpeg version information. Returns null
        /// when the libraries have not been loaded.
        /// </summary>
        public static string FFmpegVersionInfo
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the FFmpeg log level.
        /// </summary>
        public static int FFmpegLogLevel
        {
            get
            {
                return IsInitialized
                    ? ffmpeg.av_log_get_level()
                    : localFFmpegLogLevel;
            }

            set
            {
                if (IsInitialized)
                {
                    ffmpeg.av_log_set_level(value);
                }

                localFFmpegLogLevel = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether FFmpeg library has been initialised.
        /// </summary>
        public static bool IsInitialized => FFInterop.IsInitialized;

        /// <summary>
        /// Gets all registered encoder and decoder codecs.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has
        /// not been initialized.</exception>
        internal static unsafe AVCodec*[] AllCodecs
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    return localAllCodecs ??= FFInterop.RetrieveCodecs();
                }
            }
        }

        /// <summary>
        /// Forces the pre-loading of the FFmpeg libraries according to the
        /// values of the <see cref="FFmpegDirectory"/>.
        /// Also, sets the <see cref="FFmpegVersionInfo"/> property. Throws an
        /// exception if the libraries cannot be loaded.
        /// </summary>
        /// <returns>true if libraries were loaded, false if libraries were
        /// already loaded.</returns>
        public static bool LoadFFmpeg()
        {
            if (!FFInterop.Initialize(FFmpegDirectory, Constants.AllLibs))
            {
                return false;
            }

            // Set the folders and lib identifiers
            FFmpegDirectory = FFInterop.LibrariesPath;
            FFmpegVersionInfo = ffmpeg.av_version_info();
            return true;
        }
    }
}
