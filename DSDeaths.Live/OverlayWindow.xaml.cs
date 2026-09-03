using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;

namespace DSDeaths.Live {
    public partial class OverlayWindow : Window {
        private const double BaseWidth = 430;
        private const double BaseHeight = 180;
        private bool positionLocked;

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
            string textShadow,
            int scalePercent) {
            int clampedOpacity = Math.Max(0, Math.Min(100, backgroundOpacity));
            byte alpha = (byte)Math.Round(clampedOpacity * 255.0 / 100.0);
            OverlayBackgroundBorder.Background = new SolidColorBrush(
                Color.FromArgb(alpha, 0x0A, 0x0D, 0x12));

            Color foreground = (Color)ColorConverter.ConvertFromString(textColor);
            OverlayCountText.Foreground = new SolidColorBrush(foreground);
            OverlayCountText.FontFamily = new FontFamily(fontFamily);
            OverlayCountText.FontSize = Math.Max(24, Math.Min(96, fontSize));
            OverlayCountText.Effect = CreateTextShadow(textShadow);

            int clampedScale = Math.Max(50, Math.Min(200, scalePercent));
            double scale = clampedScale / 100.0;
            double newWidth = BaseWidth * scale;
            double newHeight = BaseHeight * scale;
            bool sizeChanged = Math.Abs(Width - newWidth) > 0.01 ||
                Math.Abs(Height - newHeight) > 0.01;

            if (!sizeChanged) {
                return;
            }

            bool keepCenter = IsLoaded && IsVisible;
            double centerX = keepCenter ? Left + ActualWidth / 2.0 : 0;
            double centerY = keepCenter ? Top + ActualHeight / 2.0 : 0;

            Width = newWidth;
            Height = newHeight;

            if (keepCenter) {
                Rect workArea = GetCurrentMonitorWorkArea();
                Point topLeft = OverlayGeometry.ClampTopLeft(
                    workArea,
                    new Size(newWidth, newHeight),
                    new Point(centerX - newWidth / 2.0, centerY - newHeight / 2.0));
                Left = topLeft.X;
                Top = topLeft.Y;
            }
        }

        public void ApplyBehavior(
            bool locked,
            bool showBorder,
            bool showLabel,
            bool topmost) {
            positionLocked = locked;
            OverlayBackgroundBorder.BorderBrush = showBorder
                ? new SolidColorBrush(Color.FromArgb(0x80, 0xD5, 0xA8, 0x47))
                : Brushes.Transparent;
            OverlayDeathsLabel.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
            Topmost = topmost;
        }

        public void RestorePosition(double left, double top) {
            Rect workArea = GetCurrentMonitorWorkAreaAfterMove(left, top);
            Point position = OverlayGeometry.ClampTopLeft(
                workArea,
                new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height),
                new Point(left, top));
            Left = position.X;
            Top = position.Y;
        }

        public void CenterOn(Window referenceWindow) {
            Rect workArea = GetMonitorWorkArea(referenceWindow);
            Point position = OverlayGeometry.CenterTopLeft(
                workArea,
                new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height));
            Left = position.X;
            Top = position.Y;
        }

        private Rect GetCurrentMonitorWorkArea() {
            return GetMonitorWorkArea(this);
        }

        private Rect GetCurrentMonitorWorkAreaAfterMove(double left, double top) {
            Left = left;
            Top = top;
            return GetMonitorWorkArea(this);
        }

        private static Rect GetMonitorWorkArea(Window window) {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            System.Drawing.Rectangle deviceArea = Forms.Screen.FromHandle(handle).WorkingArea;
            PresentationSource source = PresentationSource.FromVisual(window);
            if (source == null || source.CompositionTarget == null) {
                return new Rect(deviceArea.Left, deviceArea.Top, deviceArea.Width, deviceArea.Height);
            }

            Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
            Point topLeft = fromDevice.Transform(new Point(deviceArea.Left, deviceArea.Top));
            Point bottomRight = fromDevice.Transform(new Point(deviceArea.Right, deviceArea.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private static DropShadowEffect CreateTextShadow(string textShadow) {
            if (string.Equals(textShadow, "none", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            bool strong = string.Equals(textShadow, "strong", StringComparison.OrdinalIgnoreCase);
            var effect = new DropShadowEffect {
                Color = Colors.Black,
                BlurRadius = strong ? 8 : 4,
                Direction = 315,
                Opacity = strong ? 0.95 : 0.75,
                ShadowDepth = strong ? 2 : 1,
                RenderingBias = RenderingBias.Quality
            };
            effect.Freeze();
            return effect;
        }

        private void OverlayWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (!positionLocked && e.ButtonState == MouseButtonState.Pressed) {
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
