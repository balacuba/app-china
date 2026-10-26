using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;

namespace ChineseLearningServer;

// Sign in with Google with Gemini included, and Gemini called for the learner.
//
// /api/oauth/google/start sends the browser to Google (state in a cookie);
// Google sends it back to /api/oauth/google/callback with a code, which is
// swapped for tokens: the ID token says who it is (the Gmail account's account
// here, made on first use), the refresh token is kept so Gemini can be used for
// them later, if they allowed it (?gemini=0 asks only who they are). The
// browser then gets a one-use ticket, which the website trades for a sign-in
// (/api/login/ticket), so no token ever sits in an address.
//
// /api/gemini/generate passes a Gemini request on with the learner's own
// Google access (refreshed as needed), for the website and the desktop app.
public static class GoogleEndpoints
{
    private const string GeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent";
    private const string StateCookie = "google_state";
    private const int MaxGeminiRequestBytes = 256 * 1024;
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private sealed record Pending(string ReturnTo, bool Gemini, bool AskedConsent, DateTimeOffset Created);

    private sealed record Ticket(long UserId, string UserName, string ReturnTo, DateTimeOffset Expires);

    // Sign-ins under way, tickets not yet traded, and Google access tokens, for one running server.
    private sealed class Store
    {
        public readonly ConcurrentDictionary<string, Pending> PendingSignIns = new();
        public readonly ConcurrentDictionary<string, Ticket> Tickets = new();
        public readonly ConcurrentDictionary<long, (string Token, DateTimeOffset Expires)> AccessTokens = new();
    }

    public static void MapGoogle(this RouteGroupBuilder api, string clientId)
    {
        var store = new Store();

        // Where Google sends the browser back: this server's own address.
        static string RedirectUri(HttpRequest request) =>
            $"{request.Scheme}://{request.Host}/api/oauth/google/callback";

        api.MapGet("/oauth/google/start", (HttpContext context, [FromServices] IGoogleOAuth? google, string? returnTo, string? gemini) =>
        {
            if (google is null)
            {
                return Results.Redirect("/#/");
            }

            Sweep(store);
            var state = NewSecret();
            var withGemini = gemini is not ("0" or "false");
            store.PendingSignIns[state] = new Pending(SafeReturn(returnTo), withGemini, false, DateTimeOffset.UtcNow);
            context.Response.Cookies.Append(StateCookie, state, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/oauth",
                MaxAge = PendingLifetime
            });
            return Results.Redirect(google.AuthorizationUrl(RedirectUri(context.Request), state, withGemini, askConsent: false));
        });

