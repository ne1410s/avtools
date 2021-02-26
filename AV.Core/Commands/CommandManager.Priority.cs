// <copyright file="CommandManager.Priority.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Commands
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using AV.Core.Common;
    using AV.Core.Primitives;

    /// <summary>
    /// Command manager.
    /// </summary>
    internal partial class CommandManager
    {
        private readonly AtomicInteger localPendingPriorityCommand = new (0);
        private readonly ManualResetEventSlim priorityCommandCompleted = new (true);

        /// <summary>
        /// Gets a value indicating whether a priority command is pending.
        /// </summary>
        private bool IsPriorityCommandPending => this.PendingPriorityCommand != PriorityCommandType.None;

        /// <summary>
        /// Executes boilerplate code that queues priority commands.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <returns>An awaitable task.</returns>
        private Task<bool> QueuePriorityCommand(PriorityCommandType command)
        {
            lock (this.syncLock)
            {
                if (this.IsDisposed || this.IsDisposing || !this.State.IsOpen || this.IsDirectCommandPending || this.IsPriorityCommandPending)
                {
                    return Task.FromResult(false);
                }

                this.PendingPriorityCommand = command;
                this.priorityCommandCompleted.Reset();

                var commandTask = new Task<bool>(() =>
                {
                    this.ResumeAsync().Wait();
                    this.priorityCommandCompleted.Wait();
                    return true;
                });

                commandTask.Start();
                return commandTask;
            }
        }

        /// <summary>
        /// Clears the priority commands and marks the completion event as set.
        /// </summary>
        private void ClearPriorityCommands()
        {
            lock (this.syncLock)
            {
                this.PendingPriorityCommand = PriorityCommandType.None;
                this.priorityCommandCompleted.Set();
            }
        }

        /// <summary>
        /// Provides the implementation for the Play Media Command.
        /// </summary>
        /// <returns>True if the command was successful.</returns>
        private bool CommandPlayMedia()
        {
            foreach (var renderer in this.MediaCore.Renderers.Values)
            {
                renderer.OnPlay();
            }

            this.State.MediaState = MediaPlaybackState.Play;

            return true;
        }

        /// <summary>
        /// Provides the implementation for the Pause Media Command.
        /// </summary>
        /// <returns>True if the command was successful.</returns>
        private bool CommandPauseMedia()
        {
            if (this.State.CanPause == false)
            {
                return false;
            }

            this.MediaCore.PausePlayback();

            foreach (var renderer in this.MediaCore.Renderers.Values)
            {
                renderer.OnPause();
            }

            this.MediaCore.ChangePlaybackPosition(this.SnapPositionToBlockPosition(this.MediaCore.PlaybackPosition));
            this.State.MediaState = MediaPlaybackState.Pause;
            return true;
        }

        /// <summary>
        /// Provides the implementation for the Stop Media Command.
        /// </summary>
        /// <returns>True if the command was successful.</returns>
        private bool CommandStopMedia()
        {
            if (this.State.IsSeekable == false)
            {
                return false;
            }

            this.MediaCore.ResetPlaybackPosition();

            this.SeekMedia(new SeekOperation(TimeSpan.MinValue, SeekMode.Stop), CancellationToken.None);

            foreach (var renderer in this.MediaCore.Renderers.Values)
            {
                renderer.OnStop();
            }

            this.State.MediaState = MediaPlaybackState.Stop;
            return true;
        }

        /// <summary>
        /// Returns the value of a discrete frame position of the main media component if possible.
        /// Otherwise, it simply rounds the position to the nearest millisecond.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The snapped, discrete, normalized position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan SnapPositionToBlockPosition(TimeSpan position)
        {
            if (this.MediaCore.Container == null)
            {
                return position.Normalize();
            }

            var t = this.MediaCore.Container?.Components?.SeekableMediaType ?? MediaType.None;
            var blocks = this.MediaCore.Blocks[t];
            if (blocks == null)
            {
                return position.Normalize();
            }

            return blocks.GetSnapPosition(position) ?? position.Normalize();
        }
    }
}
