using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ChineseLearningServer;

// What Google's token endpoint hands back for a sign-in code or a refresh.
public sealed record GoogleTokens(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

// Google sign-in that also asks for the learner's permission to use Gemini
// for them (OAuth 2.0 authorization code flow with offline access), so no
// Gemini API key is needed: the server keeps the learner's refresh token and
// calls Gemini with the learner's own Google access.
public interface IGoogleOAuth
{
    string AuthorizationUrl(string redirectUri, string state, bool askConsent);

    // The tokens for a code Google sent back to the redirect address, or null.
    Task<GoogleTokens?> ExchangeCodeAsync(string code, string redirectUri);

    // A fresh access token from a refresh token; null when Google no longer
    // honours it (the learner removed the app's access, or it expired).
    Task<GoogleTokens?> RefreshAsync(string refreshToken);
}

public sealed class GoogleOAuth(HttpClient http, string clientId, string clientSecret) : IGoogleOAuth
{
    // Who the learner is, plus Gemini (Google's documented scopes for the
    // Gemini API with OAuth).
    public const string Scopes =
        "openid email https://www.googleapis.com/auth/cloud-platform https://www.googleapis.com/auth/generative-language.retriever";

    public string AuthorizationUrl(string redirectUri, string state, bool askConsent) =>
        "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["access_type"] = "offline",
            ["include_granted_scopes"] = "true",
            // Consent again only when Google gave no refresh token last time.
            ["prompt"] = askConsent ? "consent select_account" : "select_account",
            ["state"] = state
        }.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

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
