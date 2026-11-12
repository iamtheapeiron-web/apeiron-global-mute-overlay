using System;
using System.IO;
using System.Reflection;
using Apeiron.GlobalMute;

public static partial class Tests {
    private static void MemoryRecoveryCases() {
        Assert(typeof(Settings).GetField("FileRecoveryEnabled") == null, "Removed recovery option cannot be enabled or saved");
        Assert(typeof(MainForm).GetField("fileRecovery", BindingFlags.Instance | BindingFlags.NonPublic) == null, "Removed file-recovery checkbox is not part of the form");
        Assert(typeof(LegacyRestoreFile).GetMethod("Save") == null, "Legacy compatibility has no file writer");
        foreach (bool original in new[] { false, true })
        foreach (NormalRule normal in Enum.GetValues(typeof(NormalRule)))
        foreach (HoldRule hold in Enum.GetValues(typeof(HoldRule)))
        foreach (double seconds in new[] { 0.2, 1.7 }) {
            scenarios++;
            using (var f = new Fixture(original, true)) {
                f.S.Normal = normal; f.S.DuringHold = hold; f.S.PopupSeconds = seconds;
                f.C.BeginHold();
                Assert(f.C.HoldActive && f.A.Value == !original && f.C.HasPending, "Memory recovery retains the exact original state");
                Assert(f.Journal.Saves == 0 && !f.Recovery.HasLegacyRecovery && !f.Recovery.CanRestartSafely, "Active memory Hold has no persisted recovery");
                f.Advance(seconds); f.C.ReleaseHold();
                Assert(f.A.Value == original && !f.C.HasPending && f.Recovery.CanRestartSafely, "Release restores in memory");
                Assert(f.Journal.Clears == 0 && f.Journal.Loads == 1, "Normal Hold does not touch the legacy store after startup");
                f.C.BeginHold(); f.A.Online = false; f.C.Poll();
                Assert(f.C.HasPending && !f.C.Ready && !f.Recovery.CanRestartSafely, "Unavailable mic prevents losing in-memory recovery on Administrator restart");
                f.A.Online = true; f.Advance(2); f.C.Poll();
                Assert(f.A.Value == original && !f.C.HasPending, "Reconnect restores the same endpoint");
            }
        }
        using (var f = new Fixture(false, true)) {
            f.Journal.FailSave = true; f.Journal.FailClear = true;
            for (int i = 0; i < 200; i++) { f.C.BeginHold(); f.C.ReleaseHold(); }
            Assert(f.Journal.Loads == 1 && f.Journal.Saves == 0 && f.Journal.Clears == 0 && !f.A.Value,
                "200 memory Hold cycles ignore legacy-store write/delete failures and perform no disk operations");
            f.C.BeginHold(); f.C.Dispose();
            Assert(!f.A.Value && !f.C.HasPending, "Normal exit restores an available microphone without a recovery file");
        }
        foreach (bool original in new[] { false, true }) {
            var disk = new MemoryJournal { Value = new PendingRestore { DeviceId = "A", DeviceName = "Old device", Muted = original }, FailClear = true };
            var memory = new MemoryRestoreStore(disk);
            var settings = new Settings { DeviceId = "B" }; var audio = new FakeAudio { Value = !original };
            var clock = new Clock();
            using (var controller = new Controller(settings, audio, clock, memory)) {
                Assert(controller.HasPending && settings.DeviceId == "A" && memory.HasLegacyRecovery && memory.CanRestartSafely,
                    "Upgrade preserves exact-device legacy recovery");
                bool refused = false;
                try { memory.Save(new PendingRestore { DeviceId = "B" }); } catch (InvalidOperationException) { refused = true; }
                Assert(refused && memory.Load().DeviceId == "A", "Unfinished legacy recovery cannot be overwritten");
                controller.Poll();
                Assert(audio.Value == original && controller.HasPending && memory.HasLegacyRecovery && disk.Saves == 0,
                    "Failed legacy deletion retains recovery after restoring audio");
                Assert(!controller.SelectDevice(new DeviceInfo { Id = "B" }), "Different endpoint is blocked until legacy restore completes");
                disk.FailClear = false; clock.Time += 2; controller.Poll();
                Assert(controller.Ready && !controller.HasPending && !memory.HasLegacyRecovery && disk.Value == null,
                    "Retry finishes legacy recovery and retires the old file");
                int clears = disk.Clears; controller.BeginHold(); controller.ReleaseHold();
                Assert(audio.Value == original && disk.Saves == 0 && disk.Clears == clears, "All later Holds use memory exclusively");
            }
        }
        string directory = Path.Combine(Path.GetTempPath(), "apeiron-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string path = Path.Combine(directory, "hold-recovery.xml");
            var store = new MemoryRestoreStore(new LegacyRestoreFile(path));
            var settings = new Settings { DeviceId = "A", DeviceName = "Device" }; var audio = new FakeAudio();
            using (var controller = new Controller(settings, audio, new Clock(), store)) {
                controller.Poll(); controller.BeginHold();
                Assert(Directory.GetFiles(directory).Length == 0, "Active Hold creates no recovery, backup or temporary file");
                controller.ReleaseHold();
            }
            Assert(Directory.GetFiles(directory).Length == 0, "Normal exit creates no recovery file");
            XmlFiles.Write(path, new PendingRestore { DeviceId = "A", DeviceName = "Old device", Muted = true });
            var legacy = new MemoryRestoreStore(new LegacyRestoreFile(path));
            using (var controller = new Controller(settings, audio, new Clock(), legacy)) {
                Assert(legacy.HasLegacyRecovery, "Real pre-existing recovery is loaded"); controller.Poll();
                Assert(audio.Value && !controller.HasPending && Directory.GetFiles(directory).Length == 0, "Real legacy recovery is restored and deleted");
                controller.BeginHold(); Assert(Directory.GetFiles(directory).Length == 0, "Legacy import never enables new recovery writes"); controller.ReleaseHold();
            }
            string config = Path.Combine(directory, "settings.xml");
            File.WriteAllText(config, "<Settings><SchemaVersion>4</SchemaVersion><DeviceId>A</DeviceId><HoldKey>192</HoldKey><ToggleKey>119</ToggleKey><Opacity>45</Opacity><X>1824</X><Y>24</Y><FileRecoveryEnabled>true</FileRecoveryEnabled><BlockToggleKey>true</BlockToggleKey><BlockHoldKey>false</BlockHoldKey></Settings>");
            var loaded = XmlFiles.Read<Settings>(config); loaded.Normalize();
            Assert(loaded.SchemaVersion == 6 && loaded.HoldKey == 192 && loaded.ToggleKey == 119 && loaded.DeviceId == "A" && loaded.Opacity == 45 &&
                loaded.Transparency == 55 && loaded.X == 1824 && loaded.Y == 24 && !loaded.KeyBlockingEnabled,
                "Beta.2 migration ignores old opt-in, preserves visuals/position/mic/bindings and defaults shared blocking off");
            XmlFiles.Write(config, loaded);
            Assert(!File.ReadAllText(config).Contains("FileRecoveryEnabled") && !File.ReadAllText(config).Contains("<Transparency>"), "Saved settings contain no removed option or duplicate alpha representation");
        } finally { Directory.Delete(directory, true); }
    }

