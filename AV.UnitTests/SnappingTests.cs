using System.Collections.Generic;
using System.IO;
using AV.Core;
using AV.Core.Container;
using AV.Core.Source;
using AV.Extensions;
using FFmpeg.AutoGen;
using FullStack.Crypto;
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
        [InlineData(@"C:\temp\vid-test\p.wmv")]
        public void FileSource_AutoSnaps(string path)
        {
            // Arrange
            var name = new FileInfo(path).Name;
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

        [Theory]
        [InlineData(@"C:\temp\vid-test\sec\1.avi")]
        public void Encrypt(string path)
        {
            if (File.Exists(path))
            {
                new FileInfo(path).Encrypt(TestKey);
            }
        }

        [Theory]
        [InlineData(@"C:\temp\vid-test\sec\2457c4e00862f316c4949d4bfb33aff838fc751f054df4422a100b234b91ffd4.wmv")]
        [InlineData(@"C:\temp\vid-test\sec\fac1842340659370e81d7e510373636a962580b44f9299ac02edfcfa193a31e5.avi")]
        public void SecureSource_AutoSnaps(string path)
        {
            // Arrange
            var name = new FileInfo(path).Name;
            using var source = new SecureFileSource(path, TestKey);

            // Act
            var frameNums = new List<long>();
            source.AutoSnap((data, n) =>
            {
                frameNums.Add(data.FrameNumber);
                data.Image.Save($"c:\\temp\\vid-test-out\\sec\\snap_{name}_{n}_of_24_FRAME_NO-{data.FrameNumber}.jpg");
            });

            // Assert
            var x = string.Join(',', frameNums);
        }

        [Theory]
        [InlineData(@"C:\temp\vid-test\sec\2457c4e00862f316c4949d4bfb33aff838fc751f054df4422a100b234b91ffd4.wmv")]
        public void UrlOnly_AutoSnaps(string path)
        {
            // Arrange
            using var source = new SecureFileSource(path, TestKey);
            using var container = new MediaContainer(source);
            //using var container = new MediaContainer(path, null);

            // Act
            container.Initialize();
            container.Open();

            var block = (MediaBlock)null;
            var info = container.TakeSnap(container.Components.Video.Duration / 2, ref block);
            info.Image.Save("C:\\temp\\wootie.jpg");
        }
    }
}
