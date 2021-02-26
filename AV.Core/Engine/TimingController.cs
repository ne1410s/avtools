// <copyright file="TimingController.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Primitives;

    /// <summary>
    /// Implements a real-time clock controller capable of handling independent
    /// clocks for each of the components.
    /// </summary>
    internal sealed class TimingController
    {
        private readonly object syncLock = new object();
        private readonly MediaTypeDictionary<RealTimeClock> clocks = new MediaTypeDictionary<RealTimeClock>();
        private readonly MediaTypeDictionary<TimeSpan> offsets = new MediaTypeDictionary<TimeSpan>();
        private bool isReady;
        private MediaType localReferenceType;
        private bool localHasDisconnectedClocks;

        /// <summary>
        /// Initialises a new instance of the <see cref="TimingController"/> class.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        public TimingController(MediaEngine mediaCore)
        {
            this.MediaCore = mediaCore;
        }

        /// <summary>
        /// Gets or sets the speed ratio. All clocks are bound to the same value.
        /// </summary>
        public double SpeedRatio
        {
            get
            {
                lock (this.syncLock)
                {
                    if (!this.isReady)
                    {
                        return Constants.DefaultSpeedRatio;
                    }

                    return this.clocks[MediaType.None].SpeedRatio;
                }
            }

            set
            {
                lock (this.syncLock)
                {
                    if (!this.isReady)
                    {
                        return;
                    }

                    this.clocks[MediaType.Audio].SpeedRatio = value;
                    this.clocks[MediaType.Video].SpeedRatio = value;
                }
            }
        }

        /// <summary>
        /// Gets the clock type that positions are offset by.
        /// </summary>
        public MediaType ReferenceType
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localReferenceType;
                }
            }

            private set
            {
                lock (this.syncLock)
                {
                    this.localReferenceType = value;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the real-time clocks of the components are disconnected clocks.
        /// </summary>
        public bool HasDisconnectedClocks
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localHasDisconnectedClocks;
                }
            }

            private set
            {
                lock (this.syncLock)
                {
                    this.localHasDisconnectedClocks = value;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the real-time clock of the reference type is running.
        /// </summary>
        public bool IsRunning => this.GetIsRunning(this.ReferenceType);

        /// <summary>
        /// Gets the playback position of the real-time clock of the timing reference component type.
        /// </summary>
        /// <returns>The clock position.</returns>
        public TimeSpan Position => this.GetPosition(this.ReferenceType);

        /// <summary>
        /// Gets the duration of the reference component type.
        /// </summary>
        public TimeSpan? Duration => this.GetDuration(this.ReferenceType);

        /// <summary>
        /// Gets the start time of the reference component type.
        /// </summary>
        public TimeSpan StartTime => this.GetStartTime(this.ReferenceType);

        /// <summary>
        /// Gets the end time of the reference component type.
        /// </summary>
        public TimeSpan? EndTime => this.GetEndTime(this.ReferenceType);

        /// <summary>
        /// Gets the media core.
        /// </summary>
        private MediaEngine MediaCore { get; }

        /// <summary>
        /// Sets up timing and clocks. Call this method when media components change.
        /// </summary>
        public void Setup()
        {
            lock (this.syncLock)
            {
                var options = this.MediaCore?.MediaOptions;
                var components = this.MediaCore?.Container?.Components;

                if (components == null || options == null)
                {
                    this.MediaCore?.LogError(Aspects.Timing, "Unable to setup the timing controller. No components or options found.");
                    this.Reset();
                    return;
                }

                // Save the current clocks so they can be recreated with the
                // same properties (position and speed ratio)
                var lastClocks = new MediaTypeDictionary<RealTimeClock>();
                foreach (var kvp in this.clocks)
                {
                    lastClocks[kvp.Key] = kvp.Value;
                }

                try
                {
                    if (options.IsTimeSyncDisabled)
                    {
                        if (!this.MediaCore.Container.IsLiveStream)
                        {
                            this.MediaCore.LogWarning(
                                Aspects.Timing,
                                $"Media options had {nameof(MediaOptions.IsTimeSyncDisabled)} set to true. This is not recommended for non-live streams.");
                        }

                        return;
                    }

                    if (!components.HasAudio || !components.HasVideo)
                    {
                        return;
                    }

                    var audioStartTime = this.GetComponentStartOffset(MediaType.Audio);
                    var videoStartTime = this.GetComponentStartOffset(MediaType.Video);
                    var startTimeDifference = TimeSpan.FromTicks(Math.Abs(audioStartTime.Ticks - videoStartTime.Ticks));

                    if (startTimeDifference > Constants.TimeSyncMaxOffset)
                    {
                        this.MediaCore.LogWarning(
                            Aspects.Timing,
                            $"{nameof(MediaOptions)}.{nameof(MediaOptions.IsTimeSyncDisabled)} has been ignored because the streams seem to have unrelated timing information. Time Difference: {startTimeDifference.Format()} s.");

                        options.IsTimeSyncDisabled = true;
                    }
                }
                finally
                {
                    if (components.HasAudio && components.HasVideo)
                    {
                        this.clocks[MediaType.Audio] = new RealTimeClock();
                        this.clocks[MediaType.Video] = new RealTimeClock();

                        this.offsets[MediaType.Audio] = this.GetComponentStartOffset(MediaType.Audio);
                        this.offsets[MediaType.Video] = this.GetComponentStartOffset(MediaType.Video);
                    }
                    else
                    {
                        this.clocks[MediaType.Audio] = new RealTimeClock();
                        this.clocks[MediaType.Video] = this.clocks[MediaType.Audio];

                        this.offsets[MediaType.Audio] = this.GetComponentStartOffset(components.HasAudio ? MediaType.Audio : MediaType.Video);
                        this.offsets[MediaType.Video] = this.offsets[MediaType.Audio];
                    }

                    // Subtitles will always be whatever the video data is.
                    this.clocks[MediaType.Subtitle] = this.clocks[MediaType.Video];
                    this.offsets[MediaType.Subtitle] = this.offsets[MediaType.Video];

                    // Update from previous clocks to keep state
                    foreach (var clock in lastClocks)
                    {
                        this.clocks[clock.Key].SpeedRatio = clock.Value.SpeedRatio;
                        this.clocks[clock.Key].Update(clock.Value.Position);
                    }

                    // By default the continuous type is the audio component if it's a live stream
                    var continuousType = components.HasAudio && !this.MediaCore.Container.IsStreamSeekable
                        ? MediaType.Audio
                        : components.SeekableMediaType;

                    var discreteType = components.SeekableMediaType;
                    this.HasDisconnectedClocks = options.IsTimeSyncDisabled && this.clocks[MediaType.Audio] != this.clocks[MediaType.Video];
                    this.ReferenceType = this.HasDisconnectedClocks ? continuousType : discreteType;

                    // The default data is what the clock reference contains
                    this.clocks[MediaType.None] = this.clocks[this.ReferenceType];
                    this.offsets[MediaType.None] = this.offsets[this.ReferenceType];
                    this.isReady = true;

                    this.MediaCore.State.ReportTimingStatus();
                }
            }
        }

        /// <summary>
        /// Clears all component clocks and timing offsets.
        /// </summary>
        public void Reset()
        {
            try
            {
                lock (this.syncLock)
                {
                    this.isReady = false;
                    this.clocks.Clear();
                    this.offsets.Clear();
                }
            }
            finally
            {
                this.MediaCore.State.ReportTimingStatus();
            }
        }

        /// <summary>
        /// Gets the playback position of the real-time clock of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The clock position.</returns>
        public TimeSpan GetPosition(MediaType t)
        {
            lock (this.syncLock)
            {
                if (!this.isReady)
                {
                    return default;
                }

                return TimeSpan.FromTicks(
                    this.clocks[t].Position.Ticks +
                    this.offsets[this.HasDisconnectedClocks ? t : this.ReferenceType].Ticks);
            }
        }

        /// <summary>
        /// Gets the playback duration of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The duration of the component type.</returns>
        public TimeSpan? GetDuration(MediaType t)
        {
            lock (this.syncLock)
            {
                return this.isReady ? this.GetComponentDuration(t) : default;
            }
        }

        /// <summary>
        /// Gets the start time of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The duration of the component type.</returns>
        public TimeSpan GetStartTime(MediaType t)
        {
            lock (this.syncLock)
            {
                return this.isReady ? this.offsets[t] : default;
            }
        }

        /// <summary>
        /// Gets the playback end time of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The duration of the component type.</returns>
        public TimeSpan? GetEndTime(MediaType t)
        {
            lock (this.syncLock)
            {
                var duration = this.GetComponentDuration(t);
                return this.isReady ? duration.HasValue ? TimeSpan.FromTicks(duration.Value.Ticks + this.offsets[t].Ticks) : default : default;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the RTC of the given component type is running.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>Whether the component's RTC is running.</returns>
        public bool GetIsRunning(MediaType t)
        {
            lock (this.syncLock)
            {
                if (!this.isReady)
                {
                    return default;
                }

                return this.clocks[t].IsRunning;
            }
        }

        /// <summary>
        /// Updates the position of the component's clock. Pass none to update all clocks to the same postion.
        /// </summary>
        /// <param name="position">The position to update to.</param>
        /// <param name="t">The clock's media type.</param>
        public void Update(TimeSpan position, MediaType t)
        {
            try
            {
                lock (this.syncLock)
                {
                    if (!this.isReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.clocks[MediaType.Audio].Update(TimeSpan.FromTicks(
                            position.Ticks -
                            this.offsets[this.HasDisconnectedClocks ? MediaType.Audio : this.ReferenceType].Ticks));

                        this.clocks[MediaType.Video].Update(TimeSpan.FromTicks(
                            position.Ticks -
                            this.offsets[this.HasDisconnectedClocks ? MediaType.Video : this.ReferenceType].Ticks));

                        return;
                    }

                    this.clocks[t].Update(TimeSpan.FromTicks(
                        position.Ticks -
                        this.offsets[this.HasDisconnectedClocks ? t : this.ReferenceType].Ticks));
                }
            }
            finally
            {
                this.MediaCore.State.ReportTimingStatus();
            }
        }

        /// <summary>
        /// Pauses the specified clock. Pass none to pause all clocks.
        /// </summary>
        /// <param name="t">The clock type.</param>
        public void Pause(MediaType t)
        {
            try
            {
                lock (this.syncLock)
                {
                    if (!this.isReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.clocks[MediaType.Audio].Pause();
                        this.clocks[MediaType.Video].Pause();
                        return;
                    }

                    this.clocks[t].Pause();
                }
            }
            finally
            {
                this.MediaCore.State.ReportTimingStatus();
            }
        }

        /// <summary>
        /// Resets the position of the specified clock. Pass none to reset all.
        /// </summary>
        /// <param name="t">The media type.</param>
        public void Reset(MediaType t)
        {
            try
            {
                lock (this.syncLock)
                {
                    if (!this.isReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.clocks[MediaType.Audio].Reset();
                        this.clocks[MediaType.Video].Reset();
                        return;
                    }

                    this.clocks[t].Reset();
                }
            }
            finally
            {
                this.MediaCore.State.ReportTimingStatus();
            }
        }

        /// <summary>
        /// Plays or resumes the specified clock. Pass none to play all.
        /// </summary>
        /// <param name="t">The media type.</param>
        public void Play(MediaType t)
        {
            try
            {
                lock (this.syncLock)
                {
                    if (!this.isReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.clocks[MediaType.Audio].Play();
                        this.clocks[MediaType.Video].Play();
                        return;
                    }

                    this.clocks[t].Play();
                }
            }
            finally
            {
                this.MediaCore.State.ReportTimingStatus();
            }
        }

        /// <summary>
        /// Gets the component start offset.
        /// </summary>
        /// <param name="t">The component media type.</param>
        /// <returns>The component start time.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan GetComponentStartOffset(MediaType t)
        {
            if (this.MediaCore?.Container?.Components is MediaComponentSet components && components[t] is MediaComponent component)
            {
                return component.StartTime == TimeSpan.MinValue ? TimeSpan.Zero : component.StartTime;
            }

            return TimeSpan.Zero;
        }

        /// <summary>
        /// Gets the component duration.
        /// </summary>
        /// <param name="t">The component media type.</param>
        /// <returns>The component duration time.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TimeSpan? GetComponentDuration(MediaType t)
        {
            if (this.MediaCore?.Container?.Components is MediaComponentSet components && components[t] is MediaComponent component)
            {
                return component.Duration.Ticks <= 0 ? default(TimeSpan?) : component.Duration;
            }

            return TimeSpan.Zero;
        }
    }
}
