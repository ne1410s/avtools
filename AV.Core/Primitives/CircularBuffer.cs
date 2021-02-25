// <copyright file="CircularBuffer.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A fixed-size buffer that acts as an infinite length one.
    /// This buffer is backed by unmanaged, very fast memory so ensure you call
    /// the dispose method when you are done using it.
    /// </summary>
    /// <seealso cref="IDisposable" />
    internal sealed unsafe class CircularBuffer : IDisposable
    {
        /// <summary>
        /// The locking object to perform synchronization.
        /// </summary>
        private readonly object SyncLock = new object();

        /// <summary>
        /// The unmanaged buffer.
        /// </summary>
        private IntPtr Buffer;

        // Property backing
        private bool localIsDisposed;
        private int localReadableCount;
        private TimeSpan localWriteTag = TimeSpan.MinValue;
        private int localWriteIndex;
        private int localReadIndex;
        private int localLength;

        /// <summary>
        /// Initialises a new instance of the <see cref="CircularBuffer"/> class.
        /// </summary>
        /// <param name="bufferLength">Length of the buffer.</param>
        public CircularBuffer(int bufferLength)
        {
            this.localLength = bufferLength;
            this.Buffer = Marshal.AllocHGlobal(this.localLength);

            // Clear the memory as it might be dirty after allocating it.
            var baseAddress = (byte*)this.Buffer.ToPointer();
            for (var i = 0; i < this.localLength; i++)
            {
                baseAddress[i] = 0;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool IsDisposed { get { lock (this.SyncLock)
{
    return this.localIsDisposed;
}
        } }

        /// <summary>
        /// Gets the capacity of this buffer.
        /// </summary>
        public int Length { get { lock (this.SyncLock)
{
    return this.localLength;
}
        } }

        /// <summary>
        /// Gets the current, 0-based read index.
        /// </summary>
        public int ReadIndex { get { lock (this.SyncLock)
{
    return this.localReadIndex;
}
        } }

        /// <summary>
        /// Gets the maximum rewindable amount of bytes.
        /// </summary>
        public int RewindableCount
        {
            get
            {
                lock (this.SyncLock)
                {
                    if (this.localWriteIndex < this.localReadIndex)
                    {
                        return this.localReadIndex - this.localWriteIndex;
                    }

                    return this.localReadIndex;
                }
            }
        }

        /// <summary>
        /// Gets the current, 0-based write index.
        /// </summary>
        public int WriteIndex { get { lock (this.SyncLock)
{
    return this.localWriteIndex;
}
        } }

        /// <summary>
        /// Gets an the object associated with the last write.
        /// </summary>
        public TimeSpan WriteTag { get { lock (this.SyncLock)
{
    return this.localWriteTag;
}
        } }

        /// <summary>
        /// Gets the available bytes to read.
        /// </summary>
        public int ReadableCount { get { lock (this.SyncLock)
{
    return this.localReadableCount;
}
        } }

        /// <summary>
        /// Gets the number of bytes that can be written.
        /// </summary>
        public int WritableCount { get { lock (this.SyncLock)
{
    return this.localLength - this.localReadableCount;
}
        } }

        /// <summary>
        /// Gets percentage of used bytes (readable/available, from 0.0 to 1.0).
        /// </summary>
        public double CapacityPercent { get { lock (this.SyncLock)
{
    return (double)this.localReadableCount / this.localLength;
}
        } }

        /// <summary>
        /// Skips the specified amount requested bytes to be read.
        /// </summary>
        /// <param name="requestedBytes">The requested bytes.</param>
        /// <exception cref="InvalidOperationException">When requested bytes is greater than readable count.</exception>
        public void Skip(int requestedBytes)
        {
            lock (this.SyncLock)
            {
                if (requestedBytes > this.localReadableCount)
                {
                    throw new InvalidOperationException(
                        $"Unable to skip {requestedBytes} bytes. Only {this.localReadableCount} bytes are available for skipping");
                }

                this.localReadIndex += requestedBytes;
                this.localReadableCount -= requestedBytes;

                if (this.localReadIndex >= this.localLength)
                {
                    this.localReadIndex = 0;
                }
            }
        }

        /// <summary>
        /// Rewinds the read position by specified requested amount of bytes.
        /// </summary>
        /// <param name="requestedBytes">The requested bytes.</param>
        /// <exception cref="InvalidOperationException">When requested is greater than rewindable.</exception>
        public void Rewind(int requestedBytes)
        {
            lock (this.SyncLock)
            {
                if (requestedBytes > this.RewindableCount)
                {
                    throw new InvalidOperationException(
                        $"Unable to rewind {requestedBytes} bytes. Only {this.RewindableCount} bytes are available for rewinding");
                }

                this.localReadIndex -= requestedBytes;
                this.localReadableCount += requestedBytes;

                if (this.localReadIndex < 0)
                {
                    this.localReadIndex = 0;
                }
            }
        }

        /// <summary>
        /// Reads the specified number of bytes into the target array.
        /// </summary>
        /// <param name="requestedBytes">The requested bytes.</param>
        /// <param name="target">The target.</param>
        /// <param name="targetOffset">The target offset.</param>
        /// <exception cref="InvalidOperationException">When requested bytes is greater than readable count.</exception>
        public void Read(int requestedBytes, byte[] target, int targetOffset)
        {
            lock (this.SyncLock)
            {
                if (requestedBytes > this.localReadableCount)
                {
                    throw new InvalidOperationException(
                        $"Unable to read {requestedBytes} bytes. Only {this.localReadableCount} bytes are available.");
                }

                var readCount = 0;
                while (readCount < requestedBytes)
                {
                    var copyLength = Math.Min(this.localLength - this.localReadIndex, requestedBytes - readCount);
                    var sourcePtr = this.Buffer + this.localReadIndex;
                    Marshal.Copy(sourcePtr, target, targetOffset + readCount, copyLength);

                    readCount += copyLength;
                    this.localReadIndex += copyLength;
                    this.localReadableCount -= copyLength;

                    if (this.localReadIndex >= this.localLength)
                    {
                        this.localReadIndex = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Writes data to the backing buffer using the specified pointer and length.
        /// and associating a write tag for this operation.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="length">The length.</param>
        /// <param name="writeTag">The write tag.</param>
        /// <param name="overwrite">if set to <c>true</c>, overwrites the data even if it has not been read.</param>
        /// <exception cref="InvalidOperationException">When read needs to be called more often.</exception>
        public void Write(IntPtr source, int length, TimeSpan writeTag, bool overwrite)
        {
            lock (this.SyncLock)
            {
                if (overwrite == false && length > this.WritableCount)
                {
                    throw new InvalidOperationException(
                        $"Unable to write to circular buffer. Call the {nameof(this.Read)} method to make some additional room");
                }

                var writeCount = 0;
                while (writeCount < length)
                {
                    var copyLength = Math.Min(this.localLength - this.localWriteIndex, length - writeCount);
                    var sourcePtr = source + writeCount;
                    var targetPtr = this.Buffer + this.localWriteIndex;
                    System.Buffer.MemoryCopy(
                        sourcePtr.ToPointer(),
                        targetPtr.ToPointer(),
                        copyLength,
                        copyLength);

                    writeCount += copyLength;
                    this.localWriteIndex += copyLength;
                    this.localReadableCount += copyLength;

                    if (this.localWriteIndex >= this.localLength)
                    {
                        this.localWriteIndex = 0;
                    }
                }

                this.localWriteTag = writeTag;
            }
        }

        /// <summary>
        /// Resets all states as if this buffer had just been created.
        /// </summary>
        public void Clear()
        {
            lock (this.SyncLock)
            {
                this.localWriteIndex = 0;
                this.localReadIndex = 0;
                this.localWriteTag = TimeSpan.MinValue;
                this.localReadableCount = 0;
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

                this.Clear();
                Marshal.FreeHGlobal(this.Buffer);
                this.Buffer = IntPtr.Zero;
                this.localLength = 0;
                this.localIsDisposed = true;
            }
        }
    }
}
