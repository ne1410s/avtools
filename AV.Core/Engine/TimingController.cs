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
        private readonly object SyncLock = new object();
        private readonly MediaTypeDictionary<RealTimeClock> Clocks = new MediaTypeDictionary<RealTimeClock>();
        private readonly MediaTypeDictionary<TimeSpan> Offsets = new MediaTypeDictionary<TimeSpan>();
        private bool IsReady;
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
                lock (this.SyncLock)
                {
                    if (!this.IsReady)
                    {
                        return Constants.DefaultSpeedRatio;
                    }

                    return this.Clocks[MediaType.None].SpeedRatio;
                }
            }

            set
            {
                lock (this.SyncLock)
                {
                    if (!this.IsReady)
                    {
                        return;
                    }

                    this.Clocks[MediaType.Audio].SpeedRatio = value;
                    this.Clocks[MediaType.Video].SpeedRatio = value;
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
                lock (this.SyncLock)
                {
                    return this.localReferenceType;
                }
            }

            private set
            {
                lock (this.SyncLock)
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
                lock (this.SyncLock)
                {
                    return this.localHasDisconnectedClocks;
                }
            }

            private set
            {
                lock (this.SyncLock)
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
            lock (this.SyncLock)
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
                foreach (var kvp in this.Clocks)
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
                            $"{nameof(MediaOptions)}.{nameof(MediaOptions.IsTimeSyncDisabled)} has been ignored because the " +
                            $"streams seem to have unrelated timing information. Time Difference: {startTimeDifference.Format()} s.");

                        options.IsTimeSyncDisabled = true;
                    }
                }
                finally
                {
                    if (components.HasAudio && components.HasVideo)
                    {
                        this.Clocks[MediaType.Audio] = new RealTimeClock();
                        this.Clocks[MediaType.Video] = new RealTimeClock();

                        this.Offsets[MediaType.Audio] = this.GetComponentStartOffset(MediaType.Audio);
                        this.Offsets[MediaType.Video] = this.GetComponentStartOffset(MediaType.Video);
                    }
                    else
                    {
                        this.Clocks[MediaType.Audio] = new RealTimeClock();
                        this.Clocks[MediaType.Video] = this.Clocks[MediaType.Audio];

                        this.Offsets[MediaType.Audio] = this.GetComponentStartOffset(components.HasAudio ? MediaType.Audio : MediaType.Video);
                        this.Offsets[MediaType.Video] = this.Offsets[MediaType.Audio];
                    }

                    // Subtitles will always be whatever the video data is.
                    this.Clocks[MediaType.Subtitle] = this.Clocks[MediaType.Video];
                    this.Offsets[MediaType.Subtitle] = this.Offsets[MediaType.Video];

                    // Update from previous clocks to keep state
                    foreach (var clock in lastClocks)
                    {
                        this.Clocks[clock.Key].SpeedRatio = clock.Value.SpeedRatio;
                        this.Clocks[clock.Key].Update(clock.Value.Position);
                    }

                    // By default the continuous type is the audio component if it's a live stream
                    var continuousType = components.HasAudio && !this.MediaCore.Container.IsStreamSeekable
                        ? MediaType.Audio
                        : components.SeekableMediaType;

                    var discreteType = components.SeekableMediaType;
                    this.HasDisconnectedClocks = options.IsTimeSyncDisabled && this.Clocks[MediaType.Audio] != this.Clocks[MediaType.Video];
                    this.ReferenceType = this.HasDisconnectedClocks ? continuousType : discreteType;

                    // The default data is what the clock reference contains
                    this.Clocks[MediaType.None] = this.Clocks[this.ReferenceType];
                    this.Offsets[MediaType.None] = this.Offsets[this.ReferenceType];
                    this.IsReady = true;

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
                lock (this.SyncLock)
                {
                    this.IsReady = false;
                    this.Clocks.Clear();
                    this.Offsets.Clear();
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
            lock (this.SyncLock)
            {
                if (!this.IsReady)
                {
                    return default;
                }

                return TimeSpan.FromTicks(
                    this.Clocks[t].Position.Ticks +
                    this.Offsets[this.HasDisconnectedClocks ? t : this.ReferenceType].Ticks);
            }
        }

        /// <summary>
        /// Gets the playback duration of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The duration of the component type.</returns>
        public TimeSpan? GetDuration(MediaType t)
        {
            lock (this.SyncLock)
            {
                return this.IsReady ? this.GetComponentDuration(t) : default;
            }
        }

        /// <summary>
        /// Gets the start time of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The duration of the component type.</returns>
        public TimeSpan GetStartTime(MediaType t)
        {
            lock (this.SyncLock)
            {
                return this.IsReady ? this.Offsets[t] : default;
            }
        }

        /// <summary>
        /// Gets the playback end time of the given component type.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>The duration of the component type.</returns>
        public TimeSpan? GetEndTime(MediaType t)
        {
            lock (this.SyncLock)
            {
                var duration = this.GetComponentDuration(t);
                return this.IsReady ? duration.HasValue ? TimeSpan.FromTicks(duration.Value.Ticks + this.Offsets[t].Ticks) : default : default;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the RTC of the given component type is running.
        /// </summary>
        /// <param name="t">The t.</param>
        /// <returns>Whether the component's RTC is running.</returns>
        public bool GetIsRunning(MediaType t)
        {
            lock (this.SyncLock)
            {
                if (!this.IsReady)
                {
                    return default;
                }

                return this.Clocks[t].IsRunning;
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
                lock (this.SyncLock)
                {
                    if (!this.IsReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.Clocks[MediaType.Audio].Update(TimeSpan.FromTicks(
                            position.Ticks -
                            this.Offsets[this.HasDisconnectedClocks ? MediaType.Audio : this.ReferenceType].Ticks));

                        this.Clocks[MediaType.Video].Update(TimeSpan.FromTicks(
                            position.Ticks -
                            this.Offsets[this.HasDisconnectedClocks ? MediaType.Video : this.ReferenceType].Ticks));

                        return;
                    }

                    this.Clocks[t].Update(TimeSpan.FromTicks(
                        position.Ticks -
                        this.Offsets[this.HasDisconnectedClocks ? t : this.ReferenceType].Ticks));
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
                lock (this.SyncLock)
                {
                    if (!this.IsReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.Clocks[MediaType.Audio].Pause();
                        this.Clocks[MediaType.Video].Pause();
                        return;
                    }

                    this.Clocks[t].Pause();
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
                lock (this.SyncLock)
                {
                    if (!this.IsReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.Clocks[MediaType.Audio].Reset();
                        this.Clocks[MediaType.Video].Reset();
                        return;
                    }

                    this.Clocks[t].Reset();
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
                lock (this.SyncLock)
                {
                    if (!this.IsReady)
                    {
                        return;
                    }

                    if (t == MediaType.None)
                    {
                        this.Clocks[MediaType.Audio].Play();
                        this.Clocks[MediaType.Video].Play();
                        return;
                    }

                    this.Clocks[t].Play();
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
