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
        private readonly Queue<MediaBlock> PoolBlocks;

        /// <summary>
        /// The blocks that are available for rendering.
        /// </summary>
        private readonly List<MediaBlock> PlaybackBlocks;

        /// <summary>
        /// Controls multiple reads and exclusive writes.
        /// </summary>
        private readonly object SyncLock = new ();

        private bool IsNonMonotonic;
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
        private long LastLookupTimeTicks = TimeSpan.MinValue.Ticks;
        private int LastLookupIndex = -1;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaBlockBuffer"/> class.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        /// <param name="mediaType">Type of the media.</param>
        public MediaBlockBuffer(int capacity, MediaType mediaType)
        {
            this.Capacity = capacity;
            this.MediaType = mediaType;
            this.PoolBlocks = new Queue<MediaBlock>(capacity + 1); // +1 to be safe and not degrade performance
            this.PlaybackBlocks = new List<MediaBlock>(capacity + 1); // +1 to be safe and not degrade performance

            // allocate the blocks
            for (var i = 0; i < capacity; i++)
            {
                this.PoolBlocks.Enqueue(CreateBlock(mediaType));
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
        public bool IsDisposed { get { lock (this.SyncLock)
{
    return this.localIsDisposed;
}
        } }

        /// <summary>
        /// Gets the start time of the first block.
        /// </summary>
        public TimeSpan RangeStartTime { get { lock (this.SyncLock)
{
    return this.localRangeStartTime;
}
        } }

        /// <summary>
        /// Gets the middle time of the range.
        /// </summary>
        public TimeSpan RangeMidTime { get { lock (this.SyncLock)
{
    return this.localRangeMidTime;
}
        } }

        /// <summary>
        /// Gets the end time of the last block.
        /// </summary>
        public TimeSpan RangeEndTime { get { lock (this.SyncLock)
{
    return this.localRangeEndTime;
}
        } }

        /// <summary>
        /// Gets the range of time between the first block and the end time of the last block.
        /// </summary>
        public TimeSpan RangeDuration { get { lock (this.SyncLock)
{
    return this.localRangeDuration;
}
        } }

        /// <summary>
        /// Gets the compressed data bit rate from which media blocks were created.
        /// </summary>
        public long RangeBitRate { get { lock (this.SyncLock)
{
    return this.localRangeBitRate;
}
        } }

        /// <summary>
        /// Gets the average duration of the currently available playback blocks.
        /// </summary>
        public TimeSpan AverageBlockDuration { get { lock (this.SyncLock)
{
    return this.localAverageBlockDuration;
}
        } }

        /// <summary>
        /// Gets a value indicating whether all the durations of the blocks are equal.
        /// </summary>
        public bool IsMonotonic { get { lock (this.SyncLock)
{
    return this.localIsMonotonic;
}
        } }

        /// <summary>
        /// Gets the duration of the blocks. If the blocks are not monotonic returns zero.
        /// </summary>
        public TimeSpan MonotonicDuration { get { lock (this.SyncLock)
{
    return this.localMonotonicDuration;
}
        } }

        /// <summary>
        /// Gets the number of available playback blocks.
        /// </summary>
        public int Count { get { lock (this.SyncLock)
{
    return this.localCount;
}
        } }

        /// <summary>
        /// Gets the usage percent from 0.0 to 1.0.
        /// </summary>
        public double CapacityPercent { get { lock (this.SyncLock)
{
    return this.localCapacityPercent;
}
        } }

        /// <summary>
        /// Gets a value indicating whether the playback blocks are all allocated.
        /// </summary>
        public bool IsFull { get { lock (this.SyncLock)
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
                lock (this.SyncLock)
                {
                    return this.PlaybackBlocks[index];
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
                lock (this.SyncLock)
                {
                    var index = this.IndexOf(positionTicks);
                    return index >= 0 ? this.PlaybackBlocks[index] : null;
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
            lock (this.SyncLock)
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
            lock (this.SyncLock)
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
            lock (this.SyncLock)
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

            lock (this.SyncLock)
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

            lock (this.SyncLock)
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

            lock (this.SyncLock)
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
            lock (this.SyncLock)
            {
                if (this.PlaybackBlocks.Count == 0)
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
            lock (this.SyncLock)
            {
                if (this.LastLookupTimeTicks != TimeSpan.MinValue.Ticks && renderTimeTicks == this.LastLookupTimeTicks)
                {
                    return this.LastLookupIndex;
                }

                this.LastLookupTimeTicks = renderTimeTicks;
                this.LastLookupIndex = this.PlaybackBlocks.Count > 0 && renderTimeTicks <= this.PlaybackBlocks[0].StartTime.Ticks ? 0 :
                    this.PlaybackBlocks.StartIndexOf(this.LastLookupTimeTicks);

                return this.LastLookupIndex;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (this.SyncLock)
            {
                if (this.localIsDisposed)
                {
                    return;
                }

                this.localIsDisposed = true;

                while (this.PoolBlocks.Count > 0)
                {
                    var block = this.PoolBlocks.Dequeue();
                    block.Dispose();
                }

                for (var i = this.PlaybackBlocks.Count - 1; i >= 0; i--)
                {
                    var block = this.PlaybackBlocks[i];
                    this.PlaybackBlocks.RemoveAt(i);
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

            lock (this.SyncLock)
            {
                try
                {
                    // Check if we already have a block at the given time
                    if (this.IsInRange(source.StartTime) && source.HasValidStartTime)
                    {
                        var repeatedBlock = this.PlaybackBlocks.FirstOrDefault(f => f.StartTime.Ticks == source.StartTime.Ticks);
                        if (repeatedBlock != null)
                        {
                            this.PlaybackBlocks.Remove(repeatedBlock);
                            this.PoolBlocks.Enqueue(repeatedBlock);
                        }
                    }

                    // if there are no available blocks, make room!
                    if (this.PoolBlocks.Count <= 0)
                    {
                        // Remove the first block from playback
                        var firstBlock = this.PlaybackBlocks[0];
                        this.PlaybackBlocks.RemoveAt(0);
                        this.PoolBlocks.Enqueue(firstBlock);
                    }

                    // Get a block reference from the pool and convert it!
                    var targetBlock = this.PoolBlocks.Dequeue();
                    var lastBlock = this.PlaybackBlocks.Count > 0 ? this.PlaybackBlocks[^1] : null;

                    if (container.Convert(source, ref targetBlock, true, lastBlock) == false)
                    {
                        // return the converted block to the pool
                        this.PoolBlocks.Enqueue(targetBlock);
                        return null;
                    }

                    // Add the target block to the playback blocks
                    this.PlaybackBlocks.Add(targetBlock);

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
            lock (this.SyncLock)
            {
                // return all the blocks to the block pool
                foreach (var block in this.PlaybackBlocks)
                {
                    this.PoolBlocks.Enqueue(block);
                }

                this.PlaybackBlocks.Clear();
                this.UpdateCollectionProperties();
            }
        }

        /// <summary>
        /// Returns a formatted string with information about this buffer.
        /// </summary>
        /// <returns>The formatted string.</returns>
        internal string Debug()
        {
            lock (this.SyncLock)
            {
                return $"{this.MediaType,-12} - CAP: {this.Capacity,10} | FRE: {this.PoolBlocks.Count,7} | " +
                    $"USD: {this.PlaybackBlocks.Count,4} |  RNG: {this.RangeStartTime.Format(),8} to {this.RangeEndTime.Format().Trim()}";
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
            lock (this.SyncLock)
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
        /// Updates the <see cref="PlaybackBlocks"/> collection properties.
        /// This method must be called whenever the collection is modified.
        /// The reason this exists is to avoid computing and iterating over these values every time they are read.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCollectionProperties()
        {
            // Update the playback blocks sorting
            if (this.PlaybackBlocks.Count > 0)
            {
                var maxBlockIndex = this.PlaybackBlocks.Count - 1;

                // Perform the sorting and assignment of Previous and Next blocks
                this.PlaybackBlocks.Sort();
                this.PlaybackBlocks[0].Index = 0;
                this.PlaybackBlocks[0].Previous = null;
                this.PlaybackBlocks[0].Next = maxBlockIndex > 0 ? this.PlaybackBlocks[1] : null;

                for (var blockIndex = 1; blockIndex <= maxBlockIndex; blockIndex++)
                {
                    this.PlaybackBlocks[blockIndex].Index = blockIndex;
                    this.PlaybackBlocks[blockIndex].Previous = this.PlaybackBlocks[blockIndex - 1];
                    this.PlaybackBlocks[blockIndex].Next = blockIndex + 1 <= maxBlockIndex ? this.PlaybackBlocks[blockIndex + 1] : null;
                }
            }

            this.LastLookupIndex = -1;
            this.LastLookupTimeTicks = TimeSpan.MinValue.Ticks;

            this.localCount = this.PlaybackBlocks.Count;
            this.localRangeStartTime = this.PlaybackBlocks.Count == 0 ? TimeSpan.Zero : this.PlaybackBlocks[0].StartTime;
            this.localRangeEndTime = this.PlaybackBlocks.Count == 0 ? TimeSpan.Zero : this.PlaybackBlocks[^1].EndTime;
            this.localRangeDuration = TimeSpan.FromTicks(this.RangeEndTime.Ticks - this.RangeStartTime.Ticks);
            this.localRangeMidTime = TimeSpan.FromTicks(this.localRangeStartTime.Ticks + (this.localRangeDuration.Ticks / 2));
            this.localCapacityPercent = Convert.ToDouble(this.localCount) / this.Capacity;
            this.localIsFull = this.localCount >= this.Capacity;
            this.localRangeBitRate = this.localRangeDuration.TotalSeconds <= 0 || this.localCount <= 1 ? 0 :
                Convert.ToInt64(8d * this.PlaybackBlocks.Sum(m => m.CompressedSize) / this.localRangeDuration.TotalSeconds);

            // don't compute an average if we don't have blocks
            if (this.localCount <= 0)
            {
                this.localAverageBlockDuration = default;
                return;
            }

            // Don't compute if we've already determined that it's non-monotonic
            if (this.IsNonMonotonic)
            {
                this.localAverageBlockDuration = TimeSpan.FromTicks(
                    Convert.ToInt64(this.PlaybackBlocks.Average(b => Convert.ToDouble(b.Duration.Ticks))));

                return;
            }

            // Monotonic verification
            var lastBlockDuration = this.PlaybackBlocks[this.localCount - 1].Duration;
            this.IsNonMonotonic = this.PlaybackBlocks.Any(b => b.Duration.Ticks != lastBlockDuration.Ticks);
            this.localIsMonotonic = !this.IsNonMonotonic;
            this.localMonotonicDuration = this.localIsMonotonic ? lastBlockDuration : default;
            this.localAverageBlockDuration = this.localIsMonotonic ? lastBlockDuration : TimeSpan.FromTicks(
                Convert.ToInt64(this.PlaybackBlocks.Average(b => Convert.ToDouble(b.Duration.Ticks))));
        }
    }
}
