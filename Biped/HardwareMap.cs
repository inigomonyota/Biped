using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace biped
{
    public static class HardwareMap
    {
        // We store this in the /cfg/ subdirectory
        private static string MapFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cfg", "hardware.map");

        // In-memory cache to avoid reading disk constantly
        private static Dictionary<string, int> _cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded = false;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _cache.Clear();

            if (File.Exists(MapFile))
            {
                try
                {
                    var lines = File.ReadAllLines(MapFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            string id = parts[0].Trim();
                            if (int.TryParse(parts[1], out int pos))
                            {
                                _cache[id] = pos;
                            }
                        }
                    }
                }
                catch { }
            }
            _loaded = true;
        }

        public static void AssignDeviceToPosition(int position, string uniqueId)
        {
            // Ensure directory exists
            string dir = Path.GetDirectoryName(MapFile);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            EnsureLoaded();

            // Update Memory
            _cache[uniqueId] = position;

            // Save to Disk
            SaveMap();
        }

        // --- NEW METHOD FOR THE UNMAP BUTTON ---
        public static void UnmapDevice(string uniqueId)
        {
            EnsureLoaded();

            if (_cache.ContainsKey(uniqueId))
            {
                _cache.Remove(uniqueId);
                SaveMap();
            }
        }
        // ---------------------------------------

        public static int GetPositionForDevice(string uniqueId)
        {
            EnsureLoaded();
            return _cache.ContainsKey(uniqueId) ? _cache[uniqueId] : 0;
        }

        private static void SaveMap()
        {
            try
            {
                // Ensure dir exists before saving (in case map was empty/new)
                string dir = Path.GetDirectoryName(MapFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var writer = new StreamWriter(MapFile))
                {
                    foreach (var kvp in _cache)
                    {
                        writer.WriteLine($"{kvp.Key}={kvp.Value}");
                    }
                }
            }
            catch { }
        }

    }
}