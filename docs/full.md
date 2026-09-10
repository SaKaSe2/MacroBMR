# Dokumentasi Lengkap — BMR Macro Studio (MacroBMR)

Dokumen ini merangkum seluruh isi proyek dari awal sampai akhir: arsitektur, model data, algoritma, dan **semua perhitungan** yang dipakai. Bahasa: Indonesia.

---

## 1. Ringkasan Proyek

| Item | Nilai |
|---|---|
| Nama aplikasi | BMR Macro Recorder & Studio (`MacroBMR`) |
| Target framework | `net6.0-windows` (WPF, WinExe) |
| Bahasa | C#, XAML |
| Asm | `MacroBMR.exe` — Version 1.0.0.0, Company "BMR Studio" |
| Manifest | `app.manifest` (permission UAC dll.) |
| Proyek kedua | `NexusClick` — auto-clicker mandiri, **di-exclude** dari build utama (`DefaultItemExcludes` di csproj) |

### Paket NuGet (MacroBMR.csproj)
| Paket | Versi | Fungsi |
|---|---|---|
| AdvancedSharpAdbClient | 3.6.16 | Komunikasi ADB Android (referensi saja; implementasi utama di kode sendiri via Process) |
| InputSimulatorPlus | 1.0.7 | Simulasi input keyboard/mouse (WindowsInput) |
| MouseKeyHook | 5.7.1 | Global hook mouse & keyboard (Gma.System.MouseKeyHook) |
| Newtonsoft.Json | 13.0.4 | Serialisasi file `.bmr` |
| System.Drawing.Common | 6.0.0 | Capture gambar, template matching |
| MaterialDesignThemes / Colors | 4.9.0 / 2.1.4 | Tema UI (BundledTheme Dark: Teal + LightGreen) |

### Peta Folder
```
MacroBMR.csproj      — proyek utama (net6.0-windows, UseWPF, AllowUnsafeBlocks)
App.xaml(.cs)              — resource/design tokens + mutex single-instance (Entry Point)
.gitignore                 — konfigurasi Git
Properties/
  app.manifest             — DPI awareness & hak akses OS
  AssemblyInfo.cs          — ThemeInfo WPF
Views/
  MainWindow.xaml(.cs)     — dashboard utama (record/play/settings)
  Overlays/
    OverlayWindow          — panel mengambang saat rekam/playback
    TargetPickerOverlay    — crosshair pemilih window target
    VirtualCursorOverlay   — visualisasi kursor background mode
Core/                      — mesin sistem (MacroRecorder, MacroExecutor, ScreenCapture, AdbHelper)
Models/                    — model data aksi & proyek (MacroAction, MacroProject)
docs/                      — dokumentasi teknis & panduan desain (full.md, desain.md)
Assets/                    — aset gambar & vektor logo (BMR.svg)
Recordings/                — folder penyimpan file rekaman makro (.bmr)
NexusClick/                — sub-proyek auto-clicker (terpisah)
.agents/skills/rules/*.md  — dokumen aturan maintenance
bin/publish/               — output build rilis final
```

---

## 2. Model Data & Skema File `.bmr`

### 2.1 `MacroAction` (Models/MacroAction.cs)

| Property | JSON key | Tipe | Fungsi |
|---|---|---|---|
| `Action` | `action` | string | Jenis aksi: `click`, `mousedown`, `mouseup`, `double_click`, `move`, `scroll`, `press`, `type`, `hotkey`, `wait`, `open_app`, `switch_window` |
| `X`, `Y` | `x`, `y` | double? | Koordinat layar absolut |
| `Button` | `button` | string | `left` / `right` / `middle` |
| `Clicks` | `clicks` | int? | Jumlah klik (selalu 1 di impl. saat ini) |
| `Duration` | `duration` | double? | Lama tahan (detik) untuk klik/press lama |
| `Text` | `text` | string | Teks untuk aksi `type` |
| `Interval` | `interval` | double? | Interval (belum aktif di executor) |
| `Key` | `key` | string | Tombol untuk `press` |
| `Keys` | `keys` | string[] | Kombinasi untuk `hotkey` (`ctrl`, `shift`, `alt`, `win`, dst.) |
| `Seconds` | `seconds` | double? | Delay sebelum aksi (detik) |
| `Amount` | `amount` | int? | Jumlah scroll (delta) |
| `AppName` | `app_name` | string | Nama aplikasi utk `open_app` / `switch_window` |
| `Filename` / `Filepath` | `filename` / `filepath` | string | Cadangan (belum aktif) |
| `ClickThumbnail` | `click_thumbnail` | string (base64 JPEG) | Thumbnail CopyFromScreen — untuk Smart Playback Normal Mode |
| `ClickThumbnailBg` | `click_thumbnail_bg` | string (base64 JPEG) | Thumbnail PrintWindow — untuk Background Mode |
| `RecordedWindowId` | `recorded_window_id` | long | HWND (root) jendela aktif saat rekam |
| `RecordedWindowTitle` | `recorded_window` | string | Judul jendela aktif saat rekam |
| `Fallbacks` | `fallbacks` | `FallbackTarget[]` | Target alternatif (posisi + thumbnail) |
| `MatchThreshold` | `match_threshold` | double? | Minimal % kecocokan (default `90.0`) |
| `SearchRadius` | `search_radius` | int? | Radius slip area search di `FindTemplateOnScreen` (default `20` px). Opsional, backward compatible |
| `InnerRadius` | `inner_radius` | int? | Radius zona pusat template saat pencocokan (default `30` px). Opsional, backward compatible |
| `SmartWait` | `smart_wait` | double? | Tunggu setelah target cocok, default `2.0` detik |
| `Path` | `path` | `MousePoint[]` | Trajektori mouse (smooth) |
| `Index` | — (JsonIgnore) | int | Nomor urut tampilan (dihitung ulang saat refresh) |

`DisplayDetails` (JsonIgnore) — ringkasan string utk DataGrid: `x=..., y=..., key=..., seconds=...` dst.

### 2.2 `FallbackTarget`
`thumbnail` (base64), `x`, `y` — alternatif tombol bila target utama tidak cocok.

