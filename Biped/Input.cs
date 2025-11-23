using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace biped
{
    public class Input
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        const uint KEYEVENTF_SCANCODE = 0x0008;
        const uint KEYEVENTF_KEYUP = 0x0002;

        // Virtual Key Codes for Modifiers
        const ushort VK_SHIFT = 0x10;
        const ushort VK_CONTROL = 0x11;
        const ushort VK_MENU = 0x12; // Alt
        const ushort VK_LWIN = 0x5B;

        // Inside Input.cs

        public void SendKey(uint packedCode, bool keyUp)
        {
            var inputs = new List<INPUT>();

            // 1. Unpack
            uint keyCode = packedCode & ModifierMasks.KEY_MASK;
            bool hasShift = (packedCode & ModifierMasks.SHIFT) != 0;
            bool hasCtrl = (packedCode & ModifierMasks.CTRL) != 0;
            bool hasAlt = (packedCode & ModifierMasks.ALT) != 0;
            bool hasWin = (packedCode & ModifierMasks.WIN) != 0;

            // 2. Modifiers Down
            if (!keyUp)
            {
                if (hasShift) AddKey(inputs, 0x10, false); // VK_SHIFT
                if (hasCtrl) AddKey(inputs, 0x11, false); // VK_CONTROL
                if (hasAlt) AddKey(inputs, 0x12, false); // VK_MENU
                if (hasWin) AddKey(inputs, 0x5B, false); // VK_LWIN
            }

            // 3. Main Key OR Mouse Button
            // FIX: Check for the new high-range Mouse IDs
            if (keyCode >= 0xFF01 && keyCode <= 0xFF03)
            {
                AddMouse(inputs, keyCode, keyUp);
            }
            else if (keyCode > 0)
            {
                AddScanKey(inputs, keyCode, keyUp);
            }

            // 4. Modifiers Up
            if (keyUp)
            {
                if (hasWin) AddKey(inputs, 0x5B, true);
                if (hasAlt) AddKey(inputs, 0x12, true);
                if (hasCtrl) AddKey(inputs, 0x11, true);
                if (hasShift) AddKey(inputs, 0x10, true);
            }

            if (inputs.Count > 0)
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }

        private void AddScanKey(List<INPUT> list, uint scancode, bool keyUp)
        {
            list.Add(new INPUT
            {
                type = 1, // Keyboard
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = (ushort)scancode,
                        dwFlags = KEYEVENTF_SCANCODE | (uint)(keyUp ? KEYEVENTF_KEYUP : 0)
                    }
                }
            });
        }

        // Helper for Modifiers (using Virtual Keys, not Scancodes, for safety)
        private void AddKey(List<INPUT> list, ushort vk, bool keyUp)
        {
            list.Add(new INPUT
            {
                type = 1,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        dwFlags = (uint)(keyUp ? KEYEVENTF_KEYUP : 0)
                    }
                }
            });
        }

        private void AddMouse(List<INPUT> list, uint code, bool keyUp)
        {
            uint flag = 0;
            const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            const uint MOUSEEVENTF_LEFTUP = 0x0004;
            const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
            const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
            const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
            const uint MOUSEEVENTF_RIGHTUP = 0x0010;

            switch (code)
            {
                case CustomButtons.MouseLeft: flag = keyUp ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_LEFTDOWN; break;
                case CustomButtons.MouseMiddle: flag = keyUp ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_MIDDLEDOWN; break;
                case CustomButtons.MouseRight: flag = keyUp ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_RIGHTDOWN; break;
            }

            list.Add(new INPUT
            {
                type = 0,
                u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flag } }
            });
        }

        public void ReleaseAllModifiers()
        {
            // Manually send UP events for all common modifiers to unstick them
            // if the app crashed or exited weirdly previously.
            var inputs = new List<INPUT>();

            AddKey(inputs, 0x10, true); // Shift
            AddKey(inputs, 0x11, true); // Ctrl
            AddKey(inputs, 0x12, true); // Alt
            AddKey(inputs, 0x5B, true); // LWin

            if (inputs.Count > 0)
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }
}