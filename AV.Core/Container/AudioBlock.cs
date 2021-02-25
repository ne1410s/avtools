// <copyright file="AudioBlock.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using AV.Core.Common;

    /// <summary>
    /// A scaled, pre-allocated audio frame container.
    /// The buffer is in 16-bit signed, interleaved sample data.
    /// </summary>
    internal sealed class AudioBlock : MediaBlock
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="AudioBlock"/> class.
        /// </summary>
        internal AudioBlock()
            : base(MediaType.Audio)
        {
            // placeholder
        }

        /// <summary>
        /// Gets the sample rate.
        /// </summary>
        public int SampleRate { get; internal set; }

        /// <summary>
        /// Gets the channel count.
        /// </summary>
        public int ChannelCount { get; internal set; }

        /// <summary>
        /// Gets the available samples per channel.
        /// </summary>
        public int SamplesPerChannel { get; internal set; }

        /// <summary>
        /// Gets the length of the samples buffer. This might differ from the <see cref="MediaBlock.BufferLength"/>
        /// property after scaling but must always be less than or equal to it.
        /// </summary>
        /// <value>
        /// The length of the samples buffer.
        /// </value>
        public int SamplesBufferLength { get; internal set; }

        /// <inheritdoc />
        protected override void Deallocate()
        {
            base.Deallocate();
            this.SampleRate = default;
            this.ChannelCount = default;
            this.SamplesPerChannel = default;
            this.SamplesBufferLength = 0;
        }
    }
}
