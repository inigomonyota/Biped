using System;
using System.Collections.Generic;
using System.Linq;
using HidLibrary;

namespace biped
{
    public enum Pedal { NONE, LEFT, MIDDLE, RIGHT }

    public class MultiBiped
    {
        private const int VendorID = 0x5f3;
        private const int ProductID = 0xFF;
        private const byte LEFT_MASK = 0x01;
        private const byte MIDDLE_MASK = 0x02;
        private const byte RIGHT_MASK = 0x04;

        public List<BipedDevice> Devices { get; } = new List<BipedDevice>();
        private readonly Input input = new Input();

        public event Action<BipedDevice, Pedal> PedalPressed;

        public MultiBiped()
        {
            RefreshDevices();
        }

        public void RefreshDevices()
        {
            // 1. Cleanup OLD devices
            foreach (var d in Devices)
            {
                d.Device.MonitorDeviceEvents = false;
                d.Device.CloseDevice();

                // SAFETY FIX: If a key was held down when we refreshed/unplugged, RELEASE IT.
                // Otherwise, Shift/Ctrl might get stuck forever.
                if (d.Config != null)
                {
                    if (d.LatchedLeft && d.Config.Left != 0) input.SendKey(d.Config.Left, true);
                    if (d.LatchedMiddle && d.Config.Middle != 0) input.SendKey(d.Config.Middle, true);
                    if (d.LatchedRight && d.Config.Right != 0) input.SendKey(d.Config.Right, true);
                }
            }
            Devices.Clear();

            // 2. Find NEW devices
            var found = HidDevices.Enumerate(VendorID, ProductID).ToList();
            int idCounter = 1;

            foreach (var dev in found)
            {
                var bd = new BipedDevice(dev, idCounter++);
                bd.Device.OpenDevice();
                bd.Device.MonitorDeviceEvents = true;

                HidDevice localDevice = bd.Device;
                bd.Device.ReadReport(report => OnReport(report, localDevice, bd));

                Devices.Add(bd);
            }
        }

        private void OnReport(HidReport report, HidDevice localDevice, BipedDevice device)
        {
            // Unplug check
            if (!localDevice.IsConnected || report.ReadStatus != HidDeviceData.ReadStatus.Success) return;

            if (report.Data == null || report.Data.Length < 1)
            {
                localDevice.ReadReport(rpt => OnReport(rpt, localDevice, device));
                return;
            }

            byte status = report.Data[0];

            // Per-device timer
            var now = DateTime.Now;
            if ((now - device.LastEvent).TotalMilliseconds < 12)
            {
                localDevice.ReadReport(rpt => OnReport(rpt, localDevice, device));
                return;
            }
            device.LastEvent = now;

            Process(device, Pedal.LEFT, (status & LEFT_MASK) != 0);
            Process(device, Pedal.MIDDLE, (status & MIDDLE_MASK) != 0);
            Process(device, Pedal.RIGHT, (status & RIGHT_MASK) != 0);

            CheckEdge(device, Pedal.LEFT, status, LEFT_MASK);
            CheckEdge(device, Pedal.MIDDLE, status, MIDDLE_MASK);
            CheckEdge(device, Pedal.RIGHT, status, RIGHT_MASK);

            device.LastStatus = status;
            localDevice.ReadReport(rpt => OnReport(rpt, localDevice, device));
        }

        private void CheckEdge(BipedDevice device, Pedal p, byte current, byte mask)
        {
            bool isDown = (current & mask) != 0;
            bool wasDown = (device.LastStatus & mask) != 0;

            if (isDown && !wasDown) PedalPressed?.Invoke(device, p);
        }

        private void Process(BipedDevice dev, Pedal pedal, bool pressed)
        {
            if (dev.Config == null) return;

            uint code = pedal == Pedal.LEFT ? dev.Config.Left :
                        pedal == Pedal.MIDDLE ? dev.Config.Middle : dev.Config.Right;

            if (code == 0) return;

            // DETERMINING WHICH LATCH TO USE
            // We cannot use a variable 'ref' easily here, so we use if/else
            bool isLatched = false;
            if (pedal == Pedal.LEFT) isLatched = dev.LatchedLeft;
            else if (pedal == Pedal.MIDDLE) isLatched = dev.LatchedMiddle;
            else if (pedal == Pedal.RIGHT) isLatched = dev.LatchedRight;

            if (pressed)
            {
                // DOWN
                if (dev.SuppressOutput) return;
                if (isLatched) return; // Already down

                input.SendKey(code, false);

                // Update the specific flag
                if (pedal == Pedal.LEFT) dev.LatchedLeft = true;
                else if (pedal == Pedal.MIDDLE) dev.LatchedMiddle = true;
                else if (pedal == Pedal.RIGHT) dev.LatchedRight = true;
            }
            else
            {
                // UP
                if (isLatched)
                {
                    input.SendKey(code, true);

                    // Update the specific flag
                    if (pedal == Pedal.LEFT) dev.LatchedLeft = false;
                    else if (pedal == Pedal.MIDDLE) dev.LatchedMiddle = false;
                    else if (pedal == Pedal.RIGHT) dev.LatchedRight = false;
                }
            }
        }
    }
}