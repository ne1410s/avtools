// <copyright file="MediaEngine.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Runtime.CompilerServices;
    using AV.Core.Commands;
    using AV.Core.Common;
    using AV.Core.Diagnostics;
    using AV.Core.Platform;
    using AV.Core.Primitives;

    /// <summary>
    /// Represents a Media Engine that contains underlying streams of audio and/or video.
    /// It uses the fantastic FFmpeg library to perform reading and decoding of media streams.
    /// </summary>
    /// <seealso cref="ILoggingHandler" />
    /// <seealso cref="IDisposable" />
    public sealed partial class MediaEngine : IDisposable, ILoggingSource, ILoggingHandler
    {
        private readonly AtomicBoolean localIsDisposed = new AtomicBoolean(false);

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaEngine"/> class.
        /// </summary>
        /// <param name="parent">The associated parent object.</param>
        /// <param name="connector">The parent implementing connector methods.</param>
        /// <exception cref="InvalidOperationException">Thrown when the static Initialize method has not been called.</exception>
        public MediaEngine(object parent, IMediaConnector connector)
        {
            // Associate the parent as the media connector that implements the callbacks
            this.Parent = parent;
            this.Connector = connector;
            this.Commands = new CommandManager(this);
            this.State = new MediaEngineState(this);
            this.Timing = new TimingController(this);
        }

        /// <summary>
        /// An event that is raised whenever a global FFmpeg message is logged.
        /// </summary>
        public static event EventHandler<LoggingMessage> FFmpegMessageLogged;

        /// <inheritdoc />
        ILoggingHandler ILoggingSource.LoggingHandler => this;

        /// <summary>
        /// Contains the Media Status.
        /// </summary>
        public MediaEngineState State { get; }

        /// <summary>
        /// Provides stream, chapter and program info of the underlying media.
        /// Returns null when no media is loaded.
        /// </summary>
        public MediaInfo MediaInfo => this.Container?.MediaInfo;

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed => this.localIsDisposed.Value;

        /// <summary>
        /// Gets the associated parent object.
        /// </summary>
        public object Parent { get; }

        /// <summary>
        /// Gets the real-time playback clock position.
        /// </summary>
        public TimeSpan PlaybackPosition => this.Timing.Position;

        /// <summary>
        /// Represents a real-time time clock controller.
        /// </summary>
        internal TimingController Timing { get; }

        /// <summary>
        /// Gets the media options. Do not modify the properties of this object directly
        /// as it may cause unstable playback or crashes.
        /// </summary>
        internal MediaOptions MediaOptions => this.Container?.MediaOptions;

        /// <summary>
        /// Gets the event connector (platform specific).
        /// </summary>
        internal IMediaConnector Connector { get; }

        /// <inheritdoc />
        void ILoggingHandler.HandleLogMessage(LoggingMessage message) =>
            this.SendOnMessageLogged(message);

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.localIsDisposed == true)
            {
                return;
            }

            this.localIsDisposed.Value = true;

            // Dispose of commands. This closes the
            // Media automatically and signals an exit
            // This also causes the Container to get disposed.
            this.Commands.Dispose();

            // Reset the RTC
            this.ResetPlaybackPosition();
        }

        /// <summary>
        /// Raises the FFmpeg message logged.
        /// </summary>
        /// <param name="message">The <see cref="LoggingMessage"/> instance.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RaiseFFmpegMessageLogged(LoggingMessage message) =>
            FFmpegMessageLogged?.Invoke(null, message);
    }
}
