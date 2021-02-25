// <copyright file="ClosedCaptionPacket.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.ClosedCaptions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <inheritdoc />
    /// <summary>
    /// Represents a 3-byte packet of closed-captioning data in EIA-608 format.
    /// See: http://jackyjung.tistory.com/attachment/499e14e28c347DB.pdf.
    /// </summary>
    public sealed class ClosedCaptionPacket : IComparable<ClosedCaptionPacket>, IEquatable<ClosedCaptionPacket>
    {
        private static readonly Dictionary<byte, string> SpecialNorthAmerican = new ()
        {
            { 0x30, "®" },
            { 0x31, "°" },
            { 0x32, "½" },
            { 0x33, "¿" },
            { 0x34, "™" },
            { 0x35, "¢" },
            { 0x36, "£" },
            { 0x37, "♪" },
            { 0x38, "à" },
            { 0x39, " " },
            { 0x3A, "è" },
            { 0x3B, "â" },
            { 0x3C, "ê" },
            { 0x3D, "î" },
            { 0x3E, "ô" },
            { 0x3F, "û" },
        };

        private static readonly Dictionary<byte, string> Spanish = new ()
        {
            { 0x20, "Á" },
            { 0x21, "É" },
            { 0x22, "Ó" },
            { 0x23, "Ú" },
            { 0x24, "Ü" },
            { 0x25, "ü" },
            { 0x26, "´" },
            { 0x27, "¡" },
            { 0x28, "*" },
            { 0x29, "'" },
            { 0x2A, "-" },
            { 0x2B, "©" },
            { 0x2C, "S" },
            { 0x2D, "·" },
            { 0x2E, "\"" },
            { 0x2F, "\"" },
        };

        private static readonly Dictionary<byte, string> Portuguese = new ()
        {
            { 0x20, "Á" },
            { 0x21, "ã" },
            { 0x22, "Í" },
            { 0x23, "Ì" },
            { 0x24, "ì" },
            { 0x25, "Ò" },
            { 0x26, "ò" },
            { 0x27, "Õ" },
            { 0x28, "õ" },
            { 0x29, "{" },
            { 0x2A, "}" },
            { 0x2B, "\\" },
            { 0x2C, "^" },
            { 0x2D, "_" },
            { 0x2E, "|" },
            { 0x2F, "~" },
        };

        private static readonly Dictionary<byte, string> French = new ()
        {
            { 0x30, "À" },
            { 0x31, "Â" },
            { 0x32, "Ç" },
            { 0x33, "È" },
            { 0x34, "Ê" },
            { 0x35, "Ë" },
            { 0x36, "ë" },
            { 0x37, "Î" },
            { 0x38, "Ï" },
            { 0x39, "ï" },
            { 0x3A, "Ô" },
            { 0x3B, "Ù" },
            { 0x3C, "ù" },
            { 0x3D, "Û" },
            { 0x3E, "«" },
            { 0x3F, "»" },
        };

        private static readonly Dictionary<byte, string> German = new ()
        {
            { 0x30, "Ä" },
            { 0x31, "ä" },
            { 0x32, "Ö" },
            { 0x33, "ö" },
            { 0x34, "ß" },
            { 0x35, "¥" },
            { 0x36, "¤" },
            { 0x37, "¦" },
            { 0x38, "Å" },
            { 0x39, "å" },
            { 0x3A, "Ø" },
            { 0x3B, "ø" },
            { 0x3C, "+" },
            { 0x3D, "+" },
            { 0x3E, "+" },
            { 0x3F, "+" },
        };

        private static readonly Dictionary<byte, int> Base40PreambleRows = new ()
        {
            { 0x11, 1 },
            { 0x19, 1 },
            { 0x12, 3 },
            { 0x1A, 3 },
            { 0x15, 5 },
            { 0x1D, 5 },
            { 0x16, 7 },
            { 0x1E, 7 },
            { 0x17, 9 },
            { 0x1F, 9 },
            { 0x10, 11 },
            { 0x18, 11 },
            { 0x13, 12 },
            { 0x1B, 12 },
            { 0x14, 14 },
            { 0x1C, 14 },
        };

        private static readonly Dictionary<byte, int> Base60PreambleRows = new ()
        {
            { 0x11, 2 },
            { 0x19, 2 },
            { 0x12, 4 },
            { 0x1A, 4 },
            { 0x15, 6 },
            { 0x1D, 6 },
            { 0x16, 8 },
            { 0x1E, 8 },
            { 0x17, 10 },
            { 0x1F, 10 },
            { 0x13, 13 },
            { 0x1B, 13 },
            { 0x14, 15 },
            { 0x1C, 15 },
        };

        private static readonly Dictionary<CaptionsStyle, int> PreambleStyleIndents = new ()
        {
            { CaptionsStyle.WhiteIndent0, 0 },
            { CaptionsStyle.WhiteIndent4, 4 },
            { CaptionsStyle.WhiteIndent8, 8 },
            { CaptionsStyle.WhiteIndent12, 12 },
            { CaptionsStyle.WhiteIndent16, 16 },
            { CaptionsStyle.WhiteIndent20, 20 },
            { CaptionsStyle.WhiteIndent24, 24 },
            { CaptionsStyle.WhiteIndent28, 28 },
            { CaptionsStyle.WhiteIndent0Underline, 0 },
            { CaptionsStyle.WhiteIndent4Underline, 4 },
            { CaptionsStyle.WhiteIndent8Underline, 8 },
            { CaptionsStyle.WhiteIndent12Underline, 12 },
            { CaptionsStyle.WhiteIndent16Underline, 16 },
            { CaptionsStyle.WhiteIndent20Underline, 20 },
            { CaptionsStyle.WhiteIndent24Underline, 24 },
            { CaptionsStyle.WhiteIndent28Underline, 28 },
        };

        private static readonly CaptionsStyle[] UnderlineCaptionStyles =
        {
            CaptionsStyle.BlueUnderline,
            CaptionsStyle.CyanUnderline,
            CaptionsStyle.GreenUnderline,
            CaptionsStyle.MagentaUnderline,
            CaptionsStyle.RedUnderline,
            CaptionsStyle.WhiteIndent0Underline,
            CaptionsStyle.WhiteIndent12Underline,
            CaptionsStyle.WhiteIndent16Underline,
            CaptionsStyle.WhiteIndent20Underline,
            CaptionsStyle.WhiteIndent24Underline,
            CaptionsStyle.WhiteIndent28Underline,
            CaptionsStyle.WhiteIndent4Underline,
            CaptionsStyle.WhiteIndent8Underline,
            CaptionsStyle.WhiteItalicsUnderline,
            CaptionsStyle.WhiteUnderline,
            CaptionsStyle.YellowUnderline,
        };

        private static readonly CaptionsStyle[] ItalicsCaptionStyles =
        {
            CaptionsStyle.WhiteItalics,
            CaptionsStyle.WhiteItalicsUnderline,
        };

        private readonly byte[] Data;

        /// <summary>
        /// Initialises a new instance of the <see cref="ClosedCaptionPacket"/> class.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="source">The source.</param>
        /// <param name="offset">The offset.</param>
        internal unsafe ClosedCaptionPacket(TimeSpan timestamp, byte* source, int offset)
            : this(timestamp, source[offset + 0], source[offset + 1], source[offset + 2])
        {
            // placeholder
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="ClosedCaptionPacket"/> class.
        /// </summary>
        /// <param name="timestamp">The timestamp.</param>
        /// <param name="header">The header.</param>
        /// <param name="d0">The d0.</param>
        /// <param name="d1">The d1.</param>
        internal ClosedCaptionPacket(TimeSpan timestamp, byte header, byte d0, byte d1)
        {
            this.Data = new[] { header, d0, d1 };

            this.D0 = DropParityBit(d0);
            this.D1 = DropParityBit(d1);

            this.IsControlPacket = this.D0 >= 0x10 && this.D0 <= 0x1F;
            this.FieldParity = GetHeaderFieldType(header);
            this.FieldChannel = 0;
            this.Timestamp = timestamp;
            try
            {
                if (HeaderHasMarkers(header) == false
                    || IsHeaderValidFlagSet(header) == false
                    || this.FieldParity == 0
                    || (this.D0 == 0x00 && this.D1 == 0x00))
                {
                    this.PacketType = CaptionsPacketType.NullPad;
                    return;
                }

                this.PacketType = CaptionsPacketType.Unrecognized;

                // XDS Parsing
                if ((this.D0 & 0x0F) == this.D0 && this.D0 != 0)
                {
                    this.PacketType = CaptionsPacketType.XdsClass;
                    this.XdsClass = (CaptionsXdsClass)this.D0;
                    return;
                }

                if ((this.D0 == 0x10 || this.D0 == 0x18) && (this.D1 >= 0x20 && this.D1 <= 0x2F))
                {
                    this.FieldChannel = this.D0 == 0x10 ? 1 : 2;
                    this.PacketType = CaptionsPacketType.Color;
                    this.Color = (CaptionsColor)this.D1;
                    return;
                }

                if ((this.D0 == 0x17 || this.D0 == 0x1F) && (this.D1 >= 0x2D && this.D1 <= 0x2F))
                {
                    this.FieldChannel = this.D0 == 0x17 ? 1 : 2;
                    this.PacketType = CaptionsPacketType.Color;
                    var colorValue = this.D1 << 16;
                    this.Color = (CaptionsColor)colorValue;

                    return;
                }

                if ((this.D0 == 0x17 || this.D0 == 0x1F) && (this.D1 >= 0x24 && this.D1 <= 0x2A))
                {
                    this.FieldChannel = this.D0 == 0x17 ? 1 : 2;
                    this.PacketType = CaptionsPacketType.PrivateCharset;
                    return;
                }

                // Mid-row Code Parsing
                if ((this.D0 == 0x11 || this.D0 == 0x19) && (this.D1 >= 0x20 && this.D1 <= 0x2F))
                {
                    this.PacketType = CaptionsPacketType.MidRow;
                    this.FieldChannel = this.D0 == 0x11 ? 1 : 2;
                    this.MidRowStyle = (CaptionsStyle)this.D1;
                    this.IsItalics = ItalicsCaptionStyles.Contains(this.MidRowStyle);
                    this.IsUnderlined = UnderlineCaptionStyles.Contains(this.MidRowStyle);
                    return;
                }

                // Screen command parsing
                if ((this.D0 == 0x14 || this.D0 == 0x1C) && (this.D1 >= 0x20 && this.D1 <= 0x2F))
                {
                    this.PacketType = CaptionsPacketType.Command;
                    this.Command = (CaptionsCommand)this.D1;
                    this.FieldChannel = (this.D0 == 0x14 || this.D0 == 0x1C) ? 1 : 2;
                    return;
                }

                // Tab command Parsing
                if ((this.D0 == 0x17 || this.D0 == 0x1F) && (this.D1 >= 0x21 && this.D1 <= 0x23))
                {
                    this.PacketType = CaptionsPacketType.Tabs;
                    this.Tabs = this.D1 & 0x03;
                    this.FieldChannel = this.D0 == 0x17 ? 1 : 2;
                    return;
                }

                // Preamble Parsing
                if ((this.D1 >= 0x40 && this.D1 <= 0x5F) || (this.D1 >= 0x60 && this.D1 <= 0x7F))
                {
                    // Row 11 is different -- check for it
                    if (this.D0 == 0x10 || this.D0 == 0x18)
                    {
                        this.PacketType = CaptionsPacketType.Preamble;
                        this.FieldChannel = this.D0 == 0x10 ? 1 : 2;
                        this.PreambleRow = 11;
                        this.PreambleStyle = (CaptionsStyle)(this.D1 - 0x20);
                        this.IsItalics = ItalicsCaptionStyles.Contains(this.PreambleStyle);
                        this.IsUnderlined = UnderlineCaptionStyles.Contains(this.PreambleStyle);
                        this.PreambleIndent = PreambleStyleIndents.ContainsKey(this.PreambleStyle) ?
                            PreambleStyleIndents[this.PreambleStyle] : 0;
                        return;
                    }

                    var wasSet = false;
                    if (this.D0 >= 0x11 && this.D0 <= 0x17)
                    {
                        this.PacketType = CaptionsPacketType.Preamble;
                        this.FieldChannel = 1;
                        wasSet = true;
                    }

                    if (this.D0 >= 0x19 && this.D0 <= 0x1F)
                    {
                        this.PacketType = CaptionsPacketType.Preamble;
                        this.FieldChannel = 2;
                        wasSet = true;
                    }

                    if (wasSet)
                    {
                        // Page 109 of CEA-608 Document
                        if (this.D1 >= 0x40 && this.D1 <= 0x5F)
                        {
                            this.PreambleRow = Base40PreambleRows.ContainsKey(this.D0) ? Base40PreambleRows[this.D0] : 11;
                            this.PreambleStyle = (CaptionsStyle)(this.D1 - 0x20);
                            this.IsItalics = ItalicsCaptionStyles.Contains(this.PreambleStyle);
                            this.IsUnderlined = UnderlineCaptionStyles.Contains(this.PreambleStyle);
                            this.PreambleIndent = PreambleStyleIndents.ContainsKey(this.PreambleStyle) ?
                                PreambleStyleIndents[this.PreambleStyle] : 0;
                        }
                        else
                        {
                            this.PreambleRow = Base60PreambleRows.ContainsKey(this.D0) ? Base60PreambleRows[this.D0] : 11;
                            this.PreambleStyle = (CaptionsStyle)(this.D1 - 0x40);
                            this.IsItalics = ItalicsCaptionStyles.Contains(this.PreambleStyle);
                            this.IsUnderlined = UnderlineCaptionStyles.Contains(this.PreambleStyle);
                            this.PreambleIndent = PreambleStyleIndents.ContainsKey(this.PreambleStyle) ?
                                PreambleStyleIndents[this.PreambleStyle] : 0;
                        }

                        return;
                    }
                }

                this.PacketType = CaptionsPacketType.Text;

                // Special North American character set
                if ((this.D0 == 0x11 || this.D0 == 0x19) && this.D1 >= 0x30 && this.D1 <= 0x3F)
                {
                    if (SpecialNorthAmerican.ContainsKey(this.D1))
                    {
                        this.FieldChannel = this.D0 == 0x11 ? 1 : 2;
                        this.Text = SpecialNorthAmerican[this.D1];
                        return;
                    }
                }

                if (this.D0 == 0x12 || this.D0 == 0x1A)
                {
                    if (Spanish.ContainsKey(this.D1))
                    {
                        this.FieldChannel = 1;
                        this.Text = Spanish[this.D1];
                        return;
                    }

                    if (French.ContainsKey(this.D1))
                    {
                        this.FieldChannel = 2;
                        this.Text = French[this.D1];
                        return;
                    }
                }

                if (this.D0 == 0x13 || this.D0 == 0x1B)
                {
                    if (Portuguese.ContainsKey(this.D1))
                    {
                        this.FieldChannel = 1;
                        this.Text = Portuguese[this.D1];
                        return;
                    }

                    if (German.ContainsKey(this.D1))
                    {
                        this.FieldChannel = 2;
                        this.Text = German[this.D1];
                        return;
                    }
                }

                // Basic North American character set (2 chars)
                if (this.D0 >= 0x20 && this.D0 <= 0x7F && (this.D1 == 0x00 || (this.D1 >= 0x20 && this.D1 <= 0x7F)))
                {
                    this.FieldChannel = 0;
                    this.Text = this.D1 == 0x00 ? $"{ToEia608Char(this.D0)}" : $"{ToEia608Char(this.D0)}{ToEia608Char(this.D1)}";
                    return;
                }

                this.PacketType = CaptionsPacketType.Unrecognized;
                this.FieldChannel = 0;
            }
            finally
            {
                this.Channel = this.FieldParity != 0 && this.FieldChannel != 0 ?
                    ComputeChannel(this.FieldParity, this.FieldChannel) : Constants.DefaultClosedCaptionsChannel;
            }
        }

        /// <summary>
        /// Gets the first of the two-byte packet data.
        /// </summary>
        public byte D0
        {
            get => this.Data[1];
            private set => this.Data[1] = value;
        }

        /// <summary>
        /// Gets the second of the two-byte packet data.
        /// </summary>
        public byte D1
        {
            get => this.Data[2];
            private set => this.Data[2] = value;
        }

        /// <summary>
        /// Gets the timestamp this packet applies to.
        /// </summary>
        public TimeSpan Timestamp { get; }

        /// <summary>
        /// Gets the NTSC field (1 or 2).
        /// 0 for unknown/null packet.
        /// </summary>
        public int FieldParity { get; }

        /// <summary>
        /// Gets the channel. 0 for use previous packet, 1 or 2 for specific channel.
        /// 0 just means to use what a prior packet had specified.
        /// </summary>
        public int FieldChannel { get; }

        /// <summary>
        /// Gets the channel CC1, CC2, CC3, or CC4.
        /// Returns None when not yet computed.
        /// </summary>
        public CaptionsChannel Channel { get; internal set; }

        /// <summary>
        /// Gets the type of the packet.
        /// </summary>
        public CaptionsPacketType PacketType { get; }

        /// <summary>
        /// Gets the number of tabs, if the packet type is of Tabs.
        /// </summary>
        public int Tabs { get; }

        /// <summary>
        /// Gets the Misc Command, if the packet type is of Command.
        /// </summary>
        public CaptionsCommand Command { get; }

        /// <summary>
        /// Gets the Color, if the packet type is of Color.
        /// </summary>
        public CaptionsColor Color { get; }

        /// <summary>
        /// Gets the Style, if the packet type is of Mid Row Style.
        /// </summary>
        public CaptionsStyle MidRowStyle { get; }

        /// <summary>
        /// Gets the XDS Class, if the packet type is of XDS.
        /// </summary>
        public CaptionsXdsClass XdsClass { get; }

        /// <summary>
        /// Gets the Preamble Row Number (1 through 15), if the packet type is of Preamble.
        /// </summary>
        public int PreambleRow { get; }

        /// <summary>
        /// Gets the Style, if the packet type is of Preamble.
        /// </summary>
        public CaptionsStyle PreambleStyle { get; } = CaptionsStyle.None;

        /// <summary>
        /// Gets the Indent Style, if the packet type is of Preamble.
        /// </summary>
        public int PreambleIndent { get; }

        /// <summary>
        /// Gets the text, if the packet type is of text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets a value indicating whether this is a control packet.
        /// </summary>
        public bool IsControlPacket { get; }

        /// <summary>
        /// Gets a value indicating whether the current and following
        /// caption text packets are underlined; only valid for preamble or mid-row packets.
        /// </summary>
        public bool IsUnderlined { get; }

        /// <summary>
        /// Gets a value indicating whether the current and following
        /// caption text packets are italicized; only valid for preamble or mid-row packets.
        /// </summary>
        public bool IsItalics { get; }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator ==(ClosedCaptionPacket left, ClosedCaptionPacket right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator !=(ClosedCaptionPacket left, ClosedCaptionPacket right) =>
            !(left == right);

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <(ClosedCaptionPacket left, ClosedCaptionPacket right) =>
            left == null ? right != null : left.CompareTo(right) < 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator <=(ClosedCaptionPacket left, ClosedCaptionPacket right) =>
            left == null || left.CompareTo(right) <= 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >(ClosedCaptionPacket left, ClosedCaptionPacket right) =>
            left != null && left.CompareTo(right) > 0;

        /// <summary>
        /// Implements the operator.
        /// </summary>
        /// <param name="left">The left-hand side operand.</param>
        /// <param name="right">The right-hand side operand.</param>
        /// <returns>The result of the operation.</returns>
        public static bool operator >=(ClosedCaptionPacket left, ClosedCaptionPacket right) =>
            left == null ? right == null : left.CompareTo(right) >= 0;

        /// <summary>
        /// Computes the CC channel.
        /// </summary>
        /// <param name="fieldParity">The field parity.</param>
        /// <param name="fieldChannel">The field channel.</param>
        /// <returns>The CC channel according to the parity and channel.</returns>
        public static CaptionsChannel ComputeChannel(int fieldParity, int fieldChannel)
        {
            // packets with 0 field parity are null or unknown
            if (fieldParity <= 0)
            {
                return Constants.DefaultClosedCaptionsChannel;
            }

            var parity = fieldParity.Clamp(1, 2);
            var channel = fieldChannel.Clamp(1, 2);

            if (parity == 1)
            {
                return channel == 1 ? CaptionsChannel.CC1 : CaptionsChannel.CC2;
            }

            return channel == 1 ? CaptionsChannel.CC3 : CaptionsChannel.CC4;
        }

        /// <summary>
        /// Determines whether a previous packet is a repeated control code.
        /// This is according to CEA-608 Section D.2 Transmission of Control Code Pairs.
        /// </summary>
        /// <param name="previousPacket">The previous packet.</param>
        /// <returns>
        ///   <c>true</c> it is a repeated control code packet.
        /// </returns>
        public bool IsRepeatedControlCode(ClosedCaptionPacket previousPacket)
        {
            return this.IsControlPacket && previousPacket != null && previousPacket.D0 == this.D0 && previousPacket.D1 == this.D1;
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            var ts = $"{this.Timestamp.TotalSeconds:0.0000}";
            var channel = this.Channel == Constants.DefaultClosedCaptionsChannel ?
                ComputeChannel(this.FieldParity, this.FieldChannel) + "*" : this.Channel + " ";
            var prefixData = $"{ts} | {channel} | P: {this.FieldParity} D: {this.FieldChannel} | {this.D0:x2}h {this.D1:x2}h |";
            string output = this.PacketType switch
            {
                CaptionsPacketType.PrivateCharset => $"{prefixData} CHARSET   | SELECT 0x{this.D1:x2}",
                CaptionsPacketType.Color => $"{prefixData} COLOR SET | {nameof(this.Color)}: {this.Color}",
                CaptionsPacketType.Command => $"{prefixData} MISC CTRL | {nameof(this.Command)}: {this.Command}",
                CaptionsPacketType.MidRow => $"{prefixData} MID-ROW S | {nameof(this.MidRowStyle)}: {this.MidRowStyle}",
                CaptionsPacketType.NullPad => $"{prefixData} NULL  PAD | (NULL)",
                CaptionsPacketType.Preamble => $"{prefixData} PREAMBLE  | Row: {this.PreambleRow}, Style: {this.PreambleStyle}",
                CaptionsPacketType.Tabs => $"{prefixData} TAB SPACE | {nameof(this.Tabs)}: {this.Tabs}",
                CaptionsPacketType.Text => $"{prefixData} TEXT DATA | '{this.Text}'",
                CaptionsPacketType.XdsClass => $"{prefixData} XDS DATA  | {nameof(this.XdsClass)}: {this.XdsClass}",
                _ => $"{prefixData} INVALID   | N/A",
            };
            return output;
        }

        /// <inheritdoc />
        public int CompareTo(ClosedCaptionPacket other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            return this.Timestamp.Ticks.CompareTo(other.Timestamp.Ticks);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is ClosedCaptionPacket other)
            {
                return ReferenceEquals(this, other);
            }

            return false;
        }

        /// <inheritdoc />
        public bool Equals(ClosedCaptionPacket other) =>
            ReferenceEquals(this, other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return
                this.Timestamp.GetHashCode() ^
                this.Data.GetHashCode();
        }

        /// <summary>
        /// Checks that the header byte starts with 11111b (5 ones binary).
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>If header has markers.</returns>
        private static bool HeaderHasMarkers(byte data)
        {
            return (data & 0xF8) == 0xF8;
        }

        /// <summary>
        /// Determines whether the valid flag of the header byte is set.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>
        ///   <c>true</c> if [is header valid flag set] [the specified data]; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsHeaderValidFlagSet(byte data) => (data & 0x04) == 0x04;

        /// <summary>
        /// Gets the NTSC field type (1 or 2).
        /// Returns 0 for unknown.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>The field type.</returns>
        private static int GetHeaderFieldType(byte data)
        {
            if ((data & 0x03) == 2)
            {
                return 0;
            }

            return (data & 0x03) == 0 ? 1 : 2;
        }

        /// <summary>
        /// Drops the parity bit from the data byte.
        /// </summary>
        /// <param name="input">The input.</param>
        /// <returns>The byte without a parity bit.</returns>
        private static byte DropParityBit(byte input)
        {
            return (byte)(input & 0x7F);
        }

        /// <summary>
        /// Converts an ASCII character code to an EIA-608 char (in Unicode).
        /// </summary>
        /// <param name="input">The input.</param>
        /// <returns>The charset char.</returns>
        private static char ToEia608Char(byte input)
        {
            // see: Annex A Character Set Differences, and Table 68
            if (input == 0x2A)
            {
                return 'á';
            }

            if (input == 0x5C)
            {
                return 'é';
            }

            if (input == 0x5E)
            {
                return 'í';
            }

            if (input == 0x5F)
            {
                return 'ó';
            }

            if (input == 0x60)
            {
                return 'ú';
            }

            if (input == 0x7B)
            {
                return 'ç';
            }

            if (input == 0x7C)
            {
                return '÷';
            }

            if (input == 0x7D)
            {
                return 'Ñ';
            }

            if (input == 0x7E)
            {
                return 'ñ';
            }

            if (input == 0x7F)
            {
                return '█';
            }

            return (char)input;
        }
    }
}
