using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace biped
{
    public partial class MainWindow : Window
    {
        // --- PATHS ---
        private string ConfigDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cfg");
        private string DefaultProfilePath => Path.Combine(ConfigDirectory, "default.cfg");
        private string currentProfilePath;

        // --- STATE ---
        private MultiBiped multiBiped;
        private BipedDevice currentDevice;
        private Pedal currentPedal = Pedal.NONE;
        private DateTime bindingStartTime = DateTime.MinValue;

        // --- TRAY ---
        private WinForms.NotifyIcon trayIcon;
        private bool isExiting = false;

        private readonly System.Timers.Timer _statusTimer = new System.Timers.Timer(2500) { AutoReset = false };

        public MainWindow()
        {
            InitializeComponent();
            InitializeTrayIcon();
            InitializeHardware();

            // Minimize to tray
            Closing += (s, e) =>
            {
                if (!isExiting)
                {
                    e.Cancel = true;
                    if (currentPedal != Pedal.NONE) CancelBinding();
                    Hide();
                }
            };

            // Cancel binding when window loses focus
            Deactivated += (s, e) =>
            {
                if (currentPedal != Pedal.NONE)
                    CancelBinding();
            };

            // Subtle hover effect
            MapButton.MouseEnter += (s, e) => MapButton.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            MapButton.MouseLeave += (s, e) => MapButton.Background = Brushes.White;

            this.PreviewMouseDown += OnGlobalMouseDown;

            _statusTimer.Elapsed += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (StatusText.Text != "Ready.") // only reset if we changed it
                        StatusText.Text = "Ready.";
                });
            };
        }

        // ---------------------------------------------------------
        // INITIALIZATION
        // ---------------------------------------------------------
        private void InitializeHardware(string preferredIdToSelect = null)
        {
            string idToRestore = preferredIdToSelect ?? (currentDevice != null ? currentDevice.UniqueId : null);

            new Input().ReleaseAllModifiers();
            if (!Directory.Exists(ConfigDirectory)) Directory.CreateDirectory(ConfigDirectory);

            multiBiped = new MultiBiped();
            multiBiped.PedalPressed += OnPhysicalPedalPress;

            DeviceSelector.Items.Clear();
            foreach (var dev in multiBiped.Devices)
                DeviceSelector.Items.Add(dev);

            BipedDevice deviceToSelect = null;
            if (!string.IsNullOrEmpty(idToRestore))
            {
                foreach (BipedDevice d in multiBiped.Devices)
                {
                    if (string.Equals(d.UniqueId, idToRestore, StringComparison.OrdinalIgnoreCase))
                    {
                        deviceToSelect = d;
                        break;
                    }
                }
            }

            if (deviceToSelect == null && multiBiped.Devices.Count > 0)
                deviceToSelect = multiBiped.Devices[0];

            if (deviceToSelect != null)
            {
                DeviceSelector.SelectedItem = deviceToSelect;
                currentDevice = deviceToSelect;
            }
            else
            {
                SetStatus("No pedal detected – plug it in!");
                currentDevice = null;
            }

            currentProfilePath = DefaultProfilePath;
            if (File.Exists(DefaultProfilePath))
                ApplyGameProfile(DefaultProfilePath);
            else
            {
                ProfileLoader.Save(DefaultProfilePath, new List<BipedDevice>());
                SetStatus("Active Profile: default (New)");
            }

            RefreshProfileList();
            SelectDevice(currentDevice);
        }

        // ---------------------------------------------------------
        // PROFILE MANAGEMENT
        // ---------------------------------------------------------
        private void RefreshProfileList()
        {
            ProfileSelector.SelectionChanged -= ProfileSelector_SelectionChanged;
            ProfileSelector.Items.Clear();
            ProfileSelector.Items.Add("default");

            if (Directory.Exists(ConfigDirectory))
            {
                string[] files = Directory.GetFiles(ConfigDirectory, "*.cfg");
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!name.Equals("default", StringComparison.OrdinalIgnoreCase))
                        ProfileSelector.Items.Add(name);
                }
            }

            string currentName = Path.GetFileNameWithoutExtension(currentProfilePath);
            ProfileSelector.SelectedItem = currentName;
            if (ProfileSelector.SelectedIndex == -1) ProfileSelector.SelectedIndex = 0;

            ProfileSelector.SelectionChanged += ProfileSelector_SelectionChanged;
        }

        private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selected = ProfileSelector.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            string path = Path.Combine(ConfigDirectory, selected + ".cfg");
            if (path != currentProfilePath)
                ApplyGameProfile(path);
        }

        public void ApplyGameProfile(string filePath)
        {
            currentProfilePath = filePath;
            var profile = ProfileLoader.Load(filePath);
            var input = new Input();

            foreach (var d in multiBiped.Devices)
            {
                if (d.Config != null)
                {
                    if (d.LatchedLeft) input.SendKey(d.Config.Left, true);
                    if (d.LatchedMiddle) input.SendKey(d.Config.Middle, true);
                    if (d.LatchedRight) input.SendKey(d.Config.Right, true);
                }
                d.LatchedLeft = d.LatchedMiddle = d.LatchedRight = false;
                d.Config = new Config(0, 0, 0);
            }

            foreach (var dev in multiBiped.Devices)
            {
                Config cfg;
                if (profile.PedalBindings.TryGetValue(dev.Number, out cfg))
                    dev.Config = cfg;
            }

            if (currentDevice != null) SelectDevice(currentDevice);

            string name = Path.GetFileNameWithoutExtension(filePath);
            ProfileSelector.SelectionChanged -= ProfileSelector_SelectionChanged;
            ProfileSelector.SelectedItem = name;
            ProfileSelector.SelectionChanged += ProfileSelector_SelectionChanged;

            UpdateLabels();
            UpdateClearButtonState();
            RefreshMapMenuState();
            SetStatus("Loaded: " + name);
        }

        private void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Config Files (*.cfg)|*.cfg",
                DefaultExt = ".cfg",
                InitialDirectory = ConfigDirectory,
                Title = "Create New Blank Profile"
            };

            if (dialog.ShowDialog() == true)
            {
                ProfileLoader.Save(dialog.FileName, new List<BipedDevice>());
                RefreshProfileList();
                ApplyGameProfile(dialog.FileName);
            }
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            string name = Path.GetFileNameWithoutExtension(currentProfilePath);
            if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("You cannot delete the default profile.", "Restricted", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Are you sure you want to delete profile '{name}'?\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(currentProfilePath);
                    currentProfilePath = DefaultProfilePath;
                    ApplyGameProfile(DefaultProfilePath);
                    RefreshProfileList();
                    SetStatus("Profile deleted. Switched to default.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error deleting file: " + ex.Message);
                }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("explorer.exe", ConfigDirectory);
        }

        // ---------------------------------------------------------
        // HARDWARE MAPPING
        // ---------------------------------------------------------
        private void RefreshMapMenuState()
        {
            if (currentDevice == null)
            {
                // Disable everything if no device
                foreach (MenuItem item in MapContextMenu.Items.OfType<MenuItem>()) item.IsEnabled = false;
                UnmapMenuItem.IsEnabled = false;
                return;
            }

            bool isMapped = currentDevice.Number < 100;

            int currentPos = currentDevice.Number;

            // Only enable Unmap if we are actually mapped
            UnmapMenuItem.IsEnabled = isMapped;

            foreach (MenuItem item in MapContextMenu.Items.OfType<MenuItem>())
            {
                // Skip items that aren't position toggles (like the Unmap button itself)
                string tag = item.Tag as string;
                int pos;
                if (!int.TryParse(tag, out pos)) continue;

                // Check if position is taken by another device
                bool taken = false;
                foreach (BipedDevice d in multiBiped.Devices)
                {
                    // Check if occupied by someone OTHER than me
                    if (d.Number == pos && d != currentDevice)
                    {
                        taken = true;
                        break;
                    }
                }

                bool takenByMe = isMapped && currentPos == pos;

                // Disable if taken by someone else
                bool disabled = taken && !takenByMe;

                item.IsEnabled = !disabled;
                item.Foreground = disabled ? Brushes.Gray : Brushes.Black;

                if (takenByMe)
                    item.Header = $"Pedal {pos} (Current)";
                else if (disabled)
                    item.Header = $"Pedal {pos} (Occupied)";
                else
                    item.Header = $"Map to Pedal {pos}";
            }
        }
        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BipedDevice dev = DeviceSelector.SelectedItem as BipedDevice;
            if (dev != null)
                SelectDevice(dev);
        }

        private void SelectDevice(BipedDevice dev)
        {
            currentDevice = dev;
            if (dev != null && dev.Config == null) dev.Config = new Config(0, 0, 0);

            if (dev != null)
            {
                DeviceDetailText.Text = $"{dev.ShortId}";
                DeviceDetailText.ToolTip = dev.Path;
            }
            else
            {
                DeviceDetailText.Text = "...";
                DeviceDetailText.ToolTip = null;
            }

            // REMOVED: The logic that changed MapButton.Content to "Unmap" or "Map"
            // The button now stays static.

            UpdateLabels();
            UpdateClearButtonState();
            RefreshMapMenuState(); // This handles the enabling/disabling of menu items
        }

        private void MapMenu_Click(object sender, RoutedEventArgs e)
        {
            if (currentDevice == null) return;

            MenuItem item = sender as MenuItem;
            string id = currentDevice.UniqueId;

            if (item.Tag != null && item.Tag.ToString() == "unmap")
            {
                HardwareMap.UnmapDevice(id);
                SetStatus("Device unmapped.");
            }
            else
            {
                int pos;
                if (int.TryParse(item.Tag.ToString(), out pos))
                {
                    HardwareMap.AssignDeviceToPedal(pos, id);
                    SetStatus($"Device mapped to Pedal {pos}!");
                }
            }

            InitializeHardware(id);
            RefreshMapMenuState();
        }

        private void ClearBindings_Click(object sender, RoutedEventArgs e)
        {
            if (currentDevice == null || currentDevice.Number >= 100)
            {
                SetStatus("No mapped device selected.");
                return;
            }

            string profile = Path.GetFileNameWithoutExtension(currentProfilePath);
            if (MessageBox.Show(
                $"Are you sure you want to clear all bindings for\n({currentDevice})\nin profile \"{profile}\"?",
                "Clear All Bindings", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                SetStatus("Clear cancelled.");
                return;
            }

            currentDevice.Config = new Config(0, 0, 0);
            UpdateLabels();
            ProfileLoader.Save(currentProfilePath, multiBiped.Devices);
            SetStatus($"Cleared all bindings for {currentDevice}");
        }

        private void UpdateLabels()
        {
            if (currentDevice == null || currentDevice.Config == null)
            {
                LeftText.Text = "None";
                MiddleText.Text = "None";
                RightText.Text = "None";
                return;
            }

            LeftText.Text = GetDisplay(currentDevice.Config.Left);
            MiddleText.Text = GetDisplay(currentDevice.Config.Middle);
            RightText.Text = GetDisplay(currentDevice.Config.Right);
        }

        private void UpdateClearButtonState()
        {
            ClearBindingsButton.IsEnabled = currentDevice != null && currentDevice.Number < 100;
        }

        // ---------------------------------------------------------
        // BINDING LOGIC
        // ---------------------------------------------------------
        private void OnPhysicalPedalPress(BipedDevice dev, Pedal p)
        {
            Dispatcher.Invoke(() =>
            {
                if (Visibility != Visibility.Visible || !IsActive || currentDevice == null || dev != currentDevice || currentPedal != Pedal.NONE)
                    return;

                StartBinding(p);
            });
        }

        private void StartBinding(Pedal pedal)
        {
            if (currentDevice.Number >= 100)
            {
                MessageBox.Show(
                    "This device is not mapped to a pedal number yet.\n\nPlease use the 'Actions' button to assign it to a pedal number (e.g., Pedal 1 for Left, Pedal 2 2 for Right) before binding keys.",
                    "Device Unmapped", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            currentPedal = pedal;
            bindingStartTime = DateTime.Now;
            SetUiLocked(true);
            foreach (var d in multiBiped.Devices) d.SuppressOutput = true;

            SetStatus($"Editing Pedal {currentDevice.Number} - {ToFriendlyName(pedal)}...");
            HighlightPedal(pedal);
        }

        private void Save(Pedal pedal, uint code)
        {
            if (pedal == Pedal.LEFT) currentDevice.Config.Left = code;
            else if (pedal == Pedal.MIDDLE) currentDevice.Config.Middle = code;
            else currentDevice.Config.Right = code;

            UpdateLabels();
            ProfileLoader.Save(currentProfilePath, multiBiped.Devices);

            SetUiLocked(false);
            foreach (var d in multiBiped.Devices) d.SuppressOutput = false;

            SetStatus($"Saved to {ToFriendlyName(pedal)}!");
            HighlightPedal(Pedal.NONE);
            currentPedal = Pedal.NONE;
        }

        private void CancelBinding()
        {
            currentPedal = Pedal.NONE;
            SetUiLocked(false);
            foreach (var d in multiBiped.Devices) d.SuppressOutput = false;
            SetStatus("Binding cancelled.");
            HighlightPedal(Pedal.NONE);
        }

        // ---------------------------------------------------------
        // INPUT CAPTURE HANDLERS
        // ---------------------------------------------------------
        private void OnGlobalMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (currentPedal == Pedal.NONE) return;

            // Swallow event
            e.Handled = true;

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
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) code |= ModifierMasks.CTRL;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) code |= ModifierMasks.SHIFT;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) code |= ModifierMasks.ALT;
                if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) code |= ModifierMasks.WIN;

                Save(currentPedal, code);
            }
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // 1. If not binding, behave normally
            if (currentPedal == Pedal.NONE)
            {
                base.OnPreviewKeyDown(e);
                return;
            }

            // 2. CRITICAL: We are in binding mode. We own this event.
            // Swallow it so it doesn't interact with the UI (Tab, Alt, Space, etc.)
            e.Handled = true;

            // 3. Handle Escape (Cancel)
            if (e.Key == Key.Escape)
            {
                CancelBinding();
                return;
            }

            // 4. Safety Delay (Swallow input, do nothing)
            if ((DateTime.Now - bindingStartTime).TotalMilliseconds < 300) return;

            // 5. Ignore Repeats (Swallow input)
            if (e.IsRepeat) return;

            // 6. Ignore Garbage (Swallow input)
            if (e.Key == Key.None) return;

            // 7. Handle Unbind (Del/Back)
            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                Save(currentPedal, 0);
                return;
            }

            // 8. Handle Modifiers
            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // If it is just a modifier pressed down (e.g. holding Ctrl), we swallow it and wait.
            // We will catch the combo on the next key press, OR catch the single modifier on KeyUp.
            if (IsModifier(key)) return;

            // 9. Bind the Key (Normal keys like 'A', 'Tab', 'F1')
            uint code = GetCombinedCode(key);
            Save(currentPedal, code);
        }
        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (currentPedal == Pedal.NONE)
            {
                base.OnPreviewKeyUp(e);
                return;
            }

            // Swallow event
            e.Handled = true;

            if ((DateTime.Now - bindingStartTime).TotalMilliseconds < 300) return;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // We only act on KeyUp if it's a Modifier (binding "Just Ctrl" or "Just Alt")
            if (IsModifier(key))
            {
                uint code = GetCombinedCode(key);
                Save(currentPedal, code);
            }
        }
        private bool IsModifier(Key k)
        {
            return k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt ||
                   k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin || k == Key.System;
        }

        private uint GetCombinedCode(Key key)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            uint code = MapVirtualKey((uint)vk, 0);
            if (code == 0) code = (uint)vk;

            if (!IsModifier(key))
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) code |= ModifierMasks.CTRL;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) code |= ModifierMasks.SHIFT;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) code |= ModifierMasks.ALT;
                if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) code |= ModifierMasks.WIN;
            }
            return code;
        }

        private string GetDisplay(uint packedCode)
        {
            if (packedCode == 0) return "None";

            string prefix = "";
            if ((packedCode & ModifierMasks.CTRL) != 0) prefix += "Ctrl + ";
            if ((packedCode & ModifierMasks.SHIFT) != 0) prefix += "Shift + ";
            if ((packedCode & ModifierMasks.ALT) != 0) prefix += "Alt + ";
            if ((packedCode & ModifierMasks.WIN) != 0) prefix += "Win + ";

            uint baseCode = packedCode & ModifierMasks.KEY_MASK;

            if (baseCode == CustomButtons.MouseLeft) return prefix + "Mouse Left";
            if (baseCode == CustomButtons.MouseMiddle) return prefix + "Mouse Middle";
            if (baseCode == CustomButtons.MouseRight) return prefix + "Mouse Right";

            uint vk = MapVirtualKey(baseCode, 1);
            if (vk == 0) return prefix + $"Unknown (0x{baseCode:X})";

            Key key = KeyInterop.KeyFromVirtualKey((int)vk);
            string keyName = key.ToString();

            switch (key)
            {
                case Key.Back: keyName = "Backspace"; break;
                case Key.Return: keyName = "Enter"; break;
                case Key.Capital: keyName = "Caps Lock"; break;
                case Key.Space: keyName = "Space"; break;
                case Key.PageUp: keyName = "Page Up"; break;
                case Key.PageDown: keyName = "Page Down"; break;
                case Key.Left: keyName = "Left Arrow"; break;
                case Key.Up: keyName = "Up Arrow"; break;
                case Key.Right: keyName = "Right Arrow"; break;
                case Key.Down: keyName = "Down Arrow"; break;
                case Key.PrintScreen: keyName = "Print Screen"; break;
                case Key.NumLock: keyName = "Num Lock"; break;
                case Key.Scroll: keyName = "Scroll Lock"; break;
                case Key.LeftShift: case Key.RightShift: keyName = "Shift"; break;
                case Key.LeftCtrl: case Key.RightCtrl: keyName = "Ctrl"; break;
                case Key.LeftAlt: case Key.RightAlt: keyName = "Alt"; break;
                case Key.LWin: case Key.RWin: keyName = "Win"; break;
                case Key.Oem1: keyName = ";"; break;
                case Key.OemPlus: keyName = "+"; break;
                case Key.OemComma: keyName = ","; break;
                case Key.OemMinus: keyName = "-"; break;
                case Key.OemPeriod: keyName = "."; break;
                case Key.OemQuestion: keyName = "/"; break;
                case Key.OemTilde: keyName = "`"; break;
                case Key.OemOpenBrackets: keyName = "["; break;
                case Key.OemCloseBrackets: keyName = "]"; break;
                case Key.OemQuotes: keyName = "'"; break;
                case Key.OemBackslash: keyName = "\\"; break;
                default:
                    if (key >= Key.D0 && key <= Key.D9) keyName = ((int)(key - Key.D0)).ToString();
                    else if (key >= Key.NumPad0 && key <= Key.NumPad9) keyName = "Num " + ((int)(key - Key.NumPad0));
                    else if (key >= Key.F1 && key <= Key.F24) keyName = "F" + ((int)(key - Key.F1 + 1));
                    break;
            }

            return prefix + keyName;
        }

        // ---------------------------------------------------------
        // UI HELPERS
        // ---------------------------------------------------------
        private void SetStatus(string message)
        {
            StatusText.Text = message;
            _statusTimer.Stop();
            _statusTimer.Start();
        }
        private void SetUiLocked(bool locked)
        {
            // If locked (Binding Mode), IsEnabled = false (Grayed out)
            // If unlocked (Normal Mode), IsEnabled = true (Active)
            bool enabled = !locked;

            // Disable the containers
            ProfileHeader.IsEnabled = enabled;
            HardwareGroup.IsEnabled = enabled;
            BindingsHeader.IsEnabled = enabled;

            // Important: When we unlock, we must ensure the Clear button 
            // is correctly set based on the current device state 
            // (because simply setting BindingsHeader.IsEnabled=true might enable it incorrectly)
            if (enabled)
            {
                UpdateClearButtonState();
            }
        }
        private void MapButton_Click(object sender, RoutedEventArgs e)
        {
            if (MapButton.ContextMenu != null)
            {
                // Ensure the menu state is fresh before showing it
                RefreshMapMenuState();

                MapButton.ContextMenu.PlacementTarget = MapButton;
                MapButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                MapButton.ContextMenu.IsOpen = true;
            }
        }

        private void HighlightPedal(Pedal p)
        {
            LeftBorder.Background = MiddleBorder.Background = RightBorder.Background = Brushes.Transparent;
            if (p == Pedal.LEFT) LeftBorder.Background = Brushes.LightSkyBlue;
            else if (p == Pedal.MIDDLE) MiddleBorder.Background = Brushes.LightSkyBlue;
            else if (p == Pedal.RIGHT) RightBorder.Background = Brushes.LightSkyBlue;
        }

        private string ToFriendlyName(Pedal p)
        {
            if (p == Pedal.LEFT) return "Left";
            if (p == Pedal.MIDDLE) return "Middle";
            if (p == Pedal.RIGHT) return "Right";
            return p.ToString();
        }

        // ---------------------------------------------------------
        // TRAY & OS HOOKS
        // ---------------------------------------------------------
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
                source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0219) // WM_DEVICECHANGE
            {
                if (currentPedal != Pedal.NONE) CancelBinding();
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(500);
                    InitializeHardware();
                });
            }
            return IntPtr.Zero;
        }

        private void InitializeTrayIcon()
        {
            trayIcon = new WinForms.NotifyIcon();
            try { trayIcon.Icon = Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location); }
            catch { }
            trayIcon.Text = "Biped";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Configure", null, (s, e) => { Show(); WindowState = WindowState.Normal; Activate(); });
            menu.Items.Add("Exit", null, (s, e) => ExitApp());
            trayIcon.ContextMenuStrip = menu;
        }

        private void ExitApp()
        {
            isExiting = true;
            new Input().ReleaseAllModifiers();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Current.Shutdown();
        }

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    }
}