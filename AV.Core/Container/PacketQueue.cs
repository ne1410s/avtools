// <copyright file="PacketQueue.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Collections.Generic;
    using FFmpeg.AutoGen;

    /// <summary>
    /// A data structure containing a queue of packets to process.
    /// This class is thread safe and disposable.
    /// Queued, unmanaged packets are disposed automatically by this queue.
    /// Dequeued packets are the responsibility of the calling code.
    /// </summary>
    internal sealed class PacketQueue : IDisposable
    {
        private readonly List<MediaPacket> packets = new List<MediaPacket>(2048);
        private readonly object syncLock = new object();
        private long localBufferLength;
        private long localDuration;

        /// <summary>
        /// Gets the packet count.
        /// </summary>
        public int Count
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.packets.Count;
                }
            }
        }

        /// <summary>
        /// Gets the sum of all the packet sizes contained by this queue.
        /// </summary>
        public long BufferLength
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localBufferLength;
                }
            }
        }

        /// <summary>
        /// Gets the duration in stream time base units.
        /// </summary>
        /// <param name="timeBase">The time base.</param>
        /// <returns>The total duration.</returns>
        public TimeSpan GetDuration(AVRational timeBase)
        {
            lock (this.syncLock)
            {
                return this.localDuration.ToTimeSpan(timeBase);
            }
        }

        /// <summary>
        /// Peeks the next available packet in the queue without removing it.
        /// If no packets are available, null is returned.
        /// </summary>
        /// <returns>The packet.</returns>
        public MediaPacket Peek()
        {
            lock (this.syncLock)
            {
                return this.packets.Count <= 0 ? null : this.packets[0];
            }
        }

        /// <summary>
        /// Pushes the specified packet into the queue.
        /// In other words, queues the packet.
        /// </summary>
        /// <param name="packet">The packet.</param>
        public void Push(MediaPacket packet)
        {
            // avoid pushing null packets
            if (packet == null)
            {
                return;
            }

            lock (this.syncLock)
            {
                this.packets.Add(packet);
                this.localBufferLength += packet.Size < 0 ? default : packet.Size;
                this.localDuration += packet.Duration < 0 ? default : packet.Duration;
            }
        }

        /// <summary>
        /// Dequeue a packet from this queue.
        /// </summary>
        /// <returns>The dequeued packet.</returns>
        public MediaPacket Dequeue()
        {
            lock (this.syncLock)
            {
                if (this.packets.Count <= 0)
                {
                    return null;
                }

                var result = this.packets[0];
                this.packets.RemoveAt(0);

                var packet = result;
                this.localBufferLength -= packet.Size < 0 ? default : packet.Size;
                this.localDuration -= packet.Duration < 0 ? default : packet.Duration;
                return packet;
            }
        }

        /// <summary>
        /// Clears and frees all the unmanaged packets from this queue.
        /// </summary>
        public void Clear()
        {
            lock (this.syncLock)
            {
                while (this.packets.Count > 0)
                {
                    var packet = this.Dequeue();
                    packet.Dispose();
                }

                this.localBufferLength = 0;
                this.localDuration = 0;
            }
        }

        /// <inheritdoc />
        public void Dispose() => this.Dispose(true);

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="alsoManaged"><c>true</c> to release both managed and
        /// unmanaged resources; <c>false</c> to release only unmanaged
        /// resources.</param>
        private void Dispose(bool alsoManaged)
        {
            if (alsoManaged == false)
            {
                return;
            }

            lock (this.syncLock)
            {
                this.Clear();
            }
        }
    }
}
