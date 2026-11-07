using Tamarin.Core.Db;
using Tamarin.Core.Notes;
using Xunit;

namespace Tamarin.Core.Tests;

public class DatabaseTests : IDisposable
{
    private readonly TestDb _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Open_creates_the_schema_at_version_1()
    {
        Assert.Equal(1, _t.Db.UserVersion);
        foreach (var table in new[] { "notes", "settings", "attachments", "source_apps", "notes_fts" })
        {
            var found = _t.Db.Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE name = '{table}';");
            Assert.Equal(1L, Convert.ToInt64(found));
        }
    }

    [Fact]
    public void Open_applies_the_connection_pragmas()
    {
        Assert.Equal(1L, Convert.ToInt64(_t.Db.Scalar("PRAGMA foreign_keys;")));
        Assert.Equal(1L, Convert.ToInt64(_t.Db.Scalar("PRAGMA secure_delete;")));
        Assert.Equal("wal", Convert.ToString(_t.Db.Scalar("PRAGMA journal_mode;")));
    }

    [Fact]
    public void Reopening_keeps_data_and_does_not_run_the_migration_again()
    {
        var note = _t.Notes.Create("keep me");
        _t.Settings.TextSize = 20;

        _t.Reopen();

        Assert.Equal("keep me", _t.Notes.Get(note.Id)!.Body);
        Assert.Equal(20, _t.Settings.TextSize); // the migration would have reset this to 14
        Assert.Equal(1, _t.Db.UserVersion);
    }

    [Fact]
    public void A_database_from_a_newer_app_is_refused()
    {
        _t.Db.Exec("PRAGMA user_version = 99;");
        _t.Db.Dispose();

        var ex = Assert.Throws<DatabaseTooNewException>(() => Database.Open(_t.DbPath));
        Assert.Equal(99, ex.FileVersion);
    }

    [Fact]
    public void QuickCheck_reports_ok_on_a_healthy_file()
    {
        _t.Notes.Create("hello");
        Assert.Equal("ok", _t.Db.QuickCheck());
    }

    [Fact]
    public void The_database_rejects_a_body_over_the_size_cap()
    {
        var tooLong = new string('x', NoteRepository.MaxBodyLength + 1);
        Assert.ThrowsAny<Exception>(() => _t.Notes.Create(tooLong));
        Assert.Throws<ArgumentException>(() => _t.Notes.Save(_t.Notes.Create("a").Id, tooLong));
    }

    [Fact]
    public void Using_a_disposed_database_throws()
    {
        _t.Db.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _t.Notes.Count());
    }

    [Fact]
    public void Timestamps_have_millisecond_precision_so_notes_made_together_keep_their_order()
    {
        var a = _t.Notes.Create("a");
        Thread.Sleep(5);
        var b = _t.Notes.Create("b");
        Assert.True(b.CreatedAt > a.CreatedAt);
        Assert.Equal(b.Id, _t.Notes.Latest()!.Id);
        // Close to the real clock, in milliseconds (not seconds).
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(Math.Abs(now - b.CreatedAt) < 5000);
    }
}
