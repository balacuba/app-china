namespace ChineseLearningApp;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

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