// <copyright file="MediaWorkerType.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Engine
{
    /// <summary>
    /// Defines the different worker types.
    /// </summary>
    internal enum MediaWorkerType
    {
        /// <summary>
        /// The packet reading worker.
        /// </summary>
        Read,

        /// <summary>
        /// The frame decoding worker.
        /// </summary>
        Decode,

        /// <summary>
        /// The block rendering worker.
        /// </summary>
        Render,
    }
}
