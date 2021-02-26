// <copyright file="MediaEngineState.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Primitives;

    /// <summary>
    /// Contains all the status properties of the stream being handled by the media engine.
    /// </summary>
    public sealed class MediaEngineState : ViewModelBase, IMediaEngineState
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyDictionary = new Dictionary<string, string>(0);

        private readonly MediaEngine mediaCore;
        private readonly AtomicInteger localMediaState = new AtomicInteger((int)MediaPlaybackState.Close);
        private readonly AtomicBoolean localHasMediaEnded = new AtomicBoolean(default);

        private readonly AtomicBoolean localIsBuffering = new AtomicBoolean(default);
        private readonly AtomicLong localDecodingBitRate = new AtomicLong(default);
        private readonly AtomicDouble localBufferingProgress = new AtomicDouble(default);
        private readonly AtomicDouble localDownloadProgress = new AtomicDouble(default);
        private readonly AtomicLong localPacketBufferLength = new AtomicLong(default);
        private readonly AtomicTimeSpan localPacketBufferDuration = new AtomicTimeSpan(TimeSpan.MinValue);
        private readonly AtomicInteger localPacketBufferCount = new AtomicInteger(default);

        private readonly AtomicTimeSpan localFramePosition = new AtomicTimeSpan(default);
        private readonly AtomicTimeSpan localPosition = new AtomicTimeSpan(default);
        private readonly AtomicDouble localSpeedRatio = new AtomicDouble(Constants.DefaultSpeedRatio);
        private readonly AtomicDouble localVolume = new AtomicDouble(Constants.DefaultVolume);
        private readonly AtomicDouble localBalance = new AtomicDouble(Constants.DefaultBalance);
        private readonly AtomicBoolean localIsMuted = new AtomicBoolean(false);
        private readonly AtomicBoolean localScrubbingEnabled = new AtomicBoolean(true);
        private readonly AtomicBoolean localVerticalSyncEnabled = new AtomicBoolean(true);

        private Uri localSource;
        private bool localIsOpen;
        private TimeSpan localPositionStep;
        private long localBitRate;
        private IReadOnlyDictionary<string, string> localMetadata = EmptyDictionary;
        private bool localCanPause;
        private string localMediaFormat;
        private long localMediaStreamSize;
        private int localVideoStreamIndex;
        private int localAudioStreamIndex;
        private int localSubtitleStreamIndex;
        private bool localHasAudio;
        private bool localHasVideo;
        private bool localHasSubtitles;
        private string localVideoCodec;
        private long localVideoBitRate;
        private double localVideoRotation;
        private int localNaturalVideoWidth;
        private int localNaturalVideoHeight;
        private string localVideoAspectRatio;
        private double localVideoFrameRate;
        private string localAudioCodec;
        private long localAudioBitRate;
        private int localAudioChannels;
        private int localAudioSampleRate;
        private int localAudioBitsPerSample;
        private TimeSpan? localNaturalDuration;
        private TimeSpan? localPlaybackStartTime;
        private TimeSpan? localPlaybackEndTime;
        private bool localIsLiveStream;
        private bool localIsNetworkStream;
        private bool localIsSeekable;

        private string localVideoSmtpeTimeCode = string.Empty;
        private string localVideoHardwareDecoder = string.Empty;
        private bool localHasClosedCaptions;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaEngineState"/> class.
        /// </summary>
        /// <param name="mediaCore">The associated media core.</param>
        internal MediaEngineState(MediaEngine mediaCore)
            : base(false)
        {
            this.mediaCore = mediaCore;
            this.ResetAll();
        }

        /// <inheritdoc />
        public Uri Source
        {
            get => this.localSource;
            private set => this.SetProperty(ref this.localSource, value);
        }

        /// <inheritdoc />
        public double SpeedRatio
        {
            get => this.localSpeedRatio.Value;
            set => this.SetProperty(this.localSpeedRatio, value.Clamp(Constants.MinSpeedRatio, Constants.MaxSpeedRatio));
        }

        /// <inheritdoc />
        public double Volume
        {
            get => this.localVolume.Value;
            set => this.SetProperty(this.localVolume, value.Clamp(Constants.MinVolume, Constants.MaxVolume));
        }

        /// <inheritdoc />
        public double Balance
        {
            get => this.localBalance.Value;
            set => this.SetProperty(this.localBalance, value.Clamp(Constants.MinBalance, Constants.MaxBalance));
        }

        /// <inheritdoc />
        public bool IsMuted
        {
            get => this.localIsMuted.Value;
            set => this.SetProperty(this.localIsMuted, value);
        }

        /// <inheritdoc />
        public bool ScrubbingEnabled
        {
            get => this.localScrubbingEnabled.Value;
            set => this.SetProperty(this.localScrubbingEnabled, value);
        }

        /// <inheritdoc />
        public bool VerticalSyncEnabled
        {
            get => this.localVerticalSyncEnabled.Value;
            set => this.SetProperty(this.localVerticalSyncEnabled, value);
        }

        /// <inheritdoc />
        public MediaPlaybackState MediaState
        {
            get => (MediaPlaybackState)this.localMediaState.Value;
            internal set
            {
                var oldState = (MediaPlaybackState)this.localMediaState.Value;
                if (!this.SetProperty(this.localMediaState, (int)value))
                {
                    return;
                }

                this.ReportCommandStatus();
                this.ReportTimingStatus();
                this.mediaCore.SendOnMediaStateChanged(oldState, value);
            }
        }

        /// <inheritdoc />
        public TimeSpan Position
        {
            get => this.localPosition.Value;
            private set => this.SetProperty(this.localPosition, value);
        }

        /// <inheritdoc />
        public TimeSpan FramePosition
        {
            get => this.localFramePosition.Value;
            private set => this.SetProperty(this.localFramePosition, value);
        }

        /// <inheritdoc />
        public bool HasMediaEnded
        {
            get => this.localHasMediaEnded.Value;
            internal set
            {
                if (!this.SetProperty(this.localHasMediaEnded, value))
                {
                    return;
                }

                if (value)
                {
                    this.mediaCore.SendOnMediaEnded();
                }
            }
        }

        /// <inheritdoc />
        public string VideoSmtpeTimeCode
        {
            get => this.localVideoSmtpeTimeCode;
            private set => this.SetProperty(ref this.localVideoSmtpeTimeCode, value);
        }

        /// <inheritdoc />
        public string VideoHardwareDecoder
        {
            get => this.localVideoHardwareDecoder;
            private set => this.SetProperty(ref this.localVideoHardwareDecoder, value);
        }

        /// <inheritdoc />
        public bool HasClosedCaptions
        {
            get => this.localHasClosedCaptions;
            private set => this.SetProperty(ref this.localHasClosedCaptions, value);
        }

        /// <inheritdoc />
        public bool IsAtEndOfStream => this.mediaCore.Container?.IsAtEndOfStream ?? false;

        /// <inheritdoc />
        public bool IsPlaying => this.IsOpen && this.mediaCore.Timing.IsRunning;

        /// <inheritdoc />
        public bool IsPaused => this.IsOpen && !this.mediaCore.Timing.IsRunning;

        /// <inheritdoc />
        public bool IsSeeking => this.mediaCore.Commands?.IsSeeking ?? false;

        /// <inheritdoc />
        public bool IsClosing => this.mediaCore.Commands?.IsClosing ?? false;

        /// <inheritdoc />
        public bool IsOpening => this.mediaCore.Commands?.IsOpening ?? false;

        /// <inheritdoc />
        public bool IsChanging => this.mediaCore.Commands?.IsChanging ?? false;

        /// <inheritdoc />
        public bool IsOpen
        {
            get => this.localIsOpen;
            private set
            {
                this.SetProperty(ref this.localIsOpen, value);
                this.ReportTimingStatus();
            }
        }

        /// <inheritdoc />
        public TimeSpan PositionStep
        {
            get => this.localPositionStep;
            private set => this.SetProperty(ref this.localPositionStep, value);
        }

        /// <inheritdoc />
        public long BitRate
        {
            get => this.localBitRate;
            private set => this.SetProperty(ref this.localBitRate, value);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Metadata
        {
            get => this.localMetadata;
            private set => this.SetProperty(ref this.localMetadata, value);
        }

        /// <inheritdoc />
        public bool CanPause
        {
            get => this.localCanPause;
            private set => this.SetProperty(ref this.localCanPause, value);
        }

        /// <inheritdoc />
        public string MediaFormat
        {
            get => this.localMediaFormat;
            private set => this.SetProperty(ref this.localMediaFormat, value);
        }

        /// <inheritdoc />
        public long MediaStreamSize
        {
            get => this.localMediaStreamSize;
            private set => this.SetProperty(ref this.localMediaStreamSize, value);
        }

        /// <inheritdoc />
        public int VideoStreamIndex
        {
            get => this.localVideoStreamIndex;
            private set => this.SetProperty(ref this.localVideoStreamIndex, value);
        }

        /// <inheritdoc />
        public int AudioStreamIndex
        {
            get => this.localAudioStreamIndex;
            private set => this.SetProperty(ref this.localAudioStreamIndex, value);
        }

        /// <inheritdoc />
        public int SubtitleStreamIndex
        {
            get => this.localSubtitleStreamIndex;
            private set => this.SetProperty(ref this.localSubtitleStreamIndex, value);
        }

        /// <inheritdoc />
        public bool HasAudio
        {
            get => this.localHasAudio;
            private set => this.SetProperty(ref this.localHasAudio, value);
        }

        /// <inheritdoc />
        public bool HasVideo
        {
            get => this.localHasVideo;
            private set => this.SetProperty(ref this.localHasVideo, value);
        }

        /// <inheritdoc />
        public bool HasSubtitles
        {
            get => this.localHasSubtitles;
            private set => this.SetProperty(ref this.localHasSubtitles, value);
        }

        /// <inheritdoc />
        public string VideoCodec
        {
            get => this.localVideoCodec;
            private set => this.SetProperty(ref this.localVideoCodec, value);
        }

        /// <inheritdoc />
        public long VideoBitRate
        {
            get => this.localVideoBitRate;
            private set => this.SetProperty(ref this.localVideoBitRate, value);
        }

        /// <inheritdoc />
        public double VideoRotation
        {
            get => this.localVideoRotation;
            private set => this.SetProperty(ref this.localVideoRotation, value);
        }

        /// <inheritdoc />
        public int NaturalVideoWidth
        {
            get => this.localNaturalVideoWidth;
            private set => this.SetProperty(ref this.localNaturalVideoWidth, value);
        }

        /// <inheritdoc />
        public int NaturalVideoHeight
        {
            get => this.localNaturalVideoHeight;
            private set => this.SetProperty(ref this.localNaturalVideoHeight, value);
        }

        /// <inheritdoc />
        public string VideoAspectRatio
        {
            get => this.localVideoAspectRatio;
            private set => this.SetProperty(ref this.localVideoAspectRatio, value);
        }

        /// <inheritdoc />
        public double VideoFrameRate
        {
            get => this.localVideoFrameRate;
            private set => this.SetProperty(ref this.localVideoFrameRate, value);
        }

        /// <inheritdoc />
        public string AudioCodec
        {
            get => this.localAudioCodec;
            private set => this.SetProperty(ref this.localAudioCodec, value);
        }

        /// <inheritdoc />
        public long AudioBitRate
        {
            get => this.localAudioBitRate;
            private set => this.SetProperty(ref this.localAudioBitRate, value);
        }

        /// <inheritdoc />
        public int AudioChannels
        {
            get => this.localAudioChannels;
            private set => this.SetProperty(ref this.localAudioChannels, value);
        }

        /// <inheritdoc />
        public int AudioSampleRate
        {
            get => this.localAudioSampleRate;
            private set => this.SetProperty(ref this.localAudioSampleRate, value);
        }

        /// <inheritdoc />
        public int AudioBitsPerSample
        {
            get => this.localAudioBitsPerSample;
            private set => this.SetProperty(ref this.localAudioBitsPerSample, value);
        }

        /// <inheritdoc />
        public TimeSpan? NaturalDuration
        {
            get => this.localNaturalDuration;
            private set => this.SetProperty(ref this.localNaturalDuration, value);
        }

        /// <inheritdoc />
        public TimeSpan? PlaybackStartTime
        {
            get => this.localPlaybackStartTime;
            private set => this.SetProperty(ref this.localPlaybackStartTime, value);
        }

        /// <inheritdoc />
        public TimeSpan? PlaybackEndTime
        {
            get => this.localPlaybackEndTime;
            private set => this.SetProperty(ref this.localPlaybackEndTime, value);
        }

        /// <inheritdoc />
        public bool IsLiveStream
        {
            get => this.localIsLiveStream;
            private set => this.SetProperty(ref this.localIsLiveStream, value);
        }

        /// <inheritdoc />
        public bool IsNetworkStream
        {
            get => this.localIsNetworkStream;
            private set => this.SetProperty(ref this.localIsNetworkStream, value);
        }

        /// <inheritdoc />
        public bool IsSeekable
        {
            get => this.localIsSeekable;
            private set => this.SetProperty(ref this.localIsSeekable, value);
        }

        /// <inheritdoc />
        public bool IsBuffering
        {
            get => this.localIsBuffering.Value;
            private set => this.SetProperty(this.localIsBuffering, value);
        }

        /// <inheritdoc />
        public long DecodingBitRate
        {
            get => this.localDecodingBitRate.Value;
            private set => this.SetProperty(this.localDecodingBitRate, value);
        }

        /// <inheritdoc />
        public double BufferingProgress
        {
            get => this.localBufferingProgress.Value;
            private set => this.SetProperty(this.localBufferingProgress, value);
        }

        /// <inheritdoc />
        public double DownloadProgress
        {
            get => this.localDownloadProgress.Value;
            private set => this.SetProperty(this.localDownloadProgress, value);
        }

        /// <inheritdoc />
        public long PacketBufferLength
        {
            get => this.localPacketBufferLength.Value;
            private set => this.SetProperty(this.localPacketBufferLength, value);
        }

        /// <inheritdoc />
        public TimeSpan PacketBufferDuration
        {
            get => this.localPacketBufferDuration.Value;
            private set => this.SetProperty(this.localPacketBufferDuration, value);
        }

        /// <inheritdoc />
        public int PacketBufferCount
        {
            get => this.localPacketBufferCount.Value;
            private set
            {
                this.SetProperty(this.localPacketBufferCount, value);
                this.NotifyPropertyChanged(nameof(this.IsAtEndOfStream));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReportCommandStatus() => this.NotifyPropertyChanged(nameof(this.IsSeeking), nameof(this.IsClosing), nameof(this.IsOpening), nameof(this.IsChanging));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReportTimingStatus() => this.NotifyPropertyChanged(nameof(this.IsPlaying), nameof(this.IsPaused));

        /// <summary>
        /// Updates the <see cref="Source"/> property.
        /// </summary>
        /// <param name="newSource">The new source.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateSource(Uri newSource) => this.Source = newSource;

        /// <summary>
        /// Updates the fixed container properties.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateFixedContainerProperties()
        {
            this.BitRate = this.mediaCore.Container?.MediaBitRate ?? default;
            this.IsOpen = !this.IsOpening && (this.mediaCore.Container?.IsOpen ?? default);
            this.Metadata = this.mediaCore.Container?.Metadata ?? EmptyDictionary;
            this.MediaFormat = this.mediaCore.Container?.MediaFormatName;
            this.MediaStreamSize = this.mediaCore.Container?.MediaStreamSize ?? default;
            this.VideoStreamIndex = this.mediaCore.Container?.Components.Video?.StreamIndex ?? -1;
            this.AudioStreamIndex = this.mediaCore.Container?.Components.Audio?.StreamIndex ?? -1;
            this.SubtitleStreamIndex = this.mediaCore.Container?.Components.Subtitles?.StreamIndex ?? -1;
            this.HasAudio = this.mediaCore.Container?.Components.HasAudio ?? default;
            this.HasVideo = this.mediaCore.Container?.Components.HasVideo ?? default;
            this.HasClosedCaptions = this.mediaCore.Container?.Components.Video?.StreamInfo?.HasClosedCaptions ?? default;
            this.HasSubtitles = (this.mediaCore.PreloadedSubtitles?.Count ?? 0) > 0
                || (this.mediaCore.Container?.Components.HasSubtitles ?? false);
            this.VideoCodec = this.mediaCore.Container?.Components.Video?.CodecName;
            this.VideoBitRate = this.mediaCore.Container?.Components.Video?.BitRate ?? default;
            this.VideoRotation = this.mediaCore.Container?.Components.Video?.DisplayRotation ?? default;
            this.NaturalVideoWidth = this.mediaCore.Container?.Components.Video?.FrameWidth ?? default;
            this.NaturalVideoHeight = this.mediaCore.Container?.Components.Video?.FrameHeight ?? default;
            this.VideoFrameRate = this.mediaCore.Container?.Components.Video?.AverageFrameRate ?? default;
            this.AudioCodec = this.mediaCore.Container?.Components.Audio?.CodecName;
            this.AudioBitRate = this.mediaCore.Container?.Components.Audio?.BitRate ?? default;
            this.AudioChannels = this.mediaCore.Container?.Components.Audio?.Channels ?? default;
            this.AudioSampleRate = this.mediaCore.Container?.Components.Audio?.SampleRate ?? default;
            this.AudioBitsPerSample = this.mediaCore.Container?.Components.Audio?.BitsPerSample ?? default;
            this.NaturalDuration = this.mediaCore.Timing?.Duration;
            this.PlaybackStartTime = this.mediaCore.Timing?.StartTime;
            this.PlaybackEndTime = this.mediaCore.Timing?.EndTime;
            this.IsLiveStream = this.mediaCore.Container?.IsLiveStream ?? default;
            this.IsNetworkStream = this.mediaCore.Container?.IsNetworkStream ?? default;
            this.IsSeekable = this.mediaCore.Container?.IsStreamSeekable ?? default;
            this.CanPause = this.IsOpen ? !this.IsLiveStream : default;

            var videoAspectWidth = this.mediaCore.Container?.Components.Video?.DisplayAspectWidth ?? default;
            var videoAspectHeight = this.mediaCore.Container?.Components.Video?.DisplayAspectHeight ?? default;
            this.VideoAspectRatio = videoAspectWidth != default && videoAspectHeight != default ?
                $"{videoAspectWidth}:{videoAspectHeight}" : default;

            var seekableType = this.mediaCore.Container?.Components.SeekableMediaType ?? MediaType.None;
            var seekable = this.mediaCore.Container?.Components.Seekable;

            switch (seekableType)
            {
                case MediaType.Audio:
                    this.PositionStep = TimeSpan.FromTicks(Convert.ToInt64(
                        TimeSpan.TicksPerMillisecond * this.AudioSampleRate / 1000d));
                    break;

                case MediaType.Video:
                    var baseFrameRate = (seekable as VideoComponent)?.BaseFrameRate ?? 1d;
                    this.PositionStep = TimeSpan.FromTicks(Convert.ToInt64(
                        TimeSpan.TicksPerMillisecond * 1000d / baseFrameRate));
                    break;

                default:
                    this.PositionStep = default;
                    break;
            }
        }

        /// <summary>
        /// Updates state properties coming from a new media block.
        /// </summary>
        /// <param name="block">The block.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateDynamicBlockProperties(MediaBlock block)
        {
            if (block == null)
            {
                return;
            }

            // Update the discrete frame position upon rendering
            if (block.MediaType == (this.mediaCore.Container?.Components.SeekableMediaType ?? MediaType.None))
            {
                this.FramePosition = block.StartTime;
            }

            // Update video block properties
            if (block is VideoBlock == false)
            {
                return;
            }

            // Capture the video block
            var videoBlock = (VideoBlock)block;

            // I don't know of any codecs changing the width and the height dynamically
            // but we update the properties just to be safe.
            this.NaturalVideoWidth = videoBlock.PixelWidth;
            this.NaturalVideoHeight = videoBlock.PixelHeight;

            // Update the has closed captions state as it might come in later
            // as frames are decoded
            if (this.HasClosedCaptions == false && videoBlock.ClosedCaptions.Count > 0)
            {
                this.HasClosedCaptions = true;
            }

            this.VideoSmtpeTimeCode = videoBlock.SmtpeTimeCode;
            this.VideoHardwareDecoder = videoBlock.IsHardwareFrame ?
                videoBlock.HardwareAcceleratorName : string.Empty;
        }

        /// <summary>
        /// Updates the playback position and related properties.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReportPlaybackPosition() => this.ReportPlaybackPosition(this.mediaCore.PlaybackPosition);

        /// <summary>
        /// Updates the playback position related properties.
        /// </summary>
        /// <param name="newPosition">The new playback position.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReportPlaybackPosition(TimeSpan newPosition)
        {
            var oldSpeedRatio = this.mediaCore.Timing.SpeedRatio;
            var newSpeedRatio = this.SpeedRatio;

            if (Math.Abs(oldSpeedRatio - newSpeedRatio) > double.Epsilon)
            {
                this.mediaCore.Timing.SpeedRatio = this.SpeedRatio;
            }

            var oldPosition = this.Position;
            if (oldPosition.Ticks == newPosition.Ticks)
            {
                return;
            }

            this.Position = newPosition;
            this.mediaCore.SendOnPositionChanged(oldPosition, newPosition);
        }

        /// <summary>
        /// Resets all media state properties.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ResetAll()
        {
            this.ResetMediaProperties();
            this.UpdateFixedContainerProperties();
            this.InitializeBufferingStatistics();
            this.ReportCommandStatus();
            this.ReportTimingStatus();
        }

        /// <summary>
        /// Resets the controller properties.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ResetMediaProperties()
        {
            // Reset Method-controlled properties
            this.Position = default;
            this.FramePosition = default;
            this.HasMediaEnded = default;

            // Reset decoder and buffering
            this.ResetBufferingStatistics();

            this.VideoSmtpeTimeCode = string.Empty;
            this.VideoHardwareDecoder = string.Empty;

            // Reset controller properties
            this.SpeedRatio = Constants.DefaultSpeedRatio;

            this.MediaState = MediaPlaybackState.Close;
        }

        /// <summary>
        /// Resets all the buffering properties to their defaults.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InitializeBufferingStatistics()
        {
            const long MinimumValidFileSize = 1024 * 1024; // 1 MB

            // Start with default values
            this.ResetBufferingStatistics();

            // Reset the properties if the is no associated container
            if (this.mediaCore.Container == null)
            {
                this.MediaStreamSize = default;
                return;
            }

            // Try to get a valid stream size
            this.MediaStreamSize = this.mediaCore.Container.MediaStreamSize;
            var durationSeconds = this.NaturalDuration?.TotalSeconds ?? 0d;

            // Compute the bit rate and buffering properties based on media byte size
            if (this.MediaStreamSize >= MinimumValidFileSize && this.IsSeekable && durationSeconds > 0)
            {
                // The bit rate is simply the media size over the total duration
                this.BitRate = Convert.ToInt64(8d * this.MediaStreamSize / durationSeconds);
            }
        }

        /// <summary>
        /// Updates the decoding bit rate and duration of the reference timing component.
        /// </summary>
        /// <param name="bitRate">The bit rate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateDecodingStats(long bitRate)
        {
            this.DecodingBitRate = bitRate;
            this.NaturalDuration = this.mediaCore.Timing?.Duration;
            this.PlaybackStartTime = this.mediaCore.Timing?.StartTime;
            this.PlaybackEndTime = this.mediaCore.Timing?.EndTime;
        }

        /// <summary>
        /// Updates the buffering properties: <see cref="PacketBufferCount" />, <see cref="PacketBufferLength" />,
        /// <see cref="IsBuffering" />, <see cref="BufferingProgress" />, <see cref="DownloadProgress" />.
        /// If a change is detected on the <see cref="IsBuffering" /> property then a notification is sent.
        /// </summary>
        /// <param name="bufferLength">Length of the packet buffer.</param>
        /// <param name="bufferCount">The packet buffer count.</param>
        /// <param name="bufferCountMax">The packet buffer count maximum for all components.</param>
        /// <param name="bufferDuration">Duration of the packet buffer.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateBufferingStats(long bufferLength, int bufferCount, int bufferCountMax, TimeSpan bufferDuration)
        {
            this.PacketBufferCount = bufferCount;
            this.PacketBufferLength = bufferLength;
            this.PacketBufferDuration = bufferDuration;
            this.BufferingProgress = bufferCountMax <= 0 ? 0 : Math.Min(1d, (double)bufferCount / bufferCountMax);
            this.DownloadProgress = Math.Min(1d, (double)bufferLength / MediaEngine.BufferLengthMax);

            // Check if we are currently buffering
            var isCurrentlyBuffering = this.mediaCore.ShouldReadMorePackets
                && (this.mediaCore.IsSyncBuffering || this.BufferingProgress < 1d);

            // Detect and notify a change in buffering state
            if (isCurrentlyBuffering == this.IsBuffering)
            {
                return;
            }

            this.IsBuffering = isCurrentlyBuffering;
            if (isCurrentlyBuffering)
            {
                this.mediaCore.SendOnBufferingStarted();
            }
            else
            {
                this.mediaCore.SendOnBufferingEnded();
            }
        }

        /// <summary>
        /// Resets the buffering statistics.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetBufferingStatistics()
        {
            this.IsBuffering = default;
            this.DecodingBitRate = default;
            this.BufferingProgress = default;
            this.DownloadProgress = default;
            this.PacketBufferLength = default;
            this.PacketBufferDuration = TimeSpan.MinValue;
            this.PacketBufferCount = default;
        }
    }
}