### 2.3 `MousePoint`
`x`, `y` (int), `delay` (double, detik) — titik-titik jalur mouse; `delay` adalah durasi antar titik.

### 2.4 `MacroProject` (wadah export/import)
| Property | JSON key | Tipe |
|---|---|---|
| `LoopCount` | `loop_count` | string (teks dari TextBox; `"0"` = infinite) |
| `RecordMouse` | `record_mouse` | bool |
| `SmartPlayback` | `smart_playback` | bool |
| `DynamicThreshold` | `dynamic_threshold` | bool |
| `Actions` | `actions` | `MacroAction[]` |
| `SpeedMultiplier` | `speed_multiplier` | double (default 1.0) |
| `AutoExecute` | `auto_execute` | bool? — auto-run saat proyek dimuat (default `true`). Opsional, backward compatible |
| `VisionInterval` | `vision_interval` | int? — interval ulang perbaikan target otomatis (ms). Opsional, backward compatible |

> `AutoExecuteEnabled` (JsonIgnore) → `AutoExecute ?? true` untuk kode lama yang memakai bool non-nullable.
> Field `auto_execute` & `vision_interval` dari file `.bmr` lama (AutoReroll10-1, BaruAutoReroll, MyMacro) sekarang **tersimpan utuh** — data tidak lagi hilang saat re-export.

### 2.5 Contoh JSON asli (potongan MyMacro.bmr)
```json
{
  "loop_count": "0",
  "record_mouse": true,
  "smart_playback": true,
  "auto_execute": true,
  "vision_interval": "3",
  "actions": [
    { "action": "wait", "seconds": 0.424 },
    { "action": "move", "x": 556, "y": 203 },
    { "action": "click", "x": 650.0, "y": 639.0, "button": "left", "clicks": 1 }
  ]
}
```

---

## 3. Core/MacroRecorder.cs — Mesin Perekam

### 3.1 Keadaan (state)
- `Actions` — hasil rekaman; `IsRecording`, `IsPaused` (F11), `UseDynamicThreshold`.
- `IgnoreWindowHwnd` — HWND overlay; klik di atasnya TIDAK direkam.
- `_appendStartIndex` — batas bawah saat mode append; `CleanUpUiClicks` tidak boleh menghapus aksi lama di bawah indeks ini.
- `_ignoreNextClick` + `_ignoreNextClickTime` — abaikan klik fisik setelah F9 (jendela 2 detik).

### 3.2 Perhitungan Delay (FlushDelayAndPath)
```
diff        = now - _lastActionTime
totalDelay  = _accumulatedWaitSeconds + diff
path        = _mousePathBuffer (jika non-kosong)
round(totalDelay, 3)          → disimpan di action.Seconds
_accumulatedWaitSeconds = 0; _lastActionTime = now
```
- `_accumulatedWaitSeconds` diisi oleh **gerakan mouse**: setiap titik path terekam, selisih waktu sejak aksi terakhir di-**akumulasi** ke variabel ini (bukan langsung ke Seconds). Saat aksi nyata (klik/keyboard/wheel) terjadi, semuanya di-flush sekali → delay ternyata menggabungkan durasi gerakan + jeda diam dengan benar.

### 3.3 Throttle Perekaman Gerakan Mouse
Rekam titik hanya jika **keduanya** terpenuhi:
```
timeDiff >= 15 ms   DAN   (|ΔX| >= 3 px ATAU |ΔY| >= 3 px)
```
Jika terpenuhi: `_accumulatedWaitSeconds += diff`, titik ditambahkan ke buffer dengan `Delay = round(diff,3)`.

### 3.4 Konversi Klik → Aksi (MouseUp)
Pada `MouseUpExt`, dihitung:
```
elapsedMs = now - _lastMouseDownTime
distX = |e.X - _lastMouseDownX|;  distY = |e.Y - _lastMouseDownY|
```
- **Jika `distX < 10 && distY < 10` (gerakan < 10px)** → aksi dianggap klik:
  - **Double-click**: jika `timeSinceLastClick < 400 ms` DAN jarak < 10 px → action = `double_click`, dan `_lastMouseDownAction` (mousedown) **dihapus** dari list.
  - **Single click**: jika `elapsedMs > 100` → `Duration = round(elapsedMs/1000, 3)` (klik tahan lama).
  - Jika `UseDynamicThreshold` → `OnRequestDynamicThreshold` (pemanggilan dialog prompt).
- **Jika jarak >= 10px** → dianggap **drag**: tambahkan aksi `mouseup` terpisah (dengan path & delay tersendiri).

### 3.5 Perekaman Keyboard
- `F11` → toggle pause (reset `_lastActionTime` saat resume). `F12` → stop. `F9` → **rekam klik bersih tanpa klik fisik** (detail 3.7).
- Modifier saja (`Shift`, `Ctrl`, `Alt`, `Win`) → dilewati; akan terekam sebagai bagian kombinasi.
- Auto-repeat ditekan: jika key sudah ada di `_activeKeys` → dilewati.
- Dengan modifier aktif (deteksi `e.Shift/Control/Alt` + `GetAsyncKeyState(0x5B/0x5C)` utk Win): aksi `hotkey` dengan list key.
- Tanpa modifier: aksi `press` + `_activeKeys[key] = (action, startTime)`.
- **KeyUp**: jika `duration ms > 100` → `action.Duration = round(duration/1000, 3)` (tombol ditahan lama).

### 3.6 Kompresi Jalur Mouse (CompressMouseMoves)
Hapus titik tengah yang berada di **garis lurus** antar titik tetangga:
```
dx1 = curr.X - prev.X     dx2 = next.X - prev.X
dy1 = curr.Y - prev.Y     dy2 = next.Y - prev.Y
cross    = |dx1*dy2 - dy1*dx2|
lineLen  = sqrt(dx2² + dy2²)
distance = lineLen > 0 ? cross / lineLen : 0
```
Jika `distance < 3 px` → titik dianggap redundant; **delay-nya digabung ke titik berikutnya** (`next.Delay += curr.Delay`), titik dibuang. Berlaku hanya bila `Path.Count >= 3`.

