// <copyright file="ILoggingSource.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Diagnostics
{
    /// <summary>
    /// Defines interface members for a class that
    /// defines a logging message handler <see cref="ILoggingHandler"/>.
    /// </summary>
    public interface ILoggingSource
    {
        /// <summary>
        /// Gets the logging handler.
        /// </summary>
        ILoggingHandler LoggingHandler { get; }
    }
}
