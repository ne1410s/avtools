// <copyright file="ViewModelBase.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using AV.Core.Primitives;

    /// <summary>
    /// A base class for implementing models that fire notifications when their properties change.
    /// This class is ideal for implementing MVVM driven UIs.
    /// </summary>
    /// <seealso cref="INotifyPropertyChanged" />
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        private readonly ConcurrentDictionary<string, bool> localQueuedNotifications = new ConcurrentDictionary<string, bool>();
        private readonly bool localUseDeferredNotifications;

        /// <summary>
        /// Initialises a new instance of the <see cref="ViewModelBase"/> class.
        /// </summary>
        protected ViewModelBase()
            : this(false)
        {
            // placeholder
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="ViewModelBase"/> class.
        /// </summary>
        /// <param name="useDeferredNotifications">Set to <c>true</c> to use deferred notifications in the background.</param>
        protected ViewModelBase(bool useDeferredNotifications)
        {
            this.localUseDeferredNotifications = useDeferredNotifications;
        }

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        /// <returns>The property changed notification handler</returns>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Checks if a property already matches a desired value.  Sets the property and
        /// notifies listeners only when necessary.</summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="storage">Reference to a property with both getter and setter.</param>
        /// <param name="value">Desired value for the property.</param>
        /// <param name="propertyName">Name of the property used to notify listeners.  This
        /// value is optional and can be provided automatically when invoked from compilers that
        /// support CallerMemberName.</param>
        /// <param name="notifyAlso">An rray of property names to notify in addition to notifying the changes on the current property name.</param>
        /// <returns>True if the value was changed, false if the existing value matched the
        /// desired value.</returns>
        internal bool SetProperty<T>(AtomicTypeBase<T> storage, T value, [CallerMemberName] string propertyName = "", string[] notifyAlso = null)
            where T : struct, IComparable, IComparable<T>, IEquatable<T>
        {
            if (EqualityComparer<T>.Default.Equals(storage.Value, value))
            {
                return false;
            }

            storage.Value = value;
            this.NotifyPropertyChanged(propertyName, notifyAlso);
            return true;
        }

        /// <summary>Checks if a property already matches a desired value.  Sets the property and
        /// notifies listeners only when necessary.</summary>
        /// <typeparam name="T">Type of the property.</typeparam>
        /// <param name="storage">Reference to a property with both getter and setter.</param>
        /// <param name="value">Desired value for the property.</param>
        /// <param name="propertyName">Name of the property used to notify listeners.  This
        /// value is optional and can be provided automatically when invoked from compilers that
        /// support CallerMemberName.</param>
        /// <param name="notifyAlso">An rray of property names to notify in addition to notifying the changes on the current property name.</param>
        /// <returns>True if the value was changed, false if the existing value matched the
        /// desired value.</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "", string[] notifyAlso = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            this.NotifyPropertyChanged(propertyName, notifyAlso);
            return true;
        }

        /// <summary>
        /// Notifies one or more properties changed.
        /// </summary>
        /// <param name="propertyNames">The property names.</param>
        protected void NotifyPropertyChanged(params string[] propertyNames) => this.NotifyPropertyChanged(null, propertyNames);

        /// <summary>
        /// Notifies one or more properties changed.
        /// </summary>
        /// <param name="mainProperty">The main property.</param>
        /// <param name="auxiliaryProperties">The auxiliary properties.</param>
        private void NotifyPropertyChanged(string mainProperty, string[] auxiliaryProperties)
        {
            // Queue property notification
            if (string.IsNullOrWhiteSpace(mainProperty) == false)
            {
                this.localQueuedNotifications[mainProperty] = true;
            }

            // Set the state for notification properties
            if (auxiliaryProperties != null)
            {
                foreach (var property in auxiliaryProperties)
                {
                    if (string.IsNullOrWhiteSpace(property) == false)
                    {
                        this.localQueuedNotifications[property] = true;
                    }
                }
            }

            // Depending on operation mode, either fire the notifications in the background
            // or fire them immediately
            if (this.localUseDeferredNotifications)
            {
                Task.Run(this.NotifyQueuedProperties);
            }
            else
            {
                this.NotifyQueuedProperties();
            }
        }

        /// <summary>
        /// Notifies the queued properties and resets the property name to a non-queued stated.
        /// </summary>
        private void NotifyQueuedProperties()
        {
            // get a snapshot of property names.
            var propertyNames = this.localQueuedNotifications.Keys.ToArray();

            // Iterate through the properties
            foreach (var property in propertyNames)
            {
                // don't notify if we don't have a change
                if (!this.localQueuedNotifications[property])
                {
                    continue;
                }

                // notify and reset queued state to false
                try
                {
                    this.OnPropertyChanged(property);
                }
                finally
                {
                    this.localQueuedNotifications[property] = false;
                }
            }
        }

        /// <summary>
        /// Called when a property changes its backing value.
        /// </summary>
        /// <param name="propertyName">Name of the property.</param>
        private void OnPropertyChanged(string propertyName) =>
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
    }
}
