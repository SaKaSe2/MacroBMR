using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using MacroBMR.Models;

namespace MacroBMR.Core
{
    public class MacroRecorder
    {
        private IKeyboardMouseEvents _globalHook;
        private DateTime _lastActionTime;
        private DateTime _lastMouseMoveTime;
        private int _lastMouseX;
        private int _lastMouseY;
        private bool _isRecordingMouse;

        private Dictionary<Keys, (MacroAction action, DateTime startTime)> _activeKeys = new Dictionary<Keys, (MacroAction, DateTime)>();

        private DateTime _lastMouseDownTime;
        private int _lastMouseDownX;
        private int _lastMouseDownY;
        private MacroAction _lastMouseDownAction;
        private DateTime _lastClickTime = DateTime.MinValue;
        private MacroAction _lastClickAction;

        private List<MousePoint> _mousePathBuffer = new List<MousePoint>();
        private double _accumulatedWaitSeconds = 0;
        private int _appendStartIndex = 0; // Batas bawah index saat mode append, supaya CleanUpUiClicks tidak menghapus aksi lama

        // Variabel untuk mengabaikan klik fisik setelah menekan F9
        private bool _ignoreNextClick = false;
        private DateTime _ignoreNextClickTime = DateTime.MinValue;

        public List<MacroAction> Actions { get; set; } = new List<MacroAction>();
        public bool IsRecording { get; private set; }
        public bool IsPaused { get; set; } = false;
        public bool UseDynamicThreshold { get; set; } = false;
        public IntPtr IgnoreWindowHwnd { get; set; } = IntPtr.Zero;

        // Mode Rekaman: True = Mode Background (Target Window), False = Mode Normal / Manual (Layar Penuh)
        public bool IsBackgroundRecording { get; set; } = false;
        public IntPtr TargetWindowHandle { get; set; } = IntPtr.Zero;
        public string TargetWindowTitle { get; set; } = null;

        public Action<MacroAction> OnRequestDynamicThreshold;
        public event Action OnStopRecording;
        public event Action<bool> OnPauseStateChanged;

        // Reset waktu referensi agar delay selama pause tidak terekam
        public void ResetLastActionTime()
        {
            _lastActionTime = DateTime.Now;
        }

        public void StartRecording(bool recordMouse = false, bool append = false)
        {
            if (IsRecording) return;

            if (!append || Actions == null)
            {
                Actions = new List<MacroAction>();
                _appendStartIndex = 0;
            }
            else
            {
                _appendStartIndex = Actions.Count; // Catat posisi awal aksi baru agar cleanup tidak menghapus aksi lama
            }
            
            _lastActionTime = DateTime.Now;
            _lastMouseMoveTime = DateTime.Now;
            _isRecordingMouse = recordMouse;
            _lastMouseX = -1;
            _lastMouseY = -1;
            _mousePathBuffer.Clear();
            _accumulatedWaitSeconds = 0;
            
            _globalHook = Hook.GlobalEvents();
            _globalHook.MouseDownExt += GlobalHook_MouseDownExt;
            _globalHook.MouseUpExt += GlobalHook_MouseUpExt;
            _globalHook.MouseWheelExt += GlobalHook_MouseWheelExt;
            _globalHook.KeyDown += GlobalHook_KeyDown;
            _globalHook.KeyUp += GlobalHook_KeyUp;
            
            if (_isRecordingMouse)
            {
                _globalHook.MouseMoveExt += GlobalHook_MouseMoveExt;
            }
            
            IsRecording = true;
            IsPaused = false;
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            
            _globalHook.MouseDownExt -= GlobalHook_MouseDownExt;
            _globalHook.MouseUpExt -= GlobalHook_MouseUpExt;
            _globalHook.MouseWheelExt -= GlobalHook_MouseWheelExt;
            _globalHook.KeyDown -= GlobalHook_KeyDown;
            _globalHook.KeyUp -= GlobalHook_KeyUp;
            
            if (_isRecordingMouse)
            {
                _globalHook.MouseMoveExt -= GlobalHook_MouseMoveExt;
            }
            
            _globalHook.Dispose();
            
            IsRecording = false;
        }

