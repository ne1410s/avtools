﻿// <copyright file="DataFrame.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System;
    using AV.Core.Container;
    using AV.Core.Engine;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents a frame of non multimedia data read from an input stream.
    /// </summary>
    public sealed unsafe class DataFrame
    {
        private readonly byte[] localPacketData;

        /// <summary>
        /// Initialises a new instance of the <see cref="DataFrame"/> class.
        /// </summary>
        /// <param name="packet">The packet.</param>
        /// <param name="stream">The stream.</param>
        /// <param name="mediaCore">The associated media engine.</param>
        internal DataFrame(MediaPacket packet, StreamInfo stream, MediaEngine mediaCore)
        {
            this.StreamIndex = stream == null ? -1 : stream.StreamIndex;
            this.localPacketData = default;

            // Copy data to the Memory struct
            var bufferLength = packet.Pointer->size;
            if (bufferLength > 0)
            {
                var targetData = new byte[bufferLength];
                fixed (byte* targetPointer = &targetData[0])
                {
                    Buffer.MemoryCopy(packet.Pointer->data, targetPointer, bufferLength, bufferLength);
                }

                this.localPacketData = targetData;
            }

            this.PacketPosition = packet.Position;
            this.PacketPresetnationTimestamp = packet.Pointer->pts;
            this.PacketDecodingTimestamp = packet.Pointer->dts;

            this.DecodingTime = stream == null
                ? TimeSpan.MinValue
                : this.PacketDecodingTimestamp.ToTimeSpan(stream.TimeBase);

            this.StartTime = stream == null
                ? TimeSpan.MinValue
                : this.PacketPresetnationTimestamp.ToTimeSpan(stream.TimeBase);

            this.GuessStartTime(mediaCore);
        }

        /// <summary>
        /// Gets the stream index this packet corresponds to.
        /// </summary>
        public int StreamIndex { get; }

        /// <summary>
        /// Gets the original PTS of the packet in stream timebase units.
        /// </summary>
        public long PacketPresetnationTimestamp { get; }

        /// <summary>
        /// Gets the original DTS of the packet in stream timebase units.
        /// </summary>
        public long PacketDecodingTimestamp { get; }

        /// <summary>
        /// Gets the <see cref="PacketDecodingTimestamp"/> expressed as a <see cref="TimeSpan"/>.
        /// Returns <see cref="TimeSpan.MinValue"/> if invalid.
        /// </summary>
        public TimeSpan DecodingTime { get; }

        /// <summary>
        /// Gets a value indicating whether the presentation time of this data
        /// frame was guessed.
        /// </summary>
        public bool IsStartTimeGuessed { get; private set; }

        /// <summary>
        /// Gets the data's presentation start time.
        /// This information may have been guessed.
        /// Check the <see cref="IsStartTimeGuessed"/> property
        /// to see if this value was guessed.
        /// </summary>
        public TimeSpan StartTime { get; private set; }

        /// <summary>
        /// Gets the packet's byte position in the input stream.
        /// Returns -1 if unknown.
        /// </summary>
        public long PacketPosition { get; }

        /// <summary>
        /// Gets the raw byte data of the data packet.
        /// </summary>
        /// <returns>The raw bytes of the packet data. May return null when no packet data is available.</returns>
        public byte[] GetPacketData() => this.localPacketData;

        /// <summary>
        /// Guesses the start time of the packet.
        /// Side effect modify the <see cref="StartTime"/> property and the
        /// <see cref="IsStartTimeGuessed"/> property.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        private void GuessStartTime(MediaEngine mediaCore)
        {
            if (this.PacketPresetnationTimestamp != ffmpeg.AV_NOPTS_VALUE)
            {
                return;
            }

            if (mediaCore == null)
            {
                return;
            }

            var t = mediaCore.Timing.ReferenceType;
            var component = mediaCore.Container.Components[t];
            if (component == null)
            {
                return;
            }

            var blocks = mediaCore.Blocks[t];
            if (blocks == null)
            {
                return;
            }

            var blocksStartTime = blocks.Count > 0
                ? blocks.RangeStartTime
                : mediaCore.CurrentRenderStartTime[t];

            var bufferDuration = component.BufferDuration;

            if (bufferDuration.Ticks <= 0 && component.BufferCount > 0)
            {
                bufferDuration = TimeSpan.FromTicks(blocks.AverageBlockDuration.Ticks * component.BufferCount);
            }

            this.StartTime = TimeSpan.FromTicks(blocksStartTime.Ticks - bufferDuration.Ticks);
            this.IsStartTimeGuessed = true;
        }
    }
}
