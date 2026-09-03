using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DSDeaths.Live {
    public partial class App : Application {
        private SingleInstanceGuard instanceGuard;
        private DSDeathsMonitor monitor;

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DiagnosticLogger.Write("DSDeaths Live starting, version=" +
                typeof(App).Assembly.GetName().Version);

            string liveSettingsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                LiveSettings.FileName);
            LiveSettingsWarning[] settingsWarnings;
            LiveSettings settings = LiveSettings.Load(
                liveSettingsPath,
                out _,
                out settingsWarnings);
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

            var window = new MainWindow(monitor, settings, liveSettingsPath, settingsWarnings);
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e) {
            DiagnosticLogger.Write("DSDeaths Live exiting.");
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

        private void App_DispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e) {
            DiagnosticLogger.WriteException("Unhandled UI exception", e.Exception);
            MessageBox.Show(
                Localization.Format("UnhandledError", DiagnosticLogger.LogPath),
                Localization.Get("ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs e) {
            Exception exception = e.ExceptionObject as Exception;
            DiagnosticLogger.WriteException(
                "Unhandled application exception",
                exception ?? new Exception(e.ExceptionObject == null ? "Unknown exception" : e.ExceptionObject.ToString()));
        }
    }
}
