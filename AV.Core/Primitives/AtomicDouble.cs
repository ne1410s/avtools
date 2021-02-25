// <copyright file="AtomicDouble.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;

    /// <summary>
    /// Fast, atomic double combining interlocked to write value and volatile to read values
    /// Idea taken from Memory model and .NET operations in article:
    /// http://igoro.com/archive/volatile-keyword-in-c-memory-model-explained/.
    /// </summary>
    internal sealed class AtomicDouble : AtomicTypeBase<double>
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicDouble"/> class.
        /// </summary>
        /// <param name="initialValue">if set to <c>true</c> [initial value].</param>
        public AtomicDouble(double initialValue)
            : base(BitConverter.DoubleToInt64Bits(initialValue))
        {
            // placeholder
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicDouble"/> class.
        /// </summary>
        public AtomicDouble()
            : base(BitConverter.DoubleToInt64Bits(0))
        {
            // placeholder
        }

        /// <inheritdoc />
        protected override double FromLong(long backingValue) => BitConverter.Int64BitsToDouble(backingValue);

        /// <inheritdoc />
        protected override long ToLong(double value) => BitConverter.DoubleToInt64Bits(value);
    }
}
