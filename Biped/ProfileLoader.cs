using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace biped
{
    public class GameProfile
    {
        // Dictionary: [Position ID] -> [Config Object]
        public Dictionary<int, Config> PositionBindings { get; } = new Dictionary<int, Config>();
    }

    public static class ProfileLoader
    {
        public static GameProfile Load(string filePath)
        {
            var profile = new GameProfile();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return profile;

            try
            {
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    string clean = line.Trim();
                    if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith(";") || clean.StartsWith("#"))
                        continue;

                    // Format: Position1 = 123, 456, 789
                    var parts = clean.Split('=');
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    if (key.StartsWith("Position", StringComparison.OrdinalIgnoreCase))
                    {
                        string numStr = key.Substring(8);
                        if (int.TryParse(numStr, out int posID))
                        {
                            var codes = val.Split(',');
                            if (codes.Length == 3)
                            {
                                if (uint.TryParse(codes[0], out uint l) &&
                                    uint.TryParse(codes[1], out uint m) &&
                                    uint.TryParse(codes[2], out uint r))
                                {
                                    profile.PositionBindings[posID] = new Config(l, m, r);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return profile;
        }

        public static void Save(string filePath, List<BipedDevice> devices)
        {
            // Ensure directory exists
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("; Biped Game Profile");
                writer.WriteLine($"; Saved: {DateTime.Now}");
                writer.WriteLine("; Format: PositionX = Left, Middle, Right");
                writer.WriteLine("");

                // Sort by Position (1, 2, 3...) so the file is readable
                var sorted = devices.OrderBy(d => d.Number).ToList();

                foreach (var dev in sorted)
                {
                    // Only save devices that are mapped (Number < 100)
                    if (dev.Number < 100 && dev.Config != null)
                    {
                        writer.WriteLine($"; {dev}"); // Writes "Position 1" comment
                        writer.WriteLine($"Position{dev.Number} = {dev.Config.Left}, {dev.Config.Middle}, {dev.Config.Right}");
                        writer.WriteLine("");
                    }
                }
            }
        }
    }
}