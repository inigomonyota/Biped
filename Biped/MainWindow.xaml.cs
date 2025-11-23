using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Interop;

// Aliases to handle the Tray Icon and Graphics
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace biped
{
    public partial class MainWindow : Window
    {
        private readonly SettingsStorage settings = new SettingsStorage();
        private MultiBiped multiBiped;
        private BipedDevice currentDevice;
        private Config config;

        private Pedal currentPedal = Pedal.NONE;
        private DateTime bindingStartTime = DateTime.MinValue;

        // Tray Icon Components
        private WinForms.NotifyIcon trayIcon;
        private bool isExiting = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTrayIcon();

            // MOVED: We no longer wait for "Loaded". We start immediately.
            InitializeHardware();

            // Handle Closing to minimize instead of exit
            Closing += (s, e) =>
            {
                if (!isExiting)
                {
                    e.Cancel = true;
                    if (currentPedal != Pedal.NONE) CancelBinding();
                    Hide();
                }
            };

            this.PreviewMouseDown += OnGlobalMouseDown;

        }

        private void InitializeTrayIcon()
        {
            trayIcon = new WinForms.NotifyIcon();

            // Try to use the EXE's own icon. If that fails, it will just be blank (but functional)
            try
            {
                trayIcon.Icon = Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location);
            }
            catch { /* ignore */ }

            trayIcon.Text = "Biped Pedal Mapper";
            trayIcon.Visible = true;

            // Double-click the tray icon to bring window back
            trayIcon.DoubleClick += (s, e) => ShowWindow();

            // Right-click Context Menu
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Configure Pedals", null, (s, e) => ShowWindow());
            menu.Items.Add("-"); // Separator
            menu.Items.Add("Exit Biped", null, (s, e) => ExitApp());

            trayIcon.ContextMenuStrip = menu;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private const int WM_DEVICECHANGE = 0x0219;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // If Windows says "Hardware changed" (plug or unplug)
            if (msg == WM_DEVICECHANGE)
            {
                // Optional: Check if we are currently binding. 
                // If we are, we might want to cancel it to prevent errors if the device was unplugged.
                if (currentPedal != Pedal.NONE)
                {
                    CancelBinding();
                }

                // Force a rescan
                // We use a small DispatcherTimer or Delay to allow the OS 
                // a moment to finalize the driver loading before we try to open it.
                Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(500); // Wait 0.5s for drivers to settle
                    RefreshDeviceList();
                });
            }

            return IntPtr.Zero;
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApp()
        {
            isExiting = true;

            // SAFETY: Release any modifiers (Shift/Ctrl/Alt) that might be stuck
            new Input().ReleaseAllModifiers();

            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void InitializeHardware()
        {
            // Safety: Unstick any keys
            new Input().ReleaseAllModifiers();

            multiBiped = new MultiBiped();
            multiBiped.PedalPressed += OnPhysicalPedalPress;

            RefreshDeviceList();

            if (multiBiped.Devices.Count == 0)
                StatusText.Text = "No pedal detected – plug it in!";
            else
                SelectDevice(multiBiped.Devices[0]);
        }

        private void OnPhysicalPedalPress(BipedDevice dev, Pedal p)
        {
            Dispatcher.Invoke(() =>
            {
                // 1. If the window is hidden (minimized to tray), ignore everything
                if (Visibility != Visibility.Visible) return;

                // 2. STRICT CHECK:
                // If the pedal that was just pressed (dev) is NOT the one currently 
                // selected in the dropdown (currentDevice), IGNORE IT completely.
                if (currentDevice == null || dev != currentDevice)
                {
                    return;
                }

                // 3. If we are already busy binding a key, don't restart the process
                // (This prevents rapid-fire triggering if you hold the pedal down)
                if (currentPedal != Pedal.NONE)
                {
                    return;
                }

                // 4. If we passed checks, start binding for the SELECTED device only.
                StartBinding(p);
            });
        }
        private void SetUiLocked(bool locked)
        {
            // If locked is true, disable the controls. 
            // If locked is false, enable them.
            DeviceSelector.IsEnabled = !locked;
            ResetButton.IsEnabled = !locked;

            // Optional: Visually dim the controls slightly if you want, 
            // but IsEnabled usually handles that automatically in WPF.
        }
        private void StartBinding(Pedal pedal)
        {
            currentPedal = pedal;
            bindingStartTime = DateTime.Now;

            SetUiLocked(true);

            foreach (var d in multiBiped.Devices)
            {
                d.SuppressOutput = true;
            }

            StatusText.Text = "Press any key to bind (or Del to unbind)";

            HighlightPedal(pedal);
        }
        private void HighlightPedal(Pedal p)
        {
            LeftText.Background = Brushes.Transparent;
            MiddleText.Background = Brushes.Transparent;
            RightText.Background = Brushes.Transparent;

            switch (p)
            {
                case Pedal.LEFT: LeftText.Background = Brushes.LightSkyBlue; break;
                case Pedal.MIDDLE: MiddleText.Background = Brushes.LightSkyBlue; break;
                case Pedal.RIGHT: RightText.Background = Brushes.LightSkyBlue; break;
            }
        }

        private void OnGlobalMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (currentPedal == Pedal.NONE) return;
            if ((DateTime.Now - bindingStartTime).TotalMilliseconds < 300) return;

            uint code = 0;
            switch (e.ChangedButton)
            {
                case MouseButton.Left: code = CustomButtons.MouseLeft; break;
                case MouseButton.Middle: code = CustomButtons.MouseMiddle; break;
                case MouseButton.Right: code = CustomButtons.MouseRight; break;
            }

            if (code != 0)
            {
                // FIX: Capture modifiers too! (Allows Shift+Click, and handles stuck Shift properly)
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) code |= ModifierMasks.CTRL;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) code |= ModifierMasks.SHIFT;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) code |= ModifierMasks.ALT;
                if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) code |= ModifierMasks.WIN;

                Save(currentPedal, code);
                e.Handled = true;
            }
        }
        private bool IsModifier(Key k)
        {
            return k == Key.LeftCtrl || k == Key.RightCtrl ||
                   k == Key.LeftAlt || k == Key.RightAlt ||
                   k == Key.LeftShift || k == Key.RightShift ||
                   k == Key.LWin || k == Key.RWin ||
                   k == Key.System; // System handles Alt when pressed alone
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape && currentPedal != Pedal.NONE) { CancelBinding(); e.Handled = true; return; }
            if (currentPedal == Pedal.NONE) { base.OnPreviewKeyDown(e); return; }
            if ((DateTime.Now - bindingStartTime).TotalMilliseconds < 300) return;
            if (e.IsRepeat) { e.Handled = true; return; }
            if (e.Key == Key.Delete || e.Key == Key.Back) { Save(currentPedal, 0); e.Handled = true; return; }
            if (e.Key == Key.None) return;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // IF IT IS A MODIFIER: Do nothing yet. Wait for a normal key or a release.
            if (IsModifier(key))
            {
                return;
            }

            // IF IT IS A NORMAL KEY (e.g., 'F'): Bind immediately with current modifiers.
            uint code = GetCombinedCode(key);
            Save(currentPedal, code);
            e.Handled = true;
        }
        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (currentPedal == Pedal.NONE) { base.OnPreviewKeyUp(e); return; }
            if ((DateTime.Now - bindingStartTime).TotalMilliseconds < 300) return;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // IF A MODIFIER IS RELEASED (and we are still binding):
            // It means the user pressed "Ctrl", didn't press anything else, and let go.
            // So bind "Ctrl".
            if (IsModifier(key))
            {
                // We bind the specific modifier key that was released
                uint code = GetCombinedCode(key);
                Save(currentPedal, code);
                e.Handled = true;
            }
        }

        // 4. REUSABLE CODE EXTRACTION
        private uint GetCombinedCode(Key key)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            uint code = MapVirtualKey((uint)vk, 0);
            if (code == 0) code = (uint)vk;

            // If the key ITSELF is not a modifier, add the modifier flags.
            // (If the key IS LeftCtrl, we don't need to add the Ctrl flag, just the keycode is enough).
            if (!IsModifier(key))
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) code |= ModifierMasks.CTRL;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) code |= ModifierMasks.SHIFT;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) code |= ModifierMasks.ALT;
                if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) code |= ModifierMasks.WIN;
            }

            return code;
        }

        private void CancelBinding()
        {
            currentPedal = Pedal.NONE;

            SetUiLocked(false);

            foreach (var d in multiBiped.Devices) d.SuppressOutput = false;

            StatusText.Text = "Binding cancelled. Press a pedal to edit.";

            HighlightPedal(Pedal.NONE);
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentDevice == null) return;

            if (MessageBox.Show("This will unbind all 3 switches on the current pedal.\nContinue?",
                                "Confirm Reset",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // 1. Wipe the settings
                SaveInternal(Pedal.LEFT, 0);
                SaveInternal(Pedal.MIDDLE, 0);
                SaveInternal(Pedal.RIGHT, 0);

                // 2. Show the success message
                string resetMsg = "Device reset! Switches are now unbound.";
                StatusText.Text = resetMsg;

                // 3. Wait 3 seconds asynchronously (UI stays responsive)
                await System.Threading.Tasks.Task.Delay(3000);

                // 4. Revert text ONLY if it hasn't changed since we set it.
                // (This prevents us from overwriting "Editing..." if the user started 
                //  binding something else while the timer was running).
                if (StatusText.Text == resetMsg)
                {
                    StatusText.Text = "Press a foot pedal to configure it!";
                }
            }
        }

        private void SaveInternal(Pedal pedal, uint code)
        {
            string prefix = currentDevice.UniqueId + "_";
            settings.Save(prefix + pedal.ToString(), code);

            if (pedal == Pedal.LEFT) config.Left = code;
            else if (pedal == Pedal.MIDDLE) config.Middle = code;
            else if (pedal == Pedal.RIGHT) config.Right = code;

            currentDevice.Config = config;
            UpdateLabels();
        }
        private void Save(Pedal pedal, uint code)
        {
            SaveInternal(pedal, code);
            SetUiLocked(false);
            foreach (var d in multiBiped.Devices) d.SuppressOutput = false;

            // SIMPLIFIED SAVED TEXT:
            StatusText.Text = "Binding saved! Press a pedal to edit.";

            HighlightPedal(Pedal.NONE);
            currentPedal = Pedal.NONE;
        }
        private void RefreshDeviceList()
{
    // Store the Unique ID of the currently selected device (if any)
    string previouslySelectedId = currentDevice?.UniqueId;

    // 1. Tell the backend to close old devices and find new ones
    multiBiped.RefreshDevices();

    // 2. Update the Dropdown
    DeviceSelector.Items.Clear();
    foreach (var dev in multiBiped.Devices)
    {
        DeviceSelector.Items.Add(dev);
    }

    // 3. Try to re-select the device we were looking at
    if (DeviceSelector.Items.Count > 0)
    {
        BipedDevice newSelection = null;

        // Try to find the old device by ID in the NEW list
        if (!string.IsNullOrEmpty(previouslySelectedId))
        {
            foreach(BipedDevice dev in DeviceSelector.Items)
            {
                if (dev.UniqueId == previouslySelectedId)
                {
                    newSelection = dev;
                    break;
                }
            }
        }

        // If we found it, select it. If not (e.g. it was unplugged), select the first one.
        if (newSelection != null)
        {
            DeviceSelector.SelectedItem = newSelection;
        }
        else
        {
            DeviceSelector.SelectedIndex = 0;
        }
    }
    else
    {
        // List is empty (all devices unplugged)
        currentDevice = null;
        StatusText.Text = "No pedal detected – plug it in!";
        // Clear labels visually
        LeftText.Text = "";
        MiddleText.Text = "";
        RightText.Text = "";
    }
}

        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DeviceSelector.SelectedItem is BipedDevice dev)
                SelectDevice(dev);
        }

        private void SelectDevice(BipedDevice dev)
        {
            currentDevice = dev;
            string prefix = dev.UniqueId + "_";

            uint l = settings.Load(prefix + "LEFT", 29);
            uint m = settings.Load(prefix + "MIDDLE", 56);
            uint r = settings.Load(prefix + "RIGHT", 57);

            config = new Config(l, m, r);
            dev.Config = config;

            UpdateLabels();
            StatusText.Text = "Press a foot pedal to configure it!";
        }

        private void UpdateLabels()
        {
            LeftText.Text = GetDisplay(config.Left);
            MiddleText.Text = GetDisplay(config.Middle);
            RightText.Text = GetDisplay(config.Right);
        }

        // Empty handlers to prevent UI clicking
        private void LeftText_PreviewMouseUp(object sender, MouseButtonEventArgs e) { }
        private void MiddleText_PreviewMouseUp(object sender, MouseButtonEventArgs e) { }
        private void RightText_PreviewMouseUp(object sender, MouseButtonEventArgs e) { }

        public void ApplyCommandLineBindings(uint left, uint middle, uint right)
        {
            if (currentDevice == null && multiBiped.Devices.Count > 0)
                SelectDevice(multiBiped.Devices[0]);
            if (currentDevice == null) return;

            string prefix = currentDevice.UniqueId + "_";
            settings.Save(prefix + "LEFT", left);
            settings.Save(prefix + "MIDDLE", middle);
            settings.Save(prefix + "RIGHT", right);

            config = new Config(left, middle, right);
            currentDevice.Config = config;
            UpdateLabels();
        }

        private string ToFriendlyName(Pedal p)
        {
            switch (p)
            {
                case Pedal.LEFT: return "Left";
                case Pedal.MIDDLE: return "Middle";
                case Pedal.RIGHT: return "Right";
                default: return p.ToString();
            }
        }

        private string GetDisplay(uint packedCode)
        {
            // 1. Handle "None"
            if (packedCode == 0) return "None";

            // 2. Build the Modifier String FIRST
            string prefix = "";
            if ((packedCode & ModifierMasks.CTRL) != 0) prefix += "Ctrl + ";
            if ((packedCode & ModifierMasks.SHIFT) != 0) prefix += "Shift + ";
            if ((packedCode & ModifierMasks.ALT) != 0) prefix += "Alt + ";
            if ((packedCode & ModifierMasks.WIN) != 0) prefix += "Win + ";

            // 3. Check Mouse Buttons (High Range)
            // We do this after building the prefix so we get "Shift + Mouse Left"
            uint baseCode = packedCode & ModifierMasks.KEY_MASK;

            if (baseCode == CustomButtons.MouseLeft) return prefix + "Mouse Left";
            if (baseCode == CustomButtons.MouseMiddle) return prefix + "Mouse Middle";
            if (baseCode == CustomButtons.MouseRight) return prefix + "Mouse Right";

            // 4. Map Scancode to WPF Key Enum
            uint vk = MapVirtualKey(baseCode, 1);
            if (vk == 0) return prefix + baseCode.ToString(); // Fallback

            Key key = KeyInterop.KeyFromVirtualKey((int)vk);
            string keyName;

            // 5. Get a Friendly Name
            switch (key)
            {
                // --- Numbers ---
                case Key.D0: keyName = "0"; break;
                case Key.D1: keyName = "1"; break;
                case Key.D2: keyName = "2"; break;
                case Key.D3: keyName = "3"; break;
                case Key.D4: keyName = "4"; break;
                case Key.D5: keyName = "5"; break;
                case Key.D6: keyName = "6"; break;
                case Key.D7: keyName = "7"; break;
                case Key.D8: keyName = "8"; break;
                case Key.D9: keyName = "9"; break;

                // --- Numpad ---
                case Key.NumPad0: keyName = "Num 0"; break;
                case Key.NumPad1: keyName = "Num 1"; break;
                case Key.NumPad2: keyName = "Num 2"; break;
                case Key.NumPad3: keyName = "Num 3"; break;
                case Key.NumPad4: keyName = "Num 4"; break;
                case Key.NumPad5: keyName = "Num 5"; break;
                case Key.NumPad6: keyName = "Num 6"; break;
                case Key.NumPad7: keyName = "Num 7"; break;
                case Key.NumPad8: keyName = "Num 8"; break;
                case Key.NumPad9: keyName = "Num 9"; break;
                case Key.Decimal: keyName = "Num ."; break;
                case Key.Add: keyName = "Num +"; break;
                case Key.Subtract: keyName = "Num -"; break;
                case Key.Multiply: keyName = "Num *"; break;
                case Key.Divide: keyName = "Num /"; break;

                // --- Symbols (Standard US Layout) ---
                case Key.Oem1: keyName = ";"; break;
                case Key.OemPlus: keyName = "="; break;
                case Key.OemComma: keyName = ","; break;
                case Key.OemMinus: keyName = "-"; break;
                case Key.OemPeriod: keyName = "."; break;
                case Key.OemQuestion: keyName = "/"; break;
                case Key.OemTilde: keyName = "`"; break;
                case Key.OemOpenBrackets: keyName = "["; break;
                case Key.OemPipe: keyName = "\\"; break;
                case Key.OemCloseBrackets: keyName = "]"; break;
                case Key.OemQuotes: keyName = "'"; break;
                case Key.OemBackslash: keyName = "\\"; break;

                // --- Navigation / Editing ---
                case Key.Return: keyName = "Enter"; break;
                case Key.Back: keyName = "Backspace"; break;
                case Key.Space: keyName = "Space"; break;
                case Key.Tab: keyName = "Tab"; break;
                case Key.Escape: keyName = "Esc"; break;
                case Key.Delete: keyName = "Del"; break;
                case Key.Insert: keyName = "Ins"; break;
                case Key.Home: keyName = "Home"; break;
                case Key.End: keyName = "End"; break;
                case Key.PageUp: keyName = "PgUp"; break;
                case Key.PageDown: keyName = "PgDn"; break;
                case Key.Snapshot: keyName = "Print Screen"; break;
                case Key.Scroll: keyName = "Scroll Lock"; break;
                case Key.Pause: keyName = "Pause"; break;

                // --- Modifiers (for display cleanup) ---
                case Key.LeftCtrl: keyName = "Left Ctrl"; break;
                case Key.RightCtrl: keyName = "Right Ctrl"; break;
                case Key.LeftShift: keyName = "Left Shift"; break;
                case Key.RightShift: keyName = "Right Shift"; break;
                case Key.LeftAlt: keyName = "Left Alt"; break;
                case Key.RightAlt: keyName = "Right Alt"; break;
                case Key.LWin: keyName = "Left Win"; break;
                case Key.RWin: keyName = "Right Win"; break;

                // --- Misc ---
                case Key.OemFinish: keyName = "No Convert"; break;

                // Default fallthrough (F1-F12, Letters A-Z, etc.)
                default: keyName = key.ToString(); break;
            }

            return prefix + keyName;
        }
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    }
}