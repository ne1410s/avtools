// <copyright file="CommandManager.Seek.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Commands
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Primitives;

    internal partial class CommandManager
    {
        private readonly ManualResetEventSlim SeekBlocksAvailable = new (true);
        private readonly AtomicBoolean localIsSeeking = new (false);
        private readonly AtomicBoolean localPlayAfterSeek = new (false);
        private readonly AtomicInteger localActiveSeekMode = new ((int)SeekMode.Normal);

        private SeekOperation QueuedSeekOperation;
        private Task<bool> QueuedSeekTask;

        /// <summary>
        /// Gets a value indicating whether a seek operation is pending or in progress.
        /// </summary>
        public bool IsSeeking
        {
            get => this.localIsSeeking.Value;
            private set
            {
                this.localIsSeeking.Value = value;
                this.State.ReportCommandStatus();
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is actively seeking within a stream.
        /// </summary>
        public bool IsActivelySeeking => !this.SeekBlocksAvailable.IsSet;

        /// <summary>
        /// When actively seeking, provides the active seek mode.
        /// </summary>
        public SeekMode ActiveSeekMode
        {
            get => (SeekMode)this.localActiveSeekMode.Value;
            private set => this.localActiveSeekMode.Value = (int)value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether playback should be resumed when all
        /// seek operations complete.
        /// </summary>
        private bool PlayAfterSeek
        {
            get => this.localPlayAfterSeek.Value;
            set => this.localPlayAfterSeek.Value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimeSpan ComputeStepTargetPosition(SeekMode seekMode, MediaBlockBuffer mainBlocks, TimeSpan currentPosition)
        {
            var neighbors = mainBlocks.Neighbors(currentPosition);
            var currentBlock = neighbors[2];
            var neighborBlock = seekMode == SeekMode.StepForward ? neighbors[1] : neighbors[0];
            var blockDuration = mainBlocks.AverageBlockDuration;

            var defaultOffsetTicks = seekMode == SeekMode.StepForward
                ? blockDuration.Ticks > 0 ? (long)(blockDuration.Ticks * 1.5) : TimeSpan.FromSeconds(0.5).Ticks
                : -(blockDuration.Ticks > 0 ? Convert.ToInt64(blockDuration.Ticks * 0.8) : TimeSpan.FromSeconds(0.5).Ticks);

            if (currentBlock == null)
            {
                return TimeSpan.FromTicks(currentPosition.Ticks + defaultOffsetTicks);
            }

            if (neighborBlock == null)
            {
                return TimeSpan.FromTicks(currentBlock.StartTime.Ticks + defaultOffsetTicks);
            }

            return neighborBlock.StartTime;
        }

        /// <summary>
        /// Executes boilerplate logic to queue a seek operation.
        /// </summary>
        /// <param name="seekTarget">The seek target.</param>
        /// <param name="seekMode">The seek mode.</param>
        /// <returns>An awaitable task.</returns>
        private Task<bool> QueueSeekCommand(TimeSpan seekTarget, SeekMode seekMode)
        {
            lock (this.SyncLock)
            {
                if (this.IsDisposed || this.IsDisposing || !this.State.IsOpen || this.IsDirectCommandPending || this.IsPriorityCommandPending || !this.State.IsSeekable)
                {
                    return Task.FromResult(false);
                }

                if (this.QueuedSeekTask != null)
                {
                    this.QueuedSeekOperation.Mode = seekMode;
                    this.QueuedSeekOperation.Position = seekTarget;
                    return this.QueuedSeekTask;
                }

                if (this.IsSeeking == false)
                {
                    this.IsSeeking = true;
                    this.PlayAfterSeek = this.State.MediaState == MediaPlaybackState.Play && seekMode == SeekMode.Normal;
                    this.MediaCore.PausePlayback();
                    this.State.MediaState = MediaPlaybackState.Manual;
                    this.MediaCore.SendOnSeekingStarted();
                }

                var seekOperation = new SeekOperation(seekTarget, seekMode);
                this.QueuedSeekOperation = seekOperation;
                this.QueuedSeekTask = new Task<bool>(() =>
                {
                    seekOperation.Wait();
                    return true;
                });

                this.QueuedSeekTask.Start();
                return this.QueuedSeekTask;
            }
        }

        /// <summary>
        /// Clears the queued seek commands.
        /// </summary>
        private void ClearSeekCommands()
        {
            lock (this.SyncLock)
            {
                this.QueuedSeekOperation?.Dispose();
                this.QueuedSeekOperation = null;
                this.QueuedSeekTask = null;
                this.IsSeeking = false;
            }
        }

        /// <summary>
        /// Implements the Seek Media Command.
        /// </summary>
        /// <param name="seekOperation">The seek operation.</param>
        /// <param name="ct">The ct.</param>
        /// <returns>True if the operation was successful.</returns>
        private bool SeekMedia(SeekOperation seekOperation, CancellationToken ct)
        {
            // TODO: Handle Cancellation token ct
            var result = false;
            var hasDecoderSeeked = false;
            var startTime = DateTime.UtcNow;
            var targetSeekMode = seekOperation.Mode;
            var targetPosition = seekOperation.Position;
            var hasSeekBlocks = false;

            try
            {
                var seekableType = this.MediaCore.Container.Components.SeekableMediaType;
                var all = this.MediaCore.Container.Components.MediaTypes;
                var mainBlocks = this.MediaCore.Blocks[seekableType];
                var initialPosition = this.MediaCore.PlaybackPosition;

                if (targetSeekMode == SeekMode.StepBackward || targetSeekMode == SeekMode.StepForward)
                {
                    targetPosition = ComputeStepTargetPosition(targetSeekMode, mainBlocks, initialPosition);
                }
                else if (targetSeekMode == SeekMode.Stop)
                {
                    targetPosition = TimeSpan.MinValue;
                }

                // Check if we already have the block. If we do, simply set the clock position to the target position
                // we don't need anything else. This implements frame-by frame seeking and we need to snap to a discrete
                // position of the main component so it sticks on it.
                if (mainBlocks.IsInRange(targetPosition))
                {
                    this.MediaCore.ChangePlaybackPosition(targetPosition);
                    return true;
                }

                // Let consumers know main blocks are not available
                hasDecoderSeeked = true;

                // wait for the current reading and decoding cycles
                // to finish. We don't want to interfere with reading in progress
                // or decoding in progress.
                this.MediaCore.Workers.PauseReadDecode();
                this.SeekBlocksAvailable.Reset();

                // Signal the starting state clearing the packet buffer cache
                // TODO: this may not be necessary because the container does this for us.
                // explore the possibility of removing this line
                this.MediaCore.Container.Components.ClearQueuedPackets(flushBuffers: true);

                // Capture seek target adjustment
                var adjustedSeekTarget = targetPosition;
                if (targetPosition != TimeSpan.MinValue && mainBlocks.IsMonotonic)
                {
                    var targetSkewTicks = Convert.ToInt64(
                        mainBlocks.MonotonicDuration.Ticks * (mainBlocks.Capacity / 2d));

                    if (adjustedSeekTarget.Ticks >= targetSkewTicks)
                    {
                        adjustedSeekTarget = TimeSpan.FromTicks(adjustedSeekTarget.Ticks - targetSkewTicks);
                    }
                }

                // Populate frame queues with after-seek operation
                var firstFrame = this.MediaCore.Container.Seek(adjustedSeekTarget);
                if (firstFrame != null)
                {
                    // if we seeked to minvalue we really meant the first frame start time
                    if (targetPosition == TimeSpan.MinValue)
                    {
                        targetPosition = firstFrame.StartTime;
                    }

                    // Ensure we signal media has not ended
                    this.State.HasMediaEnded = false;

                    // Clear Blocks and frames (This does not clear the preloaded subtitles)
                    foreach (var mt in all)
                    {
                        this.MediaCore.Blocks[mt].Clear();
                    }

                    // reset the render times
                    this.MediaCore.InvalidateRenderers();

                    // Create the blocks from the obtained seek frames
                    this.MediaCore.Blocks[firstFrame.MediaType]?.Add(firstFrame, this.MediaCore.Container);
                    hasSeekBlocks = this.TrySignalBlocksAvailable(targetSeekMode, mainBlocks, targetPosition, hasSeekBlocks);

                    // Decode all available queued packets into the media component blocks
                    foreach (var mt in all)
                    {
                        while (this.MediaCore.Blocks[mt].IsFull == false && ct.IsCancellationRequested == false)
                        {
                            var frame = this.MediaCore.Container.Components[mt].ReceiveNextFrame();
                            if (frame == null)
                            {
                                break;
                            }

                            this.MediaCore.Blocks[mt].Add(frame, this.MediaCore.Container);
                            hasSeekBlocks = this.TrySignalBlocksAvailable(targetSeekMode, mainBlocks, targetPosition, hasSeekBlocks);
                        }
                    }

                    // Align to the exact requested position on the main component
                    while (this.MediaCore.ShouldReadMorePackets && ct.IsCancellationRequested == false && hasSeekBlocks == false)
                    {
                        // Check if we are already in range
                        hasSeekBlocks = this.TrySignalBlocksAvailable(targetSeekMode, mainBlocks, targetPosition, hasSeekBlocks);

                        // Read the next packet
                        var packetType = this.MediaCore.Container.Read();
                        var blocks = this.MediaCore.Blocks[packetType];
                        if (blocks == null)
                        {
                            continue;
                        }

                        // Get the next frame
                        if (blocks.RangeEndTime.Ticks < targetPosition.Ticks || blocks.IsFull == false)
                        {
                            blocks.Add(this.MediaCore.Container.Components[packetType].ReceiveNextFrame(), this.MediaCore.Container);
                            hasSeekBlocks = this.TrySignalBlocksAvailable(targetSeekMode, mainBlocks, targetPosition, hasSeekBlocks);
                        }
                    }
                }

                // Find out what the final, best-effort position was
                TimeSpan resultPosition;
                if (mainBlocks.IsInRange(targetPosition) == false)
                {
                    // We don't have a a valid main range
                    var minStartTimeTicks = mainBlocks.RangeStartTime.Ticks;
                    var maxStartTimeTicks = mainBlocks.RangeEndTime.Ticks;

                    this.LogWarning(
                        Aspects.EngineCommand,
                        $"SEEK TP: Target Pos {targetPosition.Format()} not between {mainBlocks.RangeStartTime.TotalSeconds:0.000} " +
                        $"and {mainBlocks.RangeEndTime.TotalSeconds:0.000}");

                    resultPosition = TimeSpan.FromTicks(targetPosition.Ticks.Clamp(minStartTimeTicks, maxStartTimeTicks));
                }
                else
                {
                    resultPosition = mainBlocks.Count == 0 && targetPosition != TimeSpan.Zero ?
                        initialPosition : // Unsuccessful. This initial position is simply what the clock was :(
                        targetPosition; // Successful seek with main blocks in range
                }

                // Write a new Real-time clock position now.
                if (hasSeekBlocks == false)
                {
                    this.MediaCore.ChangePlaybackPosition(resultPosition);
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                this.LogError(Aspects.EngineCommand, "SEEK ERROR", ex);
            }
            finally
            {
                if (hasDecoderSeeked)
                {
                    this.LogTrace(
                        Aspects.EngineCommand,
                        $"SEEK D: Elapsed: {startTime.FormatElapsed()} | Target: {targetPosition.Format()}");
                }

                this.SeekBlocksAvailable.Set();
                this.MediaCore.InvalidateRenderers();
                seekOperation.Dispose();
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TrySignalBlocksAvailable(SeekMode mode, MediaBlockBuffer mainBlocks, TimeSpan targetPosition, bool hasSeekBlocks)
        {
            // signal that there is a main block available
            if (hasSeekBlocks == false && mainBlocks.IsInRange(targetPosition))
            {
                // We need to update the clock immediately because
                // the renderer will need this position
                this.MediaCore.ChangePlaybackPosition(mode != SeekMode.Normal && mode != SeekMode.Stop
                    ? mainBlocks[targetPosition.Ticks].StartTime
                    : targetPosition);

                this.SeekBlocksAvailable.Set();
                return true;
            }

            return hasSeekBlocks;
        }

        /// <summary>
        /// Provides parameters and a reset event to reference when the operation completes.
        /// </summary>
        /// <seealso cref="IDisposable" />
        private sealed class SeekOperation : IDisposable
        {
            private readonly object localSyncLock = new object();
            private bool localIsDisposed;

            /// <summary>
            /// Initialises a new instance of the <see cref="SeekOperation"/> class.
            /// </summary>
            /// <param name="position">The position.</param>
            /// <param name="mode">The mode.</param>
            public SeekOperation(TimeSpan position, SeekMode mode)
            {
                this.Position = position;
                this.Mode = mode;
            }

            /// <summary>
            /// Gets or sets the target position.
            /// </summary>
            public TimeSpan Position { get; set; }

            /// <summary>
            /// Gets or sets the seek mode.
            /// </summary>
            public SeekMode Mode { get; set; }

            /// <summary>
            /// Gets the seek completed event.
            /// </summary>
            private ManualResetEventSlim SeekCompleted { get; } = new (false);

            /// <summary>
            /// Waits for the <see cref="SeekCompleted"/> event to be set.
            /// </summary>
            public void Wait()
            {
                lock (this.localSyncLock)
                {
                    if (this.localIsDisposed)
                    {
                        return;
                    }
                }

                this.SeekCompleted.Wait();
            }

            /// <inheritdoc />
            public void Dispose() => this.Dispose(true);

            /// <summary>
            /// Releases unmanaged and - optionally - managed resources.
            /// </summary>
            /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
            private void Dispose(bool alsoManaged)
            {
                lock (this.localSyncLock)
                {
                    if (this.localIsDisposed)
                    {
                        return;
                    }

                    this.SeekCompleted.Set();

                    if (alsoManaged)
                    {
                        this.SeekCompleted.Dispose();
                    }

                    this.localIsDisposed = true;
                }
            }
        }
    }
}
