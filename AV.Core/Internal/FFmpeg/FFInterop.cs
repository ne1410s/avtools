// <copyright file="FFInterop.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.FFmpeg
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using AV.Core;
    using AV.Core.Internal.Common;
    using AV.Core.Internal.Utilities;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Provides a set of utilities to perform logging, text formatting,
    /// conversion and other handy calculations.
    /// </summary>
    internal static unsafe class FFInterop
    {
        private static readonly object FFmpegLogBufferSyncLock = new object();
        private static readonly List<string> FFmpegLogBuffer = new List<string>(1024);
        private static readonly IReadOnlyDictionary<int, MediaLogMessageType> FFmpegLogLevels =
            new Dictionary<int, MediaLogMessageType>
            {
                { ffmpeg.AV_LOG_DEBUG, MediaLogMessageType.Debug },
                { ffmpeg.AV_LOG_ERROR, MediaLogMessageType.Error },
                { ffmpeg.AV_LOG_FATAL, MediaLogMessageType.Error },
                { ffmpeg.AV_LOG_INFO, MediaLogMessageType.Info },
                { ffmpeg.AV_LOG_PANIC, MediaLogMessageType.Error },
                { ffmpeg.AV_LOG_TRACE, MediaLogMessageType.Trace },
                { ffmpeg.AV_LOG_WARNING, MediaLogMessageType.Warning },
            };

        private static readonly object SyncLock = new object();
        private static readonly av_log_set_callback_callback FFmpegLogCallback = OnFFmpegMessageLogged;
        private static bool localIsInitialized;
        private static string localLibrariesPath = string.Empty;
        private static int localLibraryIdentifiers;

        /// <summary>
        /// Gets a value indicating whether libraries were initialized correctly.
        /// </summary>
        public static bool IsInitialized
        {
            get
            {
                lock (SyncLock)
                {
                    return localIsInitialized;
                }
            }
        }

        /// <summary>
        /// Gets the libraries path. Only filled when initialized correctly.
        /// </summary>
        public static string LibrariesPath
        {
            get
            {
                lock (SyncLock)
                {
                    return localLibrariesPath;
                }
            }
        }

        /// <summary>
        /// Registers FFmpeg library and initializes its components.
        /// It only needs to be called once but calling it more than once
        /// has no effect. Returns the path that FFmpeg was registered from.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="overridePath">The override path.</param>
        /// <param name="libIdentifiers">The bit-wise flag identifiers
        /// corresponding to the libraries.</param>
        /// <returns>
        /// Returns true if it was a new initialization and it succeeded. False
        /// if there was no need to initialize as there is already a valid
        /// initialization.
        /// </returns>
        /// <exception cref="FileNotFoundException">When ffmpeg libraries are
        /// not found.</exception>
        public static bool Initialize(string overridePath, int libIdentifiers)
        {
            lock (SyncLock)
            {
                if (localIsInitialized)
                {
                    return false;
                }

                try
                {
                    // Get the temporary path where FFmpeg binaries are located
                    var ffmpegPath = string.IsNullOrWhiteSpace(overridePath) == false ?
                        Path.GetFullPath(overridePath) : Constants.FFmpegSearchPath;

                    var registrationIds = 0;

                    // Load FFmpeg binaries by Library ID
                    foreach (var lib in FFLibrary.All)
                    {
                        if ((lib.FlagId & libIdentifiers) != 0 && lib.Load(ffmpegPath))
                        {
                            registrationIds |= lib.FlagId;
                        }
                    }

                    // Check if libraries were loaded correctly
                    if (FFLibrary.All.All(lib => lib.IsLoaded == false))
                    {
                        throw new FileNotFoundException($"Unable to load FFmpeg binaries from folder '{ffmpegPath}'.");
                    }

                    // Additional library initialization
                    if (FFLibrary.LibAVDevice.IsLoaded)
                    {
                        ffmpeg.avdevice_register_all();
                    }

                    // Set logging levels and callbacks
                    ffmpeg.av_log_set_flags(ffmpeg.AV_LOG_SKIP_REPEATED);
                    ffmpeg.av_log_set_level(Library.FFmpegLogLevel);
                    ffmpeg.av_log_set_callback(FFmpegLogCallback);

                    // set the static environment properties
                    localLibrariesPath = ffmpegPath;
                    localLibraryIdentifiers = registrationIds;
                    localIsInitialized = true;
                }
                catch
                {
                    localLibrariesPath = string.Empty;
                    localLibraryIdentifiers = 0;
                    localIsInitialized = false;

                    // rethrow the exception with the original stack trace.
                    throw;
                }

                return localIsInitialized;
            }
        }

        /// <summary>
        /// Copies the contents of a managed string to an unmanaged, UTF8
        /// encoded string.
        /// </summary>
        /// <param name="source">The string to copy.</param>
        /// <returns>A pointer to a string in unmanaged memory.</returns>
        public static byte* StringToBytePointerUTF8(string source)
        {
            var sourceBytes = Encoding.UTF8.GetBytes(source);
            var result = (byte*)ffmpeg.av_mallocz((ulong)sourceBytes.Length + 1);
            Marshal.Copy(sourceBytes, 0, new IntPtr(result), sourceBytes.Length);
            return result;
        }

        /// <summary>
        /// Gets the FFmpeg error message based on the error code.
        /// </summary>
        /// <param name="errorCode">The code.</param>
        /// <returns>The decoded error message.</returns>
        public static unsafe string DecodeMessage(int errorCode)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(errorCode, buffer, (ulong)bufferSize);
            var message = GeneralUtilities.PtrToStringUTF8(buffer);
            return message;
        }

        /// <summary>
        /// Retrieves the codecs.
        /// </summary>
        /// <returns>The codecs.</returns>
        public static unsafe AVCodec*[] RetrieveCodecs()
        {
            var result = new List<IntPtr>(1024);
            void* iterator;
            AVCodec* item;
            while ((item = ffmpeg.av_codec_iterate(&iterator)) != null)
            {
                result.Add((IntPtr)item);
            }

            var collection = new AVCodec*[result.Count];
            for (var i = 0; i < result.Count; i++)
            {
                collection[i] = (AVCodec*)result[i];
            }

            return collection;
        }

        /// <summary>
        /// Log message callback from ffmpeg library.
        /// </summary>
        /// <param name="p0">The p0.</param>
        /// <param name="level">The level.</param>
        /// <param name="format">The format.</param>
        /// <param name="vl">The vl.</param>
        private static unsafe void OnFFmpegMessageLogged(void* p0, int level, string format, byte* vl)
        {
            const int lineSize = 1024;
            lock (FFmpegLogBufferSyncLock)
            {
                if (level > ffmpeg.av_log_get_level())
                {
                    return;
                }

                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = GeneralUtilities.PtrToStringUTF8(lineBuffer);
                FFmpegLogBuffer.Add(line);

                var messageType = MediaLogMessageType.Debug;
                if (FFmpegLogLevels.ContainsKey(level))
                {
                    messageType = FFmpegLogLevels[level];
                }

                if (!line.EndsWith("\n", StringComparison.Ordinal))
                {
                    return;
                }

                line = string.Join(string.Empty, FFmpegLogBuffer);
                line = line.TrimEnd();
                FFmpegLogBuffer.Clear();

                //TODO: Log {messageType}
                ////{line};
                Debug.Write($"{messageType} ({nameof(FFInterop)}): {line}");
            }
        }
    }
}
