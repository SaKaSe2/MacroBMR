using System.Collections.Generic;
using Newtonsoft.Json;

namespace MacroBMR.Models
{
    public class MacroProject
    {
        [JsonProperty("loop_count")]
        public string LoopCount { get; set; }

        [JsonProperty("record_mouse")]
        public bool RecordMouse { get; set; }

        [JsonProperty("smart_playback")]
        public bool SmartPlayback { get; set; }

        [JsonProperty("dynamic_threshold")]
        public bool DynamicThreshold { get; set; }

        [JsonProperty("actions")]
        public List<MacroAction> Actions { get; set; }

        [JsonProperty("speed_multiplier")]
        public double SpeedMultiplier { get; set; } = 1.0;

        // Auto-execute: jalankan makro otomatis setelah proyek dimuat (default true).
        // Field opsional -> NullValueHandling.Ignore agar file .bmr lama tetap terbaca.
        [JsonProperty("auto_execute", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoExecute { get; set; }

        // Mode rekaman: true = Background Mode, false = Normal Mode
        [JsonProperty("is_background_mode", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsBackgroundMode { get; set; }

        [JsonProperty("target_window_title", NullValueHandling = NullValueHandling.Ignore)]
        public string TargetWindowTitle { get; set; }

        // Interval ulang perbaikan target otomatis (ms). Field opsional, backward compatible.
        [JsonProperty("vision_interval", NullValueHandling = NullValueHandling.Ignore)]
        public int? VisionInterval { get; set; }

        // Helper untuk kode lama yang memakai bool non-nullable
        [JsonIgnore]
        public bool AutoExecuteEnabled => AutoExecute ?? true;
    }
}
