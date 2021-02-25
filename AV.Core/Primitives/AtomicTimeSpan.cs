// <copyright file="AtomicTimeSpan.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;

    /// <summary>
    /// Represents an atomic TimeSpan type.
    /// </summary>
    internal sealed class AtomicTimeSpan : AtomicTypeBase<TimeSpan>
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicTimeSpan"/> class.
        /// </summary>
        /// <param name="initialValue">The initial value.</param>
        public AtomicTimeSpan(TimeSpan initialValue)
                    : base(initialValue.Ticks)
        {
            // placeholder
        }

        /// <inheritdoc />
        protected override TimeSpan FromLong(long backingValue) => TimeSpan.FromTicks(backingValue);

        /// <inheritdoc />
        protected override long ToLong(TimeSpan value) => value.Ticks;
    }
}
