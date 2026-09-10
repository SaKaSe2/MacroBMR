using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacroBMR
{
    public enum FixTargetMode { Current, Previous, Fallback, InsertAfter, InsertBefore, InsertAt }

    public partial class OverlayWindow : Window
    {
        public event Action OnStopRequested;
        public event Action OnPauseRequested;
        public event Action<FixTargetMode> OnFixTargetRequested;
        public event Action<int> OnJumpRequested;
        public event Action<double> OnSpeedChanged;
        public event Action OnEditSingleTimeRequested;
        public event Action OnEditThresholdRequested;
        public event Action OnEditSmartWaitRequested;

        // Win32: Sembunyikan jendela dari Alt+Tab
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;



        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public OverlayWindow()
        {
            InitializeComponent();
            
            // Posisikan di pojok kanan atas layar
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = screenWidth - 36;
            this.Top = 8;
        }

        // Win32: Supaya overlay tetap bisa diklik tanpa masalah
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATEANDEAT = 4;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // Sembunyikan dari Alt+Tab saja
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        private void BtnStop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            OnStopRequested?.Invoke();
        }

        private void BtnPause_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            OnPauseRequested?.Invoke();
        }

        public void SetPauseState(bool isPaused)
        {
            Dispatcher.Invoke(() =>
            {
                if (isPaused)
                {
                    BtnPause.Content = "Resume";
                    BtnPause.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")); // Hijau
                }
                else
                {
                    BtnPause.Content = "Pause";
                    BtnPause.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706")); // Amber
                }
            });
        }

        // Reset tombol fix target setelah selesai merekam target alternatif
        public void ResetFallbackButton()
        {
            Dispatcher.Invoke(() =>
            {
                BtnFixTarget.Content = "Fix";
                BtnFixTarget.IsEnabled = true;
            });
        }

        // Klik tombol kecil untuk expand panel
        private void CollapsedView_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CollapsedView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Visible;
            
            // Reposisi agar panel tidak keluar layar (lebar ringkas ~295px)
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = screenWidth - 295;
            this.Top = 8;
        }

        // Klik X untuk collapse kembali
        private void BtnCollapse_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            ExpandedView.Visibility = Visibility.Collapsed;
            CollapsedView.Visibility = Visibility.Visible;
            
            // Kembalikan posisi ke pojok kanan atas
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = screenWidth - 36;
            this.Top = 8;
        }

        private void BtnFixTarget_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            // Buka context menu
            if (BtnFixTarget.ContextMenu != null)
            {
                BtnFixTarget.ContextMenu.PlacementTarget = BtnFixTarget;
                BtnFixTarget.ContextMenu.IsOpen = true;
            }
        }

        private void StartFixing(FixTargetMode mode)
        {
            BtnFixTarget.Content = "...";
            BtnFixTarget.IsEnabled = false;
            OnFixTargetRequested?.Invoke(mode);
        }

        private void MenuRecordCurrent_Click(object sender, RoutedEventArgs e)
        {
            StartFixing(FixTargetMode.Current);
        }

        private void MenuRecordPrevious_Click(object sender, RoutedEventArgs e)
        {
            StartFixing(FixTargetMode.Previous);
        }

        private void MenuRecordFallback_Click(object sender, RoutedEventArgs e)
        {
            StartFixing(FixTargetMode.Fallback);
        }

        private void MenuEditThreshold_Click(object sender, RoutedEventArgs e)
        {
            OnEditThresholdRequested?.Invoke();
        }

        private void MenuEditSmartWait_Click(object sender, RoutedEventArgs e)
        {
            OnEditSmartWaitRequested?.Invoke();
        }

        private void MenuInsertAfter_Click(object sender, RoutedEventArgs e)
        {
            StartFixing(FixTargetMode.InsertAfter);
        }

        private void MenuInsertBefore_Click(object sender, RoutedEventArgs e)
        {
            StartFixing(FixTargetMode.InsertBefore);
        }

        private void MenuInsertAt_Click(object sender, RoutedEventArgs e)
        {
            StartFixing(FixTargetMode.InsertAt);
        }

        public void SetupIterationIndicator(int totalActions)
        {
            Dispatcher.Invoke(() =>
            {
                CmbIteration.SelectionChanged -= CmbIteration_SelectionChanged;
                CmbIteration.Items.Clear();
                for (int i = 1; i <= totalActions; i++)
                {
                    CmbIteration.Items.Add($"{i} / {totalActions}");
                }
                CmbIteration.Visibility = totalActions > 0 ? Visibility.Visible : Visibility.Collapsed;
                CmbIteration.SelectionChanged += CmbIteration_SelectionChanged;
            });
        }

        public void UpdateCurrentIteration(int current)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (current >= 1 && current <= CmbIteration.Items.Count)
                {
                    if (CmbIteration.SelectedIndex != current - 1)
                    {
                        CmbIteration.SelectionChanged -= CmbIteration_SelectionChanged;
                        CmbIteration.SelectedIndex = current - 1;
                        CmbIteration.SelectionChanged += CmbIteration_SelectionChanged;
                    }
                }
            });
        }

        private void CmbIteration_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbIteration.SelectedIndex == -1) return;
            OnJumpRequested?.Invoke(CmbIteration.SelectedIndex);
        }

        private void BtnSpeed_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (BtnSpeed.ContextMenu != null)
            {
                BtnSpeed.ContextMenu.PlacementTarget = BtnSpeed;
                BtnSpeed.ContextMenu.IsOpen = true;
            }
        }

        private void MenuEditSingleTime_Click(object sender, RoutedEventArgs e)
        {
            OnEditSingleTimeRequested?.Invoke();
        }

        private void MenuSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag != null)
            {
                if (double.TryParse(menuItem.Tag.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double newSpeed))
                {
                    SetSpeedDisplay(newSpeed);
                    OnSpeedChanged?.Invoke(newSpeed);
                }
            }
        }

        public void SetSpeedDisplay(double speed)
        {
            Dispatcher.Invoke(() =>
            {
                // Update text pada tombol
                BtnSpeed.Content = $"{speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}x";

                // Update status checklist pada menu
                if (BtnSpeed.ContextMenu != null)
                {
                    foreach (var item in BtnSpeed.ContextMenu.Items)
                    {
                        if (item is System.Windows.Controls.MenuItem mi && mi.Tag != null)
                        {
                            if (double.TryParse(mi.Tag.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double itemSpeed))
                            {
                                mi.IsChecked = Math.Abs(itemSpeed - speed) < 0.01;
                            }
                        }
                    }
                }
            });
        }

        public void SetCountdown(double? remainingSeconds)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (remainingSeconds.HasValue)
                {
                    TxtCountdown.Visibility = Visibility.Visible;
                    TxtCountdown.Text = $"{remainingSeconds.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s";
                }
                else
                {
                    TxtCountdown.Visibility = Visibility.Collapsed;
                }
            });
        }
    }
}
