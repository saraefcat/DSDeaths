using System;
using System.IO;
using System.Windows;

namespace DSDeaths.Live {
    public partial class App : Application {
        private SingleInstanceGuard instanceGuard;
        private DSDeathsMonitor monitor;

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            string liveSettingsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                LiveSettings.FileName);
            string settingsWarning;
            LiveSettings settings = LiveSettings.Load(liveSettingsPath, out settingsWarning);
            Localization.SetLanguage(settings.Language);

            if (!SingleInstanceGuard.TryAcquire(out instanceGuard)) {
                instanceGuard.Dispose();
                instanceGuard = null;
                MessageBox.Show(
                    Localization.Get("AlreadyRunningMessage"),
                    Localization.Get("AlreadyRunningTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            monitor = new DSDeathsMonitor(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DSDeathsMonitor.OutputFileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, EldenRingOffsetSettings.FileName));

            var window = new MainWindow(monitor, settings, liveSettingsPath, settingsWarning);
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e) {
            if (monitor != null) {
                monitor.Dispose();
                monitor = null;
            }
            if (instanceGuard != null) {
                instanceGuard.Dispose();
                instanceGuard = null;
            }
            base.OnExit(e);
        }
    }
}
