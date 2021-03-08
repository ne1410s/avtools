// <copyright file="DecoderOptions.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Common
{
    using System.Collections.Generic;
    using AV.Core.Internal.FFmpeg;
    using global::FFmpeg.AutoGen;

    /// <summary>
    /// Represents decoder global and private options for all streams
    /// See https://www.ffmpeg.org/ffmpeg-codecs.html#Codec-Options.
    /// </summary>
    internal sealed class DecoderOptions
    {
        private readonly Dictionary<string, string> globalOptions = new (64);
        private readonly Dictionary<int, Dictionary<string, string>> privateOptions = new ();

        /// <summary>
        /// Initialises a new instance of the <see cref="DecoderOptions"/>
        /// class.
        /// </summary>
        internal DecoderOptions()
        {
            this.Threads = "auto";
        }

        /// <summary>
        /// Gets or sets a value indicating whether [enable low resource].
        /// In theory this should be 0,1,2,3 for 1, 1/2, 1,4 and 1/8 resolutions.
        /// Port of low-res.
        /// </summary>
        public VideoResolutionDivider LowResolutionIndex { get; set; } = VideoResolutionDivider.Full;

        /// <summary>
        /// Gets or sets a value indicating whether to enable fast decoding.
        /// Port of fast.
        /// </summary>
        public bool EnableFastDecoding { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether low_delay flag for no delay
        /// in frame decoding. When frames are received by some codecs, they are
        /// delayed by 1 frame per active thread. This flag is not of much use
        /// because the decoder pre-caches and pre-orders a set of decoded
        /// frames internally.
        /// </summary>
        public bool EnableLowDelayDecoding { get; set; }

        /// <summary>
        /// Gets or sets the threads.
        /// </summary>
        public string Threads
        {
            get => this[GlobalOptionNames.Threads];
            set => this[GlobalOptionNames.Threads] = value;
        }

        /// <summary>
        /// Gets or sets whether to use reference counted frames.
        /// </summary>
        public string RefCountedFrames
        {
            get => this[GlobalOptionNames.RefCountedFrames];
            set => this[GlobalOptionNames.RefCountedFrames] = value;
        }

        /// <summary>
        /// Gets or sets the index of the low resolution index.
        /// </summary>
        internal string LowResIndexOption
        {
            get => this[GlobalOptionNames.LowRes];
            set => this[GlobalOptionNames.LowRes] = value;
        }

        /// <summary>
        /// Gets or sets the specified global option.
        /// See: https://www.ffmpeg.org/ffmpeg-codecs.html#Codec-Options.
        /// </summary>
        /// <param name="globalOptionName">Name of the global option.</param>
        /// <returns>The value of the option.</returns>
        public string this[string globalOptionName]
        {
            get => this.globalOptions.ContainsKey(globalOptionName)
                ? this.globalOptions[globalOptionName]
                : null;
            set => this.globalOptions[globalOptionName] = value;
        }

        /// <summary>
        /// Gets or sets the specified private option
        /// See: https://www.ffmpeg.org/ffmpeg-codecs.html#toc-Decoders.
        /// </summary>
        /// <param name="streamIndex">Index of the stream.</param>
        /// <param name="privateOptionName">Name of the private option.</param>
        /// <returns>The private option value.</returns>
        public string this[int streamIndex, string privateOptionName]
        {
            get
            {
                if (this.privateOptions.ContainsKey(streamIndex) == false)
                {
                    return null;
                }

                return this.privateOptions[streamIndex].ContainsKey(privateOptionName) ?
                    this.privateOptions[streamIndex][privateOptionName] : null;
            }

            set
            {
                if (this.privateOptions.ContainsKey(streamIndex) == false)
                {
                    this.privateOptions[streamIndex] = new ();
                }

                this.privateOptions[streamIndex][privateOptionName] = value;
            }
        }

        /// <summary>
        /// Gets the combined global and private stream codec options as a
        /// dictionary.
        /// </summary>
        /// <param name="streamIndex">Index of the stream.</param>
        /// <returns>An options dictionary.</returns>
        internal FFDictionary GetStreamCodecOptions(int streamIndex)
        {
            var result = new Dictionary<string, string>(this.globalOptions);
            if (!this.privateOptions.ContainsKey(streamIndex))
            {
                return new FFDictionary(result);
            }

            foreach (var kvp in this.privateOptions[streamIndex])
            {
                result[kvp.Key] = kvp.Value;
            }

            return new FFDictionary(result);
        }

        /// <summary>
        /// Well-known codec option names.
        /// </summary>
        private static class GlobalOptionNames
        {
            /// <summary>
            /// The threads.
            /// </summary>
            public const string Threads = "threads";

            /// <summary>
            /// The reference counted frames.
            /// </summary>
            public const string RefCountedFrames = "refcounted_frames";

            /// <summary>
            /// The low resource.
            /// </summary>
            public const string LowRes = "lowres";
        }
    }
}
