using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ChineseLearning.Sync;
using ChineseLearningServer;
using Microsoft.AspNetCore.ResponseCompression;

// The study server: accounts, each account's progress (shared by the website
// and the desktop app), the word list, and the website itself from wwwroot.
//
// Installed (anything but Development), it runs from its own folder whatever
// folder it is started in, keeps its database with the app's other data in
// %LOCALAPPDATA%\ChineseLearningApp\server, and with --open-browser opens the
// website once it is listening (or, if it is already running, just opens it).
var installed = !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
var openBrowser = args.Contains("--open-browser");
args = args.Where(arg => arg != "--open-browser").ToArray();
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

// The address to open in a browser: a "listen on every network card" address
// is opened as localhost on this PC.
string WebsiteAddress() => (app.Urls.FirstOrDefault() ?? builder.Configuration["Urls"] ?? "http://localhost:5080")
    .Replace("0.0.0.0", "localhost").Replace("[::]", "localhost").Replace("://*", "://localhost").Replace("://+", "://localhost");

if (openBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() => OpenInBrowser(WebsiteAddress()));
}

try
{
    app.Run();
}
catch (IOException ex) when (openBrowser && ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException)
{
    // Already running (the shortcut was clicked twice): just show the website.
    OpenInBrowser(WebsiteAddress());
}

static void OpenInBrowser(string address)
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true });
    }
    catch (System.ComponentModel.Win32Exception)
    {
        Console.WriteLine($"Mở trình duyệt vào {address} để học.");
    }
}

static string? BearerToken(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
}

static (long Id, string UserName)? SignedInUser(HttpContext context, Database db) =>
    BearerToken(context) is { Length: > 0 } token ? db.FindToken(token) : null;

static IResult Problem(string message, int status = StatusCodes.Status400BadRequest) =>
    Results.Json(new { error = message }, statusCode: status);

static IResult Unauthorized() => Problem("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.", StatusCodes.Status401Unauthorized);

internal sealed record Credentials(string? UserName, string? Password);

internal sealed record SignedIn(string Token, string UserName);

internal static partial class Rules
{
    [GeneratedRegex("^[A-Za-z0-9._-]{3,32}$")]
    public static partial Regex UserName();
}

// Lets the tests start the server in memory.
public partial class Program;