        private double FlushDelayAndPath(out List<MousePoint> path)
        {
            var now = DateTime.Now;
            var diff = (now - _lastActionTime).TotalSeconds;
            double totalDelay = _accumulatedWaitSeconds + diff;
            
            path = null;
            if (_mousePathBuffer.Count > 0)
            {
                path = new List<MousePoint>(_mousePathBuffer);
                _mousePathBuffer.Clear();
            }

            _accumulatedWaitSeconds = 0;
            _lastActionTime = now;
            return Math.Round(totalDelay, 3);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);


        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);

        [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        private const uint GA_ROOT = 2;

        // Ambil judul jendela yang sedang aktif saat ini
        private string GetActiveWindowTitle()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;

                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        // Deteksi jendela di bawah kursor lalu buat thumbnail versi PrintWindow (background).
        // File yang sudah direkam lama (tanpa field baru) tetap aman: nilai out default 0/null.
        public static long CaptureBackgroundThumbnail(int x, int y, out string thumbnailBg, IntPtr ignoreWindowHwnd = default, IntPtr targetHwndOverride = default)
        {
            thumbnailBg = null;
            try
            {
                IntPtr rootHwnd = targetHwndOverride;
                if (rootHwnd == IntPtr.Zero)
                {
                    IntPtr hwndAtCursor = WindowFromPoint(new POINT(x, y));
                    if (hwndAtCursor == IntPtr.Zero) return 0;

                    rootHwnd = GetAncestor(hwndAtCursor, GA_ROOT);
                    if (rootHwnd == IntPtr.Zero) rootHwnd = hwndAtCursor;

                    // Abaikan overlay recorder agar tidak ikut terekam
                    if (ignoreWindowHwnd != IntPtr.Zero && (rootHwnd == ignoreWindowHwnd || hwndAtCursor == ignoreWindowHwnd))
                        return 0;
                }

                thumbnailBg = ScreenCapture.CropWindowBg(rootHwnd.ToInt64(), x, y);
                return rootHwnd.ToInt64();
            }
            catch
            {
                thumbnailBg = null;
                return 0;
            }
        }

        private void GlobalHook_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            if (IsPaused) return;

            if (IgnoreWindowHwnd != IntPtr.Zero)
            {
                IntPtr hwndAtCursor = WindowFromPoint(new POINT(e.X, e.Y));
                if (hwndAtCursor != IntPtr.Zero)
                {
                    IntPtr rootHwnd = GetAncestor(hwndAtCursor, GA_ROOT);
                    if (rootHwnd == IgnoreWindowHwnd || hwndAtCursor == IgnoreWindowHwnd)
                    {
                        return; // Abaikan klik jika kursor berada di atas OverlayWindow
                    }
                }
            }

            // Jika user baru saja menekan F9 (rekam gambar bersih), abaikan klik fisik aslinya agar tidak dobel!
            if (_ignoreNextClick && (DateTime.Now - _ignoreNextClickTime).TotalSeconds < 2)
            {
                return;
            }
            _ignoreNextClick = false;

            double delay = FlushDelayAndPath(out var path);
            string button = e.Button == MouseButtons.Left ? "left" : (e.Button == MouseButtons.Right ? "right" : "middle");
            
            // Capture thumbnail di sekitar klik untuk Smart Playback
            string thumbnail = null;
            try { thumbnail = ScreenCapture.CropAroundPoint(e.X, e.Y); }
            catch { /* Jangan gagalkan recording jika crop error */ }

            string thumbnailBg = null;
            long recordedWindowId = 0;
            string windowTitle = null;

            if (IsBackgroundRecording)
            {
                // Mode Background: Ambil thumbnail background & simpan ID jendela target
                recordedWindowId = CaptureBackgroundThumbnail(e.X, e.Y, out thumbnailBg, IgnoreWindowHwnd, TargetWindowHandle);
                windowTitle = !string.IsNullOrEmpty(TargetWindowTitle) ? TargetWindowTitle : GetActiveWindowTitle();
            }
            else
            {
                // Mode Normal: Manual / Layar Penuh
                try { windowTitle = GetActiveWindowTitle(); } catch { }
            }

            var action = new MacroAction
            {
                Action = "mousedown",
                X = e.X,
                Y = e.Y,
                Button = button,
                Clicks = 1,
                ClickThumbnail = thumbnail,
                ClickThumbnailBg = thumbnailBg,
                RecordedWindowId = recordedWindowId,
                RecordedWindowTitle = windowTitle,
                Seconds = delay > 0 ? delay : (double?)null,
                Path = path
            };

            Actions.Add(action);
            _lastMouseDownAction = action;
            _lastMouseDownTime = DateTime.Now;
            _lastMouseDownX = e.X;
            _lastMouseDownY = e.Y;
        }

