namespace ChineseLearningApp;

static class Program
{
    // Set by a second start of the app: the running one (often hidden in the
    // tray) shows its window instead.
    internal static EventWaitHandle? ShowRequested { get; private set; }

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        // One app per Windows user: starting it again shows the one already running.
        using var single = new Mutex(true, @"Local\ChineseLearningApp.SingleInstance", out var first);
        using var showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ChineseLearningApp.Show");
        if (!first)
        {
            showRequested.Set();
            return;
        }

        ShowRequested = showRequested;

        // The study website runs for as long as the app does.
        StudyWebsite.Start();
        try
        {
            using var nameForm = new NameEntryForm();
            if (nameForm.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            Application.Run(new Form1(nameForm.EnteredName));
        }
        finally
        {
            try
            {
                StudyWebsite.StopAsync().Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException)
            {
                // Closing anyway.
            }
        }
    }
}