using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Gma.System.MouseKeyHook;
using MacroBMR.Core;
using MacroBMR.Models;
using Newtonsoft.Json;

namespace MacroBMR
{
    public partial class MainWindow : Window
    {
        private MacroRecorder _recorder;
        private MacroExecutor _executor;
        private OverlayWindow _overlayWindow;
        private VirtualCursorOverlay _virtualCursorOverlay;
        private Action _currentStopAction;
        private Action _currentPauseAction;
        private IKeyboardMouseEvents _playbackHook = null;

        // Pemetaan dari urutan interaksi ke indeks asli di _recorder.Actions
        private List<int> _interactionIndices = new List<int>();

        // Untuk menangkap klik saat tambah rekaman alternatif (fallback) / rekam ulang
        private TargetPickerOverlay _targetPickerOverlay;

        // Speed multiplier global untuk macro saat ini
        private double _currentSpeed = 1.0;
        private string _lastExecutionError = "";

        private IntPtr _bgTargetHandle = IntPtr.Zero;

        // P/Invoke untuk manipulasi window style & z-order overlay
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int SW_RESTORE = 9;

        public MainWindow()
        {
            InitializeComponent();
            _overlayWindow = new OverlayWindow();
            _overlayWindow.OnStopRequested += () => { _currentStopAction?.Invoke(); };
            _overlayWindow.OnPauseRequested += () => { _currentPauseAction?.Invoke(); };
            _overlayWindow.OnFixTargetRequested += (mode) => Dispatcher.Invoke(() => StartFixTargetRecord(mode));
            
            _overlayWindow.OnSpeedChanged += (newSpeed) =>
            {
                _currentSpeed = newSpeed;
                _executor.SpeedMultiplier = newSpeed;
            };

            _overlayWindow.OnEditSingleTimeRequested += () => Dispatcher.Invoke(() => EditSingleWaitTime());

            _overlayWindow.OnEditThresholdRequested += () => Dispatcher.Invoke(() => EditActionThreshold());
            _overlayWindow.OnEditSmartWaitRequested += () => Dispatcher.Invoke(() => EditActionSmartWait());

            _overlayWindow.OnJumpRequested += (comboIndex) => 
            {
                if (_interactionIndices != null && comboIndex >= 0 && comboIndex < _interactionIndices.Count)
                {
                    int targetActionIndex = _interactionIndices[comboIndex];
                    _executor.JumpToIndex = targetActionIndex;
                    // Paksa update aksi saat ini agar "Perbaiki Target" langsung mengarah ke iterasi yang baru dipilih meskipun sedang di-pause
                    if (_recorder.Actions != null && targetActionIndex >= 0 && targetActionIndex < _recorder.Actions.Count)
                    {
                        _executor.ForceUpdateCurrentAction(targetActionIndex, _recorder.Actions[targetActionIndex]);
                    }
                    
                    // Otomatis jalan jika sedang dipause
                    if (_executor.IsPaused)
                    {
                        _executor.TogglePause();
                        _overlayWindow.SetPauseState(_executor.IsPaused);
                    }
                }
            };
            
            _recorder = new MacroRecorder();
            _recorder.OnRequestDynamicThreshold += Recorder_OnRequestDynamicThreshold;
            _recorder.OnStopRecording += () => 
            {
                Dispatcher.Invoke(() => 
                {
                    ExecuteStopRecording();
                });
            };

            _recorder.OnPauseStateChanged += (isPaused) =>
            {
                Dispatcher.Invoke(() => _overlayWindow.SetPauseState(isPaused));
            };

            _executor = new MacroExecutor();
            _executor.OnLog = (msg) => Dispatcher.Invoke(() => Log(msg));
            _executor.OnWaitCountdown = (remaining) => _overlayWindow.SetCountdown(remaining == 0 ? null : (double?)remaining);

            StateChanged += Window_StateChanged;
            MenuListBox.SelectedIndex = 0;
            UpdateModeLockState();
        }

        // Mode yang sedang aktif untuk fix target
        private FixTargetMode _currentFixMode;

