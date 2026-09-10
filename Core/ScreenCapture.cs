using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Collections.Concurrent;

namespace MacroBMR.Core
{
    public static class ScreenCapture
    {
        public static IntPtr BackgroundWindowHandle { get; set; } = IntPtr.Zero;

        // Jika true, gunakan PrintWindow (untuk mode Hide / window tersembunyi di belakang).
        // Jika false, gunakan CopyFromScreen (zero flicker, cocok saat window visible di layar).
        public static bool IsOffscreenCapture { get; set; } = false;

        // Saat aktif (Background + Offscreen), CaptureScreenArea DILARANG jatuh ke CopyFromScreen:
        // window target sedang di belakang window lain, jadi piksel layar bukanlah konten target.
        public static bool OffscreenLocked { get; set; } = false;

        // Saat aktif, CaptureScreenArea membaca frame layar perangkat Android langsung via ADB (screencap)
        public static bool UseAdbFrameCapture { get; set; } = false;

        // Area window emulator yang menampilkan layar Android (dalam koordinat layar absolut)
        public static RECT RenderRect { get; set; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
        private static readonly ImageCodecInfo JpegCodec = GetJpegCodec();

        public class TemplateData
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int Stride { get; set; }
            public byte[] Pixels { get; set; }
        }

        // Cache template untuk menghindari GC overhead akibat decode base64 terus menerus
        private static readonly ConcurrentDictionary<string, TemplateData> _templateCache = new ConcurrentDictionary<string, TemplateData>();

        public static TemplateData GetTemplateData(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            
            if (_templateCache.TryGetValue(base64, out var cached))
                return cached;
                
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using var ms = new MemoryStream(bytes);
                using var bmp = new Bitmap(ms);
                
                var data = new TemplateData
                {
                    Width = bmp.Width,
                    Height = bmp.Height
                };
                
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                
                data.Stride = bmpData.Stride;
                int bytesCount = bmpData.Stride * bmp.Height;
                data.Pixels = new byte[bytesCount];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, data.Pixels, 0, bytesCount);
                
                bmp.UnlockBits(bmpData);
                
                // Mencegah memory leak jika cache terlalu besar
                if (_templateCache.Count > 100) _templateCache.Clear();
                _templateCache[base64] = data;
                
