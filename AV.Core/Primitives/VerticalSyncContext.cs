﻿// <copyright file="VerticalSyncContext.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Primitives
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;

    /// <summary>
    /// Vertical Synchronization provider.
    /// Ideas taken from:
    /// 1. https://github.com/fuse-open/fuse-studio/blob/master/Source/Fusion/Windows/VerticalSynchronization.cs.
    /// 2. https://gist.github.com/anonymous/4397e4909c524c939bee.
    /// Related Discussion:
    /// https://bugs.chromium.org/p/chromium/issues/detail?id=467617.
    /// </summary>
    internal sealed class VerticalSyncContext : IDisposable
    {
        private static readonly object NativeSyncLock = new object();
        private readonly Stopwatch refreshStopwatch = Stopwatch.StartNew();
        private readonly object syncLock = new object();
        private bool isDisposed;
        private DisplayDeviceInfo? displayDevice;
        private AdapterInfo currentAdapterInfo;
        private VerticalSyncEventInfo verticalSyncEvent;
        private double refreshCount;

        /// <summary>
        /// Initialises static members of the <see cref="VerticalSyncContext"/> class.
        /// Initializes static members of the <see cref="VerticalSyncContext"/> class.
        /// </summary>
        static VerticalSyncContext()
        {
            IsAvailable = IsWindowsVistaOrAbove && PrimaryDisplayDevice != null;
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="VerticalSyncContext"/> class.
        /// </summary>
        public VerticalSyncContext()
        {
            lock (this.syncLock)
            {
                this.EnsureAdapter();
            }
        }

        /// <summary>
        /// Enumerates the device state flags.
        /// </summary>
        [Flags]
        private enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x1,
            MultiDriver = 0x2,
            PrimaryDevice = 0x4,
            MirroringDriver = 0x8,
            VGACompatible = 0x10,
            Removable = 0x20,
            ModesPruned = 0x8000000,
            Remote = 0x4000000,
            Disconnect = 0x2000000,
        }

        /// <summary>
        /// Gets a value indicating whether Vertical Synchronization is available on the system.
        /// </summary>
        public static bool IsAvailable { get; private set; }

        /// <summary>
        /// Gets the display device refresh rate in Hz.
        /// </summary>
        public double RefreshRateHz { get; private set; } = 60;

        /// <summary>
        /// Gets the refresh period of the display device.
        /// </summary>
        public TimeSpan RefreshPeriod => TimeSpan.FromSeconds(1d / this.RefreshRateHz);

        /// <summary>
        /// Gets a value indicating whether this system is running Windows Vista or above.
        /// </summary>
        private static bool IsWindowsVistaOrAbove =>
            Environment.OSVersion.Platform == PlatformID.Win32NT &&
            Environment.OSVersion.Version.Major >= 6;

        /// <summary>
        /// Gets the display devices.
        /// </summary>
        private static DisplayDeviceInfo[] DisplayDevices
        {
            get
            {
                lock (NativeSyncLock)
                {
                    var structSize = Marshal.SizeOf<DisplayDeviceInfo>();
                    var result = new List<DisplayDeviceInfo>(16);

                    try
                    {
                        var deviceIndex = 0u;
                        while (true)
                        {
                            var d = default(DisplayDeviceInfo);
                            d.StructSize = structSize;
                            if (!NativeMethods.EnumDisplayDevices(null, deviceIndex, ref d, 0))
                            {
                                break;
                            }

                            result.Add(d);
                            deviceIndex++;
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    return result.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets the primary display device.
        /// </summary>
        private static DisplayDeviceInfo? PrimaryDisplayDevice
        {
            get
            {
                var devices = DisplayDevices;
                if (devices.Length == 0 || !devices.Any(d => d.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice)))
                {
                    return null;
                }

                return devices.First(d => d.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice));
            }
        }

        /// <summary>
        /// An alternative, less precise method to <see cref="WaitForBlank"/> for synchronizing pictures to the monitor's refresh rate.
        /// Requires DWM composition enabled on Windows Vista and above.
        /// For further info, see https://docs.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush.
        /// </summary>
        public static void Flush() => NativeMethods.DwmFlush();

        /// <summary>
        /// Waits for the vertical blanking interval on the primary display adapter to occur and then returns.
        /// </summary>
        /// <returns>True if the wait was performed using the adapter, and false otherwise.</returns>
        public bool WaitForBlank()
        {
            lock (this.syncLock)
            {
                try
                {
                    if (!IsAvailable || !this.EnsureAdapter())
                    {
                        Thread.Sleep(Constants.DefaultTimingPeriod);
                        return false;
                    }

                    try
                    {
                        var waitResult = NativeMethods.D3DKMTWaitForVerticalBlankEvent(ref this.verticalSyncEvent);
                        if (waitResult != 0)
                        {
                            throw new Exception("Adapter needs to be recreated. Resources will be released.");
                        }

                        return true;
                    }
                    catch
                    {
                        this.ReleaseAdapter();
                        return false;
                    }
                }
                finally
                {
                    this.refreshCount++;

                    if (this.refreshCount >= 60)
                    {
                        this.RefreshRateHz = this.refreshCount / this.refreshStopwatch.Elapsed.TotalSeconds;
                        this.refreshStopwatch.Restart();
                        this.refreshCount = 0;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (this.syncLock)
            {
                if (this.isDisposed)
                {
                    return;
                }

                this.isDisposed = true;

                this.ReleaseAdapter();
            }
        }

        /// <summary>
        /// Ensures the adapter is avaliable.
        /// If the adapter cannot be created, the <see cref="IsAvailable"/> property is permanently set to false.
        /// </summary>
        /// <returns>True if the adapter is available, and false otherwise.</returns>
        private bool EnsureAdapter()
        {
            if (this.displayDevice == null)
            {
                this.displayDevice = PrimaryDisplayDevice;
                if (this.displayDevice == null)
                {
                    IsAvailable = false;
                    return false;
                }
            }

            if (this.currentAdapterInfo.DCHandle == IntPtr.Zero)
            {
                try
                {
                    this.currentAdapterInfo = default;
                    this.currentAdapterInfo.DCHandle = NativeMethods.CreateDC(this.displayDevice.Value.DeviceName, null, null, IntPtr.Zero);
                    if (this.currentAdapterInfo.DCHandle == IntPtr.Zero)
                    {
                        throw new NotSupportedException("Unable to create DC for adapter.");
                    }
                }
                catch
                {
                    this.ReleaseAdapter();
                    IsAvailable = false;
                    return false;
                }
            }

            if (this.verticalSyncEvent.AdapterHandle == 0 && this.currentAdapterInfo.DCHandle != IntPtr.Zero)
            {
                try
                {
                    var openAdapterResult = NativeMethods.D3DKMTOpenAdapterFromHdc(ref this.currentAdapterInfo);
                    if (openAdapterResult == 0)
                    {
                        this.verticalSyncEvent = default;
                        this.verticalSyncEvent.AdapterHandle = this.currentAdapterInfo.AdapterHandle;
                        this.verticalSyncEvent.DeviceHandle = 0;
                        this.verticalSyncEvent.PresentSourceId = this.currentAdapterInfo.PresentSourceId;
                    }
                    else
                    {
                        throw new NotSupportedException("Unable to open D3D adapter.");
                    }
                }
                catch
                {
                    this.ReleaseAdapter();
                    IsAvailable = false;
                    return false;
                }
            }

            return this.verticalSyncEvent.AdapterHandle != 0;
        }

        /// <summary>
        /// Releases the adapter and associated unmanaged references.
        /// </summary>
        private void ReleaseAdapter()
        {
            if (this.currentAdapterInfo.AdapterHandle != 0)
            {
                try
                {
                    var closeInfo = default(CloseAdapterInfo);
                    closeInfo.AdapterHandle = this.currentAdapterInfo.AdapterHandle;

                    // This will return 0 on success, and another value for failure.
                    var closeAdapterResult = NativeMethods.D3DKMTCloseAdapter(ref closeInfo);
                }
                catch
                {
                    // ignore
                }
            }

            if (this.currentAdapterInfo.DCHandle != IntPtr.Zero)
            {
                try
                {
                    // this will return 1 on success, 0 on failure.
                    var deleteContextResult = NativeMethods.DeleteDC(this.currentAdapterInfo.DCHandle);
                }
                catch
                {
                    // ignore
                }
            }

            this.displayDevice = null;
            this.currentAdapterInfo = default;
            this.verticalSyncEvent = default;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterInfo
        {
            public IntPtr DCHandle;
            public uint AdapterHandle;
            public uint AdapterLuidLowPart;
            public uint AdapterLuidHighPart;
            public uint PresentSourceId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VerticalSyncEventInfo
        {
            public uint AdapterHandle;
            public uint DeviceHandle;
            public uint PresentSourceId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CloseAdapterInfo
        {
            public uint AdapterHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DisplayDeviceInfo
        {
            [MarshalAs(UnmanagedType.U4)]
            public int StructSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            [MarshalAs(UnmanagedType.U4)]
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        private static class NativeMethods
        {
            private const string GDI32 = "Gdi32.dll";
            private const string USER32 = "User32.dll";
            private const string DWMAPI = "DwmApi.dll";

            [DllImport(USER32, CharSet = CharSet.Unicode)]
            public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DisplayDeviceInfo lpDisplayDevice, uint dwFlags);

            [DllImport(GDI32, CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

            [DllImport(USER32)]
            public static extern IntPtr GetDesktopWindow();

            [DllImport(USER32)]
            public static extern IntPtr GetDC(IntPtr windowHandle);

            [DllImport(GDI32)]
            public static extern uint DeleteDC(IntPtr deviceContextHandle);

            [DllImport(GDI32)]
            public static extern uint D3DKMTOpenAdapterFromHdc(ref AdapterInfo adapterInfo);

            [DllImport(GDI32)]
            public static extern uint D3DKMTWaitForVerticalBlankEvent(ref VerticalSyncEventInfo eventInfo);

            [DllImport(GDI32)]
            public static extern uint D3DKMTCloseAdapter(ref CloseAdapterInfo adapterInfo);

            [DllImport(DWMAPI)]
            public static extern void DwmFlush();
        }
    }
}