// <copyright file="Aspects.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Diagnostics
{
    /// <summary>
    /// Provides constants for logging aspect identifiers.
    /// </summary>
    public static class Aspects
    {
        /// <summary>
        /// Gets none.
        /// </summary>
        public static string None => "Log.Text";

        /// <summary>
        /// Gets ffmpeg log.
        /// </summary>
        public static string FFmpegLog => "FFmpeg.Log";

        /// <summary>
        /// Gets engine command.
        /// </summary>
        public static string EngineCommand => "Engine.Commands";

        /// <summary>
        /// Gets reading worker.
        /// </summary>
        public static string ReadingWorker => "Engine.Reading";

        /// <summary>
        /// Gets decoder worker.
        /// </summary>
        public static string DecodingWorker => "Engine.Decoding";

        /// <summary>
        /// Gets rendering worker.
        /// </summary>
        public static string RenderingWorker => "Engine.Rendering";

        /// <summary>
        /// Gets connector.
        /// </summary>
        public static string Connector => "Engine.Connector";

        /// <summary>
        /// Gets container.
        /// </summary>
        public static string Container => "Container";

        /// <summary>
        /// Gets timing.
        /// </summary>
        public static string Timing => "Timing";

        /// <summary>
        /// Gets component.
        /// </summary>
        public static string Component => "Container.Component";

        /// <summary>
        /// Gets reference counter.
        /// </summary>
        public static string ReferenceCounter => "ReferenceCounter";

        /// <summary>
        /// Gets video renderer.
        /// </summary>
        public static string VideoRenderer => "Element.Video";

        /// <summary>
        /// Gets audio renderer.
        /// </summary>
        public static string AudioRenderer => "Element.Audio";

        /// <summary>
        /// Gets subtitle.
        /// </summary>
        public static string SubtitleRenderer => "Element.Subtitle";

        /// <summary>
        /// Gets events.
        /// </summary>
        public static string Events => "Element.Events";
    }
}
