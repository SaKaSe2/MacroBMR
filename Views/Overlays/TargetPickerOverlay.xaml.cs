using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MacroBMR.Core;

namespace MacroBMR
{
    public partial class TargetPickerOverlay : Window
    {
        public event Action<int, int, string, string> OnTargetSelected;
        public event Action OnCancelled;

        private bool _isBackgroundMode;
        private IntPtr _bgTargetHandle;
        private IntPtr _ignoreWindowHwnd;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public TargetPickerOverlay(string titleText, bool isBackgroundMode = false, IntPtr bgTargetHandle = default, IntPtr ignoreWindowHwnd = default)
        {
            InitializeComponent();

            _isBackgroundMode = isBackgroundMode;
            _bgTargetHandle = bgTargetHandle;
            _ignoreWindowHwnd = ignoreWindowHwnd;

            if (!string.IsNullOrEmpty(titleText))
            {
                TxtTitle.Text = titleText;
            }

            // Cakup seluruh area virtual screen (semua monitor jika ada multi-monitor)
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            this.Loaded += (s, e) =>
            {
                this.Activate();
                this.Focus();

                // Posisikan HUD Header di tengah horizontal
                double headerLeft = (this.Width - HudHeader.ActualWidth) / 2.0;
                Canvas.SetLeft(HudHeader, Math.Max(20, headerLeft));

                // Posisikan scopebox di posisi kursor awal
                var mousePos = System.Windows.Forms.Cursor.Position;
                Point p = this.PointFromScreen(new Point(mousePos.X, mousePos.Y));
                UpdateScopePosition(p, mousePos.X, mousePos.Y);
            };

            this.MouseMove += TargetPickerOverlay_MouseMove;
            this.PreviewMouseDown += TargetPickerOverlay_MouseDown;
            this.KeyDown += TargetPickerOverlay_KeyDown;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // Sembunyikan dari Alt+Tab, namun TETAP MENERIMA INPUT KLIK (Bukan WS_EX_TRANSPARENT)
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        private void UpdateScopePosition(Point localPoint, int screenX, int screenY)
        {
            Canvas.SetLeft(ScopeBox, localPoint.X - 50);
            Canvas.SetTop(ScopeBox, localPoint.Y - 50);

            Canvas.SetLeft(CoordBadge, localPoint.X + 15);
            Canvas.SetTop(CoordBadge, localPoint.Y + 15);

            TxtCoord.Text = $"X: {screenX}, Y: {screenY}";
        }

        private void TargetPickerOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            Point localPoint = e.GetPosition(this);
            Point screenPoint = this.PointToScreen(localPoint);
            UpdateScopePosition(localPoint, (int)screenPoint.X, (int)screenPoint.Y);
        }

        private void TargetPickerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right) return;

            Point localPoint = e.GetPosition(this);
            Point screenPoint = this.PointToScreen(localPoint);
            ExecuteCapture((int)screenPoint.X, (int)screenPoint.Y);
        }

        private void TargetPickerOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                this.Close();
                OnCancelled?.Invoke();
            }
            else if (e.Key == Key.F9 || e.Key == Key.Enter)
            {
                e.Handled = true;
                var mousePos = System.Windows.Forms.Cursor.Position;
                ExecuteCapture(mousePos.X, mousePos.Y);
            }
        }

        private void ExecuteCapture(int screenX, int screenY)
        {
            // Sembunyikan overlay terlebih dahulu agar tidak ikut ter-screenshot
            this.Visibility = Visibility.Hidden;
            
            // Tunggu 1 frame agar DWM selesai merender layar bersih di bawahnya
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            System.Threading.Thread.Sleep(30);

            string thumbnail = null;
            string thumbnailBg = null;

            try
            {
                thumbnail = ScreenCapture.CropAroundPoint(screenX, screenY);
            }
            catch { }

            if (_isBackgroundMode && _bgTargetHandle != IntPtr.Zero)
            {
                try
                {
                    MacroRecorder.CaptureBackgroundThumbnail(screenX, screenY, out thumbnailBg, _ignoreWindowHwnd, _bgTargetHandle);
                }
                catch { }
            }

            // Bunyikan konfirmasi
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

            this.Close();
            OnTargetSelected?.Invoke(screenX, screenY, thumbnail, thumbnailBg);
        }
    }
}
