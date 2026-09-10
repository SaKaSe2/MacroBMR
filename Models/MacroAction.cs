using System.Collections.Generic;
using Newtonsoft.Json;

namespace MacroBMR.Models
{
    public class MacroAction
    {
        [JsonIgnore]
        public int Index { get; set; }

        [JsonProperty("action", NullValueHandling = NullValueHandling.Ignore)]
        public string Action { get; set; }

        // Click / Move
        [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)]
        public double? X { get; set; }

        [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)]
        public double? Y { get; set; }

        [JsonProperty("button", NullValueHandling = NullValueHandling.Ignore)]
        public string Button { get; set; }

        [JsonProperty("clicks", NullValueHandling = NullValueHandling.Ignore)]
        public int? Clicks { get; set; }
        
        [JsonProperty("duration", NullValueHandling = NullValueHandling.Ignore)]
        public double? Duration { get; set; }

        // Type
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("interval", NullValueHandling = NullValueHandling.Ignore)]
        public double? Interval { get; set; }

        // Press / Hotkey
        [JsonProperty("key", NullValueHandling = NullValueHandling.Ignore)]
        public string Key { get; set; }

        [JsonProperty("keys", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Keys { get; set; }

        // Wait
        [JsonProperty("seconds", NullValueHandling = NullValueHandling.Ignore)]
        public double? Seconds { get; set; }

        // Scroll
        [JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)]
        public int? Amount { get; set; }

        // System
        [JsonProperty("app_name", NullValueHandling = NullValueHandling.Ignore)]
        public string AppName { get; set; }

        [JsonProperty("filename", NullValueHandling = NullValueHandling.Ignore)]
        public string Filename { get; set; }
        
        [JsonProperty("filepath", NullValueHandling = NullValueHandling.Ignore)]
        public string Filepath { get; set; }

        // Smart Playback: screenshot kecil di sekitar titik klik saat recording
        [JsonProperty("click_thumbnail", NullValueHandling = NullValueHandling.Ignore)]
        public string ClickThumbnail { get; set; }

        // Smart Playback: versi background (PrintWindow) dari titik klik.
        // Dipakai saat background+offscreen agar bisa menembus penutup jendela target.
        [JsonProperty("click_thumbnail_bg", NullValueHandling = NullValueHandling.Ignore)]
        public string ClickThumbnailBg { get; set; }

        // Handle (id) jendela yang aktif pada saat klik direkam,
        // dipakai untuk mencocokkan PrintWindow yang konsisten saat playback.
        [JsonProperty("recorded_window_id", NullValueHandling = NullValueHandling.Ignore)]
        public long RecordedWindowId { get; set; }

        // Smart Playback: judul jendela yang aktif saat aksi direkam
        [JsonProperty("recorded_window", NullValueHandling = NullValueHandling.Ignore)]
        public string RecordedWindowTitle { get; set; }

        // Smart Playback: Rekaman alternatif (fallback) jika target utama tidak ditemukan
        [JsonProperty("fallbacks", NullValueHandling = NullValueHandling.Ignore)]
        public List<FallbackTarget> Fallbacks { get; set; }

        [JsonProperty("match_threshold", NullValueHandling = NullValueHandling.Ignore)]
        public double? MatchThreshold { get; set; }

        // Radius pencarian template di sekitar koordinat asli saat SmartPlayback (default 20px).
        // Field opsional -> NullValueHandling.Ignore agar file .bmr lama tetap terbaca.
        [JsonProperty("search_radius", NullValueHandling = NullValueHandling.Ignore)]
        public int? SearchRadius { get; set; }

        // Radius zona dalam (center area) template saat pencocokan (default 30px).
        // Field opsional -> NullValueHandling.Ignore agar file .bmr lama tetap terbaca.
        [JsonProperty("inner_radius", NullValueHandling = NullValueHandling.Ignore)]
        public int? InnerRadius { get; set; }

        [JsonProperty("smart_wait", NullValueHandling = NullValueHandling.Ignore)]
        public double? SmartWait { get; set; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public List<MousePoint> Path { get; set; }

        // Utility property for display in UI
        [JsonIgnore]
        public string DisplayDetails 
        {
            get 
            {
                var dict = new Dictionary<string, object>();
                if (X.HasValue) dict["x"] = X;
                if (Y.HasValue) dict["y"] = Y;
                if (!string.IsNullOrEmpty(Text)) dict["text"] = Text;
                if (!string.IsNullOrEmpty(Key)) dict["key"] = Key;
                if (Keys != null && Keys.Count > 0) dict["keys"] = string.Join("+", Keys);
                if (Seconds.HasValue) dict["seconds"] = Seconds;
                if (!string.IsNullOrEmpty(AppName)) dict["app_name"] = AppName;
                
                var details = new List<string>();
                foreach (var kvp in dict) details.Add($"{kvp.Key}={kvp.Value}");
                return string.Join(", ", details);
            }
        }
    }

    public class FallbackTarget
    {
        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }
    }

    public class MousePoint
    {
        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("delay")]
        public double Delay { get; set; }
    }
}
