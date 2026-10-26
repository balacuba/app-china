using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChineseLearningServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ChineseLearning.Tests;

// Signing in through Google's own page (with Gemini allowed) and Gemini used
// through the server, with Google and Gemini replaced by stand-ins.
public sealed class GeminiThroughGoogleTests : IDisposable
{
    private const string ClientId = "test-client.apps.googleusercontent.com";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chinese-gemini-{Guid.NewGuid():N}.db");
    private readonly FakeOAuth _google = new();
    private readonly FakeGemini _gemini = new();
    private readonly WebApplicationFactory<Program> _factory;

    public GeminiThroughGoogleTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database", _databasePath);
            builder.UseSetting("GoogleClientId", ClientId);
            builder.UseSetting("GoogleClientSecret", "test-secret");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IGoogleTokenValidator>(new FakeIdTokens());
                services.AddSingleton<IGoogleOAuth>(_google);
                services.AddHttpClient("gemini").ConfigurePrimaryHttpMessageHandler(() => _gemini);
            });
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

    private HttpClient Browser() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string Query(Uri address, string name) =>
        System.Web.HttpUtility.ParseQueryString(address.Query)[name] ?? "";

    // Start, "come back from Google" with a code, and return where the browser lands.
    private static async Task<Uri> SignInAtGoogle(HttpClient browser, string code = "good")
    {
        var start = await browser.GetAsync("/api/oauth/google/start?returnTo=%23%2Ftranslate");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var state = Query(start.Headers.Location!, "state");
        var back = await browser.GetAsync($"/api/oauth/google/callback?code={code}&state={state}");
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        return back.Headers.Location!;
    }

    private static async Task<TicketReply> Redeem(HttpClient browser, Uri landing)
    {
        var ticket = landing.OriginalString.Split("ticket=")[1];
        var reply = await browser.PostAsJsonAsync("/api/login/ticket", new { ticket });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        return (await reply.Content.ReadFromJsonAsync<TicketReply>())!;
    }

    [Fact]
    public async Task TheWebsiteIsToldToSignInThroughGooglesPage()
    {
        var config = await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/config");

        Assert.True(config!["googleRedirectLogin"].GetBoolean());
    }

    [Fact]
    public async Task SigningInAtGoogleGivesAOneUseTicketBackToThePageStartedFrom()
    {
        var browser = Browser();
        var landing = await SignInAtGoogle(browser);
        var signedIn = await Redeem(browser, landing);
        var again = await browser.PostAsJsonAsync("/api/login/ticket", new { ticket = landing.OriginalString.Split("ticket=")[1] });

        Assert.StartsWith("/#/google-done?ticket=", landing.OriginalString);
        Assert.Equal("lai.hoai@gmail.com", signedIn.UserName);
        Assert.Equal("#/translate", signedIn.ReturnTo);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task AnAnswerForASignInThisBrowserDidNotStartIsRefused()
    {
        var back = await Browser().GetAsync("/api/oauth/google/callback?code=good&state=made-up");

        Assert.Equal("/#/?google=expired", back.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task WithoutARefreshTokenGoogleIsAskedForConsentOnce()
    {
        var browser = Browser();
        var landing = await SignInAtGoogle(browser, code: "no-refresh");

        Assert.StartsWith("https://accounts.google.com/", landing.OriginalString);
        Assert.Equal("consent", Query(landing, "prompt"));
    }

    [Fact]
    public async Task GeminiIsCalledWithTheLearnersOwnGoogleAccess()
    {
        var browser = Browser();
        var signedIn = await Redeem(browser, await SignInAtGoogle(browser));
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Token);

        var status = await browser.GetFromJsonAsync<Dictionary<string, bool>>("/api/gemini/status");
        var answer = await browser.PostAsync("/api/gemini/generate",
            new StringContent("""{"contents":[{"parts":[{"text":"你好"}]}]}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.True(status!["connected"]);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Contains("xin chào", await answer.Content.ReadAsStringAsync());
        Assert.Equal("access-1", _gemini.LastAccessToken);
        Assert.Contains("你好", _gemini.LastBody);
    }

    [Fact]
    public async Task WhenGoogleNoLongerHonoursTheGrantTheLearnerIsAskedToAllowGeminiAgain()
    {
        var browser = Browser();
        var signedIn = await Redeem(browser, await SignInAtGoogle(browser, code: "short-lived"));
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Token);

        var answer = await browser.PostAsync("/api/gemini/generate", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        var status = await browser.GetFromJsonAsync<Dictionary<string, bool>>("/api/gemini/status");

        Assert.Equal(HttpStatusCode.PreconditionFailed, answer.StatusCode);
        Assert.False(status!["connected"]);
    }

    [Fact]
    public async Task GeminiNeedsASignIn()
    {
        var answer = await _factory.CreateClient().PostAsync("/api/gemini/generate",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    private sealed class FakeIdTokens : IGoogleTokenValidator
    {
        public Task<GoogleIdentity?> ValidateAsync(string credential, string clientId) =>
            Task.FromResult(credential == "id-token" && clientId == ClientId
                ? new GoogleIdentity("google-sub-123", "lai.hoai@gmail.com")
                : null);
    }

    // "good": a refresh token that works; "short-lived": an access token that
    // has already run out and a refresh token Google refuses; "no-refresh": none.
    private sealed class FakeOAuth : IGoogleOAuth
    {
        public string AuthorizationUrl(string redirectUri, string state, bool askConsent) =>
            $"https://accounts.google.com/o/oauth2/v2/auth?state={state}&prompt={(askConsent ? "consent" : "select_account")}";

        public Task<GoogleTokens?> ExchangeCodeAsync(string code, string redirectUri) => Task.FromResult<GoogleTokens?>(code switch
        {
            "good" => new GoogleTokens("id-token", "access-1", "refresh-good", 3600),
            "short-lived" => new GoogleTokens("id-token", null, "refresh-revoked", 0),
            "no-refresh" => new GoogleTokens("id-token", "access-1", null, 3600),
            _ => null
        });

        public Task<GoogleTokens?> RefreshAsync(string refreshToken) => Task.FromResult<GoogleTokens?>(
            refreshToken == "refresh-good" ? new GoogleTokens(null, "access-2", null, 3600) : null);
    }

    private sealed class FakeGemini : HttpMessageHandler
    {
        public string? LastAccessToken { get; private set; }

        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAccessToken = request.Headers.Authorization?.Parameter;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"xin chào"}]}}]}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record TicketReply(string Token, string UserName, string ReturnTo);
}
