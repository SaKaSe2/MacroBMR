using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MacroBMR.Core
{
    public class AdbHelper
    {
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static string _adbPath = "adb";
        private static string _deviceSerial = null;
        private static bool _initialized = false;
        private static IntPtr _lastHwnd = IntPtr.Zero;
        
        public static Action<string> OnLog { get; set; }

        private static void AutoDiscoverAdb()
        {
            // PRIORITAS 1: Cek folder dari proses target window yang sedang dipilih (dinamis 100%)
            IntPtr targetHwnd = ScreenCapture.BackgroundWindowHandle;
            if (targetHwnd != IntPtr.Zero)
            {
                try
                {
                    GetWindowThreadProcessId(targetHwnd, out uint pid);
                    if (pid > 0)
                    {
                        var proc = Process.GetProcessById((int)pid);
                        string procPath = proc?.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(procPath))
                        {
                            string dir = Path.GetDirectoryName(procPath);
                            string[] candidates = new[]
                            {
                                Path.Combine(dir, "adb.exe"),
                                Path.Combine(dir, "HD-Adb.exe"),
                                Path.Combine(dir, "nox_adb.exe"),
                                Path.Combine(Path.GetDirectoryName(dir) ?? "", "adb.exe")
                            };
                            foreach (var cand in candidates)
                            {
                                if (File.Exists(cand))
                                {
                                    _adbPath = cand;
                                    OnLog?.Invoke($"[ADB] DynamicAutoDiscover: Menemukan ADB dari proses target ({proc.ProcessName}) di {_adbPath}");
                                    return;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // PRIORITAS 2: Cek folder dari semua proses emulator yang sedang berjalan di Windows
            var emuProcessNames = new[] { "dnplayer", "LdBoxHeadless", "LdVBoxHeadless", "HD-Player", "Nox", "NoxVMHandle", "MEmu", "MEmuHeadless", "Leidian", "ld" };
            foreach (var procName in emuProcessNames)
            {
                try
                {
                    var procs = Process.GetProcessesByName(procName);
                    foreach (var p in procs)
                    {
                        string procPath = p?.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(procPath))
                        {
                            string dir = Path.GetDirectoryName(procPath);
                            string[] candidates = new[]
                            {
                                Path.Combine(dir, "adb.exe"),
                                Path.Combine(dir, "HD-Adb.exe"),
                                Path.Combine(dir, "nox_adb.exe")
                            };
                            foreach (var cand in candidates)
                            {
                                if (File.Exists(cand))
                                {
                                    _adbPath = cand;
                                    OnLog?.Invoke($"[ADB] DynamicAutoDiscover: Menemukan ADB dari proses emulator aktif ({p.ProcessName}) di {_adbPath}");
                                    return;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // PRIORITAS 3: Pindai seluruh drive sistem untuk mencari instalasi LDPlayer/BlueStacks/Nox/MEmu secara dinamis
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    string driveRoot = drive.Name;
                    string[] searchRoots = new[]
                    {
                        Path.Combine(driveRoot, "LDPlayer"),
                        Path.Combine(driveRoot, "Program Files", "LDPlayer"),
                        Path.Combine(driveRoot, "Program Files (x86)", "LDPlayer"),
                        Path.Combine(driveRoot, "Program Files", "BlueStacks_nxt"),
                        Path.Combine(driveRoot, "Program Files", "Nox"),
                        Path.Combine(driveRoot, "Program Files", "Microvirt")
                    };

                    foreach (var baseDir in searchRoots)
                    {
                        if (Directory.Exists(baseDir))
                        {
                            string adbDirect = Path.Combine(baseDir, "adb.exe");
                            if (File.Exists(adbDirect))
                            {
                                _adbPath = adbDirect;
                                OnLog?.Invoke($"[ADB] DynamicAutoDiscover: Menemukan ADB di {adbDirect}");
                                return;
                            }

                            // Subfolder scan (misal: LDPlayer9, LDPlayer14, LDPlayer4.0, LDPlayer5)
                            foreach (var sub in Directory.GetDirectories(baseDir))
                            {
                                string adbSub = Path.Combine(sub, "adb.exe");
                                if (File.Exists(adbSub))
                                {
                                    _adbPath = adbSub;
                                    OnLog?.Invoke($"[ADB] DynamicAutoDiscover: Menemukan ADB di {adbSub}");
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // PRIORITAS 4: Path fallback umum
            var commonPaths = new[]
            {
                @"C:\LDPlayer\LDPlayer9\adb.exe",
                @"C:\LDPlayer\LDPlayer14\adb.exe",
                @"C:\LDPlayer\LDPlayer4.0\adb.exe",
                @"D:\LDPlayer\LDPlayer9\adb.exe",
                @"D:\LDPlayer\LDPlayer14\adb.exe",
                @"C:\Program Files\BlueStacks_nxt\HD-Adb.exe",
                @"C:\Program Files\BlueStacks\HD-Adb.exe",
                @"D:\Program Files\BlueStacks_nxt\HD-Adb.exe",
                @"C:\Program Files\Nox\bin\nox_adb.exe",
                @"C:\Program Files\Microvirt\MEmu\adb.exe",
                @"C:\Program Files (x86)\Minimal ADB and Fastboot\adb.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    _adbPath = path;
                    OnLog?.Invoke($"[ADB] DynamicAutoDiscover: Menemukan ADB di {_adbPath}");
                    return;
                }
            }

            OnLog?.Invoke("[ADB] DynamicAutoDiscover: Tidak menemukan adb.exe khusus. Menggunakan default 'adb'.");
        }

        private static string _touchDeviceNode = null;
        private static int _maxX = 0;
        private static int _maxY = 0;
        private static int _wmWidth = 0;
        private static int _wmHeight = 0;

        public static bool IsConnected => _initialized && _deviceSerial != null;

        // Cache frame layar perangkat (screencap) agar tidak spam adb exec-out tiap iterasi scan.
        // 300ms menyamakan TTL dengan PrintWindow (300ms) sehingga kedua sumber frame konsisten
        // saat SmartPlayback bergantian memakai ADB vs PrintWindow.
        private static readonly object _frameLock = new object();
        private static Bitmap _cachedFrame = null;
        private static long _cachedFrameTime = 0;
        private const long FRAME_CACHE_MS = 300;

        // Reset semua state ADB agar sesi berikutnya bisa Initialize() ulang dari awal
        public static void Cleanup()
        {
            ClosePersistentShell();
            lock (_frameLock)
            {
                _cachedFrame?.Dispose();
                _cachedFrame = null;
            }
            _initialized = false;
            _deviceSerial = null;
            _lastHwnd = IntPtr.Zero;
            _touchDeviceNode = null;
            _maxX = 0;
            _maxY = 0;
            _wmWidth = 0;
            _wmHeight = 0;
        }

        // Ambil frame layar perangkat (screencap), di-scale ke ukuran render area window.
        // Dipakai sbg sumber "mata" di background mode supaya pencocokan tetap jalan walau
        // jendela emulator tertutup penuh / minimize (tidak bergantung pada DWM).
        public static Bitmap GetScaledFrame(int targetW, int targetH)
        {
            if (!IsConnected || targetW <= 0 || targetH <= 0) return null;

            Bitmap source = null;
            lock (_frameLock)
            {
                long now = Environment.TickCount64;
                if (_cachedFrame != null && now - _cachedFrameTime < FRAME_CACHE_MS)
                {
                    source = _cachedFrame;
                }
            }

            if (source == null)
            {
                Bitmap fresh = CaptureFrameRaw();
                if (fresh == null)
                {
                    // Screencap gagal: gunakan frame lama jika masih segar, agar tidak pecah sesaat
                    lock (_frameLock) { source = _cachedFrame; }
                }
                else
                {
                    lock (_frameLock)
                    {
                        Bitmap old = _cachedFrame;
                        _cachedFrame = fresh;
                        _cachedFrameTime = Environment.TickCount64;
                        old?.Dispose();
                    }
                    source = fresh;
                }
            }

            if (source == null) return null;

            try
            {
                // Selalu kembalikan salinan baru agar pemanggil aman dispose tanpa merusak cache
                return new Bitmap(source, targetW, targetH);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap CaptureFrameRaw()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = $"-s {_deviceSerial} exec-out screencap -p",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                using var ms = new MemoryStream();
                proc.StandardOutput.BaseStream.CopyTo(ms);
                proc.WaitForExit(8000);
                if (ms.Length == 0) return null;
                ms.Position = 0;
                using var bmp = new Bitmap(ms);
                return new Bitmap(bmp);
            }
            catch
            {
                return null;
            }
        }

        public static void Initialize()
        {
            IntPtr currentHwnd = ScreenCapture.BackgroundWindowHandle;
            if (IsConnected && _lastHwnd == currentHwnd && currentHwnd != IntPtr.Zero) return;

            _lastHwnd = currentHwnd;
            Cleanup(); // Reset koneksi ADB lama jika target window berubah atau belum terhubung

            try
            {
                AutoDiscoverAdb();
                
                // Matikan daemon ADB lama yang berbeda versi agar tidak memblokir koneksi
                RunAdbSync("kill-server");

                var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                var portsToConnect = new List<int>();
                
                foreach (var listener in listeners)
                {
                    int port = listener.Port;
                    // Range port emulator Android umum (5554-5585, 62001-62025, 21503, 21513, 7555)
                    if ((port >= 5554 && port <= 5585) || 
                        (port >= 62001 && port <= 62025) || 
                        port == 21503 || 
                        port == 21513 ||
                        port == 7555)
                    {
                        if (!portsToConnect.Contains(port))
                        {
                            portsToConnect.Add(port);
                        }
                    }
                }

                if (portsToConnect.Count == 0)
                {
                    OnLog?.Invoke("[ADB] Tidak ada port emulator standar yang sedang LISTENING di PC. Mencoba port default 5555...");
                    portsToConnect.Add(5555);
                }
                else
                {
                    OnLog?.Invoke($"[ADB] Menemukan {portsToConnect.Count} port aktif: {string.Join(", ", portsToConnect)}");
                }

                foreach (int port in portsToConnect)
                {
                    string connRes = RunAdbSync($"connect 127.0.0.1:{port}");
                    OnLog?.Invoke($"[ADB] Connect 127.0.0.1:{port} -> {connRes.Trim()}");
                }

                string output = RunAdbSync("devices");
                OnLog?.Invoke($"[ADB] Perintah 'devices':\n{output.Trim()}");

                var lines = output.Split('\n')
                                  .Skip(1)
                                  .Select(l => l.Trim())
                                  .Where(l => l.Contains("device") && !l.Contains("offline") && !l.Contains("List of"))
                                  .ToList();

                if (lines.Count == 0)
                {
                    // Retry dengan kill-server jika belum terhubung
                    OnLog?.Invoke("[ADB] Belum ada device terhubung. Mereset ADB daemon...");
                    RunAdbSync("kill-server");
                    foreach (int port in portsToConnect)
                    {
                        RunAdbSync($"connect 127.0.0.1:{port}");
                    }
                    output = RunAdbSync("devices");
                    lines = output.Split('\n')
                                  .Skip(1)
                                  .Select(l => l.Trim())
                                  .Where(l => l.Contains("device") && !l.Contains("offline") && !l.Contains("List of"))
                                  .ToList();
                }

                OnLog?.Invoke($"[ADB] Ditemukan {lines.Count} perangkat aktif.");
                if (lines.Count > 0)
                {
                    _deviceSerial = lines[0].Split('\t')[0].Trim();
                    OnLog?.Invoke($"[ADB] Menggunakan perangkat serial: {_deviceSerial}");
                    
                    // Ambil resolusi layar Android (wm size)
                    string wmSize = RunAdbSync("shell wm size");
                    OnLog?.Invoke($"[ADB] Resolusi Android (wm size): {wmSize.Trim()}");
                    var matchWm = Regex.Match(wmSize, @"(\d+)x(\d+)");
                    if (matchWm.Success)
                    {
                        _wmWidth = int.Parse(matchWm.Groups[1].Value);
                        _wmHeight = int.Parse(matchWm.Groups[2].Value);
                    }

                    // Temukan device touchscreen fisik via getevent -il
                    string getEventOutput = RunAdbSync("shell getevent -il");
                    string currentDevice = null;
                    int curMaxX = 0, curMaxY = 0;

                    foreach (var line in getEventOutput.Split('\n'))
                    {
                        string tLine = line.Trim();
                        if (tLine.StartsWith("add device"))
                        {
                            var parts = tLine.Split(':');
                            if (parts.Length > 1) currentDevice = parts[1].Trim();
                        }
                        else if (currentDevice != null)
                        {
                            if (tLine.Contains("ABS_MT_POSITION_X") || tLine.Contains("ABS_X") || tLine.Contains("0035") || tLine.Contains("0000"))
                            {
                                var m = Regex.Match(tLine, @"max\s+(\d+)");
                                if (m.Success) curMaxX = int.Parse(m.Groups[1].Value);
                            }
                            if (tLine.Contains("ABS_MT_POSITION_Y") || tLine.Contains("ABS_Y") || tLine.Contains("0036") || tLine.Contains("0001"))
                            {
                                var m = Regex.Match(tLine, @"max\s+(\d+)");
                                if (m.Success) curMaxY = int.Parse(m.Groups[1].Value);
                            }
                        }

                        if (currentDevice != null && curMaxX > 0 && curMaxY > 0)
                        {
                            _touchDeviceNode = currentDevice;
                            _maxX = curMaxX;
                            _maxY = curMaxY;
                            OnLog?.Invoke($"[ADB] Touchscreen device ditemukan: {_touchDeviceNode} (MaxX: {_maxX}, MaxY: {_maxY})");
                            break;
                        }
                    }

                    if (_wmWidth > 0 && _wmHeight > 0)
                    {
                        _initialized = true;
                        OnLog?.Invoke($"[ADB] Inisialisasi ADB Touch Injector BERHASIL (Ukuran Layar: {_wmWidth}x{_wmHeight}).");
                    }
                    else
                    {
                        OnLog?.Invoke($"[ADB] Gagal inisialisasi: TouchDeviceNode={_touchDeviceNode}, WmWidth={_wmWidth}, WmHeight={_wmHeight}");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[ADB] Gagal inisialisasi akibat eksepsi: {ex.Message}");
            }
        }

        // Jalankan Touch Down / Tap
        public static Task TouchDownAsync(int clientX, int clientY, int clientWidth, int clientHeight)
        {
            if (!IsConnected) return Task.CompletedTask;

            int targetW = (_touchDeviceNode != null && _maxX > 0) ? _maxX : (_wmWidth > 0 ? _wmWidth : 1280);
            int targetH = (_touchDeviceNode != null && _maxY > 0) ? _maxY : (_wmHeight > 0 ? _wmHeight : 720);
            int scaledX = (int)((double)clientX / Math.Max(1, clientWidth) * targetW);
            int scaledY = (int)((double)clientY / Math.Max(1, clientHeight) * targetH);

            if (_touchDeviceNode != null)
            {
                string cmd = $"sendevent {_touchDeviceNode} 3 57 0;" +
                             $"sendevent {_touchDeviceNode} 3 53 {scaledX};" +
                             $"sendevent {_touchDeviceNode} 3 54 {scaledY};" +
                             $"sendevent {_touchDeviceNode} 1 330 1;" +
                             $"sendevent {_touchDeviceNode} 0 0 0";
                RunAdbShell(cmd);
            }
            else
            {
                string cmd = $"input tap {scaledX} {scaledY}";
                RunAdbShell(cmd);
            }
            return Task.CompletedTask;
        }

        // Jalankan Touch Move (untuk path/drag)
        public static Task TouchMoveAsync(int clientX, int clientY, int clientWidth, int clientHeight)
        {
            if (!IsConnected) return Task.CompletedTask;

            int targetW = (_touchDeviceNode != null && _maxX > 0) ? _maxX : (_wmWidth > 0 ? _wmWidth : 1280);
            int targetH = (_touchDeviceNode != null && _maxY > 0) ? _maxY : (_wmHeight > 0 ? _wmHeight : 720);
            int scaledX = (int)((double)clientX / Math.Max(1, clientWidth) * targetW);
            int scaledY = (int)((double)clientY / Math.Max(1, clientHeight) * targetH);

            if (_touchDeviceNode != null)
            {
                string cmd = $"sendevent {_touchDeviceNode} 3 53 {scaledX};" +
                             $"sendevent {_touchDeviceNode} 3 54 {scaledY};" +
                             $"sendevent {_touchDeviceNode} 0 0 0";
                RunAdbShell(cmd);
            }
            return Task.CompletedTask;
        }

        // Jalankan Touch Up
        public static Task TouchUpAsync()
        {
            if (!IsConnected) return Task.CompletedTask;

            if (_touchDeviceNode != null)
            {
                string cmd = $"sendevent {_touchDeviceNode} 3 57 -1;" +
                             $"sendevent {_touchDeviceNode} 1 330 0;" +
                             $"sendevent {_touchDeviceNode} 0 0 0";
                RunAdbShell(cmd);
            }
            return Task.CompletedTask;
        }

        private static Process _persistentShellProc = null;
        private static System.IO.StreamWriter _shellWriter = null;
        private static readonly object _shellLock = new object();

        private static void EnsurePersistentShell()
        {
            lock (_shellLock)
            {
                if (_persistentShellProc == null || _persistentShellProc.HasExited || _shellWriter == null)
                {
                    try
                    {
                        var psi = new ProcessStartInfo()
                        {
                            FileName = _adbPath,
                            Arguments = $"-s {_deviceSerial} shell",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false
                        };
                        _persistentShellProc = Process.Start(psi);
                        _shellWriter = _persistentShellProc.StandardInput;
                        _shellWriter.AutoFlush = true;
                    }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke($"[ADB] Persistent Shell Error: {ex.Message}");
                    }
                }
            }
        }

        public static void ClosePersistentShell()
        {
            lock (_shellLock)
            {
                try
                {
                    _shellWriter?.Close();
                    _shellWriter = null;
                    if (_persistentShellProc != null && !_persistentShellProc.HasExited)
                    {
                        _persistentShellProc.Kill();
                    }
                    _persistentShellProc = null;
                }
                catch { }
            }
        }

        private static void RunAdbShell(string shellCommand)
        {
            try
            {
                EnsurePersistentShell();
                lock (_shellLock)
                {
                    if (_shellWriter != null)
                    {
                        string formattedCmd = shellCommand.Replace(';', '\n');
                        _shellWriter.WriteLine(formattedCmd);
                    }
                }
            }
            catch
            {
                ClosePersistentShell();
            }
        }

        // Jalankan Touch Hold menggunakan perintah native Android 'input swipe X Y X Y durationMs'
        public static async Task TouchHoldAsync(int clientX, int clientY, int clientWidth, int clientHeight, int durationMs)
        {
            if (!IsConnected) return;

            int targetW = _wmWidth > 0 ? _wmWidth : 1280;
            int targetH = _wmHeight > 0 ? _wmHeight : 720;
            int scaledX = (int)((double)clientX / Math.Max(1, clientWidth) * targetW);
            int scaledY = (int)((double)clientY / Math.Max(1, clientHeight) * targetH);

            string cmd = $"input swipe {scaledX} {scaledY} {scaledX} {scaledY} {durationMs}";
            await Task.Run(() => RunAdbShell(cmd));
        }

        private static string RunAdbSync(string arguments)
        {
            try
            {
                string fullArgs = arguments;
                if (!string.IsNullOrEmpty(_deviceSerial) && !arguments.StartsWith("connect") && !arguments.StartsWith("devices") && !arguments.StartsWith("-s") && !arguments.StartsWith("kill-server"))
                {
                    fullArgs = $"-s {_deviceSerial} {arguments}";
                }

                var psi = new ProcessStartInfo()
                {
                    FileName = _adbPath,
                    Arguments = fullArgs,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                string result = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(5000);
                return result;
            }
            catch { return ""; }
        }
    }
}
