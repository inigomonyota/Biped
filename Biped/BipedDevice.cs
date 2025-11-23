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
                if (!string.IsNullOrEmpty(Serial)) return $"SN: {Serial}";

                // Extract the unique part of the USB path
                // Path format: \\?\HID#VID_05F3&PID_00FF# 7&2a6b2d4a&0&0000 #{GUID}
                if (!string.IsNullOrEmpty(Path))
                {
                    var parts = Path.Split('#');
                    if (parts.Length >= 3) return parts[2];
                }
                return "Unknown ID";
            }
        }

        // MODIFIED: Return ONLY the clean name
        public override string ToString()
        {
            if (Number < 100) return $"Position {Number}";
            return "Unmapped Device";
        }
    }
}