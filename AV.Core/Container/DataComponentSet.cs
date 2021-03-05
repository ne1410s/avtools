﻿// <copyright file="DataComponentSet.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using AV.Core.Common;

    /// <summary>
    /// Represents the component set for non-media packets.
    /// Data packets sent to this component is not queued and decoding of
    /// data frames is up to the user.
    /// </summary>
    public sealed class DataComponentSet
    {
        private readonly object localSyncLock = new object();

        /// <summary>
        /// Packet received delegate.
        /// </summary>
        /// <param name="dataPacket">The data packet.</param>
        /// <param name="stream">The stream.</param>
        public delegate void OnDataPacketReceivedDelegate(
            MediaPacket dataPacket,
            StreamInfo stream);

        /// <summary>
        /// Gets or sets a method that gets called when a data packet is
        /// received from the input stream.
        /// </summary>
        public OnDataPacketReceivedDelegate OnDataPacketReceived { get; set; }

        /// <summary>
        /// Tries to handle processing of a data packet. If the packet is in
        /// fact a data packet, it is automatically disposed after executing the
        /// appropriate callbacks and returns true. If the packet is not a data
        /// packet, this method returns false and does not dispose of the packet
        /// so that the media component set tries to handle it.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="packet">The packet.</param>
        /// <returns>Returns false if the packet is not a data packet.</returns>
        public bool TryHandleDataPacket(MediaContainer container, MediaPacket packet)
        {
            lock (this.localSyncLock)
            {
                // ensure packet and container are not null
                if (packet == null || container == null)
                {
                    return false;
                }

                // Get the associated stream
                var stream = container.MediaInfo.Streams.ContainsKey(packet.StreamIndex)
                    ? container.MediaInfo.Streams[packet.StreamIndex]
                    : null;

                // Ensure the stream is in fact a data stream
                if (stream == null || !stream.IsNonMedia)
                {
                    return false;
                }

                try
                {
                    // Execute the packet handling callback
                    this.OnDataPacketReceived?.Invoke(packet, stream);
                }
                catch
                {
                    // Ignore
                }
                finally
                {
                    // always dispose of the packet
                    packet.Dispose();
                }

                // Signal that the packet has been handled as a data packet
                return true;
            }
        }
    }
}
