using HidLibrary;
using System;

namespace biped
{
    public class BipedDevice
    {
        public HidDevice Device { get; }
        public string Serial { get; }
        public string Path { get; }
        public Config Config { get; set; }

        // State Flags
        public bool SuppressOutput { get; set; } = false;

        // FIX: Track EACH pedal individually
        public bool LatchedLeft { get; set; } = false;
        public bool LatchedMiddle { get; set; } = false;
        public bool LatchedRight { get; set; } = false;

        // Device Memory
        public byte LastStatus { get; set; } = 0;
        public DateTime LastEvent { get; set; } = DateTime.MinValue;

        private readonly int _number;

        public BipedDevice(HidDevice dev, int id)
        {
            Device = dev;
            Path = dev.DevicePath ?? "";
            Serial = GetSerialFromDevice(dev);
            _number = id;
        }

        private string GetSerialFromDevice(HidDevice dev)
        {
            try
            {
                if (dev.ReadSerialNumber(out byte[] serialBytes))
                {
                    string s = System.Text.Encoding.Unicode.GetString(serialBytes).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { /* ignore */ }
            return "";
        }

        public string UniqueId => !string.IsNullOrEmpty(Serial) ? Serial : Path;

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Serial)) return $"Pedal {Serial}";
            return $"Pedal {_number}";
        }
    }
}