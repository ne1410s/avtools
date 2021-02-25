// <copyright file="SyncLockerFactory.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Threading;

    /// <summary>
    /// Provides factory methods to create synchronized reader-writer locks
    /// that support a generalized locking and releasing api and syntax.
    /// </summary>
    internal static class SyncLockerFactory
    {
        private const int DefaultTimeout = 100;

        /// <summary>
        /// Enumerates the locking operations.
        /// </summary>
        private enum LockHolderType
        {
            Read,
            Write,
        }

        /// <summary>
        /// Defines methods for releasing locks.
        /// </summary>
        private interface ISyncReleasable
        {
            /// <summary>
            /// Releases the writer lock.
            /// </summary>
            void ReleaseWriterLock();

            /// <summary>
            /// Releases the reader lock.
            /// </summary>
            void ReleaseReaderLock();
        }

        /// <summary>
        /// Creates a reader-writer lock backed by a standard ReaderWriterLock.
        /// </summary>
        /// <returns>The synchronized locker.</returns>
        public static ISyncLocker Create() => new SyncLocker();

        /// <summary>
        /// Creates a reader-writer lock backed by a ReaderWriterLockSlim.
        /// </summary>
        /// <returns>The synchronized locker.</returns>
        public static ISyncLocker CreateSlim() => new SyncLockerSlim();

        /// <summary>
        /// Creates a reader-writer lock.
        /// </summary>
        /// <param name="useSlim">if set to <c>true</c> it uses the Slim version of a reader-writer lock.</param>
        /// <returns>The Sync Locker.</returns>
        public static ISyncLocker Create(bool useSlim) => useSlim ? CreateSlim() : Create();

        /// <inheritdoc />
        private sealed class SyncLockReleaser : IDisposable
        {
            private readonly ISyncReleasable Parent;
            private readonly LockHolderType Operation;
            private bool IsDisposed;

            /// <summary>
            /// Initialises a new instance of the <see cref="SyncLockReleaser"/> class.
            /// </summary>
            /// <param name="parent">The parent.</param>
            /// <param name="operation">The operation.</param>
            public SyncLockReleaser(ISyncReleasable parent, LockHolderType operation)
            {
                this.Parent = parent;
                this.Operation = operation;

                if (parent == null)
                {
                    this.IsDisposed = true;
                }
            }

            /// <summary>
            /// An action-less, dummy disposable object.
            /// </summary>
            public static SyncLockReleaser Empty { get; } = new SyncLockReleaser(null, default);

            /// <inheritdoc />
            public void Dispose()
            {
                if (this.IsDisposed)
                {
                    return;
                }

                this.IsDisposed = true;

                if (this.Operation == LockHolderType.Read)
                {
                    this.Parent?.ReleaseReaderLock();
                }
                else
                {
                    this.Parent?.ReleaseWriterLock();
                }
            }
        }

        /// <summary>
        /// The Sync Locker backed by a ReaderWriterLock.
        /// </summary>
        /// <seealso cref="ISyncLocker" />
        /// <seealso cref="ISyncReleasable" />
        private sealed class SyncLocker : ISyncLocker, ISyncReleasable
        {
            private readonly AtomicBoolean localIsDisposed = new AtomicBoolean(false);
            private readonly ReaderWriterLock Locker = new ReaderWriterLock();

            /// <summary>
            /// Gets a value indicating whether this instance is disposed.
            /// </summary>
            public bool IsDisposed => this.localIsDisposed.Value;

            /// <inheritdoc />
            public IDisposable AcquireReaderLock()
            {
                this.AcquireReaderLock(Timeout.Infinite, out var releaser);
                return releaser;
            }

            /// <inheritdoc />
            public bool TryAcquireReaderLock(int timeoutMilliseconds, out IDisposable locker) =>
                this.AcquireReaderLock(timeoutMilliseconds, out locker);

            /// <inheritdoc />
            public IDisposable AcquireWriterLock()
            {
                this.AcquireWriterLock(Timeout.Infinite, out var releaser);
                return releaser;
            }

            /// <inheritdoc />
            public bool TryAcquireWriterLock(int timeoutMilliseconds, out IDisposable locker) =>
                this.AcquireWriterLock(timeoutMilliseconds, out locker);

            /// <inheritdoc />
            public bool TryAcquireWriterLock(out IDisposable locker) =>
                this.TryAcquireWriterLock(DefaultTimeout, out locker);

            /// <inheritdoc />
            public bool TryAcquireReaderLock(out IDisposable locker) =>
                this.TryAcquireReaderLock(DefaultTimeout, out locker);

            /// <inheritdoc />
            public void ReleaseWriterLock() => this.Locker.ReleaseWriterLock();

            /// <inheritdoc />
            public void ReleaseReaderLock() => this.Locker.ReleaseReaderLock();

            /// <inheritdoc />
            public void Dispose()
            {
                if (this.localIsDisposed == true)
                {
                    return;
                }

                this.localIsDisposed.Value = true;
                this.Locker.ReleaseLock();
            }

            /// <summary>
            /// Acquires the writer lock.
            /// </summary>
            /// <param name="timeoutMilliseconds">The timeout milliseconds.</param>
            /// <param name="releaser">The releaser.</param>
            /// <returns>Success.</returns>
            private bool AcquireWriterLock(int timeoutMilliseconds, out IDisposable releaser)
            {
                if (this.localIsDisposed == true)
                {
                    throw new ObjectDisposedException(nameof(ISyncLocker));
                }

                releaser = SyncLockReleaser.Empty;
                if (this.Locker.IsReaderLockHeld)
                {
                    this.Locker.AcquireReaderLock(timeoutMilliseconds);
                    releaser = new SyncLockReleaser(this, LockHolderType.Read);
                    return this.Locker.IsReaderLockHeld;
                }

                this.Locker.AcquireWriterLock(timeoutMilliseconds);
                if (this.Locker.IsWriterLockHeld)
                {
                    releaser = new SyncLockReleaser(this, LockHolderType.Write);
                }

                return this.Locker.IsWriterLockHeld;
            }

            /// <summary>
            /// Acquires the reader lock.
            /// </summary>
            /// <param name="timeoutMilliseconds">The timeout milliseconds.</param>
            /// <param name="releaser">The releaser.</param>
            /// <returns>Success.</returns>
            private bool AcquireReaderLock(int timeoutMilliseconds, out IDisposable releaser)
            {
                if (this.localIsDisposed == true)
                {
                    throw new ObjectDisposedException(nameof(ISyncLocker));
                }

                releaser = SyncLockReleaser.Empty;
                this.Locker.AcquireReaderLock(timeoutMilliseconds);
                if (!this.Locker.IsReaderLockHeld)
                {
                    return false;
                }

                releaser = new SyncLockReleaser(this, LockHolderType.Read);
                return true;
            }
        }

        /// <summary>
        /// The Sync Locker backed by ReaderWriterLockSlim.
        /// </summary>
        /// <seealso cref="ISyncLocker" />
        /// <seealso cref="ISyncReleasable" />
        private sealed class SyncLockerSlim : ISyncLocker, ISyncReleasable
        {
            private readonly AtomicBoolean localIsDisposed = new AtomicBoolean(false);
            private readonly ReaderWriterLockSlim Locker = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

            /// <inheritdoc />
            public bool IsDisposed => this.localIsDisposed.Value;

            /// <inheritdoc />
            public IDisposable AcquireReaderLock()
            {
                this.AcquireReaderLock(Timeout.Infinite, out var releaser);
                return releaser;
            }

            /// <inheritdoc />
            public bool TryAcquireReaderLock(int timeoutMilliseconds, out IDisposable locker) =>
                this.AcquireReaderLock(timeoutMilliseconds, out locker);

            /// <inheritdoc />
            public IDisposable AcquireWriterLock()
            {
                this.AcquireWriterLock(Timeout.Infinite, out var releaser);
                return releaser;
            }

            /// <inheritdoc />
            public bool TryAcquireWriterLock(int timeoutMilliseconds, out IDisposable locker) =>
                this.AcquireWriterLock(timeoutMilliseconds, out locker);

            /// <inheritdoc />
            public bool TryAcquireWriterLock(out IDisposable locker) =>
                this.TryAcquireWriterLock(DefaultTimeout, out locker);

            /// <inheritdoc />
            public bool TryAcquireReaderLock(out IDisposable locker) =>
                this.TryAcquireReaderLock(DefaultTimeout, out locker);

            /// <inheritdoc />
            public void ReleaseWriterLock() => this.Locker.ExitWriteLock();

            /// <inheritdoc />
            public void ReleaseReaderLock() => this.Locker.ExitReadLock();

            /// <inheritdoc />
            public void Dispose()
            {
                if (this.localIsDisposed == true)
                {
                    return;
                }

                this.localIsDisposed.Value = true;
                this.Locker.Dispose();
            }

            /// <summary>
            /// Acquires the writer lock.
            /// </summary>
            /// <param name="timeoutMilliseconds">The timeout milliseconds.</param>
            /// <param name="releaser">The releaser.</param>
            /// <returns>Success.</returns>
            private bool AcquireWriterLock(int timeoutMilliseconds, out IDisposable releaser)
            {
                if (this.localIsDisposed == true)
                {
                    throw new ObjectDisposedException(nameof(ISyncLocker));
                }

                releaser = SyncLockReleaser.Empty;
                bool result;

                if (this.Locker.IsReadLockHeld)
                {
                    result = this.Locker.TryEnterReadLock(timeoutMilliseconds);
                    if (result)
                    {
                        releaser = new SyncLockReleaser(this, LockHolderType.Read);
                    }

                    return result;
                }

                result = this.Locker.TryEnterWriteLock(timeoutMilliseconds);
                if (result)
                {
                    releaser = new SyncLockReleaser(this, LockHolderType.Write);
                }

                return result;
            }

            /// <summary>
            /// Acquires the reader lock.
            /// </summary>
            /// <param name="timeoutMilliseconds">The timeout milliseconds.</param>
            /// <param name="releaser">The releaser.</param>
            /// <returns>Success.</returns>
            private bool AcquireReaderLock(int timeoutMilliseconds, out IDisposable releaser)
            {
                if (this.localIsDisposed == true)
                {
                    throw new ObjectDisposedException(nameof(ISyncLocker));
                }

                releaser = SyncLockReleaser.Empty;
                var result = this.Locker.TryEnterReadLock(timeoutMilliseconds);
                if (result)
                {
                    releaser = new SyncLockReleaser(this, LockHolderType.Read);
                }

                return result;
            }
        }
    }
}