        private void GlobalHook_MouseUpExt(object sender, MouseEventExtArgs e)
        {
            if (IsPaused) return;

            if (IgnoreWindowHwnd != IntPtr.Zero)
            {
                IntPtr hwndAtCursor = WindowFromPoint(new POINT(e.X, e.Y));
                if (hwndAtCursor != IntPtr.Zero)
                {
                    IntPtr rootHwnd = GetAncestor(hwndAtCursor, GA_ROOT);
                    if (rootHwnd == IgnoreWindowHwnd || hwndAtCursor == IgnoreWindowHwnd)
                    {
                        return; // Abaikan klik jika kursor berada di atas OverlayWindow
                    }
                }
            }

            // Abaikan juga MouseUp jika ini adalah bagian dari klik yang diabaikan (karena F9)
            if (_ignoreNextClick && (DateTime.Now - _ignoreNextClickTime).TotalSeconds < 2)
            {
                _ignoreNextClick = false; // Reset setelah MouseUp
                return;
            }

            var now = DateTime.Now;
            double elapsedMs = (now - _lastMouseDownTime).TotalMilliseconds;
            int distX = Math.Abs(e.X - _lastMouseDownX);
            int distY = Math.Abs(e.Y - _lastMouseDownY);

            // Jika gerakan < 10px, anggap sebagai click/tap (berikan Duration jika ditekan lama)
            if (distX < 10 && distY < 10 && _lastMouseDownAction != null)
            {
                double timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
                if (timeSinceLastClick < 400 && _lastClickAction != null && 
                    Math.Abs(e.X - (_lastClickAction.X ?? 0)) < 10 && 
                    Math.Abs(e.Y - (_lastClickAction.Y ?? 0)) < 10)
                {
                    _lastClickAction.Action = "double_click";
                    Actions.Remove(_lastMouseDownAction);
                    
                    _lastClickAction = null; 
                }
                else
                {
                    _lastMouseDownAction.Action = "click";
                    if (elapsedMs > 100)
                    {
                        _lastMouseDownAction.Duration = Math.Round(elapsedMs / 1000.0, 3);
                    }
                    _lastClickAction = _lastMouseDownAction;
                    _lastClickTime = now;
                }
                
                if (UseDynamicThreshold && _lastClickAction != null)
                {
                    OnRequestDynamicThreshold?.Invoke(_lastClickAction);
                }
                
                _lastMouseDownAction = null;
            }
            else
            {
                // Jika digeser (jarak >= 10px), anggap sebagai drag (mousedown & mouseup terpisah)
                double delay = FlushDelayAndPath(out var path);
                string button = e.Button == MouseButtons.Left ? "left" : (e.Button == MouseButtons.Right ? "right" : "middle");
                Actions.Add(new MacroAction
                {
                    Action = "mouseup",
                    X = e.X,
                    Y = e.Y,
                    Button = button,
                    Seconds = delay > 0 ? delay : (double?)null,
                    Path = path
                });
                _lastMouseDownAction = null;
                _lastClickAction = null;
            }
        }

        private void GlobalHook_MouseWheelExt(object sender, MouseEventExtArgs e)
        {
            if (IsPaused) return;

            double delay = FlushDelayAndPath(out var path);
            Actions.Add(new MacroAction
            {
                Action = "scroll",
                Amount = e.Delta,
                Seconds = delay > 0 ? delay : (double?)null,
                Path = path
            });
        }

