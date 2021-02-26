// <copyright file="PacketReadingWorker.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Threading;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Primitives;

    /// <summary>
    /// Implement packet reading worker logic.
    /// </summary>
    /// <seealso cref="IMediaWorker" />
    internal sealed class PacketReadingWorker : IntervalWorkerBase, IMediaWorker, ILoggingSource
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="PacketReadingWorker"/>
        /// class.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        public PacketReadingWorker(MediaEngine mediaCore)
            : base(nameof(PacketReadingWorker))
        {
            this.MediaCore = mediaCore;
            this.Container = mediaCore.Container;

            // Enable data frame processing as a connector callback (i.e. hanlde non-media frames)
            this.Container.Data.OnDataPacketReceived = (dataPacket, stream) =>
            {
                try
                {
                    var dataFrame = new DataFrame(dataPacket, stream, this.MediaCore);
                    this.MediaCore.Connector?.OnDataFrameReceived(dataFrame, stream);
                }
                catch
                {
                    // ignore
                }
            };

            // Packet Buffer Notification Callbacks
            this.Container.Components.OnPacketQueueChanged = (op, packet, mediaType, state) =>
            {
                this.MediaCore.State.UpdateBufferingStats(state.Length, state.Count, state.CountThreshold, state.Duration);

                if (op != PacketQueueOp.Queued)
                {
                    return;
                }

                unsafe
                {
                    this.MediaCore.Connector?.OnPacketRead(packet.Pointer, this.Container.InputContext);
                }
            };
        }

        /// <inheritdoc />
        public MediaEngine MediaCore { get; }

        /// <inheritdoc />
        ILoggingHandler ILoggingSource.LoggingHandler => this.MediaCore;

        /// <summary>
        /// Gets the Media Engine's container.
        /// </summary>
        private MediaContainer Container { get; }

        /// <inheritdoc />
        protected override void ExecuteCycleLogic(CancellationToken ct)
        {
            while (this.MediaCore.ShouldReadMorePackets)
            {
                if (this.Container.IsReadAborted || this.Container.IsAtEndOfStream || ct.IsCancellationRequested ||
                    this.WorkerState != this.WantedWorkerState)
                {
                    break;
                }

                try
                {
                    this.Container.Read();
                }
                catch (MediaContainerException)
                { /* ignore */
                }
            }
        }

        /// <inheritdoc />
        protected override void OnCycleException(Exception ex) =>
            this.LogError(Aspects.ReadingWorker, "Worker Cycle exception thrown", ex);
    }
}
