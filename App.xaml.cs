using System.Windows;

namespace FastImageGallery
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Settings.Load();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Settings.Save();
            base.OnExit(e);
        }
    }
} 