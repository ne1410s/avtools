﻿// <copyright file="Aspects.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Diagnostics
{
    /// <summary>
    /// Provides constants for logging aspect identifiers.
    /// </summary>
    public static class Aspects
    {
        public static string None => "Log.Text";

        public static string FFmpegLog => "FFmpeg.Log";

        public static string EngineCommand => "Engine.Commands";

        public static string ReadingWorker => "Engine.Reading";

        public static string DecodingWorker => "Engine.Decoding";

        public static string RenderingWorker => "Engine.Rendering";

        public static string Connector => "Engine.Connector";

        public static string Container => "Container";

        public static string Timing => "Timing";

        public static string Component => "Container.Component";

        public static string ReferenceCounter => "ReferenceCounter";

        public static string VideoRenderer => "Element.Video";

        public static string AudioRenderer => "Element.Audio";

        public static string SubtitleRenderer => "Element.Subtitle";

        public static string Events => "Element.Events";
    }
}
