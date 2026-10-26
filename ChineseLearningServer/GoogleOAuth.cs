using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ChineseLearningServer;

// What Google's token endpoint hands back for a sign-in code or a refresh.
public sealed record GoogleTokens(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    // The permissions the learner actually gave (they can untick Gemini).
    [property: JsonPropertyName("scope")] string? Scope = null)
{
    // Whether Gemini was allowed; Google leaves out "scope" only when all asked were given.
    public bool AllowsGemini =>
        Scope is null || Scope.Contains("https://www.googleapis.com/auth/cloud-platform", StringComparison.Ordinal);
}

// Google sign-in (OAuth 2.0 authorization code flow) that by default also asks
// for the learner's permission to use Gemini for them, with offline access, so
// no Gemini API key is needed: the server keeps the learner's refresh token and
// calls Gemini with the learner's own Google access. The learner can untick
// Gemini and still sign in (a key then works instead), and allow it later.
public interface IGoogleOAuth
{
    string AuthorizationUrl(string redirectUri, string state, bool gemini, bool askConsent);

    // The tokens for a code Google sent back to the redirect address, or null.
    Task<GoogleTokens?> ExchangeCodeAsync(string code, string redirectUri);

    // A fresh access token from a refresh token; null when Google no longer
    // honours it (the learner removed the app's access, or it expired).
    Task<GoogleTokens?> RefreshAsync(string refreshToken);
}

public sealed class GoogleOAuth(HttpClient http, string clientId, string clientSecret) : IGoogleOAuth
{
    // Who the learner is.
    public const string SignInScopes = "openid email";

    // Plus Gemini (Google's documented scopes for the Gemini API with OAuth).
    public const string GeminiScopes =
        SignInScopes + " https://www.googleapis.com/auth/cloud-platform https://www.googleapis.com/auth/generative-language.retriever";

    public string AuthorizationUrl(string redirectUri, string state, bool gemini, bool askConsent)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = gemini ? GeminiScopes : SignInScopes,
            // Consent again only when Google gave no refresh token last time.
            ["prompt"] = askConsent ? "consent select_account" : "select_account",
            ["state"] = state
        };
        if (gemini)
        {
            query["access_type"] = "offline";
            query["include_granted_scopes"] = "true";
        }

        return "https://accounts.google.com/o/oauth2/v2/auth?" +
               string.Join("&", query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
    }

    public Task<GoogleTokens?> ExchangeCodeAsync(string code, string redirectUri) => Post(new Dictionary<string, string>
    {
        ["code"] = code,
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret,
        ["redirect_uri"] = redirectUri,
        ["grant_type"] = "authorization_code"
    });

    public Task<GoogleTokens?> RefreshAsync(string refreshToken) => Post(new Dictionary<string, string>
    {
        ["refresh_token"] = refreshToken,
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret,
        ["grant_type"] = "refresh_token"
    });

    private async Task<GoogleTokens?> Post(Dictionary<string, string> form)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<GoogleTokens>() : null;
    }
}
