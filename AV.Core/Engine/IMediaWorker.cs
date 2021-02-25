// <copyright file="IMediaWorker.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    using AV.Core.Primitives;

    /// <summary>
    /// Represents a worker API owned by a <see cref="MediaEngine"/>.
    /// </summary>
    /// <seealso cref="IWorker" />
    internal interface IMediaWorker : IWorker
    {
        /// <summary>
        /// Gets the media core this worker belongs to.
        /// </summary>
        MediaEngine MediaCore { get; }
    }
}
