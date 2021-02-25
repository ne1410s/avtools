// <copyright file="FrameDecodingWorker.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using AV.Core.Common;
    using AV.Core.Container;
    using AV.Core.Diagnostics;
    using AV.Core.Primitives;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Implement frame decoding worker logic.
    /// </summary>
    /// <seealso cref="IMediaWorker" />
    internal sealed class FrameDecodingWorker : IntervalWorkerBase, IMediaWorker, ILoggingSource
    {
        private readonly Action<IEnumerable<MediaType>, CancellationToken> SerialDecodeBlocks;
        private readonly Action<IEnumerable<MediaType>, CancellationToken> ParallelDecodeBlocks;

        /// <summary>
        /// The decoded frame count for a cycle. This is used to detect end of decoding scenarios.
        /// </summary>
        private int DecodedFrameCount;

        /// <summary>
        /// Initialises a new instance of the <see cref="FrameDecodingWorker"/> class.
        /// </summary>
        /// <param name="mediaCore">The media core.</param>
        public FrameDecodingWorker(MediaEngine mediaCore)
            : base(nameof(FrameDecodingWorker))
        {
            this.MediaCore = mediaCore;
            this.Container = mediaCore.Container;
            this.State = mediaCore.State;

            this.ParallelDecodeBlocks = (all, ct) =>
            {
                Parallel.ForEach(all, (t) =>
                    Interlocked.Add(
                        ref this.DecodedFrameCount,
                    this.DecodeComponentBlocks(t, ct)));
            };

            this.SerialDecodeBlocks = (all, ct) =>
            {
                foreach (var t in this.Container.Components.MediaTypes)
                {
                    this.DecodedFrameCount += this.DecodeComponentBlocks(t, ct);
                }
            };

            this.Container.Components.OnFrameDecoded = (frame, type) =>
            {
                unsafe
                {
                    if (type == MediaType.Audio)
                    {
                        this.MediaCore.Connector?.OnAudioFrameDecoded((AVFrame*)frame.ToPointer(), this.Container.InputContext);
                    }
                    else if (type == MediaType.Video)
                    {
                        this.MediaCore.Connector?.OnVideoFrameDecoded((AVFrame*)frame.ToPointer(), this.Container.InputContext);
                    }
                }
            };

            this.Container.Components.OnSubtitleDecoded = (subtitle) =>
            {
                unsafe
                {
                    this.MediaCore.Connector?.OnSubtitleDecoded((AVSubtitle*)subtitle.ToPointer(), this.Container.InputContext);
                }
            };
        }

        /// <inheritdoc />
        public MediaEngine MediaCore { get; }

        /// <inheritdoc />
        ILoggingHandler ILoggingSource.LoggingHandler => this.MediaCore;

        /// <summary>
        /// Gets the Media Engine's Container.
        /// </summary>
        private MediaContainer Container { get; }

        /// <summary>
        /// Gets the Media Engine's State.
        /// </summary>
        private MediaEngineState State { get; }

        /// <summary>
        /// Gets a value indicating whether parallel decoding is enabled.
        /// </summary>
        private bool UseParallelDecoding => this.MediaCore.Timing.HasDisconnectedClocks || this.Container.MediaOptions.UseParallelDecoding;

        /// <inheritdoc />
        protected override void ExecuteCycleLogic(CancellationToken ct)
        {
            try
            {
                if (this.MediaCore.HasDecodingEnded || ct.IsCancellationRequested)
                {
                    return;
                }

                // Call the frame decoding logic
                this.DecodedFrameCount = 0;
                if (this.UseParallelDecoding)
                {
                    this.ParallelDecodeBlocks.Invoke(this.Container.Components.MediaTypes, ct);
                }
                else
                {
                    this.SerialDecodeBlocks.Invoke(this.Container.Components.MediaTypes, ct);
                }
            }
            finally
            {
                // Provide updates to decoding stats -- don't count attached pictures
                var hasAttachedPictures = this.Container.Components.Video?.IsStillPictures ?? false;
                this.State.UpdateDecodingStats(this.MediaCore.Blocks.Values
                    .Sum(b => b.MediaType == MediaType.Video && hasAttachedPictures ? 0 : b.RangeBitRate));

                // Detect End of Decoding Scenarios
                // The Rendering will check for end of media when this condition is set.
                this.MediaCore.HasDecodingEnded = this.DetectHasDecodingEnded();
            }
        }

        /// <inheritdoc />
        protected override void OnCycleException(Exception ex) =>
            this.LogError(Aspects.DecodingWorker, "Worker Cycle exception thrown", ex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int DecodeComponentBlocks(MediaType t, CancellationToken ct)
        {
            var decoderBlocks = this.MediaCore.Blocks[t]; // the blocks reference
            var addedBlocks = 0; // the number of blocks that have been added
            var maxAddedBlocks = decoderBlocks.Capacity; // the max blocks to add for this cycle

            while (addedBlocks < maxAddedBlocks)
            {
                var position = this.MediaCore.Timing.GetPosition(t).Ticks;
                var rangeHalf = decoderBlocks.RangeMidTime.Ticks;

                // We break decoding if we have a full set of blocks and if the
                // clock is not past the first half of the available block range
                if (decoderBlocks.IsFull && position < rangeHalf)
                {
                    break;
                }

                // Try adding the next block. Stop decoding upon failure or cancellation
                if (ct.IsCancellationRequested || this.AddNextBlock(t) == false)
                {
                    break;
                }

                // At this point we notify that we have added the block
                addedBlocks++;
            }

            return addedBlocks;
        }

        /// <summary>
        /// Tries to receive the next frame from the decoder by decoding queued
        /// Packets and converting the decoded frame into a Media Block which gets
        /// queued into the playback block buffer.
        /// </summary>
        /// <param name="t">The MediaType.</param>
        /// <returns>True if a block could be added. False otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool AddNextBlock(MediaType t)
        {
            // Decode the frames
            var block = this.MediaCore.Blocks[t].Add(this.Container.Components[t].ReceiveNextFrame(), this.Container);
            return block != null;
        }

        /// <summary>
        /// Detects the end of media in the decoding worker.
        /// </summary>
        /// <returns>True if media docding has ended.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool DetectHasDecodingEnded() =>
            this.DecodedFrameCount <= 0 &&
            this.CanReadMoreFramesOf(this.Container.Components.SeekableMediaType) == false;

        /// <summary>
        /// Gets a value indicating whether more frames can be decoded into blocks of the given type.
        /// </summary>
        /// <param name="t">The media type.</param>
        /// <returns>
        ///   <c>true</c> if more frames can be decoded; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanReadMoreFramesOf(MediaType t)
        {
            return
                this.Container.Components[t].BufferLength > 0 ||
                this.Container.Components[t].HasPacketsInCodec ||
                this.MediaCore.ShouldReadMorePackets;
        }
    }
}
