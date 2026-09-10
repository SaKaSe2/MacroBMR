using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using MacroBMR.Models;
using WindowsInput;
using WindowsInput.Native;

namespace MacroBMR.Core
{
    public class MacroExecutor
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowThreadProcessIdPid(IntPtr hWnd, out uint processId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const uint SWP_NOACTIVATE = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        
        private const uint GW_CHILD = 5;
        private const uint GW_HWNDNEXT = 2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        
        public bool IsBackgroundMode { get; set; } = false;
        public bool IsOffscreenMode { get; set; } = false;
        public IntPtr BackgroundWindowHandle { get; set; } = IntPtr.Zero;

        // State tracking untuk klik background (berguna untuk Drag & Drop)
        private bool _bgLeftMouseDown = false;
        private bool _bgRightMouseDown = false;
        private bool _hasLoggedOffscreen = false;

        // Retry inisialisasi ADB & peringatan capture Background (PrintWindow blank / ADB mati)
        private bool _adbRetryAttempted = false;
        private bool _warnedBgCaptureUnavailable = false;

        // Batasi frekuensi switch jendela otomatis (judul game/emulator bisa berubah dinamis)
        private DateTime _lastSmartSwitchTime = DateTime.MinValue;

        private double _lastExecMouseX = -1;
        private double _lastExecMouseY = -1;

        private readonly InputSimulator _sim = new InputSimulator();
        private readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
        private readonly Random _rng = new Random();

        // Mapping nama key umum ke VirtualKeyCode (static agar tidak dibuat ulang tiap panggil)
        private static readonly Dictionary<string, VirtualKeyCode> _keyMap = new Dictionary<string, VirtualKeyCode>(StringComparer.OrdinalIgnoreCase)
        {
            { "ctrl", VirtualKeyCode.CONTROL },
            { "control", VirtualKeyCode.CONTROL },
            { "alt", VirtualKeyCode.MENU },
            { "shift", VirtualKeyCode.SHIFT },
            { "enter", VirtualKeyCode.RETURN },
            { "return", VirtualKeyCode.RETURN },
            { "tab", VirtualKeyCode.TAB },
            { "esc", VirtualKeyCode.ESCAPE },
            { "escape", VirtualKeyCode.ESCAPE },
            { "space", VirtualKeyCode.SPACE },
            { "backspace", VirtualKeyCode.BACK },
            { "delete", VirtualKeyCode.DELETE },
            { "del", VirtualKeyCode.DELETE },
            { "up", VirtualKeyCode.UP },
            { "down", VirtualKeyCode.DOWN },
            { "left", VirtualKeyCode.LEFT },
            { "right", VirtualKeyCode.RIGHT },
            { "home", VirtualKeyCode.HOME },
            { "end", VirtualKeyCode.END },
            { "pageup", VirtualKeyCode.PRIOR },
            { "pagedown", VirtualKeyCode.NEXT },
            { "f1", VirtualKeyCode.F1 },
            { "f2", VirtualKeyCode.F2 },
            { "f3", VirtualKeyCode.F3 },
            { "f4", VirtualKeyCode.F4 },
            { "f5", VirtualKeyCode.F5 },
            { "f6", VirtualKeyCode.F6 },
            { "f7", VirtualKeyCode.F7 },
            { "f8", VirtualKeyCode.F8 },
            { "f9", VirtualKeyCode.F9 },
            { "f10", VirtualKeyCode.F10 },
            { "f11", VirtualKeyCode.F11 },
            { "f12", VirtualKeyCode.F12 },
            { "win", VirtualKeyCode.LWIN },
            { "windows", VirtualKeyCode.LWIN },
        };
        
        public bool IsPaused => !_pauseEvent.IsSet;
        
        // Anti-deteksi: tambahkan variasi acak pada input agar terlihat manusiawi (Dinonaktifkan agar presisi)
        public bool HumanizeInput { get; set; } = false;
        
        // Smart Playback: koreksi klik menggunakan pencocokan template
        public bool SmartPlayback { get; set; } = false;
        
        public Action<string> OnLog { get; set; }
        
        public MacroAction CurrentExecutingAction { get; private set; }
        
        // Index aksi yang sedang berjalan (untuk fitur rekam ulang target)
        public int CurrentActionIndex { get; private set; } = -1;

        // Properti untuk melompat ke iterasi tertentu
        public int? JumpToIndex { get; set; }
        
        // Pengali kecepatan eksekusi (bisa diubah real-time)
        public double SpeedMultiplier { get; set; } = 1.0;
        
        // Event untuk memberi tahu UI tentang sisa hitung mundur (countdown)
        public Action<double> OnWaitCountdown { get; set; }

        // Event untuk memberi tahu UI tentang perubahan iterasi
        public Action<int, int> OnActionIndexChanged { get; set; }

        // Event kursor virtual untuk penanda visual playback
        public Action<double, double, string, int> OnVirtualCursorMoved { get; set; }
        public Action<double, double, string> OnVirtualCursorClicked { get; set; }

        public void ForceUpdateCurrentAction(int index, MacroAction action)
        {
            CurrentActionIndex = index;
            CurrentExecutingAction = action;
        }

        public void TogglePause()
        {
            if (IsPaused) _pauseEvent.Set();
            else _pauseEvent.Reset();
        }

        // Tambahkan delay acak kecil antar-aksi agar timing tidak terlalu presisi/mesin
        private async Task HumanDelay(CancellationToken ct)
        {
            if (!HumanizeInput) return;
            int delayMs = _rng.Next(15, 80);
            await Task.Delay(delayMs, ct);
        }

        private async Task<(bool success, string reason)> WaitPreciseAsync(int waitMs, CancellationToken ct, bool updateCountdown = false)
        {
            if (waitMs <= 0) return (true, "");
            var swWait = System.Diagnostics.Stopwatch.StartNew();
            long lastCountdownMs = -100;

            while (swWait.ElapsedMilliseconds < waitMs)
            {
                if (ct.IsCancellationRequested) return (false, "Batal_CancelToken");
                if (JumpToIndex.HasValue) return (false, "Batal_Jump");
                _pauseEvent.Wait(ct);
                ApplyOffscreenWindow(); // Jaga jendela target tetap di belakang saat menunggu
                
                long elapsed = swWait.ElapsedMilliseconds;
                if (updateCountdown && waitMs > 50)
                {
                    if (elapsed - lastCountdownMs >= 100)
                    {
                        lastCountdownMs = elapsed;
                        double remaining = (waitMs - elapsed) / 1000.0;
                        if (remaining < 0) remaining = 0;
                        OnWaitCountdown?.Invoke(remaining);
                    }
                }
                
                int rem = waitMs - (int)elapsed;
                if (rem > 15) await Task.Delay(15, ct);
                else await Task.Delay(1, ct);
            }
            if (updateCountdown && waitMs > 50) OnWaitCountdown?.Invoke(0);
            return (true, "");
        }

        public async Task<(bool success, string message)> ExecuteAsync(List<MacroAction> actions, CancellationToken ct = default)
        {
            // Set mode background untuk ScreenCapture
            ScreenCapture.BackgroundWindowHandle = IsBackgroundMode ? BackgroundWindowHandle : IntPtr.Zero;

            if (IsBackgroundMode)
            {
                AdbHelper.OnLog = (msg) => OnLog?.Invoke(msg);
                AdbHelper.Initialize();
                if (AdbHelper.IsConnected)
                {
                    OnLog?.Invoke("[ADB] Kernel Touch Injector aktif. Menggunakan metode sendevent Android.");
                    OnLog?.Invoke("[BACKGROUND] Frame capture memakai ADB screencap (tahan jendela tertutup/minimize).");
                }
            }

            // Reset state klik background
            _bgLeftMouseDown = false;
            _bgRightMouseDown = false;
            _lastExecMouseX = -1;
            _lastExecMouseY = -1;

            // Reset pause state agar tidak stuck dari sesi sebelumnya
            _pauseEvent.Set();
            OnWaitCountdown?.Invoke(0); // Reset countdown
            
            CurrentActionIndex = -1;
            CurrentExecutingAction = null;

_hasLoggedOffscreen = false;
            _adbRetryAttempted = false;
            _warnedBgCaptureUnavailable = false;

            try
            {
                ScreenCapture.BackgroundWindowHandle = (IsBackgroundMode && BackgroundWindowHandle != IntPtr.Zero) ? BackgroundWindowHandle : IntPtr.Zero;
                // PrintWindow hanya digunakan saat mode Hide aktif (window tersembunyi di belakang)
                // Tanpa Hide, window visible di layar cukup CopyFromScreen (zero flicker)
                ScreenCapture.IsOffscreenCapture = IsOffscreenMode;
                ScreenCapture.OffscreenLocked = IsBackgroundMode && IsOffscreenMode && !AdbHelper.IsConnected;
                ApplyOffscreenWindow();

                for (int i = 0; i < actions.Count; i++)
                {
                    ApplyOffscreenWindow(); // Jaga agar jendela target selalu terlempar ke latar paling belakang jika pengguna membuka/mengklik jendela
                    if (i % 30 == 0) ScreenCapture.ClearCache(); // Bersihkan RAM & bitmap cache secara periodik agar tidak memicu lag/crash saat berjalan lama

                    if (JumpToIndex.HasValue)
                    {
                        i = JumpToIndex.Value;
                        JumpToIndex = null;
                        if (i >= actions.Count) break;
                    }

                    var action = actions[i];
                    CurrentExecutingAction = action;
                    CurrentActionIndex = i;
                    OnActionIndexChanged?.Invoke(i + 1, actions.Count);

                    // Posisikan kursor virtual di awal hanya jika belum pernah diposisikan atau aksi instan tanpa delay/path
                    if (_lastExecMouseX < 0 || _lastExecMouseY < 0)
                    {
                        if (action.Path != null && action.Path.Count > 0)
                        {
                            _lastExecMouseX = action.Path[0].X;
                            _lastExecMouseY = action.Path[0].Y;
                        }
                        else if (action.X.HasValue && action.Y.HasValue)
                        {
                            _lastExecMouseX = action.X.Value;
                            _lastExecMouseY = action.Y.Value;
                        }
                        if (_lastExecMouseX >= 0 && _lastExecMouseY >= 0)
                        {
                            OnVirtualCursorMoved?.Invoke(_lastExecMouseX, _lastExecMouseY, "start", i + 1);
                        }
                    }
                    else if (!action.Seconds.HasValue && action.X.HasValue && action.Y.HasValue)
                    {
                        OnVirtualCursorMoved?.Invoke(action.X.Value, action.Y.Value, action.Action ?? "action", i + 1);
                    }
                    
                    if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.");
                    
                    // Tunggu jika sedang di-pause
                    _pauseEvent.Wait(ct);

                    if (JumpToIndex.HasValue)
                    {
                        i = JumpToIndex.Value - 1; // -1 karena iterasi loop akan melakukan i++
                        JumpToIndex = null;
                        continue;
                    }

                    // PROSES DELAY & GERAKAN MOUSE BERSAMAAN (JIKA ADA)
                    if (action.Seconds.HasValue)
                    {
                        double adjustedSeconds = action.Seconds.Value / SpeedMultiplier;
                        int totalMs = (int)(adjustedSeconds * 1000);
                        
                        if (totalMs > 0)
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();

                            // Pre-compute timestamp kumulatif untuk setiap titik path (dalam ms, sudah disesuaikan SpeedMultiplier)
                            List<MousePoint> effectivePath = action.Path;
                            if ((effectivePath == null || effectivePath.Count == 0) && action.X.HasValue && action.Y.HasValue)
                            {
                                // Generate pergerakan fiktif jika makro lama tidak memiliki path
                                double curX = _lastExecMouseX;
                                double curY = _lastExecMouseY;

                                if (curX < 0 || curY < 0)
                                {
                                    if (IsBackgroundMode)
                                    {
                                        curX = action.X.Value;
                                        curY = action.Y.Value;
                                    }
                                    else
                                    {
                                        var curPos = System.Windows.Forms.Cursor.Position;
                                        curX = curPos.X;
                                        curY = curPos.Y;
                                    }
                                }

                                double tgtX = action.X.Value;
                                double tgtY = action.Y.Value;
                                double dist = Math.Sqrt(Math.Pow(tgtX - curX, 2) + Math.Pow(tgtY - curY, 2));

                                if (dist > 2)
                                {
                                    effectivePath = new List<MousePoint>();
                                    int numPoints = Math.Max(5, Math.Min(25, (int)(totalMs / 25.0)));
                                    double secPerPoint = action.Seconds.Value / numPoints;

                                    for (int p = 0; p < numPoints; p++)
                                    {
                                        double t = (double)p / (numPoints - 1);
                                        double tSmooth = t * t * (3.0 - 2.0 * t);
                                        int px = (int)Math.Round(curX + (tgtX - curX) * tSmooth);
                                        int py = (int)Math.Round(curY + (tgtY - curY) * tSmooth);

                                        effectivePath.Add(new MousePoint
                                        {
                                            X = px,
                                            Y = py,
                                            Delay = secPerPoint
                                        });
                                    }
                                }
                            }

                            double[] pathTimestamps = null;
                            if (effectivePath != null && effectivePath.Count > 0)
                            {
                                pathTimestamps = new double[effectivePath.Count];
                                double cumMs = 0;
                                for (int pi = 0; pi < effectivePath.Count; pi++)
                                {
                                    cumMs += effectivePath[pi].Delay * 1000.0 / SpeedMultiplier;
                                    pathTimestamps[pi] = cumMs;
                                }
                            }

                            double screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                            double screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                            int lastRenderedIndex = -1;
                            
                            while (sw.ElapsedMilliseconds < totalMs)
                            {
                                if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.");
                                if (JumpToIndex.HasValue) break;
                                if (!_pauseEvent.IsSet) _pauseEvent.Wait(ct);
                                ApplyOffscreenWindow(); // Jaga jendela target tetap di belakang saat delay/path
                                
                                double elapsed = sw.Elapsed.TotalMilliseconds;
                                double remaining = (totalMs - elapsed) / 1000.0;
                                if (remaining < 0) remaining = 0;
                                if (totalMs > 50) OnWaitCountdown?.Invoke(remaining);
                                
                                if (pathTimestamps != null && pathTimestamps.Length > 0)
                                {
                                    if (elapsed <= pathTimestamps[0])
                                    {
                                        // Belum sampai titik pertama
                                    }
                                    else if (elapsed >= pathTimestamps[pathTimestamps.Length - 1])
                                    {
                                        if (lastRenderedIndex < pathTimestamps.Length - 1)
                                        {
                                            var lastPt = effectivePath[pathTimestamps.Length - 1];
                                            _lastExecMouseX = lastPt.X;
                                            _lastExecMouseY = lastPt.Y;
                                            OnVirtualCursorMoved?.Invoke(lastPt.X, lastPt.Y, "move", i + 1);

                                            if (IsBackgroundMode)
                                            {
                                                if (AdbHelper.IsConnected)
                                                {
                                                    await AdbTouchMove((int)lastPt.X, (int)lastPt.Y);
                                                }
                                                else
                                                {
                                                    IntPtr target = BackgroundWindowHandle;
                                                    SendBackgroundMouse(target, (int)lastPt.X, (int)lastPt.Y, WM_MOUSEMOVE);
                                                }
                                            }
                                            else
                                            {
                                                double vx = (lastPt.X / screenWidth) * 65535;
                                                double vy = (lastPt.Y / screenHeight) * 65535;
                                                _sim.Mouse.MoveMouseToPositionOnVirtualDesktop(vx, vy);
                                            }
                                            lastRenderedIndex = pathTimestamps.Length - 1;
                                        }
                                    }
                                    else
                                    {
                                        int nextIdx = 0;
                                        for (int pi = 0; pi < pathTimestamps.Length; pi++)
                                        {
                                            if (pathTimestamps[pi] > elapsed) { nextIdx = pi; break; }
                                        }
                                        int prevIdx = nextIdx > 0 ? nextIdx - 1 : 0;
                                        var prevPt = effectivePath[prevIdx];
                                        var nextPt = effectivePath[nextIdx];
                                        double prevTime = prevIdx > 0 ? pathTimestamps[prevIdx] : 0;
                                        double nextTime = pathTimestamps[nextIdx];
                                        double segmentDuration = nextTime - prevTime;
                                        double interpX, interpY;
                                        if (segmentDuration > 0)
                                        {
                                            double t = (elapsed - prevTime) / segmentDuration;
                                            interpX = prevPt.X + (nextPt.X - prevPt.X) * t;
                                            interpY = prevPt.Y + (nextPt.Y - prevPt.Y) * t;
                                        }
                                        else
                                        {
                                            interpX = nextPt.X;
                                            interpY = nextPt.Y;
                                        }
                                        
                                        _lastExecMouseX = interpX;
                                        _lastExecMouseY = interpY;
                                        OnVirtualCursorMoved?.Invoke(interpX, interpY, "move", i + 1);

                                        if (IsBackgroundMode)
                                        {
                                            if (AdbHelper.IsConnected)
                                            {
                                                await AdbTouchMove((int)interpX, (int)interpY);
                                            }
                                            else
                                            {
                                                IntPtr target = BackgroundWindowHandle;
                                                SendBackgroundMouse(target, (int)interpX, (int)interpY, WM_MOUSEMOVE);
                                            }
                                        }
                                        else
                                        {
                                            double vx2 = (interpX / screenWidth) * 65535;
                                            double vy2 = (interpY / screenHeight) * 65535;
                                            _sim.Mouse.MoveMouseToPositionOnVirtualDesktop(vx2, vy2);
                                        }
                                        lastRenderedIndex = prevIdx;
                                    }
                                }
                                
                                if (IsBackgroundMode)
                                {
                                    // Rate limit pergerakan background agar tidak flood antrian pesan
                                    await WaitPreciseAsync(15, ct);
                                }
                                else
                                {
                                    await Task.Delay(2, ct);
                                }
                            }
                            if (totalMs > 50) OnWaitCountdown?.Invoke(0);
                        }
                    }

                    switch (action.Action?.ToLower())
                    {
                        case "open_app":
                            if (!string.IsNullOrEmpty(action.AppName))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "cmd.exe",
                                    Arguments = $"/c start {action.AppName}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                });
                            }
                            break;

                        case "switch_window":
                            // Saat background mode, skip switch_window karena input sudah dikirim via PostMessage
                            if (IsBackgroundMode)
                            {
                                OnLog?.Invoke($"[BG] switch_window dilewati (mode background aktif).");
                                break;
                            }
                            if (!string.IsNullOrEmpty(action.AppName))
                            {
                                // Cegah makro membuka paksa dashboard aplikasi ini sendiri saat Playback berjalan
                                if (action.AppName.ToLower().Contains("bmr") || action.AppName.ToLower().Contains("macro studio"))
                                {
                                    OnLog?.Invoke($"[SMART] Makro mencoba membuka dashboard sendiri. Aksi ini diblokir.");
                                    break;
                                }

                                var proc = System.Diagnostics.Process.GetProcesses()
                                    .FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle) && 
                                                    p.MainWindowTitle.ToLower().Contains(action.AppName.ToLower()));
                                
                                if (proc != null)
                                {
                                    if (IsIconic(proc.MainWindowHandle)) ShowWindow(proc.MainWindowHandle, 9); // SW_RESTORE
                                    else ShowWindow(proc.MainWindowHandle, 5); // SW_SHOW
                                    
                                    // Bypass Windows limitation on SetForegroundWindow
                                    IntPtr foregroundWindow = GetForegroundWindow();
                                    uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
                                    uint targetThreadId = GetWindowThreadProcessId(proc.MainWindowHandle, IntPtr.Zero);

                                    if (foregroundThreadId != targetThreadId)
                                    {
                                        AttachThreadInput(foregroundThreadId, targetThreadId, true);
                                        SetForegroundWindow(proc.MainWindowHandle);
                                        AttachThreadInput(foregroundThreadId, targetThreadId, false);
                                    }
                                    else
                                    {
                                        SetForegroundWindow(proc.MainWindowHandle);
                                    }

                                    // Ultimate bypass: SwitchToThisWindow + SetWindowPos TopMost
                                    SwitchToThisWindow(proc.MainWindowHandle, true);
                                    SetWindowPos(proc.MainWindowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                                    SetWindowPos(proc.MainWindowHandle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

                                    await Task.Delay(500, ct); // Wait for animation
                                }
                                else
                                {
                                    OnLog?.Invoke($"[SMART] Tidak dapat menemukan jendela aplikasi: {action.AppName}");
                                }
                            }
                            break;

                        case "wait":
                            // Wait logic is handled above, so here we do nothing
                            break;

                        case "type":
                            if (!string.IsNullOrEmpty(action.Text))
                            {
                                foreach (char c in action.Text)
                                {
                                    if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.");
                                    _pauseEvent.Wait(ct);

                                    if (IsBackgroundMode)
                                    {
                                        IntPtr target = BackgroundWindowHandle;
                                        if (target != IntPtr.Zero)
                                        {
                                            if (c == ' ') SendBackgroundKey(target, VirtualKeyCode.SPACE, true);
                                            else
                                            {
                                                PostMessage(target, 0x0102 /* WM_CHAR */, c, 0);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (c == ' ')
                                            _sim.Keyboard.KeyPress(VirtualKeyCode.SPACE);
                                        else
                                            _sim.Keyboard.TextEntry(c);
                                    }
                                    
                                    await Task.Delay(30, ct); // Delay kecil antar karakter agar stabil
                                }
                            }
                            break;

                        case "press":
                            if (!string.IsNullOrEmpty(action.Key))
                            {
                                var resolved = ResolveKey(action.Key);
                                if (resolved.HasValue)
                                {
                                    if (action.Duration.HasValue && action.Duration.Value > 0)
                                    {
                                        if (IsBackgroundMode)
                                        {
                                            IntPtr target = BackgroundWindowHandle;
                                            SendBackgroundKey(target, resolved.Value, true);
                                            var waitRes1 = await WaitPreciseAsync((int)(action.Duration.Value * 1000), ct);
                                            if (!waitRes1.success) return (false, waitRes1.reason);
                                            SendBackgroundKey(target, resolved.Value, false);
                                        }
                                        else
                                        {
                                            _sim.Keyboard.KeyDown(resolved.Value);
                                            var waitRes2 = await WaitPreciseAsync((int)(action.Duration.Value * 1000), ct);
                                            if (!waitRes2.success) return (false, waitRes2.reason);
                                            _sim.Keyboard.KeyUp(resolved.Value);
                                        }
                                    }
                                    else
                                    {
                                        if (IsBackgroundMode)
                                        {
                                            IntPtr target = BackgroundWindowHandle;
                                            SendBackgroundKey(target, resolved.Value, true);
                                            SendBackgroundKey(target, resolved.Value, false);
                                        }
                                        else
                                        {
                                            _sim.Keyboard.KeyPress(resolved.Value);
                                        }
                                    }
                                }
                            }
                            break;

                        case "mousedown":
                        case "click":
                            if (action.X.HasValue && action.Y.HasValue)
                            {
                                int clickX = (int)Math.Round(action.X.Value);
                                int clickY = (int)Math.Round(action.Y.Value);
                                
                                bool usedSmartPlayback = false;
                                if (SmartPlayback && !string.IsNullOrEmpty(action.ClickThumbnail))
                                {
                                    var result = await SmartPlaybackFindTarget(action, clickX, clickY, ct);
                                    if (!result.success) return (false, result.message);
                                    if (result.jumped) continue;
                                    clickX = result.clickX;
                                    clickY = result.clickY;
                                    usedSmartPlayback = true;
                                }

                                if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.");

                                OnVirtualCursorMoved?.Invoke(clickX, clickY, action.Action ?? "click", i + 1);
                                OnVirtualCursorClicked?.Invoke(clickX, clickY, action.Button ?? "left");

                                // Lakukan SmartWait HANYA jika SmartPlayback menemukan target (dan sebelum klik eksekusi)
                                if (usedSmartPlayback)
                                {
                                    int waitMs = (int)((action.SmartWait ?? 2.0) * 1000);
                                    if (waitMs > 0)
                                    {
                                        OnLog?.Invoke($"[SMART] Memulai waktu tunggu sebelum klik ({waitMs}ms)...");
                                        var waitRes3 = await WaitPreciseAsync(waitMs, ct, true);
                                        if (!waitRes3.success)
                                        {
                                            if (waitRes3.reason == "Batal_Jump") continue;
                                            return (false, waitRes3.reason);
                                        }
                                    }
                                }

                                if (IsBackgroundMode)
                                {
                                    IntPtr target = BackgroundWindowHandle;
                                    SendBackgroundMouse(target, clickX, clickY, WM_MOUSEMOVE);

                                    if (action.Action?.ToLower() == "mousedown")
                                    {
                                        if (AdbHelper.IsConnected)
                                        {
                                            await AdbTouchDown(clickX, clickY);
                                        }
                                        else
                                        {
                                            SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONDOWN, action.Button);
                                        }
                                    }
                                    else
                                    {
                                        if (action.Duration.HasValue && action.Duration.Value > 0)
                                        {
                                            int totalHoldMs = (int)(action.Duration.Value * 1000);
                                            if (AdbHelper.IsConnected)
                                            {
                                                await AdbTouchHold(clickX, clickY, totalHoldMs);
                                            }
                                            else
                                            {
                                                SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONDOWN, action.Button);
                                                
                                                var swHold = System.Diagnostics.Stopwatch.StartNew();
                                                while (swHold.ElapsedMilliseconds < totalHoldMs)
                                                {
                                                    if (ct.IsCancellationRequested) break;
                                                    SendBackgroundMouse(target, clickX, clickY, WM_MOUSEMOVE);
                                                    int waitRem = totalHoldMs - (int)swHold.ElapsedMilliseconds;
                                                    if (waitRem > 20) await Task.Delay(20);
                                                    else { await Task.Delay(Math.Max(1, waitRem)); break; }
                                                }
                                                SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONUP, action.Button);
                                            }
                                        }
                                        else
                                        {
                                            if (AdbHelper.IsConnected)
                                            {
                                                await AdbTouchDown(clickX, clickY);
                                                await Task.Delay(Math.Max(5, (int)(50 / SpeedMultiplier)));
                                                await AdbTouchUp();
                                            }
                                            else
                                            {
                                                SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONDOWN, action.Button);
                                                await Task.Delay(Math.Max(5, (int)(150 / SpeedMultiplier)));
                                                SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONUP, action.Button);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    double screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                                    double screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                                    double x = ((double)clickX / screenWidth) * 65535;
                                    double y = ((double)clickY / screenHeight) * 65535;
                                    _sim.Mouse.MoveMouseToPositionOnVirtualDesktop(x, y);

                                    // Eksekusi klik
                                    if (action.Action?.ToLower() == "mousedown")
                                    {
                                        if (action.Button == "right") _sim.Mouse.RightButtonDown();
                                        else _sim.Mouse.LeftButtonDown();
                                    }
                                    else
                                    {
                                        if (action.Duration.HasValue && action.Duration.Value > 0)
                                        {
                                            if (action.Button == "right") _sim.Mouse.RightButtonDown();
                                            else _sim.Mouse.LeftButtonDown();
                                            
                                            var waitRes5 = await WaitPreciseAsync((int)(action.Duration.Value * 1000), ct);
                                            if (!waitRes5.success) return (false, waitRes5.reason);
                                            
                                            if (action.Button == "right") _sim.Mouse.RightButtonUp();
                                            else _sim.Mouse.LeftButtonUp();
                                        }
                                        else
                                        {
                                            if (action.Button == "right") _sim.Mouse.RightButtonClick();
                                            else _sim.Mouse.LeftButtonClick();
                                        }
                                    }
                                }
                            }
                            break;

                        case "mouseup":
                            if (action.X.HasValue && action.Y.HasValue)
                            {
                                int clickX = (int)Math.Round(action.X.Value);
                                int clickY = (int)Math.Round(action.Y.Value);
                                
                                if (SmartPlayback && !string.IsNullOrEmpty(action.ClickThumbnail))
                                {
                                    var result = await SmartPlaybackFindTarget(action, clickX, clickY, ct);
                                    if (!result.success) return (false, result.message);
                                    if (result.jumped) continue;
                                    clickX = result.clickX;
                                    clickY = result.clickY;
                                }

                                if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.");

                                OnVirtualCursorMoved?.Invoke(clickX, clickY, "mouseup", i + 1);

                                if (IsBackgroundMode)
                                {
                                    if (AdbHelper.IsConnected)
                                    {
                                        await AdbTouchUp();
                                    }
                                    else
                                    {
                                        IntPtr target = BackgroundWindowHandle;
                                        SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONUP, action.Button);
                                    }
                                }
                                else
                                {
                                    double screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                                    double screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                                    double x = ((double)clickX / screenWidth) * 65535;
                                    double y = ((double)clickY / screenHeight) * 65535;
                                    _sim.Mouse.MoveMouseToPositionOnVirtualDesktop(x, y);
                                    
                                    if (action.Button == "right") _sim.Mouse.RightButtonUp();
                                    else _sim.Mouse.LeftButtonUp();
                                }
                            }
                            break;

                        case "double_click":
                            if (action.X.HasValue && action.Y.HasValue)
                            {
                                int clickX = (int)Math.Round(action.X.Value);
                                int clickY = (int)Math.Round(action.Y.Value);
                                bool usedSmartPlayback = false;
                                if (SmartPlayback && !string.IsNullOrEmpty(action.ClickThumbnail))
                                {
                                    var result = await SmartPlaybackFindTarget(action, clickX, clickY, ct);
                                    if (!result.success) return (false, result.message);
                                    if (result.jumped) continue;
                                    clickX = result.clickX;
                                    clickY = result.clickY;
                                    usedSmartPlayback = true;
                                }

                                if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.");

                                if (usedSmartPlayback)
                                {
                                    int waitMs = (int)((action.SmartWait ?? 2.0) * 1000);
                                    if (waitMs > 0)
                                    {
                                        OnLog?.Invoke($"[SMART] Memulai waktu tunggu sebelum klik ganda ({waitMs}ms)...");
                                        var waitRes6 = await WaitPreciseAsync(waitMs, ct, true);
                                        if (!waitRes6.success) return (false, waitRes6.reason);
                                    }
                                }

                                if (IsBackgroundMode)
                                {
                                    int holdMs = Math.Max(5, (int)(50 / SpeedMultiplier));
                                    if (AdbHelper.IsConnected)
                                    {
                                        await AdbTouchDown(clickX, clickY);
                                        await Task.Delay(holdMs);
                                        await AdbTouchUp();
                                        await Task.Delay(holdMs);
                                        await AdbTouchDown(clickX, clickY);
                                        await Task.Delay(holdMs);
                                        await AdbTouchUp();
                                    }
                                    else
                                    {
                                        IntPtr target = BackgroundWindowHandle;
                                        SendBackgroundMouse(target, clickX, clickY, WM_MOUSEMOVE);
                                        SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONDOWN, action.Button);
                                        await Task.Delay(holdMs);
                                        SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONUP, action.Button);
                                        await Task.Delay(holdMs);
                                        SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONDOWN, action.Button);
                                        await Task.Delay(holdMs);
                                        SendBackgroundMouse(target, clickX, clickY, WM_LBUTTONUP, action.Button);
                                    }
                                }
                                else
                                {
                                    double screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                                    double screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                                    double x = ((double)clickX / screenWidth) * 65535;
                                    double y = ((double)clickY / screenHeight) * 65535;
                                    _sim.Mouse.MoveMouseToPositionOnVirtualDesktop(x, y);

                                    if (action.Button == "right") _sim.Mouse.RightButtonDoubleClick();
                                    else _sim.Mouse.LeftButtonDoubleClick();
                                }
                            }
                            break;

                        case "move":
                            if (action.X.HasValue && action.Y.HasValue)
                            {
                                if (IsBackgroundMode)
                                {
                                    if (AdbHelper.IsConnected)
                                    {
                                        await AdbTouchMove((int)action.X.Value, (int)action.Y.Value);
                                    }
                                    else
                                    {
                                        IntPtr target = BackgroundWindowHandle;
                                        SendBackgroundMouse(target, (int)action.X.Value, (int)action.Y.Value, WM_MOUSEMOVE);
                                    }
                                }
                                else
                                {
                                    double screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                                    double screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                                    double x = ((action.X.Value) / screenWidth) * 65535;
                                    double y = ((action.Y.Value) / screenHeight) * 65535;
                                    _sim.Mouse.MoveMouseToPositionOnVirtualDesktop(x, y);
                                }
                            }
                            break;

                        case "scroll":
                            if (action.Amount.HasValue)
                            {
                                if (IsBackgroundMode)
                                {
                                    IntPtr target = BackgroundWindowHandle;
                                    if (target != IntPtr.Zero)
                                    {
                                        // WM_MOUSEWHEEL = 0x020A, wParam high word = wheel delta
                                        int delta = (action.Amount.Value > 0) ? 120 : -120;
                                        int wParam = (delta << 16);
                                        PostMessage(target, 0x020A, wParam, 0);
                                    }
                                }
                                else
                                {
                                    _sim.Mouse.VerticalScroll(action.Amount.Value / 120);
                                }
                            }
                            break;

                        case "hotkey":
                            if (action.Keys != null && action.Keys.Count > 0)
                            {
                                var modifiers = new List<VirtualKeyCode>();
                                VirtualKeyCode mainKey = VirtualKeyCode.RETURN;

                                foreach (var k in action.Keys)
                                {
                                    var resolved = ResolveKey(k);
                                    if (!resolved.HasValue) continue;
                                    
                                    var vkh = resolved.Value;
                                    // Deteksi modifier keys
                                    if (vkh == VirtualKeyCode.CONTROL || vkh == VirtualKeyCode.LCONTROL || vkh == VirtualKeyCode.RCONTROL)
                                        modifiers.Add(VirtualKeyCode.CONTROL);
                                    else if (vkh == VirtualKeyCode.MENU || vkh == VirtualKeyCode.LMENU || vkh == VirtualKeyCode.RMENU)
                                        modifiers.Add(VirtualKeyCode.MENU);
                                    else if (vkh == VirtualKeyCode.SHIFT || vkh == VirtualKeyCode.LSHIFT || vkh == VirtualKeyCode.RSHIFT)
                                        modifiers.Add(VirtualKeyCode.SHIFT);
                                    else
                                        mainKey = vkh;
                                }

                                if (IsBackgroundMode)
                                {
                                    IntPtr target = BackgroundWindowHandle;
                                    if (target != IntPtr.Zero)
                                    {
                                        foreach (var m in modifiers) SendBackgroundKey(target, m, true);
                                        SendBackgroundKey(target, mainKey, true);
                                        await Task.Delay(20);
                                        SendBackgroundKey(target, mainKey, false);
                                        foreach (var m in Enumerable.Reverse(modifiers)) SendBackgroundKey(target, m, false);
                                    }
                                }
                                else
                                {
                                    if (modifiers.Count > 0)
                                        _sim.Keyboard.ModifiedKeyStroke(modifiers, mainKey);
                                    else
                                        _sim.Keyboard.KeyPress(mainKey);
                                }
                            }
                            break;
                    }

                    // Jeda minimum setelah setiap aksi interaksi agar playback tidak terlalu cepat
                    string currentAct = action.Action?.ToLower();
                    if (currentAct != "wait" && currentAct != "move")
                    {
                        await HumanDelay(ct);
                    }
                }
                return (true, "Berhasil dieksekusi");
            }
            catch (Exception ex)
            {
                return (false, $"Error saat eksekusi: {ex.Message}");
            }
            finally
            {
                RestoreOffscreenWindow();
                ScreenCapture.IsOffscreenCapture = false;
                ScreenCapture.OffscreenLocked = false;
                ScreenCapture.UseAdbFrameCapture = false;
                ScreenCapture.ClearCache();
            }
        }

        // Aktifkan/update sumber frame capture di ScreenCapture saat background + ADB.
        // Basis RenderRect diambil dari GetRenderArea agar konsisten dengan basis klik ADB.
        private void UpdateBackgroundFrameCapture()
        {
            if (IsBackgroundMode && AdbHelper.IsConnected && BackgroundWindowHandle != IntPtr.Zero)
            {
                var (area, _) = GetRenderArea(BackgroundWindowHandle);
                if (area.Width > 0 && area.Height > 0)
                {
                    ScreenCapture.RenderRect = new ScreenCapture.RECT
                    {
                        Left = area.Left,
                        Top = area.Top,
                        Right = area.Right,
                        Bottom = area.Bottom
                    };
                    ScreenCapture.UseAdbFrameCapture = true;
                    return;
                }
            }

            ScreenCapture.UseAdbFrameCapture = false;
        }

        private async Task<(bool success, string message, bool jumped, int clickX, int clickY, bool hadToWait)> SmartPlaybackFindTarget(MacroAction action, int clickX, int clickY, CancellationToken ct)
        {
            // LANGKAH 1: Pastikan jendela yang benar ada di depan (skip saat background mode)
            if (!IsBackgroundMode && !string.IsNullOrEmpty(action.RecordedWindowTitle))
            {
                var targetProc = System.Diagnostics.Process.GetProcesses()
                    .FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle) &&
                        (p.MainWindowTitle.Contains(action.RecordedWindowTitle) || action.RecordedWindowTitle.Contains(p.MainWindowTitle)));

                if (targetProc != null && !targetProc.HasExited && targetProc.MainWindowHandle != IntPtr.Zero)
                {
                    // Cocokkan berdasarkan PID proses, bukan judul (judul game/emulator bisa berubah dinamis)
                    IntPtr currentFg = GetForegroundWindow();
                    GetWindowThreadProcessIdPid(currentFg, out uint fgPid);
                    GetWindowThreadProcessIdPid(targetProc.MainWindowHandle, out uint targetPid);

                    bool alreadyActive = currentFg == targetProc.MainWindowHandle || fgPid == targetPid;

                    if (!alreadyActive && (DateTime.UtcNow - _lastSmartSwitchTime).TotalMilliseconds >= 3000)
                    {
                        OnLog?.Invoke($"[SMART] Jendela aktif (PID {fgPid}) != target \"{action.RecordedWindowTitle}\" (PID {targetPid}). Switching...");
                        _lastSmartSwitchTime = DateTime.UtcNow;

                        if (IsIconic(targetProc.MainWindowHandle)) ShowWindow(targetProc.MainWindowHandle, 9);
                        else ShowWindow(targetProc.MainWindowHandle, 5);
                        uint foregroundThreadId = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
                        uint targetThreadId = GetWindowThreadProcessId(targetProc.MainWindowHandle, IntPtr.Zero);
                        if (foregroundThreadId != targetThreadId)
                        {
                            AttachThreadInput(foregroundThreadId, targetThreadId, true);
                            SetForegroundWindow(targetProc.MainWindowHandle);
                            AttachThreadInput(foregroundThreadId, targetThreadId, false);
                        }
                        else
                        {
                            SetForegroundWindow(targetProc.MainWindowHandle);
                        }
                        SwitchToThisWindow(targetProc.MainWindowHandle, true);
                        SetWindowPos(targetProc.MainWindowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        SetWindowPos(targetProc.MainWindowHandle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        await Task.Delay(800, ct);
                        OnLog?.Invoke($"[SMART] Berhasil switch ke \"{targetProc.MainWindowTitle}\".");
                    }
                }
                else if (targetProc == null)
                {
                    OnLog?.Invoke($"[SMART] Jendela \"{action.RecordedWindowTitle}\" tidak ditemukan di daftar proses.");
                }
            }

            // LANGKAH 2: Tunggu gambar target muncul di posisi asli (tanpa mengubah koordinat)
            bool finallyFound = false;
            bool hadToWait = false;
            int tryCount = 0;

            // Background mode: siapkan thumbnail versi PrintWindow (ClickThumbnailBg) sebagai
            // cadangan saat pencocokan CopyFromScreen gagal (misal target tertutup tab/window lain).
            // Rekaman lama tanpa ClickThumbnailBg di-generate otomatis sekali lalu di-cache per action.
            bool bgThumbReady = IsBackgroundMode && BackgroundWindowHandle != IntPtr.Zero;
            if (bgThumbReady) bgThumbReady = ResolveBackgroundThumbnail(action);

            while (!finallyFound)
            {
                if (ct.IsCancellationRequested) return (false, "Eksekusi dibatalkan oleh pengguna.", false, clickX, clickY, hadToWait);
                _pauseEvent.Wait(ct);
                ApplyOffscreenWindow(); // Jaga jendela target tetap di belakang saat SmartPlayback scanning

                if (JumpToIndex.HasValue) return (true, "Jumped", true, clickX, clickY, hadToWait);

                double threshold = action.MatchThreshold ?? (IsBackgroundMode ? 70.0 : 90.0);
                tryCount++;

                // Retry inisialisasi ADB bila tiba-tiba mati/hang di tengah playback (mis. emulator restart).
                // Tanpa retry, loop hanya menghasilkan frame null dan makro berkata "gagal temukan target"
                // sampai sesi dieksekusi ulang. Setelah >6.5 detik gagal, beri peringatan sekali.
                if (IsBackgroundMode && tryCount == 3 && !_adbRetryAttempted && !AdbHelper.IsConnected)
                {
                    _adbRetryAttempted = true;
                    OnLog?.Invoke("[BACKGROUND] ADB terputus saat playback. Mencoba inisialisasi ulang...");
                    AdbHelper.Initialize();
                    UpdateBackgroundFrameCapture();
                    ScreenCapture.InvalidateFrameCache();
                }
                if (IsBackgroundMode && tryCount > 30 && !_warnedBgCaptureUnavailable)
                {
                    _warnedBgCaptureUnavailable = true;
                    OnLog?.Invoke("[BACKGROUND] Capture background masih gagal setelah beberapa detik. Pastikan emulator/ADB aktif; makro tetap menunggu target.");
                }

                // 1. Cek target di posisi asli
                if (IsBackgroundMode && BackgroundWindowHandle != IntPtr.Zero)
                {
                    // Mode Background: Gunakan PrintWindow terhadap jendela target (menembus penutup browser/window lain)
                    if (bgThumbReady && ScreenCapture.CompareCropWithThumbnail(clickX, clickY, action.ClickThumbnailBg, threshold, true))
                    {
                        finallyFound = true;
                        break;
                    }
                    if (ScreenCapture.CompareCropWithThumbnail(clickX, clickY, action.ClickThumbnail, threshold, true))
                    {
                        finallyFound = true;
                        break;
                    }
                }
                else
                {
                    // Mode Normal: Gunakan CopyFromScreen terhadap piksel layar depan
                    if (ScreenCapture.CompareCropWithThumbnail(clickX, clickY, action.ClickThumbnail, threshold, false))
                    {
                        finallyFound = true;
                        break;
                    }
                }

                // 2. Cek fallbacks di posisi asli masing-masing (statis fast-path)
                bool isBg = IsBackgroundMode && BackgroundWindowHandle != IntPtr.Zero;
                if (action.Fallbacks != null && action.Fallbacks.Count > 0)
                {
                    lock (action)
                    {
                        for (int fi = 0; fi < action.Fallbacks.Count; fi++)
                        {
                            var fallback = action.Fallbacks[fi];
                            int fX = (int)Math.Round(fallback.X);
                            int fY = (int)Math.Round(fallback.Y);
                            
                            if (ScreenCapture.CompareCropWithThumbnail(fX, fY, fallback.Thumbnail, threshold, isBg))
                            {
                                clickX = fX;
                                clickY = fY;
                                finallyFound = true;
                                break;
                            }
                        }
                    }
                }

                if (finallyFound) break;

                // 3. Hanya jika pencocokan statis gagal > 2x, jalankan pencarian area (bisa toleransi geser kecil)
                if (tryCount >= 2)
                {
                    int searchX = (int)(action.X ?? -1);
                    int searchY = (int)(action.Y ?? -1);
                    
                    bool found = false;
                    int matchX = 0, matchY = 0;
                    double conf = 0;

                    if (isBg)
                    {
                        // Background: prioritaskan thumbnail Bg, fallback ke thumbnail utama (tetap pakai PrintWindow/ADB)
                        if (bgThumbReady)
                        {
                            (found, matchX, matchY, conf) = ScreenCapture.FindTemplateOnScreen(action.ClickThumbnailBg, searchX, searchY, 4, threshold, true, action.InnerRadius, action.SearchRadius);
                        }
                        if (!found)
                        {
                            (found, matchX, matchY, conf) = ScreenCapture.FindTemplateOnScreen(action.ClickThumbnail, searchX, searchY, 4, threshold, true, action.InnerRadius, action.SearchRadius);
                        }
                    }
                    else
                    {
                        // Normal: pakai CopyFromScreen
                        (found, matchX, matchY, conf) = ScreenCapture.FindTemplateOnScreen(action.ClickThumbnail, searchX, searchY, 4, threshold, false, action.InnerRadius, action.SearchRadius);
                    }

                    if (found)
                    {
                        double dist = Math.Sqrt(Math.Pow(matchX - clickX, 2) + Math.Pow(matchY - clickY, 2));
                        if (dist <= 20)
                        {
                            finallyFound = true;
                            break;
                        }
                    }
                }

                // Target belum ketemu, tunggu 150ms lalu scan ulang (CPU tetap 0% saat menunggu tombol muncul)
                hadToWait = true;
                try { await Task.Delay(150, ct); } catch (TaskCanceledException) { break; }
            }

            return (true, "Berhasil", false, clickX, clickY, hadToWait);
        }

        // Siapkan thumbnail versi PrintWindow untuk pencocokan cadangan background mode.
        // Rekaman baru memakai ClickThumbnailBg yang sudah terekam di file.
        // Rekaman lama (tanpa field ClickThumbnailBg) akan dipadankan langsung menggunakan ClickThumbnail (non-bg) terhadap frame background.
        private bool ResolveBackgroundThumbnail(MacroAction action)
        {
            return !string.IsNullOrEmpty(action.ClickThumbnailBg);
        }

        // Cache render child window agar tidak enumerate ulang setiap klik
        private static IntPtr _cachedRenderChild = IntPtr.Zero;
        private static IntPtr _cachedRenderParent = IntPtr.Zero;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        private void ApplyOffscreenWindow()
        {
            UpdateBackgroundFrameCapture(); // Sinkronkan basis frame ADB tiap iterasi loop (aksi/wait/path/smart scan)

            if (IsBackgroundMode && IsOffscreenMode && BackgroundWindowHandle != IntPtr.Zero)
            {
                // Hanya panggil SetWindowPos jika:
                // 1. Pertama kali di sesi ini (initial push), ATAU
                // 2. Jendela target benar-benar menjadi Foreground (user mengekliknya)
                // JANGAN panggil secara periodik karena DWM akan re-composite GPU surface
                // dan menyebabkan kedap-kedip saat game aktif di dalam emulator.
                if (!_hasLoggedOffscreen)
                {
                    _hasLoggedOffscreen = true;
                    SetWindowPos(BackgroundWindowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    OnLog?.Invoke("[BACKGROUND] Jendela target ditaruh di latar paling belakang (Behind Windows - Anti Freeze).");
                }
                else if (GetForegroundWindow() == BackgroundWindowHandle)
                {
                    // Jendela target aktif mencuri fokus, dorong kembali ke belakang
                    SetWindowPos(BackgroundWindowHandle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }


        public void RestoreOffscreenWindow()
        {
            // Jendela tetap di koordinat semula, tidak perlu dipindahkan
        }

        private static (RECT rect, IntPtr hwnd) GetRenderArea(IntPtr mainHwnd)
        {
            if (mainHwnd == IntPtr.Zero) return (default, IntPtr.Zero);

            // Gunakan cached child jika parent belum berubah
            if (_cachedRenderParent == mainHwnd && _cachedRenderChild != IntPtr.Zero)
            {
                if (GetWindowRect(_cachedRenderChild, out RECT cachedRect) && cachedRect.Width > 0 && cachedRect.Height > 0)
                {
                    return (cachedRect, _cachedRenderChild);
                }
                // Cache invalid, reset
                _cachedRenderChild = IntPtr.Zero;
            }

            // Rasio aspek parent sebagai preferensi: beberapa emulator punya banyak child (UI overlay,
            // toolbar, jendela log) dengan luas hampir sama; ingin pilih yang paling proporsional dengan
            // jendela utama agar RenderRect (basis koordinat ADB) tidak miring/memotong.
            double parentRatio = 0;
            if (GetClientRect(mainHwnd, out RECT parentClient))
            {
                parentRatio = parentClient.Height > 0 ? (double)parentClient.Width / parentClient.Height : 0;
            }

            IntPtr largestChild = IntPtr.Zero;
            int maxArea = 0;
            double maxRatioClose = double.MaxValue;
            
            IntPtr child = GetWindow(mainHwnd, GW_CHILD);
            while (child != IntPtr.Zero)
            {
                if (GetWindowRect(child, out RECT r))
                {
                    int area = r.Width * r.Height;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        largestChild = child;
                        maxRatioClose = parentRatio > 0 && r.Height > 0
                            ? Math.Abs((double)r.Width / r.Height - parentRatio)
                            : double.MaxValue;
                    }
                    else if (area > 10000 && maxArea > 10000 && parentRatio > 0 && r.Height > 0 &&
                             (double)area > (double)maxArea * 0.9)
                    {
                        // Luas hampir sama (>90% dari terbesar): pilih yang rasio aspeknya paling mirip parent
                        double ratioClose = Math.Abs((double)r.Width / r.Height - parentRatio);
                        if (ratioClose < maxRatioClose)
                        {
                            maxArea = area;
                            largestChild = child;
                            maxRatioClose = ratioClose;
                        }
                    }
                }
                child = GetWindow(child, GW_HWNDNEXT);
            }
            
            if (largestChild != IntPtr.Zero && maxArea > 10000)
            {
                _cachedRenderParent = mainHwnd;
                _cachedRenderChild = largestChild;
                if (GetWindowRect(largestChild, out RECT renderRect))
                {
                    return (renderRect, largestChild);
                }
            }
            
            if (GetClientRect(mainHwnd, out RECT clientRect))
            {
                POINT pt = new POINT { X = clientRect.Left, Y = clientRect.Top };
                ClientToScreen(mainHwnd, ref pt);
                RECT sRect = new RECT
                {
                    Left = pt.X,
                    Top = pt.Y,
                    Right = pt.X + clientRect.Width,
                    Bottom = pt.Y + clientRect.Height
                };
                return (sRect, mainHwnd);
            }
            
            GetWindowRect(mainHwnd, out RECT winRect);
            return (winRect, mainHwnd);
        }

        private void SendBackgroundMouse(IntPtr hWnd, int x, int y, uint msg, string button = "left")
        {
            IntPtr mainTarget = (hWnd != IntPtr.Zero) ? hWnd : BackgroundWindowHandle;
            if (mainTarget == IntPtr.Zero) return;
            
            var (renderRect, targetHwnd) = GetRenderArea(mainTarget);
            
            POINT pt = new POINT { X = x, Y = y };
            ScreenToClient(targetHwnd, ref pt);
            
            int lParam = (pt.Y << 16) | (pt.X & 0xFFFF);
            
            uint actualMsg = msg;
            int wParam = 0;
            
            if (button == "right")
            {
                if (msg == WM_LBUTTONDOWN) { actualMsg = 0x0204; _bgRightMouseDown = true; } // WM_RBUTTONDOWN
                else if (msg == WM_LBUTTONUP) { actualMsg = 0x0205; _bgRightMouseDown = false; } // WM_RBUTTONUP
                else if (msg == WM_MOUSEMOVE) { actualMsg = WM_MOUSEMOVE; }
            }
            else
            {
                if (msg == WM_LBUTTONDOWN) { actualMsg = WM_LBUTTONDOWN; _bgLeftMouseDown = true; }
                else if (msg == WM_LBUTTONUP) { actualMsg = WM_LBUTTONUP; _bgLeftMouseDown = false; }
                else if (msg == WM_MOUSEMOVE) { actualMsg = WM_MOUSEMOVE; }
            }
            
            // Sertakan bendera modifier penahan untuk simulasi Drag & Drop yang benar di background
            if (_bgLeftMouseDown) wParam |= 0x0001; // MK_LBUTTON
            if (_bgRightMouseDown) wParam |= 0x0002; // MK_RBUTTON

            PostMessage(targetHwnd, actualMsg, wParam, lParam);
        }

        private void SendBackgroundKey(IntPtr hWnd, VirtualKeyCode key, bool isDown)
        {
            IntPtr realTarget = (hWnd != IntPtr.Zero) ? hWnd : BackgroundWindowHandle;
            if (realTarget == IntPtr.Zero) return;
            
            uint msg = isDown ? WM_KEYDOWN : WM_KEYUP;
            PostMessage(realTarget, msg, (int)key, 0);
        }

        private async Task AdbTouchDown(int screenX, int screenY)
        {
            if (!AdbHelper.IsConnected) return;
            IntPtr target = BackgroundWindowHandle;
            if (target == IntPtr.Zero) return;
            
            var (renderRect, _) = GetRenderArea(target);
            int clientX = screenX - renderRect.Left;
            int clientY = screenY - renderRect.Top;
            int clientW = Math.Max(1, renderRect.Width);
            int clientH = Math.Max(1, renderRect.Height);
            
            await AdbHelper.TouchDownAsync(clientX, clientY, clientW, clientH);
        }

        private async Task AdbTouchMove(int screenX, int screenY)
        {
            if (!AdbHelper.IsConnected) return;
            IntPtr target = BackgroundWindowHandle;
            if (target == IntPtr.Zero) return;
            
            var (renderRect, _) = GetRenderArea(target);
            int clientX = screenX - renderRect.Left;
            int clientY = screenY - renderRect.Top;
            int clientW = Math.Max(1, renderRect.Width);
            int clientH = Math.Max(1, renderRect.Height);
            
            await AdbHelper.TouchMoveAsync(clientX, clientY, clientW, clientH);
        }

        private async Task AdbTouchUp()
        {
            if (!AdbHelper.IsConnected) return;
            await AdbHelper.TouchUpAsync();
        }

        private async Task AdbTouchHold(int screenX, int screenY, int durationMs)
        {
            if (!AdbHelper.IsConnected) return;
            IntPtr target = BackgroundWindowHandle;
            if (target == IntPtr.Zero) return;
            
            var (renderRect, _) = GetRenderArea(target);
            int clientX = screenX - renderRect.Left;
            int clientY = screenY - renderRect.Top;
            int clientW = Math.Max(1, renderRect.Width);
            int clientH = Math.Max(1, renderRect.Height);
            
            await AdbHelper.TouchHoldAsync(clientX, clientY, clientW, clientH, durationMs);
        }

        // Mapping nama key umum ke VirtualKeyCode
        private static VirtualKeyCode? ResolveKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return null;
            if (_keyMap.TryGetValue(keyName, out var mapped)) return mapped;
            if (Enum.TryParse<VirtualKeyCode>(keyName, true, out var parsed)) return parsed;
            // Huruf tunggal a-z
            if (keyName.Length == 1 && char.IsLetter(keyName[0]))
                return (VirtualKeyCode)char.ToUpper(keyName[0]);
            
            return null;
        }
    }
}
