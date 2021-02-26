// <copyright file="MediaBlockBuffer.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;

    /// <summary>
    /// Represents a set of pre-allocated media blocks of the same media type.
    /// A block buffer contains playback and pool blocks. Pool blocks are blocks that
    /// can be reused. Playback blocks are blocks that have been filled.
    /// This class is thread safe.
    /// </summary>
    internal sealed class MediaBlockBuffer : IDisposable
    {
        /// <summary>
        /// The blocks that are available to be filled.
        /// </summary>
        private readonly Queue<MediaBlock> poolBlocks;

        /// <summary>
        /// The blocks that are available for rendering.
        /// </summary>
        private readonly List<MediaBlock> playbackBlocks;

        /// <summary>
        /// Controls multiple reads and exclusive writes.
        /// </summary>
        private readonly object syncLock = new ();

        private bool isNonMonotonic;
        private TimeSpan localRangeStartTime;
        private TimeSpan localRangeEndTime;
        private TimeSpan localRangeMidTime;
        private TimeSpan localRangeDuration;
        private TimeSpan localAverageBlockDuration;
        private TimeSpan localMonotonicDuration;
        private int localCount;
        private long localRangeBitRate;
        private double localCapacityPercent;
        private bool localIsMonotonic;
        private bool localIsFull;
        private bool localIsDisposed;

        // Fast Last Lookup.
        private long lastLookupTimeTicks = TimeSpan.MinValue.Ticks;
        private int lastLookupIndex = -1;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaBlockBuffer"/> class.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        /// <param name="mediaType">Type of the media.</param>
        public MediaBlockBuffer(int capacity, MediaType mediaType)
        {
            this.Capacity = capacity;
            this.MediaType = mediaType;
            this.poolBlocks = new Queue<MediaBlock>(capacity + 1); // +1 to be safe and not degrade performance
            this.playbackBlocks = new List<MediaBlock>(capacity + 1); // +1 to be safe and not degrade performance

            // allocate the blocks
            for (var i = 0; i < capacity; i++)
            {
                this.poolBlocks.Enqueue(CreateBlock(mediaType));
            }
        }

        /// <summary>
        /// Gets the media type of the block buffer.
        /// </summary>
        public MediaType MediaType { get; }

        /// <summary>
        /// Gets the maximum count of this buffer.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localIsDisposed;
                }
            }
        }

        /// <summary>
        /// Gets the start time of the first block.
        /// </summary>
        public TimeSpan RangeStartTime
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localRangeStartTime;
                }
            }
        }

        /// <summary>
        /// Gets the middle time of the range.
        /// </summary>
        public TimeSpan RangeMidTime
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localRangeMidTime;
                }
            }
        }

        /// <summary>
        /// Gets the end time of the last block.
        /// </summary>
        public TimeSpan RangeEndTime
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.localRangeEndTime;
                }
            }
        }

        /// <summary>
        /// Gets the range of time between the first block and the end time of the last block.
        /// </summary>
        public TimeSpan RangeDuration { get { lock (this.syncLock)
{
    return this.localRangeDuration;
}
        } }

        /// <summary>
        /// Gets the compressed data bit rate from which media blocks were created.
        /// </summary>
        public long RangeBitRate { get { lock (this.syncLock)
{
    return this.localRangeBitRate;
}
        } }

        /// <summary>
        /// Gets the average duration of the currently available playback blocks.
        /// </summary>
        public TimeSpan AverageBlockDuration { get { lock (this.syncLock)
{
    return this.localAverageBlockDuration;
}
        } }

        /// <summary>
        /// Gets a value indicating whether all the durations of the blocks are equal.
        /// </summary>
        public bool IsMonotonic { get { lock (this.syncLock)
{
    return this.localIsMonotonic;
}
        } }

        /// <summary>
        /// Gets the duration of the blocks. If the blocks are not monotonic returns zero.
        /// </summary>
        public TimeSpan MonotonicDuration { get { lock (this.syncLock)
{
    return this.localMonotonicDuration;
}
        } }

        /// <summary>
        /// Gets the number of available playback blocks.
        /// </summary>
        public int Count { get { lock (this.syncLock)
{
    return this.localCount;
}
        } }

        /// <summary>
        /// Gets the usage percent from 0.0 to 1.0.
        /// </summary>
        public double CapacityPercent { get { lock (this.syncLock)
{
    return this.localCapacityPercent;
}
        } }

        /// <summary>
        /// Gets a value indicating whether the playback blocks are all allocated.
        /// </summary>
        public bool IsFull { get { lock (this.syncLock)
{
    return this.localIsFull;
}
        } }

        /// <summary>
        /// Gets the <see cref="MediaBlock" /> at the specified index.
        /// </summary>
        /// <value>
        /// The <see cref="MediaBlock"/>.
        /// </value>
        /// <param name="index">The index.</param>
        /// <returns>The media block.</returns>
        public MediaBlock this[int index]
        {
            get
            {
                lock (this.syncLock)
                {
                    return this.playbackBlocks[index];
                }
            }
        }

        /// <summary>
        /// Gets the <see cref="MediaBlock" /> at the specified timestamp.
        /// </summary>
        /// <value>
        /// The <see cref="MediaBlock"/>.
        /// </value>
        /// <param name="positionTicks">The position to lookup.</param>
        /// <returns>The media block.</returns>
        public MediaBlock this[long positionTicks]
        {
            get
            {
                lock (this.syncLock)
                {
                    var index = this.IndexOf(positionTicks);
                    return index >= 0 ? this.playbackBlocks[index] : null;
                }
            }
        }

        /// <summary>
        /// Gets the percentage of the range for the given time position.
        /// A value of less than 0 means the position is behind (lagging).
        /// A value of more than 1 means the position is beyond the range).
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The percent of the range.</returns>
        public double GetRangePercent(TimeSpan position)
        {
            lock (this.syncLock)
            {
                return this.RangeDuration.Ticks != 0 ?
                    Convert.ToDouble(position.Ticks - this.RangeStartTime.Ticks) / this.RangeDuration.Ticks : 0d;
            }
        }

        /// <summary>
        /// Gets the neighboring blocks in an atomic operation.
        /// The first item in the array is the previous block. The second is the next block. The third is the current block.
        /// </summary>
        /// <param name="current">The current block to get neighbors from.</param>
        /// <returns>The previous (if any) and next (if any) blocks.</returns>
        public MediaBlock[] Neighbors(MediaBlock current)
        {
            lock (this.syncLock)
            {
                var result = new MediaBlock[3];
                if (current == null)
                {
                    return result;
                }

                result[0] = current.Previous;
                result[1] = current.Next;
                result[2] = current;

                return result;
            }
        }

        /// <summary>
        /// Gets the neighboring blocks in an atomic operation.
        /// The first item in the array is the previous block. The second is the next block. The third is the current block.
        /// </summary>
        /// <param name="position">The current block position to get neighbors from.</param>
        /// <returns>The previous (if any) and next (if any) blocks.</returns>
        public MediaBlock[] Neighbors(TimeSpan position)
        {
            lock (this.syncLock)
            {
                var current = this[position.Ticks];
                return this.Neighbors(current);
            }
        }

        /// <summary>
        /// Retrieves the block following the provided current block.
        /// If the argument is null and there are blocks, the first block is returned.
        /// </summary>
        /// <param name="current">The current block.</param>
        /// <returns>The next media block.</returns>
        public MediaBlock Next(MediaBlock current)
        {
            if (current == null)
            {
                return null;
            }

            lock (this.syncLock)
            {
                return current.Next;
            }
        }

        /// <summary>
        /// Retrieves the next time-continuous block.
        /// </summary>
        /// <param name="current">The current.</param>
        /// <returns>The next time-continuous block.</returns>
        public MediaBlock ContinuousNext(MediaBlock current)
        {
            if (current == null)
            {
                return null;
            }

            lock (this.syncLock)
            {
                // capture the next frame
                var next = current.Next;
                if (next == null)
                {
                    return null;
                }

                // capture the spacing between the current and the next frame
                var discontinuity = TimeSpan.FromTicks(
                    next.StartTime.Ticks - current.EndTime.Ticks);

                // return null if we have a discontinuity of more than half of the duration
                var discontinuityThreshold = this.IsMonotonic ?
                    TimeSpan.FromTicks(current.Duration.Ticks / 2) :
                    TimeSpan.FromMilliseconds(1);

                return discontinuity.Ticks > discontinuityThreshold.Ticks ? null : next;
            }
        }

        /// <summary>
        /// Retrieves the block prior the provided current block.
        /// If the argument is null and there are blocks, the last block is returned.
        /// </summary>
        /// <param name="current">The current block.</param>
        /// <returns>The next media block.</returns>
        public MediaBlock Previous(MediaBlock current)
        {
            if (current == null)
            {
                return null;
            }

            lock (this.syncLock)
            {
                return current.Previous;
            }
        }

        /// <summary>
        /// Determines whether the given render time is within the range of playback blocks.
        /// </summary>
        /// <param name="renderTime">The render time.</param>
        /// <returns>
        ///   <c>true</c> if [is in range] [the specified render time]; otherwise, <c>false</c>.
        /// </returns>
        public bool IsInRange(TimeSpan renderTime)
        {
            lock (this.syncLock)
            {
                if (this.playbackBlocks.Count == 0)
                {
                    return false;
                }

                return renderTime.Ticks >= this.RangeStartTime.Ticks && renderTime.Ticks <= this.RangeEndTime.Ticks;
            }
        }

        /// <summary>
        /// Retrieves the index of the playback block corresponding to the specified
        /// render time. This uses very fast binary and linear search combinations.
        /// If there are no playback blocks it returns -1.
        /// If the render time is greater than the range end time, it returns the last playback block index.
        /// If the render time is less than the range start time, it returns the first playback block index.
        /// </summary>
        /// <param name="renderTimeTicks">The render time.</param>
        /// <returns>The media block's index.</returns>
        public int IndexOf(long renderTimeTicks)
        {
            lock (this.syncLock)
            {
                if (this.lastLookupTimeTicks != TimeSpan.MinValue.Ticks && renderTimeTicks == this.lastLookupTimeTicks)
                {
                    return this.lastLookupIndex;
                }

                this.lastLookupTimeTicks = renderTimeTicks;
                this.lastLookupIndex = this.playbackBlocks.Count > 0 && renderTimeTicks <= this.playbackBlocks[0].StartTime.Ticks ? 0 :
                    this.playbackBlocks.StartIndexOf(this.lastLookupTimeTicks);

                return this.lastLookupIndex;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (this.syncLock)
            {
                if (this.localIsDisposed)
                {
                    return;
                }

                this.localIsDisposed = true;

                while (this.poolBlocks.Count > 0)
                {
                    var block = this.poolBlocks.Dequeue();
                    block.Dispose();
                }

                for (var i = this.playbackBlocks.Count - 1; i >= 0; i--)
                {
                    var block = this.playbackBlocks[i];
                    this.playbackBlocks.RemoveAt(i);
                    block.Dispose();
                }

                this.UpdateCollectionProperties();
            }
        }

        /// <summary>
        /// Adds a block to the playback blocks by converting the given frame.
        /// If there are no more blocks in the pool, the oldest block is returned to the pool
        /// and reused for the new block. The source frame is automatically disposed.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="container">The container.</param>
        /// <returns>The filled block.</returns>
        internal MediaBlock Add(MediaFrame source, MediaContainer container)
        {
            if (source == null)
            {
                return null;
            }

            lock (this.syncLock)
            {
                try
                {
                    // Check if we already have a block at the given time
                    if (this.IsInRange(source.StartTime) && source.HasValidStartTime)
                    {
                        var repeatedBlock = this.playbackBlocks.FirstOrDefault(f => f.StartTime.Ticks == source.StartTime.Ticks);
                        if (repeatedBlock != null)
                        {
                            this.playbackBlocks.Remove(repeatedBlock);
                            this.poolBlocks.Enqueue(repeatedBlock);
                        }
                    }

                    // if there are no available blocks, make room!
                    if (this.poolBlocks.Count <= 0)
                    {
                        // Remove the first block from playback
                        var firstBlock = this.playbackBlocks[0];
                        this.playbackBlocks.RemoveAt(0);
                        this.poolBlocks.Enqueue(firstBlock);
                    }

                    // Get a block reference from the pool and convert it!
                    var targetBlock = this.poolBlocks.Dequeue();
                    var lastBlock = this.playbackBlocks.Count > 0 ? this.playbackBlocks[^1] : null;

                    if (container.Convert(source, ref targetBlock, true, lastBlock) == false)
                    {
                        // return the converted block to the pool
                        this.poolBlocks.Enqueue(targetBlock);
                        return null;
                    }

                    // Add the target block to the playback blocks
                    this.playbackBlocks.Add(targetBlock);

                    // return the new target block
                    return targetBlock;
                }
                finally
                {
                    // update collection-wide properties
                    this.UpdateCollectionProperties();
                }
            }
        }

        /// <summary>
        /// Clears all the playback blocks returning them to the
        /// block pool.
        /// </summary>
        internal void Clear()
        {
            lock (this.syncLock)
            {
                // return all the blocks to the block pool
                foreach (var block in this.playbackBlocks)
                {
                    this.poolBlocks.Enqueue(block);
                }

                this.playbackBlocks.Clear();
                this.UpdateCollectionProperties();
            }
        }

        /// <summary>
        /// Returns a formatted string with information about this buffer.
        /// </summary>
        /// <returns>The formatted string.</returns>
        internal string Debug()
        {
            lock (this.syncLock)
            {
                return $"{this.MediaType,-12} - CAP: {this.Capacity,10} | FRE: {this.poolBlocks.Count,7} | " +
                    $"USD: {this.playbackBlocks.Count,4} |  RNG: {this.RangeStartTime.Format(),8} to {this.RangeEndTime.Format().Trim()}";
            }
        }

        /// <summary>
        /// Gets the snap, discrete position of the corresponding block.
        /// If the position is greater than the end time of the block, the
        /// start time of the next available block is returned.
        /// </summary>
        /// <param name="position">The analog position.</param>
        /// <returns>A discrete frame position.</returns>
        internal TimeSpan? GetSnapPosition(TimeSpan position)
        {
            lock (this.syncLock)
            {
                if (this.IsMonotonic == false)
                {
                    return this[position.Ticks]?.StartTime;
                }

                var block = this[position.Ticks];
                if (block == null)
                {
                    return default;
                }

                if (block.EndTime > position)
                {
                    return block.StartTime;
                }

                var nextBlock = this.Next(block);
                return nextBlock?.StartTime ?? block.StartTime;
            }
        }

        /// <summary>
        /// Block factory method.
        /// </summary>
        /// <param name="mediaType">Type of the media.</param>
        /// <exception cref="InvalidCastException">MediaBlock does not have a valid type.</exception>
        /// <returns>An instance of the block of the specified type.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MediaBlock CreateBlock(MediaType mediaType)
        {
            if (mediaType == MediaType.Video)
            {
                return new VideoBlock();
            }

            if (mediaType == MediaType.Audio)
            {
                return new AudioBlock();
            }

            if (mediaType == MediaType.Subtitle)
            {
                return new SubtitleBlock();
            }

            throw new InvalidCastException($"No {nameof(MediaBlock)} constructor for {nameof(MediaType)} '{mediaType}'");
        }

        /// <summary>
        /// Updates the <see cref="playbackBlocks"/> collection properties.
        /// This method must be called whenever the collection is modified.
        /// The reason this exists is to avoid computing and iterating over these values every time they are read.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCollectionProperties()
        {
            // Update the playback blocks sorting
            if (this.playbackBlocks.Count > 0)
            {
                var maxBlockIndex = this.playbackBlocks.Count - 1;

                // Perform the sorting and assignment of Previous and Next blocks
                this.playbackBlocks.Sort();
                this.playbackBlocks[0].Index = 0;
                this.playbackBlocks[0].Previous = null;
                this.playbackBlocks[0].Next = maxBlockIndex > 0 ? this.playbackBlocks[1] : null;

                for (var blockIndex = 1; blockIndex <= maxBlockIndex; blockIndex++)
                {
                    this.playbackBlocks[blockIndex].Index = blockIndex;
                    this.playbackBlocks[blockIndex].Previous = this.playbackBlocks[blockIndex - 1];
                    this.playbackBlocks[blockIndex].Next = blockIndex + 1 <= maxBlockIndex ? this.playbackBlocks[blockIndex + 1] : null;
                }
            }

            this.lastLookupIndex = -1;
            this.lastLookupTimeTicks = TimeSpan.MinValue.Ticks;

            this.localCount = this.playbackBlocks.Count;
            this.localRangeStartTime = this.playbackBlocks.Count == 0 ? TimeSpan.Zero : this.playbackBlocks[0].StartTime;
            this.localRangeEndTime = this.playbackBlocks.Count == 0 ? TimeSpan.Zero : this.playbackBlocks[^1].EndTime;
            this.localRangeDuration = TimeSpan.FromTicks(this.RangeEndTime.Ticks - this.RangeStartTime.Ticks);
            this.localRangeMidTime = TimeSpan.FromTicks(this.localRangeStartTime.Ticks + (this.localRangeDuration.Ticks / 2));
            this.localCapacityPercent = Convert.ToDouble(this.localCount) / this.Capacity;
            this.localIsFull = this.localCount >= this.Capacity;
            this.localRangeBitRate = this.localRangeDuration.TotalSeconds <= 0 || this.localCount <= 1 ? 0 :
                Convert.ToInt64(8d * this.playbackBlocks.Sum(m => m.CompressedSize) / this.localRangeDuration.TotalSeconds);

            // don't compute an average if we don't have blocks
            if (this.localCount <= 0)
            {
                this.localAverageBlockDuration = default;
                return;
            }

            // Don't compute if we've already determined that it's non-monotonic
            if (this.isNonMonotonic)
            {
                this.localAverageBlockDuration = TimeSpan.FromTicks(
                    Convert.ToInt64(this.playbackBlocks.Average(b => Convert.ToDouble(b.Duration.Ticks))));

                return;
            }

            // Monotonic verification
            var lastBlockDuration = this.playbackBlocks[this.localCount - 1].Duration;
            this.isNonMonotonic = this.playbackBlocks.Any(b => b.Duration.Ticks != lastBlockDuration.Ticks);
            this.localIsMonotonic = !this.isNonMonotonic;
            this.localMonotonicDuration = this.localIsMonotonic ? lastBlockDuration : default;
            this.localAverageBlockDuration = this.localIsMonotonic ? lastBlockDuration : TimeSpan.FromTicks(
                Convert.ToInt64(this.playbackBlocks.Average(b => Convert.ToDouble(b.Duration.Ticks))));
        }
    }
}
