// <copyright file="OptionMetadata.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using FFmpeg.AutoGen;

    /// <summary>
    /// Provides information about a named option for a demuxer or a codec.
    /// </summary>
    public sealed class OptionMetadata
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="OptionMetadata"/> class.
        /// </summary>
        /// <param name="option">The option.</param>
        internal unsafe OptionMetadata(AVOption* option)
        {
            this.OptionType = option->type;
            this.Flags = option->flags;
            this.HelpText = Utilities.PtrToStringUTF8(option->help);
            this.Name = Utilities.PtrToStringUTF8(option->name);
            this.Min = option->min;
            this.Max = option->max;

            // Default values
            // DefaultString = FFInterop.PtrToStringUTF8(option->default_val.str); // TODO: This throws a memory violation for some reason
            this.DefaultDouble = option->default_val.dbl;
            this.DefaultLong = option->default_val.i64;
            this.DefaultRational = option->default_val.q;

            // Flag Parsing
            this.IsAudioOption = (option->flags & ffmpeg.AV_OPT_FLAG_AUDIO_PARAM) > 0;
            this.IsBsfOption = (option->flags & ffmpeg.AV_OPT_FLAG_BSF_PARAM) > 0;
            this.IsDecodingOption = (option->flags & ffmpeg.AV_OPT_FLAG_DECODING_PARAM) > 0;
            this.IsEncodingOption = (option->flags & ffmpeg.AV_OPT_FLAG_ENCODING_PARAM) > 0;
            this.IsExported = (option->flags & ffmpeg.AV_OPT_FLAG_EXPORT) > 0;
            this.IsFilteringOption = (option->flags & ffmpeg.AV_OPT_FLAG_FILTERING_PARAM) > 0;
            this.IsReadonly = (option->flags & ffmpeg.AV_OPT_FLAG_READONLY) > 0;
            this.IsSubtitleOption = (option->flags & ffmpeg.AV_OPT_FLAG_SUBTITLE_PARAM) > 0;
            this.IsVideoOption = (option->flags & ffmpeg.AV_OPT_FLAG_VIDEO_PARAM) > 0;
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the flags.
        /// </summary>
        public int Flags { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is encoding option.
        /// </summary>
        public bool IsEncodingOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is decoding option.
        /// </summary>
        public bool IsDecodingOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is filtering option.
        /// </summary>
        public bool IsFilteringOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is audio option.
        /// </summary>
        public bool IsAudioOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is subtitle option.
        /// </summary>
        public bool IsSubtitleOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is video option.
        /// </summary>
        public bool IsVideoOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is BSF option.
        /// </summary>
        public bool IsBsfOption { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is readonly.
        /// </summary>
        public bool IsReadonly { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is exported.
        /// </summary>
        public bool IsExported { get; }

        /// <summary>
        /// Gets the type of the option.
        /// </summary>
        public AVOptionType OptionType { get; }

        /// <summary>
        /// Gets the default long.
        /// </summary>
        public long DefaultLong { get; }

        /// <summary>
        /// Gets the default double.
        /// </summary>
        public double DefaultDouble { get; }

        /// <summary>
        /// Gets the default rational.
        /// </summary>
        public AVRational DefaultRational { get; }

        /// <summary>
        /// Gets the help text.
        /// </summary>
        public string HelpText { get; }

        /// <summary>
        /// Gets the minimum.
        /// </summary>
        public double Min { get; }

        /// <summary>
        /// Gets the maximum.
        /// </summary>
        public double Max { get; }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"{this.Name} {this.OptionType.ToString().ReplaceOrdinal("AV_OPT_TYPE_", string.Empty)}: {this.HelpText} ";
        }
    }
}
