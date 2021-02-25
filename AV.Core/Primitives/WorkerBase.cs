// <copyright file="WorkerBase.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;

    internal abstract class WorkerBase : IWorker
    {
        private readonly object SyncLock = new object();
        private readonly Stopwatch CycleClock = new Stopwatch();
        private readonly ManualResetEventSlim WantedStateCompleted = new ManualResetEventSlim(true);

        private int localIsDisposed;
        private int localIsDisposing;
        private int localWorkerState = (int)WorkerState.Created;
        private int localWantedWorkerState = (int)WorkerState.Running;
        private CancellationTokenSource TokenSource = new CancellationTokenSource();

        protected WorkerBase(string name)
        {
            this.Name = name;
            this.CycleClock.Restart();
        }

        /// <summary>
        /// Gets the name of the worker.
        /// </summary>
        public string Name { get; }

        /// <inheritdoc />
        public WorkerState WorkerState
        {
            get => (WorkerState)Interlocked.CompareExchange(ref this.localWorkerState, 0, 0);
            private set => Interlocked.Exchange(ref this.localWorkerState, (int)value);
        }

        /// <inheritdoc />
        public bool IsDisposed
        {
            get => Interlocked.CompareExchange(ref this.localIsDisposed, 0, 0) != 0;
            private set => Interlocked.Exchange(ref this.localIsDisposed, value ? 1 : 0);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is currently being disposed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is disposing; otherwise, <c>false</c>.
        /// </value>
        protected bool IsDisposing
        {
            get => Interlocked.CompareExchange(ref this.localIsDisposing, 0, 0) != 0;
            private set => Interlocked.Exchange(ref this.localIsDisposing, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets the desired state of the worker.
        /// </summary>
        protected WorkerState WantedWorkerState
        {
            get => (WorkerState)Interlocked.CompareExchange(ref this.localWantedWorkerState, 0, 0);
            set => Interlocked.Exchange(ref this.localWantedWorkerState, (int)value);
        }

        /// <summary>
        /// Gets the elapsed time of the last cycle.
        /// </summary>
        protected TimeSpan LastCycleElapsed { get; private set; }

        /// <summary>
        /// Gets the elapsed time of the current cycle.
        /// </summary>
        protected TimeSpan CurrentCycleElapsed => this.CycleClock.Elapsed;

        /// <inheritdoc />
        public Task<WorkerState> StartAsync()
        {
            lock (this.SyncLock)
            {
                if (this.IsDisposed || this.IsDisposing)
                {
                    return Task.FromResult(this.WorkerState);
                }

                if (this.WorkerState == WorkerState.Created)
                {
                    this.WantedWorkerState = WorkerState.Running;
                    this.WorkerState = WorkerState.Running;
                    return Task.FromResult(this.WorkerState);
                }
                else if (this.WorkerState == WorkerState.Paused)
                {
                    this.WantedStateCompleted.Reset();
                    this.WantedWorkerState = WorkerState.Running;
                }
            }

            return this.RunWaitForWantedState();
        }

        /// <inheritdoc />
        public Task<WorkerState> PauseAsync()
        {
            lock (this.SyncLock)
            {
                if (this.IsDisposed || this.IsDisposing)
                {
                    return Task.FromResult(this.WorkerState);
                }

                if (this.WorkerState != WorkerState.Running)
                {
                    return Task.FromResult(this.WorkerState);
                }

                this.WantedStateCompleted.Reset();
                this.WantedWorkerState = WorkerState.Paused;
            }

            return this.RunWaitForWantedState();
        }

        /// <inheritdoc />
        public Task<WorkerState> ResumeAsync()
        {
            lock (this.SyncLock)
            {
                if (this.IsDisposed || this.IsDisposing)
                {
                    return Task.FromResult(this.WorkerState);
                }

                if (this.WorkerState != WorkerState.Paused)
                {
                    return Task.FromResult(this.WorkerState);
                }

                this.WantedStateCompleted.Reset();
                this.WantedWorkerState = WorkerState.Running;
            }

            return this.RunWaitForWantedState();
        }

        /// <inheritdoc />
        public Task<WorkerState> StopAsync()
        {
            lock (this.SyncLock)
            {
                if (this.IsDisposed || this.IsDisposing)
                {
                    return Task.FromResult(this.WorkerState);
                }

                if (this.WorkerState != WorkerState.Running && this.WorkerState != WorkerState.Paused)
                {
                    return Task.FromResult(this.WorkerState);
                }

                this.WantedStateCompleted.Reset();
                this.WantedWorkerState = WorkerState.Stopped;
                this.Interrupt();
            }

            return this.RunWaitForWantedState();
        }

        /// <inheritdoc />
        public virtual void Dispose() => this.Dispose(true);

        /// <summary>
        /// Releases unmanaged and optionally managed resources.
        /// </summary>
        /// <param name="alsoManaged">Determines if managed resources hsould also be released.</param>
        protected virtual void Dispose(bool alsoManaged)
        {
            this.StopAsync().Wait();

            lock (this.SyncLock)
            {
                if (this.IsDisposed || this.IsDisposing)
                {
                    return;
                }

                this.IsDisposing = true;
                this.WantedStateCompleted.Set();
                try
                {
                    this.OnDisposing();
                }
                catch
                { /* Ignore */
                }
                this.CycleClock.Reset();
                this.WantedStateCompleted.Dispose();
                this.TokenSource.Dispose();
                this.IsDisposed = true;
                this.IsDisposing = false;
            }
        }

        /// <summary>
        /// Handles the cycle logic exceptions.
        /// </summary>
        /// <param name="ex">The exception that was thrown.</param>
        protected virtual void OnCycleException(Exception ex)
        {
            // placeholder
        }

        /// <summary>
        /// This method is called automatically when <see cref="Dispose()"/> is called.
        /// Makes sure you release all resources within this call.
        /// </summary>
        protected virtual void OnDisposing()
        {
            // placeholder
        }

        /// <summary>
        /// Represents the user defined logic to be executed on a single worker cycle.
        /// Check the cancellation token continuously if you need responsive interrupts.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        protected abstract void ExecuteCycleLogic(CancellationToken ct);

        /// <summary>
        /// Interrupts a cycle or a wait operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void Interrupt() => this.TokenSource.Cancel();

        /// <summary>
        /// Tries to acquire a cycle for execution.
        /// </summary>
        /// <returns>True if a cycle should be executed.</returns>
        protected bool TryBeginCycle()
        {
            if (this.WorkerState == WorkerState.Created || this.WorkerState == WorkerState.Stopped)
            {
                return false;
            }

            this.LastCycleElapsed = this.CycleClock.Elapsed;
            this.CycleClock.Restart();

            lock (this.SyncLock)
            {
                this.WorkerState = this.WantedWorkerState;
                this.WantedStateCompleted.Set();

                if (this.WorkerState == WorkerState.Stopped)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Executes the cyle calling the user-defined code.
        /// </summary>
        protected void ExecuteCyle()
        {
            // Recreate the token source -- applies to cycle logic and delay
            var ts = this.TokenSource;
            if (ts.IsCancellationRequested)
            {
                this.TokenSource = new CancellationTokenSource();
                ts.Dispose();
            }

            if (this.WorkerState == WorkerState.Running)
            {
                try
                {
                    this.ExecuteCycleLogic(this.TokenSource.Token);
                }
                catch (Exception ex)
                {
                    this.OnCycleException(ex);
                }
            }
        }

        /// <summary>
        /// Returns a hot task that waits for the state of the worker to change.
        /// </summary>
        /// <returns>The awaitable state change task.</returns>
        private Task<WorkerState> RunWaitForWantedState() => Task.Run(() =>
        {
            while (!this.WantedStateCompleted.Wait(Constants.DefaultTimingPeriod))
            {
                this.Interrupt();
            }

            return this.WorkerState;
        });
    }
}
