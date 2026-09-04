using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DSDeaths.Live {
    public partial class MainWindow : Window {
        private readonly DSDeathsMonitor monitor;
        private readonly LiveSettings settings;
        private readonly string settingsPath;
        private readonly LiveSettingsWarning[] startupWarnings;
        private readonly DispatcherTimer settingsSaveTimer;
        private readonly DispatcherTimer transientStatusTimer;
        private readonly DispatcherTimer offlineLaunchResetTimer;
        private Forms.NotifyIcon trayIcon;
        private System.Drawing.Icon trayAppIcon;
        private Forms.ToolStripMenuItem trayShowItem;
        private Forms.ToolStripMenuItem trayExitItem;
        private OverlayWindow overlayWindow;
        private MonitorSnapshot latestSnapshot;
        private bool initializing = true;
        private bool updatingControls;
        private bool allowClose;
        private bool columnBalancePending;
        private bool restoringOverlayPosition;
        private bool offlineLaunchPending;

        internal MainWindow(
            DSDeathsMonitor monitor,
            LiveSettings settings,
            string settingsPath,
            LiveSettingsWarning[] startupWarnings) {
            this.monitor = monitor;
            this.settings = settings;
            this.settingsPath = settingsPath;
            this.startupWarnings = startupWarnings;

            InitializeComponent();
            settingsSaveTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            settingsSaveTimer.Tick += SettingsSaveTimer_Tick;
            transientStatusTimer = new DispatcherTimer {
                Interval = TimeSpan.FromSeconds(2)
            };
            transientStatusTimer.Tick += TransientStatusTimer_Tick;
            offlineLaunchResetTimer = new DispatcherTimer {
                Interval = TimeSpan.FromSeconds(10)
            };
            offlineLaunchResetTimer.Tick += OfflineLaunchResetTimer_Tick;
            CreateTrayIcon();
            SelectConfiguredLanguage();
            PopulateFontFamilies();
            SelectOverlayTextShadow();
            SelectCloseButtonBehavior();
            OverlayScaleSlider.Value = settings.OverlayScalePercent;
            OverlayOpacitySlider.Value = settings.OverlayBackgroundOpacity;
            OverlayFontSizeSlider.Value = settings.OverlayFontSize;
            OverlayPositionLockedCheckBox.IsChecked = settings.OverlayPositionLocked;
            OverlayTopmostCheckBox.IsChecked = settings.OverlayTopmost;
            OverlayShowBorderCheckBox.IsChecked = settings.OverlayShowBorder;
            OverlayShowLabelCheckBox.IsChecked = settings.OverlayShowLabel;
            ApplyOverlayAppearance();
            ApplyLocalization();
            latestSnapshot = monitor.LatestSnapshot;
            ApplySnapshot(latestSnapshot);

            monitor.SnapshotChanged += Monitor_SnapshotChanged;
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            initializing = false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            monitor.Start();
            if (settings.OverlayVisible) {
                ShowOverlay();
            }

            string warning = CombineWarnings(
                SettingsWarningFormatter.Format(startupWarnings),
                SettingsWarningFormatter.Format(monitor.SettingsWarnings));
            if (!string.IsNullOrEmpty(warning)) {
                MessageBox.Show(this, warning, Localization.Get("ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ScheduleColumnBalance();
        }

        private void Monitor_SnapshotChanged(object sender, MonitorSnapshotEventArgs e) {
            try {
                Dispatcher.BeginInvoke(new Action(delegate {
                    latestSnapshot = e.Snapshot;
                    ApplySnapshot(e.Snapshot);
                }));
            } catch (InvalidOperationException) {
            }
        }

        private void ApplySnapshot(MonitorSnapshot snapshot) {
            DiagnosticLogger.WriteSnapshot(snapshot);
            bool hasGame = snapshot.Game != null;
            bool isEldenRing = hasGame && snapshot.Game.IsEldenRing;
            bool canEditOffset = isEldenRing && snapshot.State == MonitorState.Monitoring;
            string monitorMessage = MonitorMessageFormatter.Format(snapshot);

            GameText.Text = hasGame ? snapshot.Game.DisplayName : Localization.Get("NoGame");
            DeathCountText.Text = snapshot.HasDeathCount
                ? snapshot.DeathCount.ToString(CultureInfo.InvariantCulture)
                : "0";
            RawCountText.Text = snapshot.HasDeathCount
                ? Localization.Format("RawFormat", snapshot.RawDeathCount)
                : Localization.Get("RawUnavailable");

            UpdateEldenRingLaunchBanner(snapshot);
            OffsetPanel.IsEnabled = canEditOffset;
            OffsetOnlyBadge.Visibility = isEldenRing ? Visibility.Collapsed : Visibility.Visible;

            updatingControls = true;
            OffsetEnabledCheckBox.IsChecked = snapshot.OffsetEnabled;
            if (!BaselineTextBox.IsKeyboardFocused) {
                BaselineTextBox.Text = snapshot.Offset.ToString(CultureInfo.InvariantCulture);
            }
            updatingControls = false;

            bool clamped = isEldenRing && snapshot.HasDeathCount && snapshot.OffsetEnabled &&
                           snapshot.RawDeathCount < snapshot.Offset;
            ClampedWarning.Visibility = clamped ? Visibility.Visible : Visibility.Collapsed;

            OutputStatusText.Text = snapshot.OutputWriteSucceeded
                ? Localization.Get("ObsReady")
                : Localization.Get("ObsError");
            OutputStatusDot.Fill = BrushFromHex(snapshot.OutputWriteSucceeded ? "#42C77A" : "#E05B5B");
            OutputPathText.Text = Localization.Format("OutputPathLabel", monitor.OutputPath);

            switch (snapshot.State) {
                case MonitorState.Searching:
                    ConnectionText.Text = Localization.Get("Searching");
                    MonitorStatusText.Text = Localization.Get("Searching");
                    MonitorStatusDot.Fill = BrushFromHex("#84909D");
                    break;
                case MonitorState.Connecting:
                    ConnectionText.Text = Localization.Format("Connecting", snapshot.Game.DisplayName);
                    MonitorStatusText.Text = ConnectionText.Text;
                    MonitorStatusDot.Fill = BrushFromHex("#D5A847");
                    break;
                case MonitorState.Monitoring:
                    ConnectionText.Text = Localization.Format(
                        "Monitoring", snapshot.Game.DisplayName, snapshot.Is64Bit ? 64 : 32);
                    MonitorStatusText.Text = AppendMessage(ConnectionText.Text, monitorMessage);
                    MonitorStatusDot.Fill = BrushFromHex("#42C77A");
                    break;
                case MonitorState.Unsupported:
                    ConnectionText.Text = Localization.Get("Unsupported");
                    MonitorStatusText.Text = AppendMessage(ConnectionText.Text, monitorMessage);
                    MonitorStatusDot.Fill = BrushFromHex("#E05B5B");
                    break;
                case MonitorState.Error:
                    ConnectionText.Text = Localization.Get("MonitorError");
                    MonitorStatusText.Text = AppendMessage(ConnectionText.Text, monitorMessage);
                    MonitorStatusDot.Fill = BrushFromHex("#E05B5B");
                    break;
                case MonitorState.Stopped:
                    ConnectionText.Text = Localization.Get("Stopped");
                    MonitorStatusText.Text = Localization.Get("Stopped");
                    MonitorStatusDot.Fill = BrushFromHex("#84909D");
                    break;
            }

            if (!snapshot.OutputWriteSucceeded && !string.IsNullOrEmpty(snapshot.OutputError)) {
                MonitorStatusText.Text = AppendMessage(MonitorStatusText.Text, snapshot.OutputError);
            }

            if (overlayWindow != null) {
                overlayWindow.UpdateDeathCount(snapshot.HasDeathCount ? snapshot.DeathCount : 0);
            }
            ScheduleColumnBalance();
        }

        private void ApplyLocalization() {
            Title = Localization.Get("AppTitle");
            TitleText.Text = Localization.Get("AppTitle");
            SubtitleText.Text = Localization.Get("Subtitle");
            DeathsLabelText.Text = Localization.Get("DeathsLabel");
            ChooseEldenRingExecutableButton.Content = Localization.Get("OfflineLaunchChoose");
            LaunchEldenRingOfflineButton.Content = Localization.Get("OfflineLaunchButton");
            OffsetTitleText.Text = Localization.Get("OffsetTitle");
            OffsetOnlyText.Text = Localization.Get("OffsetOnly");
            OffsetEnabledCheckBox.Content = Localization.Get("OffsetEnable");
            BaselineLabelText.Text = Localization.Get("BaselineLabel");
            SetCurrentZeroButton.Content = Localization.Get("SetCurrentZero");
            ApplyOffsetButton.Content = Localization.Get("Apply");
            OffsetHelpText.Text = Localization.Get("OffsetHelp");
            ObsTitleText.Text = Localization.Get("ObsTitle");
            SettingsTitleText.Text = Localization.Get("SettingsTitle");
            ApplicationSettingsTitleText.Text = Localization.Get("ApplicationSettingsTitle");
            LanguageLabelText.Text = Localization.Get("LanguageLabel");
            CloseButtonBehaviorLabelText.Text = Localization.Get("CloseButtonBehavior");
            ((ComboBoxItem)CloseButtonBehaviorComboBox.Items[0]).Content = Localization.Get("CloseToTray");
            ((ComboBoxItem)CloseButtonBehaviorComboBox.Items[1]).Content = Localization.Get("CloseImmediately");
            OverlayAppearanceTitleText.Text = Localization.Get("OverlayAppearanceTitle");
            OverlayScaleLabelText.Text = Localization.Get("OverlayScale");
            BackgroundOpacityLabelText.Text = Localization.Get("BackgroundOpacity");
            TextColorLabelText.Text = Localization.Get("TextColor");
            FontFamilyLabelText.Text = Localization.Get("FontFamily");
            FontSizeLabelText.Text = Localization.Get("FontSize");
            TextShadowLabelText.Text = Localization.Get("TextShadow");
            OverlayPositionLockedCheckBox.Content = Localization.Get("OverlayPositionLocked");
            OverlayTopmostCheckBox.Content = Localization.Get("OverlayTopmost");
            OverlayShowBorderCheckBox.Content = Localization.Get("OverlayShowBorder");
            OverlayShowLabelCheckBox.Content = Localization.Get("OverlayShowLabel");
            ResetOverlayPositionButton.Content = Localization.Get("ResetOverlayPosition");
            OpenOutputFolderButton.Content = Localization.Get("OpenOutputFolder");
            CopyOutputPathButton.Content = Localization.Get("CopyOutputPath");
            CopyStatusButton.Content = Localization.Get("CopyStatus");
            ((ComboBoxItem)OverlayTextShadowComboBox.Items[0]).Content = Localization.Get("TextShadowNone");
            ((ComboBoxItem)OverlayTextShadowComboBox.Items[1]).Content = Localization.Get("TextShadowSoft");
            ((ComboBoxItem)OverlayTextShadowComboBox.Items[2]).Content = Localization.Get("TextShadowStrong");
            ClampedWarningText.Text = Localization.Get("ClampedWarning");

            ((ComboBoxItem)LanguageComboBox.Items[0]).Content = Localization.Get("LanguageAuto");
            ((ComboBoxItem)LanguageComboBox.Items[1]).Content = Localization.Get("LanguageJapanese");
            ((ComboBoxItem)LanguageComboBox.Items[2]).Content = Localization.Get("LanguageEnglish");

            if (trayIcon != null) {
                trayIcon.Text = Localization.Get("AppTitle");
                trayShowItem.Text = Localization.Get("TrayShow");
                trayExitItem.Text = Localization.Get("TrayExit");
            }
            if (overlayWindow != null) {
                overlayWindow.ApplyLocalization();
            }

            UpdateOverlayButtonText();
            UpdateEldenRingLaunchBanner(latestSnapshot);
            ApplyOverlayAppearance();
            if (latestSnapshot != null) {
                ApplySnapshot(latestSnapshot);
            } else {
                MonitorStatusText.Text = Localization.Get("StatusReady");
            }
            ScheduleColumnBalance();
        }

        private void ScheduleColumnBalance() {
            if (columnBalancePending || Dispatcher.HasShutdownStarted) {
                return;
            }

            columnBalancePending = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(delegate {
                    columnBalancePending = false;
                    BalanceColumnHeights();
                }));
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) {
            ScheduleColumnBalance();
        }

        private void BalanceColumnHeights() {
            if (!IsLoaded || LeftColumnPanel.DesiredSize.Height <= 0 ||
                RightColumnPanel.DesiredSize.Height <= 0 ||
                LeftColumnPanel.ActualWidth <= 0 || RightColumnPanel.ActualWidth <= 0) {
                return;
            }

            OverlayButton.ClearValue(FrameworkElement.HeightProperty);
            SettingsPanelsGrid.ClearValue(FrameworkElement.HeightProperty);
            LeftColumnPanel.Measure(new Size(LeftColumnPanel.ActualWidth, double.PositiveInfinity));
            RightColumnPanel.Measure(new Size(RightColumnPanel.ActualWidth, double.PositiveInfinity));

            double leftGrowth = OverlayGeometry.AdditionalHeight(
                LeftColumnPanel.DesiredSize.Height,
                RightColumnPanel.DesiredSize.Height);
            double rightGrowth = OverlayGeometry.AdditionalHeight(
                RightColumnPanel.DesiredSize.Height,
                LeftColumnPanel.DesiredSize.Height);
            if (leftGrowth > 0.5) {
                OverlayButton.Height = OverlayButton.DesiredSize.Height + leftGrowth;
            } else if (rightGrowth > 0.5) {
                SettingsPanelsGrid.Height = SettingsPanelsGrid.DesiredSize.Height + rightGrowth;
            }
        }

        private void UpdateEldenRingLaunchBanner(MonitorSnapshot snapshot) {
            bool hasGame = snapshot != null && snapshot.Game != null;
            bool isEldenRing = hasGame && snapshot.Game.IsEldenRing;
            bool showLauncher = !hasGame;

            EacWarning.Visibility = isEldenRing ? Visibility.Visible : Visibility.Collapsed;
            EldenRingLaunchActions.Visibility = showLauncher
                ? Visibility.Visible
                : Visibility.Collapsed;
            HeaderStatusPanel.Visibility = showLauncher
                ? Visibility.Collapsed
                : Visibility.Visible;

            EacWarningTitleText.Text = Localization.Get("EacWarningTitle");
            EacWarningBodyText.Text = Localization.Get("EacWarningText");
            EldenRingLaunchActions.ToolTip = Localization.Get("OfflineLaunchText");

            LaunchEldenRingOfflineButton.IsEnabled = showLauncher && !offlineLaunchPending;
            ChooseEldenRingExecutableButton.IsEnabled = showLauncher && !offlineLaunchPending;

            string configuredPath = settings.EldenRingExecutablePath;
            EldenRingExecutablePathText.Text = string.IsNullOrEmpty(configuredPath)
                ? Localization.Get("OfflineLaunchPathNotSet")
                : Localization.Format("OfflineLaunchPath", configuredPath);
            EldenRingExecutablePathText.ToolTip = string.IsNullOrEmpty(configuredPath)
                ? Localization.Get("OfflineLaunchPathUnset")
                : Localization.Format("OfflineLaunchPath", configuredPath);
            LaunchEldenRingOfflineButton.ToolTip = string.IsNullOrEmpty(configuredPath)
                ? Localization.Get("OfflineLaunchPathUnset")
                : Localization.Format("OfflineLaunchPath", configuredPath);
            ChooseEldenRingExecutableButton.ToolTip = Localization.Get("OfflineLaunchChooseHint");
        }

        private void ChooseEldenRingExecutableButton_Click(object sender, RoutedEventArgs e) {
            string selectedPath;
            if (!TryChooseEldenRingExecutable(out selectedPath)) {
                return;
            }

            settings.EldenRingExecutablePath = selectedPath;
            if (!SaveSettings()) {
                return;
            }

            UpdateEldenRingLaunchBanner(latestSnapshot);
            ShowTransientStatus(Localization.Get("OfflineLaunchPathSaved"));
        }

        private void LaunchEldenRingOfflineButton_Click(object sender, RoutedEventArgs e) {
            EldenRingOfflineLaunchErrorCode errorCode;
            string error;
            if (!EldenRingOfflineLauncher.TryEnsureGameNotRunning(
                    out errorCode,
                    out error)) {
                ShowError(FormatOfflineLaunchError(errorCode, error));
                return;
            }

            string executablePath = settings.EldenRingExecutablePath;
            string normalizedPath;
            if (!EldenRingOfflineLauncher.TryValidateExecutable(
                    executablePath,
                    out normalizedPath,
                    out errorCode,
                    out error)) {
                if (!TryChooseEldenRingExecutable(out normalizedPath)) {
                    return;
                }
                settings.EldenRingExecutablePath = normalizedPath;
                if (!SaveSettings()) {
                    return;
                }
            }

            string appIdFilePath = Path.Combine(
                Path.GetDirectoryName(normalizedPath),
                EldenRingOfflineLauncher.AppIdFileName);
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                Localization.Format(
                    "OfflineLaunchConfirm",
                    appIdFilePath,
                    EldenRingOfflineLauncher.SteamAppId,
                    normalizedPath),
                Localization.Get("OfflineLaunchConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) {
                return;
            }

            EldenRingOfflineLaunchPreparation preparation;
            if (!EldenRingOfflineLauncher.TryPrepare(
                    normalizedPath,
                    out preparation,
                    out errorCode,
                    out error)) {
                ShowError(FormatOfflineLaunchError(errorCode, error));
                return;
            }

            Process process;
            if (!EldenRingOfflineLauncher.TryStart(
                    preparation,
                    out process,
                    out errorCode,
                    out error)) {
                ShowError(FormatOfflineLaunchError(errorCode, error));
                return;
            }
            if (process != null) {
                process.Dispose();
            }

            offlineLaunchPending = true;
            offlineLaunchResetTimer.Stop();
            offlineLaunchResetTimer.Start();
            UpdateEldenRingLaunchBanner(latestSnapshot);
            DiagnosticLogger.Write(
                "Elden Ring offline launch requested; steam_appid.txt=" +
                (preparation.AppIdFileCreated ? "created" : "already-valid"));
            ShowTransientStatus(Localization.Get("OfflineLaunchStarted"));
        }

        private bool TryChooseEldenRingExecutable(out string selectedPath) {
            selectedPath = null;
            var dialog = new Microsoft.Win32.OpenFileDialog {
                Title = Localization.Get("OfflineLaunchDialogTitle"),
                Filter = Localization.Get("OfflineLaunchDialogFilter"),
                FileName = EldenRingOfflineLauncher.ExecutableFileName,
                CheckFileExists = true,
                Multiselect = false
            };

            string configuredPath = settings.EldenRingExecutablePath;
            if (!string.IsNullOrEmpty(configuredPath)) {
                try {
                    string configuredDirectory = Path.GetDirectoryName(
                        Path.GetFullPath(configuredPath));
                    if (Directory.Exists(configuredDirectory)) {
                        dialog.InitialDirectory = configuredDirectory;
                    }
                } catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is NotSupportedException ||
                    exception is PathTooLongException ||
                    exception is System.Security.SecurityException) {
                }
            } else {
                string defaultPath = EldenRingOfflineLauncher.FindDefaultExecutablePath();
                if (!string.IsNullOrEmpty(defaultPath)) {
                    dialog.InitialDirectory = Path.GetDirectoryName(defaultPath);
                }
            }

            if (dialog.ShowDialog(this) != true) {
                return false;
            }

            EldenRingOfflineLaunchErrorCode errorCode;
            string error;
            if (!EldenRingOfflineLauncher.TryValidateExecutable(
                    dialog.FileName,
                    out selectedPath,
                    out errorCode,
                    out error)) {
                ShowError(FormatOfflineLaunchError(errorCode, error));
                selectedPath = null;
                return false;
            }
            return true;
        }

        private static string FormatOfflineLaunchError(
            EldenRingOfflineLaunchErrorCode errorCode,
            string error) {
            switch (errorCode) {
                case EldenRingOfflineLaunchErrorCode.WrongExecutableName:
                    return Localization.Get("OfflineLaunchWrongExecutable");
                case EldenRingOfflineLaunchErrorCode.ExecutableNotFound:
                    return Localization.Format("OfflineLaunchExecutableMissing", error);
                case EldenRingOfflineLaunchErrorCode.AppIdFileConflict:
                    return Localization.Format("OfflineLaunchAppIdConflict", error);
                case EldenRingOfflineLaunchErrorCode.AppIdFileReadFailed:
                    return Localization.Format("OfflineLaunchFileReadFailed", error);
                case EldenRingOfflineLaunchErrorCode.AppIdFileWriteFailed:
                    return Localization.Format("OfflineLaunchFileWriteFailed", error);
                case EldenRingOfflineLaunchErrorCode.ProcessCheckFailed:
                    return Localization.Format("OfflineLaunchProcessCheckFailed", error);
                case EldenRingOfflineLaunchErrorCode.AlreadyRunning:
                    return Localization.Get("OfflineLaunchAlreadyRunning");
                case EldenRingOfflineLaunchErrorCode.ProcessStartFailed:
                    return Localization.Format("OfflineLaunchStartFailed", error);
                default:
                    return Localization.Format("OfflineLaunchInvalidPath", error);
            }
        }

        private void OfflineLaunchResetTimer_Tick(object sender, EventArgs e) {
            offlineLaunchResetTimer.Stop();
            offlineLaunchPending = false;
            UpdateEldenRingLaunchBanner(latestSnapshot);
        }

        private void OffsetEnabledCheckBox_Click(object sender, RoutedEventArgs e) {
            if (updatingControls) {
                return;
            }

            string error;
            MonitorOperationErrorCode errorCode;
            bool enabled = OffsetEnabledCheckBox.IsChecked == true;
            if (!monitor.TrySetOffsetEnabled(enabled, out error, out errorCode)) {
                ShowError(MonitorOperationErrorFormatter.Format(errorCode, error));
                ApplySnapshot(monitor.LatestSnapshot);
            }
        }

        private void SetCurrentZeroButton_Click(object sender, RoutedEventArgs e) {
            string error;
            MonitorOperationErrorCode errorCode;
            if (!monitor.TrySetCurrentAsZero(out error, out errorCode)) {
                ShowError(MonitorOperationErrorFormatter.Format(errorCode, error));
                return;
            }
            MonitorStatusText.Text = Localization.Get("CurrentZeroSuccess");
        }

        private void ApplyOffsetButton_Click(object sender, RoutedEventArgs e) {
            int offset;
            if (!int.TryParse(
                    BaselineTextBox.Text.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out offset) || offset < 0) {
                ShowError(Localization.Get("InvalidBaseline"));
                return;
            }

            string error;
            MonitorOperationErrorCode errorCode;
            if (!monitor.TrySetOffset(offset, out error, out errorCode)) {
                ShowError(MonitorOperationErrorFormatter.Format(errorCode, error));
                return;
            }
            MonitorStatusText.Text = Localization.Get("BaselineSaved");
        }

        private void OverlayButton_Click(object sender, RoutedEventArgs e) {
            if (overlayWindow != null && overlayWindow.IsVisible) {
                HideOverlay();
            } else {
                ShowOverlay();
            }
        }

        private void ShowOverlay() {
            if (overlayWindow == null) {
                overlayWindow = new OverlayWindow();
                overlayWindow.HideRequested += OverlayWindow_HideRequested;
                overlayWindow.LocationChanged += OverlayWindow_LocationChanged;
                overlayWindow.Closed += delegate { overlayWindow = null; };
            }

            overlayWindow.ApplyLocalization();
            overlayWindow.ApplyAppearance(
                settings.OverlayBackgroundOpacity,
                settings.OverlayTextColor,
                settings.OverlayFontFamily,
                settings.OverlayFontSize,
                settings.OverlayTextShadow,
                settings.OverlayScalePercent);
            ApplyOverlayBehavior();
            overlayWindow.UpdateDeathCount(
                latestSnapshot != null && latestSnapshot.HasDeathCount ? latestSnapshot.DeathCount : 0);
            restoringOverlayPosition = true;
            try {
                overlayWindow.Show();
                if (settings.OverlayPositionSet) {
                    overlayWindow.RestorePosition(settings.OverlayLeft, settings.OverlayTop);
                }
            } finally {
                restoringOverlayPosition = false;
            }
            SaveCurrentOverlayPosition();
            overlayWindow.Activate();
            settings.OverlayVisible = true;
            SaveSettings();
            UpdateOverlayButtonText();
        }

        private void HideOverlay() {
            if (overlayWindow != null) {
                overlayWindow.Hide();
            }
            settings.OverlayVisible = false;
            SaveSettings();
            UpdateOverlayButtonText();
        }

        private void OverlayWindow_HideRequested(object sender, EventArgs e) {
            HideOverlay();
        }

        private void OverlayWindow_LocationChanged(object sender, EventArgs e) {
            if (restoringOverlayPosition || overlayWindow == null || !overlayWindow.IsLoaded) {
                return;
            }

            SaveCurrentOverlayPosition();
            ScheduleSettingsSave();
        }

        private void SaveCurrentOverlayPosition() {
            if (overlayWindow == null || !overlayWindow.IsLoaded ||
                double.IsNaN(overlayWindow.Left) || double.IsNaN(overlayWindow.Top)) {
                return;
            }

            settings.OverlayPositionSet = true;
            settings.OverlayLeft = overlayWindow.Left;
            settings.OverlayTop = overlayWindow.Top;
        }

        private void UpdateOverlayButtonText() {
            OverlayButton.Content = overlayWindow != null && overlayWindow.IsVisible
                ? Localization.Get("HideOverlay")
                : Localization.Get("ShowOverlay");
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (initializing || LanguageComboBox.SelectedItem == null) {
                return;
            }

            var item = (ComboBoxItem)LanguageComboBox.SelectedItem;
            settings.Language = item.Tag.ToString();
            Localization.SetLanguage(settings.Language);
            ApplyLocalization();
            SaveSettings();
        }

        private void CloseButtonBehaviorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (initializing || CloseButtonBehaviorComboBox.SelectedItem == null) {
                return;
            }

            var item = (ComboBoxItem)CloseButtonBehaviorComboBox.SelectedItem;
            settings.MinimizeToTray = string.Equals(
                item.Tag.ToString(),
                "tray",
                StringComparison.OrdinalIgnoreCase);
            SaveSettings();
        }

        private void SelectCloseButtonBehavior() {
            string behavior = settings.MinimizeToTray ? "tray" : "exit";
            foreach (ComboBoxItem item in CloseButtonBehaviorComboBox.Items) {
                if (string.Equals(item.Tag.ToString(), behavior, StringComparison.OrdinalIgnoreCase)) {
                    CloseButtonBehaviorComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (initializing) {
                return;
            }

            settings.OverlayBackgroundOpacity = (int)Math.Round(e.NewValue);
            ApplyOverlayAppearance();
            ScheduleSettingsSave();
        }

        private void OverlayScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (initializing) {
                return;
            }

            settings.OverlayScalePercent = (int)Math.Round(e.NewValue);
            ApplyOverlayAppearance();
            ScheduleSettingsSave();
        }

        private void TextColorButton_Click(object sender, RoutedEventArgs e) {
            System.Drawing.Color initialColor;
            try {
                initialColor = System.Drawing.ColorTranslator.FromHtml(settings.OverlayTextColor);
            } catch (Exception) {
                initialColor = System.Drawing.Color.White;
            }

            using (var dialog = new Forms.ColorDialog {
                Color = initialColor,
                FullOpen = true,
                AnyColor = true
            }) {
                if (dialog.ShowDialog() != Forms.DialogResult.OK) {
                    return;
                }

                settings.OverlayTextColor = string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0:X2}{1:X2}{2:X2}",
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B);
            }

            ApplyOverlayAppearance();
            SaveSettings();
        }

        private void OverlayFontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (initializing || OverlayFontFamilyComboBox.SelectedItem == null) {
                return;
            }

            settings.OverlayFontFamily = OverlayFontFamilyComboBox.SelectedItem.ToString();
            ApplyOverlayAppearance();
            SaveSettings();
        }

        private void OverlayFontSizeSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e) {
            if (initializing) {
                return;
            }

            settings.OverlayFontSize = (int)Math.Round(e.NewValue);
            ApplyOverlayAppearance();
            ScheduleSettingsSave();
        }

        private void OverlayTextShadowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (initializing || OverlayTextShadowComboBox.SelectedItem == null) {
                return;
            }

            var item = (ComboBoxItem)OverlayTextShadowComboBox.SelectedItem;
            settings.OverlayTextShadow = item.Tag.ToString();
            ApplyOverlayAppearance();
            SaveSettings();
        }

        private void OverlayPositionLockedCheckBox_Click(object sender, RoutedEventArgs e) {
            if (initializing) {
                return;
            }

            settings.OverlayPositionLocked = OverlayPositionLockedCheckBox.IsChecked == true;
            ApplyOverlayBehavior();
            SaveSettings();
        }

        private void OverlayTopmostCheckBox_Click(object sender, RoutedEventArgs e) {
            if (initializing) {
                return;
            }

            settings.OverlayTopmost = OverlayTopmostCheckBox.IsChecked == true;
            ApplyOverlayBehavior();
            SaveSettings();
        }

        private void OverlayShowBorderCheckBox_Click(object sender, RoutedEventArgs e) {
            if (initializing) {
                return;
            }

            settings.OverlayShowBorder = OverlayShowBorderCheckBox.IsChecked == true;
            ApplyOverlayBehavior();
            SaveSettings();
        }

        private void OverlayShowLabelCheckBox_Click(object sender, RoutedEventArgs e) {
            if (initializing) {
                return;
            }

            settings.OverlayShowLabel = OverlayShowLabelCheckBox.IsChecked == true;
            ApplyOverlayBehavior();
            SaveSettings();
        }

        private void ResetOverlayPositionButton_Click(object sender, RoutedEventArgs e) {
            ShowOverlay();
            restoringOverlayPosition = true;
            try {
                overlayWindow.CenterOn(this);
            } finally {
                restoringOverlayPosition = false;
            }
            SaveCurrentOverlayPosition();
            SaveSettings();
        }

        private void ApplyOverlayAppearance() {
            OverlayScaleValueText.Text = Localization.Format(
                "OverlayScaleValueFormat",
                settings.OverlayScalePercent);
            OpacityValueText.Text = Localization.Format(
                "OpacityValueFormat",
                settings.OverlayBackgroundOpacity);
            TextColorValueText.Text = settings.OverlayTextColor;
            TextColorSwatch.Background = BrushFromHex(settings.OverlayTextColor);
            FontSizeValueText.Text = Localization.Format("FontSizeValueFormat", settings.OverlayFontSize);

            if (overlayWindow != null) {
                overlayWindow.ApplyAppearance(
                    settings.OverlayBackgroundOpacity,
                    settings.OverlayTextColor,
                    settings.OverlayFontFamily,
                    settings.OverlayFontSize,
                    settings.OverlayTextShadow,
                    settings.OverlayScalePercent);
            }
        }

        private void ApplyOverlayBehavior() {
            if (overlayWindow == null) {
                return;
            }

            overlayWindow.ApplyBehavior(
                settings.OverlayPositionLocked,
                settings.OverlayShowBorder,
                settings.OverlayShowLabel,
                settings.OverlayTopmost);
        }

        private void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e) {
            try {
                string directory = Path.GetDirectoryName(monitor.OutputPath);
                Process.Start(new ProcessStartInfo {
                    FileName = directory,
                    UseShellExecute = true
                });
            } catch (Exception exception) when (
                exception is InvalidOperationException ||
                exception is Win32Exception ||
                exception is IOException) {
                ShowError(Localization.Format("OpenOutputFolderError", exception.Message));
            }
        }

        private void CopyOutputPathButton_Click(object sender, RoutedEventArgs e) {
            CopyText(monitor.OutputPath, "OutputPathCopied");
        }

        private void CopyStatusButton_Click(object sender, RoutedEventArgs e) {
            CopyText(BuildDiagnosticSummary(), "StatusCopied");
        }

        private string BuildDiagnosticSummary() {
            MonitorSnapshot snapshot = latestSnapshot;
            string game = snapshot != null && snapshot.Game != null
                ? snapshot.Game.DisplayName
                : Localization.Get("NoGame");
            string state = string.IsNullOrEmpty(ConnectionText.Text)
                ? Localization.Get("Stopped")
                : ConnectionText.Text;
            return Localization.Format(
                "DiagnosticSummary",
                typeof(MainWindow).Assembly.GetName().Version,
                game,
                state,
                MonitorStatusText.Text,
                monitor.OutputPath,
                DiagnosticLogger.LogPath);
        }

        private void CopyText(string text, string successKey) {
            try {
                Clipboard.SetText(text ?? string.Empty);
                ShowTransientStatus(Localization.Get(successKey));
            } catch (ExternalException exception) {
                ShowError(Localization.Format("ClipboardError", exception.Message));
            }
        }

        private void ShowTransientStatus(string message) {
            MonitorStatusText.Text = message;
            MonitorStatusDot.Fill = BrushFromHex("#42C77A");
            transientStatusTimer.Stop();
            transientStatusTimer.Start();
        }

        private void TransientStatusTimer_Tick(object sender, EventArgs e) {
            transientStatusTimer.Stop();
            if (latestSnapshot != null) {
                ApplySnapshot(latestSnapshot);
            }
        }

        private void SelectOverlayTextShadow() {
            foreach (ComboBoxItem item in OverlayTextShadowComboBox.Items) {
                if (string.Equals(
                        item.Tag.ToString(),
                        settings.OverlayTextShadow,
                        StringComparison.OrdinalIgnoreCase)) {
                    OverlayTextShadowComboBox.SelectedItem = item;
                    return;
                }
            }
            OverlayTextShadowComboBox.SelectedIndex = 1;
        }

        private void PopulateFontFamilies() {
            string[] fontNames = Fonts.SystemFontFamilies
                .Select(font => font.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            string selectedFont = fontNames.FirstOrDefault(name =>
                string.Equals(name, settings.OverlayFontFamily, StringComparison.OrdinalIgnoreCase));
            if (selectedFont == null) {
                selectedFont = fontNames.FirstOrDefault(name =>
                    string.Equals(name, "Segoe UI", StringComparison.OrdinalIgnoreCase)) ?? fontNames.First();
                settings.OverlayFontFamily = selectedFont;
            }

            OverlayFontFamilyComboBox.ItemsSource = fontNames;
            OverlayFontFamilyComboBox.SelectedItem = selectedFont;
        }

        private void ScheduleSettingsSave() {
            settingsSaveTimer.Stop();
            settingsSaveTimer.Start();
        }

        private void SettingsSaveTimer_Tick(object sender, EventArgs e) {
            settingsSaveTimer.Stop();
            SaveSettings();
        }

        private void SelectConfiguredLanguage() {
            foreach (ComboBoxItem item in LanguageComboBox.Items) {
                if (string.Equals(item.Tag.ToString(), settings.Language, StringComparison.OrdinalIgnoreCase)) {
                    LanguageComboBox.SelectedItem = item;
                    return;
                }
            }
            LanguageComboBox.SelectedIndex = 0;
        }

        private void CreateTrayIcon() {
            trayShowItem = new Forms.ToolStripMenuItem();
            trayShowItem.Click += delegate { Dispatcher.BeginInvoke(new Action(ShowFromTray)); };
            trayExitItem = new Forms.ToolStripMenuItem();
            trayExitItem.Click += delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(trayShowItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(trayExitItem);

            trayAppIcon = System.Drawing.Icon.ExtractAssociatedIcon(typeof(MainWindow).Assembly.Location);
            trayIcon = new Forms.NotifyIcon {
                Icon = trayAppIcon ?? System.Drawing.SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowFromTray)); };
        }

        private void ShowFromTray() {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApplication() {
            allowClose = true;
            Close();
        }

        protected override void OnStateChanged(EventArgs e) {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized && settings.MinimizeToTray) {
                Hide();
            }
        }

        protected override void OnClosing(CancelEventArgs e) {
            if (!allowClose && settings.MinimizeToTray) {
                e.Cancel = true;
                Hide();
                return;
            }
            allowClose = true;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e) {
            monitor.SnapshotChanged -= Monitor_SnapshotChanged;
            settingsSaveTimer.Stop();
            transientStatusTimer.Stop();
            offlineLaunchResetTimer.Stop();
            SaveSettings();
            if (overlayWindow != null) {
                overlayWindow.Close();
                overlayWindow = null;
            }
            if (trayIcon != null) {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (trayAppIcon != null) {
                trayAppIcon.Dispose();
                trayAppIcon = null;
            }
            base.OnClosed(e);
        }

        private bool SaveSettings() {
            settingsSaveTimer.Stop();
            string error;
            if (!settings.TrySave(settingsPath, out error)) {
                MonitorStatusText.Text = Localization.Format("SettingsSaveError", error);
                MonitorStatusDot.Fill = BrushFromHex("#E05B5B");
                return false;
            }
            return true;
        }

        private void ShowError(string message) {
            MessageBox.Show(this, message, Localization.Get("ErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string AppendMessage(string first, string second) {
            return string.IsNullOrEmpty(second) ? first : first + " · " + second;
        }

        private static string CombineWarnings(string first, string second) {
            if (string.IsNullOrEmpty(first)) {
                return second;
            }
            if (string.IsNullOrEmpty(second)) {
                return first;
            }
            return first + Environment.NewLine + second;
        }

        private static SolidColorBrush BrushFromHex(string color) {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }
}
