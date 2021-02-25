// <copyright file="AtomicLong.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    /// <summary>
    /// Fast, atomic long combining interlocked to write value and volatile to read values
    /// Idea taken from Memory model and .NET operations in article:
    /// http://igoro.com/archive/volatile-keyword-in-c-memory-model-explained/.
    /// </summary>
    internal sealed class AtomicLong : AtomicTypeBase<long>
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicLong"/> class.
        /// </summary>
        /// <param name="initialValue">if set to <c>true</c> [initial value].</param>
        public AtomicLong(long initialValue)
            : base(initialValue)
        {
            // placeholder
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicLong"/> class.
        /// </summary>
        public AtomicLong()
            : base(0)
        {
            // placeholder
        }

        /// <inheritdoc />
        protected override long FromLong(long backingValue) => backingValue;

        /// <inheritdoc />
        protected override long ToLong(long value) => value;
    }
}
