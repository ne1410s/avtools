﻿// <copyright file="GeneralUtilities.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Internal.Utilities
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text;

    /// <summary>
    /// Provides various helpers and extension methods.
    /// </summary>
    internal static class GeneralUtilities
    {
        /// <summary>
        /// Converts a byte pointer to a UTF8 encoded string.
        /// </summary>
        /// <param name="stringAddress">Pointer to the first character.</param>
        /// <returns>The string.</returns>
        public static unsafe string PtrToStringUTF8(byte* stringAddress)
        {
            if (stringAddress == null)
            {
                return null;
            }

            if (*stringAddress == 0)
            {
                return string.Empty;
            }

            var byteLength = 0;
            while (true)
            {
                if (stringAddress[byteLength] == 0)
                {
                    break;
                }

                byteLength++;
            }

            var stringBuffer = stackalloc byte[byteLength];
            Buffer.MemoryCopy(stringAddress, stringBuffer, byteLength, byteLength);
            return Encoding.UTF8.GetString(stringBuffer, byteLength);
        }

        /// <summary>
        /// A cross-platform implementation of string.Replace.
        /// </summary>
        /// <param name="source">The string to search.</param>
        /// <param name="find">The string to find.</param>
        /// <param name="replace">The string to replace.</param>
        /// <returns>The string with the replacement.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ReplaceOrdinal(this string source, string find, string replace) =>
#if NETCOREAPP
            source?.Replace(find, replace, StringComparison.Ordinal);
#else
            source?.Replace(find, replace);
#endif

        /// <summary>
        /// Determines if the string contains the search term in ordinal
        /// (binary) comparison.
        /// </summary>
        /// <param name="source">The string to search.</param>
        /// <param name="find">The search term.</param>
        /// <returns>Whether the search term is found in the string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsOrdinal(this string source, string find) =>
            source != null && source.IndexOf(find, StringComparison.Ordinal) > -1;
    }
}
