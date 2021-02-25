// <copyright file="MediaEngine.Workers.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Platform;
    using AV.Core.Primitives;

    public partial class MediaEngine
    {
        /// <summary>
        /// Gets the buffer length maximum.
        /// port of MAX_QUEUE_SIZE (ffplay.c).
        /// </summary>
        internal const long BufferLengthMax = 16 * 1024 * 1024;

        private readonly AtomicBoolean localIsSyncBuffering = new AtomicBoolean(false);
        private readonly AtomicBoolean localHasDecodingEnded = new AtomicBoolean(false);

        private DateTime SyncBufferStartTime = DateTime.UtcNow;

        /// <summary>
        /// Holds the materialized block cache for each media type.
        /// </summary>
        internal MediaTypeDictionary<MediaBlockBuffer> Blocks { get; } = new MediaTypeDictionary<MediaBlockBuffer>();

        /// <summary>
        /// Gets the preloaded subtitle blocks.
        /// </summary>
        internal MediaBlockBuffer PreloadedSubtitles { get; set; }

        /// <summary>
        /// Gets the worker collection.
        /// </summary>
        internal MediaWorkerSet Workers { get; set; }

        /// <summary>
        /// Holds the block renderers.
        /// </summary>
        internal MediaTypeDictionary<IMediaRenderer> Renderers { get; } = new MediaTypeDictionary<IMediaRenderer>();

        /// <summary>
        /// Holds the last rendered StartTime for each of the media block types.
        /// </summary>
        internal MediaTypeDictionary<TimeSpan> CurrentRenderStartTime { get; } = new MediaTypeDictionary<TimeSpan>();

        /// <summary>
        /// Gets a value indicating whether the decoder worker is sync-buffering.
        /// Sync-buffering is entered when there are no main blocks for the current clock.
        /// This in turn pauses the clock (without changing the media state).
        /// The decoder exits this condition when buffering is no longer needed and
        /// updates the clock position to what is available in the main block buffer.
        /// </summary>
        internal bool IsSyncBuffering
        {
            get => this.localIsSyncBuffering.Value;
            private set => this.localIsSyncBuffering.Value = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the decoder worker has decoded all frames.
        /// This is an indication that the rendering worker should probe for end of media scenarios.
        /// </summary>
        internal bool HasDecodingEnded
        {
            get => this.localHasDecodingEnded.Value;
            set => this.localHasDecodingEnded.Value = value;
        }

        /// <summary>
        /// Gets a value indicating whether packets can be read and
        /// room is available in the download cache.
        /// </summary>
        internal bool ShouldReadMorePackets
        {
            get
            {
                if (this.Container?.Components == null)
                {
                    return false;
                }

                if (this.Container.IsReadAborted || this.Container.IsAtEndOfStream)
                {
                    return false;
                }

                // If it's a live stream always continue reading, regardless
                if (this.Container.IsLiveStream)
                {
                    return true;
                }

                // For network streams always expect a minimum buffer length
                if (this.Container.IsNetworkStream && this.Container.Components.BufferLength < BufferLengthMax)
                {
                    return true;
                }

                // if we don't have enough packets queued we should read
                return this.Container.Components.HasEnoughPackets == false;
            }
        }

        /// <summary>
        /// Signals that the engine has entered the syn-buffering state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SignalSyncBufferingEntered()
        {
            if (this.IsSyncBuffering)
            {
                return;
            }

            this.PausePlayback();
            this.SyncBufferStartTime = DateTime.UtcNow;
            this.IsSyncBuffering = true;

            this.LogInfo(
                Aspects.RenderingWorker,
                $"SYNC-BUFFER: Entered at {this.PlaybackPosition.TotalSeconds:0.000} s." +
                $" | Disconnected Clocks: {this.Timing.HasDisconnectedClocks}" +
                $" | Buffer Progress: {this.State.BufferingProgress:p2}" +
                $" | Buffer Audio: {this.Container?.Components[MediaType.Audio]?.BufferCount}" +
                $" | Buffer Video: {this.Container?.Components[MediaType.Video]?.BufferCount}");
        }

        /// <summary>
        /// Signals that the engine has exited the syn-buffering state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SignalSyncBufferingExited()
        {
            if (!this.IsSyncBuffering)
            {
                return;
            }

            this.IsSyncBuffering = false;
            this.LogInfo(
                Aspects.RenderingWorker,
                $"SYNC-BUFFER: Exited in {DateTime.UtcNow.Subtract(this.SyncBufferStartTime).TotalSeconds:0.000} s." +
                $" | Commands Pending: {this.Commands.HasPendingCommands}" +
                $" | Decoding Ended: {this.HasDecodingEnded}" +
                $" | Buffer Progress: {this.State.BufferingProgress:p2}" +
                $" | Buffer Audio: {this.Container?.Components[MediaType.Audio]?.BufferCount}" +
                $" | Buffer Video: {this.Container?.Components[MediaType.Video]?.BufferCount}");
        }

        /// <summary>
        /// Updates the specified clock type to a new playback position.
        /// </summary>
        /// <param name="playbackPosition">The new playback position.</param>
        /// <param name="t">The clock type. Pass none for all clocks.</param>
        /// <param name="reportPosition">If the new playback position should be reported.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ChangePlaybackPosition(TimeSpan playbackPosition, MediaType t, bool reportPosition)
        {
            if (this.Timing.HasDisconnectedClocks && t == MediaType.None)
            {
                this.LogWarning(
                    Aspects.Container,
                    $"Changing the playback position on disconnected clocks is not supported." +
                    $"Plase set the {nameof(this.MediaOptions.IsTimeSyncDisabled)} to false.");
            }

            this.Timing.Update(playbackPosition, t);

            if (t == MediaType.None)
            {
                this.InvalidateRenderers();
            }
            else
            {
                this.InvalidateRenderer(t);
            }

            if (reportPosition)
            {
                this.State.ReportPlaybackPosition();
            }
        }

        /// <summary>
        /// Updates the clock position and notifies the new
        /// position to the <see cref="State" />.
        /// </summary>
        /// <param name="playbackPosition">The position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ChangePlaybackPosition(TimeSpan playbackPosition) =>
            this.ChangePlaybackPosition(playbackPosition, MediaType.None, true);

        /// <summary>
        /// Pauses the playback by pausing the RTC.
        /// This does not change the state.
        /// </summary>
        /// <param name="t">The clock to pause.</param>
        /// <param name="reportPosition">If the new playback position should be reported.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PausePlayback(MediaType t, bool reportPosition)
        {
            this.Timing.Pause(t);

            if (reportPosition)
            {
                this.State.ReportPlaybackPosition();
            }
        }

        /// <summary>
        /// Pauses the playback by pausing the RTC.
        /// This does not change the state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PausePlayback() => this.PausePlayback(MediaType.None, true);

        /// <summary>
        /// Resets the clock to the zero position and notifies the new
        /// position to rhe <see cref="State"/>.
        /// </summary>
        /// <returns>The newly set position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TimeSpan ResetPlaybackPosition()
        {
            this.Timing.Pause(MediaType.None);
            this.Timing.Reset(MediaType.None);
            this.State.ReportPlaybackPosition();
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Invalidates the last render time for the given component.
        /// Additionally, it calls Seek on the renderer to remove any caches.
        /// </summary>
        /// <param name="t">The t.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InvalidateRenderer(MediaType t)
        {
            // This forces the rendering worker to send the
            // corresponding block to its renderer
            this.CurrentRenderStartTime[t] = TimeSpan.MinValue;
            this.Renderers[t]?.OnSeek();
        }

        /// <summary>
        /// Invalidates the last render time for all renderers given component.
        /// Additionally, it calls Seek on the renderers to remove any caches.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InvalidateRenderers()
        {
            var mediaTypes = this.Renderers.Keys.ToArray();
            foreach (var t in mediaTypes)
            {
                this.InvalidateRenderer(t);
            }
        }
    }
}
