using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MacroBMR
{
    public partial class VirtualCursorOverlay : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public VirtualCursorOverlay()
        {
            InitializeComponent();
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // 100% click-through, tidak mengambil fokus, tersembunyi dari Alt+Tab
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        public void MoveCursor(double x, double y, string actionType = "move", int stepIndex = 0)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => MoveCursor(x, y, actionType, stepIndex)));
                return;
            }

            Canvas.SetLeft(CursorContainer, x);
            Canvas.SetTop(CursorContainer, y);

            if (stepIndex > 0)
            {
                TxtStep.Text = $"#{stepIndex}";
                StepBadge.Visibility = Visibility.Visible;
            }
            else
            {
                StepBadge.Visibility = Visibility.Collapsed;
            }
        }

        public void PlayClickAnimation(double x, double y, string clickType = "click")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => PlayClickAnimation(x, y, clickType)));
                return;
            }

            // Pindahkan kursor ke posisi klik
            Canvas.SetLeft(CursorContainer, x);
            Canvas.SetTop(CursorContainer, y);

            // Buat efek ripple melingkar di titik klik
            var ripple = new Ellipse
            {
                Width = 10,
                Height = 10,
                Stroke = clickType == "right" ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                StrokeThickness = 2,
                Fill = clickType == "right" ? new SolidColorBrush(Color.FromArgb(60, 245, 158, 11)) : new SolidColorBrush(Color.FromArgb(60, 239, 68, 68)),
                Opacity = 0.9,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(ripple, x - 5);
            Canvas.SetTop(ripple, y - 5);
            RippleCanvas.Children.Add(ripple);

            // Animasi membesar
            var sizeAnim = new DoubleAnimation(10, 42, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Animasi pudar
            var fadeAnim = new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var leftAnim = new DoubleAnimation(x - 5, x - 21, TimeSpan.FromMilliseconds(300));
            var topAnim = new DoubleAnimation(y - 5, y - 21, TimeSpan.FromMilliseconds(300));

            fadeAnim.Completed += (s, ev) =>
            {
                RippleCanvas.Children.Remove(ripple);
            };

            ripple.BeginAnimation(WidthProperty, sizeAnim);
            ripple.BeginAnimation(HeightProperty, sizeAnim);
            ripple.BeginAnimation(Canvas.LeftProperty, leftAnim);
            ripple.BeginAnimation(Canvas.TopProperty, topAnim);
            ripple.BeginAnimation(OpacityProperty, fadeAnim);
        }

        public void HideCursor()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideCursor));
                return;
            }

            Canvas.SetLeft(CursorContainer, -100);
            Canvas.SetTop(CursorContainer, -100);
            RippleCanvas.Children.Clear();
        }
    }
}
