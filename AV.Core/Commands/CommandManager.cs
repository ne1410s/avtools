// <copyright file="CommandManager.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Commands
{
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using AV.Core.Common;
    using AV.Core.Diagnostics;
    using AV.Core.Engine;
    using AV.Core.Primitives;

    /// <summary>
    /// Provides the MediEngine with an API to execute media control commands.
    /// Direct Commands execute immediately (Open, CLose, Change)
    /// Priority Commands execute in the queue but before anything else and are exclusive (Play, Pause, Stop)
    /// Seek commands are queued and replaced. These are processed in a deferred manner by this worker.
    /// </summary>
    /// <seealso cref="IntervalWorkerBase" />
    /// <seealso cref="IMediaWorker" />
    /// <seealso cref="ILoggingSource" />
    internal sealed partial class CommandManager : IntervalWorkerBase, IMediaWorker, ILoggingSource
    {
        private readonly object SyncLock = new ();

        /// <summary>
        /// Initialises a new instance of the <see cref="CommandManager"/> class.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        public CommandManager(MediaEngine mediaCore)
            : base(nameof(CommandManager))
        {
            this.MediaCore = mediaCore;
            this.StartAsync();
        }

        /// <inheritdoc />
        public MediaEngine MediaCore { get; }

        /// <summary>
        /// Gets a value indicating whether the command manager is executing commands or has pending commands.
        /// </summary>
        public bool HasPendingCommands => this.IsSeeking || this.IsDirectCommandPending || this.IsPriorityCommandPending;

        /// <inheritdoc />
        ILoggingHandler ILoggingSource.LoggingHandler => this.MediaCore;

        /// <summary>
        /// Gets the media engine state.
        /// </summary>
        private MediaEngineState State => this.MediaCore.State;

        /// <summary>
        /// Gets or sets the pending priority command. There can only be one at
        /// a time.
        /// </summary>
        private PriorityCommandType PendingPriorityCommand
        {
            get => (PriorityCommandType)this.localPendingPriorityCommand.Value;
            set => this.localPendingPriorityCommand.Value = (int)value;
        }

        /// <summary>
        /// Opens the media using a standard URI.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>An awaitable task which contains a boolean whether or not to resume media when completed.</returns>
        public Task<bool> OpenMediaAsync(Uri uri) => this.ExecuteDirectCommand(DirectCommandType.Open, () => this.CommandOpenMedia(null, uri));

        /// <summary>
        /// Opens the media using a custom stream.
        /// </summary>
        /// <param name="stream">The custom input stream.</param>
        /// <returns>An awaitable task which contains a boolean whether or not to resume media when completed.</returns>
        public Task<bool> OpenMediaAsync(IMediaInputStream stream) => this.ExecuteDirectCommand(DirectCommandType.Open, () => this.CommandOpenMedia(stream, null));

        /// <summary>
        /// Closes the currently open media.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean whether or not to resume media when completed.</returns>
        public Task<bool> CloseMediaAsync()
        {
            lock (this.SyncLock)
            {
                if (this.IsCloseInterruptPending)
                {
                    this.LogWarning(Aspects.EngineCommand, $"Direct Command interrupt for {this.PendingDirectCommand} is already pending completion.");
                    return Task.FromResult(false);
                }

                var shouldInterrupt =
                    !this.IsCloseInterruptPending &&
                    this.PendingDirectCommand != DirectCommandType.Close &&
                    this.PendingDirectCommand != DirectCommandType.None;

                if (shouldInterrupt)
                {
                    this.IsCloseInterruptPending = true;
                    this.MediaCore.Container?.SignalAbortReads(false);

                    return Task.Run(() =>
                    {
                        try
                        {
                            while (this.hasDirectCommandCompleted == false)
                            {
                                this.MediaCore.Container?.SignalAbortReads(false);
                                Task.Delay(Constants.DefaultTimingPeriod).GetAwaiter().GetResult();
                            }

                            this.CommandCloseMedia();
                        }
                        catch (Exception ex)
                        {
                            this.LogWarning(Aspects.Container, $"Closing media via interrupt did not execute cleanly. {ex.Message}");
                        }
                        finally
                        {
                            this.IsCloseInterruptPending = false;
                            this.PostProcessDirectCommand(DirectCommandType.Close, null, false);
                        }

                        return true;
                    });
                }

                return this.ExecuteDirectCommand(DirectCommandType.Close, () => this.CommandCloseMedia());
            }
        }

        /// <summary>
        /// Changes the media components and applies new configuration.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean whether or not to resume media when completed.</returns>
        public Task<bool> ChangeMediaAsync() => this.ExecuteDirectCommand(DirectCommandType.Change, () => this.CommandChangeMedia(this.State.MediaState == MediaPlaybackState.Play));

        /// <summary>
        /// Plays the currently open media.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean result. True means success. False means failure.</returns>
        public Task<bool> PlayMediaAsync() => this.QueuePriorityCommand(PriorityCommandType.Play);

        /// <summary>
        /// Pauses the currently open media asynchronous.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean result. True means success. False means failure.</returns>
        public Task<bool> PauseMediaAsync() => this.QueuePriorityCommand(PriorityCommandType.Pause);

        /// <summary>
        /// Stops the currently open media. This seeks to the start of the input and pauses the clock.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean result. True means success. False means failure.</returns>
        public Task<bool> StopMediaAsync() => this.QueuePriorityCommand(PriorityCommandType.Stop);

        /// <summary>
        /// Queues a seek operation.
        /// </summary>
        /// <param name="seekTarget">The seek target.</param>
        /// <returns>An awaitable task which contains a boolean result. True means success. False means failure.</returns>
        public Task<bool> SeekMediaAsync(TimeSpan seekTarget) => this.QueueSeekCommand(seekTarget, SeekMode.Normal);

        /// <summary>
        /// Queues a seek operation that steps a single frame forward.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean result. True means success. False means failure.</returns>
        public Task<bool> StepForwardAsync() => this.QueueSeekCommand(TimeSpan.Zero, SeekMode.StepForward);

        /// <summary>
        /// Queues a seek operation that steps a single frame backward.
        /// </summary>
        /// <returns>An awaitable task which contains a boolean result. True means success. False means failure.</returns>
        public Task<bool> StepBackwardAsync() => this.QueueSeekCommand(TimeSpan.Zero, SeekMode.StepBackward);

        /// <summary>
        /// When a seek operation is in progress, this method blocks until the first block of the main
        /// component is available.
        /// </summary>
        /// <param name="millisecondsTimeout">The timeout to wait for.</param>
        /// <returns>If the wait completed successfully.</returns>
        public bool WaitForSeekBlocks(int millisecondsTimeout) => this.SeekBlocksAvailable.Wait(millisecondsTimeout);

        /// <inheritdoc />
        protected override void ExecuteCycleLogic(CancellationToken ct)
        {
            // Before anything, let's process priority commands
            var priorityCommand = this.PendingPriorityCommand;
            if (priorityCommand != PriorityCommandType.None)
            {
                // Pause all the workers in preparation for execution
                this.MediaCore.Workers.PauseAll();

                // Execute the pending priority command
                if (priorityCommand == PriorityCommandType.Play)
                {
                    this.CommandPlayMedia();
                }
                else if (priorityCommand == PriorityCommandType.Pause)
                {
                    this.CommandPauseMedia();
                }
                else if (priorityCommand == PriorityCommandType.Stop)
                {
                    this.CommandStopMedia();
                }
                else
                {
                    throw new NotSupportedException($"Command '{priorityCommand}' is not supported");
                }

                // Finish the command execution
                this.ClearSeekCommands();
                this.ClearPriorityCommands();
                this.MediaCore.Workers.ResumeAll();
                return;
            }

            // Perform current and queued seeks.
            while (true)
            {
                SeekOperation seekOperation;
                lock (this.SyncLock)
                {
                    seekOperation = this.QueuedSeekOperation;
                    this.QueuedSeekOperation = null;
                    this.QueuedSeekTask = null;
                }

                if (seekOperation == null)
                {
                    break;
                }

                this.ActiveSeekMode = seekOperation.Mode;
                this.SeekMedia(seekOperation, ct);
            }

            // Handle the case when there is no more seeking needed.
            lock (this.SyncLock)
            {
                if (this.IsSeeking && this.QueuedSeekOperation == null)
                {
                    this.IsSeeking = false;

                    // Resume the workers since the seek media operation
                    // might have required pausing them.
                    this.State.ReportPlaybackPosition();
                    this.MediaCore.Workers.ResumePaused();

                    // Resume if requested
                    if (this.PlayAfterSeek == true)
                    {
                        this.PlayAfterSeek = false;
                        this.State.MediaState = MediaPlaybackState.Play;
                    }
                    else
                    {
                        if (this.State.MediaState != MediaPlaybackState.Stop)
                        {
                            this.State.MediaState = MediaPlaybackState.Pause;
                        }
                    }

                    this.MediaCore.SendOnSeekingEnded();
                }
            }
        }

        /// <inheritdoc />
        protected override void OnCycleException(Exception ex) =>
            this.LogError(Aspects.EngineCommand, "Command Manager Exception Thrown", ex);

        /// <inheritdoc />
        protected override void OnDisposing()
        {
            this.LogDebug(Aspects.EngineCommand, "Dispose Entered. Waiting for Command Manager processor to stop.");
            this.ClearPriorityCommands();
            this.ClearSeekCommands();
            this.SeekBlocksAvailable.Set();

            // wait for any pending direct commands (unlikely)
            this.LogDebug(Aspects.EngineCommand, "Dispose is waiting for pending direct commands.");
            while (this.IsDirectCommandPending)
            {
                Task.Delay(Constants.DefaultTimingPeriod).Wait();
            }

            this.LogDebug(Aspects.EngineCommand, "Dispose is closing media.");
            try
            {
                // Execute the close media logic directly
                this.CommandCloseMedia();
                this.PostProcessDirectCommand(DirectCommandType.Close, null, false);
            }
            catch (Exception ex)
            {
                this.LogError(Aspects.EngineCommand, "Dispose had issues closing media. This is most likely a bug.", ex);
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool alsoManaged)
        {
            // Call the base dispose method
            base.Dispose(alsoManaged);

            // Dispose unmanged resources
            this.PriorityCommandCompleted.Dispose();
            this.SeekBlocksAvailable.Dispose();
            this.QueuedSeekOperation?.Dispose();
            this.QueuedSeekOperation = null;
            this.LogDebug(Aspects.EngineCommand, "Dispose completed.");
        }

        /// <summary>
        /// Outputs Reference Counter Results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogReferenceCounter()
        {
            if (!Debugger.IsAttached)
            {
                return;
            }

            if (RC.Current.InstancesByLocation.Count <= 0)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Unmanaged references are still alive. If there are no further media container instances to be disposed,");
            builder.AppendLine("this is an indication that there is a memory leak. Otherwise, this message can be ignored.");
            foreach (var kvp in RC.Current.InstancesByLocation)
            {
                builder.AppendLine($"    {kvp.Key,30} - Instances: {kvp.Value}");
            }

            this.LogError(Aspects.ReferenceCounter, builder.ToString());
        }
    }
}