        private void StartFixTargetRecord(FixTargetMode mode)
        {
            if (mode == FixTargetMode.InsertAfter || mode == FixTargetMode.InsertBefore || mode == FixTargetMode.InsertAt)
            {
                int currentIndex = _executor.CurrentActionIndex;
                if (currentIndex < 0) currentIndex = 0;
                
                int targetIndex = currentIndex;
                string logText = "";
                
                if (mode == FixTargetMode.InsertAfter)
                {
                    targetIndex = currentIndex + 1;
                    logText = $"Sisipkan aksi visual setelah baris {currentIndex + 1}";
                }
                else if (mode == FixTargetMode.InsertBefore)
                {
                    targetIndex = currentIndex;
                    logText = $"Sisipkan aksi visual sebelum baris {currentIndex + 1}";
                }
                else if (mode == FixTargetMode.InsertAt)
                {
                    string input = PromptInputDialog("Sisipkan Rekaman", "Masukkan nomor baris tujuan sisipan:");
                    if (int.TryParse(input, out int num) && num > 0)
                    {
                        targetIndex = num - 1;
                        if (_recorder.Actions != null && targetIndex > _recorder.Actions.Count) targetIndex = _recorder.Actions.Count;
                        logText = $"Sisipkan aksi visual pada baris ke-{targetIndex + 1}";
                    }
                    else
                    {
                        _overlayWindow.ResetFallbackButton();
                        return; // Batal
                    }
                }
                
                // Pause playback tanpa membatalkannya
                if (!_executor.IsPaused)
                {
                    _executor.TogglePause();
                    Dispatcher.Invoke(() => _overlayWindow.SetPauseState(_executor.IsPaused));
                }

                Log($"[SMART] {logText}: Silakan klik target di layar (game aman, tidak akan terpencet)...");

                IntPtr overlayHwnd = _overlayWindow != null ? new System.Windows.Interop.WindowInteropHelper(_overlayWindow).Handle : IntPtr.Zero;
                _targetPickerOverlay = new TargetPickerOverlay(
                    $"Mode Penunjuk Visual: {logText.ToUpper()}",
                    _executor.IsBackgroundMode,
                    _executor.BackgroundWindowHandle,
                    overlayHwnd
                );

                // Sembunyikan kursor virtual agar tidak ikut ter-screenshot
                _virtualCursorOverlay?.HideCursor();
                _virtualCursorOverlay?.Hide();

                _targetPickerOverlay.OnTargetSelected += (x, y, thumbnail, thumbnailBg) =>
                {
                    _targetPickerOverlay = null;
                    // Tampilkan kembali kursor virtual
                    _virtualCursorOverlay?.Show();

                    var newAction = new MacroAction
                    {
                        Action = "click",
                        X = x,
                        Y = y,
                        Button = "left",
                        Clicks = 1,
                        ClickThumbnail = thumbnail,
                        ClickThumbnailBg = thumbnailBg,
                        RecordedWindowId = _executor.IsBackgroundMode && _executor.BackgroundWindowHandle != IntPtr.Zero ? _executor.BackgroundWindowHandle.ToInt64() : 0,
                        RecordedWindowTitle = !string.IsNullOrEmpty(_recorder.TargetWindowTitle) ? _recorder.TargetWindowTitle : null,
                        Seconds = 1.0
                    };

                    if (_recorder.Actions == null) _recorder.Actions = new List<MacroAction>();
                    if (targetIndex > _recorder.Actions.Count) targetIndex = _recorder.Actions.Count;
                    if (targetIndex < 0) targetIndex = 0;

                    _recorder.Actions.Insert(targetIndex, newAction);

                    // Rebuild daftar interaksi karena list sudah berubah setelah insert
                    _interactionIndices.Clear();
                    for (int i = 0; i < _recorder.Actions.Count; i++)
                    {
                        string act = _recorder.Actions[i].Action?.ToLower();
                        if (act != "move" && act != "wait")
                        {
                            _interactionIndices.Add(i);
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        RefreshTable();
                        _overlayWindow.SetupIterationIndicator(_interactionIndices.Count);
                        _overlayWindow.ResetFallbackButton();
                    });

                    Log($"[SMART] Aksi visual berhasil disisipkan pada baris ke-{targetIndex + 1} di koordinat {x}, {y}.");

                    if (mode == FixTargetMode.InsertBefore || mode == FixTargetMode.InsertAt)
                    {
                        _executor.JumpToIndex = targetIndex;
                    }

                    // Lanjutkan MacroExecutor
                    if (_executor.IsPaused)
                    {
                        _executor.TogglePause();
                        Dispatcher.Invoke(() => _overlayWindow.SetPauseState(_executor.IsPaused));
                    }
                };

                _targetPickerOverlay.OnCancelled += () =>
                {
                    _targetPickerOverlay = null;
                    // Tampilkan kembali kursor virtual
                    _virtualCursorOverlay?.Show();
                    Log("[SMART] Penyisipan aksi visual dibatalkan.");
                    _overlayWindow.ResetFallbackButton();
                    if (_executor.IsPaused)
                    {
                        _executor.TogglePause();
                        Dispatcher.Invoke(() => _overlayWindow.SetPauseState(_executor.IsPaused));
                    }
                };

                _targetPickerOverlay.Show();
                return;
            }

            // Validasi: pastikan ada aksi yang sedang berjalan
            if (_executor.CurrentExecutingAction == null)
            {
                Log("[SMART] Tidak ada aksi yang sedang berjalan.");
                _overlayWindow.ResetFallbackButton();
                return;
            }

            // Validasi: mode Previous butuh ada aksi klik sebelumnya
            if (mode == FixTargetMode.Previous)
            {
                bool hasPrevious = false;
                for (int pIdx = _executor.CurrentActionIndex - 1; pIdx >= 0; pIdx--)
                {
                    if (_recorder.Actions != null && pIdx < _recorder.Actions.Count)
                    {
                        var act = _recorder.Actions[pIdx];
                        if (act.Action == "click" || act.Action == "mousedown" || act.Action == "double_click" || (act.X.HasValue && act.Y.HasValue))
                        {
                            hasPrevious = true;
                            break;
                        }
                    }
                }

                if (!hasPrevious)
                {
                    Log("[SMART] Tidak ada aksi klik sebelumnya untuk direkam ulang.");
                    _overlayWindow.ResetFallbackButton();
                    return;
                }
            }

            _currentFixMode = mode;
            string modeLabel = mode switch
            {
                FixTargetMode.Current => "target saat ini",
                FixTargetMode.Previous => "target sebelumnya",
                FixTargetMode.Fallback => "target alternatif (fallback)",
                _ => "target"
            };
            Log($"[SMART] Mode penunjuk visual {modeLabel} aktif. Silakan klik target di layar (game aman, tidak akan terpencet)...");

            IntPtr overlayHwndMain = _overlayWindow != null ? new System.Windows.Interop.WindowInteropHelper(_overlayWindow).Handle : IntPtr.Zero;
            _targetPickerOverlay = new TargetPickerOverlay(
                $"Mode Penunjuk Visual: Rekam Ulang {modeLabel.ToUpper()}",
                _executor.IsBackgroundMode,
                _executor.BackgroundWindowHandle,
                overlayHwndMain
            );

            // Sembunyikan kursor virtual agar tidak ikut ter-screenshot
            _virtualCursorOverlay?.HideCursor();
            _virtualCursorOverlay?.Hide();

            _targetPickerOverlay.OnTargetSelected += (x, y, thumbnail, thumbnailBg) =>
            {
                _targetPickerOverlay = null;
                // Tampilkan kembali kursor virtual
                _virtualCursorOverlay?.Show();
                ProcessFixTargetCapture(x, y, thumbnail, thumbnailBg);
            };

            _targetPickerOverlay.OnCancelled += () =>
            {
                _targetPickerOverlay = null;
                // Tampilkan kembali kursor virtual
                _virtualCursorOverlay?.Show();
                Log("[SMART] Pemilihan target visual dibatalkan.");
                _overlayWindow.ResetFallbackButton();
            };

            _targetPickerOverlay.Show();
        }

