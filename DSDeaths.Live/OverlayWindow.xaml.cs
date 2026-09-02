using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace DSDeaths.Live {
    public partial class OverlayWindow : Window {
        public OverlayWindow() {
            InitializeComponent();
            MouseLeftButtonDown += OverlayWindow_MouseLeftButtonDown;
            MouseRightButtonDown += OverlayWindow_MouseRightButtonDown;
            ApplyLocalization();
        }

        public event EventHandler HideRequested;

        public void UpdateDeathCount(int deathCount) {
            OverlayCountText.Text = deathCount.ToString(CultureInfo.InvariantCulture);
        }

        public void ApplyLocalization() {
            OverlayDeathsLabel.Text = Localization.Get("DeathsLabel");
        }

        private void OverlayWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ButtonState == MouseButtonState.Pressed) {
                DragMove();
            }
        }

        private void OverlayWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            EventHandler handler = HideRequested;
            if (handler != null) {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
