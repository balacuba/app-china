using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ChineseLearning.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;

namespace ChineseLearningServer;

// The study server: accounts, each account's progress (shared by the website
// and the desktop app), the word list, and the website itself from wwwroot.
// Run on its own (Program.cs) or inside the desktop app, which starts it with
// the app so the website is there whenever the app is open.
//
// Installed (anything but Development), it runs from its own folder whatever
// folder it is started in and keeps its database with the app's other data in
// %LOCALAPPDATA%\ChineseLearningApp\server.
public static class StudyServer
{
    public static WebApplication Create(string[] args, bool installed)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = installed ? AppContext.BaseDirectory : null
        });
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/json");
        });

        var contentRoot = builder.Environment.ContentRootPath;
        var defaultDatabase = installed
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChineseLearningApp", "server", "app-data.db")
            : "app-data.db";
        var databasePath = Path.GetFullPath(builder.Configuration["Database"] ?? defaultDatabase, contentRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new Database(databasePath);
        database.Initialize();
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
        // "Sign in with Google" needs this site's OAuth client ID from Google Cloud
        // (GoogleClientId); without one the button stays hidden. Installed, it can sit
        // in settings.json beside the database, which a reinstall leaves alone.
        // The installer may ship one beside the server too (the data folder's wins).
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "settings.json"), optional: true, reloadOnChange: false);
        builder.Configuration.AddJsonFile(Path.Combine(Path.GetDirectoryName(databasePath)!, "settings.json"), optional: true, reloadOnChange: false);
        var googleClientId = builder.Configuration["GoogleClientId"]?.Trim() ?? "";
        // With the client's secret too (GoogleClientSecret), signing in with Google also
        // asks to use Gemini for the learner, so Dịch đoạn văn needs no API key.
        var googleClientSecret = builder.Configuration["GoogleClientSecret"]?.Trim() ?? "";
        builder.Services.AddHttpClient("gemini", client => client.Timeout = TimeSpan.FromSeconds(60));
        if (googleClientId.Length > 0 && googleClientSecret.Length > 0)
        {
            builder.Services.AddHttpClient<IGoogleOAuth, GoogleOAuth>(http => new GoogleOAuth(http, googleClientId, googleClientSecret));
            builder.Services.AddHttpClient<IGeminiKeyMaker, GoogleCloudKeys>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }
        // The word list is copied next to the server when it is built (see the .csproj).
        var vocabularyPath = Path.GetFullPath(
            builder.Configuration["VocabularyPath"] ?? Path.Combine(AppContext.BaseDirectory, "Data", "vocabulary-starter.json"),
            contentRoot);

        var app = builder.Build();
        app.UseResponseCompression();
        app.UseDefaultFiles();
        // Browsers always ask whether a page file changed (a cheap 304 when not), so
        // an updated website shows up at once instead of an old cached copy.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
        });

        // Five wrong passwords in a row lock that name for a minute.
        var failedSignIns = new ConcurrentDictionary<string, (int Count, DateTimeOffset LockedUntil)>(StringComparer.OrdinalIgnoreCase);
        const int MaxFailures = 5;
        var lockTime = TimeSpan.FromMinutes(1);
        const int MaxProgressBytes = 2 * 1024 * 1024;

        var api = app.MapGroup("/api");

        api.MapPost("/register", (Credentials credentials, Database db) =>
        {
            var userName = credentials.UserName?.Trim() ?? "";
            if (!Rules.UserName().IsMatch(userName))
            {
                return Problem("Tên đăng nhập dài 3–32 ký tự, chỉ gồm chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới.");
            }

            if ((credentials.Password ?? "").Length < 6)
            {
                return Problem("Mật khẩu cần ít nhất 6 ký tự.");
            }

            var id = db.CreateUser(userName, credentials.Password!);
            return id is null
                ? Problem("Tên đăng nhập này đã có người dùng.", StatusCodes.Status409Conflict)
                : Results.Ok(new SignedIn(db.IssueToken(id.Value), userName));
        });

        api.MapPost("/login", (Credentials credentials, Database db) =>
        {
            var userName = credentials.UserName?.Trim() ?? "";
            if (failedSignIns.TryGetValue(userName, out var failures) && failures.LockedUntil > DateTimeOffset.UtcNow)
            {
                return Problem("Sai mật khẩu nhiều lần. Hãy thử lại sau một phút.", StatusCodes.Status429TooManyRequests);
            }

            var id = db.CheckPassword(userName, credentials.Password ?? "");
            if (id is null)
            {
                var count = failedSignIns.TryGetValue(userName, out var before) && before.LockedUntil <= DateTimeOffset.UtcNow && before.Count < MaxFailures
                    ? before.Count + 1
                    : 1;
                failedSignIns[userName] = (count, count >= MaxFailures ? DateTimeOffset.UtcNow + lockTime : DateTimeOffset.MinValue);
                return Problem("Sai tên đăng nhập hoặc mật khẩu.", StatusCodes.Status401Unauthorized);
            }

            failedSignIns.TryRemove(userName, out _);
            return Results.Ok(new SignedIn(db.IssueToken(id.Value), userName));
        });

        // What the website needs to know before anyone signs in.
        api.MapGet("/config", ([FromServices] IGoogleOAuth? google) =>
            Results.Ok(new { googleClientId, googleRedirectLogin = googleClientId.Length > 0 && google is not null }));

        api.MapGoogle(googleClientId);

        // Signs in with the credential from the Sign in with Google button: the Gmail
        // account's own account here, made on first use.
        api.MapPost("/login/google", async (GoogleCredential body, Database db, IGoogleTokenValidator google) =>
        {
            if (googleClientId.Length == 0)
            {
                return Problem("Máy chủ này chưa bật đăng nhập bằng Google.", StatusCodes.Status404NotFound);
            }

            if (string.IsNullOrWhiteSpace(body.Credential) || await google.ValidateAsync(body.Credential, googleClientId) is not { } identity)
            {
                return Problem("Google không xác nhận được tài khoản này. Hãy thử đăng nhập lại.", StatusCodes.Status401Unauthorized);
            }

            try
            {
                var (id, userName) = db.FindOrCreateGoogleUser(identity);
                return Results.Ok(new SignedIn(db.IssueToken(id), userName));
            }
            catch (InvalidOperationException ex)
            {
                return Problem(ex.Message, StatusCodes.Status409Conflict);
            }
        });

        // A second sign-in for the same account, handed to the desktop app when the
        // learner signs it in through the website (each device keeps its own token).
        api.MapPost("/tokens", (HttpContext context, Database db) =>
            SignedInUser(context, db) is { } user
                ? Results.Ok(new SignedIn(db.IssueToken(user.Id), user.UserName))
                : Unauthorized());

        api.MapPost("/logout", (HttpContext context, Database db) =>
        {
            if (BearerToken(context) is { } token)
            {
                db.RevokeToken(token);
            }

            return Results.NoContent();
        });

        api.MapGet("/progress", (HttpContext context, Database db) =>
            SignedInUser(context, db) is { } user ? Results.Ok(db.GetProgress(user.Id)) : Unauthorized());

        // Saves the merged document only if the caller merged into the latest version
        // (sent as If-Match); otherwise 409 with the latest, to merge again and retry.
        api.MapPut("/progress", async (HttpContext context, Database db) =>
        {
            if (SignedInUser(context, db) is not { } user)
            {
                return Unauthorized();
            }

            if (!int.TryParse(context.Request.Headers.IfMatch.ToString().Trim('"'), out var expected) || expected < 0)
            {
                return Problem("Thiếu phiên bản (If-Match).", StatusCodes.Status428PreconditionRequired);
            }

            if (context.Request.ContentLength is > MaxProgressBytes)
            {
                return Problem("Dữ liệu tiến độ quá lớn.", StatusCodes.Status413PayloadTooLarge);
            }

            ProgressDocument? document;
            try
            {
                document = await context.Request.ReadFromJsonAsync<ProgressDocument>();
            }
            catch (System.Text.Json.JsonException)
            {
                document = null;
            }

            if (document is null)
            {
                return Problem("Dữ liệu tiến độ không hợp lệ.");
            }

            return db.SaveProgress(user.Id, expected, document) is { } saved
                ? Results.Ok(saved)
                : Results.Json(db.GetProgress(user.Id), statusCode: StatusCodes.Status409Conflict);
        });

        api.MapGet("/me", (HttpContext context, Database db) =>
            SignedInUser(context, db) is { } user ? Results.Ok(new { userName = user.UserName }) : Unauthorized());

        // The shared word list (the same file the desktop app is built with). Browsers
        // keep it and only ask whether it changed, so it downloads once.
        api.MapGet("/vocabulary", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            return Results.File(vocabularyPath, "application/json", lastModified: File.GetLastWriteTimeUtc(vocabularyPath));
        });

        return app;

        // The helpers the endpoints above use, kept with the other endpoints in Auth.cs.
        static string? BearerToken(HttpContext context) => Auth.BearerToken(context);

        static (long Id, string UserName)? SignedInUser(HttpContext context, Database db) => Auth.SignedInUser(context, db);

        static IResult Problem(string message, int status = StatusCodes.Status400BadRequest) => Auth.Problem(message, status);

        static IResult Unauthorized() => Auth.Unauthorized();
    }
}

internal sealed record Credentials(string? UserName, string? Password);

internal sealed record SignedIn(string Token, string UserName);

internal sealed record GoogleCredential(string? Credential);

internal static partial class Rules
{
    [GeneratedRegex("^[A-Za-z0-9._-]{3,32}$")]
    public static partial Regex UserName();
}
