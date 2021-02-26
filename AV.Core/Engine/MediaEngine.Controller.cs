// <copyright file="MediaEngine.Controller.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Threading.Tasks;
    using AV.Core.Commands;
    using AV.Core.Common;
    using AV.Core.Container;

    /// <summary>
    /// Media engine.
    /// </summary>
    public partial class MediaEngine
    {
        /// <summary>
        /// Gets the command queue to be executed in the order they were sent.
        /// </summary>
        internal CommandManager Commands { get; }

        /// <summary>
        /// Gets or sets underlying media container that provides access to
        /// individual media component streams.
        /// </summary>
        internal MediaContainer Container { get; set; }

        /// <summary>
        /// Opens the media using the specified URI.
        /// </summary>
        /// <param name="uri">The URI.</param>
        /// <returns>The awaitable task.</returns>
        /// <exception cref="InvalidOperationException">Source.</exception>
        public Task<bool> Open(Uri uri)
        {
            if (uri != null)
            {
                return Task.Run(async () =>
                {
                    await this.Commands.CloseMediaAsync().ConfigureAwait(true);
                    return await this.Commands.OpenMediaAsync(uri).ConfigureAwait(true);
                });
            }
            else
            {
                return this.Commands.CloseMediaAsync();
            }
        }

        /// <summary>
        /// Opens the media using a custom media input stream.
        /// </summary>
        /// <param name="stream">The URI.</param>
        /// <returns>The awaitable task.</returns>
        /// <exception cref="InvalidOperationException">Source.</exception>
        public Task<bool> Open(IMediaInputStream stream)
        {
            if (stream != null)
            {
                return Task.Run(async () =>
                {
                    await this.Commands.CloseMediaAsync().ConfigureAwait(true);
                    return await this.Commands.OpenMediaAsync(stream).ConfigureAwait(true);
                });
            }
            else
            {
                return this.Commands.CloseMediaAsync();
            }
        }

        /// <summary>
        /// Closes the currently loaded media.
        /// </summary>
        /// <returns>The awaitable task.</returns>
        public Task<bool> Close() =>
            this.Commands.CloseMediaAsync();

        /// <summary>
        /// Requests new media options to be applied, including stream component selection.
        /// </summary>
        /// <returns>The awaitable command.</returns>
        public Task<bool> ChangeMedia() =>
            this.Commands.ChangeMediaAsync();

        /// <summary>
        /// Begins or resumes playback of the currently loaded media.
        /// </summary>
        /// <returns>The awaitable command.</returns>
        public Task<bool> Play() =>
            this.Commands.PlayMediaAsync();

        /// <summary>
        /// Pauses playback of the currently loaded media.
        /// </summary>
        /// <returns>The awaitable command.</returns>
        public Task<bool> Pause() =>
            this.Commands.PauseMediaAsync();

        /// <summary>
        /// Pauses and rewinds the currently loaded media.
        /// </summary>
        /// <returns>The awaitable command.</returns>
        public Task<bool> Stop() =>
            this.Commands.StopMediaAsync();

        /// <summary>
        /// Seeks to the specified position.
        /// </summary>
        /// <param name="position">New position for the player.</param>
        /// <returns>The awaitable command.</returns>
        public Task<bool> Seek(TimeSpan position) =>
            this.Commands.SeekMediaAsync(position);

        /// <summary>
        /// Seeks a single frame forward.
        /// </summary>
        /// <returns>The awaitable command.</returns>
        public Task<bool> StepForward() =>
            this.Commands.StepForwardAsync();

        /// <summary>
        /// Seeks a single frame backward.
        /// </summary>
        /// <returns>The awaitable command.</returns>
        public Task<bool> StepBackward() =>
            this.Commands.StepBackwardAsync();
    }
}
