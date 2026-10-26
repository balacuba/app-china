using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChineseLearningServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChineseLearning.Tests;

// Google sign-in against the in-memory server, with Google's check replaced by
// a stand-in that knows two made-up credentials.
public sealed class GoogleSignInTests : IDisposable
{
    private const string ClientId = "test-client.apps.googleusercontent.com";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chinese-google-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public GoogleSignInTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database", _databasePath);
            builder.UseSetting("GoogleClientId", ClientId);
            builder.ConfigureServices(services => services.AddSingleton<IGoogleTokenValidator>(new FakeGoogle()));
        });
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

    private async Task<HttpResponseMessage> SignIn(string credential) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/login/google", new { credential });

    [Fact]
    public async Task TheSiteTellsTheWebsiteItsClientId()
    {
        var config = await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/config");

        Assert.Equal(ClientId, config!["googleClientId"].GetString());
        Assert.False(config["googleRedirectLogin"].GetBoolean());
    }

    [Fact]
    public async Task AGmailAccountIsMadeOnFirstSignInAndFoundAfterwards()
    {
        var first = await (await SignIn("hoai")).Content.ReadFromJsonAsync<SignedInReply>();
        var again = await (await SignIn("hoai")).Content.ReadFromJsonAsync<SignedInReply>();

        Assert.Equal("Lại Hoài", first!.UserName);
        Assert.Equal(first.UserName, again!.UserName);
        Assert.NotEqual(first.Token, again.Token);
    }

    [Fact]
    public async Task ACredentialGoogleDoesNotConfirmIsRefused()
    {
        var response = await SignIn("forged");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheGoogleAccountCannotBeEnteredWithAPassword()
    {
        await SignIn("hoai");

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/login", new { userName = "Lại Hoài", password = "anything" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task ASignedInDeviceCanHandTheDesktopAppItsOwnToken()
    {
        var signedIn = await (await SignIn("hoai")).Content.ReadFromJsonAsync<SignedInReply>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn!.Token);

        var desktop = await (await client.PostAsync("/api/tokens", null)).Content.ReadFromJsonAsync<SignedInReply>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", desktop!.Token);

        Assert.Equal(signedIn.UserName, desktop.UserName);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/progress")).StatusCode);
    }

    [Fact]
    public async Task WithoutAClientIdGoogleSignInIsOff()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Database", _databasePath + ".off"));

        var config = await factory.CreateClient().GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/config");
        var response = await factory.CreateClient().PostAsJsonAsync("/api/login/google", new { credential = "hoai" });

        Assert.Equal("", config!["googleClientId"].GetString());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath + ".off");
    }

    private sealed class FakeGoogle : IGoogleTokenValidator
    {
        public Task<GoogleIdentity?> ValidateAsync(string credential, string clientId) =>
            Task.FromResult(credential == "hoai" && clientId == ClientId
                ? new GoogleIdentity("google-sub-123", "lai.hoai@gmail.com", "Lại Hoài")
                : null);
    }

    private sealed record SignedInReply(string Token, string UserName);
}