        private void ProcessFixTargetCapture(int x, int y, string thumbnail, string thumbnailBg)
        {
            MacroAction targetAction = null;
            int targetIndex = _executor.CurrentActionIndex;

            switch (_currentFixMode)
            {
                case FixTargetMode.Current:
                    targetAction = _executor.CurrentExecutingAction;
                    if (targetAction != null && targetAction.Action == "wait" && _recorder.Actions != null && targetIndex + 1 < _recorder.Actions.Count)
                    {
                        var nextAct = _recorder.Actions[targetIndex + 1];
                        if (nextAct.Action == "click" || nextAct.Action == "mousedown" || nextAct.Action == "double_click")
                        {
                            targetAction = nextAct;
                        }
                    }
                    break;

                case FixTargetMode.Previous:
                    if (_recorder.Actions != null)
                    {
                        for (int p = targetIndex - 1; p >= 0; p--)
                        {
                            if (p < _recorder.Actions.Count)
                            {
                                var a = _recorder.Actions[p];
                                if (a.Action == "click" || a.Action == "mousedown" || a.Action == "double_click" || (a.X.HasValue && a.Y.HasValue))
                                {
                                    targetAction = a;
                                    break;
                                }
                            }
                        }
                    }
                    break;

                case FixTargetMode.Fallback:
                    targetAction = _executor.CurrentExecutingAction;
                    break;
            }

            if (targetAction == null)
            {
                Log("[SMART] Gagal menentukan aksi target.");
                _overlayWindow.ResetFallbackButton();
                return;
            }

            if (_currentFixMode == FixTargetMode.Fallback)
            {
                lock (targetAction)
                {
                    if (targetAction.Fallbacks == null)
                        targetAction.Fallbacks = new List<FallbackTarget>();

                    targetAction.Fallbacks.Add(new FallbackTarget {
                        Thumbnail = thumbnail,
                        X = x,
                        Y = y
                    });
                }
                Log($"[SMART] Target alternatif berhasil disimpan di koordinat {x}, {y}");
            }
            else
            {
                lock (targetAction)
                {
                    targetAction.ClickThumbnail = thumbnail;
                    targetAction.ClickThumbnailBg = thumbnailBg;
                    targetAction.X = x;
                    targetAction.Y = y;
                }

                string label = _currentFixMode == FixTargetMode.Current ? "saat ini" : "sebelumnya";
                Log($"[SMART] Target {label} berhasil diperbarui ke koordinat {x}, {y}");
            }
            
            Dispatcher.Invoke(() => RefreshTable());
            _overlayWindow.ResetFallbackButton();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Tutup overlay window yang mungkin tersembunyi (Hide) di memory
            _overlayWindow?.Close();
            
            // Paksa matikan semua thread background & Global Hook yang nyangkut
            Environment.Exit(0);
        }

