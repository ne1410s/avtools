using AV.Core;
using AV.Core.Container;
using AV.Extensions;
using AV.Source;
using FFmpeg.AutoGen;
using Xunit;

namespace AV.UnitTests
{
    public class ExtensionsTests
    {
        private static readonly byte[] TestKey = new byte[] { 3, 44, 201, 0, 6 };
    
        public ExtensionsTests()
        {
            Library.FFmpegDirectory = "ffmpeg";
            Library.FFmpegLoadModeFlags = FFmpegLoadMode.FullFeatures;
            Library.LoadFFmpeg();
        }

        [Theory]
        //[InlineData(@"C:\temp\vid-test\1.3gp")]
        [InlineData(@"C:\temp\vid-test\1.avi")]
        //[InlineData(@"C:\temp\vid-test\1.flv")]
        //[InlineData(@"C:\temp\vid-test\1.m4v")]
        //[InlineData(@"C:\temp\vid-test\1.mkv")]
        //[InlineData(@"C:\temp\vid-test\1.mov")]
        //[InlineData(@"C:\temp\vid-test\1.mp4")]
        //[InlineData(@"C:\temp\vid-test\1.mpeg")]
        //[InlineData(@"C:\temp\vid-test\1.mpg")]
        //[InlineData(@"C:\temp\vid-test\1.mts")]
        //[InlineData(@"C:\temp\vid-test\1.vob")]
        //[InlineData(@"C:\temp\vid-test\1.webm")]
        //[InlineData(@"C:\temp\vid-test\1.wmv")]
        public void FileSource_LoadsMediaInfo(string path)
        {
            // Arrange
            using var source = new FileSource(path);
            using var container = new MediaContainer(source);
            container.Initialize();
            container.Open();

            // Act
            container.Snap((data, n) =>
            {
                data.Image.Save($"c:\\temp\\vid-test-out\\snap_{n}.jpg");
            });

            // Assert
        }
    }
}
