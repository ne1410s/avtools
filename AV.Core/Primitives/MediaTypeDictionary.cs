﻿// <copyright file="MediaTypeDictionary.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using AV.Core.Common;

    /// <summary>
    /// Represents a very simple dictionary for MediaType keys.
    /// </summary>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    [Serializable]
    internal sealed class MediaTypeDictionary<TValue>
        : Dictionary<MediaType, TValue>
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="MediaTypeDictionary{TValue}"/> class.
        /// </summary>
        public MediaTypeDictionary()
            : base(Enum.GetValues(typeof(MediaType)).Length)
        {
            // placeholder
        }

        private MediaTypeDictionary(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            // placeholder
        }

        /// <summary>
        /// Gets or sets the item with the specified key.
        /// return the default value of the value type when the key does not exist.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The item.</returns>
        public new TValue this[MediaType key]
        {
            get => this.ContainsKey(key) == false ? default : base[key];
            internal set => base[key] = value;
        }
    }
}