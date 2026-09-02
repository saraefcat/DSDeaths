using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DSDeaths.Live {
    public partial class OverlayWindow : Window {
        public OverlayWindow() {
            InitializeComponent();
            MouseLeftButtonDown += OverlayWindow_MouseLeftButtonDown;
            ApplyLocalization();
        }

        public event EventHandler HideRequested;

        public void UpdateDeathCount(int deathCount) {
            OverlayCountText.Text = deathCount.ToString(CultureInfo.InvariantCulture);
        }

        public void ApplyLocalization() {
            OverlayDeathsLabel.Text = Localization.Get("DeathsLabel");
            HideOverlayMenuItem.Header = Localization.Get("HideOverlay");
        }

        public void ApplyAppearance(
            int backgroundOpacity,
            string textColor,
            string fontFamily,
            int fontSize) {
            int clampedOpacity = Math.Max(0, Math.Min(100, backgroundOpacity));
            byte alpha = (byte)Math.Round(clampedOpacity * 255.0 / 100.0);
            OverlayBackgroundBorder.Background = new SolidColorBrush(
                Color.FromArgb(alpha, 0x0A, 0x0D, 0x12));

            Color foreground = (Color)ColorConverter.ConvertFromString(textColor);
            OverlayCountText.Foreground = new SolidColorBrush(foreground);
            OverlayCountText.FontFamily = new FontFamily(fontFamily);
            OverlayCountText.FontSize = Math.Max(24, Math.Min(96, fontSize));
        }

        private void OverlayWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ButtonState == MouseButtonState.Pressed) {
                DragMove();
            }
        }

        private void HideOverlayMenuItem_Click(object sender, RoutedEventArgs e) {
            EventHandler handler = HideRequested;
            if (handler != null) {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
