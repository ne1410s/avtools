// <copyright file="AtomicTypeBase.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /// <summary>
    /// Provides a generic implementation of an Atomic (interlocked) type.
    /// </summary>
    /// <typeparam name="T">The structure backed by a 64-bit value.</typeparam>
    internal abstract class AtomicTypeBase<T> : IComparable, IComparable<T>, IComparable<AtomicTypeBase<T>>, IEquatable<T>, IEquatable<AtomicTypeBase<T>>
        where T : struct, IComparable, IComparable<T>, IEquatable<T>
    {
        private long backingValue;

        /// <summary>
        /// Initialises a new instance of the <see cref="AtomicTypeBase{T}"/>
        /// class.
        /// </summary>
        /// <param name="initialValue">The initial value.</param>
        protected AtomicTypeBase(long initialValue)
        {
            this.BackingValue = initialValue;
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        public T Value
        {
            get => this.FromLong(this.BackingValue);
            set => this.BackingValue = this.ToLong(value);
        }

        /// <summary>
        /// Gets or sets the backing value.
        /// </summary>
        protected long BackingValue
        {
            get => Interlocked.Read(ref this.backingValue);
            set => Interlocked.Exchange(ref this.backingValue, value);
        }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator ==(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => Equals(left, right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator ==(AtomicTypeBase<T> left, T right) => Equals(left, right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator ==(T left, AtomicTypeBase<T> right) => Equals(left, right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator !=(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => !Equals(left, right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator !=(AtomicTypeBase<T> left, T right) => !Equals(left, right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator !=(T left, AtomicTypeBase<T> right) => !Equals(left, right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >(AtomicTypeBase<T> left, T right) => CompareTo(left, right) > 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => CompareTo(left, right) > 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >(T left, AtomicTypeBase<T> right) => CompareTo(left, right) > 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <(AtomicTypeBase<T> left, T right) => CompareTo(left, right) < 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => CompareTo(left, right) < 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <(T left, AtomicTypeBase<T> right) => CompareTo(left, right) < 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >=(AtomicTypeBase<T> left, T right) => CompareTo(left, right) >= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >=(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => CompareTo(left, right) >= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >=(T left, AtomicTypeBase<T> right) => CompareTo(left, right) >= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <=(AtomicTypeBase<T> left, T right) => CompareTo(left, right) <= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <=(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => CompareTo(left, right) <= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <=(T left, AtomicTypeBase<T> right) => CompareTo(left, right) <= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="instance">The operand.</param>
        /// <returns>The result of the operation.</returns>
        public static AtomicTypeBase<T> operator ++(AtomicTypeBase<T> instance)
        {
            Interlocked.Increment(ref instance.backingValue);
            return instance;
        }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="instance">The operand.</param>
        /// <returns>The result of the operation.</returns>
        public static AtomicTypeBase<T> operator --(AtomicTypeBase<T> instance)
        {
            Interlocked.Decrement(ref instance.backingValue);
            return instance;
        }

        /// <summary>
        /// Increments the value of the the backing field.
        /// </summary>
        public virtual void Increment() => Interlocked.Increment(ref this.backingValue);

        /// <summary>
        /// Decrements the value of the the backing field.
        /// </summary>
        public virtual void Decrement() => Interlocked.Decrement(ref this.backingValue);

        /// <inheritdoc />
        public int CompareTo(object obj)
        {
            switch (obj)
            {
                case null:
                    return 1;
                case AtomicTypeBase<T> atomicType:
                    return CompareTo(this, atomicType);
                case T variable:
                    return CompareTo(this, variable);
                default:
                    throw new ArgumentException("Incompatible comparison types");
            }
        }

        /// <inheritdoc />
        public int CompareTo(T other) => CompareTo(this, other);

        /// <inheritdoc />
        public int CompareTo(AtomicTypeBase<T> other) => CompareTo(this, other);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            switch (obj)
            {
                case null:
                    return false;
                case AtomicTypeBase<T> atomicType:
                    return Equals(this, atomicType);
                case T variable:
                    return Equals(this, variable);
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override int GetHashCode() => this.BackingValue.GetHashCode();

        /// <inheritdoc />
        public bool Equals(AtomicTypeBase<T> other) => Equals(this, other);

        /// <inheritdoc />
        public bool Equals(T other) => Equals(this.Value, other);

        /// <summary>
        /// Converts from a long value to the target type.
        /// </summary>
        /// <param name="backingValue">The backing value.</param>
        /// <returns>The value converted form a long value.</returns>
        protected abstract T FromLong(long backingValue);

        /// <summary>
        /// Converts from the target type to a long value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The value converted to a long value.</returns>
        protected abstract long ToLong(T value);

#pragma warning disable IDE0041 // Use 'is null' check

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareTo(AtomicTypeBase<T> left, AtomicTypeBase<T> right)
        {
            switch (left)
            {
                case null when ReferenceEquals(right, null):
                    return 0;
                case null:
                    return -1;
                default:
                    return ReferenceEquals(right, null) ? 1 :
                        left.BackingValue.CompareTo(right.BackingValue);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareTo(AtomicTypeBase<T> left, T right) => ReferenceEquals(left, null) ? -1 : left.Value.CompareTo(right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareTo(T left, AtomicTypeBase<T> right) => ReferenceEquals(right, null) ? 1 : left.CompareTo(right.Value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Equals(AtomicTypeBase<T> left, AtomicTypeBase<T> right) => CompareTo(left, right) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Equals(AtomicTypeBase<T> left, T right) => CompareTo(left, right) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Equals(T left, AtomicTypeBase<T> right) => CompareTo(left, right) == 0;

#pragma warning restore IDE0041 // Use 'is null' check
    }
}