        private void MenuListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabControl != null)
                MainTabControl.SelectedIndex = MenuListBox.SelectedIndex;
        }

        // ===== Title bar kustom (chromeless window) =====
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                MaxHeight = SystemParameters.WorkArea.Height;
                MaxWidth = SystemParameters.WorkArea.Width;
                WindowState = WindowState.Maximized;
            }
            else
            {
                MaxHeight = double.PositiveInfinity;
                MaxWidth = double.PositiveInfinity;
                WindowState = WindowState.Normal;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Update ikon maximize ↔ restore pada custom window chrome
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (IcoMaximize == null) return;
            IcoMaximize.Kind = WindowState == WindowState.Maximized
                ? MaterialDesignThemes.Wpf.PackIconKind.WindowRestore
                : MaterialDesignThemes.Wpf.PackIconKind.WindowMaximize;
        }

        private void Log(string message)
        {
            if (TxtLog.Text.Length > 20000)
            {
                int cutPos = TxtLog.Text.IndexOf('\n', 10000);
                if (cutPos > 0) TxtLog.Text = TxtLog.Text.Substring(cutPos + 1);
                else TxtLog.Text = TxtLog.Text.Substring(10000);
            }
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }

        // Sembunyikan overlay dan kembalikan jendela utama
        private void RestoreFromOverlay()
        {
            if (_targetPickerOverlay != null)
            {
                _targetPickerOverlay.Close();
                _targetPickerOverlay = null;
                _overlayWindow.ResetFallbackButton();
            }

            _bgTargetHandle = IntPtr.Zero;

            if (_virtualCursorOverlay != null)
            {
                _virtualCursorOverlay.Close();
                _virtualCursorOverlay = null;
            }

            _executor.RestoreOffscreenWindow();
            ScreenCapture.ClearCache();

            _overlayWindow.Hide();
            _overlayWindow.SetPauseState(false);
            _overlayWindow.SetCountdown(null); // Reset countdown saat overlay ditutup
            
            var helper = new System.Windows.Interop.WindowInteropHelper(_overlayWindow);
            helper.Owner = IntPtr.Zero;
            _overlayWindow.Topmost = true;

            this.ShowInTaskbar = true;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_recorder.IsRecording)
            {
                _currentStopAction?.Invoke();
                return;
            }

            if (!_recorder.IsRecording)
            {
                bool append = false;
                if (_recorder.Actions != null && _recorder.Actions.Count > 0)
                {
                    var result = MessageBox.Show(
                        "Ada rekaman sebelumnya. Apakah Anda ingin melanjutkan rekaman tersebut?\n\n[Yes] Lanjut Record\n[No] Mulai Baru (Hapus yang lama)\n[Cancel] Batal", 
                        "Pilihan Rekaman", 
                        MessageBoxButton.YesNoCancel, 
                        MessageBoxImage.Question);
                        
                    if (result == MessageBoxResult.Cancel) return;
                    if (result == MessageBoxResult.Yes) append = true;
                }

                bool recordMouse = ChkRecordMouse.IsChecked == true;
                _recorder.UseDynamicThreshold = ChkDynamicThreshold.IsChecked == true;
                
                if (_overlayWindow != null)
                {
                    _recorder.IgnoreWindowHwnd = new System.Windows.Interop.WindowInteropHelper(_overlayWindow).EnsureHandle();
                }

                // Konfigurasi Mode Rekaman: Background (Target Window) vs Normal (Manual)
                _recorder.IsBackgroundRecording = ChkBackgroundMode.IsChecked == true;
                if (_recorder.IsBackgroundRecording && CmbBackgroundWindow.SelectedValue != null)
                {
                    _recorder.TargetWindowHandle = (IntPtr)CmbBackgroundWindow.SelectedValue;
                    _recorder.TargetWindowTitle = (CmbBackgroundWindow.SelectedItem as WindowInfo)?.Title;
                    Log($"[RECORDER] Memulai Rekaman MODE BACKGROUND (Target: {_recorder.TargetWindowTitle ?? "Auto"})");
                }
                else
                {
                    _recorder.TargetWindowHandle = IntPtr.Zero;
                    _recorder.TargetWindowTitle = null;
                    if (_recorder.IsBackgroundRecording)
                        Log("[RECORDER] Memulai Rekaman MODE BACKGROUND (Auto-detect jendela)");
                    else
                        Log("[RECORDER] Memulai Rekaman MODE NORMAL (Manual / Layar Depan)");
                }
                
                _recorder.StartRecording(recordMouse, append);
                UpdateRecordButtonState();

                // Atur aksi yang dipanggil saat tombol Stop/Pause overlay diklik
                _currentStopAction = () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ExecuteStopRecording();
                    });
                };

                _currentPauseAction = () =>
                {
                    // Toggle pause pada recorder (F11 handler sudah ada di MacroRecorder)
                    _recorder.IsPaused = !_recorder.IsPaused;
                    if (!_recorder.IsPaused) 
                    {
                        _recorder.ResetLastActionTime();
                    }
                    else
                    {
                        // Tidak perlu hapus aksi, karena klik pada OverlayWindow sudah otomatis diabaikan.
                    }
                    Dispatcher.Invoke(() => _overlayWindow.SetPauseState(_recorder.IsPaused));
                };

                // Sembunyikan jendela utama sepenuhnya (hilang dari Alt+Tab dan Taskbar)
                this.ShowInTaskbar = false;
                this.Hide();
                _overlayWindow.SetPauseState(false);
                _overlayWindow.SetupIterationIndicator(0); // Sembunyikan indikator iterasi saat merekam
                _overlayWindow.Show();
                
                if (append) Log("Lanjut Recording dimulai. Tekan F12 untuk stop.");
                else Log("Recording dimulai. Tekan F12 untuk stop.");
            }
            else
            {
                ExecuteStopRecording();
            }
        }

        private void ExecuteStopRecording()
        {
            if (_recorder.IsRecording)
            {
                _recorder.StopRecording();
            }
            _recorder.CompressMouseMoves();

            UpdateRecordButtonState();
            RefreshTable();
            Log($"Recording dihentikan. { _recorder.Actions.Count } aksi tersimpan.");
            RestoreFromOverlay();
        }

        private bool _isPlaying = false;

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                if (_currentStopAction != null)
                {
                    _currentStopAction.Invoke();
                }
                else
                {
                    _isPlaying = false;
                }
                return;
            }

            if (_recorder.Actions == null || _recorder.Actions.Count == 0) return;
            
            int loopCount = 1;
            if (!int.TryParse(TxtLoopCount.Text, out loopCount) || loopCount < 0)
            {
                MessageBox.Show("Nilai loop tidak valid. Masukkan angka (0 = infinite).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _isPlaying = true;
            UpdateDashboardStats();
            // BtnPlay.IsEnabled = false; // Kita hapus agar bisa diklik untuk STOP
            BtnPlay.Content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.StopCircle, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) }, new TextBlock { Text = "Stop Playback (F12)", VerticalAlignment = VerticalAlignment.Center } } };
            BtnPlay.Background = System.Windows.Media.Brushes.Gray;

            // Atur Smart Playback mode
            _executor.SmartPlayback = ChkSmartPlayback.IsChecked == true;

            // Atur Background Mode & Offscreen Mode
            _executor.IsBackgroundMode = ChkBackgroundMode.IsChecked == true;
            _executor.IsOffscreenMode = ChkOffscreenMode.IsChecked == true;
            if (_executor.IsBackgroundMode && CmbBackgroundWindow.SelectedValue != null)
            {
                _executor.BackgroundWindowHandle = (IntPtr)CmbBackgroundWindow.SelectedValue;
            }
            else
            {
                _executor.BackgroundWindowHandle = IntPtr.Zero;
            }

            // Fitur ambil PID dihapus sesuai permintaan
            if (_executor.IsBackgroundMode && _executor.BackgroundWindowHandle != IntPtr.Zero)
            {
                _bgTargetHandle = _executor.BackgroundWindowHandle;
            }

            string modeInfo = _executor.SmartPlayback ? " [Smart Playback ON]" : "";
            if (_executor.IsBackgroundMode) modeInfo += " [BACKGROUND MODE]";
            Log(loopCount == 0 ? $"Menjalankan macro (Infinite Loop){modeInfo}... Tekan F12 untuk stop." : $"Menjalankan macro ({loopCount}x){modeInfo}... Tekan F12 untuk stop.");

            using var cts = new CancellationTokenSource();

            // Atur aksi untuk tombol overlay
            _currentStopAction = () => { cts.Cancel(); };
            _currentPauseAction = () =>
            {
                _executor.TogglePause();
                Dispatcher.Invoke(() => _overlayWindow.SetPauseState(_executor.IsPaused));
            };

            // Sembunyikan jendela utama sepenuhnya (hilang dari Alt+Tab dan Taskbar)
            this.ShowInTaskbar = false;
            this.Hide();

            // Overlay selalu Topmost
            _overlayWindow.Topmost = true;

            _overlayWindow.SetPauseState(false);

            _interactionIndices.Clear();
            if (_recorder.Actions != null)
            {
                for (int i = 0; i < _recorder.Actions.Count; i++)
                {
                    string act = _recorder.Actions[i].Action?.ToLower();
                    if (act != "move" && act != "wait")
                    {
                        _interactionIndices.Add(i);
                    }
                }
            }

            _overlayWindow.SetupIterationIndicator(_interactionIndices.Count);
            
            int lastComboIndex = -1;
            _executor.OnActionIndexChanged = (currentActionIndex, total) => 
            {
                int comboIndexToSelect = -1;
                for (int k = 0; k < _interactionIndices.Count; k++)
                {
                    if (_interactionIndices[k] <= (currentActionIndex - 1))
                    {
                        comboIndexToSelect = k;
                    }
                    else
                    {
                        break;
                    }
                }
                if (comboIndexToSelect != -1 && comboIndexToSelect != lastComboIndex)
                {
                    lastComboIndex = comboIndexToSelect;
                    _overlayWindow.UpdateCurrentIteration(comboIndexToSelect + 1);
                }
            };

            _overlayWindow.Show();

            if (_executor.IsBackgroundMode)
            {
                _virtualCursorOverlay = new VirtualCursorOverlay();
                _virtualCursorOverlay.Show();

                _executor.OnVirtualCursorMoved = (x, y, act, idx) =>
                {
                    _virtualCursorOverlay?.MoveCursor(x, y, act, idx);
                };
                _executor.OnVirtualCursorClicked = (x, y, btn) =>
                {
                    _virtualCursorOverlay?.PlayClickAnimation(x, y, btn);
                };
            }
            else
            {
                _executor.OnVirtualCursorMoved = null;
                _executor.OnVirtualCursorClicked = null;
            }

            _playbackHook = Hook.GlobalEvents();
            _playbackHook.KeyDown += PlaybackHook_KeyDown;

            int currentLoop = 0;
            bool success = true;
            
            while (!cts.IsCancellationRequested && (loopCount == 0 || currentLoop < loopCount))
            {
                currentLoop++;
                if (loopCount > 1 || loopCount == 0)
                    Log($"--- Memutar loop ke-{currentLoop} ---");

                // Jalankan di background thread agar UI tidak freeze dan hook bisa diproses
                _executor.SpeedMultiplier = _currentSpeed;
                var result = await Task.Run(() => _executor.ExecuteAsync(_recorder.Actions, cts.Token));
                if (!result.success)
                {
                    success = false;
                    _lastExecutionError = result.message;
                    Log($"[TERHENTI] {result.message}");
                    break;
                }

                // Tambahkan delay 1 detik agar tidak spam CPU/layar saat semua aksi gagal & di-skip
                try { await Task.Delay(1000, cts.Token); } catch (TaskCanceledException) { break; }
            }

            if (cts.IsCancellationRequested)
            {
                Log("Macro dihentikan secara paksa oleh pengguna.");
            }
            else if (success)
            {
                Log("Macro selesai.");
            }
            else
            {
                // Pengguna tidak ingin ada pesan error muncul, macro cukup berhenti saja (silent stop)
                Log("Macro terhenti.");
            }

            RestoreFromOverlay();

            // Bersihkan koneksi ADB agar sesi berikutnya bisa re-Initialize() dari awal
            AdbHelper.Cleanup();

            _playbackHook?.Dispose();
            _playbackHook = null;

            _currentStopAction = null;
            _currentPauseAction = null;
            _isPlaying = false;

            BtnPlay.Content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Play, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) }, new TextBlock { Text = "Play Macro", VerticalAlignment = VerticalAlignment.Center } } };
            BtnPlay.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2ecc71"));
            BtnPlay.IsEnabled = true;
            UpdateDashboardStats();
        }

        private void PlaybackHook_KeyDown(object sender, System.Windows.Forms.KeyEventArgs args)
        {
            if (args.KeyCode == System.Windows.Forms.Keys.F12)
            {
                if (_recorder.IsRecording) return; // Biarkan MacroRecorder yang menghandle
                _currentStopAction?.Invoke();
                args.Handled = true;
            }
            else if (args.KeyCode == System.Windows.Forms.Keys.F11)
            {
                if (_recorder.IsRecording) return; // Biarkan MacroRecorder yang menghandle
                _currentPauseAction?.Invoke();
                args.Handled = true;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _recorder.Actions?.Clear();
            _recorder.IsBackgroundRecording = false;
            _recorder.TargetWindowHandle = IntPtr.Zero;
            _recorder.TargetWindowTitle = null;
            RefreshTable();
            Log("[SISTEM] Semua aksi telah dihapus. Mode 'Run in Background' kini dapat diubah kembali.");
        }

        private string GetRecordingsDirectory()
        {
            try
            {
                string recDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");
                if (!Directory.Exists(recDir))
                {
                    string cwdRecDir = Path.Combine(Directory.GetCurrentDirectory(), "Recordings");
                    if (Directory.Exists(cwdRecDir)) return cwdRecDir;
                    Directory.CreateDirectory(recDir);
                }
                return recDir;
            }
            catch
            {
                return null;
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_recorder.Actions == null || _recorder.Actions.Count == 0)
            {
                MessageBox.Show("Tidak ada aksi makro untuk diekspor!", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var project = new Models.MacroProject
            {
                LoopCount = TxtLoopCount.Text,
                RecordMouse = ChkRecordMouse.IsChecked ?? false,
                SmartPlayback = ChkSmartPlayback.IsChecked ?? false,
                DynamicThreshold = ChkDynamicThreshold.IsChecked ?? false,
                IsBackgroundMode = ChkBackgroundMode.IsChecked ?? false,
                TargetWindowTitle = (CmbBackgroundWindow.SelectedItem as WindowInfo)?.Title ?? _recorder.TargetWindowTitle,
                Actions = _recorder.Actions,
                SpeedMultiplier = _currentSpeed
            };

            string recDir = GetRecordingsDirectory();
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "BMR Project Files (*.bmr)|*.bmr|JSON Files (*.json)|*.json",
                DefaultExt = "bmr",
                FileName = "MyMacro.bmr",
                InitialDirectory = !string.IsNullOrEmpty(recDir) ? recDir : null
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(project, Formatting.Indented);
                    File.WriteAllText(sfd.FileName, json);
                    MessageBox.Show("Makro berhasil diekspor!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal mengekspor file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            string recDir = GetRecordingsDirectory();
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "BMR Project Files (*.bmr)|*.bmr|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                InitialDirectory = !string.IsNullOrEmpty(recDir) ? recDir : null
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(ofd.FileName);
                    var project = JsonConvert.DeserializeObject<Models.MacroProject>(json);

                    if (project != null && project.Actions != null)
                    {
                        _recorder.Actions = project.Actions;
                        
                        // Restore settings
                        if (!string.IsNullOrEmpty(project.LoopCount)) TxtLoopCount.Text = project.LoopCount;
                        
                        ChkRecordMouse.IsChecked = project.RecordMouse;
                        ChkSmartPlayback.IsChecked = project.SmartPlayback;
                        ChkDynamicThreshold.IsChecked = project.DynamicThreshold;

                        // Tentukan mode background berdasarkan file yang diimpor
                        bool isBg = project.IsBackgroundMode ?? project.Actions.Any(a => !string.IsNullOrEmpty(a.ClickThumbnailBg) || a.RecordedWindowId > 0);
                        ChkBackgroundMode.IsChecked = isBg;
                        _recorder.IsBackgroundRecording = isBg;

                        if (isBg && !string.IsNullOrEmpty(project.TargetWindowTitle))
                        {
                            RefreshWindowList();
                            var match = (CmbBackgroundWindow.ItemsSource as List<WindowInfo>)?.FirstOrDefault(w => w.Title == project.TargetWindowTitle);
                            if (match != null)
                            {
                                CmbBackgroundWindow.SelectedValue = match.Handle;
                            }
                        }
                        
                        _currentSpeed = project.SpeedMultiplier > 0 ? project.SpeedMultiplier : 1.0;
                        _executor.SpeedMultiplier = _currentSpeed; // Sinkronkan ke executor juga
                        _overlayWindow.SetSpeedDisplay(_currentSpeed);

                        RefreshTable();
                        MessageBox.Show($"Makro berhasil diimpor! ({project.Actions.Count} aksi)", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("File tidak valid atau kosong.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal membaca file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshTable()
        {
            DgActions.ItemsSource = null;
            if (_recorder.Actions != null)
            {
                for (int i = 0; i < _recorder.Actions.Count; i++)
                {
                    _recorder.Actions[i].Index = i + 1;
                }
                DgActions.ItemsSource = _recorder.Actions;
            }
            UpdateDashboardStats();
            UpdateModeLockState();
        }

        // Kunci switch mode jika sudah ada rekaman agar mode background & normal benar-benar terpisah
        private void UpdateModeLockState()
        {
            if (ChkBackgroundMode == null) return;

            bool hasActions = _recorder.Actions != null && _recorder.Actions.Count > 0;
            if (hasActions)
            {
                // Mode terkunci: ikuti rekaman yang ada
                ChkBackgroundMode.IsEnabled = false;
                ChkBackgroundMode.ToolTip = "Mode terkunci sesuai rekaman yang ada. Untuk mengganti mode, hapus semua aksi terlebih dahulu (atau ekspor dulu).";
            }
            else
            {
                // Daftar aksi kosong: switch dapat diubah bebas
                ChkBackgroundMode.IsEnabled = true;
                ChkBackgroundMode.ToolTip = "Aktifkan untuk merekam dan menjalankan khusus jendela background (emulator/browser). Matikan untuk mode normal/layar penuh.";
            }

            UpdateRecordButtonState();
        }

        // Update statistik dashboard (Total Aksi, Interaksi, Estimasi Durasi, Status)
        private void UpdateDashboardStats()
        {
            if (TxtStatTotal == null) return;
            var actions = _recorder.Actions;
            int total = actions?.Count ?? 0;
            int interactions = 0;
            double duration = 0;

            if (actions != null)
            {
                foreach (var a in actions)
                {
                    string act = a.Action?.ToLower();
                    if (act != null && act != "move" && act != "wait")
                    {
                        interactions++;
                    }
                    if (a.Seconds.HasValue)
                    {
                        duration += a.Seconds.Value / _currentSpeed;
                    }
                }
            }

            TxtStatTotal.Text = total.ToString();
            TxtStatInteractions.Text = interactions.ToString();
            TxtStatDuration.Text = duration >= 60
                ? $"{(int)(duration / 60)}m {(int)(duration % 60)}s"
                : $"{duration:0.#}s";

            if (_recorder.IsRecording)
                TxtStatStatus.Text = "Recording";
            else if (_isPlaying)
                TxtStatStatus.Text = "Playing";
            else
                TxtStatStatus.Text = "Idle";
        }

        private void EditSingleWaitTime()
        {
            if (_recorder.Actions == null || _recorder.Actions.Count == 0) return;
            
            // Cari index aksi saat ini
            int currentIndex = _executor.CurrentActionIndex;
            if (currentIndex < 0 || currentIndex >= _recorder.Actions.Count)
                currentIndex = 0; // Default

            MacroAction targetAction = _recorder.Actions[currentIndex];

            bool wasPaused = _executor.IsPaused;
            if (!wasPaused) 
            {
                _executor.TogglePause(); // Pause sementara
                _overlayWindow.SetPauseState(true);
            }

            var dialog = new Window
            {
                Title = "Edit Waktu (Tunggal)",
                Width = 320,
                Height = 165,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Topmost = true,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#021C1E")),
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"))
            };

            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock { Text = "Masukkan waktu tunggu (detik):", Margin = new Thickness(0, 0, 0, 10), Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B9B9")) });
            
            var txt = new TextBox
            {
                Text = (targetAction.Seconds ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Margin = new Thickness(0, 0, 0, 15),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#021C1E")),
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#802C7873")),
                BorderThickness = new Thickness(1),
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0)
            };
            stack.Children.Add(txt);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "Simpan", Width = 70, Height = 32, Margin = new Thickness(0, 0, 10, 0), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2C7873")), Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF")), BorderThickness = new Thickness(0) };
            var btnCancel = new Button { Content = "Batal", Width = 70, Height = 32, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#004445")), Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B9B9")), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#802C7873")), BorderThickness = new Thickness(1) };
            
            btnOk.Click += (s, args) =>
            {
                if (double.TryParse(txt.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    targetAction.Seconds = val;
                    RefreshTable();
                    dialog.DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Nilai angka tidak valid!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            
            btnCancel.Click += (s, args) => dialog.DialogResult = false;

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            stack.Children.Add(btnPanel);
            
            dialog.Content = stack;
            
            dialog.Loaded += (s, args) =>
            {
                txt.Focus();
                txt.SelectAll();
            };

            dialog.ShowDialog();

            if (!wasPaused && _executor.IsPaused) 
            {
                _executor.TogglePause(); // Lanjutkan kembali
                _overlayWindow.SetPauseState(false);
            }
        }

        private void DgActions_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Cari baris yang diklik kanan dan otomatis di-select
            DependencyObject dep = (DependencyObject)e.OriginalSource;
            while ((dep != null) && !(dep is DataGridRow))
            {
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }

            if (dep is DataGridRow row)
            {
                row.IsSelected = true;
                DgActions.SelectedItem = row.DataContext;
            }
        }

        private void MenuEditThreshold_Click(object sender, RoutedEventArgs e)
        {
            if (DgActions.SelectedIndex < 0 || _recorder.Actions == null || DgActions.SelectedIndex >= _recorder.Actions.Count) return;

            var action = _recorder.Actions[DgActions.SelectedIndex];
            double defThresh = (_recorder.IsBackgroundRecording || ChkBackgroundMode.IsChecked == true) ? 70.0 : 90.0;
            string currentVal = (action.MatchThreshold ?? defThresh).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string input = PromptInputDialog("Presentase Kecocokan", $"Masukkan minimal persentase kecocokan (1 - 100, default {(int)defThresh}):", currentVal);

            if (!string.IsNullOrEmpty(input) && double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                if (val < 1) val = 1;
                if (val > 100) val = 100;
                action.MatchThreshold = val;
                Log($"[EDIT] Persentase kecocokan baris {DgActions.SelectedIndex + 1} diubah menjadi {val}%.");
                RefreshTable();
            }
        }

        private void MenuEditSmartWait_Click(object sender, RoutedEventArgs e)
        {
            if (DgActions.SelectedIndex < 0 || _recorder.Actions == null || DgActions.SelectedIndex >= _recorder.Actions.Count) return;

            var action = _recorder.Actions[DgActions.SelectedIndex];
            string currentVal = (action.SmartWait ?? 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string input = PromptInputDialog("Waktu Tunggu", "Masukkan waktu tunggu (detik) setelah target ditemukan:", currentVal);

            if (!string.IsNullOrEmpty(input) && double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                if (val < 0) val = 0;
                action.SmartWait = val;
                Log($"[EDIT] Waktu tunggu baris {DgActions.SelectedIndex + 1} diubah menjadi {val} detik.");
                RefreshTable();
            }
        }

        private string PromptInputDialog(string title, string prompt, string defaultValue = "")
        {
            var window = new Window
            {
                Title = title,
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#021C1E")),
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"))
            };

            var stackPanel = new StackPanel { Margin = new Thickness(12) };
            var textBlock = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10), Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B3B9B9")) };
            var textBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Text = defaultValue,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#021C1E")),
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#802C7873")),
                BorderThickness = new Thickness(1),
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0)
            };
            var okButton = new Button { Content = "OK", Width = 100, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2C7873")), Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF")), BorderThickness = new Thickness(0) };

            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(okButton);
            window.Content = stackPanel;

            bool isThreshold = title.ToLower().Contains("threshold");
            bool isNumeric = isThreshold || title.ToLower().Contains("sisipkan");

            if (isNumeric)
            {
                textBox.PreviewTextInput += (s, e) =>
                {
                    e.Handled = !e.Text.All(char.IsDigit);
                };

                textBox.TextChanged += (s, e) =>
                {
                    string txt = textBox.Text;
                    if (!string.IsNullOrEmpty(txt) && !txt.All(char.IsDigit))
                    {
                        textBox.Text = new string(txt.Where(char.IsDigit).ToArray());
                        textBox.SelectionStart = textBox.Text.Length;
                    }

                    if (isThreshold && int.TryParse(textBox.Text, out int val))
                    {
                        if (val > 100)
                        {
                            textBox.Text = "100";
                            textBox.SelectionStart = textBox.Text.Length;
                        }
                    }
                };
            }

            // Auto select all text for fast editing
            window.Loaded += (s, e) => {
                textBox.Focus();
                textBox.SelectAll();
            };

            okButton.Click += (s, ev) =>
            {
                window.DialogResult = true;
                window.Close();
            };

            if (window.ShowDialog() == true)
            {
                return textBox.Text;
            }
            return null;
        }

        private void MenuDeleteAction_Click(object sender, RoutedEventArgs e)
        {
            if (DgActions.SelectedIndex < 0) return;

            var result = MessageBox.Show(
                $"Apakah Anda yakin ingin menghapus aksi baris {DgActions.SelectedIndex + 1}?", 
                "Hapus Aksi", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                _recorder.Actions.RemoveAt(DgActions.SelectedIndex);
                RefreshTable();
                
                int interactionCount = 0;
                foreach (var a in _recorder.Actions)
                {
                    string act = a.Action?.ToLower();
                    if (act != "move" && act != "wait")
                    {
                        interactionCount++;
                    }
                }
                
                _overlayWindow.SetupIterationIndicator(interactionCount);
                Log("Aksi berhasil dihapus.");
            }
        }

private void EditActionThreshold()
        {
            var targetAction = _executor.CurrentExecutingAction;
            if (targetAction == null)
            {
                Log("[SMART] Tidak ada aksi target saat ini.");
                return;
            }
            
            bool wasPaused = _executor.IsPaused;
            if (!wasPaused)
            {
                _executor.TogglePause();
                _overlayWindow.SetPauseState(true);
            }
            
            double defThresh = _executor.IsBackgroundMode ? 70.0 : 90.0;
            string currentVal = (targetAction.MatchThreshold ?? defThresh).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string input = PromptInputDialog("Edit Threshold", $"Masukkan minimal persentase kecocokan (default {(int)defThresh}):", currentVal);
            
            if (!string.IsNullOrEmpty(input) && double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                targetAction.MatchThreshold = val;
                Log($"[SMART] Threshold baris {_executor.CurrentActionIndex + 1} diubah menjadi {val}%");
                RefreshTable();
            }
            
            if (!wasPaused)
            {
                _executor.TogglePause();
                _overlayWindow.SetPauseState(false);
            }
        }

        private void EditActionSmartWait()
        {
            var targetAction = _executor.CurrentExecutingAction;
            if (targetAction == null)
            {
                Log("[SMART] Tidak ada aksi target saat ini.");
                return;
            }
            
            bool wasPaused = _executor.IsPaused;
            if (!wasPaused)
            {
                _executor.TogglePause();
                _overlayWindow.SetPauseState(true);
            }
            
            string currentVal = (targetAction.SmartWait ?? 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string input = PromptInputDialog("Edit Waktu Tunggu", "Masukkan waktu tunggu (detik) setelah gambar target ditemukan (default 2):", currentVal);
            
            if (!string.IsNullOrEmpty(input) && double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                targetAction.SmartWait = val;
                Log($"[SMART] Waktu tunggu baris {_executor.CurrentActionIndex + 1} diubah menjadi {val} detik.");
                RefreshTable();
            }
            
            if (!wasPaused)
            {
                _executor.TogglePause();
                _overlayWindow.SetPauseState(false);
            }
        }

        private void Recorder_OnRequestDynamicThreshold(MacroAction action)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _recorder.IsPaused = true;
                _overlayWindow.SetPauseState(true);
                
                double defThresh = _recorder.IsBackgroundRecording ? 70.0 : 90.0;
                string defaultStr = ((int)defThresh).ToString();
                string input = PromptInputDialog("Dynamic Threshold", $"Masukkan minimal persentase kecocokan (default {defaultStr}):", defaultStr);
                if (!string.IsNullOrEmpty(input) && double.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    action.MatchThreshold = val;
                }
                
                _recorder.IsPaused = false;
                _overlayWindow.SetPauseState(false);
                _recorder.ResetLastActionTime();
            }));
        }

        private void ChkBackgroundMode_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (PnlBackgroundOptions != null)
            {
                if (ChkBackgroundMode.IsChecked == true)
                {
                    PnlBackgroundOptions.Visibility = Visibility.Visible;
                    RefreshWindowList();
                }
                else
                {
                    PnlBackgroundOptions.Visibility = Visibility.Collapsed;
                }
            }
            UpdateRecordButtonState();
        }

        private void UpdateRecordButtonState()
        {
            if (BtnRecord == null) return;

            if (_recorder != null && _recorder.IsRecording)
            {
                BtnRecord.Content = new StackPanel { 
                    Orientation = System.Windows.Controls.Orientation.Horizontal, 
                    Children = { 
                        new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.StopCircle, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0), Width = 18, Height = 18 }, 
                        new TextBlock { Text = "Stop (F12)", VerticalAlignment = VerticalAlignment.Center } 
                    } 
                };
                BtnRecord.Background = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                string label = ChkBackgroundMode?.IsChecked == true ? "Record (Background)" : "Record (Normal)";
                BtnRecord.Content = new StackPanel { 
                    Orientation = System.Windows.Controls.Orientation.Horizontal, 
                    Children = { 
                        new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.RecordCircle, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0), Width = 18, Height = 18 }, 
                        new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } 
                    } 
                };
                BtnRecord.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#e74c3c"));
            }
        }

        private void BtnRefreshWindows_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void RefreshWindowList()
        {
            var windows = WindowUtils.GetOpenWindows();
            IntPtr currentSelected = CmbBackgroundWindow.SelectedValue is IntPtr h ? h : IntPtr.Zero;
            CmbBackgroundWindow.ItemsSource = windows;
            
            if (currentSelected != IntPtr.Zero && windows.Any(w => w.Handle == currentSelected))
            {
                CmbBackgroundWindow.SelectedValue = currentSelected;
            }
            else if (windows.Count > 0)
            {
                CmbBackgroundWindow.SelectedIndex = 0;
            }
        }
    }

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
    }

    public static class WindowUtils
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

        [System.Runtime.InteropServices.DllImport("USER32.DLL")]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        [System.Runtime.InteropServices.DllImport("USER32.DLL")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("USER32.DLL")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("USER32.DLL")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("USER32.DLL")]
        private static extern IntPtr GetShellWindow();

        public static List<WindowInfo> GetOpenWindows()
        {
            IntPtr shellWindow = GetShellWindow();
            List<WindowInfo> windows = new List<WindowInfo>();

            EnumWindows(delegate (IntPtr hWnd, int lParam)
            {
                if (hWnd == shellWindow) return true;
                if (!IsWindowVisible(hWnd)) return true;

                int length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                System.Text.StringBuilder builder = new System.Text.StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                
                string title = builder.ToString();
                if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                {
                    windows.Add(new WindowInfo { Handle = hWnd, Title = title });
                }
                return true;

            }, 0);

            return windows.OrderBy(w => w.Title).ToList();
        }
    }
}
