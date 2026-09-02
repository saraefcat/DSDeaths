using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DSDeaths.Live {
    public partial class OverlayWindow : Window {
        private const double BaseWidth = 430;
        private const double BaseHeight = 180;

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
            int fontSize,
            int scalePercent) {
            int clampedOpacity = Math.Max(0, Math.Min(100, backgroundOpacity));
            byte alpha = (byte)Math.Round(clampedOpacity * 255.0 / 100.0);
            OverlayBackgroundBorder.Background = new SolidColorBrush(
                Color.FromArgb(alpha, 0x0A, 0x0D, 0x12));

            Color foreground = (Color)ColorConverter.ConvertFromString(textColor);
            OverlayCountText.Foreground = new SolidColorBrush(foreground);
            OverlayCountText.FontFamily = new FontFamily(fontFamily);
            OverlayCountText.FontSize = Math.Max(24, Math.Min(96, fontSize));

            int clampedScale = Math.Max(50, Math.Min(200, scalePercent));
            double scale = clampedScale / 100.0;
            double newWidth = BaseWidth * scale;
            double newHeight = BaseHeight * scale;
            bool keepCenter = IsLoaded && IsVisible;
            double centerX = keepCenter ? Left + ActualWidth / 2.0 : 0;
            double centerY = keepCenter ? Top + ActualHeight / 2.0 : 0;

            Width = newWidth;
            Height = newHeight;

            if (keepCenter) {
                Rect workArea = SystemParameters.WorkArea;
                Left = Math.Max(workArea.Left, Math.Min(centerX - newWidth / 2.0, workArea.Right - newWidth));
                Top = Math.Max(workArea.Top, Math.Min(centerY - newHeight / 2.0, workArea.Bottom - newHeight));
            }
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
