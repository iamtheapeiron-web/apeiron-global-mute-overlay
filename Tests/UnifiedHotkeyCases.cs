using System;
using System.IO;
using System.Reflection;
using Apeiron.GlobalMute;

public static partial class Tests {
    private static readonly ShortcutModifiers None = ShortcutModifiers.None;
    private static void UnifiedHotkeyCases() {
        Assert(typeof(Settings).GetField("BlockToggleKey") == null && typeof(Settings).GetField("BlockHoldKey") == null,
            "Removed per-action blocking preferences cannot be saved or applied");
        Assert(typeof(MainForm).GetField("blockToggle", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
            typeof(MainForm).GetField("blockHold", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "The interface has one blocking switch");
        foreach (int left in new[] { 160, 162, 164 }) {
            Assert(BlockingShortcut.IsValid(left, None, left + 1) && BlockingShortcut.IsValid(left + 1, None, left), "Both orders identify the same modifier pair");
            Assert(!BlockingShortcut.IsValid(left, None) && !BlockingShortcut.IsValid(left, ShortcutModifiers.Control, left + 1), "One modifier alone or pair plus modifier mask is not a binding");
            Assert(BlockingShortcut.Format(left + 1, None, left) == BlockingShortcut.Format(left, None, left + 1) &&
                BlockingShortcut.Format(left, None, left + 1).Contains(Ui.Text(left == 160 ? "Left Shift" : left == 162 ? "Left Ctrl" : "Left Alt")), "Pair caption names the actual sides in stable order");
        }
        Assert(!BlockingShortcut.IsValid(80, None, 220) && !BlockingShortcut.IsValid(160, None, 163), "Arbitrary ordinary pairs and mixed modifier pairs are not supported");
        var config = new Settings();
        Assert(BlockingShortcut.Conflicts(config, config.ToggleKey, None, 0) && BlockingShortcut.Conflicts(config, config.HoldKey, None, 0), "A single command must be distinct from either mic key");
        config.ToggleEnabled = config.HoldEnabled = false;
        Assert(BlockingShortcut.Conflicts(config, config.ToggleKey, None, 0) && BlockingShortcut.Conflicts(config, config.HoldKey, None, 0), "Disabled stored mic bindings also cannot share a single command key");
        Assert(!BlockingShortcut.Conflicts(config, 119, None, 0), "A separate single key is available");
        config.ToggleEnabled = config.HoldEnabled = true;
        Assert(!BlockingShortcut.Conflicts(config, 120, ShortcutModifiers.Control, 0), "An ordinary modifier combination can share its completing key with Toggle");
        foreach (int left in new[] { 160, 162, 164 }) {
            foreach (int mic in new[] { left, left + 1, left == 160 ? 16 : left == 162 ? 17 : 18 }) {
                config.ToggleKey = mic;
                Assert(BlockingShortcut.Conflicts(config, left, None, left + 1), "Pair rejects both sides and generic alias of an active mic modifier");
                config.ToggleEnabled = false;
                Assert(!BlockingShortcut.Conflicts(config, left, None, left + 1), "A disabled modifier action does not reserve a pair member");
                config.ToggleEnabled = true;
            }
        }
        foreach (bool pass in new[] { false, true })
        foreach (bool original in new[] { false, true }) {
            scenarios++;
            using (var f = new Fixture(original, true)) {
                var shortcut = new BlockingShortcut(); shortcut.Configure(119, None, true, false);
                Assert(!shortcut.Process(119, true, ShortcutModifiers.Control, pass, true).Triggered, "Single command requires no extra modifiers");
                shortcut.Process(119, false, None, pass, true);
                var result = shortcut.Process(119, true, None, pass, true);
                Assert(result.Triggered && result.SkipActions && result.Suppress == !pass, "Single ordinary command works with blocking initially off");
                f.Router.KeyBlockingEnabled = !f.Router.KeyBlockingEnabled;
                Assert(f.Router.BlockToggleKey && f.Router.BlockHoldKey && f.A.Value == original && f.A.Writes == 0 && f.Journal.Saves == 0,
                    "Single command enables blocking for both actions and does not touch audio or recovery storage");
                for (int n = 0; n < 10; n++) Assert(!shortcut.Process(119, true, None, !pass, true).Triggered, "Held single command does not repeat");
                shortcut.Configure(119, None, false, true);
                Assert(shortcut.Process(119, false, None, !pass, false).Suppress == !pass, "Disabling command preserves original key-up route");
            }
        }
        foreach (int left in new[] { 160, 162, 164 })
        foreach (bool reversePress in new[] { false, true })
        foreach (bool releaseCompletingFirst in new[] { false, true })
        foreach (bool pass in new[] { false, true }) {
            scenarios++;
            int first = left + (reversePress ? 1 : 0), completing = left + (reversePress ? 0 : 1);
            var shortcut = new BlockingShortcut(); shortcut.Configure(left, None, left + 1, true, false, false);
            Assert(shortcut.PartnerToCheck(first, true) == completing && !shortcut.NeedsModifiers(first, true), "Pair samples only the partner, not the modifier mask");
            var result = shortcut.Process(first, true, None, pass, true, false);
            Assert(!result.Triggered && !result.SkipActions && !result.Suppress && shortcut.IsActionDown(first, true), "First modifier keeps ordinary behavior and is not delayed");
            result = shortcut.Process(completing, true, ShortcutModifiers.Windows, pass, true, true);
            Assert(result.Triggered && result.SkipActions && result.Suppress == !pass, "Second side triggers once, even with another modifier held");
            Assert(!shortcut.IsActionDown(completing, true) && shortcut.IsConsumedDown(completing) == !pass, "Only the completing key is claimed");
            for (int n = 0; n < 8; n++) {
                result = shortcut.Process(completing, true, None, !pass, false);
                Assert(!result.Triggered && result.SkipActions && result.Suppress == !pass, "Pair repeat stays on original route across focus/capture changes");
                Assert(!shortcut.Process(first, true, None, !pass, true).Triggered, "First-side repeat cannot toggle again");
            }
            int upFirst = releaseCompletingFirst ? completing : first, upLast = releaseCompletingFirst ? first : completing;
            result = shortcut.Process(upFirst, false, None, !pass, false);
            Assert(result.Suppress == (upFirst == completing && !pass), "Either release order preserves each original route");
            // Re-pressing a released side while the other stays down is not a new pair gesture.
            result = shortcut.Process(upFirst, true, None, !pass, true, true);
            Assert(!result.Triggered && !result.Suppress && !result.SkipActions, "Both sides must be released before the next command");
            shortcut.Process(upFirst, false, None, !pass, true);
            result = shortcut.Process(upLast, false, None, !pass, false);
            Assert(result.Suppress == (upLast == completing && !pass), "The original completing release stays balanced after partial release/repress");
            shortcut.Process(first, true, None, pass, true, false);
            Assert(shortcut.Process(completing, true, None, pass, true, true).Triggered, "Full release re-arms either press order");
            shortcut.Configure(119, None, true, false);
            Assert(shortcut.Process(completing, false, None, !pass, false).Suppress == !pass, "Rebinding to a single key cannot leak the old pair release");
            shortcut.Process(first, false, None, !pass, false);
            Assert(shortcut.Process(119, true, None, pass, true).Triggered, "New single binding works after a pair");
        }
        foreach (int left in new[] { 160, 162, 164 }) {
            scenarios++;
            var shortcut = new BlockingShortcut(); shortcut.Configure(left, None, left + 1, true, true, true);
            Assert(!shortcut.Process(left, true, None, false, true).Triggered && !shortcut.Process(left + 1, true, None, false, true).Triggered,
                "Enabling or resuming with both modifiers held creates no command");
            shortcut.Process(left, false, None, false, true);
            Assert(!shortcut.Process(left, true, None, false, true, true).Triggered, "An already held pair requires full release before rearming");
            shortcut.Process(left, false, None, false, true); shortcut.Process(left + 1, false, None, false, true);
            shortcut.Process(left, true, None, false, true, false);
            Assert(shortcut.Process(left + 1, true, None, false, true, true).Triggered, "Fresh pair works after seeded input is released");
            shortcut.Configure(left, None, left + 1, false, true, true);
            Assert(shortcut.Process(left + 1, false, None, true, false).Suppress, "Paused pair still consumes its owned completing release");
            shortcut.Process(left, false, None, true, false);
            shortcut.Configure(left, None, left + 1, true, false, false);
            shortcut.Process(left, true, None, false, true, false); // Its up is deliberately lost.
            Assert(!shortcut.Process(left + 1, true, None, false, true, false).Triggered, "Physical partner state repairs a lost passed key-up; lone opposite side is not a command");
            shortcut.Process(left + 1, false, None, false, true);
            Assert(shortcut.Process(left + 1, true, None, false, true, true).Triggered, "Physical partner state repairs a missed first-side down");
            Assert(shortcut.Process(left + 1, false, None, false, true).Suppress, "Repaired pair still balances the consumed release");
            shortcut.Process(left, false, None, false, true);
        }
        PairHoldAndQueueCases();
        UnifiedMigrationCases();
    }
    private static void PairHoldAndQueueCases() {
        foreach (int left in new[] { 160, 162, 164 })
        foreach (bool original in new[] { false, true })
        foreach (bool initialBlocking in new[] { false, true }) {
            scenarios++;
            using (var f = new Fixture(original, true)) {
                f.Router.KeyBlockingEnabled = initialBlocking;
                var gate = new KeySuppression(); ConfigureBlocking(f, gate, false, false);
                var box = new InputMailbox(f.Clock); box.Configure(120, 121, false, false, null);
                bool holdRoute = gate.Process(121, true, false); box.Record(121, true);
                var snapshot = box.Take(false, false, false);
                f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                int writes = f.A.Writes;
                var shortcut = new BlockingShortcut(); shortcut.Configure(left, None, left + 1, true, false, false);
                for (int n = 0; n < 3; n++) {
                    shortcut.Process(left, true, None, false, true, false);
                    var result = shortcut.Process(left + 1, true, None, false, true, true);
                    if (result.Triggered) box.RequestBlockingToggle();
                    shortcut.Process(left, false, None, false, true); shortcut.Process(left + 1, false, None, false, true);
                }
                snapshot = box.Take(false, false, false);
                Assert(snapshot.BlockingToggleCount == 3, "Three quick pair gestures survive the mailbox as three commands");
                f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                if ((snapshot.BlockingToggleCount & 1) != 0) f.Router.KeyBlockingEnabled = !f.Router.KeyBlockingEnabled;
                gate.UpdateBlocking(f.Router.BlockToggleKey, f.Router.BlockHoldKey);
                Assert(f.C.HoldActive && f.A.Value == !original && f.A.Writes == writes && f.Router.KeyBlockingEnabled == !initialBlocking,
                    "Pair command changes only blocking during an active Hold");
                box.Record(121, false);
                Assert(gate.Process(121, false, false) == holdRoute, "Pair command preserves the Hold's original down/up route");
                snapshot = box.Take(false, false, false); f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                Assert(!f.C.HoldActive && f.A.Value == original && f.Journal.Saves == 0 && f.Journal.Clears == 0, "Hold restores after pair commands without file operations");
            }
        }
    }
    private static void UnifiedMigrationCases() {
        string directory = Path.Combine(Path.GetTempPath(), "apeiron-unified-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.xml");
        try {
            foreach (bool master in new[] { false, true })
            foreach (bool oldToggle in new[] { false, true })
            foreach (bool oldHold in new[] { false, true }) {
                scenarios++;
                File.WriteAllText(path, "<Settings><SchemaVersion>5</SchemaVersion><DeviceId>A</DeviceId><DeviceName>Микрофон</DeviceName><ToggleKey>119</ToggleKey><HoldKey>192</HoldKey><Opacity>45</Opacity><X>1824</X><Y>24</Y><KeyBlockingEnabled>" + master.ToString().ToLowerInvariant() + "</KeyBlockingEnabled><BlockToggleKey>" + oldToggle.ToString().ToLowerInvariant() + "</BlockToggleKey><BlockHoldKey>" + oldHold.ToString().ToLowerInvariant() + "</BlockHoldKey><BlockingShortcutEnabled>true</BlockingShortcutEnabled><BlockingShortcutKey>192</BlockingShortcutKey><BlockingShortcutModifiers>Control</BlockingShortcutModifiers></Settings>");
                var s = XmlFiles.Read<Settings>(path); s.Normalize();
                using (var controller = new Controller(s, new FakeAudio(), new Clock(), new MemoryJournal())) {
                    var router = new InputRouter(controller);
                    Assert(router.KeyBlockingEnabled == master && router.BlockToggleKey == master && router.BlockHoldKey == master,
                        "Beta.3's master is preserved; all old individual preference combinations become unified blocking");
                    Assert(s.SchemaVersion == 6 && s.DeviceId == "A" && s.DeviceName == "Микрофон" && s.ToggleKey == 119 && s.HoldKey == 192 &&
                        s.Opacity == 45 && s.X == 1824 && s.Y == 24 && s.BlockingShortcutEnabled && s.BlockingShortcutModifiers == ShortcutModifiers.Control && s.BlockingShortcutSecondKey == 0,
                        "Migration preserves microphone, appearance, position and existing modifier combination");
                }
                XmlFiles.Write(path, s);
                string saved = File.ReadAllText(path);
                Assert(!saved.Contains("BlockToggleKey") && !saved.Contains("BlockHoldKey") && !saved.Contains("FileRecoveryEnabled"), "Removed preferences cannot return in saved XML");
            }
            foreach (int left in new[] { 160, 162, 164 }) {
                var s = new Settings { BlockingShortcutEnabled = true, BlockingShortcutKey = left + 1, BlockingShortcutSecondKey = left };
                s.Normalize(); XmlFiles.Write(path, s); s = XmlFiles.Read<Settings>(path); s.Normalize();
                Assert(s.BlockingShortcutEnabled && s.BlockingShortcutKey == left && s.BlockingShortcutSecondKey == left + 1 && s.BlockingShortcutModifiers == None, "Pair binding saves both physical sides in canonical order");
            }
            var single = new Settings { BlockingShortcutEnabled = true, BlockingShortcutKey = 119 };
            single.Normalize(); XmlFiles.Write(path, single); single = XmlFiles.Read<Settings>(path); single.Normalize();
            Assert(single.BlockingShortcutEnabled && single.BlockingShortcutKey == 119 && single.BlockingShortcutSecondKey == 0, "Single key round-trips with no modifier or second key");
            single.BlockingShortcutKey = single.ToggleKey; single.Normalize();
            Assert(!single.BlockingShortcutEnabled && single.ToggleEnabled && single.ToggleKey == 120, "Loading a single command conflict disables the command and preserves mic binding");
            single.BlockingShortcutSecondKey = 220; single.BlockingShortcutEnabled = true; single.Normalize();
            Assert(!single.BlockingShortcutEnabled && single.BlockingShortcutKey == 0 && single.BlockingShortcutSecondKey == 0, "Invalid ordinary pair clears every binding field safely");
        } finally { Directory.Delete(directory, true); }
    }
}