                return data;
            }
            catch
            {
                return null;
            }
        }

        // Ambil screenshot layar penuh dan kembalikan sebagai Base64 JPEG
        public static string CaptureScreenAsBase64(int quality = 75)
        {
            int captureX = 0;
            int captureY = 0;
            int width = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            int height = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

            if (BackgroundWindowHandle != IntPtr.Zero && GetWindowRect(BackgroundWindowHandle, out RECT rect))
            {
                captureX = rect.Left;
                captureY = rect.Top;
                width = Math.Max(1, rect.Width);
                height = Math.Max(1, rect.Height);
            }

            using var bitmap = CaptureScreenArea(captureX, captureY, width, height, true);
            if (bitmap == null) return "";

            // Kompres sebagai JPEG agar ukuran base64 tidak terlalu besar
            using var ms = new MemoryStream();
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

            if (JpegCodec != null)
            {
                bitmap.Save(ms, JpegCodec, encoderParams);
            }
            else
            {
                bitmap.Save(ms, ImageFormat.Jpeg);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        private static ImageCodecInfo GetJpegCodec()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    return codec;
            }
            return null;
        }

        // Ambil crop kecil di sekitar titik klik (untuk Smart Playback)
        public static string CropAroundPoint(int x, int y, int radius = 100)
        {
            int screenW = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            int screenH = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

            int cropX = Math.Max(0, Math.Min(x - radius, screenW - 1));
            int cropY = Math.Max(0, Math.Min(y - radius, screenH - 1));
            int cropW = Math.Max(1, Math.Min(radius * 2, screenW - cropX));
            int cropH = Math.Max(1, Math.Min(radius * 2, screenH - cropY));

            using var cropped = CaptureScreenArea(cropX, cropY, cropW, cropH, false);

            using var ms = new MemoryStream();
            if (JpegCodec != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                cropped.Save(ms, JpegCodec, encoderParams);
            }
            else
            {
                cropped.Save(ms, ImageFormat.Jpeg);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        // Ambil crop kecil di sekitar titik klik VIA PRINTWINDOW (untuk thumbnail background).
        // Jika PrintWindow gagal (GPU-rendered window seperti BlueStacks), fallback ke CopyFromScreen
        // karena saat rekaman jendela target pasti visible di layar.
        public static string CropWindowBg(long hwnd, int x, int y, int radius = 100)
        {
            IntPtr window = new IntPtr(hwnd);
            int screenW = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
            int screenH = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

            int cropX = Math.Max(0, Math.Min(x - radius, screenW - 1));
            int cropY = Math.Max(0, Math.Min(y - radius, screenH - 1));
            int cropW = Math.Max(1, Math.Min(radius * 2, screenW - cropX));
            int cropH = Math.Max(1, Math.Min(radius * 2, screenH - cropY));

            IntPtr prevHandle = BackgroundWindowHandle;
            bool prevOffscreen = IsOffscreenCapture;
            lock (_cacheLock)
            {
            BackgroundWindowHandle = window;
            IsOffscreenCapture = true;

            try
            {
                Bitmap cropped = CaptureScreenArea(cropX, cropY, cropW, cropH, true);

                // Fallback: PrintWindow gagal (GPU window) → pakai CopyFromScreen
                // Saat rekaman, jendela pasti visible di layar jadi hasilnya tetap valid.
                if (cropped == null)
                {
                    cropped = CaptureScreenArea(cropX, cropY, cropW, cropH, false);
                }

                if (cropped == null) return null;

                using (cropped)
                {
                    using var ms = new MemoryStream();
                    if (JpegCodec != null)
                    {
                        using var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                        cropped.Save(ms, JpegCodec, encoderParams);
                    }
                    else
                    {
                        cropped.Save(ms, ImageFormat.Jpeg);
                    }

                    string base64 = Convert.ToBase64String(ms.ToArray());
                    return IsBlankThumbnail(base64) ? null : base64;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                BackgroundWindowHandle = prevHandle;
                IsOffscreenCapture = prevOffscreen;
            }
            }
        }

        // Deteksi thumbnail yang kosong/hitam (indikasi PrintWindow gagal menangkap jendela).
        public static bool IsBlankThumbnail(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return true;

            var template = GetTemplateData(base64);
            if (template == null) return true;

            return IsBlankPixels(template.Pixels, template.Stride, template.Width, template.Height);
        }

        // Deteksi bitmap (frame PrintWindow langsung) yang kosong/hitam.
        private static bool IsBlankBitmap(Bitmap bmp)
        {
            if (bmp == null) return true;
            try
            {
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int bytesCount = bd.Stride * bd.Height;
                    byte[] pixels = new byte[bytesCount];
                    System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, pixels, 0, bytesCount);
                    return IsBlankPixels(pixels, bd.Stride, bmp.Width, bmp.Height);
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
            }
            catch
            {
                return true; // Tidak bisa dibaca = dianggap tidak tersedia
            }
        }

        // Statistik kecerahan sederhana: < 5% pixel terang (R+G+B > 40) dianggap kosong.
        private static bool IsBlankPixels(byte[] pixels, int stride, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0) return true;

            long brightPixels = 0;
            long total = 0;
            for (int y = 0; y < height; y += 8)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x += 8)
                {
                    int offset = rowOffset + x * 3;
                    if (offset + 2 >= pixels.Length) continue;
                    byte b = pixels[offset];
                    byte g = pixels[offset + 1];
                    byte r = pixels[offset + 2];
                    if (r + g + b > 40) brightPixels++;
                    total++;
                }
            }

            return total > 0 && brightPixels * 100 / total < 5;
        }

        // Verifikasi struktur tepi (edge) di zona dalam: menolak kandidat yang warnanya mirip
        // tapi strukturnya jauh berbeda (misal ikon/button bersebelahan dengan warna senada).
        // Membandingkan ada/tidaknya perubahan luminansi (>= EDGE_LUM_DIFF) antar pixel sampling.
        private static unsafe bool HasGoodStructure(byte* pScreen, int screenStride, int originX, int originY,
            byte* pThumb, int thumbStride, int thumbW, int thumbH, int step, int centerTx = -1, int centerTy = -1)
        {
            const int EDGE_LUM_DIFF = 40;
            const double MAX_MISMATCH_RATIO = 0.55;
            const int INNER = 30;

            if (centerTx < 0) centerTx = thumbW / 2;
            if (centerTy < 0) centerTy = thumbH / 2;

            int left = Math.Max(0, centerTx - INNER);
            int top = Math.Max(0, centerTy - INNER);
            int right = Math.Min(thumbW, centerTx + INNER);
            int bottom = Math.Min(thumbH, centerTy + INNER);

            int total = 0;
            int mismatched = 0;

            for (int ty = top; ty < bottom; ty += step)
            {
                int sRow = (originY + ty) * screenStride;
                int tRow = ty * thumbStride;

                for (int tx = left; tx < right; tx += step)
                {
                    int si = sRow + (originX + tx) * 3;
                    int ti = tRow + tx * 3;
                    int sLum = pScreen[si] + pScreen[si + 1] + pScreen[si + 2];
                    int tLum = pThumb[ti] + pThumb[ti + 1] + pThumb[ti + 2];

                    // Tepi horizontal
                    if (tx + step < thumbW)
                    {
                        int si2 = si + step * 3;
                        int ti2 = ti + step * 3;
                        bool sEdge = Math.Abs(sLum - (pScreen[si2] + pScreen[si2 + 1] + pScreen[si2 + 2])) >= EDGE_LUM_DIFF;
                        bool tEdge = Math.Abs(tLum - (pThumb[ti2] + pThumb[ti2 + 1] + pThumb[ti2 + 2])) >= EDGE_LUM_DIFF;
                        if (sEdge != tEdge) mismatched++;
                        total++;
                    }

                    // Tepi vertikal
                    if (ty + step < thumbH)
                    {
                        int sni = (originY + ty + step) * screenStride + (originX + tx) * 3;
                        int tni = (ty + step) * thumbStride + tx * 3;
                        bool sEdge = Math.Abs(sLum - (pScreen[sni] + pScreen[sni + 1] + pScreen[sni + 2])) >= EDGE_LUM_DIFF;
                        bool tEdge = Math.Abs(tLum - (pThumb[tni] + pThumb[tni + 1] + pThumb[tni + 2])) >= EDGE_LUM_DIFF;
                        if (sEdge != tEdge) mismatched++;
                        total++;
                    }
                }
            }

            return total == 0 || (double)mismatched / total < MAX_MISMATCH_RATIO;
        }

        public static unsafe bool CompareCropWithThumbnail(int x, int y, string recordedBase64, double matchThreshold = 90.0, bool usePrintWindow = false, int? innerRadiusOverride = null)
        {
            if (string.IsNullOrEmpty(recordedBase64)) return false;

            try
            {
                var template = GetTemplateData(recordedBase64);
                if (template == null) return false;

                int radius = 100;
                int screenW = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                int screenH = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                
                int cropX = Math.Max(0, Math.Min(x - radius, screenW - 1));
                int cropY = Math.Max(0, Math.Min(y - radius, screenH - 1));
                int cropW = Math.Max(1, Math.Min(radius * 2, screenW - cropX));
                int cropH = Math.Max(1, Math.Min(radius * 2, screenH - cropY));

                using var currentBmp = CaptureScreenArea(cropX, cropY, cropW, cropH, usePrintWindow);
                if (currentBmp == null) return false;

                if (currentBmp.Width != template.Width || currentBmp.Height != template.Height)
                    return false;

                int innerMatch = 0, innerTotal = 0;
                int outerMatch = 0, outerTotal = 0;
                int step = 4;

                int innerRadius = innerRadiusOverride ?? 30;
                // Pakai posisi klik aktual di dalam crop, bukan pusat geometris.
                // Saat crop di-clamp di tepi layar/window, titik klik bisa bergeser dari tengah.
                int centerPx = Math.Max(0, Math.Min(x - cropX, cropW - 1));
                int centerPy = Math.Max(0, Math.Min(y - cropY, cropH - 1));
                int innerLeft = Math.Max(0, centerPx - innerRadius);
                int innerTop = Math.Max(0, centerPy - innerRadius);
                int innerRight = Math.Min(cropW, centerPx + innerRadius);
                int innerBottom = Math.Min(cropH, centerPy + innerRadius);

                Rectangle rect = new Rectangle(0, 0, cropW, cropH);
                BitmapData currentData = currentBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

                int stride = currentData.Stride;
                byte* pCurrent = (byte*)currentData.Scan0;

                long currentTotalR = 0, currentTotalG = 0, currentTotalB = 0;
                long recordedTotalR = 0, recordedTotalG = 0, recordedTotalB = 0;
                bool structureOk = true;

                fixed (byte* pRecorded = template.Pixels)
                {
                    for (int py = 0; py < cropH; py += step)
                    {
                        int rowOffset = py * stride;
                        for (int px = 0; px < cropW; px += step)
                        {
                            int offset = rowOffset + (px * 3);

                            byte cB = pCurrent[offset];
                            byte cG = pCurrent[offset + 1];
                            byte cR = pCurrent[offset + 2];

                            byte rB = pRecorded[offset];
                            byte rG = pRecorded[offset + 1];
                            byte rR = pRecorded[offset + 2];

                            int diff = Math.Abs(cB - rB) + Math.Abs(cG - rG) + Math.Abs(cR - rR);
                            int diffLimit = matchThreshold >= 98.0 ? 15 : (usePrintWindow ? 50 : 35);
                            bool isMatch = diff < diffLimit;

                            if (px >= innerLeft && px < innerRight && py >= innerTop && py < innerBottom)
                            {
                                if (isMatch) innerMatch++;
                                innerTotal++;
                            }
                            else
                            {
                                if (isMatch) outerMatch++;
                                outerTotal++;
                            }

                            if (cG > cR + 15 && cG > cB + 15 && cG > 50) currentTotalG++;
                            else if (cR > cG + 15 && cR > cB + 15 && cR > 50) currentTotalR++;
                            else if (cB > cR + 15 && cB > cG + 15 && cB > 50) currentTotalB++;

                            if (rG > rR + 15 && rG > rB + 15 && rG > 50) recordedTotalG++;
                            else if (rR > rG + 15 && rR > rB + 15 && rR > 50) recordedTotalR++;
                            else if (rB > rR + 15 && rB > rG + 15 && rB > 50) recordedTotalB++;
                        }
                    }

                    // Verifikasi struktur tepi di zona dalam di sekitar titik klik aktual
                    structureOk = HasGoodStructure(pCurrent, stride, 0, 0, pRecorded, template.Stride, cropW, cropH, step, centerPx, centerPy);
                }

                currentBmp.UnlockBits(currentData);

                if (!structureOk) return false;

                int totalPx = innerTotal + outerTotal;
                if (totalPx > 0)
                {
                    double recPctG = (double)recordedTotalG / totalPx * 100;
                    double curPctG = (double)currentTotalG / totalPx * 100;
                    if (recPctG >= 5.0 && curPctG < recPctG * 0.25) return false;

                    double recPctR = (double)recordedTotalR / totalPx * 100;
                    double curPctR = (double)currentTotalR / totalPx * 100;
                    if (recPctR >= 5.0 && curPctR < recPctR * 0.25) return false;

                    double recPctB = (double)recordedTotalB / totalPx * 100;
                    double curPctB = (double)currentTotalB / totalPx * 100;
                    if (recPctB >= 5.0 && curPctB < recPctB * 0.25) return false;
                }

                if (innerTotal > 0)
                {
                    double innerPercent = (double)innerMatch / innerTotal * 100.0;
                    if (innerPercent < matchThreshold) return false;
                }

                if (outerTotal > 0)
                {
                    double outerPercent = (double)outerMatch / outerTotal * 100.0;
                    double minOuter = usePrintWindow ? (matchThreshold * 0.50) : (matchThreshold * 0.70);
                    if (outerPercent < minOuter) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static unsafe (bool found, int x, int y, double confidence) FindTemplateOnScreen(
            string recordedBase64, int originX = -1, int originY = -1, int step = 4, double matchThreshold = 90.0, bool usePrintWindow = false, int? innerRadiusOverride = null, int? searchRadiusOverride = null)
        {
            if (string.IsNullOrEmpty(recordedBase64)) return (false, 0, 0, 0);

            try
            {
                var template = GetTemplateData(recordedBase64);
                if (template == null) return (false, 0, 0, 0);

                int fullScreenW = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                int fullScreenH = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

                int thumbW = template.Width;
                int thumbH = template.Height;

                int inner = innerRadiusOverride ?? 30;
                int captureX = 0, captureY = 0, captureW = fullScreenW, captureH = fullScreenH;

                // Hitung offset klik dalam template (saat rekaman, crop di-clamp di tepi layar)
                // Rumus sama persis dengan CropAroundPoint / CropWindowBg: cropX = max(0, x - 100)
                int clickOffsetInThumbX = thumbW / 2; // default: tengah
                int clickOffsetInThumbY = thumbH / 2;
                if (originX != -1 && originY != -1)
                {
                    int recCropX = Math.Max(0, originX - 100);
                    int recCropY = Math.Max(0, originY - 100);
                    clickOffsetInThumbX = Math.Min(thumbW - 1, Math.Max(0, originX - recCropX));
                    clickOffsetInThumbY = Math.Min(thumbH - 1, Math.Max(0, originY - recCropY));
                }

                if (originX != -1 && originY != -1)
                {
                    int radius = searchRadiusOverride ?? 20;
                    captureX = originX - thumbW / 2 - radius;
                    captureY = originY - thumbH / 2 - radius;
                    captureW = thumbW + radius * 2;
                    captureH = thumbH + radius * 2;

                    captureX = Math.Max(0, Math.Min(captureX, fullScreenW - 1));
                    captureY = Math.Max(0, Math.Min(captureY, fullScreenH - 1));
                    captureW = Math.Min(captureW, fullScreenW - captureX);
                    captureH = Math.Min(captureH, fullScreenH - captureY);
                }
                else
                {
                    captureX = 0;
                    captureY = 0;
                    captureW = fullScreenW;
                    captureH = fullScreenH;
                }

                using var screenBmp = CaptureScreenArea(captureX, captureY, captureW, captureH, usePrintWindow);
                if (screenBmp == null) return (false, 0, 0, 0);

                Rectangle screenRect = new Rectangle(0, 0, captureW, captureH);
                BitmapData screenData = screenBmp.LockBits(screenRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                
                int screenStride = screenData.Stride;
                int thumbStride = template.Stride;

                byte* pScreen = (byte*)screenData.Scan0;
                double bestScore = 0.0;
                int bestX = 0, bestY = 0;
                double bestDistance = double.MaxValue;
                int sampleCount = (thumbH / step) * (thumbW / step);
                if (sampleCount == 0) sampleCount = 1;

                fixed (byte* pThumb = template.Pixels)
                {
                    for (int sy = 0; sy <= captureH - thumbH; sy += step)
                    {
                        for (int sx = 0; sx <= captureW - thumbW; sx += step)
                        {
                            int matchCount = 0;
                            int innerMatchCount = 0;
                            int innerTotalCount = 0;

                            // Pakai offset klik aktual (bukan pusat geometris) agar edge crops cocok
                            int centerTx = clickOffsetInThumbX;
                            int centerTy = clickOffsetInThumbY;

                            for (int ty = 0; ty < thumbH; ty += step)
                            {
                                int screenRowOffset = (sy + ty) * screenStride;
                                int thumbRowOffset = ty * thumbStride;

                                for (int tx = 0; tx < thumbW; tx += step)
                                {
                                    int si = screenRowOffset + (sx + tx) * 3;
                                    int ti = thumbRowOffset + tx * 3;

                                    byte sB = pScreen[si];
                                    byte sG = pScreen[si + 1];
                                    byte sR = pScreen[si + 2];

                                    byte tB = pThumb[ti];
                                    byte tG = pThumb[ti + 1];
                                    byte tR = pThumb[ti + 2];

                                    int diff = Math.Abs(sB - tB) + Math.Abs(sG - tG) + Math.Abs(sR - tR);
                                    int diffLimit = matchThreshold >= 98.0 ? 15 : (usePrintWindow ? 50 : 35);
                                    bool isMatch = diff < diffLimit;
                                    
                                    if (isMatch) matchCount++;

                                    bool isInner = Math.Abs(tx - centerTx) <= inner && Math.Abs(ty - centerTy) <= inner;
                                    if (isInner)
                                    {
                                        if (isMatch) innerMatchCount++;
                                        innerTotalCount++;
                                    }
                                }
                            }

                            double score = (double)matchCount / sampleCount * 100.0;
                            double innerScore = innerTotalCount > 0 ? (double)innerMatchCount / innerTotalCount * 100.0 : score;
                            double minOverall = usePrintWindow ? (matchThreshold * 0.60) : matchThreshold;

                            if (innerScore >= matchThreshold && score >= minOverall)
                            {
                                // Tolak kandidat yang strukturnya (tepi) jauh berbeda walau warnanya mirip
                                if (!HasGoodStructure(pScreen, screenStride, sx, sy, pThumb, thumbStride, thumbW, thumbH, step, centerTx, centerTy))
                                {
                                    continue;
                                }

                                int actualX = captureX + sx + clickOffsetInThumbX;
                                int actualY = captureY + sy + clickOffsetInThumbY;

                                double dist = (originX != -1 && originY != -1) ? 
                                    Math.Sqrt(Math.Pow(actualX - originX, 2) + Math.Pow(actualY - originY, 2)) : 0;

                                if (score > bestScore || (Math.Abs(score - bestScore) < 0.01 && dist < bestDistance))
                                {
                                    bestScore = score;
                                    bestX = actualX;
                                    bestY = actualY;
                                    bestDistance = dist;
                                }
                            }
                        }
                    }
                }

                screenBmp.UnlockBits(screenData);

                double finalMinScore = usePrintWindow ? (matchThreshold * 0.60) : matchThreshold;
                if (bestScore >= finalMinScore)
                {
                    return (true, bestX, bestY, bestScore);
                }

                return (false, 0, 0, bestScore);
            }
            catch
            {
                return (false, 0, 0, 0);
            }
        }

        private static Bitmap _cachedFullWin = null;
        private static long _cachedFullWinTime = 0;
        private static readonly object _cacheLock = new object();

        private static Bitmap CaptureScreenArea(int x, int y, int width, int height, bool usePrintWindow = false)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);

            if (usePrintWindow && UseAdbFrameCapture && AdbHelper.IsConnected)
            {
                if (RenderRect.Width > 0 && RenderRect.Height > 0)
                {
                    using var frame = AdbHelper.GetScaledFrame(RenderRect.Width, RenderRect.Height);
                    if (frame != null)
                    {
                        int winLeft = (RenderRect.Left >= -5000 && RenderRect.Right > RenderRect.Left) ? RenderRect.Left : 0;
                        int winTop = (RenderRect.Top >= -5000 && RenderRect.Bottom > RenderRect.Top) ? RenderRect.Top : 0;
                        int winWidth = frame.Width;
                        int winHeight = frame.Height;

                        // Hitung perpotongan presisi antara crop box dan area render jendela
                        int intersectLeft = Math.Max(x, winLeft);
                        int intersectTop = Math.Max(y, winTop);
                        int intersectRight = Math.Min(x + width, winLeft + winWidth);
                        int intersectBottom = Math.Min(y + height, winTop + winHeight);

                        int intersectWidth = intersectRight - intersectLeft;
                        int intersectHeight = intersectBottom - intersectTop;

                        if (intersectWidth > 0 && intersectHeight > 0)
                        {
                            int srcX = intersectLeft - winLeft;
                            int srcY = intersectTop - winTop;
                            int destX = intersectLeft - x;
                            int destY = intersectTop - y;

                            g.DrawImage(frame,
                                new Rectangle(destX, destY, intersectWidth, intersectHeight),
                                new Rectangle(srcX, srcY, intersectWidth, intersectHeight),
                                GraphicsUnit.Pixel);
                        }
                        return bmp;
                    }
                }
            }

            if (usePrintWindow && BackgroundWindowHandle != IntPtr.Zero)
            {
                if (GetWindowRect(BackgroundWindowHandle, out RECT rect))
                {
                    lock (_cacheLock)
                    {
                        long now = Environment.TickCount64;
                        if (_cachedFullWin == null || (now - _cachedFullWinTime) > 300 || 
                            _cachedFullWin.Width != Math.Max(1, rect.Width) || 
                            _cachedFullWin.Height != Math.Max(1, rect.Height))
                        {
                            _cachedFullWin?.Dispose();
                            _cachedFullWin = new Bitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height), PixelFormat.Format24bppRgb);
                            using var gWin = Graphics.FromImage(_cachedFullWin);
                            IntPtr hdc = gWin.GetHdc();
                            try 
                            { 
                                bool pwResult = PrintWindow(BackgroundWindowHandle, hdc, 2); 
                                if (!pwResult)
                                {
                                    PrintWindow(BackgroundWindowHandle, hdc, 0);
                                }
                            }
                            finally 
                            { 
                                gWin.ReleaseHdc(hdc); 
                            }

                            // PrintWindow sering gagal pada window render GPU (emulator seperti LDPlayer/BlueStacks):
                            // Jika jendela sedang terlihat di layar (tidak OffscreenLocked), fallback ke CopyFromScreen.
                            if (IsBlankBitmap(_cachedFullWin))
                            {
                                _cachedFullWin.Dispose();
                                _cachedFullWin = null;
                                _cachedFullWinTime = 0;

                                if (!OffscreenLocked)
                                {
                                    try
                                    {
                                        g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                                        return bmp;
                                    }
                                    catch
                                    {
                                        return null;
                                    }
                                }
                                return null;
                            }

                            _cachedFullWinTime = now;
                        }
                        
                        int winLeft = rect.Left;
                        int winTop = rect.Top;
                        int winWidth = _cachedFullWin.Width;
                        int winHeight = _cachedFullWin.Height;

                        // Hitung perpotongan presisi antara crop box dan area render jendela
                        int intersectLeft = Math.Max(x, winLeft);
                        int intersectTop = Math.Max(y, winTop);
                        int intersectRight = Math.Min(x + width, winLeft + winWidth);
                        int intersectBottom = Math.Min(y + height, winTop + winHeight);

                        int intersectWidth = intersectRight - intersectLeft;
                        int intersectHeight = intersectBottom - intersectTop;

                        if (intersectWidth > 0 && intersectHeight > 0)
                        {
                            int srcX = intersectLeft - winLeft;
                            int srcY = intersectTop - winTop;
                            int destX = intersectLeft - x;
                            int destY = intersectTop - y;

                            try
                            {
                                g.DrawImage(_cachedFullWin,
                                    new Rectangle(destX, destY, intersectWidth, intersectHeight),
                                    new Rectangle(srcX, srcY, intersectWidth, intersectHeight),
                                    GraphicsUnit.Pixel);
                            }
                            catch (ObjectDisposedException)
                            {
                                return null; // Cache dibuang oleh thread lain; coba lagi di iterasi berikut
                            }
                        }
                    }
                    return bmp;
                }
                else if (OffscreenLocked)
                {
                    return null; // Window tidak punya rect valid saat offscreen: jangan pakai piksel layar
                }
            }

            // JANGAN jatuh ke CopyFromScreen saat Offscreen aktif: layar bukan konten window target
            if (usePrintWindow && OffscreenLocked)
            {
                return null;
            }

            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            return bmp;
        }

        // Hapus hanya cache frame (bukan template) — dipakai saat sumber frame ADB berubah
        public static void InvalidateFrameCache()
        {
            lock (_cacheLock)
            {
                _cachedFullWin?.Dispose();
                _cachedFullWin = null;
                _cachedFullWinTime = 0;
            }
        }

        public static void ClearCache()
        {
            InvalidateFrameCache();
            _templateCache.Clear();
            GC.Collect(0, GCCollectionMode.Optimized);
        }
    }
}
