// <copyright file="MediaContainerException.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System;
    using System.Runtime.Serialization;

    /// <inheritdoc cref="Exception"/>
    /// <summary>
    /// A Media Container Exception.
    /// </summary>
    [Serializable]
    public class MediaContainerException : Exception
    {
        // TODO: Add error code property and enumerate error codes.

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaContainerException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public MediaContainerException(string message)
            : base(message)
        {
            // placeholder
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaContainerException"/> class.
        /// </summary>
        public MediaContainerException()
            : base("Unidentified media container exception")
        {
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaContainerException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public MediaContainerException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaContainerException"/> class.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        protected MediaContainerException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            // placholder
        }
    }
}
