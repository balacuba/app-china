using ChineseLearningServer;

// The study server on its own (the desktop app also runs it inside itself; see
// StudyServer). With --open-browser it opens the website once it is listening
// (or, if it is already running, just opens it).
var installed = !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
var openBrowser = args.Contains("--open-browser");
var app = StudyServer.Create(args.Where(arg => arg != "--open-browser").ToArray(), installed);

// The address to open in a browser: a "listen on every network card" address
// is opened as localhost on this PC.
string WebsiteAddress() => (app.Urls.FirstOrDefault() ?? app.Configuration["Urls"] ?? "http://localhost:5080")
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

// Lets the tests start the server in memory.
public partial class Program;
