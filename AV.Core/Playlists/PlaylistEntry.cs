// <copyright file="PlaylistEntry.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Playlists
{
    using System;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Represents a generic playlist entry.
    /// </summary>
    public class PlaylistEntry : INotifyPropertyChanged
    {
        private string localMediaSource;
        private string localTitle;
        private TimeSpan localDuration;

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the media source URL.
        /// </summary>
        public string MediaSource
        {
            get => this.localMediaSource;
            set => this.SetProperty(ref this.localMediaSource, value);
        }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string Title
        {
            get => this.localTitle;
            set => this.SetProperty(ref this.localTitle, value);
        }

        /// <summary>
        /// Gets or sets the duration.
        /// </summary>
        public TimeSpan Duration
        {
            get => this.localDuration;
            set => this.SetProperty(ref this.localDuration, value);
        }

        /// <summary>
        /// Gets the extended attributes.
        /// </summary>
        public PlaylistEntryAttributeDictionary Attributes { get; } = new PlaylistEntryAttributeDictionary();

        /// <summary>
        /// Checks if a property already matches a desired value.  Sets the property and
        /// notifies listeners only when necessary.
        /// </summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="storage">Reference to a property with both getter and setter.</param>
        /// <param name="value">Desired value for the property.</param>
        /// <param name="propertyName">Name of the property used to notify listeners.  This
        /// value is optional and can be provided automatically when invoked from compilers that
        /// support CallerMemberName.</param>
        /// <returns>True if the value was changed, false if the existing value matched the
        /// desired value.</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            this.OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Notifies listeners that a property value has changed.
        /// </summary>
        /// <param name="propertyName">Name of the property used to notify listeners.  This
        /// value is optional and can be provided automatically when invoked from compilers
        /// that support <see cref="CallerMemberNameAttribute"/>.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
