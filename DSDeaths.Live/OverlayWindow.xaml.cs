using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

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
