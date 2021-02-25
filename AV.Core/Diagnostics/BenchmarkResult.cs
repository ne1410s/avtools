// <copyright file="BenchmarkResult.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Contains benchmark summary data.
    /// </summary>
    internal sealed class BenchmarkResult
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="BenchmarkResult"/> class.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="measures">The measures.</param>
        internal BenchmarkResult(string identifier, List<TimeSpan> measures)
        {
            this.Identifier = identifier;
            this.Count = measures.Count;
            this.Average = measures.Average(t => t.TotalMilliseconds);
            this.Min = measures.Min(t => t.TotalMilliseconds);
            this.Max = measures.Max(t => t.TotalMilliseconds);
        }

        /// <summary>
        /// Gets the benchmark identifier.
        /// </summary>
        public string Identifier { get; }

        /// <summary>
        /// Gets the measure count.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets the average time in milliseconds.
        /// </summary>
        public double Average { get; }

        /// <summary>
        /// Gets the minimum time in milliseconds.
        /// </summary>
        public double Min { get; }

        /// <summary>
        /// Gets the maximum time in milliseconds.
        /// </summary>
        public double Max { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"BID: {this.Identifier,-30} | CNT: {this.Count,6} | " +
                $"AVG: {this.Average,8:0.000} ms. | " +
                $"MAX: {this.Max,8:0.000} ms. | " +
                $"MIN: {this.Min,8:0.000} ms. | ";
        }
    }
}