        private void GlobalHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                IsPaused = !IsPaused;
                OnPauseStateChanged?.Invoke(IsPaused);
                if (!IsPaused)
                {
                    ResetLastActionTime();
                }
                return;
            }

            if (e.KeyCode == Keys.F12)
            {
                StopRecording();
                OnStopRecording?.Invoke();
                return;
            }

            if (e.KeyCode == Keys.F9)
            {
                if (IsPaused) return;
                
                double f9Delay = FlushDelayAndPath(out var f9Path);
                
                // Ambil posisi mouse saat ini (tanpa mengeklik beneran)
                var mousePos = System.Windows.Forms.Cursor.Position;
                int mx = mousePos.X;
                int my = mousePos.Y;
                
                string thumbnail = null;
                try { thumbnail = ScreenCapture.CropAroundPoint(mx, my); }
                catch { }

                string thumbnailBg = null;
                long recordedWindowId = 0;
                string windowTitle = null;

                if (IsBackgroundRecording)
                {
                    recordedWindowId = CaptureBackgroundThumbnail(mx, my, out thumbnailBg, IgnoreWindowHwnd, TargetWindowHandle);
                    windowTitle = !string.IsNullOrEmpty(TargetWindowTitle) ? TargetWindowTitle : GetActiveWindowTitle();
                }
                else
                {
                    try { windowTitle = GetActiveWindowTitle(); } catch { }
                }

                var action = new MacroAction
                {
                    Action = "click",
                    X = mx,
                    Y = my,
                    Button = "left",
                    Clicks = 1,
                    ClickThumbnail = thumbnail,
                    ClickThumbnailBg = thumbnailBg,
                    RecordedWindowId = recordedWindowId,
                    RecordedWindowTitle = windowTitle,
                    Seconds = f9Delay > 0 ? f9Delay : (double?)null,
                    Path = f9Path
                };

                Actions.Add(action);
                
                // Set flag agar klik kiri fisik yang akan dilakukan user (untuk lanjut game) DIABAIKAN
                _ignoreNextClick = true;
                _ignoreNextClickTime = DateTime.Now;
                
                // Bunyikan beep agar user tahu F9 berhasil merekam
                System.Media.SystemSounds.Beep.Play();
                
                return;
            }

            if (IsPaused) return;

            // Abaikan auto-repeat: jika key masih aktif ditekan
            if (_activeKeys.ContainsKey(e.KeyCode))
            {
                return;
            }

            // Abaikan penekanan modifier key saja (akan direkam sebagai bagian dari hotkey)
            if (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey ||
                e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey ||
                e.KeyCode == Keys.Menu || e.KeyCode == Keys.LMenu || e.KeyCode == Keys.RMenu ||
                e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                return;
            }

            double delay = FlushDelayAndPath(out var path);

            // Cek apakah ada modifier yang sedang ditekan bersamaan
            bool hasShift = e.Shift;
            bool hasCtrl = e.Control;
            bool hasAlt = e.Alt;
            bool hasWin = GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0; // VK_LWIN / VK_RWIN

            if (hasShift || hasCtrl || hasAlt || hasWin)
            {
                // Rekam sebagai hotkey combination
                var keys = new List<string>();
                if (hasCtrl) keys.Add("ctrl");
                if (hasShift) keys.Add("shift");
                if (hasAlt) keys.Add("alt");
                if (hasWin) keys.Add("win");
                keys.Add(e.KeyCode.ToString().ToLower());

                Actions.Add(new MacroAction
                {
                    Action = "hotkey",
                    Keys = keys,
                    Seconds = delay > 0 ? delay : (double?)null,
                    Path = path
                });
            }
            else
            {
                // Tombol biasa tanpa modifier
                string keyString = e.KeyCode.ToString().ToLower();
                var action = new MacroAction
                {
                    Action = "press",
                    Key = keyString,
                    Seconds = delay > 0 ? delay : (double?)null,
                    Path = path
                };
                Actions.Add(action);
                _activeKeys[e.KeyCode] = (action, DateTime.Now);
            }
        }

        private void GlobalHook_KeyUp(object sender, KeyEventArgs e)
        {
            if (_activeKeys.TryGetValue(e.KeyCode, out var data))
            {
                double durationMs = (DateTime.Now - data.startTime).TotalMilliseconds;
                if (durationMs > 100) // Jika ditahan lebih dari 100ms, set durasi
                {
                    data.action.Duration = Math.Round(durationMs / 1000.0, 3);
                }
                _activeKeys.Remove(e.KeyCode);
            }
        }

        private void GlobalHook_MouseMoveExt(object sender, MouseEventExtArgs e)
        {
            if (IsPaused) return;

            var now = DateTime.Now;
            double timeDiff = (now - _lastMouseMoveTime).TotalMilliseconds;
            if (_lastMouseX == -1)
            {
                _lastMouseX = e.X;
                _lastMouseY = e.Y;
                _lastMouseMoveTime = now;
                return;
            }

            int distX = Math.Abs(e.X - _lastMouseX);
            int distY = Math.Abs(e.Y - _lastMouseY);

            // Rekam hanya jika: waktu >= 15ms DAN jarak >= 3 pixel (akurasi tinggi)
            if (timeDiff >= 15 && (distX >= 3 || distY >= 3))
            {
                var diff = (now - _lastActionTime).TotalSeconds;
                _accumulatedWaitSeconds += diff;
                
                _mousePathBuffer.Add(new MousePoint
                {
                    X = e.X,
                    Y = e.Y,
                    Delay = Math.Round(diff, 3)
                });
                
                _lastActionTime = now;
                _lastMouseMoveTime = now;
                _lastMouseX = e.X;
                _lastMouseY = e.Y;
            }
        }

        // Kompres gerakan mouse: hapus titik-titik di tengah garis lurus pada Path
        public void CompressMouseMoves()
        {
            if (Actions == null) return;

            foreach (var action in Actions)
            {
                if (action.Path == null || action.Path.Count < 3) continue;

                var compressed = new List<MousePoint>();
                compressed.Add(action.Path[0]);

                for (int i = 1; i < action.Path.Count - 1; i++)
                {
                    var prev = compressed[compressed.Count - 1];
                    var curr = action.Path[i];
                    var next = action.Path[i + 1];

                    // Cek apakah titik ini berada di garis lurus antara prev dan next
                    double dx1 = curr.X - prev.X;
                    double dy1 = curr.Y - prev.Y;
                    double dx2 = next.X - prev.X;
                    double dy2 = next.Y - prev.Y;
                    double cross = Math.Abs(dx1 * dy2 - dy1 * dx2);
                    double lineLen = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                    double dist = lineLen > 0 ? cross / lineLen : 0;

                    // Jika titik jaraknya < 3 pixel dari garis lurus, gabungkan delay-nya ke titik berikutnya (skip redundan)
                    if (dist < 3)
                    {
                        next.Delay += curr.Delay;
                        continue;
                    }

                    compressed.Add(curr);
                }

                compressed.Add(action.Path[action.Path.Count - 1]);
                action.Path = compressed;
            }
        }

        public void CleanUpUiClicks()
        {
            if (Actions == null || Actions.Count == 0) return;
            
            // Cari dari belakang ke depan, hapus aksi terakhir yang berhubungan dengan klik UI
            // Tapi JANGAN hapus melewati batas _appendStartIndex (aksi dari rekaman sebelumnya)
            bool foundStopAction = false;
            for (int i = Actions.Count - 1; i >= _appendStartIndex; i--)
            {
                var act = Actions[i].Action?.ToLower();
                
                // Jika kita menemukan click, mouseup, atau mousedown, ini adalah aksi klik tombol stop
                if (!foundStopAction && (act == "click" || act == "mouseup" || act == "mousedown" || act == "double_click"))
                {
                    foundStopAction = true;
                    Actions.RemoveAt(i);
                    
                    // Kalau ini mouseup, mousedown pasangannya (jika ada) harus ikut dihapus
                    // Lanjut ke iterasi berikutnya untuk mencari mousedown atau move.
                    continue;
                }
                
                if (foundStopAction)
                {
                    if (act == "mousedown" || act == "wait" || act == "move")
                    {
                        Actions.RemoveAt(i);
                        if (act == "mousedown")
                        {
                            // Mousedown adalah awal dari klik, kita bisa berhenti menghapus gerakan mouse di titik ini
                            // Tapi kita hapus satu 'wait' lagi sebelum mousedown agar tidak ada jeda aneh
                            if (i - 1 >= _appendStartIndex && Actions[i - 1].Action?.ToLower() == "wait")
                            {
                                Actions.RemoveAt(i - 1);
                            }
                            break;
                        }
                    }
                    else
                    {
                        // Jika ketemu aksi lain (keyboard, dll), berhenti menghapus
                        break;
                    }
                }
            }
        }
    }
}
