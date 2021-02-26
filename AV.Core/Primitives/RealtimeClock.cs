// <copyright file="RealtimeClock.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// A time measurement artifact.
    /// </summary>
    internal sealed class RealTimeClock
    {
        private readonly Stopwatch chronometer = new Stopwatch();
        private readonly object syncLock = new object();
        private long offsetTicks;
        private double localSpeedRatio = Constants.DefaultSpeedRatio;

        /// <summary>
        /// Initialises a new instance of the <see cref="RealTimeClock"/> class.
        /// The clock starts paused and at the 0 position.
        /// </summary>
        public RealTimeClock() => this.Reset();

        /// <summary>
        /// Gets the clock position.
        /// </summary>
        public TimeSpan Position
        {
            get
            {
                lock (this.syncLock)
                {
                    return TimeSpan.FromTicks(
                        this.offsetTicks + Convert.ToInt64(this.chronometer.Elapsed.Ticks * this.SpeedRatio));
                }
            }
        }

        /// <summary>
        /// Gets the elapsed time of the internal stopwatch.
        /// </summary>
        public TimeSpan ElapsedInternal => this.chronometer.Elapsed;

        /// <summary>
        /// Gets a value indicating whether the clock is running.
        /// </summary>
        public bool IsRunning => this.chronometer.IsRunning;

        /// <summary>
        /// Gets or sets the speed ratio at which the clock runs.
        /// </summary>
        public double SpeedRatio
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localSpeedRatio;
                }
            }

            set
            {
                lock (this.syncLock)
                {
                    // Capture the initial position se we set it even after the speed ratio has changed
                    // this ensures a smooth position transition
                    var initialPosition = this.Position;
                    this.localSpeedRatio = value < 0d ? 0d : value;
                    this.Update(initialPosition);
                }
            }
        }

        /// <summary>
        /// Sets a new position value atomically.
        /// </summary>
        /// <param name="value">The new value that the position property will hold.</param>
        public void Update(TimeSpan value)
        {
            lock (this.syncLock)
            {
                var resume = this.chronometer.IsRunning;
                this.chronometer.Reset();
                this.offsetTicks = value.Ticks;
                if (resume)
                {
                    this.chronometer.Start();
                }
            }
        }

        /// <summary>
        /// Starts or resumes the clock.
        /// </summary>
        public void Play()
        {
            lock (this.syncLock)
            {
                if (this.chronometer.IsRunning)
                {
                    return;
                }

                this.chronometer.Start();
            }
        }

        /// <summary>
        /// Pauses the clock.
        /// </summary>
        public void Pause()
        {
            lock (this.syncLock)
            {
                this.chronometer.Stop();
            }
        }

        /// <summary>
        /// Sets the clock position to 0 and stops it.
        /// The speed ratio is not modified.
        /// </summary>
        public void Reset()
        {
            lock (this.syncLock)
            {
                this.offsetTicks = 0;
                this.chronometer.Reset();
            }
        }

        /// <summary>
        /// Sets the clock position to 0 and restarts it.
        /// The speed ratio is not modified.
        /// </summary>
        public void Restart()
        {
            lock (this.syncLock)
            {
                this.offsetTicks = 0;
                this.chronometer.Restart();
            }
        }

        /// <summary>
        /// Sets the clock position to the specificed offsetand restarts it.
        /// The speed ratio is not modified.
        /// </summary>
        /// <param name="offset">The offset to start at.</param>
        public void Restart(TimeSpan offset)
        {
            lock (this.syncLock)
            {
                this.offsetTicks = offset.Ticks;
                this.chronometer.Restart();
            }
        }
    }
}
