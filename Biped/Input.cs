using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace biped
{
    public class Input
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        // --- EXPLICIT STRUCT LAYOUT (Prevents Memory Bleed) ---

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type; // 0=Mouse, 1=Keyboard
            public InputUnion U;
            public static int Size => Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // --- CONSTANTS ---
        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;

        const uint KEYEVENTF_SCANCODE = 0x0008;
        const uint KEYEVENTF_KEYUP = 0x0002;

        // --- METHODS ---

        public void SendKey(uint packedCode, bool keyUp)
        {
            var inputs = new List<INPUT>();

            // 1. Unpack Modifiers
            uint keyCode = packedCode & ModifierMasks.KEY_MASK;
            bool hasShift = (packedCode & ModifierMasks.SHIFT) != 0;
            bool hasCtrl = (packedCode & ModifierMasks.CTRL) != 0;
            bool hasAlt = (packedCode & ModifierMasks.ALT) != 0;
            bool hasWin = (packedCode & ModifierMasks.WIN) != 0;

            // 2. Modifiers Down (Pressed before the main key)
            if (!keyUp)
            {
                if (hasShift) AddKey(inputs, 0x10, false); // VK_SHIFT
                if (hasCtrl) AddKey(inputs, 0x11, false); // VK_CONTROL
                if (hasAlt) AddKey(inputs, 0x12, false); // VK_MENU
                if (hasWin) AddKey(inputs, 0x5B, false); // VK_LWIN
            }

            // 3. Main Key OR Mouse Button
            if (keyCode >= 0xFF01 && keyCode <= 0xFF03)
            {
                AddMouse(inputs, keyCode, keyUp);
            }
            else if (keyCode > 0)
            {
                // Prefer ScanCodes for games, but use VK logic inside helper
                AddScanKey(inputs, keyCode, keyUp);
            }

            // 4. Modifiers Up (Released after the main key)
            if (keyUp)
            {
                if (hasWin) AddKey(inputs, 0x5B, true);
                if (hasAlt) AddKey(inputs, 0x12, true);
                if (hasCtrl) AddKey(inputs, 0x11, true);
                if (hasShift) AddKey(inputs, 0x10, true);
            }

            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
            }
        }

        private void AddScanKey(List<INPUT> list, uint scancode, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wScan = (ushort)scancode,
                        wVk = 0, // Must be 0 when using ScanCode flag
                        dwFlags = KEYEVENTF_SCANCODE | (uint)(keyUp ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = GetMessageExtraInfo()
                    }
                }
            };
            list.Add(input);
        }

        private void AddKey(List<INPUT> list, ushort vk, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = (uint)(keyUp ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = GetMessageExtraInfo()
                    }
                }
            };
            list.Add(input);
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

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = flag,
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        time = 0,
                        dwExtraInfo = GetMessageExtraInfo()
                    }
                }
            };
            list.Add(input);
        }

        public void ReleaseAllModifiers()
        {
            var inputs = new List<INPUT>();
            AddKey(inputs, 0x10, true); // Shift
            AddKey(inputs, 0x11, true); // Ctrl
            AddKey(inputs, 0x12, true); // Alt
            AddKey(inputs, 0x5B, true); // LWin
            if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        }
    }
}