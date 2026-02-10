using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GameRegionGuard.Models;
using GameRegionGuard.Services;

namespace GameRegionGuard
{
    public partial class MainWindow
    {
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isOperationRunning;

        private volatile bool _isClosing;

        private bool _isDarkTheme;

        public MainWindow()
        {
            InitializeComponent();
            Logger.LogEntryAdded += OnLogEntryAdded;

            // Reflect the user's last chosen theme.
            _isDarkTheme = AppSettingsService.IsDarkTheme;
            UpdateThemeButtonContent();
            Logger.Info("Main window initialized");
        }

        private void OnBlockingModeChanged(object sender, RoutedEventArgs e)
        {
            if (RadioSpecificApp == null || AppPathPanel == null) return;

            if (RadioSpecificApp.IsChecked == true)
            {
                AppPathPanel.IsEnabled = true;
                Logger.Debug("Switched to Specific Application mode");
            }
            else
            {
                AppPathPanel.IsEnabled = false;
                if (TxtAppPath != null) TxtAppPath.Text = string.Empty;
                Logger.Debug("Switched to System-wide mode");
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Game Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtAppPath.Text = dialog.FileName;
                Logger.Info($"Selected application: {dialog.FileName}");
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Install button clicked");

            var mode = RadioSystemWide.IsChecked == true
                ? BlockingMode.SystemWide
                : BlockingMode.SpecificApplication;

            string appPath = null;

            if (mode == BlockingMode.SpecificApplication)
            {
                appPath = TxtAppPath.Text;

                if (string.IsNullOrWhiteSpace(appPath))
                {
                    MessageBox.Show(
                        "Please select an application executable.",
                        "No Application Selected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Logger.Warning("Installation cancelled: No application selected");
                    return;
                }

                if (!File.Exists(appPath))
                {
                    MessageBox.Show(
                        $"The selected file does not exist:\n\n{appPath}",
                        "File Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Logger.Error($"Installation cancelled: File not found - {appPath}");
                    return;
                }
            }

            var confirmMessage = mode == BlockingMode.SystemWide
                ? "This will block all Russian IP ranges system-wide.\n\n" +
                  "This means you may not be able to access services hosted in that region.\n\n" +
                  "This may take a few minutes to process.\n\n" +
                  "Do you want to continue?"
                : $"This will block Russian IP ranges only for:\n\n{Path.GetFileName(appPath)}\n\n" +
                  "This may take a few minutes to process.\n\n" +
                  "Do you want to continue?";

            var result = MessageBox.Show(
                confirmMessage,
                "Confirm Installation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                Logger.Info("Installation cancelled by user at confirmation");
                return;
            }

            await RunInstallationAsync(mode, appPath);
        }

        private async Task RunInstallationAsync(BlockingMode mode, string appPath)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _isOperationRunning = true;
            SetUIState(false);

            try
            {
                Logger.Info($"Starting installation - Mode: {mode}, Path: {appPath ?? "N/A"}");

                UpdateProgress(0, "Downloading Russian IP ranges...");

                var downloadService = new IPDownloadService();
                var ipRanges = await downloadService.DownloadIPRangesAsync(_cancellationTokenSource.Token);

                if (ipRanges == null || ipRanges.Count == 0)
                {
                    MessageBox.Show(
                        "Failed to download IP ranges. Please check your internet connection.",
                        "Download Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Logger.Error("Installation failed: No IP ranges downloaded");
                    return;
                }

                Logger.Info($"Downloaded {ipRanges.Count} IP ranges");

                var firewallService = new FirewallService();
                var progress = new Progress<(int current, int total, string message)>(report =>
                {
                    try
                    {
                        var percent = report.total > 0
                            ? (int)((double)report.current / report.total * 100)
                            : 0;
                        UpdateProgress(percent, report.message);
                    }
                    catch
                    {
                        // Ignore UI progress failures.
                    }
                });

                UpdateProgress(5, $"Creating {ipRanges.Count} firewall rules...");

                var installResult = await firewallService.InstallRulesAsync(
                    ipRanges,
                    mode,
                    appPath,
                    progress,
                    _cancellationTokenSource.Token);

                if (installResult.WasCancelled)
                {
                    Logger.Warning($"Installation cancelled. Completed: {installResult.SuccessCount}/{installResult.TotalRules}");
                    UpdateProgress(100, $"Installation cancelled ({installResult.SuccessCount}/{installResult.TotalRules})");
                    if (!_isClosing)
                    {
                        MessageBox.Show(
                            $"Installation was cancelled.\n\n" +
                            $"Rules created: {installResult.SuccessCount}\n" +
                            $"Skipped (already existed): {installResult.SkippedCount}\n" +
                            $"Cancelled before completion.",
                            "Installation Cancelled",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else if (installResult.FailedCount == 0)
                {
                    Logger.Info($"Installation completed successfully. Created: {installResult.SuccessCount}, Skipped: {installResult.SkippedCount}");
                    if (!_isClosing)
                    {
                        MessageBox.Show(
                            $"Installation completed successfully!\n\n" +
                            $"Rules created: {installResult.SuccessCount}\n" +
                            $"Skipped (already existed): {installResult.SkippedCount}\n\n" +
                            $"Blocking rules have been installed.",
                            "Installation Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    UpdateProgress(100, "Installation complete");
                }
                else
                {
                    Logger.Warning($"Installation completed with errors. Success: {installResult.SuccessCount}, Failed: {installResult.FailedCount}");
                    if (!_isClosing)
                    {
                        MessageBox.Show(
                            $"Installation completed with some errors.\n\n" +
                            $"Rules created: {installResult.SuccessCount}\n" +
                            $"Skipped (already existed): {installResult.SkippedCount}\n" +
                            $"Failed: {installResult.FailedCount}\n\n" +
                            $"Check the log for details.",
                            "Installation Complete with Warnings",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    UpdateProgress(100, $"Complete: {installResult.SuccessCount} created, {installResult.FailedCount} failed");
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Installation cancelled via cancellation token");
                UpdateProgress(100, "Installation cancelled");
            }
            catch (Exception ex)
            {
                Logger.Error($"Installation error: {ex.Message}\n{ex.StackTrace}");
                if (!_isClosing)
                {
                    MessageBox.Show(
                        $"Installation failed:\n\n{ex.Message}\n\nCheck the log file for details.",
                        "Installation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    UpdateProgress(0, "Installation failed");
                }
            }
            finally
            {
                _isOperationRunning = false;
                SetUIState(true);
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Remove button clicked");

            // Ask for removal mode
            var modeResult = MessageBox.Show(
                "What do you want to remove?\n\n" +
                "YES = Remove ALL blocking rules (system-wide and all applications)\n" +
                "NO = Remove rules for a specific application only",
                "Select Removal Mode",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (modeResult == MessageBoxResult.Cancel)
            {
                Logger.Info("Removal cancelled at mode selection");
                return;
            }

            BlockingMode removeMode;
            string appPath = null;

            if (modeResult == MessageBoxResult.Yes)
            {
                removeMode = BlockingMode.SystemWide;
                Logger.Info("User selected system-wide removal");
            }
            else
            {
                removeMode = BlockingMode.SpecificApplication;

                var dialog = new OpenFileDialog
                {
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    Title = "Select Application to Remove Rules For"
                };

                if (dialog.ShowDialog() != true)
                {
                    Logger.Info("Removal cancelled: No application selected");
                    return;
                }

                appPath = dialog.FileName;
                Logger.Info($"User selected application for removal: {appPath}");
            }

            var confirmMessage = removeMode == BlockingMode.SystemWide
                ? "This will remove ALL Russian IP blocking rules (system-wide and all application-specific rules).\n\nAre you sure?"
                : $"This will remove all Russian IP blocking rules for:\n\n{Path.GetFileName(appPath)}\n\nAre you sure?";

            var confirmResult = MessageBox.Show(
                confirmMessage,
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
            {
                Logger.Info("Removal cancelled by user at confirmation");
                return;
            }

            await RunRemovalAsync(removeMode, appPath);
        }

        private async Task RunRemovalAsync(BlockingMode mode, string appPath)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _isOperationRunning = true;
            SetUIState(false);

            try
            {
                Logger.Info($"Starting rule removal - Mode: {mode}, Path: {appPath ?? "N/A"}");

                var firewallService = new FirewallService();
                var progress = new Progress<(int current, int total, string message)>(report =>
                {
                    var percent = report.total > 0
                        ? (int)((double)report.current / report.total * 100)
                        : 0;
                    UpdateProgress(percent, report.message);
                });

                UpdateProgress(0, "Searching for firewall rules to remove...");

                var removeResult = await firewallService.RemoveRulesAsync(
                    mode,
                    appPath,
                    progress,
                    _cancellationTokenSource.Token);

                if (removeResult.SuccessCount == 0)
                {
                    Logger.Info("No rules found to remove");
                    if (!_isClosing)
                    {
                        MessageBox.Show(
                            "No blocking rules were found to remove.",
                            "Nothing to Remove",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else if (removeResult.FailedCount == 0)
                {
                    Logger.Info($"Removal completed successfully. Removed: {removeResult.SuccessCount}");
                    if (!_isClosing)
                    {
                        MessageBox.Show(
                            $"Removal completed successfully!\n\nRules removed: {removeResult.SuccessCount}",
                            "Removal Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    Logger.Warning($"Removal completed with errors. Success: {removeResult.SuccessCount}, Failed: {removeResult.FailedCount}");
                    if (!_isClosing)
                    {
                        MessageBox.Show(
                            $"Removal completed with some errors.\n\n" +
                            $"Removed: {removeResult.SuccessCount}\n" +
                            $"Failed: {removeResult.FailedCount}\n\n" +
                            $"Check the log for details.",
                            "Removal Complete with Warnings",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                UpdateProgress(100, "Removal complete");
            }
            catch (Exception ex)
            {
                Logger.Error($"Removal error: {ex.Message}\n{ex.StackTrace}");
                if (!_isClosing)
                {
                    MessageBox.Show(
                        $"Removal failed:\n\n{ex.Message}\n\nCheck the log file for details.",
                        "Removal Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    UpdateProgress(0, "Removal failed");
                }
            }
            finally
            {
                _isOperationRunning = false;
                SetUIState(true);
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to cancel the operation?\n\n" +
                    "Rules created so far will remain in place.",
                    "Confirm Cancellation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    Logger.Warning("User requested cancellation");
                    _cancellationTokenSource.Cancel();
                    BtnCancel.IsEnabled = false;
                    UpdateProgress(ProgressBar?.Value ?? 0, "Cancelling...");
                }
            }
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtLog.Text);
                Logger.Info("Logs copied to clipboard");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to copy logs: {ex.Message}");
                MessageBox.Show(
                    $"Failed to copy logs:\n\n{ex.Message}",
                    "Copy Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnSaveLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Log Files (*.log)|*.log|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    FileName = $"GameRegionGuard_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                    Title = "Save Log File"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, TxtLog.Text);
                    Logger.Info($"Logs saved to: {dialog.FileName}");
                    MessageBox.Show(
                        $"Logs saved to:\n{dialog.FileName}",
                        "Saved",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save logs: {ex.Message}");
                MessageBox.Show(
                    $"Failed to save logs:\n\n{ex.Message}",
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SafeUI(Action action)
        {
            if (_isClosing) return;

            try
            {
                if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                if (Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Dispatcher.BeginInvoke(action);
                }
            }
            catch (InvalidOperationException)
            {
                // Dispatcher is unavailable (shutdown in progress).
            }
            catch (TaskCanceledException)
            {
                // Ignored.
            }
        }

        private void SetUIState(bool enabled)
        {
            SafeUI(() =>
            {
                if (BtnInstall != null) BtnInstall.IsEnabled = enabled;
                if (BtnRemove != null) BtnRemove.IsEnabled = enabled;
                if (BtnBrowse != null) BtnBrowse.IsEnabled = enabled && RadioSpecificApp?.IsChecked == true;
                if (RadioSystemWide != null) RadioSystemWide.IsEnabled = enabled;
                if (RadioSpecificApp != null) RadioSpecificApp.IsEnabled = enabled;
                if (BtnCancel != null) BtnCancel.IsEnabled = !enabled;
            });
        }

        private void UpdateProgress(double percent, string message)
        {
            var clamped = Math.Max(0, Math.Min(100, percent));

            SafeUI(() =>
            {
                if (ProgressBar != null) ProgressBar.Value = clamped;
                if (TxtProgressPercent != null) TxtProgressPercent.Text = $"{clamped:F0}%";
                if (TxtProgressStatus != null) TxtProgressStatus.Text = message;
            });
        }

        private void OnLogEntryAdded(object sender, string logMessage)
        {
            SafeUI(() =>
            {
                if (TxtLog == null) return;
                TxtLog.AppendText(logMessage + Environment.NewLine);

                // The TextBox is hosted inside an outer ScrollViewer for styling.
                // Scroll the container so the latest log line is always visible.
                LogScrollViewer?.ScrollToBottom();
            });
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            var newDark = !_isDarkTheme;
            if (!ThemeService.ApplyTheme(newDark))
            {
                Logger.Warning("Theme switch failed.");
                return;
            }

            _isDarkTheme = newDark;
            AppSettingsService.SetTheme(newDark);
            AppSettingsService.Save();

            UpdateThemeButtonContent();
            Logger.Info($"Theme switched to {(newDark ? "Dark" : "Light")}");
        }

        private void UpdateThemeButtonContent()
        {
            if (BtnToggleTheme == null) return;
            BtnToggleTheme.Content = _isDarkTheme ? "White Theme" : "Dark Theme";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _isClosing = true;

            // Prevent late UI updates from background tasks during shutdown.
            if (_isOperationRunning && _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    Logger.Warning("Application closing - cancelling running operation");
                    _cancellationTokenSource.Cancel();
                }
                catch
                {
                    // Ignored.
                }
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger.LogEntryAdded -= OnLogEntryAdded;
            base.OnClosed(e);
        }
    }
}