// <copyright file="MediaComponentSet.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Primitives;

    /// <summary>
    /// Represents a set of Audio, Video and Subtitle components.
    /// This class is useful in order to group all components into
    /// a single set. Sending packets is automatically handled by
    /// this class. This class is thread safe.
    /// </summary>
    public sealed class MediaComponentSet : IDisposable
    {
        // Synchronization locks
        private readonly object componentSyncLock = new object();
        private readonly object bufferSyncLock = new object();
        private readonly AtomicBoolean localIsDisposed = new AtomicBoolean(false);

        private IReadOnlyList<MediaComponent> localAll = new List<MediaComponent>(0);
        private IReadOnlyList<MediaType> localMediaTypes = new List<MediaType>(0);

        private int localCount;
        private MediaType localSeekableMediaType = MediaType.None;
        private MediaComponent localSeekable;
        private VideoComponent localVideo;
        private PacketBufferState localBufferState;

        /// <summary>
        /// Packet queue changed delegate.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="avPacket">The av packet.</param>
        /// <param name="mediaType">The media type.</param>
        /// <param name="bufferState">The buffer state.</param>
        public delegate void OnPacketQueueChangedDelegate(
            PacketQueueOp operation,
            MediaPacket avPacket,
            MediaType mediaType,
            PacketBufferState bufferState);

        /// <summary>
        /// Frame decoded delegate.
        /// </summary>
        /// <param name="avFrame">The frame pointer.</param>
        /// <param name="mediaType">The media type.</param>
        public delegate void OnFrameDecodedDelegate(IntPtr avFrame, MediaType mediaType);

        /// <summary>
        /// Subtitle decoded delegate.
        /// </summary>
        /// <param name="avSubtitle">The subtitle pointer.</param>
        public delegate void OnSubtitleDecodedDelegate(IntPtr avSubtitle);

        /// <summary>
        /// Gets or sets a method that gets called when a packet is queued.
        /// </summary>
        public OnPacketQueueChangedDelegate OnPacketQueueChanged { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when an audio or video frame gets decoded.
        /// </summary>
        public OnFrameDecodedDelegate OnFrameDecoded { get; set; }

        /// <summary>
        /// Gets or sets a method that gets called when a subtitle frame gets decoded.
        /// </summary>
        public OnSubtitleDecodedDelegate OnSubtitleDecoded { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed => this.localIsDisposed.Value;

        /// <summary>
        /// Gets the registered component count.
        /// </summary>
        public int Count
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localCount;
                }
            }
        }

        /// <summary>
        /// Gets the available component media types.
        /// </summary>
        public IReadOnlyList<MediaType> MediaTypes
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localMediaTypes;
                }
            }
        }

        /// <summary>
        /// Gets all the components in a read-only collection.
        /// </summary>
        public IReadOnlyList<MediaComponent> All
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localAll;
                }
            }
        }

        /// <summary>
        /// Gets the type of the component on which seek and frame stepping is performed.
        /// </summary>
        public MediaType SeekableMediaType
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localSeekableMediaType;
                }
            }
        }

        /// <summary>
        /// Gets the media component of the stream on which seeking and frame stepping is performed.
        /// By order of priority, first Video (not containing picture attachments), then audio.
        /// </summary>
        public MediaComponent Seekable
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localSeekable;
                }
            }
        }

        /// <summary>
        /// Gets the video component.
        /// Returns null when there is no such stream component.
        /// </summary>
        public VideoComponent Video
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localVideo;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance has a video component.
        /// </summary>
        public bool HasVideo
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return this.localVideo != null;
                }
            }
        }

        /// <summary>
        /// Gets the current length in bytes of the packet buffer for all components.
        /// These packets are the ones that have not been yet decoded.
        /// </summary>
        public long BufferLength
        {
            get
            {
                lock (this.bufferSyncLock)
                {
                    return this.localBufferState.Length;
                }
            }
        }

        /// <summary>
        /// Gets the total number of packets in the packet buffer for all components.
        /// </summary>
        public int BufferCount
        {
            get
            {
                lock (this.bufferSyncLock)
                {
                    return this.localBufferState.Count;
                }
            }
        }

        /// <summary>
        /// Gets the the least duration between the buffered audio and video packets.
        /// If no duration information is encoded in neither, this property will return
        /// <see cref="TimeSpan.MinValue"/>.
        /// </summary>
        public TimeSpan BufferDuration
        {
            get
            {
                lock (this.bufferSyncLock)
                {
                    return this.localBufferState.Duration;
                }
            }
        }

        /// <summary>
        /// Gets the minimum number of packets to read before <see cref="HasEnoughPackets"/> is able to return true.
        /// </summary>
        public int BufferCountThreshold
        {
            get
            {
                lock (this.bufferSyncLock)
                {
                    return this.localBufferState.CountThreshold;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether all packet queues contain enough packets.
        /// Port of ffplay.c stream_has_enough_packets.
        /// </summary>
        public bool HasEnoughPackets
        {
            get
            {
                lock (this.bufferSyncLock)
                {
                    return this.localBufferState.HasEnoughPackets;
                }
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="MediaComponent"/> with the specified media type.
        /// Setting a new component on an existing media type component will throw.
        /// Getting a non existing media component fro the given media type will return null.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        /// <returns>The media component.</returns>
        /// <exception cref="ArgumentException">When the media type is invalid.</exception>
        /// <exception cref="ArgumentNullException">MediaComponent.</exception>
        public MediaComponent this[MediaType mediaType]
        {
            get
            {
                lock (this.componentSyncLock)
                {
                    return mediaType switch
                    {
                        MediaType.Video => this.localVideo,
                        _ => null,
                    };
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() => this.Dispose(true);

        /// <summary>
        /// Sends the specified packet to the correct component by reading the stream index
        /// of the packet that is being sent. No packet is sent if the provided packet is set to null.
        /// Returns the media type of the component that accepted the packet.
        /// </summary>
        /// <param name="packet">The packet.</param>
        /// <returns>The media type.</returns>
        public MediaType SendPacket(MediaPacket packet)
        {
            if (packet == null)
            {
                return MediaType.None;
            }

            foreach (var component in this.All)
            {
                if (component.StreamIndex != packet.StreamIndex)
                {
                    continue;
                }

                component.SendPacket(packet);
                return component.MediaType;
            }

            return MediaType.None;
        }

        /// <summary>
        /// Sends an empty packet to all media components.
        /// When an EOF/EOS situation is encountered, this forces
        /// the decoders to enter draining mode until all frames are decoded.
        /// </summary>
        public void SendEmptyPackets()
        {
            foreach (var component in this.All)
            {
                component.SendEmptyPacket();
            }
        }

        /// <summary>
        /// Clears the packet queues for all components.
        /// Additionally it flushes the codec buffered packets.
        /// This is useful after a seek operation is performed or a stream
        /// index is changed.
        /// </summary>
        /// <param name="flushBuffers">if set to <c>true</c> flush codec buffers.</param>
        public void ClearQueuedPackets(bool flushBuffers)
        {
            foreach (var component in this.All)
            {
                component.ClearQueuedPackets(flushBuffers);
            }
        }

        /// <summary>
        /// Updates queue properties and invokes the on packet queue changed callback.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="packet">The packet.</param>
        /// <param name="mediaType">Type of the media.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ProcessPacketQueueChanges(PacketQueueOp operation, MediaPacket packet, MediaType mediaType)
        {
            if (this.OnPacketQueueChanged == null)
            {
                return;
            }

            var state = default(PacketBufferState);
            state.HasEnoughPackets = true;
            state.Duration = TimeSpan.MaxValue;

            foreach (var c in this.All)
            {
                state.Length += c.BufferLength;
                state.Count += c.BufferCount;
                state.CountThreshold += c.BufferCountThreshold;
                if (c.HasEnoughPackets == false)
                {
                    state.HasEnoughPackets = false;
                }

                if ((c.MediaType == MediaType.Video) &&
                    c.BufferDuration != TimeSpan.MinValue &&
                    c.BufferDuration.Ticks < state.Duration.Ticks)
                {
                    state.Duration = c.BufferDuration;
                }
            }

            if (state.Duration == TimeSpan.MaxValue)
            {
                state.Duration = TimeSpan.MinValue;
            }

            // Update the buffer state
            lock (this.bufferSyncLock)
            {
                this.localBufferState = state;
            }

            // Send the callback
            this.OnPacketQueueChanged?.Invoke(operation, packet, mediaType, state);
        }

        /// <summary>
        /// Registers the component in this component set.
        /// </summary>
        /// <param name="component">The component.</param>
        /// <exception cref="ArgumentNullException">When component of the same type is already registered.</exception>
        /// <exception cref="NotSupportedException">When MediaType is not supported.</exception>
        /// <exception cref="ArgumentException">When the component is null.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddComponent(MediaComponent component)
        {
            lock (this.componentSyncLock)
            {
                if (component == null)
                {
                    throw new ArgumentNullException(nameof(component));
                }

                var errorMessage = $"A component for '{component.MediaType}' is already registered.";
                switch (component.MediaType)
                {
                    case MediaType.Video:
                        if (this.localVideo != null)
                        {
                            throw new ArgumentException(errorMessage);
                        }

                        this.localVideo = component as VideoComponent;
                        break;
                    default:
                        throw new NotSupportedException($"Unable to register component with {nameof(MediaType)} '{component.MediaType}'");
                }

                this.UpdateComponentBackingFields();
            }
        }

        /// <summary>
        /// Removes the component of specified media type (if registered).
        /// It calls the dispose method of the media component too.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RemoveComponent(MediaType mediaType)
        {
            lock (this.componentSyncLock)
            {
                var component = default(MediaComponent);
                if (mediaType == MediaType.Video)
                {
                    component = this.localVideo;
                    this.localVideo = null;
                }

                component?.Dispose();
                this.UpdateComponentBackingFields();
            }
        }

        /// <summary>
        /// Computes the main component and backing fields.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void UpdateComponentBackingFields()
        {
            var allComponents = new List<MediaComponent>(4);
            var allMediaTypes = new List<MediaType>(4);

            // assign allMediaTypes. IMPORTANT: Order matters because this
            // establishes the priority in which playback measures are computed
            if (this.localVideo != null)
            {
                allComponents.Add(this.localVideo);
                allMediaTypes.Add(MediaType.Video);
            }

            this.localAll = allComponents;
            this.localMediaTypes = allMediaTypes;
            this.localCount = allComponents.Count;

            // Try for the main component to be the video (if it's not stuff like audio album art, that is)
            if (this.localVideo != null && !this.localVideo.IsStillPictures)
            {
                this.localSeekable = this.localVideo;
                this.localSeekableMediaType = MediaType.Video;
                return;
            }

            // We should never really hit this line (unless subtitles or data)
            this.localSeekable = null;
            this.localSeekableMediaType = MediaType.None;
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        private void Dispose(bool alsoManaged)
        {
            lock (this.componentSyncLock)
            {
                if (this.IsDisposed || alsoManaged == false)
                {
                    return;
                }

                this.localIsDisposed.Value = true;
                foreach (var mediaType in this.localMediaTypes)
                {
                    this.RemoveComponent(mediaType);
                }
            }
        }
    }
}
