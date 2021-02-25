// <copyright file="AtomicDateTime.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;

    /// <summary>
    /// Defines an atomic DateTime.
    /// </summary>
    internal sealed class AtomicDateTime : AtomicTypeBase<DateTime>
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicDateTime"/> class.
        /// </summary>
        /// <param name="initialValue">The initial value.</param>
        public AtomicDateTime(DateTime initialValue)
            : base(initialValue.Ticks)
        {
            // placeholder
        }

        /// <inheritdoc />
        protected override DateTime FromLong(long backingValue) => new DateTime(backingValue);

        /// <inheritdoc />
        protected override long ToLong(DateTime value) => value.Ticks;
    }
}
