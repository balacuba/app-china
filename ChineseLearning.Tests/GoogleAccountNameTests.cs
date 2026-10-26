using ChineseLearningServer;

namespace ChineseLearning.Tests;

// Accounts made by signing in with Google are named after the Google account.
public sealed class GoogleAccountNameTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chinese-names-{Guid.NewGuid():N}.db");
    private readonly Database _db;

    public GoogleAccountNameTests()
    {
        _db = new Database(_databasePath);
        _db.Initialize();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TheAccountTakesTheNameOnGoogle()
    {
        var user = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-1", "lai.hoai@gmail.com", "  Lại   Hoài "));

        Assert.Equal("Lại Hoài", user.UserName);
    }

    [Fact]
    public void TwoPeopleWithTheSameNameGetDifferentAccountNames()
    {
        var first = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-1", "a@gmail.com", "Minh Anh"));
        var second = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-2", "b@gmail.com", "Minh Anh"));
        var firstAgain = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-1", "a@gmail.com", "Minh Anh"));

        Assert.Equal("Minh Anh", first.UserName);
        Assert.Equal("Minh Anh 2", second.UserName);
        Assert.Equal(first, firstAgain);
    }

    [Fact]
    public void AnAccountNamedAfterItsGmailTakesTheGoogleNameNextTime()
    {
        var before = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-1", "lai.hoai@gmail.com"));
        var after = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-1", "lai.hoai@gmail.com", "Lại Hoài"));

        Assert.Equal("lai.hoai@gmail.com", before.UserName);
        Assert.Equal((before.Id, "Lại Hoài"), after);
    }

    [Fact]
    public void APasswordAccountWithTheSameNameIsNotTakenOver()
    {
        var own = _db.CreateUser("Hoai", "mat-khau-123");
        var google = _db.FindOrCreateGoogleUser(new GoogleIdentity("sub-1", "lai.hoai@gmail.com", "Hoai"));

        Assert.NotEqual(own, google.Id);
        Assert.Equal("Hoai 2", google.UserName);
    }
}
