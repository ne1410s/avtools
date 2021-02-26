// <copyright file="MediaWorkerSet.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using AV.Core.Primitives;

    /// <summary>
    /// Provides a easy accessors to the Read, Decode and Render workers.
    /// </summary>
    /// <seealso cref="IDisposable" />
    internal sealed class MediaWorkerSet : IDisposable
    {
        private readonly object syncLock = new object();
        private readonly IMediaWorker[] workers = new IMediaWorker[3];
        private bool localIsDisposed;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaWorkerSet"/> class.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        public MediaWorkerSet(MediaEngine mediaCore)
        {
            this.MediaCore = mediaCore;

            this.Reading = new PacketReadingWorker(mediaCore);
            this.Decoding = new FrameDecodingWorker(mediaCore);
            this.Rendering = new BlockRenderingWorker(mediaCore);

            this.workers[(int)MediaWorkerType.Read] = this.Reading;
            this.workers[(int)MediaWorkerType.Decode] = this.Decoding;
            this.workers[(int)MediaWorkerType.Render] = this.Rendering;
        }

        /// <summary>
        /// Gets the media engine that owns this set of workers.
        /// </summary>
        public MediaEngine MediaCore { get; }

        /// <summary>
        /// Gets the packet reading worker.
        /// </summary>
        public PacketReadingWorker Reading { get; }

        /// <summary>
        /// Gets the frame decoding worker.
        /// </summary>
        public FrameDecodingWorker Decoding { get; }

        /// <summary>
        /// Gets the block rendering worker.
        /// </summary>
        public BlockRenderingWorker Rendering { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localIsDisposed;
                }
            }

            private set
            {
                lock (this.syncLock)
                {
                    this.localIsDisposed = value;
                }
            }
        }

        /// <summary>
        /// Gets the <see cref="IMediaWorker"/> with the specified worker type.
        /// </summary>
        /// <value>
        /// The <see cref="IMediaWorker"/>.
        /// </value>
        /// <param name="workerType">Type of the worker.</param>
        /// <returns>The matching worker.</returns>
        public IMediaWorker this[MediaWorkerType workerType] => this.workers[(int)workerType];

        /// <summary>
        /// Starts the workers.
        /// </summary>
        public void Start()
        {
            if (this.IsDisposed)
            {
                return;
            }

            var tasks = new Task[this.workers.Length];
            for (var i = 0; i < this.workers.Length; i++)
            {
                tasks[i] = this.workers[i].StartAsync();
            }

            Task.WaitAll(tasks);
        }

        /// <summary>
        /// Pauses all the media core workers and waits for the operation to complete.
        /// </summary>
        public void PauseAll() => this.Pause(true, true, true, true);

        /// <summary>
        /// Resumes all the media core workers waiting for the operation to complete.
        /// </summary>
        public void ResumeAll() => this.Resume(true, true, true, true);

        /// <summary>
        /// Pauses the reading and decoding workers waiting for the operation to complete.
        /// </summary>
        public void PauseReadDecode() => this.Pause(true, true, true, false);

        /// <summary>
        /// Resumes only those workers which are in the paused state.
        /// This prevents an interrupt being sent to the worker by calling
        /// its resume method.
        /// </summary>
        public void ResumePaused() => this.Resume(
            true,
            this.Reading.WorkerState == WorkerState.Paused,
            this.Decoding.WorkerState == WorkerState.Paused,
            this.Rendering.WorkerState == WorkerState.Paused);

        /// <inheritdoc />
        public void Dispose() => this.Dispose(true);

        /// <summary>
        /// Pauses the specified wrokers.
        /// </summary>
        /// <param name="wait">if set to <c>true</c> waits for the operation to complete.</param>
        /// <param name="read">if set to <c>true</c> executes the opration on the reading worker.</param>
        /// <param name="decode">if set to <c>true</c> executes the opration on the decoding worker.</param>
        /// <param name="render">if set to <c>true</c> executes the opration on the rendering worker.</param>
        private void Pause(bool wait, bool read, bool decode, bool render)
        {
            if (this.IsDisposed)
            {
                return;
            }

            var tasks = this.CaptureTasks(read, decode, render, WorkerState.Paused);
            if (wait)
            {
                Task.WaitAll(tasks);
            }
        }

        /// <summary>
        /// Resumes the specified wrokers.
        /// </summary>
        /// <param name="wait">if set to <c>true</c> waits for the operation to complete.</param>
        /// <param name="read">if set to <c>true</c> executes the opration on the reading worker.</param>
        /// <param name="decode">if set to <c>true</c> executes the opration on the decoding worker.</param>
        /// <param name="render">if set to <c>true</c> executes the opration on the rendering worker.</param>
        private void Resume(bool wait, bool read, bool decode, bool render)
        {
            if (this.IsDisposed)
            {
                return;
            }

            var tasks = this.CaptureTasks(read, decode, render, WorkerState.Running);
            if (wait)
            {
                Task.WaitAll(tasks);
            }
        }

        /// <summary>
        /// Captures the awaitable tasks for the given workers.
        /// </summary>
        /// <param name="read">The read worker.</param>
        /// <param name="decode">The decode worker.</param>
        /// <param name="render">The render worker.</param>
        /// <param name="targetState">The target state.</param>
        /// <returns>The awaitable tasks.</returns>
        private Task<WorkerState>[] CaptureTasks(bool read, bool decode, bool render, WorkerState targetState)
        {
            var tasks = new List<Task<WorkerState>>(3);
            var workers = new List<IMediaWorker>(3);

            if (read)
            {
                workers.Add(this.Reading);
            }

            if (decode)
            {
                workers.Add(this.Decoding);
            }

            if (render)
            {
                workers.Add(this.Rendering);
            }

            foreach (var worker in workers)
            {
                switch (targetState)
                {
                    case WorkerState.Paused:
                        tasks.Add(worker.PauseAsync());
                        break;
                    case WorkerState.Running:
                        tasks.Add(worker.ResumeAsync());
                        break;
                    case WorkerState.Stopped:
                        tasks.Add(worker.StopAsync());
                        break;
                    default:
                        throw new NotSupportedException($"{nameof(targetState)} '{targetState}' is not supported.");
                }
            }

            return tasks.ToArray();
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        private void Dispose(bool alsoManaged)
        {
            lock (this.syncLock)
            {
                if (this.IsDisposed)
                {
                    return;
                }

                this.IsDisposed = true;

                if (alsoManaged == false)
                {
                    return;
                }

                this.Pause(true, true, true, true);
                foreach (var worker in this.workers)
                {
                    worker.Dispose();
                }
            }
        }
    }
}
