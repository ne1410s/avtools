﻿// <copyright file="FFDictionaryEntry.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace FFmpeg.AutoGen
{
    using System;
    using AV.Core;

    /// <summary>
    /// An AVDictionaryEntry wrapper.
    /// </summary>
    internal unsafe class FFDictionaryEntry
    {
        // This pointer is generated in unmanaged code.
        private readonly IntPtr localPointer;

        /// <summary>
        /// Initialises a new instance of the <see cref="FFDictionaryEntry"/> class.
        /// </summary>
        /// <param name="entryPointer">The entry pointer.</param>
        public FFDictionaryEntry(AVDictionaryEntry* entryPointer)
        {
            this.localPointer = new IntPtr(entryPointer);
        }

        /// <summary>
        /// Gets the unmanaged pointer.
        /// </summary>
        public AVDictionaryEntry* Pointer => (AVDictionaryEntry*)this.localPointer;

        /// <summary>
        /// Gets the key.
        /// </summary>
        public string Key => this.localPointer != IntPtr.Zero ? Utilities.PtrToStringUTF8(Pointer->key) : null;

        /// <summary>
        /// Gets the value.
        /// </summary>
        public string Value => this.localPointer != IntPtr.Zero ? Utilities.PtrToStringUTF8(Pointer->value) : null;
    }
}