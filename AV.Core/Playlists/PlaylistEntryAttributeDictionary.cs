// <copyright file="PlaylistEntryAttributeDictionary.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Playlists
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using System.Web;

    /// <summary>
    /// Represents a dictionary of attributes (key-value pairs).
    /// </summary>
    [Serializable]
    public class PlaylistEntryAttributeDictionary : Dictionary<string, string>
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="PlaylistEntryAttributeDictionary"/> class.
        /// </summary>
        public PlaylistEntryAttributeDictionary()
            : base(16)
        {
            // placeholder
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="PlaylistEntryAttributeDictionary"/> class.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        protected PlaylistEntryAttributeDictionary(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            // placeholder
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var attributes = new List<string>(this.Count);
            foreach (var kvp in this)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    continue;
                }

                var value = string.IsNullOrWhiteSpace(kvp.Value) ? string.Empty : kvp.Value;
                attributes.Add($"{HttpUtility.UrlEncode(kvp.Key)}=\"{HttpUtility.UrlEncode(value)}\"");
            }

            return string.Join(" ", attributes);
        }

        /// <summary>
        /// Gets the entry value safely.
        /// </summary>
        /// <param name="entryKey">The entry key.</param>
        /// <returns>The entry value or null.</returns>
        public string GetEntryValue(string entryKey)
        {
            return this.ContainsKey(entryKey) ? this[entryKey] : null;
        }

        /// <summary>
        /// Sets the entry value and returns true if the value changes.
        /// </summary>
        /// <param name="entryKey">The entry key.</param>
        /// <param name="value">The value.</param>
        /// <returns>True if the property changed, false otherwise.</returns>
        public bool SetEntryValue(string entryKey, string value)
        {
            var existingValue = this.GetEntryValue(entryKey);
            this[entryKey] = value;
            if (existingValue == null)
            {
                return true;
            }

            return Equals(existingValue, value) == false;
        }
    }
}