        api.MapGet("/oauth/google/callback", async (HttpContext context, [FromServices] IGoogleOAuth? google, IGoogleTokenValidator validator, Database db,
            string? code, string? state, string? error) =>
        {
            // The answer must belong to a sign-in this browser started here.
            if (google is null || string.IsNullOrEmpty(state) || context.Request.Cookies[StateCookie] != state ||
                !store.PendingSignIns.TryRemove(state, out var pending) || DateTimeOffset.UtcNow - pending.Created > PendingLifetime)
            {
                return Results.Redirect("/#/?google=expired");
            }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                return Results.Redirect("/#/?google=cancelled");
            }

            var redirectUri = RedirectUri(context.Request);
            var tokens = await google.ExchangeCodeAsync(code, redirectUri);
            if (tokens?.IdToken is null || await validator.ValidateAsync(tokens.IdToken, clientId) is not { } identity)
            {
                return Results.Redirect("/#/?google=failed");
            }

            (long Id, string UserName) user;
            try
            {
                user = db.FindOrCreateGoogleUser(identity);
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/#/?google=failed");
            }

            // Signing in without Gemini (not asked, or unticked at Google) leaves
            // an earlier Gemini permission as it is.
            var allowedGemini = pending.Gemini && tokens.AllowsGemini;
            if (allowedGemini && !string.IsNullOrEmpty(tokens.RefreshToken))
            {
                db.SaveGeminiGrant(user.Id, tokens.RefreshToken);
                if (tokens.AccessToken is { Length: > 0 })
                {
                    store.AccessTokens[user.Id] = (tokens.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tokens.ExpiresIn - 60)));
                }
            }
            else if (allowedGemini && db.LoadGeminiGrant(user.Id) is null && !pending.AskedConsent)
            {
                // Google gives a refresh token only when asked for consent: ask once more.
                var again = NewSecret();
                store.PendingSignIns[again] = pending with { AskedConsent = true, Created = DateTimeOffset.UtcNow };
                context.Response.Cookies.Append(StateCookie, again, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/api/oauth",
                    MaxAge = PendingLifetime
                });
                return Results.Redirect(google.AuthorizationUrl(redirectUri, again, gemini: true, askConsent: true));
            }

            context.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/oauth" });
            var ticket = NewSecret();
            store.Tickets[ticket] = new Ticket(user.Id, user.UserName, pending.ReturnTo, DateTimeOffset.UtcNow.AddMinutes(2));
            return Results.Redirect($"/#/google-done?ticket={ticket}");
        });

        api.MapPost("/login/ticket", (TicketBody body, Database db) =>
            !string.IsNullOrEmpty(body.Ticket) && store.Tickets.TryRemove(body.Ticket, out var ticket) && ticket.Expires > DateTimeOffset.UtcNow
                ? Results.Ok(new { token = db.IssueToken(ticket.UserId), userName = ticket.UserName, returnTo = ticket.ReturnTo })
                : Auth.Problem("Phiên đăng nhập Google đã hết hạn. Hãy bấm đăng nhập lại.", StatusCodes.Status401Unauthorized));

        // Whether Dịch đoạn văn can use Gemini through this server for the caller.
        api.MapGet("/gemini/status", (HttpContext context, Database db, [FromServices] IGoogleOAuth? google) =>
            Auth.SignedInUser(context, db) is { } user
                ? Results.Ok(new { available = google is not null, connected = google is not null && db.LoadGeminiGrant(user.Id) is not null })
                : Auth.Unauthorized());

        // A Gemini generateContent request, sent on with the learner's own Google
        // access; Google's answer (and status) comes back as it is.
        api.MapPost("/gemini/generate", async (HttpContext context, Database db, [FromServices] IGoogleOAuth? google, IHttpClientFactory clients) =>
        {
            if (Auth.SignedInUser(context, db) is not { } user)
            {
                return Auth.Unauthorized();
            }

            if (google is null)
            {
                return Auth.Problem("Máy chủ chưa bật Gemini qua tài khoản Google.", StatusCodes.Status404NotFound);
            }

            if (context.Request.ContentLength is null or > MaxGeminiRequestBytes)
            {
                return Auth.Problem("Yêu cầu gửi Gemini không hợp lệ.", StatusCodes.Status413PayloadTooLarge);
            }

            var access = await AccessToken(store, user.Id, db, google);
            if (access is null)
            {
                return Auth.Problem("Tài khoản chưa cho phép dùng Gemini qua Google. Hãy bấm nút 🔑 → “Cho phép Gemini qua Google” và đánh dấu quyền Gemini.",
                    StatusCodes.Status412PreconditionFailed);
            }

            using var body = new StreamContent(context.Request.Body);
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, GeminiEndpoint) { Content = body };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await clients.CreateClient("gemini").SendAsync(request, context.RequestAborted);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                store.AccessTokens.TryRemove(user.Id, out _);
            }

            return Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json",
                statusCode: (int)response.StatusCode);
        });
    }

    // The learner's Google access token, refreshed when it is (nearly) out of date.
    private static async Task<string?> AccessToken(Store store, long userId, Database db, IGoogleOAuth google)
    {
        if (store.AccessTokens.TryGetValue(userId, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Token;
        }

        if (db.LoadGeminiGrant(userId) is not { } refresh)
        {
            return null;
        }

        var tokens = await google.RefreshAsync(refresh);
        if (tokens?.AccessToken is not { Length: > 0 } access)
        {
            // Google no longer honours it: the learner must allow Gemini again.
            db.DeleteGeminiGrant(userId);
            return null;
        }

        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            db.SaveGeminiGrant(userId, tokens.RefreshToken);
        }

        store.AccessTokens[userId] = (access, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, tokens.ExpiresIn - 60)));
        return access;
    }

    // Back to a page of this website only ("#/…"), never to another site.
    private static string SafeReturn(string? returnTo) =>
        returnTo is { Length: < 200 } && returnTo.StartsWith("#/", StringComparison.Ordinal) && !returnTo.Contains("//") ? returnTo : "#/";

    private static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static void Sweep(Store store)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, value) in store.PendingSignIns)
        {
            if (now - value.Created > PendingLifetime)
            {
                store.PendingSignIns.TryRemove(key, out _);
            }
        }

        foreach (var (key, value) in store.Tickets)
        {
            if (value.Expires < now)
            {
                store.Tickets.TryRemove(key, out _);
            }
        }
    }

    private sealed record TicketBody(string? Ticket);
}
