using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Apeiron.GlobalMute {
    public enum NormalRule { OnChange, MutedOnly, UnmutedOnly, KeepMuted, Hidden }
    public enum HoldRule { FollowNormal, Always, Hidden }

    public class Settings {
        public int SchemaVersion = 5;
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
        public bool OfferAdministratorRestart = true;
        public bool BlockToggleKey = false, BlockHoldKey = true;
        public bool KeyBlockingEnabled = false;
        public bool BlockingShortcutEnabled = false;
        public int BlockingShortcutKey = 0;
        public ShortcutModifiers BlockingShortcutModifiers = ShortcutModifiers.None;

        // Keep the old stored opacity so an upgrade preserves the exact appearance.
        [XmlIgnore]
        public int Transparency {
            get { return 100 - Opacity; }
            set { Opacity = 100 - Math.Max(0, Math.Min(95, value)); }
        }

        public void Normalize() {
            SchemaVersion = 5;
            DeviceId = DeviceId ?? ""; DeviceName = DeviceName ?? "";
            if (ToggleKey < 1 || ToggleKey > 254) ToggleKey = 120;
            if (HoldKey < 1 || HoldKey > 254) HoldKey = 121;
            if (ToggleEnabled && HoldEnabled && ToggleKey == HoldKey) HoldEnabled = false;
            if (!BlockingShortcut.IsValid(BlockingShortcutKey, BlockingShortcutModifiers)) {
                BlockingShortcutEnabled = false; BlockingShortcutKey = 0; BlockingShortcutModifiers = ShortcutModifiers.None;
            }
            if (BlockingShortcut.Conflicts(this, BlockingShortcutModifiers)) BlockingShortcutEnabled = false;
            if (!Enum.IsDefined(typeof(NormalRule), Normal)) Normal = NormalRule.OnChange;
            if (!Enum.IsDefined(typeof(HoldRule), DuringHold)) DuringHold = HoldRule.Always;
            if (Double.IsNaN(PopupSeconds) || Double.IsInfinity(PopupSeconds)) PopupSeconds = 2.5;
            PopupSeconds = Math.Max(0.2, Math.Min(10, PopupSeconds));
            Size = Math.Max(32, Math.Min(220, Size));
            Opacity = Math.Max(5, Math.Min(100, Opacity));
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

    public interface ILegacyRestoreFile {
        PendingRestore Load();
        void Clear();
    }

    public static class XmlFiles {
        private static class Serializer<T> { internal static readonly XmlSerializer Instance = new XmlSerializer(typeof(T)); }
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

    // Upgrade compatibility only: read and retire an existing recovery file.
    // This class cannot create or update a recovery file.
    public sealed class LegacyRestoreFile : ILegacyRestoreFile {
        private readonly string path;
        public LegacyRestoreFile(string path) { this.path = path; }
        public PendingRestore Load() {
            if (!File.Exists(path)) return null;
            var result = XmlFiles.Read<PendingRestore>(path);
            if (String.IsNullOrWhiteSpace(result.DeviceId))
                throw new InvalidDataException(Ui.Text("The saved Hold recovery has no device identity."));
            return result;
        }
        public void Clear() {
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // New Hold sessions use memory exclusively. Only a legacy file loaded at
    // startup is retained until the original endpoint has been restored.
    public sealed class MemoryRestoreStore : IRestoreStore {
        private readonly ILegacyRestoreFile legacy;
        private PendingRestore value;
        public bool HasLegacyRecovery { get; private set; }
        public bool CanRestartSafely { get { return value == null || HasLegacyRecovery; } }
        public MemoryRestoreStore(ILegacyRestoreFile legacy) {
            this.legacy = legacy;
            value = legacy.Load(); HasLegacyRecovery = value != null;
        }
        public PendingRestore Load() { return value; }
        public void Save(PendingRestore pending) {
            if (HasLegacyRecovery) throw new InvalidOperationException(Ui.Text("Restore the previous Hold state before starting another Hold."));
            value = pending;
        }
        public void Clear() {
            if (HasLegacyRecovery) legacy.Clear();
            HasLegacyRecovery = false; value = null;
        }
    }
}
