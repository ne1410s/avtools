// <copyright file="Library.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using AV.Core.Common;
    using AV.Core.Container;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Provides access to the underlying FFmpeg library information.
    /// </summary>
    public static partial class Library
    {
        private static readonly string NotInitializedErrorMessage =
            $"{nameof(FFmpeg)} library not initialized. Set the {nameof(FFmpegDirectory)} and call {nameof(LoadFFmpeg)}";

        private static readonly object SyncLock = new ();
        private static string localFFmpegDirectory = Constants.FFmpegSearchPath;
        private static int localFFmpegLoadModeFlags = FFmpegLoadMode.FullFeatures;
        private static IReadOnlyList<string> localInputFormatNames;
        private static IReadOnlyList<OptionMetadata> localGlobalInputFormatOptions;
        private static IReadOnlyDictionary<string, IReadOnlyList<OptionMetadata>> localInputFormatOptions;
        private static IReadOnlyList<string> localDecoderNames;
        private static IReadOnlyList<string> localEncoderNames;
        private static IReadOnlyList<OptionMetadata> localGlobalDecoderOptions;
        private static IReadOnlyDictionary<string, IReadOnlyList<OptionMetadata>> localDecoderOptions;
        private static unsafe AVCodec*[] localAllCodecs;
        private static int localFFmpegLogLevel = Debugger.IsAttached ? ffmpeg.AV_LOG_VERBOSE : ffmpeg.AV_LOG_WARNING;

        /// <summary>
        /// Gets or sets the FFmpeg path from which to load the FFmpeg binaries.
        /// You must set this path before setting the Source property for the first time on any instance of this control.
        /// Setting this property when FFmpeg binaries have been registered will have no effect.
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
        /// Gets or sets the bitwise library identifiers to load.
        /// See the <see cref="FFmpegLoadMode"/> constants.
        /// If FFmpeg is already loaded, the value cannot be changed.
        /// </summary>
        public static int FFmpegLoadModeFlags
        {
            get => localFFmpegLoadModeFlags;
            set
            {
                if (FFInterop.IsInitialized)
                {
                    return;
                }

                localFFmpegLoadModeFlags = value;
            }
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
        /// Gets a value indicating whether the FFmpeg library has been initialized.
        /// </summary>
        public static bool IsInitialized => FFInterop.IsInitialized;

        /// <summary>
        /// Gets the registered FFmpeg input format names.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static IReadOnlyList<string> InputFormatNames
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    return localInputFormatNames ??= FFInterop.RetrieveInputFormatNames();
                }
            }
        }

        /// <summary>
        /// Gets the global input format options information.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static IReadOnlyList<OptionMetadata> InputFormatOptionsGlobal
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    return localGlobalInputFormatOptions ??= FFInterop.RetrieveGlobalFormatOptions();
                }
            }
        }

        /// <summary>
        /// Gets the input format options.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static IReadOnlyDictionary<string, IReadOnlyList<OptionMetadata>> InputFormatOptions
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    if (localInputFormatOptions != null)
                    {
                        return localInputFormatOptions;
                    }

                    var result = new Dictionary<string, IReadOnlyList<OptionMetadata>>(InputFormatNames.Count);
                    foreach (var formatName in InputFormatNames)
                    {
                        var optionsInfo = FFInterop.RetrieveInputFormatOptions(formatName);
                        result[formatName] = optionsInfo;
                    }

                    localInputFormatOptions = new Dictionary<string, IReadOnlyList<OptionMetadata>>(result);

                    return localInputFormatOptions;
                }
            }
        }

        /// <summary>
        /// Gets the registered FFmpeg decoder codec names.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static unsafe IReadOnlyList<string> DecoderNames
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    return localDecoderNames ??= FFInterop.RetrieveDecoderNames(AllCodecs);
                }
            }
        }

        /// <summary>
        /// Gets the registered FFmpeg decoder codec names.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static unsafe IReadOnlyList<string> EncoderNames
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    return localEncoderNames ??= FFInterop.RetrieveEncoderNames(AllCodecs);
                }
            }
        }

        /// <summary>
        /// Gets the global options that apply to all decoders.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static IReadOnlyList<OptionMetadata> DecoderOptionsGlobal
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    return localGlobalDecoderOptions ??= FFInterop.RetrieveGlobalCodecOptions().Where(o => o.IsDecodingOption).ToArray();
                }
            }
        }

        /// <summary>
        /// Gets the decoder specific options.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
        public static unsafe IReadOnlyDictionary<string, IReadOnlyList<OptionMetadata>> DecoderOptions
        {
            get
            {
                lock (SyncLock)
                {
                    if (!FFInterop.IsInitialized)
                    {
                        throw new InvalidOperationException(NotInitializedErrorMessage);
                    }

                    if (localDecoderOptions != null)
                    {
                        return localDecoderOptions;
                    }

                    var result = new Dictionary<string, IReadOnlyList<OptionMetadata>>(DecoderNames.Count);
                    foreach (var c in AllCodecs)
                    {
                        if (c->decode.Pointer == IntPtr.Zero)
                        {
                            continue;
                        }

                        result[Utilities.PtrToStringUTF8(c->name)] = FFInterop.RetrieveCodecOptions(c);
                    }

                    localDecoderOptions = new Dictionary<string, IReadOnlyList<OptionMetadata>>(result);

                    return localDecoderOptions;
                }
            }
        }

        /// <summary>
        /// Gets all registered encoder and decoder codecs.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the MediaEngine has not been initialized.</exception>
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
        /// Forces the pre-loading of the FFmpeg libraries according to the values of the
        /// <see cref="FFmpegDirectory"/> and <see cref="FFmpegLoadModeFlags"/>
        /// Also, sets the <see cref="FFmpegVersionInfo"/> property. Throws an exception
        /// if the libraries cannot be loaded.
        /// </summary>
        /// <returns>true if libraries were loaded, false if libraries were already loaded.</returns>
        public static bool LoadFFmpeg()
        {
            if (!FFInterop.Initialize(FFmpegDirectory, FFmpegLoadModeFlags))
            {
                return false;
            }

            // Set the folders and lib identifiers
            FFmpegDirectory = FFInterop.LibrariesPath;
            FFmpegLoadModeFlags = FFInterop.LibraryIdentifiers;
            FFmpegVersionInfo = ffmpeg.av_version_info();
            return true;
        }

        /// <summary>
        /// Provides an asynchronous version of the <see cref="LoadFFmpeg"/> call.
        /// </summary>
        /// <returns>true if libraries were loaded, false if libraries were already loaded.</returns>
        public static ConfiguredTaskAwaitable<bool> LoadFFmpegAsync() =>
            Task.Run(() => LoadFFmpeg()).ConfigureAwait(true);

        /// <summary>
        /// Unloads FFmpeg libraries from memory.
        /// </summary>
        /// <exception cref="NotImplementedException">Unloading FFmpeg libraries is not yet supported.</exception>
        public static void UnloadFFmpeg() =>
            throw new NotImplementedException("Unloading FFmpeg libraries is not yet supported");

        /// <summary>
        /// Retrieves the media information including all streams, chapters and programs.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <returns>The contents of the media information.</returns>
        public static MediaInfo RetrieveMediaInfo(string mediaSource)
        {
            using var container = new MediaContainer(mediaSource, null, null);
            container.Initialize();
            return container.MediaInfo;
        }

        /// <summary>
        /// Creates a viedo seek index object by decoding video frames and obtaining the intra-frames that are valid for index positions.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <param name="streamIndex">Index of the stream. Use -1 for automatic stream selection.</param>
        /// <returns>
        /// The seek index object.
        /// </returns>
        public static VideoSeekIndex CreateVideoSeekIndex(string mediaSource, int streamIndex)
        {
            var result = new VideoSeekIndex(mediaSource, -1);

            using (var container = new MediaContainer(mediaSource, null, null))
            {
                container.Initialize();
                container.MediaOptions.IsAudioDisabled = true;
                container.MediaOptions.IsVideoDisabled = false;
                container.MediaOptions.IsSubtitleDisabled = true;

                if (streamIndex >= 0)
                {
                    container.MediaOptions.VideoStream = container.MediaInfo.Streams[streamIndex];
                }

                container.Open();
                result.StreamIndex = container.Components.Video.StreamIndex;
                while (container.IsStreamSeekable)
                {
                    container.Read();
                    var frames = container.Decode();
                    foreach (var frame in frames)
                    {
                        try
                        {
                            if (frame.MediaType != MediaType.Video)
                            {
                                continue;
                            }

                            // Check if the frame is a key frame and add it to the index.
                            result.TryAdd(frame as VideoFrame);
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
            }

            return result;
        }

        /// <summary>
        /// Creates a viedo seek index object of the default video stream.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <returns>
        /// The seek index object.
        /// </returns>
        public static VideoSeekIndex CreateVideoSeekIndex(string mediaSource) => CreateVideoSeekIndex(mediaSource, -1);
    }
}
