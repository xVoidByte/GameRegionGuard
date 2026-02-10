using System;
using System.Windows;
using GameRegionGuard.Helpers;
using GameRegionGuard.Services;

namespace GameRegionGuard
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Initialize();
            Logger.Info("Application starting...");

            // Load user settings early so the theme is applied before the MainWindow is created.
            AppSettingsService.Load();
            if (ThemeService.TryApplySavedTheme())
            {
                Logger.Info($"Loaded saved theme: {(AppSettingsService.IsDarkTheme ? "Dark" : "Light")}");
            }

            if (!AdminHelper.IsAdministrator())
            {
                Logger.Error("Application not running as administrator");
                MessageBox.Show(
                    "This application requires administrator privileges.\n\nPlease right-click the executable and select 'Run as Administrator'.",
                    "Administrator Rights Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Logger.Info("Application shutting down due to insufficient privileges");
                Environment.Exit(1);
                return;
            }

            Logger.Info("Administrator privileges confirmed");

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            base.OnStartup(e);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Logger.Error($"Unhandled exception: {exception?.Message}\n{exception?.StackTrace}");

            MessageBox.Show(
                $"A critical error occurred:\n\n{exception?.Message}\n\nPlease check the log file for details.",
                "Critical Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Environment.Exit(1);
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error($"UI exception: {e.Exception.Message}\n{e.Exception.StackTrace}");

            MessageBox.Show(
                $"An error occurred:\n\n{e.Exception.Message}\n\nPlease check the log file for details.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
            Environment.Exit(1);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info($"Application exiting with code {e.ApplicationExitCode}");
            Logger.Shutdown();
            base.OnExit(e);
        }
    }
}