### 3.7 F9 — Rekam Klik Bersih
1. `FlushDelayAndPath` → ambil delay+path.
2. Ambil posisi kursor saat ini (tanpa klik fisik), capture `ClickThumbnail` + `ClickThumbnailBg` + judul jendela.
3. Tambahkan aksi `click` (button left, clicks 1).
4. Set `_ignoreNextClick = true` (masa berlaku 2 detik) → **klik fisik user yang dilakukan untuk melanjutkan permainan DIABAIKAN** (tidak dobel).
5. `SystemSounds.Beep` sebagai konfirmasi.

### 3.8 CleanUpUiClicks
Scan **mundur** dari `Actions.Count - 1` sampai `_appendStartIndex`:
- Aksi klik pertama yang ditemukan (`click`/`mouseup`/`mousedown`/`double_click`) = klik tombol stop → dihapus.
- Lanjut: hapus `mousedown`, `wait`, `move` berikutnya; berhenti saat aksi lain (keyboard dll).
- Saat `mousedown` terhapus → hapus satu `wait` sebelumnya (jika ada) agar tidak ada jeda aneh.

### 3.9 Dual Thumbnail per Klik (`MacroRecorder.cs:184-207, 236-262`)
- `ClickThumbnail` → `ScreenCapture.CropAroundPoint(x,y)` (CopyFromScreen, radius 100).
- `ClickThumbnailBg` → `ScreenCapture.CropWindowBg(hwnd, x, y)` (PrintWindow ke root window di bawah kursor via `WindowFromPoint` + `GetAncestor(GA_ROOT)`).
- `windowTitle` → `GetWindowText(GetForegroundWindow())`.
- Semua dibungkus try/catch agar gagal capture tidak menghentikan rekaman.

---

## 4. Core/MacroExecutor.cs — Mesin Pemutar

### 4.1 Sinkronisasi Delay & Path (Perhitungan Kunci)
Setiap aksi dengan `Seconds` dijalankan dengan Stopwatch **bersamaan** dengan playback path:

```
adjustedSeconds = action.Seconds / SpeedMultiplier
totalMs         = (int)(adjustedSeconds * 1000)
```
**Path timestamps kumulatif:**
```
cumMs += point.Delay * 1000.0 / SpeedMultiplier   // utk setiap titik path
```
**Interpolasi posisi** saat `elapsed` ada di antara dua titik:
```
t      = (elapsed - prevTime) / (nextTime - prevTime)
interp = prevPt + (nextPt - prevPt) * t    (linear)
```
- Loop sementara `sw.ElapsedMilliseconds < totalMs`; rate limit: Background 15ms, Normal 2ms (Task.Delay).
- Countdown dikirim ke UI: `(totalMs - elapsed)/1000` saat `totalMs > 50`, update minimal tiap 100ms.
- Posisi akhir jika `elapsed >= pathTimestamps[last]` → titik terakhir render sekali saja.

### 4.2 Fictitious Path (Aksi Lama tanpa Path)
Jika `action.Path` kosong dan ada koordinat target (jarak > 2px):
```
numPoints      = max(5, min(25, totalMs / 25))
secPerPoint    = action.Seconds / numPoints
tSmooth        = t² * (3 - 2t)                     // smoothstep ease-in-out
px = round(curX + (targetX - curX) * tSmooth)
py = round(curY + (targetY - curY) * tSmooth)
```
Titik awal = posisi kursor saat ini (`Cursor.Position`), atau di Background Mode = koordinat aksi pertama. Mencegah kursor "teleport".

### 4.3 Koordinat Input
**Normal Mode** — InputSimulator memakai koordinat virtual desktop 16-bit:
```
vx = (x / screenWidth)  * 65535
vy = (y / screenHeight) * 65535
```
**Background Mode** — PostMessage ke window target dengan koordinat **client**:
```
ScreenToClient(hwnd, pt)        // dari layar abs ke local
lParam = (pt.Y << 16) | (pt.X & 0xFFFF)
wParam = 0 | MK_LBUTTON(0x0001) jika _bgLeftMouseDown | MK_RBUTTON(0x0002) jika _bgRightMouseDown
```
wParam dengan bendera penahan ini penting agar **Drag & Drop** bekerja di background.

**ADB Mode** (`AdbTouch*`):
```
clientX = screenX - renderRect.Left      (renderRect = area render child window)
clientY = screenY - renderRect.Top
clientW = renderRect.Width, clientH = renderRect.Height
```
scaling fisik device dilakukan di AdbHelper (lihat 6.4).

### 4.4 Tabel Aksi (switch di ExecuteAsync)

| Aksi | Normal | Background (tanpa ADB) | Background (dgn ADB) |
|---|---|---|---|
| `open_app` | `cmd /c start <app>` | sama | sama |
| `switch_window` | cari proses by judul → ShowWindow/SetForegroundWindow/AttachThreadInput + SwitchToThisWindow + SetWindowPos TOPMOST→NOTOPMOST, delay 500ms | **di-skip** (log `[BG] switch_window dilewati`) | di-skip |
| `wait` | delay di-handle blok umum | sama | sama |
| `type` | `TextEntry(c)` tiap char; space → `KeyPress(SPACE)`; delay 30ms/char | `WM_CHAR` (0x0102) via PostMessage; space → `SendBackgroundKey(SPACE)` | `WM_CHAR` |
| `press` | `KeyDown`/`KeyUp` + durasi | `PostMessage(WM_KEYDOWN/UP)` + durasi | sama |
| `mousedown` | `MoveMouseToPositionOnVirtualDesktop` + `Left/RightButtonDown` | `WM_MOUSEMOVE` + `WM_LBUTTONDOWN` | `AdbTouchDown` |
| `click` | move + `Left/RightButtonClick`; durasi → Down→Wait→Up | move + down + hold loop + up | `AdbTouchDown` → 50ms → `AdbTouchUp` (durasi → `AdbTouchHold`) |
| `mouseup` | move + `Left/RightButtonUp` | `WM_LBUTTONUP` | `AdbTouchUp` |
| `double_click` | move + `Left/RightButtonDoubleClick` | 2× (down→50ms→up) | 2× `AdbTouchDown/Up` 50ms |
| `move` | move mouse virtual | `WM_MOUSEMOVE` | `AdbTouchMove` |
| `scroll` | `VerticalScroll(amount/120)` | `PostMessage(WM_MOUSEWHEEL 0x020A)`; delta = ±120 → `wParam = delta << 16` | `WM_MOUSEWHEEL` |
| `hotkey` | `ModifiedKeyStroke(modifiers, mainKey)` | void `SendBackgroundKey` × mod+main turun/naik, delay 20ms | sama |

