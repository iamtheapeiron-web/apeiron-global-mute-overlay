using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Apeiron.GlobalMute {
    // Windows Core Audio endpoint mute. Audio samples and volume levels are never read or changed.
    public sealed class CoreAudio : IAudio {
        private IAudioEndpointVolume volume;
        private IMMDevice connectedDevice;
        public string ConnectedId { get; private set; }
        public bool HardwareMute { get; private set; }
        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        private static void Release(object value) {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
        private static IMMDeviceEnumerator Enumerator() { return (IMMDeviceEnumerator)new MMDeviceEnumerator(); }
        public List<DeviceInfo> Enumerate() {
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            IMMDevice defaultDevice = null;
            var result = new List<DeviceInfo>();
            try {
                enumerator = Enumerator(); string defaultId = "";
                if (enumerator.GetDefaultAudioEndpoint(1, 1, out defaultDevice) >= 0)
                    Check(defaultDevice.GetId(out defaultId));
                Check(enumerator.EnumAudioEndpoints(1, 1, out collection)); // eCapture, DEVICE_STATE_ACTIVE
                uint count; Check(collection.GetCount(out count));
                for (uint i = 0; i < count; ++i) {
                    IMMDevice device = null;
                    try {
                        Check(collection.Item(i, out device)); string id;
                        Check(device.GetId(out id));
                        result.Add(new DeviceInfo { Id = id, Name = FriendlyName(device, id), IsDefault = id == defaultId });
                    } finally { Release(device); }
                }
                result.Sort(delegate(DeviceInfo a, DeviceInfo b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); });
                return result;
            } finally { Release(defaultDevice); Release(collection); Release(enumerator); }
        }
        private static string FriendlyName(IMMDevice device, string fallback) {
            IPropertyStore properties = null;
            try {
                Check(device.OpenPropertyStore(0, out properties));
                var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                PropVariant value; Check(properties.GetValue(ref key, out value));
                try {
                    if (value.Type == 31 && value.Pointer != IntPtr.Zero) return Marshal.PtrToStringUni(value.Pointer);
                    return fallback;
                } finally { PropVariantClear(ref value); }
            } catch { return fallback; }
            finally { Release(properties); }
        }
        public void Connect(string deviceId) {
            Disconnect();
            IMMDeviceEnumerator enumerator = null; IMMDevice device = null; object activated = null;
            try {
                enumerator = Enumerator();
                Check(enumerator.GetDevice(deviceId, out device));
                uint state; Check(device.GetState(out state));
                if (state != 1) throw new InvalidOperationException(Ui.Text("The selected microphone is disconnected or disabled."));
                int flow; Check(((IMMEndpoint)device).GetDataFlow(out flow));
                if (flow != 1) throw new InvalidOperationException(Ui.Text("The selected device is not a recording device."));
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                Check(device.Activate(ref iid, 23, IntPtr.Zero, out activated)); // CLSCTX_ALL
                volume = (IAudioEndpointVolume)activated; activated = null;
                uint capabilities; Check(volume.QueryHardwareSupport(out capabilities));
                HardwareMute = (capabilities & 2) != 0;
                ConnectedId = deviceId;
                connectedDevice = device; device = null;
                GetMute(); // Confirm the endpoint supports reading its real mute state.
            } catch { Disconnect(); throw; }
            finally { Release(activated); Release(device); Release(enumerator); }
        }
        public bool GetMute() {
            if (volume == null) throw new InvalidOperationException(Ui.Text("The selected microphone is unavailable."));
            bool muted; Check(volume.GetMute(out muted)); return muted;
        }
        public void ValidateDevice() {
            if (connectedDevice == null) throw new InvalidOperationException(Ui.Text("The selected microphone is unavailable."));
            uint state; Check(connectedDevice.GetState(out state));
            if (state != 1) throw new InvalidOperationException(Ui.Text("The selected microphone is disconnected or disabled."));
        }
        public void SetMute(bool muted) {
            if (volume == null) throw new InvalidOperationException(Ui.Text("The selected microphone is unavailable."));
            Guid context = new Guid("c418ac92-1881-4689-856d-d56b967411d2");
            Check(volume.SetMute(muted, ref context));
        }
        public void Disconnect() {
            Release(volume); volume = null; Release(connectedDevice); connectedDevice = null;
            ConnectedId = null; HardwareMute = false;
        }
        public void Dispose() { Disconnect(); }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey { public Guid FormatId; public uint Id; }
    // This build is x64. Native PROPVARIANT is 24 bytes with its union at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMEndpoint { [PreserveSig] int GetDataFlow(out int flow); }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr client);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr client);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }
}
