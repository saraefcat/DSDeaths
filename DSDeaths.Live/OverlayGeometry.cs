using System;
using System.Windows;

namespace DSDeaths.Live {
    internal static class OverlayGeometry {
        internal static Point ClampTopLeft(Rect workArea, Size windowSize, Point desiredTopLeft) {
            double width = Math.Max(0, windowSize.Width);
            double height = Math.Max(0, windowSize.Height);
            double maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
            double maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);

            return new Point(
                Math.Max(workArea.Left, Math.Min(desiredTopLeft.X, maximumLeft)),
                Math.Max(workArea.Top, Math.Min(desiredTopLeft.Y, maximumTop)));
        }

        internal static Point CenterTopLeft(Rect workArea, Size windowSize) {
            return ClampTopLeft(
                workArea,
                windowSize,
                new Point(
                    workArea.Left + (workArea.Width - windowSize.Width) / 2.0,
                    workArea.Top + (workArea.Height - windowSize.Height) / 2.0));
        }

        internal static double AdditionalHeight(double sourceHeight, double targetHeight) {
            return Math.Max(0, targetHeight - sourceHeight);
        }
    }
}
