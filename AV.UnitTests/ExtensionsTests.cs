using System.IO;
using AV.Core;
using AV.Extensions;
using FullStack.Crypto;
using Xunit;

namespace AV.UnitTests
{
    public class ExtensionsTests
    {
        private static readonly byte[] TestKey = new byte[] { 3, 44, 201, 0, 6 };
    
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
        [InlineData(@"C:\temp\vid-test\sec\2457c4e00862f316c4949d4bfb33aff838fc751f054df4422a100b234b91ffd4.wmv")]
        [InlineData(@"C:\temp\vid-test\sec\fac1842340659370e81d7e510373636a962580b44f9299ac02edfcfa193a31e5.avi")]
        public void FileSource_AutoSnaps(string path)
        {
            using var session = AVExtensions.OpenSession(path, TestKey);
            var name = new FileInfo(session.SessionInfo.StreamUri).Name;
            session.AutoSnap((frame, _) =>
            {
                var thumb = frame.Image.Resize(200);
                frame.Image.Dispose();
                thumb.Save($"c:\\temp\\vid-test-out\\{name}_{frame.FrameNumber}.jpg");
                thumb.Dispose();
            });
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
    }
}