**Blockir cerdas**: `switch_window` membatalkan aksi bila nama jendela mengandung `bmr` atau `macro studio` (mencegah aplikasi membuka dashboard sendiri) — `MacroExecutor.cs:520-525`.

**Anti-Deteksi (HumanDelay, default OFF)**: `HumanizeInput` → jeda acak `rng.Next(15, 80)` ms antar aksi non-wait/move.

### 4.5 SmartPlaybackFindTarget (MacroExecutor.cs:1039-...)
Alur per aksi klik (`SmartPlayback = true` + ada thumbnail):

1. **Sinkronisasi jendela** (Non-BG): jika foreground (`currentFg != MainWindowHandle` **atau PID berbeda**) ≠ target → cari proses; restore/show + AttachThreadInput + SetWindowPos TOPMOST→NOTOPMOST + delay 800ms.
   - **Cocokkan berdasarkan PID** (`GetWindowThreadProcessId`) — judul game/emulator bisa berubah dinamis, PID tidak.
   - **Throttle 3 detik** (`_lastSmartSwitchTime`): `SetForegroundWindow` tidak diklaim berulang kali per aksi bila sudah pernah switch baru-baru ini.
2. **Retry ADB di tengah playback** (Background mode, `tryCount == 3`): bila `AdbHelper.IsConnected == false` → `AdbHelper.Initialize()` + `UpdateBackgroundFrameCapture()` + `ScreenCapture.InvalidateFrameCache()`, sekali per sesi (`_adbRetryAttempted`). Tanpa ini, emulator yang restart hanya menghasilkan frame null sampai makro dijalankan ulang. Setelah `tryCount > 30` → peringatan sekali `_warnedBgCaptureUnavailable`.
3. **Static match di posisi asli**:
   - BG: `CompareCropWithThumbnail(..., ClickThumbnailBg, threshold, usePrintWindow=true)`, lalu fallback ke `ClickThumbnail`.
   - Normal: `CompareCropWithThumbnail(..., ClickThumbnail, threshold, usePrintWindow=false)`.
4. **Fallbacks** (di bawah `lock (action)`): loop `action.Fallbacks`, cocokkan thumbnail masing-masing di `(fX,fY)`; jika cocok → koordinat klik diganti.
5. **Area search** (hanya jika `tryCount >= 2`): `FindTemplateOnScreen(..., searchRadius, innerRadius)` di area `±action.SearchRadius` (default 20px) dari posisi asli; jika ditemukan dgn `dist <= 20px` → ok. Area search BG pakai thumbnail Bg dulu, lalu utama.
6. Jika belum ketemu → `Task.Delay(150ms)` lalu ulangi scan (CPU minim).

Setelah target ditemukan (hanya untuk `click`/`double_click`): **SmartWait** → `waitMs = SmartWait * 1000`, `WaitPreciseAsync(waitMs, ct, updateCountdown=true)`.

> **Hold klik background diskala `SpeedMultiplier`**: klik biasa BG memegang tombol `50ms` (ADB) / `150ms` (SendMessage), dan double-click `50ms` antar-jentik — semuanya `max(5, (int)(base / SpeedMultiplier))` sehingga kecepatan makro konsisten antara mode Normal dan Background.

### 4.6 GetRenderArea (background coordinate basis)
- Memakai cache `_cachedRenderChild` bila parent sama.
- Enumerasi semua child window (`GW_CHILD` → `GW_HWNDNEXT`), pilih **child dengan area terbesar** (`maxArea > 10000 px²`) sebagai area render (khusus emulator: child = surface game).
- **Preferensi rasio aspek**: bila ada beberapa child dengan luas hampir sama (>90% dari terbesar), pilih yang rasio `width/height` **paling dekat dengan parent** (`GetClientRect`). Mencegah RenderRect memilih toolbar/overlay UI yang sebenarnya bukan surface game.
- Fallback: `GetClientRect` + `ClientToScreen`; fallback terakhir `GetWindowRect`.

### 4.7 ApplyOffscreenWindow (Hide Window / Offscreen)
- Memanggil `UpdateBackgroundFrameCapture()` tiap iterasi (sinkronisasi basis frame ADB).
- **Hanya jika** `IsBackgroundMode && IsOffscreenMode`:
  - Pertama kali sesi → `SetWindowPos(HWND_BOTTOM, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`.
  - Berikutnya → hanya jika window target menjadi FOREGROUND (user mengekliknya) → dorong balik ke BOTTOM.
- **Jangan** dipanggil periodik (DWM akan re-composite → kedap-kedip).
- Tanpa check `IsOffscreenMode`, Z-order TIDAK disentuh sama sekali.

### 4.8 WaitPreciseAsync — delay presisi & responsif
```
loop while sw.ElapsedMilliseconds < waitMs:
  cek ct.IsCancellationRequested → "Batal_CancelToken"
  cek JumpToIndex → "Batal_Jump"
  _pauseEvent.Wait(ct)
  ApplyOffscreenWindow()
  countdown: jika waitMs > 50 dan selisih >= 100ms → OnWaitCountdown(remaining)
  sleep: rem > 15 ? Task.Delay(15) : Task.Delay(1)
```
Model **ManualResetEventSlim**: `_pauseEvent.Set()` = jalan, `Reset()` = pause; `IsPaused => !_pauseEvent.IsSet`. JumpToIndex dibaca di 3 tempat (blok delay, loop utama) agar responsif.

---

## 5. Core/ScreenCapture.cs — Image Matching

