// <copyright file="MediaContainerException.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Common
{
    using System;

    /// <inheritdoc cref="Exception"/>
    /// <summary>
    /// A Media Container Exception.
    /// </summary>
    [Serializable]
    internal class MediaContainerException : Exception
    {
        /// <summary>
        /// Initialises a new instance of the
        /// <see cref="MediaContainerException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public MediaContainerException(string message)
            : base(message)
        {
        }
    }
}
