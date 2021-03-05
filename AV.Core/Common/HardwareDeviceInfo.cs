// <copyright file="HardwareDeviceInfo.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Common
{
    using FFmpeg.AutoGen;

    /// <summary>
    /// Represents a hardware configuration pair of device and pixel format.
    /// </summary>
    public sealed unsafe class HardwareDeviceInfo
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="HardwareDeviceInfo"/>
        /// class.
        /// </summary>
        /// <param name="config">The source configuration.</param>
        internal HardwareDeviceInfo(AVCodecHWConfig* config)
        {
            this.DeviceType = config->device_type;
            this.PixelFormat = config->pix_fmt;
            this.DeviceTypeName = ffmpeg.av_hwdevice_get_type_name(this.DeviceType);
            this.PixelFormatName = ffmpeg.av_get_pix_fmt_name(this.PixelFormat);
        }

        /// <summary>
        /// Gets the type of hardware device.
        /// </summary>
        public AVHWDeviceType DeviceType { get; }

        /// <summary>
        /// Gets the name of the device type.
        /// </summary>
        public string DeviceTypeName { get; }

        /// <summary>
        /// Gets the hardware output pixel format.
        /// </summary>
        public AVPixelFormat PixelFormat { get; }

        /// <summary>
        /// Gets the name of the pixel format.
        /// </summary>
        public string PixelFormatName { get; }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            return $"Device {this.DeviceTypeName}: {this.PixelFormatName}";
        }
    }
}
