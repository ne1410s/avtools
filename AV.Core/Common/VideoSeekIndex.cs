// <copyright file="VideoSeekIndex.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using AV.Core.Container;
    using FFmpeg.AutoGen;

    /// <summary>
    /// Provides a collection of <see cref="VideoSeekIndexEntry"/>.
    /// Seek entries are contain specific positions where key frames (or I frames) are located
    /// within a seekable stream.
    /// </summary>
    public sealed class VideoSeekIndex
    {
        private const string VersionPrefix = "FILE-SECTION-V01";
        private static readonly string SectionHeaderText = $"{VersionPrefix}:{nameof(VideoSeekIndex)}.{nameof(Entries)}";
        private static readonly string SectionHeaderFields = $"{nameof(StreamIndex)},{nameof(MediaSource)}";
        private static readonly string SectionDataText = $"{VersionPrefix}:{nameof(VideoSeekIndex)}.{nameof(Entries)}";
        private static readonly string SectionDataFields =
            $"{nameof(VideoSeekIndexEntry.StreamIndex)}" +
            $",{nameof(VideoSeekIndexEntry.StreamTimeBase)}Num" +
            $",{nameof(VideoSeekIndexEntry.StreamTimeBase)}Den" +
            $",{nameof(VideoSeekIndexEntry.StartTime)}" +
            $",{nameof(VideoSeekIndexEntry.PresentationTime)}" +
            $",{nameof(VideoSeekIndexEntry.DecodingTime)}";

        private readonly VideoSeekIndexEntryComparer lookupComparer = new VideoSeekIndexEntryComparer();

        /// <summary>
        /// Initialises a new instance of the <see cref="VideoSeekIndex"/> class.
        /// </summary>
        /// <param name="mediaSource">The source URL.</param>
        /// <param name="streamIndex">Index of the stream.</param>
        public VideoSeekIndex(string mediaSource, int streamIndex)
        {
            this.MediaSource = mediaSource;
            this.StreamIndex = streamIndex;
        }

        /// <summary>
        /// Provides access to the seek entries.
        /// </summary>
        public List<VideoSeekIndexEntry> Entries { get; } = new List<VideoSeekIndexEntry>(2048);

        /// <summary>
        /// Gets the stream index this seeking index belongs to.
        /// </summary>
        public int StreamIndex { get; internal set; }

        /// <summary>
        /// Gets the source URL this seeking index belongs to.
        /// </summary>
        public string MediaSource { get; internal set; }

        /// <summary>
        /// Loads the specified stream in the CSV-like UTF8 format it was written by the <see cref="Save(Stream)"/> method.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>The loaded index from the specified stream.</returns>
        public static VideoSeekIndex Load(Stream stream)
        {
            var separator = new[] { ',' };
            var trimQuotes = new[] { '"' };
            var result = new VideoSeekIndex(null, -1);

            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var state = 0;

                while (reader.EndOfStream == false)
                {
                    var line = reader.ReadLine()?.Trim() ?? string.Empty;
                    if (state == 0 && line == SectionHeaderText)
                    {
                        state = 1;
                        continue;
                    }

                    if (state == 1 && line == SectionHeaderFields)
                    {
                        state = 2;
                        continue;
                    }

                    if (state == 2 && !string.IsNullOrWhiteSpace(line))
                    {
                        var parts = line.Split(separator, 2);
                        if (parts.Length >= 2)
                        {
                            if (int.TryParse(parts[0], out var index))
                            {
                                result.StreamIndex = index;
                            }

                            result.MediaSource = parts[1]
                                .Trim(trimQuotes)
                                .ReplaceOrdinal("\"\"", "\"");
                        }

                        state = 3;
                    }

                    if (state == 3 && line == SectionDataText)
                    {
                        state = 4;
                        continue;
                    }

                    if (state == 4 && line == SectionDataFields)
                    {
                        state = 5;
                        continue;
                    }

                    if (state == 5 && !string.IsNullOrWhiteSpace(line))
                    {
                        if (VideoSeekIndexEntry.FromCsvString(line) is VideoSeekIndexEntry entry)
                        {
                            result.Entries.Add(entry);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Writes the index data to the specified stream in CSV-like UTF8 text format.
        /// </summary>
        /// <param name="stream">The stream to write data to.</param>
        public void Save(Stream stream)
        {
            using (var writer = new StreamWriter(stream, Encoding.UTF8, 4096, true))
            {
                writer.WriteLine(SectionHeaderText);
                writer.WriteLine(SectionHeaderFields);
                writer.WriteLine($"{this.StreamIndex},\"{this.MediaSource?.ReplaceOrdinal("\"", "\"\"")}\"");

                writer.WriteLine(SectionDataText);
                writer.WriteLine(SectionDataFields);
                foreach (var entry in this.Entries)
                {
                    writer.WriteLine(entry.ToCsvString());
                }
            }
        }

        /// <summary>
        /// Finds the closest seek entry that is on or prior to the seek target.
        /// </summary>
        /// <param name="seekTarget">The seek target.</param>
        /// <returns>The seek entry or null of not found.</returns>
        public VideoSeekIndexEntry Find(TimeSpan seekTarget)
        {
            var index = this.Entries.StartIndexOf(seekTarget);
            if (index < 0)
            {
                return null;
            }

            return this.Entries[index];
        }

        /// <summary>
        /// Tries to add an entry created from the frame.
        /// </summary>
        /// <param name="managedFrame">The managed frame.</param>
        /// <returns>
        /// True if the index entry was created from the frame.
        /// False if the frame is of wrong picture type or if it already existed.
        /// </returns>
        internal bool TryAdd(VideoFrame managedFrame)
        {
            // Update the Seek index
            if (managedFrame.PictureType != AVPictureType.AV_PICTURE_TYPE_I)
            {
                return false;
            }

            // Create the seek entry
            var seekEntry = new VideoSeekIndexEntry(managedFrame);

            // Check if the entry already exists.
            if (this.Entries.BinarySearch(seekEntry, this.lookupComparer) >= 0)
            {
                return false;
            }

            // Add the seek entry and ensure they are sorted.
            this.Entries.Add(seekEntry);
            this.Entries.Sort(this.lookupComparer);
            return true;
        }

        /// <summary>
        /// A comparer for <see cref="VideoSeekIndexEntry"/>.
        /// </summary>
        private class VideoSeekIndexEntryComparer : IComparer<VideoSeekIndexEntry>
        {
            /// <inheritdoc />
            public int Compare(VideoSeekIndexEntry x, VideoSeekIndexEntry y) =>
                x.StartTime.Ticks.CompareTo(y.StartTime.Ticks);
        }
    }
}
