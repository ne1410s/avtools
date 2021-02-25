// <copyright file="BlockRenderingWorker.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using AV.Core.Commands;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Primitives;

    /// <summary>
    /// Implements the block rendering worker.
    /// </summary>
    /// <seealso cref="IMediaWorker" />
    internal sealed class BlockRenderingWorker : WorkerBase, IMediaWorker, ILoggingSource
    {
        private readonly AtomicBoolean HasInitialized = new (false);
        private readonly Action<MediaType[]> SerialRenderBlocks;
        private readonly Action<MediaType[]> ParallelRenderBlocks;
        private readonly Thread QuantumThread;
        private readonly ManualResetEventSlim QuantumWaiter = new (false);
        private DateTime LastSpeedRatioTime;

        /// <summary>
        /// Initialises a new instance of the <see cref="BlockRenderingWorker"/> class.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        public BlockRenderingWorker(MediaEngine mediaCore)
            : base(nameof(BlockRenderingWorker))
        {
            this.MediaCore = mediaCore;
            this.Commands = this.MediaCore.Commands;
            this.Container = this.MediaCore.Container;
            this.MediaOptions = mediaCore.MediaOptions;
            this.State = this.MediaCore.State;
            this.ParallelRenderBlocks = (all) => Parallel.ForEach(all, (t) => this.RenderBlock(t));
            this.SerialRenderBlocks = (all) => { foreach (var t in all)
{
    this.RenderBlock(t);
}
            };

            this.QuantumThread = new Thread(this.RunQuantumThread)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = $"{nameof(BlockRenderingWorker)}.Thread",
            };

            this.QuantumThread.Start();
        }

        /// <inheritdoc />
        public MediaEngine MediaCore { get; }

        /// <inheritdoc />
        ILoggingHandler ILoggingSource.LoggingHandler => this.MediaCore;

        /// <summary>
        /// Gets the Media Engine's commands.
        /// </summary>
        private CommandManager Commands { get; }

        /// <summary>
        /// Gets the Media Engine's container.
        /// </summary>
        private MediaContainer Container { get; }

        /// <summary>
        /// Gets the media options.
        /// </summary>
        private MediaOptions MediaOptions { get; }

        /// <summary>
        /// Gets a value indicating whether the component clocks are not bound together.
        /// </summary>
        private bool HasDisconnectedClocks => this.MediaCore.Timing.HasDisconnectedClocks;

        /// <summary>
        /// Gets the Media Engine's state.
        /// </summary>
        private MediaEngineState State { get; }

        /// <summary>
        /// Gets the remaining cycle time.
        /// </summary>
        private TimeSpan RemainingCycleTime
        {
            get
            {
                const double MaxFrameDuration = 50d;
                const double MinFrameDuration = 10d;

                try
                {
                    var frameDuration = this.MediaCore.Timing.ReferenceType == MediaType.Video && this.MediaCore.Blocks[MediaType.Video].Count > 0
                        ? this.MediaCore.Blocks[MediaType.Video].AverageBlockDuration
                        : Constants.DefaultTimingPeriod;

                    // protect against too slow or too fast of a video framerate
                    // which might impact audio rendering.
                    frameDuration = frameDuration.Clamp(
                        TimeSpan.FromMilliseconds(MinFrameDuration),
                        TimeSpan.FromMilliseconds(MaxFrameDuration));

                    return TimeSpan.FromTicks(frameDuration.Ticks - this.CurrentCycleElapsed.Ticks);
                }
                catch
                {
                    // ignore
                }

                return Constants.DefaultTimingPeriod;
            }
        }

        /// <inheritdoc />
        protected override void ExecuteCycleLogic(CancellationToken ct)
        {
            // Update Status Properties
            var main = this.MediaCore.Timing.ReferenceType;
            var all = this.MediaCore.Renderers.Keys.ToArray();

            // Ensure we have renderers ready and main blocks available
            if (!this.Initialize(all))
            {
                return;
            }

            try
            {
                // If we are in the middle of a seek, wait for seek blocks
                this.WaitForSeekBlocks(main, ct);

                // Ensure the RTC clocks match the playback position
                this.AlignClocksToPlayback(main, all);

                // Check for and enter a sync-buffering scenario
                this.EnterSyncBuffering(main, all);

                // Render each of the Media Types if it is time to do so.
                if (this.MediaOptions.UseParallelRendering)
                {
                    this.ParallelRenderBlocks.Invoke(all);
                }
                else
                {
                    this.SerialRenderBlocks.Invoke(all);
                }
            }
            catch (Exception ex)
            {
                this.MediaCore.LogError(
                    Aspects.RenderingWorker, "Error while in rendering worker cycle", ex);

                throw;
            }
            finally
            {
                this.DetectPlaybackEnded(main);

                // CatchUpWithLiveStream(); // TODO: We are on to something good here
                this.ExitSyncBuffering(main, all, ct);
                this.ReportAndResumePlayback(all);
            }
        }

        /// <inheritdoc />
        protected override void OnCycleException(Exception ex) =>
            this.LogError(Aspects.RenderingWorker, "Worker Cycle exception thrown", ex);

        /// <inheritdoc />
        protected override void OnDisposing()
        {
            // Reset the state to non-sync-buffering
            this.MediaCore.SignalSyncBufferingExited();
        }

        /// <inheritdoc />
        protected override void Dispose(bool alsoManaged)
        {
            base.Dispose(alsoManaged);
            this.QuantumWaiter.Dispose();
        }

        /// <summary>
        /// Executes render thread logic in a cycle.
        /// </summary>
        private void RunQuantumThread(object state)
        {
            using var vsync = new VerticalSyncContext();
            while (this.WorkerState != WorkerState.Stopped)
            {
                if (!VerticalSyncContext.IsAvailable)
                {
                    this.State.VerticalSyncEnabled = false;
                }

                var performVersticalSyncWait =
                    this.Container.Components.HasVideo &&
                    this.MediaCore.Timing.GetIsRunning(MediaType.Video) &&
                    this.State.VerticalSyncEnabled;

                if (performVersticalSyncWait)
                {
                    // wait a few times as there is no need to move on to the next frame
                    // if the remaining cycle time is more than twice the refresh rate.
                    while (this.RemainingCycleTime.Ticks >= vsync.RefreshPeriod.Ticks * 2)
                    {
                        vsync.WaitForBlank();
                    }

                    // wait one last time for the actual v-sync
                    if (this.RemainingCycleTime.Ticks > 0)
                    {
                        vsync.WaitForBlank();
                    }
                }
                else
                {
                    // Perform a synthetic wait
                    var waitTime = this.RemainingCycleTime;
                    if (waitTime.Ticks > 0)
                    {
                        this.QuantumWaiter.Wait(waitTime);
                    }
                }

                if (!this.TryBeginCycle())
                {
                    continue;
                }

                this.ExecuteCyle();
            }
        }

        /// <summary>
        /// Performs initialization before regular render loops are executed.
        /// </summary>
        /// <param name="all">All the component renderer types.</param>
        /// <returns>If media was initialized successfully.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Initialize(MediaType[] all)
        {
            // Don't run the cycle if we have already initialized
            if (this.HasInitialized == true)
            {
                return true;
            }

            // Wait for renderers to be ready
            foreach (var t in all)
            {
                this.MediaCore.Renderers[t]?.OnStarting();
            }

            // Mark as initialized
            this.HasInitialized.Value = true;
            return true;
        }

        /// <summary>
        /// Ensures the real-time clocks do not lag or move beyond the range of their corresponding blocks.
        /// </summary>
        /// <param name="main">The main renderer component.</param>
        /// <param name="all">All the renderer components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AlignClocksToPlayback(MediaType main, MediaType[] all)
        {
            // we don't want to disturb the clock or align it if we are not ready
            if (this.Commands.HasPendingCommands)
            {
                return;
            }

            if (this.HasDisconnectedClocks)
            {
                foreach (var t in all)
                {
                    if (t == MediaType.Subtitle)
                    {
                        continue;
                    }

                    var compBlocks = this.MediaCore.Blocks[t];
                    var compPosition = this.MediaCore.Timing.GetPosition(t);

                    if (compBlocks.Count <= 0)
                    {
                        this.MediaCore.PausePlayback(t, false);

                        if (this.MediaCore.Timing.GetIsRunning(t))
                        {
                            this.LogDebug(
                                Aspects.Timing,
                                $"CLOCK PAUSED: {t} clock was paused at {compPosition.Format()} because no decoded {t} content was found");
                        }

                        continue;
                    }

                    // Don't let the RTC lag behind the blocks or move beyond them
                    if (compPosition.Ticks < compBlocks.RangeStartTime.Ticks)
                    {
                        this.MediaCore.ChangePlaybackPosition(compBlocks.RangeStartTime, t, false);
                        this.LogDebug(
                            Aspects.Timing,
                            $"CLOCK BEHIND: {t} clock was {compPosition.Format()}. It was updated to {compBlocks.RangeStartTime.Format()}");
                    }
                    else if (compPosition.Ticks > compBlocks.RangeEndTime.Ticks)
                    {
                        if (t != MediaType.Audio)
                        {
                            this.MediaCore.PausePlayback(t, false);
                        }

                        this.MediaCore.ChangePlaybackPosition(compBlocks.RangeEndTime, t, false);

                        this.LogDebug(
                            Aspects.Timing,
                            $"CLOCK AHEAD : {t} clock was {compPosition.Format()}. It was updated to {compBlocks.RangeEndTime.Format()}");
                    }
                }

                return;
            }

            // Get a reference to the main blocks.
            // The range will be 0 if there are no blocks.
            var blocks = this.MediaCore.Blocks[main];
            var position = this.MediaCore.PlaybackPosition;

            if (blocks.Count == 0)
            {
                // We have no main blocks in range. All we can do is pause the clock
                if (this.MediaCore.Timing.IsRunning)
                {
                    this.LogDebug(
                        Aspects.Timing,
                        $"CLOCK PAUSED: playback clock was paused at {position.Format()} because no decoded {main} content was found");
                }

                this.MediaCore.PausePlayback();
                return;
            }

            if (position.Ticks < blocks.RangeStartTime.Ticks)
            {
                // Don't let the RTC lag behind what is available on the main component
                this.MediaCore.ChangePlaybackPosition(blocks.RangeStartTime);
                this.LogTrace(
                    Aspects.Timing,
                    $"CLOCK BEHIND: playback clock was {position.Format()}. It was updated to {blocks.RangeStartTime.Format()}");
            }
            else if (position.Ticks > blocks.RangeEndTime.Ticks)
            {
                // Don't let the RTC move beyond what is available on the main component
                this.MediaCore.PausePlayback();
                this.MediaCore.ChangePlaybackPosition(blocks.RangeEndTime);
                this.LogTrace(
                    Aspects.Timing,
                    $"CLOCK AHEAD : playback clock was {position.Format()}. It was updated to {blocks.RangeEndTime.Format()}");
            }
        }

        /// <summary>
        /// Speeds up or slows down the speed ratio until the packet buffer
        /// becomes the ideal to continue stable rendering.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CatchUpWithLiveStream()
        {
            // TODO: This is not yet complete.
            // It will fail on m3u8 files for example
            // I am using 2 approches: dealing with timing and dealing with speed ratios
            // I need more time to complete.
            const double DefaultMinBufferMs = 500d;
            const double DefaultMaxBufferMs = 1000d;
            const double UpdateTimeoutMs = 100d;

            if (!this.State.IsLiveStream)
            {
                return;
            }

            // Check if we have a valid duration
            if (this.State.PacketBufferDuration == TimeSpan.MinValue)
            {
                return;
            }

            var maxBufferedMs = DefaultMaxBufferMs;
            var minBufferedMs = DefaultMinBufferMs;
            var bufferedMs = this.Container.Components.Seekable.BufferDuration.TotalMilliseconds; // State.PacketBufferDuration.TotalMilliseconds;

            if (this.State.HasAudio && this.State.HasVideo && !this.HasDisconnectedClocks)
            {
                var videoStartOffset = this.Container.Components[MediaType.Video].StartTime;
                var audioStartOffset = this.Container.Components[MediaType.Audio].StartTime;

                if (videoStartOffset != TimeSpan.MinValue && audioStartOffset != TimeSpan.MinValue)
                {
                    var offsetMs = Math.Abs(videoStartOffset.TotalMilliseconds - audioStartOffset.TotalMilliseconds);
                    maxBufferedMs = Math.Max(maxBufferedMs, offsetMs * 2);
                    minBufferedMs = Math.Min(minBufferedMs, maxBufferedMs / 2d);
                }
            }

            var canChangeSpeed = !this.MediaCore.IsSyncBuffering && !this.Commands.HasPendingCommands;
            var needsSpeedUp = canChangeSpeed && bufferedMs > maxBufferedMs;
            var needsSlowDown = canChangeSpeed && bufferedMs < minBufferedMs;
            var needsSpeedChange = needsSpeedUp || needsSlowDown;
            var lastUpdateSinceMs = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - this.LastSpeedRatioTime.Ticks).TotalMilliseconds;
            var bufferedDelta = needsSpeedUp
                ? bufferedMs - maxBufferedMs
                : minBufferedMs - bufferedMs;

            if (!needsSpeedChange || lastUpdateSinceMs < UpdateTimeoutMs)
            {
                return;
            }

            // TODO: Another option is to mess around some with the timing itself
            // instead of using the speedratio.
            if (bufferedDelta > 100d && (needsSpeedUp || needsSlowDown))
            {
                var deltaPosition = TimeSpan.FromMilliseconds(bufferedDelta / 10);
                if (needsSlowDown)
                {
                    deltaPosition = deltaPosition.Negate();
                }

                this.MediaCore.Timing.Update(this.MediaCore.Timing.Position.Add(deltaPosition), MediaType.None);
                this.LastSpeedRatioTime = DateTime.UtcNow;

                this.LogWarning(
                    nameof(BlockRenderingWorker),
                    $"RT SYNC: Buffered: {bufferedMs:0.000} ms. | Delta: {bufferedDelta:0.000} ms. | Adjustment: {deltaPosition.TotalMilliseconds:0.000} ms.");
            }

            // function computes large changes for large differences.
            /*
            var speedRatioDelta = Math.Min(10d + (Math.Pow(bufferedDelta, 2d) / 100000d), 50d) / 100d;
            if (bufferedDelta < 100d && !needsSlowDown)
                speedRatioDelta = 0d;

            var originalSpeedRatio = State.SpeedRatio;
            var changePercent = (needsSlowDown ? -1d : 1d) * speedRatioDelta;
            State.SpeedRatio = Constants.DefaultSpeedRatio + changePercent;

            if (originalSpeedRatio != State.SpeedRatio)
                LastSpeedRatioTime = DateTime.UtcNow;
            */
        }

        /// <summary>
        /// Enters the sync-buffering scenario if needed.
        /// </summary>
        /// <param name="main">The main renderer component.</param>
        /// <param name="all">All the renderer components.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnterSyncBuffering(MediaType main, MediaType[] all)
        {
            // Determine if Sync-buffering can be potentially entered.
            // Entering the sync-buffering state pauses the RTC and forces the decoder make
            // components catch up with the main component.
            if (this.MediaCore.IsSyncBuffering || this.HasDisconnectedClocks || this.Commands.HasPendingCommands ||
                this.State.MediaState != MediaPlaybackState.Play || this.State.HasMediaEnded || this.Container.IsAtEndOfStream)
            {
                return;
            }

            foreach (var t in all)
            {
                if (t == MediaType.Subtitle || t == main)
                {
                    continue;
                }

                // We don't want to sync-buffer on attached pictures
                if (this.Container.Components[t].IsStillPictures)
                {
                    continue;
                }

                // If we have data on the t component beyond the start time of the main
                // we don't need to enter sync-buffering.
                if (this.MediaCore.Blocks[t].RangeEndTime >= this.MediaCore.Blocks[main].RangeStartTime)
                {
                    continue;
                }

                // If we are not in range of the non-main component we need to
                // enter sync-buffering
                this.MediaCore.SignalSyncBufferingEntered();
                return;
            }
        }

        /// <summary>
        /// Exits the sync-buffering state.
        /// </summary>
        /// <param name="main">The main renderer component.</param>
        /// <param name="all">All the renderer components.</param>
        /// <param name="ct">The cancellation token.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExitSyncBuffering(MediaType main, MediaType[] all, CancellationToken ct)
        {
            // Don't exit syc-buffering if we are not in syncbuffering
            if (!this.MediaCore.IsSyncBuffering)
            {
                return;
            }

            // Detect if an exit from Sync Buffering is required
            var canExitSyncBuffering = this.MediaCore.Blocks[main].Count > 0;
            var mustExitSyncBuffering =
                ct.IsCancellationRequested ||
                this.MediaCore.HasDecodingEnded ||
                this.Container.IsAtEndOfStream ||
                this.State.HasMediaEnded ||
                this.Commands.HasPendingCommands ||
                this.HasDisconnectedClocks;

            try
            {
                if (mustExitSyncBuffering)
                {
                    this.LogDebug(Aspects.ReadingWorker, $"SYNC-BUFFER: 'must exit' condition met.");
                    return;
                }

                if (!canExitSyncBuffering)
                {
                    return;
                }

                foreach (var t in all)
                {
                    if (t == MediaType.Subtitle || t == main)
                    {
                        continue;
                    }

                    // We don't want to consider sync-buffer on attached pictures
                    if (this.Container.Components[t].IsStillPictures)
                    {
                        continue;
                    }

                    // If we don't have data on the t component beyond the mid time of the main
                    // we can't exit sync-buffering.
                    if (this.MediaCore.Blocks[t].RangeEndTime < this.MediaCore.Blocks[main].RangeMidTime)
                    {
                        canExitSyncBuffering = false;
                        break;
                    }
                }
            }
            finally
            {
                // Exit sync-buffering state if we can or we must
                if (mustExitSyncBuffering || canExitSyncBuffering)
                {
                    this.AlignClocksToPlayback(main, all);
                    this.MediaCore.SignalSyncBufferingExited();
                }
            }
        }

        /// <summary>
        /// Waits for seek blocks to become available.
        /// </summary>
        /// <param name="main">The main renderer component.</param>
        /// <param name="ct">The cancellation token.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WaitForSeekBlocks(MediaType main, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested
            && this.Commands.IsActivelySeeking
            && !this.MediaCore.Blocks[main].IsInRange(this.MediaCore.PlaybackPosition))
            {
                // Check if we finally have seek blocks available
                // if we don't get seek blocks in range and we are not step-seeking,
                // then we simply break out of the loop and render whatever it is we have
                // to create the illussion of smooth seeking. For precision seeking we
                // continue the loop.
                if (this.Commands.ActiveSeekMode == CommandManager.SeekMode.Normal &&
                    !this.Commands.WaitForSeekBlocks(1))
                {
                    if (!this.State.ScrubbingEnabled)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Renders the available, non-repeated block.
        /// </summary>
        /// <param name="t">The media type.</param>
        /// <returns>Whether a block was sent to its corresponding renderer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool RenderBlock(MediaType t)
        {
            var result = 0;
            var playbackClock = this.MediaCore.Timing.GetPosition(t);

            try
            {
                // We don't need non-video blocks if we are seeking
                if (this.Commands.HasPendingCommands && t != MediaType.Video)
                {
                    return result > 0;
                }

                // Get the audio, video, or subtitle block to render
                var currentBlock = t == MediaType.Subtitle && this.MediaCore.PreloadedSubtitles != null
                    ? this.MediaCore.PreloadedSubtitles[playbackClock.Ticks]
                    : this.MediaCore.Blocks[t][playbackClock.Ticks];

                // Send the block to the corresponding renderer
                // this will handle fringe and skip cases
                result += this.SendBlockToRenderer(currentBlock, playbackClock);
            }
            finally
            {
                // Call the update method on all renderers so they receive what the new playback clock is.
                this.MediaCore.Renderers[t]?.Update(playbackClock);
            }

            return result > 0;
        }

        /// <summary>
        /// Detects whether the playback has ended.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DetectPlaybackEnded(MediaType main)
        {
            var playbackEndClock = this.MediaCore.Blocks[main].Count > 0
                ? this.MediaCore.Blocks[main].RangeEndTime
                : this.MediaCore.Timing.GetEndTime(main) ?? TimeSpan.MaxValue;

            // Check End of Media Scenarios
            if (!this.Commands.HasPendingCommands
                && this.MediaCore.HasDecodingEnded
                && !this.CanResumeClock(main))
            {
                // Rendered all and nothing else to render
                if (this.State.HasMediaEnded == false)
                {
                    if (this.Container.IsStreamSeekable)
                    {
                        var componentStartTime = this.Container.Components[main].StartTime;
                        var actualComponentDuration = TimeSpan.FromTicks(playbackEndClock.Ticks - componentStartTime.Ticks);
                        this.Container.Components[main].Duration = actualComponentDuration;
                    }

                    this.MediaCore.PausePlayback();
                    this.MediaCore.ChangePlaybackPosition(playbackEndClock);
                }

                this.State.MediaState = MediaPlaybackState.Stop;
                this.State.HasMediaEnded = true;
            }
            else
            {
                this.State.HasMediaEnded = false;
            }
        }

        /// <summary>
        /// Reports the playback position if needed and
        /// resumes the playback clock if the conditions allow for it.
        /// </summary>
        /// <param name="all">All the media component types.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReportAndResumePlayback(MediaType[] all)
        {
            var hasPendingCommands = this.Commands.HasPendingCommands;
            var isSyncBuffering = this.MediaCore.IsSyncBuffering;

            // Notify a change in playback position
            if (!hasPendingCommands && !isSyncBuffering)
            {
                this.State.ReportPlaybackPosition();
            }

            // We don't want to resume the clock if we are not ready for playback
            if (this.State.MediaState != MediaPlaybackState.Play || isSyncBuffering ||
                hasPendingCommands)
            {
                return;
            }

            // wait for packets
            if (this.MediaOptions.MinimumPlaybackBufferPercent > 0 &&
                this.MediaCore.ShouldReadMorePackets &&
                !this.Container.Components.HasEnoughPackets &&
                this.State.BufferingProgress < Math.Min(1, this.MediaOptions.MinimumPlaybackBufferPercent))
            {
                return;
            }

            if (!this.HasDisconnectedClocks)
            {
                // Resume the reference type clock.
                var t = MediaType.None;
                if (this.CanResumeClock(t))
                {
                    this.MediaCore.Timing.Play(t);
                }

                return;
            }

            // Resume individual clock components
            foreach (var t in all)
            {
                if (!this.CanResumeClock(t))
                {
                    continue;
                }

                this.MediaCore.Timing.Play(t);
            }
        }

        /// <summary>
        /// Gets a value indicating whther a component's timing can be resumed.
        /// </summary>
        /// <param name="t">The component media type.</param>
        /// <returns>Whether the clock can be resumed.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanResumeClock(MediaType t)
        {
            var blocks = this.MediaCore.Blocks[t == MediaType.None ? this.MediaCore.Timing.ReferenceType : t];
            if (blocks == null || blocks.Count <= 0)
            {
                return false;
            }

            return this.MediaCore.Timing.GetPosition(t).Ticks < blocks.RangeEndTime.Ticks;
        }

        /// <summary>
        /// Sends the given block to its corresponding media renderer.
        /// </summary>
        /// <param name="incomingBlock">The block.</param>
        /// <param name="playbackPosition">The clock position.</param>
        /// <returns>
        /// The number of blocks sent to the renderer.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int SendBlockToRenderer(MediaBlock incomingBlock, TimeSpan playbackPosition)
        {
            // No blocks were rendered
            if (incomingBlock == null || incomingBlock.IsDisposed)
            {
                return 0;
            }

            var t = incomingBlock.MediaType;
            var isAttachedPicture = t == MediaType.Video && this.Container.Components[t].IsStillPictures;
            var currentBlockStartTime = this.MediaCore.CurrentRenderStartTime.ContainsKey(t)
                ? this.MediaCore.CurrentRenderStartTime[t]
                : TimeSpan.MinValue;

            var isRepeatedBlock = currentBlockStartTime != TimeSpan.MinValue && currentBlockStartTime == incomingBlock.StartTime;
            var requiresRepeatedBlocks = t == MediaType.Audio || isAttachedPicture;

            // Render by forced signal (TimeSpan.MinValue) or because simply it is time to do so
            // otherwise simply skip block rendering as we have sent the block already.
            if (isRepeatedBlock && !requiresRepeatedBlocks)
            {
                return 0;
            }

            // Process property changes coming from video blocks
            this.State.UpdateDynamicBlockProperties(incomingBlock);

            // Capture the last render time so we don't repeat the block
            this.MediaCore.CurrentRenderStartTime[t] = incomingBlock.StartTime;

            // Send the block to its corresponding renderer
            this.MediaCore.Renderers[t]?.Render(incomingBlock, playbackPosition);

            // Log the block statistics for debugging
            this.LogRenderBlock(incomingBlock, playbackPosition);

            return 1;
        }

        /// <summary>
        /// Logs a block rendering operation as a Trace Message
        /// if the debugger is attached.
        /// </summary>
        /// <param name="block">The block.</param>
        /// <param name="clockPosition">The clock position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogRenderBlock(MediaBlock block, TimeSpan clockPosition)
        {
            // Prevent logging for production use
            if (!Debugger.IsAttached)
            {
                return;
            }

            try
            {
                var drift = TimeSpan.FromTicks(clockPosition.Ticks - block.StartTime.Ticks);
                this.LogTrace(
                    Aspects.RenderingWorker,
                    $"{block.MediaType.ToString().Substring(0, 1)} "
                    + $"BLK: {block.StartTime.Format()} | "
                    + $"CLK: {clockPosition.Format()} | "
                    + $"DFT: {drift.TotalMilliseconds,4:0} | "
                    + $"IX: {block.Index,3} | "
                    + $"RNG: {this.MediaCore.Blocks[block.MediaType].GetRangePercent(clockPosition):p} | "
                    + $"PQ: {this.Container?.Components[block.MediaType]?.BufferLength / 1024d,7:0.0}k | "
                    + $"TQ: {this.Container?.Components.BufferLength / 1024d,7:0.0}k");
            }
            catch
            {
                // swallow
            }
        }
    }
}
