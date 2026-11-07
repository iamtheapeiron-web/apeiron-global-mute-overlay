using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Apeiron.GlobalMute {
    public enum NormalRule { OnChange, MutedOnly, UnmutedOnly, KeepMuted, Hidden }
    public enum HoldRule { FollowNormal, Always, Hidden }

    public class Settings {
        public int SchemaVersion = 3;
        public string DeviceId = "", DeviceName = "";
        public bool ToggleEnabled = true, HoldEnabled = true;
        public int ToggleKey = 120, HoldKey = 121;
        public NormalRule Normal = NormalRule.OnChange;
        public HoldRule DuringHold = HoldRule.Always;
        public double PopupSeconds = 2.5;
        public string Shape = "Circle";
        public int Size = 72, Opacity = 88, X = 50, Y = 50;
        public string MutedColor = "#D23B3B", OnColor = "#2FA866", IconColor = "#FFFFFF";
        public bool AutoStart = false, StartMinimized = false;
        public bool FileRecoveryEnabled = false;
        public bool OfferAdministratorRestart = true;
        public bool BlockToggleKey = false, BlockHoldKey = true;

        public void Normalize() {
            SchemaVersion = 3;
            DeviceId = DeviceId ?? ""; DeviceName = DeviceName ?? "";
            if (ToggleKey < 1 || ToggleKey > 254) ToggleKey = 120;
            if (HoldKey < 1 || HoldKey > 254) HoldKey = 121;
            if (ToggleEnabled && HoldEnabled && ToggleKey == HoldKey) HoldEnabled = false;
            if (!Enum.IsDefined(typeof(NormalRule), Normal)) Normal = NormalRule.OnChange;
            if (!Enum.IsDefined(typeof(HoldRule), DuringHold)) DuringHold = HoldRule.Always;
            if (Double.IsNaN(PopupSeconds) || Double.IsInfinity(PopupSeconds)) PopupSeconds = 2.5;
            PopupSeconds = Math.Max(0.2, Math.Min(10, PopupSeconds));
            Size = Math.Max(32, Math.Min(220, Size));
            Opacity = Math.Max(15, Math.Min(100, Opacity));
            X = Math.Max(-30000, Math.Min(30000, X)); Y = Math.Max(-30000, Math.Min(30000, Y));
            if (Shape != "Circle" && Shape != "Rounded square" && Shape != "Square") Shape = "Circle";
            MutedColor = ColorOrDefault(MutedColor, "#D23B3B");
            OnColor = ColorOrDefault(OnColor, "#2FA866");
            IconColor = ColorOrDefault(IconColor, "#FFFFFF");
        }
        private static string ColorOrDefault(string value, string fallback) {
            if (value == null || value.Length != 7 || value[0] != '#') return fallback;
            int n; return Int32.TryParse(value.Substring(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out n) ? value : fallback;
        }
    }

    public class PendingRestore {
        public string DeviceId = "", DeviceName = "";
        public bool Muted;
    }

    public interface IRestoreStore {
        PendingRestore Load();
        void Save(PendingRestore pending);
        void Clear();
    }

    public static class XmlFiles {
        private static class Serializer<T> { internal static readonly XmlSerializer Instance = new XmlSerializer(typeof(T)); }
        public static void WarmUpRecovery() {
            // Pay the first serializer generation/JIT cost at startup, not on the first Hold.
            using (var stream = new MemoryStream()) Serializer<PendingRestore>.Instance.Serialize(stream, new PendingRestore());
        }
        public static T Read<T>(string path) {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) {
                return (T)Serializer<T>.Instance.Deserialize(reader);
            }
        }
        public static void Write<T>(string path, T value) { Write(path, value, true); }
        public static void Write<T>(string path, T value, bool flushToDisk) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            try {
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    Serializer<T>.Instance.Serialize(file, value);
                    if (flushToDisk) file.Flush(true); else file.Flush();
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public class RestoreStore : IRestoreStore {
        private readonly string path;
        public RestoreStore(string path) { this.path = path; }
        public PendingRestore Load() {
            if (!File.Exists(path)) return null;
            var result = XmlFiles.Read<PendingRestore>(path);
            if (String.IsNullOrWhiteSpace(result.DeviceId))
                throw new InvalidDataException("The saved Hold recovery has no device identity.");
            return result;
        }
        // Close and atomically publish before changing mute. OS caching preserves recovery
        // across an app crash without waiting for hardware cache flush on every key press.
        // This is not a promise of power-loss durability.
        public void Save(PendingRestore value) { XmlFiles.Write(path, value, false); }
        public void Clear() {
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // Keeps Hold recovery in memory by default. A pre-existing disk recovery is
    // always loaded and retained until it has been restored, including after upgrade.
    public sealed class OptionalRestoreStore : IRestoreStore {
        private readonly IRestoreStore disk;
        private PendingRestore value;
        public bool Enabled { get; private set; }
        public bool HasPersistedRecovery { get; private set; }
        public bool CanRestartSafely { get { return value == null || HasPersistedRecovery; } }
        public OptionalRestoreStore(IRestoreStore disk, bool enabled) {
            this.disk = disk; Enabled = enabled;
            value = disk.Load(); HasPersistedRecovery = value != null;
        }
        public PendingRestore Load() { return value; }
        public void Save(PendingRestore pending) {
            if (Enabled) { disk.Save(pending); HasPersistedRecovery = true; }
            value = pending;
        }
        public void Clear() {
            if (HasPersistedRecovery) disk.Clear();
            HasPersistedRecovery = false; value = null;
        }
        public void SetEnabled(bool enabled) {
            if (enabled == Enabled) return;
            if (enabled && value != null) {
                disk.Save(value); HasPersistedRecovery = true;
            } else if (!enabled && HasPersistedRecovery) {
                // Keep the in-memory recovery. If deletion fails, leave file mode on.
                disk.Clear(); HasPersistedRecovery = false;
            }
            Enabled = enabled;
        }
    }
}
