// <copyright file="PacketQueueOp.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    /// <summary>
    /// Defines the multiple packet queue operations.
    /// </summary>
    public enum PacketQueueOp
    {
        /// <summary>
        /// The packet queue was cleared.
        /// </summary>
        Clear = 0,

        /// <summary>
        /// The packet queue queued a packet.
        /// </summary>
        Queued = 1,

        /// <summary>
        /// The packet queue dequeued a packet.
        /// </summary>
        Dequeued = 2,
    }
}
