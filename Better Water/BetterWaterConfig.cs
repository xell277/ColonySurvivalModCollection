using System;
using System.IO;
using Newtonsoft.Json;

namespace BetterWater
{
    public sealed class BetterWaterConfig
    {
        public bool Enabled = true;
        public bool VanillaSpreadEnabled = false;
        public int MaxFallDistance = 10;
        public int HorizontalSpread = 3;
        public int SourceHorizontalSpread = 6;
        public int FlowHorizontalSpread = 3;
        public int TickIntervalMilliseconds = 250;
        public int MaxUpdatesPerTick = 96;
        public bool TreatUntrackedWaterAsSource = true;
        public bool EnableWaterfallSounds = true;
        public string WaterfallAudioClipName = "BetterWater.Waterfall";
        public int WaterfallAudioCooldownMilliseconds = 6000;
        public int WaterfallAudioLoopMilliseconds = 12000;
        public int WaterfallAudioMinimumFallDistance = 1;
        public int WaterfallAudioClusterRadius = 6;
        public bool ShowSourceMarkers = false;
        public int SourceMarkerIntervalMilliseconds = 4000;
        public float SourceMarkerDurationSeconds = 4.5f;
        public bool DebugLogging = false;

        public static BetterWaterConfig LoadOrCreate(string path)
        {
            BetterWaterConfig config = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    config = JsonConvert.DeserializeObject<BetterWaterConfig>(File.ReadAllText(path));
                }
                catch (Exception exception)
                {
                    BetterWaterLogger.Write("Failed to read config, using defaults: " + exception.Message);
                }
            }

            if (config == null)
            {
                config = new BetterWaterConfig();
            }

            config.Normalize();
            Save(path, config);
            return config;
        }

        public static void Save(string path, BetterWaterConfig config)
        {
            if (string.IsNullOrEmpty(path) || config == null)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to save config: " + exception.Message);
            }
        }

        public void Normalize()
        {
            MaxFallDistance = Math.Max(1, Math.Min(64, MaxFallDistance));
            SourceHorizontalSpread = Math.Max(0, Math.Min(32, SourceHorizontalSpread));
            FlowHorizontalSpread = Math.Max(0, Math.Min(32, FlowHorizontalSpread));
            HorizontalSpread = FlowHorizontalSpread;
            TickIntervalMilliseconds = Math.Max(50, Math.Min(5000, TickIntervalMilliseconds));
            MaxUpdatesPerTick = Math.Max(1, Math.Min(4096, MaxUpdatesPerTick));
            if (string.IsNullOrWhiteSpace(WaterfallAudioClipName))
            {
                WaterfallAudioClipName = "BetterWater.Waterfall";
            }

            WaterfallAudioCooldownMilliseconds = Math.Max(250, Math.Min(60000, WaterfallAudioCooldownMilliseconds));
            WaterfallAudioLoopMilliseconds = Math.Max(1000, Math.Min(60000, WaterfallAudioLoopMilliseconds));
            WaterfallAudioMinimumFallDistance = Math.Max(1, Math.Min(64, WaterfallAudioMinimumFallDistance));
            WaterfallAudioClusterRadius = Math.Max(1, Math.Min(64, WaterfallAudioClusterRadius));
            SourceMarkerIntervalMilliseconds = Math.Max(1000, Math.Min(60000, SourceMarkerIntervalMilliseconds));
            SourceMarkerDurationSeconds = Math.Max(1f, Math.Min(30f, SourceMarkerDurationSeconds));
        }
    }
}
