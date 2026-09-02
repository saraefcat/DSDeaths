using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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
        private readonly string startupWarning;
        private readonly DispatcherTimer settingsSaveTimer;
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

        public MainWindow(
            DSDeathsMonitor monitor,
            LiveSettings settings,
            string settingsPath,
            string startupWarning) {
            this.monitor = monitor;
            this.settings = settings;
            this.settingsPath = settingsPath;
            this.startupWarning = startupWarning;

            InitializeComponent();
            settingsSaveTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            settingsSaveTimer.Tick += SettingsSaveTimer_Tick;
            CreateTrayIcon();
            SelectConfiguredLanguage();
            PopulateFontFamilies();
            SelectCloseButtonBehavior();
            OverlayScaleSlider.Value = settings.OverlayScalePercent;
            OverlayOpacitySlider.Value = settings.OverlayBackgroundOpacity;
            OverlayFontSizeSlider.Value = settings.OverlayFontSize;
            ApplyOverlayAppearance();
            ApplyLocalization();
            latestSnapshot = monitor.LatestSnapshot;
            ApplySnapshot(latestSnapshot);

            monitor.SnapshotChanged += Monitor_SnapshotChanged;
            Loaded += MainWindow_Loaded;
            initializing = false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            monitor.Start();
            if (settings.OverlayVisible) {
                ShowOverlay();
            }

            string warning = CombineWarnings(startupWarning, monitor.SettingsWarning);
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
            bool hasGame = snapshot.Game != null;
            bool isEldenRing = hasGame && snapshot.Game.IsEldenRing;
            bool canEditOffset = isEldenRing && snapshot.State == MonitorState.Monitoring;

            GameText.Text = hasGame ? snapshot.Game.DisplayName : Localization.Get("NoGame");
            DeathCountText.Text = snapshot.HasDeathCount
                ? snapshot.DeathCount.ToString(CultureInfo.InvariantCulture)
                : "0";
            RawCountText.Text = snapshot.HasDeathCount
                ? Localization.Format("RawFormat", snapshot.RawDeathCount)
                : Localization.Get("RawUnavailable");

            EacWarning.Visibility = isEldenRing ? Visibility.Visible : Visibility.Collapsed;
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
                    MonitorStatusText.Text = string.IsNullOrEmpty(snapshot.Message)
                        ? ConnectionText.Text
                        : ConnectionText.Text + " · " + snapshot.Message;
                    MonitorStatusDot.Fill = BrushFromHex("#42C77A");
                    break;
                case MonitorState.Unsupported:
                    ConnectionText.Text = Localization.Get("Unsupported");
                    MonitorStatusText.Text = AppendMessage(ConnectionText.Text, snapshot.Message);
                    MonitorStatusDot.Fill = BrushFromHex("#E05B5B");
                    break;
                case MonitorState.Error:
                    ConnectionText.Text = Localization.Get("MonitorError");
                    MonitorStatusText.Text = AppendMessage(ConnectionText.Text, snapshot.Message);
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
            EacWarningTitleText.Text = Localization.Get("EacWarningTitle");
            EacWarningBodyText.Text = Localization.Get("EacWarningText");
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

        private void BalanceColumnHeights() {
            if (!IsLoaded || LeftColumnPanel.DesiredSize.Height <= 0 ||
                RightColumnPanel.DesiredSize.Height <= 0) {
                return;
            }

            double leftWithoutOverlayButton =
                LeftColumnPanel.DesiredSize.Height - OverlayButton.DesiredSize.Height;
            double targetButtonHeight = Math.Max(
                OverlayButton.MinHeight,
                RightColumnPanel.DesiredSize.Height - leftWithoutOverlayButton);

            if (double.IsNaN(OverlayButton.Height) ||
                Math.Abs(OverlayButton.Height - targetButtonHeight) > 0.5) {
                OverlayButton.Height = targetButtonHeight;
            }
        }

        private void OffsetEnabledCheckBox_Click(object sender, RoutedEventArgs e) {
            if (updatingControls) {
                return;
            }

            string error;
            bool enabled = OffsetEnabledCheckBox.IsChecked == true;
            if (!monitor.TrySetOffsetEnabled(enabled, out error)) {
                ShowError(error);
                ApplySnapshot(monitor.LatestSnapshot);
            }
        }

        private void SetCurrentZeroButton_Click(object sender, RoutedEventArgs e) {
            string error;
            if (!monitor.TrySetCurrentAsZero(out error)) {
                ShowError(error);
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
            if (!monitor.TrySetOffset(offset, out error)) {
                ShowError(error);
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
                overlayWindow.Closed += delegate { overlayWindow = null; };
            }

            overlayWindow.ApplyLocalization();
            overlayWindow.ApplyAppearance(
                settings.OverlayBackgroundOpacity,
                settings.OverlayTextColor,
                settings.OverlayFontFamily,
                settings.OverlayFontSize,
                settings.OverlayScalePercent);
            overlayWindow.UpdateDeathCount(
                latestSnapshot != null && latestSnapshot.HasDeathCount ? latestSnapshot.DeathCount : 0);
            overlayWindow.Show();
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
                    settings.OverlayScalePercent);
            }
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

        private void SaveSettings() {
            settingsSaveTimer.Stop();
            string error;
            if (!settings.TrySave(settingsPath, out error)) {
                MonitorStatusText.Text = Localization.Format("SettingsSaveError", error);
                MonitorStatusDot.Fill = BrushFromHex("#E05B5B");
            }
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
