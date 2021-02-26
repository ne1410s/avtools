using AV.Core;
using AV.Core.Container;
using AV.Source;
using FFmpeg.AutoGen;
using Xunit;

namespace AV.UnitTests
{
    public class MediaLoadTests
    {
        private static readonly byte[] TestKey = new byte[] { 3, 44, 201, 0, 6 };

        public MediaLoadTests()
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
        public void FileSource_LoadsMediaInfo(string path)
        {
            // Arrange
            using var source = new FileSource(path);
            using var container = new MediaContainer(source);

            // Act
            container.Initialize();
            
            // Assert
            Assert.True(container.IsInitialized);
            Assert.False(container.IsLiveStream);
            Assert.False(container.IsNetworkStream);
            Assert.NotNull(container?.MediaInfo?.Format);
        }

        [Theory]
        [InlineData(@"C:\temp\vid-test\sec\f201f11ab2a27b55897b519de795b058d74acf9d1c9d3665921f2ff4618f31e0.mp4")]
        public void SecureFileSource_LoadsMediaInfo(string path)
        {
            // Arrange
            using var source = new SecureFileSource(path, TestKey);
            using var container = new MediaContainer(source);

            // Act
            container.Initialize();

            // Assert
            Assert.True(container.IsInitialized);
            Assert.False(container.IsLiveStream);
            Assert.True(container.IsNetworkStream);
            Assert.NotNull(container?.MediaInfo?.Format);
        }
    }
}
