// <copyright file="VideoResolutionDivider.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Common
{
    /// <summary>
    /// Enumerates the different low resolution divider indices.
    /// </summary>
    internal enum VideoResolutionDivider
    {
        /// <summary>
        /// Represents no resolution reduction.
        /// </summary>
        Full = 0,

        /// <summary>
        /// Represents 1/2 resolution.
        /// </summary>
        Half = 1,

        /// <summary>
        /// Represents 1/4 resolution.
        /// </summary>
        Quarter = 2,

        /// <summary>
        /// Represents 1/8 resolution.
        /// </summary>
        Eighth = 3,
    }
}
