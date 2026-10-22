using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChineseLearning.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ChineseLearning.Tests;

// The server running in memory against a throw-away database.
public sealed class ServerApiTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chinese-test-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public ServerApiTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Database", _databasePath));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            File.Delete(file);
        }
    }

    private async Task<HttpClient> SignedInClient(string userName = "hoai")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/register", new { userName, password = "secret1" });
        response.EnsureSuccessStatusCode();
        var signedIn = await response.Content.ReadFromJsonAsync<SignedInReply>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn!.Token);
        return client;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, int version, ProgressDocument document)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/progress") { Content = JsonContent.Create(document) };
        request.Headers.TryAddWithoutValidation("If-Match", version.ToString());
        return client.SendAsync(request);
    }

    [Fact]
    public async Task ANewAccountStartsWithEmptyProgressAtVersionZero()
    {
        var client = await SignedInClient();

        var progress = await client.GetFromJsonAsync<ProgressEnvelope>("/api/progress");

        Assert.Equal(0, progress!.Version);
        Assert.Empty(progress.Progress.SelectedWords);
    }

    [Fact]
    public async Task SavedProgressComesBackOnAnotherSignIn()
    {
        var client = await SignedInClient();
        var saved = await Put(client, 0, new ProgressDocument { SelectedWords = ["你好"], CorrectAnswers = 3 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var other = _factory.CreateClient();
        var login = await other.PostAsJsonAsync("/api/login", new { userName = "HOAI", password = "secret1" });
        var token = (await login.Content.ReadFromJsonAsync<SignedInReply>())!.Token;
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var progress = await other.GetFromJsonAsync<ProgressEnvelope>("/api/progress");

        Assert.Equal(1, progress!.Version);
        Assert.Equal(["你好"], progress.Progress.SelectedWords);
        Assert.Equal(3, progress.Progress.CorrectAnswers);
    }

    [Fact]
    public async Task SavingOverAnOldVersionIsRefusedWithTheLatest()
    {
        var client = await SignedInClient();
        await Put(client, 0, new ProgressDocument { SelectedWords = ["一"] });
        await Put(client, 1, new ProgressDocument { SelectedWords = ["一", "二"] });

        var stale = await Put(client, 1, new ProgressDocument { SelectedWords = ["三"] });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var latest = await stale.Content.ReadFromJsonAsync<ProgressEnvelope>();
        Assert.Equal(2, latest!.Version);
        Assert.Equal(["一", "二"], latest.Progress.SelectedWords);
    }

    [Fact]
    public async Task ProgressNeedsASignIn()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/progress")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "made-up");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/progress")).StatusCode);
    }

    [Fact]
    public async Task LoggingOutEndsTheToken()
    {
        var client = await SignedInClient();

        await client.PostAsync("/api/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/progress")).StatusCode);
    }

    [Fact]
    public async Task ATakenNameCannotRegisterAgain()
    {
        await SignedInClient("lai");
        var client = _factory.CreateClient();

        var again = await client.PostAsJsonAsync("/api/register", new { userName = "Lai", password = "another1" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheName()
    {
        await SignedInClient("thanh");
        var client = _factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/login", new { userName = "thanh", password = "nope" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var locked = await client.PostAsJsonAsync("/api/login", new { userName = "thanh", password = "secret1" });

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task TheWordListIsServed()
    {
        var client = _factory.CreateClient();

        var words = await client.GetFromJsonAsync<List<Dictionary<string, string>>>("/api/vocabulary");

        Assert.True(words!.Count > 10_000);
        Assert.Equal("你好", words[0]["Chinese"]);
    }

    private sealed record SignedInReply(string Token, string UserName);
}
