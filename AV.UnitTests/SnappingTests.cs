﻿using System.Collections.Generic;
using AV.Core;
using AV.Extensions;
using AV.Source;
using FFmpeg.AutoGen;
using Xunit;

namespace AV.UnitTests
{
    public class SnappingTests
    {
        private static readonly byte[] TestKey = new byte[] { 3, 44, 201, 0, 6 };
    
        public SnappingTests()
        {
            Library.FFmpegDirectory = "ffmpeg";
            Library.FFmpegLoadModeFlags = FFmpegLoadMode.FullFeatures;
            Library.LoadFFmpeg();
        }

        [Theory]
        [InlineData(@"C:\temp\vid-test\1.3gp")]
        [InlineData(@"C:\temp\vid-test\1.avi")]
        [InlineData(@"C:\temp\vid-test\1.flv")]
        [InlineData(@"C:\temp\vid-test\1.m4v")]
        [InlineData(@"C:\temp\vid-test\1.mkv")]
        [InlineData(@"C:\temp\vid-test\1.mov")]
        [InlineData(@"C:\temp\vid-test\1.mp4")]
        [InlineData(@"C:\temp\vid-test\1.mpeg")]
        [InlineData(@"C:\temp\vid-test\1.mpg")]
        [InlineData(@"C:\temp\vid-test\1.mts")]
        [InlineData(@"C:\temp\vid-test\1.vob")]
        [InlineData(@"C:\temp\vid-test\1.webm")]
        [InlineData(@"C:\temp\vid-test\1.wmv")]
        [InlineData(@"C:\temp\vid-test\4k.mp4")]
        [InlineData(@"C:\temp\vid-test\xl.mkv")]
        [InlineData(@"C:\temp\vid-test\xl.webm")]
        [InlineData(@"C:\temp\vid-test\xl.wmv")]
        public void FileSource_AutoSnaps(string path)
        {
            // Arrange
            var name = new System.IO.FileInfo(path).Name;
            using var source = new FileSource(path);

            // Act
            var frameNums = new List<long>();
            source.AutoSnap((data, n) =>
            {
                frameNums.Add(data.FrameNumber);
                data.Image.Save($"c:\\temp\\vid-test-out\\snap_{name}_{n}_of_24_FRAME_NO-{data.FrameNumber}.jpg");
            });

            // Assert
            var x = string.Join(',', frameNums);
        }
    }
}
