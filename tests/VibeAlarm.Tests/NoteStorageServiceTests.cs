using System;
using System.IO;
using System.Linq;
using System.Threading;
using VibeAlarm.Models;
using VibeAlarm.Services;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Note persistence: one plain .txt per note ({Id}.txt), first line the title,
    /// the remainder the content, last-write time the UpdatedAt. Driven against
    /// temp directories via the dir-injected overloads — the real calls target
    /// AppPaths.NotesDir with the same code.
    /// </summary>
    public class NoteStorageServiceTests : IDisposable
    {
        private readonly string tempRoot;

        public NoteStorageServiceTests()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "vibealarm-notes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best-effort cleanup; temp dirs are disposable.
            }
        }

        [Fact]
        public void Save_writes_one_txt_file_per_note_with_title_as_the_first_line()
        {
            var note = new NoteItem { Id = "abc", Title = "Groceries", Content = "milk\neggs" };

            NoteStorageService.Save(note, tempRoot);

            string path = Path.Combine(tempRoot, "abc.txt");
            Assert.True(File.Exists(path));
            Assert.Equal("Groceries\nmilk\neggs", File.ReadAllText(path));
        }

        [Fact]
        public void Load_round_trips_title_and_content()
        {
            NoteStorageService.Save(new NoteItem { Id = "a", Title = "One", Content = "first" }, tempRoot);
            NoteStorageService.Save(new NoteItem { Id = "b", Title = "Two", Content = "second\nline" }, tempRoot);

            var notes = NoteStorageService.Load(tempRoot);

            Assert.Equal(2, notes.Count);
            NoteItem one = notes.Single(n => n.Id == "a");
            Assert.Equal("One", one.Title);
            Assert.Equal("first", one.Content);
            NoteItem two = notes.Single(n => n.Id == "b");
            Assert.Equal("Two", two.Title);
            Assert.Equal("second\nline", two.Content);
        }

        [Fact]
        public void A_title_only_note_has_no_content()
        {
            NoteStorageService.Save(new NoteItem { Id = "a", Title = "Just a title" }, tempRoot);

            NoteItem note = NoteStorageService.Load(tempRoot).Single();

            Assert.Equal("Just a title", note.Title);
            Assert.Equal(string.Empty, note.Content);
        }

        [Fact]
        public void A_title_with_a_newline_is_flattened_so_the_first_line_stays_the_title()
        {
            // A pasted multi-line title must not silently corrupt the file format.
            NoteStorageService.Save(new NoteItem { Id = "a", Title = "Two\r\nlines", Content = "body" }, tempRoot);

            NoteItem note = NoteStorageService.Load(tempRoot).Single();

            Assert.Equal("Two lines", note.Title);
            Assert.Equal("body", note.Content);
        }

        [Fact]
        public void Load_orders_most_recently_updated_first()
        {
            NoteStorageService.Save(new NoteItem { Id = "older", Title = "Old" }, tempRoot);
            Thread.Sleep(20); // last-write times share a coarse filesystem granularity
            NoteStorageService.Save(new NoteItem { Id = "newer", Title = "New" }, tempRoot);

            var notes = NoteStorageService.Load(tempRoot);

            Assert.Equal(new[] { "newer", "older" }, notes.Select(n => n.Id).ToArray());
        }

        [Fact]
        public void UpdatedAt_comes_from_the_file_not_the_model()
        {
            // The disk is the source of truth: a wrong in-memory UpdatedAt is
            // corrected by the file's last-write time on load.
            NoteStorageService.Save(new NoteItem { Id = "a", Title = "T", UpdatedAt = DateTime.MinValue }, tempRoot);

            NoteItem note = NoteStorageService.Load(tempRoot).Single();

            Assert.True(note.UpdatedAt > DateTime.MinValue);
        }

        [Fact]
        public void Delete_removes_only_that_notes_file()
        {
            NoteStorageService.Save(new NoteItem { Id = "a", Title = "A" }, tempRoot);
            NoteStorageService.Save(new NoteItem { Id = "b", Title = "B" }, tempRoot);

            NoteStorageService.Delete("a", tempRoot);

            Assert.False(File.Exists(Path.Combine(tempRoot, "a.txt")));
            Assert.Single(NoteStorageService.Load(tempRoot));
        }

        [Fact]
        public void Delete_of_a_missing_note_is_not_an_error()
        {
            NoteStorageService.Delete("nope", tempRoot);
        }

        [Fact]
        public void Load_of_a_missing_directory_returns_empty()
        {
            Assert.Empty(NoteStorageService.Load(Path.Combine(tempRoot, "does-not-exist")));
        }

        [Fact]
        public void Non_txt_files_in_the_directory_are_ignored()
        {
            NoteStorageService.Save(new NoteItem { Id = "a", Title = "A" }, tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "stray.json"), "{}");

            Assert.Single(NoteStorageService.Load(tempRoot));
        }
    }
}