### 5.1 Sumber Frame (CaptureScreenArea)
Prioritas:
1. **ADB screencap** — hanya jika `usePrintWindow == true && UseAdbFrameCapture && AdbHelper.IsConnected` dan `RenderRect` valid → `AdbHelper.GetScaledFrame(RenderRect.W, RenderRect.H)`, potong per region.
2. **PrintWindow** — jika `usePrintWindow && BackgroundWindowHandle != 0`: bitmap penuh window di-cache **300ms** (`_cachedFullWin`), panggil `PrintWindow(hwnd, hdc, 2)` (PW_RENDERFULLCONTENT), gagal → fallback flag `0`. Crop dari area cache.
   - **Validasi blank**: hasil PrintWindow diuji `IsBlankBitmap` (lihat 5.3). Bila blank → cache dibuang (`_cachedFullWinTime = 0`) dan fungsi mengembalikan **null** — frame kosong dianggap "capture tidak tersedia", bukan hasil valid (mencegah false-positive match terhadap layar hitam).
   - **Gate Offscreen**: `usePrintWindow == true && OffscreenLocked == true` → bila ADB & PrintWindow sama-sama gagal, fungsi mengembalikan **null dan TIDAK jatuh ke CopyFromScreen** — piksel monitor bukan konten window yang disembunyikan.
3. **CopyFromScreen** — fallback default (piksel monitor fisik), hanya jika Offscreen TIDAK terkunci.

> **Aturan scope**: ADB frame HANYA aktif di Background Mode (`usePrintWindow=true`). Di Normal Mode wajib CopyFromScreen walau ADB terhubung — menghindari mismatch koordinat (raw pixel Android vs pixel layar monitor).
> **OffscreenLocked** di-set/ reset oleh `MacroExecutor.ExecuteAsync` (`IsBackgroundMode && IsOffscreenMode && !AdbHelper.IsConnected` di awal; `false` di `finally`).

### 5.2 Konversi & kualitas
- JPEG quality: crop thumbnail = **95**; screenshot penuh = **75**.
- Template di-decode sekali lalu di-`cache` di `ConcurrentDictionary<string,TemplateData>` (max 100 entri; di atas itu cleared). Simpan Pixels raw (24bppRGB) + stride utk iterasi cepat dengan pointer unsafe.

### 5.3 Deteksi Gambar Kosong (`IsBlankBitmap` + helper `IsBlankPixels`)
- Samplikan piksel tiap **8px**; pixel "terang" jika `R + G + B > 40`.
- Jika `brightPixels * 100 / total < 5` → dianggap kosong/hitam (tanda PrintWindow/screencap gagal).
- Dipakai oleh: `CaptureScreenAsBase64` (thumbnail rekam — hasil kosong dibuang, lihat MacroRecorder), `CaptureScreenArea` (validasi frame PrintWindow penuh), dan path lama `IsBlankThumbnail` yang kini di-delegate ke helper yang sama.

### 5.4 CompareCropWithThumbnail — pencocokan statis 1 posisi
Signature: `(int x, int y, string recordedBase64, double matchThreshold = 90.0, bool usePrintWindow = false, int? innerRadiusOverride = null)` — parameter radius (default `100`) & step (`4`) tetap; `innerRadiusOverride` (default `30`) menggantikan konstanta lama.

```
diff        = |cB-rB| + |cG-rG| + |cR-rR|   (per-pixel, sampling tiap 4px)
diffLimit   = 15  jika matchThreshold >= 98.0   (exact match)
               50  jika usePrintWindow=true      (BG: toleransi GDI/ADB)
               35  jika usePrintWindow=false     (Normal: presisi tinggi)
isMatch     = diff < diffLimit
```

**Zona ganda** (pusat lebih penting):
```
inner radius innerRadiusOverride(30) px → innerMatch/innerTotal   (harus >= matchThreshold %)
outer                                    → outerMatch/outerTotal   (harus >= matchThreshold %)
```

**Filter warna dominan** (anti false-positive saat loading screen gelap):
```
recPctG = recordedTotalG / totalPx * 100     (hitung di kedua bitmap, hanya warna dominan ≥ 2%)
jika recPctG >= 2.0 dan curPctG < recPctG * 0.3 → REJECT (warna hijau hilang ≥70%)
(sama untuk R dan B)
```

**Struktur tepi `HasGoodStructure`** (anti false-positive untuk konten teks/hitam yang nyaris kosong):
```
const EDGE_LUM_DIFF = 40   (pixel dianggap "tepi" bila |lum_screen - lum_thumb| > 40)
const MAX_MISMATCH_RATIO = 0.55  (terima bila mismatch tepi < 55% dari total tepi)
const INNER = 30           (zona ±30px dari pusat dikecualikan: warning icon/animation)
```
- Hanya membandingkan pixel `step` (sampling); `scanBounds` dipangkas tepi template ±4px (border).
- `currentBmp == null` → langsung `return false` (bukan pengecualian).

### 5.5 FindTemplateOnScreen — pencarian area
Signature: `(string recordedBase64, int originX = -1, int originY = -1, int step = 4, double matchThreshold = 90.0, bool usePrintWindow = false, int? innerRadiusOverride = null, int? searchRadiusOverride = null)`
- `sampleCount = (thumbH/step) * (thumbW/step)`.
- Sliding window di layar (atau area `origin ± searchRadiusOverride` — default 20 px — bila origin diberikan), step 4px.
- `score = matchCount / sampleCount * 100`; `innerScore` sama utk zona ±`innerRadiusOverride` (default 30).
- Valid jika **keduanya** `>= matchThreshold` **dan** `HasGoodStructure` lolos (tepinya tidak bolak-balik kontras > 40 di > 55% sampel).
- Pick best: **global scan penuh tanpa early-exit** — skor tertinggi; tie-break jarak ke origin. (Early-exit lama `score >= 98.0 → goto MatchFoundExit` dihapus: area ±20px memotong target geser → false-negative.)
- Hasil: `(found, x, y, confidence)` — koordinat = pusat window yang cocok (`captureX + sx + thumbW/2`).

### 5.6 Pembersihan Cache
- `InvalidateFrameCache()`: dispose `_cachedFullWin` + reset `_cachedFullWinTime` saja (dipakai saat sumber frame berubah, mis. retry ADB).
- `ClearCache()`: `InvalidateFrameCache()` + clear template cache + `GC.Collect(0, Optimized)`.
- Executor memanggil `ClearCache()` tiap **30 aksi** (`i % 30 == 0`) utk mencegah lag/crash saat makro panjang.

