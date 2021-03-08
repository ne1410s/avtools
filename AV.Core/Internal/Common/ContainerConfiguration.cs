﻿// <copyright file="ContainerConfiguration.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Common
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a set of options that are used to initialize a media
    /// container before opening the stream. This includes both, demuxer and
    /// decoder options.
    /// </summary>
    internal sealed class ContainerConfiguration
    {
        /// <summary>
        /// The scan all PMTS private option name.
        /// </summary>
        internal const string ScanAllPmts = "scan_all_pmts";

        /// <summary>
        /// Initialises a new instance of the
        /// <see cref="ContainerConfiguration"/> class.
        /// </summary>
        internal ContainerConfiguration()
        {
        }

        /// <summary>
        /// Gets or sets the forced input format. If let null or empty,
        /// the input format will be selected automatically.
        /// </summary>
        public string ForcedInputFormat { get; set; }

        /// <summary>
        /// Gets or sets the protocol prefix.
        /// Typically async for local files and empty for other types.
        /// </summary>
        public string ProtocolPrefix { get; set; }

        /// <summary>
        /// Gets or sets the amount of time to wait for a an open or read
        /// operation to complete before it times out. 10 seconds, by default.
        /// </summary>
        public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets global options for the demuxer. For additional info
        /// please see: https://ffmpeg.org/ffmpeg-formats.html#Format-Options.
        /// </summary>
        public DemuxerGlobalOptions GlobalOptions { get; } = new ();

        /// <summary>
        /// Gets private demuxer options. For additional info
        /// please see: https://ffmpeg.org/ffmpeg-all.html#Demuxers.
        /// </summary>
        public Dictionary<string, string> PrivateOptions { get; } =
            new (512, StringComparer.InvariantCultureIgnoreCase);
    }
}