    private static void PresentationCases() {
        var settings = new Settings();
        Assert(!settings.KeyBlockingEnabled && settings.OfferAdministratorRestart, "Fresh defaults use opt-in blocking and retain Administrator startup notice");
        for (int opacity = 5; opacity <= 100; opacity++) {
            settings.Opacity = opacity; int visible = settings.Transparency;
            settings.Transparency = visible; settings.Normalize();
            Assert(settings.Opacity == opacity && visible == 100 - opacity, "Transparency is the inverse of the unchanged stored opacity");
        }
        settings.Transparency = 95; settings.Normalize(); Assert(settings.Opacity == 5 && settings.Transparency == 95, "95% transparency remains valid after normalization");
        settings.Transparency = 999; Assert(settings.Transparency == 95, "Transparency upper bound");
        settings.Transparency = -5; Assert(settings.Transparency == 0 && settings.Opacity == 100, "Transparency lower bound");
        var normal = OverlayRuleChoice.NormalChoices(); var hold = OverlayRuleChoice.HoldChoices();
        Assert(normal.Length == Enum.GetValues(typeof(NormalRule)).Length && hold.Length == Enum.GetValues(typeof(HoldRule)).Length,
            "Both dropdowns retain every existing behavior");
        for (int i = 0; i < normal.Length; i++) Assert(normal[i].Value == i && !String.IsNullOrEmpty(normal[i].ToString()), "Normal dropdown retains stable enum values");
        for (int i = 0; i < hold.Length; i++) Assert(hold[i].Value == i && !String.IsNullOrEmpty(hold[i].ToString()), "Hold dropdown retains stable enum values");
        Assert(normal[1].ToString() != normal[3].ToString(), "Brief muted display and persistent muted display have distinct labels");
        settings = new Settings(); var changes = new SettingsChangeTracker(settings);
        Assert(!changes.HasChanges(settings), "Loaded settings start clean");
        foreach (var field in typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public)) {
            if (field.Name == "SchemaVersion" || field.Name == "DeviceName") continue;
            object original = field.GetValue(settings), changed;
            if (field.FieldType == typeof(bool)) changed = !(bool)original;
            else if (field.FieldType == typeof(int)) changed = (int)original + 1;
            else if (field.FieldType == typeof(double)) changed = (double)original + .1;
            else if (field.FieldType == typeof(string)) changed = (string)original + "changed";
            else if (field.FieldType.IsEnum) changed = Enum.ToObject(field.FieldType, Convert.ToInt32(original) + 1);
            else throw new Exception("Uncovered setting type: " + field.Name);
            field.SetValue(settings, changed); Assert(changes.HasChanges(settings), "Dirty state notices: " + field.Name);
            field.SetValue(settings, original); Assert(!changes.HasChanges(settings), "Reverting clears dirty state: " + field.Name);
        }
        settings.DeviceName = "Driver refreshed its display name"; settings.SchemaVersion = 999;
        Assert(!changes.HasChanges(settings), "Automatic device-name and schema updates are not user edits");
        settings.KeyBlockingEnabled = true; Assert(changes.HasChanges(settings), "Hotkey/tray master changes require Save for persistence");
        changes.Accept(settings); Assert(!changes.HasChanges(settings), "Successful save accepts the current settings");
        using (var f = new Fixture(false, true)) {
            var tracker = new SettingsChangeTracker(f.S);
            f.C.Preview(true); f.C.Toggle(); f.C.BeginHold(); f.C.ReleaseHold(); f.Router.SetPaused(true, false, false);
            Assert(!tracker.HasChanges(f.S), "Preview, mute actions, Hold and session pause never mark settings dirty");
            f.Router.SetPaused(false, false, false);
            Assert(InputStatusText.Blocking(f.Router) == Ui.Text("Key blocking off"), "Status reports actual default-off blocking");
            f.Router.KeyBlockingEnabled = true; Assert(InputStatusText.Blocking(f.Router) == Ui.Text("Both keys blocked"), "Status reports both enabled microphone keys");
            f.S.ToggleEnabled = false; Assert(InputStatusText.Blocking(f.Router) == Ui.Text("Blocked: Hold"), "Only the enabled Hold key is blocked");
            f.S.ToggleEnabled = f.S.HoldEnabled = false;
            Assert(InputStatusText.Blocking(f.Router) == Ui.Text("No active keys to block"), "Enabled master with no effective keys is not reported as blocking");
            f.Router.SetPaused(true, false, false);
            Assert(InputStatusText.Blocking(f.Router) == Ui.Text("Key blocking paused"), "Pause overrides effective blocking status");
        }
        string path = Path.Combine(Path.GetTempPath(), "apeiron-blocking-" + Guid.NewGuid().ToString("N") + ".xml");
        try {
            foreach (bool enabled in new[] { false, true }) {
                settings = new Settings { KeyBlockingEnabled = enabled, Opacity = 5 };
                XmlFiles.Write(path, settings); settings = XmlFiles.Read<Settings>(path); settings.Normalize();
                using (var controller = new Controller(settings, new FakeAudio(), new Clock(), new MemoryJournal())) {
                    var router = new InputRouter(controller);
                    Assert(router.KeyBlockingEnabled == enabled && !router.Paused && settings.Transparency == 95,
                        "Save/restart retains either master state and 95% transparency while clearing pause");
                }
            }
        } finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
        string language = Ui.LanguageCode;
        Assert(language == "RU" ? Ui.PunctuationHint == "" : Ui.PunctuationHint.Contains("US"), "US-layout explanation is absent in RU and retained in EN");
    }
}