---

## 6. Core/AdbHelper.cs — Injeksi Sentuhan Android

### 6.1 Auto-Discover `adb.exe` (4 prioritas)
1. **Proses target window** (dari `ScreenCapture.BackgroundWindowHandle` → PID → MainModule.FileName): cari `adb.exe`, `HD-Adb.exe`, `nox_adb.exe` di folder proses & parent.
2. **Proses emulator aktif**: `dnplayer`, `LdBoxHeadless`, `LdVBoxHeadless`, `HD-Player`, `Nox`, `NoxVMHandle`, `MEmu`, `MEmuHeadless`, `Leidian`, `ld`.
3. **Scan seluruh drive**: `LDPlayer`, `Program Files([x86])/LDPlayer`, `BlueStacks_nxt`, `Nox`, `Microvirt` + subfolder (LDPlayer9/14/4.0/5 dst).
4. **Path umum**: `C:\LDPlayer\LDPlayer9\adb.exe`, `C:\LDPlayer\LDPlayer14\`, `BlueStacks_nxt\HD-Adb.exe`, `Nox\bin\nox_adb.exe`, Microvirt MEmu, Minimal ADB, Android SDK platform-tools.

### 6.2 Koneksi Device
- `adb kill-server` dulu (matikan daemon versi beda).
- **Port emulator yang dipindai** dari TCP listeners aktif:
  - 5554–5585, 62001–62025, 21503, 21513, 7555.
  - Tidak ada → coba 5555.
- Ambil `wm size` → `_wmWidth/_wmHeight` (resolusi eksternal).
- **getevent -il** → cari device touchscreen pertama yang punya `ABS_MT_POSITION_X` (nik `0035`/`0000`) dan `ABS_MT_POSITION_Y` (`0036`/`0001`), baca `max X/Y`.

### 6.3 Sendevent — Touch Level Kernel
`targetW = _maxX (touch device) | _wmWidth | 1280` dan `targetH` analog.

**Touch Down / Tap:**
```
sendevent <dev> 3 57 0      (slot)
sendevent <dev> 3 53 X      (ABS_MT_POSITION_X)
sendevent <dev> 3 54 Y      (ABS_MT_POSITION_Y)
sendevent <dev> 1 330 1     (BTN_TOUCH)
sendevent <dev> 0 0 0       (SYN_REPORT)
```
**Touch Move:** `3 53 X; 3 54 Y; 0 0 0`
**Touch Up:** `3 57 -1; 1 330 0; 0 0 0`
(Perintah dipisah `;` → dikonversi jadi baris baru di persistent shell.)
- Tanpa touch device node → fallback `input tap X Y` / `input swipe X Y X Y dur`.

### 6.4 Scaling Koordinat Client → Device
```
scaledX = (int)(clientX / max(1, clientW) * targetW)
scaledY = (int)(clientY / max(1, clientH) * targetH)
```
Sama utk `TouchHoldAsync` (pakai `_wmWidth/_wmHeight`).

### 6.5 Persistent Shell & Cache Frame
- **Persistent shell**: satu proses `adb -s <serial> shell` dengan `StandardInput` dijaga; semua sendevent ditulis sebagai baris → hemat startup process overhead.
- **Frame cache screencap**: `CaptureFrameRaw()` = `adb -s <serial> exec-out screencap -p`, di-cache **300 ms** (sama dengan TTL PrintWindow `_cachedFullWin` agar kedua sumber frame konsisten saat SmartPlayback bergantian memakai ADB vs PrintWindow); `GetScaledFrame(w,h)` mengembalikan **salinan baru** (pemanggil aman dispose). Screencap gagal → pakai frame lama bila masih segar.
- `Cleanup()`: tutup shell, dispose frame, reset state; dipanggil di `AdbHelper.Initialize()` (jika target berubah) dan setelah playback selesai (`MainWindow.xaml.cs:692`).

---

## 7. UI — Design System (App.xaml + desain.md)

### 7.1 Design Tokens (App.xaml)
| Token | Nilai | Catatan |
|---|---|---|
| Base900 | `#021C1E` | Background window (blue black) |
| Base700 | `#004445` | Panel/sidebar/card (cadet blue) |
| Accent500 | `#2C7873` | Primary kontrol (rain) |
| Highlight300 | `#6FB98F` | Tombol action (greenery) |
| BgHover | `#332C7873` | Accent **20% opacity** → alpha 0x33 |
| BorderSubtle | `#802C7873` | Accent **50% opacity** → alpha 0x80 |
| TextPrimary | `#FFFFFF` | teks utama |
| TextSecondary | `#B3B9B9` | label/placeholder |
| StatusDelete | `#FF6B6B` | merah kritis |

- **Kalkulasi alpha 8-digit ARGB**: 20% dari 255 = 51 = `0x33`; 50% = 128 = `0x80`. Format `#AARRGGBB`.
- Override MaterialDesign agar ikut gelap: `MaterialDesignPaper/Background/Card/Body/BodyLight/TextBoxBorder/CheckBoxOff/SeparatorBackground/TableAlternatingRow/GridBackground`.
- Style global: `PremiumCard`, `DashboardCard`, `PrimaryButtonStyle` (36px, hover→Highlight300), `ActionButtonStyle` (48px, utk Record), `OutlinedButtonStyle`, `IconButtonStyle` (40px), `TitleBarButtonStyle` (44×32), `InputStyle` (36px), `ToggleSwitchStyle` (track 40×20, thumb 16×16), `BmrDataGridHeader`.

### 7.2 MainWindow.xaml — Layout
```
Window: 1100×750, min 800×600, chromeless (WindowChrome CaptionHeight=32, CornerRadius=8)
Grid row 0 (32px): title bar kustom — branding kiri, minimize/maximize/close kanan
Grid row 1 (*):
   col 0 (240px): sidebar (MENU: Manual Record, Pengaturan) — ListBoxItem 40px, radius 6
   col 1 (*): TabControl (2 tab tersembunyi via ItemContainerStyle)
```
Tab 1 — Manual Record: header, 4 dashboard cards, toolbar (Record/Play/Clear + Export/Import + toggle: Rekam Gerakan Mouse, Smart Playback, Run in Background→panel ComboBox jendela + Refresh + Offscreen, Dynamic Threshold), DataGrid (40px header, 48px row, zebra `#021C1E`/`#052325`), log box (Consolas, max 20k chars).
Tab 2 — Pengaturan: Loop count input.

