// <copyright file="PacketBufferState.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Container
{
    using System;

    /// <summary>
    /// A value type that representing the packet buffer state.
    /// </summary>
    internal struct PacketBufferState : IEquatable<PacketBufferState>
    {
        /// <summary>
        /// The length in bytes of the packet buffer.
        /// </summary>
        public long Length;

        /// <summary>
        /// The number of packets in the packet buffer.
        /// </summary>
        public int Count;

        /// <summary>
        /// The minimum number of packets so <see cref="HasEnoughPackets"/> is
        /// set to true.
        /// </summary>
        public int CountThreshold;

        /// <summary>
        /// Whether the packet buffer has enough packets.
        /// </summary>
        public bool HasEnoughPackets;

        /// <summary>
        /// The duration of the packets. An invalid value will return
        /// <see cref="TimeSpan.MinValue"/>.
        /// </summary>
        public TimeSpan Duration;

        /// <inheritdoc />
        public bool Equals(PacketBufferState other) =>
                    this.Length == other.Length &&
                    this.Count == other.Count &&
                    this.CountThreshold == other.CountThreshold &&
                    this.HasEnoughPackets == other.HasEnoughPackets;

        /// <inheritdoc />
        public override bool Equals(object obj) =>
            obj is PacketBufferState state && this.Equals(state);

        /// <inheritdoc />
        public override int GetHashCode() =>
            throw new NotSupportedException($"{nameof(PacketBufferState)} does not support hashing.");
    }
}
