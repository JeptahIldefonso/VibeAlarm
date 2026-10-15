using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    public sealed class AlbumArtServiceTests : IDisposable
    {
        private readonly string testDir;

        public AlbumArtServiceTests()
        {
            testDir = Path.Combine(Path.GetTempPath(), "VibeAlarm_ArtTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void EnsureTrackArt_NonExistentFile_MarksCheckedWithNullKey()
        {
            using var service = new AlbumArtService();
            var track = new MusicTrack
            {
                Id = "test-1",
                Path = Path.Combine(testDir, "nonexistent.mp3"),
                Title = "Nonexistent",
                ArtChecked = false,
                ArtKey = null
            };

            var key = service.EnsureTrackArt(track);

            Assert.Null(key);
            Assert.True(track.ArtChecked);
            Assert.Null(track.ArtKey);
        }

        [Fact]
        public void ExtractArtForTrack_FolderCoverJpg_ExtractsHashAndCreates160x160Thumbnail()
        {
            using var service = new AlbumArtService();

            // Create a fake folder with audio file and cover.jpg
            string folder = Path.Combine(testDir, "AlbumA");
            Directory.CreateDirectory(folder);

            string audioPath = Path.Combine(folder, "song1.mp3");
            File.WriteAllText(audioPath, "fake mp3 content");

            string coverPath = Path.Combine(folder, "cover.jpg");
            using (var bmp = new Bitmap(300, 200))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.CornflowerBlue);
                bmp.Save(coverPath, ImageFormat.Jpeg);
            }

            var track = new MusicTrack
            {
                Id = "track-a",
                Path = audioPath,
                Title = "Song 1",
                ArtChecked = false
            };

            var artKey = service.ExtractArtForTrack(track);

            Assert.NotNull(artKey);
            Assert.True(track.ArtChecked);
            Assert.Equal(artKey, track.ArtKey);

            // Thumbnail must exist in AppPaths.ArtCacheDir
            string thumbFile = Path.Combine(AppPaths.ArtCacheDir, artKey + ".jpg");
            Assert.True(File.Exists(thumbFile));

            // Verify dimensions are 160x160
            using (var thumbBmp = Image.FromFile(thumbFile))
            {
                Assert.Equal(160, thumbBmp.Width);
                Assert.Equal(160, thumbBmp.Height);
            }
        }

        [Fact]
        public void ExtractArtForTrack_Deduplication_TwoTracksSameFolder_ReuseSameArtKey()
        {
            using var service = new AlbumArtService();

            string folder = Path.Combine(testDir, "AlbumB");
            Directory.CreateDirectory(folder);

            string song1 = Path.Combine(folder, "track1.mp3");
            string song2 = Path.Combine(folder, "track2.mp3");
            File.WriteAllText(song1, "content 1");
            File.WriteAllText(song2, "content 2");

            string folderImage = Path.Combine(folder, "folder.png");
            using (var bmp = new Bitmap(100, 100))
            {
                bmp.Save(folderImage, ImageFormat.Png);
            }

            var t1 = new MusicTrack { Id = "t1", Path = song1, Title = "Track 1" };
            var t2 = new MusicTrack { Id = "t2", Path = song2, Title = "Track 2" };

            var key1 = service.ExtractArtForTrack(t1);
            var key2 = service.ExtractArtForTrack(t2);

            Assert.NotNull(key1);
            Assert.Equal(key1, key2);
            Assert.Equal(t1.ArtKey, t2.ArtKey);
        }

        [Fact]
        public void CleanupOrphanedThumbnails_DeletesUnreferencedFiles()
        {
            using var service = new AlbumArtService();

            string orphanFile = Path.Combine(AppPaths.ArtCacheDir, "fakeorphanart12345.jpg");
            File.WriteAllText(orphanFile, "dummy");

            var keptTrack = new MusicTrack
            {
                Id = "t-kept",
                ArtKey = "keepme123"
            };

            service.CleanupOrphanedThumbnails(new[] { keptTrack });

            Assert.False(File.Exists(orphanFile));
        }
    }
}