### 7.3 OverlayWindow — Panel Mengambang
- **Collapsed**: lingkaran merah 36px berlabel "M" di pojok kanan atas.
- **Expanded** (lebar ~415px, pill radius 30, border Highlight300 2px): countdown, ComboBox iterasi (lompat aksi), tombol Stop (merah), Pause (oranye↔hijau saat resume), Fix (hijau, context menu: rekam ulang target saat ini/sebelumnya, fallback, edit threshold, edit smart-wait, sisipkan sebelum/sesudah/nomor), Speed (menu 0.25x–5x + edit waktu tunggal), X collapse.
- **Posisi**: collapsed → `Left = screenWidth - 40, Top = 5`; expanded → `Left = screenWidth - 415, Top = 5`.
- `WS_EX_TOOLWINDOW` → tidak muncul di Alt+Tab.

---

## 8. MainWindow.xaml.cs — Alur Orkestrasi

### 8.1 Mode Record
1. Jika sudah ada aksi → dialog Yes/No/Cancel (Yes = append ke `_appendStartIndex`, No = mulai baru).
2. `_recorder.StartRecording(recordMouse, append)`; `IgnoreWindowHwnd = overlay.Handle`.
3. Main window disembunyikan penuh (`ShowInTaskbar=false; Hide()`), overlay ditampilkan.
4. `_currentStopAction` / `_currentPauseAction` di-set utk tombol overlay.
5. Stop (F12 / tombol) → `ExecuteStopRecording()` → `CompressMouseMoves()` → (jika insert: merge, lihat 8.3) → `RefreshTable()` → `RestoreFromOverlay()`.

### 8.2 Mode Playback
1. Validasi loop (`TxtLoopCount`, `0` = infinite).
2. Set `SmartPlayback`, `IsBackgroundMode`, `IsOffscreenMode`, `BackgroundWindowHandle` (dari ComboBox).
3. Sembunyikan window, tampilkan overlay, build `_interactionIndices` (indeks aksi selain `move`/`wait`) → ComboBox iterasi.
4. `_executor.OnActionIndexChanged` → pilih combo index terakhir yang `interactionIndices[k] <= currentActionIndex - 1`.
5. Loop outer: `while !cancelled && (loopCount==0 || loop < loopCount)` → `ExecuteAsync(Actions, token)` via `Task.Run`; jeda 1 detik antar loop.
6. Selesai → `RestoreFromOverlay()`, `AdbHelper.Cleanup()`, dispose `_playbackHook`, reset tombol.

### 8.3 Insert Recording (After / Before / At)
1. Pause executor (tanpa membatalkan), simpan `_currentStopAction/_currentPauseAction` lama.
2. `_isTransitioningToInsert = true`; `_originalListBeforeInsert = _recorder.Actions`; `_insertIndex` = posisi.
3. `StartInsertRecording(...)` → dispose `_playbackHook` (hindari bentrok 2 hook), `StartRecording(recordMouse, append:false)` — aksi baru terpisah.
4. **Merge saat stop** (ExecuteStopRecording):
   ```
   newItems = _recorder.Actions
   _recorder.Actions = _originalListBeforeInsert
   _recorder.Actions.InsertRange(_insertIndex, newItems)
   ```
5. Resume otomatis: rebuild `_interactionIndices`, `JumpToIndex = insertedAt` (kecuali InsertAfter), toggle pause, re-create `_playbackHook`.

### 8.4 Fix Target & Fallback
- `FixTargetHook_MouseDownExt` (hook global sementara): klik kiri/kanan → `CropAroundPoint` → berdasarkan mode:
  - `Current`/`Previous` → di dalam `lock (targetAction)`: ganti `ClickThumbnail`, `X`, `Y` (lock sama seperti mode Fallback — executor membaca field ini selama SmartPlayback).
  - `Fallback` → `lock (targetAction)` lalu tambah `FallbackTarget {Thumbnail, X, Y}`.
- Validasi: tidak ada aksi berjalan → log; mode Previous butuh `CurrentActionIndex > 0`.

### 8.5 Hotkey Global
| Key | Saat rekam | Saat playback |
|---|---|---|
| F9 | ↓ (di MacroRecorder) | — |
| F11 | pause recorder (di MacroRecorder) | pause executor (`PlaybackHook_KeyDown`) |
| F12 | stop recorder | stop executor (cancel) |

### 8.6 Statistik Dashboard
```
interactions = jumlah aksi dengan Action != "move" dan != "wait"
duration     = Σ (a.Seconds / _currentSpeed)
TxtStatDuration: >= 60s → "Xm Ys", else "Z.Zs"
status: Recording | Playing | Idle
```

### 8.7 Utilitas Lain
- **Single instance**: Mutex `MacroBMRAppMutex`; instance kedua → MessageBox petunjuk F12 + shutdown.
- **Crash log**: `DispatcherUnhandledException` → append ke `crash_log.txt` + MessageBox.
- **EnumWindows** (`WindowUtils.GetOpenWindows`): daftar jendela visible dengan judul non-kosong (kecuali shell) untuk ComboBox background.
- **PromptInputDialog** buatan tangan: validasi digit-only utk threshold/sisip, auto-cap 100 utk threshold.

---

## 9. NexusClick — Auto-Clicker (Proyek Terpisah)

### 9.1 Spesifikasi
- WPF net6.0-windows, tema terang (AppBackground `#FAFAFA`, Accent `#0A2540`), MVVM minimal (`MainViewModel`, `RelayCommand`), botol tanpa border (CornerRadius 20).
- Hotkey global: **F8** (RegisterHotKey `0x77`).

