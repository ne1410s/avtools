// <copyright file="MediaPacket.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Runtime.CompilerServices;
    using AV.Core.Primitives;
    using FFmpeg.AutoGen;

    /// <summary>
    /// A managed packet wrapper for the <see cref="AVPacket"/> struct.
    /// </summary>
    /// <seealso cref="IDisposable" />
    public sealed unsafe class MediaPacket : IDisposable
    {
        /// <summary>
        /// The flush packet data pointer.
        /// </summary>
        private static readonly IntPtr FlushPacketData = (IntPtr)ffmpeg.av_malloc(0);

        private readonly AtomicBoolean localIsDisposed = new AtomicBoolean(false);
        private readonly IntPtr localPointer;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaPacket"/> class.
        /// </summary>
        /// <param name="packet">The packet.</param>
        private MediaPacket(AVPacket* packet)
        {
            this.localPointer = new IntPtr(packet);
        }

        /// <summary>
        /// Gets the <see cref="AVPacket"/> pointer.
        /// </summary>
        public AVPacket* Pointer => this.localIsDisposed.Value ? null : (AVPacket*)this.localPointer;

        /// <summary>
        /// Gets the <see cref="AVPacket"/> safe pointer.
        /// </summary>
        public IntPtr SafePointer => this.localIsDisposed.Value ? IntPtr.Zero : this.localPointer;

        /// <summary>
        /// Gets the size in bytes.
        /// </summary>
        public int Size => this.localIsDisposed.Value ? 0 : ((AVPacket*)this.localPointer)->size;

        /// <summary>
        /// Gets the byte position of the packet -1 if unknown.
        /// </summary>
        public long Position => this.localIsDisposed.Value ? 0 : ((AVPacket*)this.localPointer)->pos;

        /// <summary>
        /// Gets the stream index this packet belongs to.
        /// </summary>
        public int StreamIndex => this.localIsDisposed.Value ? -1 : ((AVPacket*)this.localPointer)->stream_index;

        /// <summary>
        /// Gets the duration in stream timebase units.
        /// </summary>
        public long Duration => this.localIsDisposed.Value ? -1 : ((AVPacket*)this.localPointer)->duration;

        /// <summary>
        /// Gets a value indicating whether the packet is a flush packet.
        /// These flush packets are used to clear the internal decoder buffers.
        /// </summary>
        public bool IsFlushPacket => !this.localIsDisposed.Value && (IntPtr)((AVPacket*)this.localPointer)->data == FlushPacketData;

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed => this.localIsDisposed.Value;

        /// <summary>
        /// Allocates a default readable packet.
        /// </summary>
        /// <returns>
        /// A packet used for receiving data.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MediaPacket CreateReadPacket()
        {
            var packet = new MediaPacket(ffmpeg.av_packet_alloc());
            RC.Current.Add(packet.Pointer);
            return packet;
        }

        /// <summary>
        /// Creates the empty packet.
        /// </summary>
        /// <param name="streamIndex">The stream index of the packet.</param>
        /// <returns>Special packet to enter decoder's draining mode.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MediaPacket CreateEmptyPacket(int streamIndex)
        {
            var packet = new MediaPacket(ffmpeg.av_packet_alloc());
            RC.Current.Add(packet.Pointer);
            ffmpeg.av_init_packet(packet.Pointer);
            packet.Pointer->data = null;
            packet.Pointer->size = 0;
            packet.Pointer->stream_index = streamIndex;
            return packet;
        }

        /// <summary>
        /// Creates a flush packet.
        /// </summary>
        /// <param name="streamIndex">The stream index of the packet.</param>
        /// <returns>A special packet to flush decoder buffers.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MediaPacket CreateFlushPacket(int streamIndex)
        {
            var packet = new MediaPacket(ffmpeg.av_packet_alloc());
            RC.Current.Add(packet.Pointer);
            ffmpeg.av_init_packet(packet.Pointer);
            packet.Pointer->data = (byte*)FlushPacketData;
            packet.Pointer->size = 0;
            packet.Pointer->stream_index = streamIndex;

            return packet;
        }

        /// <summary>
        /// Clones the packet.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>The packet clone.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MediaPacket ClonePacket(AVPacket* source)
        {
            var packet = new MediaPacket(ffmpeg.av_packet_clone(source));
            RC.Current.Add(packet.Pointer);
            return packet;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.localIsDisposed.Value)
            {
                return;
            }

            this.localIsDisposed.Value = true;

            if (this.localPointer == IntPtr.Zero)
            {
                return;
            }

            var packetPointer = (AVPacket*)this.localPointer;
            RC.Current.Remove(packetPointer);
            ffmpeg.av_packet_free(&packetPointer);
        }
    }
}
