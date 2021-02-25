// <copyright file="CommandManager.Direct.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Engine;
    using AV.Core.Primitives;
    using FFmpeg.AutoGen;

    internal partial class CommandManager
    {
        private readonly AtomicBoolean HasDirectCommandCompleted = new (true);
        private readonly AtomicInteger localPendingDirectCommand = new ((int)DirectCommandType.None);
        private readonly AtomicBoolean localIsCloseInterruptPending = new (false);

        /// <summary>
        /// Gets a value indicating whether a <see cref="OpenMediaAsync(Uri)"/> operation is in progress.
        /// </summary>
        public bool IsOpening => this.PendingDirectCommand == DirectCommandType.Open;

        /// <summary>
        /// Gets a value indicating whether a <see cref="CloseMediaAsync"/> operation is in progress.
        /// </summary>
        public bool IsClosing => this.PendingDirectCommand == DirectCommandType.Close;

        /// <summary>
        /// Gets a value indicating whether a <see cref="ChangeMediaAsync"/> operation is in progress.
        /// </summary>
        public bool IsChanging => this.PendingDirectCommand == DirectCommandType.Change;

        /// <summary>
        /// Gets a value indicating the direct command that is pending or in progress.
        /// </summary>
        private DirectCommandType PendingDirectCommand
        {
            get => (DirectCommandType)this.localPendingDirectCommand.Value;
            set
            {
                this.localPendingDirectCommand.Value = (int)value;
                this.State.ReportCommandStatus();
            }
        }

        /// <summary>
        /// Gets a value indicating whether a direct command is pending or in progress.
        /// </summary>
        private bool IsDirectCommandPending =>
            this.PendingDirectCommand != DirectCommandType.None ||
            this.HasDirectCommandCompleted.Value == false;

        /// <summary>
        /// Gets or sets a value indicating whether a close interrupt is pending.
        /// </summary>
        private bool IsCloseInterruptPending
        {
            get => this.localIsCloseInterruptPending.Value;
            set => this.localIsCloseInterruptPending.Value = value;
        }

        /// <summary>
        /// Execute boilerplate logic required ofr the execution of direct commands.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="commandDeleagte">The command deleagte.</param>
        /// <returns>The awaitable task.</returns>
        private Task<bool> ExecuteDirectCommand(DirectCommandType command, Func<bool> commandDeleagte)
        {
            lock (this.SyncLock)
            {
                // Check the basic conditions for a direct command to execute
                if (this.IsDisposed || this.IsDisposing)
                {
                    this.LogWarning(Aspects.EngineCommand, $"Direct Command '{command}' not accepted. Commanding is disposed or a command is pending completion.");
                    return Task.FromResult(false);
                }

                if (this.IsDirectCommandPending || command == DirectCommandType.None)
                {
                    this.LogWarning(Aspects.EngineCommand, $"Direct Command '{command}' not accepted. {this.PendingDirectCommand} command is pending completion.");
                    return Task.FromResult(false);
                }

                if (this.IsCloseInterruptPending && command != DirectCommandType.Close)
                {
                    this.LogWarning(Aspects.EngineCommand, $"Direct Command '{command}' not accepted. Close interrupt is pending completion.");
                    return Task.FromResult(false);
                }

                // Check if we are already open
                if (command == DirectCommandType.Open && (this.State.IsOpen || this.State.IsOpening))
                {
                    this.LogWarning(Aspects.EngineCommand, $"Direct Command '{command}' not accepted. Close the media before calling Open.");
                    return Task.FromResult(false);
                }

                // Close or Change Require the media to be open
                if ((command == DirectCommandType.Close || command == DirectCommandType.Change) && !this.State.IsOpen)
                {
                    this.LogWarning(Aspects.EngineCommand, $"Direct Command '{command}' not accepted. Open media before closing or changing media.");
                    return Task.FromResult(false);
                }

                this.LogDebug(Aspects.EngineCommand, $"Direct Command '{command}' accepted. Perparing execution.");

                this.PendingDirectCommand = command;
                this.HasDirectCommandCompleted.Value = false;
                this.MediaCore.PausePlayback();

                var commandTask = new Task<bool>(() =>
                {
                    var commandResult = false;
                    var resumeResult = false;
                    Exception commandException = null;

                    // Cause an immediate packet read abort if we need to close
                    if (command == DirectCommandType.Close)
                    {
                        this.MediaCore.Container?.SignalAbortReads(false);
                    }

                    // Pause the media core workers
                    this.MediaCore.Workers?.PauseAll();

                    // pause the queue processor
                    this.PauseAsync().Wait();

                    // clear the command queue and requests
                    this.ClearPriorityCommands();
                    this.ClearSeekCommands();

                    // execute the command
                    try
                    {
                        this.LogDebug(Aspects.EngineCommand, $"Direct Command '{command}' entered");
                        resumeResult = commandDeleagte.Invoke();
                    }
                    catch (Exception ex)
                    {
                        this.LogError(Aspects.EngineCommand, $"Direct Command '{command}' execution error", ex);
                        commandException = ex;
                        commandResult = false;
                    }

                    // We are done executing -- Update the commanding state
                    // The post-procesor will use the new IsOpening, IsClosing and IsChanging states
                    this.PendingDirectCommand = DirectCommandType.None;

                    try
                    {
                        // Update the sate based on command result
                        commandResult = this.PostProcessDirectCommand(command, commandException, resumeResult);

                        // Resume the workers and this processor if we are in the Open state
                        if (this.State.IsOpen && commandResult)
                        {
                            // Resume the media core workers
                            this.MediaCore.Workers.ResumePaused();

                            // Resume this queue processor
                            this.ResumeAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        commandResult = false;
                        this.LogError(Aspects.EngineCommand, $"Direct Command '{command}' postprocessing error", ex);
                    }
                    finally
                    {
                        // Allow for a new direct command to be processed
                        this.HasDirectCommandCompleted.Value = true;
                        this.LogDebug(Aspects.EngineCommand, $"Direct Command '{command}' completed. Result: {commandResult}");
                    }

                    return commandResult;
                });

                commandTask.Start();
                return commandTask;
            }
        }

        /// <summary>
        /// Executes boilerplate logic required when a direct command finishes executing.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="commandException">The command exception -- can be null.</param>
        /// <param name="resumeMedia">Only valid for the change command.</param>
        /// <returns>Fasle if there was an exception passed as an argument. True if null was passed to command exception.</returns>
        private bool PostProcessDirectCommand(DirectCommandType command, Exception commandException, bool resumeMedia)
        {
            if (command == DirectCommandType.Open)
            {
                this.State.UpdateFixedContainerProperties();

                if (commandException == null)
                {
                    this.State.MediaState = MediaPlaybackState.Stop;
                    this.MediaCore.SendOnMediaOpened();
                }
                else
                {
                    if (!this.IsCloseInterruptPending)
                    {
                        this.MediaCore.ResetPlaybackPosition();
                        this.State.MediaState = MediaPlaybackState.Close;
                        this.MediaCore.SendOnMediaFailed(commandException);
                        this.MediaCore.SendOnMediaClosed();
                    }
                }
            }
            else if (command == DirectCommandType.Close)
            {
                // Update notification properties
                this.MediaCore.Timing.Reset();
                this.State.ResetAll();
                this.MediaCore.ResetPlaybackPosition();
                this.State.MediaState = MediaPlaybackState.Close;
                this.State.UpdateSource(null);

                // Notify media has closed
                this.MediaCore.SendOnMediaClosed();
                this.LogReferenceCounter();
            }
            else if (command == DirectCommandType.Change)
            {
                this.State.UpdateFixedContainerProperties();

                if (commandException == null)
                {
                    this.MediaCore.SendOnMediaChanged();

                    // command result contains the play after seek.
                    this.State.MediaState = resumeMedia ? MediaPlaybackState.Play : MediaPlaybackState.Pause;
                }
                else
                {
                    if (!this.IsCloseInterruptPending)
                    {
                        this.MediaCore.SendOnMediaFailed(commandException);
                        this.State.MediaState = MediaPlaybackState.Pause;
                    }
                }
            }

            // return true if there was no exception found running the command.
            return commandException == null;
        }

        /// <summary>
        /// Provides the implementation for the Open Media Command.
        /// </summary>
        /// <param name="inputStream">The input stream.</param>
        /// <param name="streamUri">The stream URI.</param>
        /// <returns>Always returns false because media will not be resumed.</returns>
        /// <exception cref="MediaContainerException">Unable to initialize at least one audio or video component from the input stream.</exception>
        private bool CommandOpenMedia(IMediaInputStream inputStream, Uri streamUri)
        {
            try
            {
                // TODO: Sometimes when the stream can't be read, the sample player stays as if it were trying to open
                // until the interrupt timeout occurs but and the Real-Time Clock continues. Strange behavior. Investigate more.

                // Signal the initial state
                var source = inputStream == null ? streamUri : inputStream.StreamUri;
                this.MediaCore.Timing.Reset();
                this.State.ResetAll();
                this.State.UpdateSource(source);

                // Register FFmpeg libraries if not already done
                if (Library.LoadFFmpeg())
                {
                    // Log an init message
                    this.LogInfo(
                        Aspects.EngineCommand,
                        $"{nameof(FFInterop)}.{nameof(FFInterop.Initialize)}: FFmpeg v{Library.FFmpegVersionInfo}");
                }

                // Create a default stream container configuration object
                var containerConfig = new ContainerConfiguration();

                // Convert the URI object to something the Media Container understands (Uri to String)
                var mediaSource = source.IsWellFormedOriginalString()
                    ? source.OriginalString
                    : Uri.EscapeUriString(source.OriginalString);

                // When opening via URL (and not via custom input stream), fix up the protocols and stuff
                if (inputStream == null)
                {
                    try
                    {
                        // the async protocol prefix allows for increased performance for local files.
                        // or anything that is file-system related
                        if (source.IsFile || source.IsUnc)
                        {
                            // Set the default protocol Prefix
                            // The async protocol prefix by default does not ssem to provide
                            // any performance improvements. Just leaving it for future reference below.
                            // containerConfig.ProtocolPrefix = "async"
                            mediaSource = source.LocalPath;
                        }
                    }
                    catch
                    { /* Ignore exception and continue */
                    }

                    // Support device URLs
                    // GDI GRAB: Example URI: device://gdigrab?desktop
                    if (string.IsNullOrWhiteSpace(source.Scheme) == false
                        && (source.Scheme == "format" || source.Scheme == "device")
                        && string.IsNullOrWhiteSpace(source.Host) == false
                        && string.IsNullOrWhiteSpace(containerConfig.ForcedInputFormat)
                        && string.IsNullOrWhiteSpace(source.Query) == false)
                    {
                        // Update the Input format and container input URL
                        // It is also possible to set some input options as follows:
                        // Example: streamOptions.PrivateOptions["framerate"] = "20"
                        containerConfig.ForcedInputFormat = source.Host;
                        mediaSource = Uri.UnescapeDataString(source.Query).TrimStart('?');
                        this.LogInfo(
                            Aspects.EngineCommand,
                            $"Media URI will be updated. Input Format: {source.Host}, Input Argument: {mediaSource}");
                    }
                }

                // Allow the stream input options to be changed
                this.MediaCore.SendOnMediaInitializing(containerConfig, mediaSource);

                // Instantiate the internal container using either a URL (default) or a custom input stream.
                this.MediaCore.Container = inputStream == null ?
                    new MediaContainer(mediaSource, containerConfig, this.MediaCore) :
                    new MediaContainer(inputStream, containerConfig, this.MediaCore);

                // Initialize the container
                this.MediaCore.Container.Initialize();

                // Notify the user media is opening and allow for media options to be modified
                // Stuff like audio and video filters and stream selection can be performed here.
                this.State.UpdateFixedContainerProperties();
                this.MediaCore.SendOnMediaOpening();

                // Side-load subtitles if requested
                this.PreLoadSubtitles();

                // Get the main container open
                this.MediaCore.Container.Open();

                // Reset buffering properties
                this.State.UpdateFixedContainerProperties();
                this.MediaCore.Timing.Setup();
                this.State.InitializeBufferingStatistics();

                // Check if we have at least audio or video here
                if (this.State.HasAudio == false && this.State.HasVideo == false)
                {
                    throw new MediaContainerException("Unable to initialize at least one audio or video component from the input stream.");
                }

                // Charge! We are good to go, fire up the worker threads!
                this.StartWorkers();
            }
            catch
            {
                try
                {
                    this.StopWorkers();
                }
                catch
                { /* Ignore any exceptions and continue */
                }
                try
                {
                    this.MediaCore.Container?.Dispose();
                }
                catch
                { /* Ignore any exceptions and continue */
                }
                this.DisposePreloadedSubtitles();
                this.MediaCore.Container = null;
                throw;
            }

            return false;
        }

        /// <summary>
        /// Provides the implementation for the Close Media Command.
        /// </summary>
        /// <returns>Always returns false because media will not be resumed.</returns>
        private bool CommandCloseMedia()
        {
            // Wait for the workers to stop
            this.StopWorkers();

            // Dispose the container
            this.MediaCore.Container?.Dispose();
            this.MediaCore.Container = null;

            return false;
        }

        /// <summary>
        /// Provides the implementation for the Change Media Command.
        /// </summary>
        /// <param name="playWhenCompleted">If media should be resume when the command gets pot processed.</param>
        /// <returns>Simply return the play when completed boolean if there are no exceptions.</returns>
        private bool CommandChangeMedia(bool playWhenCompleted)
        {
            // Signal a change so the user get the chance to update
            // selected streams and options
            this.MediaCore.SendOnMediaChanging();

            // Side load subtitles
            this.PreLoadSubtitles();

            // Recreate selected streams as media components
            this.MediaCore.Container.UpdateComponents();
            this.State.UpdateFixedContainerProperties();
            this.MediaCore.Timing.Setup();

            // Dispose unused rendered and blocks and create new ones
            this.InitializeRendering();

            // Depending on whether or not the media is seekable
            // perform either a seek operation or a quick buffering operation.
            if (this.State.IsSeekable)
            {
                // Let's simply do an automated seek
                this.SeekMedia(
                    new SeekOperation(this.MediaCore.PlaybackPosition, SeekMode.Normal),
                    CancellationToken.None);
            }
            else
            {
                this.MediaCore.InvalidateRenderers();
            }

            return playWhenCompleted;
        }

        /// <summary>
        /// Initializes the media block buffers and
        /// starts packet reader, frame decoder, and block rendering workers.
        /// </summary>
        private void StartWorkers()
        {
            // Ensure renderers and blocks are available
            this.InitializeRendering();

            // Instantiate the workers and fire them up.
            this.MediaCore.Workers = new MediaWorkerSet(this.MediaCore);
            this.MediaCore.Workers.Start();
        }

        /// <summary>
        /// Stops the packet reader, frame decoder, and block renderers.
        /// </summary>
        private void StopWorkers()
        {
            // Pause the clock so no further updates are propagated
            this.MediaCore.PausePlayback();

            // Cause an immediate Packet read abort
            this.MediaCore.Container?.SignalAbortReads(false);

            // This causes the workers to stop and dispose.
            this.MediaCore.Workers?.Dispose();

            // Call close on all renderers
            foreach (var renderer in this.MediaCore.Renderers.Values)
            {
                renderer.OnClose();
            }

            // Remove the renderers disposing of them
            this.MediaCore.Renderers.Clear();

            // Dispose the Blocks for all components
            foreach (var kvp in this.MediaCore.Blocks)
            {
                kvp.Value.Dispose();
            }

            this.MediaCore.Blocks.Clear();
            this.DisposePreloadedSubtitles();

            // Clear the render times
            this.MediaCore.CurrentRenderStartTime.Clear();

            // Reset the clock
            this.MediaCore.ResetPlaybackPosition();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaType[] GetCurrentComponentTypes()
        {
            var result = new List<MediaType>(4);

            var components = this.MediaCore.Container?.Components;
            if (components != null)
            {
                result.AddRange(components.MediaTypes);
            }

            if (this.MediaCore.PreloadedSubtitles != null)
            {
                result.Add(MediaType.Subtitle);
            }

            return result.Distinct().ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaType[] GetCurrentRenderingTypes()
        {
            var currentMediaTypes = new List<MediaType>(8);
            currentMediaTypes.AddRange(this.MediaCore?.Renderers?.Keys?.ToArray() ?? Array.Empty<MediaType>());
            currentMediaTypes.AddRange(this.MediaCore?.Blocks?.Keys?.ToArray() ?? Array.Empty<MediaType>());

            return currentMediaTypes.Distinct().ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeRendering()
        {
            var oldMediaTypes = this.GetCurrentRenderingTypes();

            // We always remove the audio renderer in case there is a change in audio device.
            if (this.MediaCore.Renderers.ContainsKey(MediaType.Audio))
            {
                this.MediaCore.Renderers[MediaType.Audio].OnClose();
                this.MediaCore.Renderers.Remove(MediaType.Audio);
            }

            // capture the newly selected media types
            var newMediaTypes = this.GetCurrentComponentTypes();

            // capture all media types
            var allMediaTypes = oldMediaTypes.Union(newMediaTypes).Distinct().ToArray();

            // find all existing component blocks and renderers that are no longer needed
            var removableRenderers = oldMediaTypes.Where(t => !newMediaTypes.Contains(t)).Distinct().ToArray();

            // find all existing component renderers that are no longer needed
            foreach (var t in removableRenderers)
            {
                // Remove the renderer for the component
                if (!this.MediaCore.Renderers.ContainsKey(t))
                {
                    continue;
                }

                this.MediaCore.Renderers[t].OnClose();
                this.MediaCore.Renderers.Remove(t);
            }

            // Remove blocks that no longer are required or don't match in cache size
            foreach (var t in allMediaTypes)
            {
                // if blocks don't exist we don't need to remove them
                if (!this.MediaCore.Blocks.ContainsKey(t))
                {
                    continue;
                }

                // if blocks are in the new components and match in block size,
                // we don't need to remove them.
                if (newMediaTypes.Contains(t) && this.MediaCore.Blocks[t].Capacity == Constants.GetMaxBlocks(t, this.MediaCore))
                {
                    continue;
                }

                this.MediaCore.Blocks[t].Dispose();
                this.MediaCore.Blocks.Remove(t);
            }

            // Create the block buffers and renderers as necessary
            foreach (var t in newMediaTypes)
            {
                if (this.MediaCore.Blocks.ContainsKey(t) == false)
                {
                    this.MediaCore.Blocks[t] = new MediaBlockBuffer(Constants.GetMaxBlocks(t, this.MediaCore), t);
                }

                if (this.MediaCore.Renderers.ContainsKey(t) == false)
                {
                    this.MediaCore.Renderers[t] = this.MediaCore.Connector.CreateRenderer(t, this.MediaCore);
                }

                this.MediaCore.Blocks[t].Clear();
                this.MediaCore.Renderers[t].OnStarting();
                this.MediaCore.InvalidateRenderer(t);
            }
        }

        /// <summary>
        /// Pre-loads the subtitles from the MediaOptions.SubtitlesUrl.
        /// </summary>
        private void PreLoadSubtitles()
        {
            this.DisposePreloadedSubtitles();
            var subtitlesUrl = this.MediaCore.MediaOptions.SubtitlesSource;

            // Don't load a thing if we don't have to
            if (string.IsNullOrWhiteSpace(subtitlesUrl))
            {
                return;
            }

            try
            {
                this.MediaCore.PreloadedSubtitles = Utilities.LoadBlocks(subtitlesUrl, MediaType.Subtitle, this.MediaCore);

                // Process and adjust subtitle delays if necessary
                if (this.MediaCore.MediaOptions.SubtitlesDelay != TimeSpan.Zero)
                {
                    var delay = this.MediaCore.MediaOptions.SubtitlesDelay;
                    for (var i = 0; i < this.MediaCore.PreloadedSubtitles.Count; i++)
                    {
                        var target = this.MediaCore.PreloadedSubtitles[i];
                        target.StartTime = TimeSpan.FromTicks(target.StartTime.Ticks + delay.Ticks);
                        target.EndTime = TimeSpan.FromTicks(target.EndTime.Ticks + delay.Ticks);
                        target.Duration = TimeSpan.FromTicks(target.EndTime.Ticks - target.StartTime.Ticks);
                    }
                }

                this.MediaCore.MediaOptions.IsSubtitleDisabled = true;
            }
            catch (MediaContainerException mex)
            {
                this.DisposePreloadedSubtitles();
                this.LogWarning(
                    Aspects.Component,
                    $"No subtitles to side-load found in media '{subtitlesUrl}'. {mex.Message}");
            }
        }

        /// <summary>
        /// Disposes the preloaded subtitles.
        /// </summary>
        private void DisposePreloadedSubtitles()
        {
            this.MediaCore.PreloadedSubtitles?.Dispose();
            this.MediaCore.PreloadedSubtitles = null;
        }
    }
}