### 9.2 Perhitungan Interval
```
baseInterval = H*3600000 + M*60000 + S*1000 + ms
failsafe: jika <= 0 → 10 ms
randomization: jitter = rnd.Next(10, 40)
actualInterval = baseInterval ± jitter (simpen ±; min 1 ms)
```

### 9.3 Input
- `MouseSimulator.Click(buttonType, doubleClick, x?, y?)` → `SetCursorPos` (jika fixed) + 2× `SendInput` (DOWN+UP); double click → `Thread.Sleep(30)` di antara.
- Position: `FollowCursor` (dinamis, klik di posisi kursor saat ini) atau `FixedPosition`.
- `PickLocation`: tunggu 2 detik → ambil `GetCursorPos`.
- Saat running: window di-minimize (hindari klik tombol sendiri); saat stop: restore + activate.

---

## 10. Build & Publish

```bash
# Verifikasi build — wajib 0 warning 0 error
dotnet build -c Release

# Publish stand-alone (framework-dependent, win-x64)
dotnet publish -c Release -r win-x64 --self-contained false `
  -o "bin\Release\net6.0-windows\win-x64\publish"

# Single instance exe untuk distribusi:
# app.manifest + mutex "MacroBMRAppMutex" sudah aktif.
```
Hasil publish: `MacroBMR.exe` + DLL pendukung (MaterialDesignThemes, Newtonsoft.Json, WindowsInput, Gma MouseKeyHook, AdvancedSharpAdbClient, System.Drawing).

**Aturan build.md**: jangan pernah pakai `php artisan` (ini .NET); exe wajib self-test (bersih exit, stabil saat background) sebelum dinyatakan rilis.

---

## 11. Catatan Teknis & Isu Dikenal

1. ~~**Skema `.bmr` tidak sinkron**~~ (DIPERBAIKI 2026-08-20): field `auto_execute` & `vision_interval` sekarang ada di `MacroProject` dengan `NullValueHandling.Ignore` — data lama tersimpan utuh.
2. **.NET 6 EOL** (support berakhir Nov 2024) → pertimbangkan upgrade ke .NET 8 LTS.
3. **Multi-monitor tidak didukung**: semua perhitungan koordinat memakai `Screen.PrimaryScreen`.
4. ~~**`implementation_plan.md` & `Dialogs/` kosong**~~ (DIBERSIHKAN 2026-09-10) — folder kosong dan file 0-byte dihapus, dialog dibuat di code-behind (`PromptInputDialog`, `EditSingleWaitTime`).
5. ~~**Dead code Core**~~ (DIBERSIHKAN 2026-09-10) — `AdbHelper.ScaleCoordinates`, `ScreenCapture.MATCH_THRESHOLD`, `MacroRecorder.RECT/GetWindowRect`, dan `MacroExecutor.SWP_NOZORDER/GetWindowLong` telah dibersihkan.
6. **NexusClick** tidak ikut di-build (di-exclude); punya tema berlawanan dengan BMR.
7. **Anti-deteksi dinonaktifkan** (`HumanizeInput = false`) — prioritas presisi, bukan kealamian input.
8. **Single-thread hook**: saat insert recording, `_playbackHook` di-dispose dulu agar dua hook global tidak bentrok di message pump yang sama.
9. Not git repo; `.gitignore` hanya berisi `.agents`. Direktori `bin/` & `obj/` ada di proyek.
10. Crash safety: Screencap ADB dan capture thumbnail dibungkus try/catch — kegagalan capture tidak pernah menghentikan rekaman/playback.

## 12. Perubahan 2026-08-20 (Perbaikan Review 8 Temuan)

| # | Severity | Temuan | Perbaikan |
|---|---|---|---|
| 1 | Kritis | Offscreen/default: `CaptureScreenArea` jatuh ke `CopyFromScreen` saat usePrintWindow gagal → klik ke koordinat layar kosong / salah window | Flag `OffscreenLocked`; gate di `CaptureScreenArea` (null, bukan CopyFromScreen) saat locked; reset otomatis di `ExecuteAsync.finally` |
| 2 | Kritis | Target menutup screenshot valid PrintWindow (cache 300ms) → false positive match dgn layar yang sebenarnya tidak tersedia | Validasi `IsBlankBitmap` pada frame `_cachedFullWin` → dispose + return null |
| 3 | Kritis | `FindTemplateOnScreen` early-exit score ≥ 98 → target tergeser >20px (fullscreen`origin -1`) tak pernah ketemu | Global best scan penuh + tie-break jarak; `HasGoodStructure` (edge check) filter false-positive; radius & inner radius dapat diset per aksi |
| 4 | Sedang | Sinkronisasi jendela by judul + delay 800ms tiap klik → judul berubah dinamis, mahal | Cocokkan **PID proses** (bukan hWnd) + throttle `_lastSmartSwitchTime` 3 detik |
| 5 | Sedang | Hold klik BG (50/150ms) tak diskala `SpeedMultiplier` → BG selalu lebih lambat | `max(5, (int)(base / SpeedMultiplier))` utk tap, click, double-click BG (ADB & SendMessage) |
| 6 | Sedang | ADB mati/restart di tengah playback → frame null selamanya (hanya log) | Retry `AdbHelper.Initialize()` + `UpdateBackgroundFrameCapture()` + `InvalidateFrameCache()` di `tryCount == 3`, sekali per sesi; peringatan setelah ±5s |
| 7 | Sedang | `GetRenderArea` pilih child terbesar; emulator punya banyak child besar (toolbar/log) → basis koordinat Bias | Preferensi rasio aspek: bila area >90% dari terbesar, pilih yang rasio paling mirip parent |
| 8 | Grup minor | TTL frame ADB (150ms) ≠ PrintWindow (300ms); `FixTarget` Current/Previous tanpa lock; skema `.bmr` hilangkan field | `FRAME_CACHE_MS = 300`; lock `targetAction` utk semua mode Fix; `MacroAction.SearchRadius`/`InnerRadius` & `MacroProject.AutoExecute`/`VisionInterval` (`NullValueHandling.Ignore`) |

Semua perubahan sudah di-build **0 warning 0 error** dan di-publish ke `bin\Release\net6.0-windows\win-x64\publish` (SHA256 DLL berbeda per RID folder karena target runtime, bukan isu).