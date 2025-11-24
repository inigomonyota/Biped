using HidLibrary;
using System;
using System.IO;

namespace biped
{
    public class BipedDevice
    {
        public HidDevice Device { get; }
        public string Serial { get; }
        public string Path { get; }
        public Config Config { get; set; }

        public bool SuppressOutput { get; set; } = false;
        public bool LatchedLeft { get; set; } = false;
        public bool LatchedMiddle { get; set; } = false;
        public bool LatchedRight { get; set; } = false;

        public byte LastStatus { get; set; } = 0;
        public DateTime LastEvent { get; set; } = DateTime.MinValue;

        public int Number { get; }

        public BipedDevice(HidDevice dev, int position)
        {
            Device = dev;
            Path = dev.DevicePath ?? "";
            Serial = GetSerialFromDevice(dev);
            Number = position;
        }

        public static string GetIdFromHid(HidDevice dev)
        {
            string path = dev.DevicePath ?? "";
            try
            {
                if (dev.ReadSerialNumber(out byte[] serialBytes))
                {
                    string s = System.Text.Encoding.Unicode.GetString(serialBytes).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return path;
        }

        private string GetSerialFromDevice(HidDevice dev)
        {
            string id = GetIdFromHid(dev);
            return id == Path ? "" : id;
        }

        public string UniqueId => !string.IsNullOrEmpty(Serial) ? Serial : Path;

        // NEW: Public Property for the UI to read
        public string ShortId
        {
            get
            {
                // 1. Prefer Serial if available
                if (!string.IsNullOrEmpty(Serial)) return $"SN: {Serial}";

                // 2. Get Hardware Info directly from the device firmware
                // This is more reliable than parsing the path string.
                string vid = Device.Attributes.VendorId.ToString("X4"); // Hex format (e.g. 05F3)
                string pid = Device.Attributes.ProductId.ToString("X4"); // Hex format (e.g. 00FF)
                string rev = Device.Attributes.Version.ToString("X4");   // Hex format (e.g. 0120)

                // 3. Get the Unique Port ID from the Path
                string uniquePart = "Unknown";

                if (!string.IsNullOrEmpty(Path))
                {
                    string clean = Path;

                    // Remove System Prefix
                    if (clean.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                        clean = clean.Substring(4);

                    // Remove Trailing GUID
                    int guidIndex = clean.LastIndexOf("#{");
                    if (guidIndex > 0)
                        clean = clean.Substring(0, guidIndex);

                    // Split to find the Instance ID
                    // Path: HID # VID_... # InstanceID
                    string[] parts = clean.Split('#');
                    if (parts.Length >= 3)
                    {
                        uniquePart = parts[2];
                    }
                }

                // 4. Combine them
                return $"VID: {vid}  PID: {pid}  REV: {rev}  ID: {uniquePart}";
            }
        }

        // MODIFIED: Return ONLY the clean name
        public override string ToString()
        {
            if (Number < 100) return $"Pedal {Number}";
            return "Unmapped Device";
        }
    }
}