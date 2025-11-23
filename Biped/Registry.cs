using Microsoft.Win32;

namespace biped
{
    public class SettingsStorage
    {
        private const string RegKeyPath = @"Software\Biped";

        public void Save(string pedalName, uint value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegKeyPath))
                {
                    key?.SetValue(pedalName, value, RegistryValueKind.DWord);
                }
            }
            catch { /* silently ignore - not critical */ }
        }

        public uint Load(string pedalName, uint defaultValue = 0)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKeyPath))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(pedalName);
                        if (val is int i) return (uint)i;
                        if (val != null) return ConvertToUInt32(val);
                    }
                }
            }
            catch { /* ignore */ }

            return defaultValue;
        }

        // Helper for safety
        private static uint ConvertToUInt32(object value)
        {
            try { return System.Convert.ToUInt32(value); }
            catch { return 0; }
        }

        // Works perfectly on .NET Framework 4.8
        public void ClearAll()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKey(RegKeyPath); // throws if not exist → we catch
            }
            catch { /* key didn't exist - that's fine */ }
        }
    }
}