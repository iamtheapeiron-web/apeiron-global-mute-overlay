using System;
using System.IO;
using Apeiron.GlobalMute;

public static partial class Tests {
    private static void ShortcutCases() {
        var ctrl = ShortcutModifiers.Control;
        var combination = ctrl | ShortcutModifiers.Shift;
        Assert(BlockingShortcut.IsValid(119, ShortcutModifiers.None), "An ordinary single command key is supported");
        Assert(!BlockingShortcut.IsValid(162, ctrl) && !BlockingShortcut.IsValid(1, ctrl), "No modifier-only or mouse shortcut");
        Assert(!BlockingShortcut.IsValid(27, ctrl) && !BlockingShortcut.IsValid(46, ctrl | ShortcutModifiers.Alt), "Cancel and secure attention keys cannot be assigned");
        Assert(!BlockingShortcut.IsValid(120, ShortcutModifiers.Windows), "Windows modifier is not offered");
        foreach (int modifier in new[] { 17, 162, 163 }) Assert(BlockingShortcut.UsesModifierKey(modifier, ctrl), "Both sides and generic Ctrl conflict");
        Assert(!BlockingShortcut.UsesModifierKey(160, ctrl), "Other modifier need not conflict");

        foreach (int key in new[] { 120, 121, 192, 75 })
        foreach (bool pass in new[] { false, true })
        foreach (bool enabledAfterDown in new[] { false, true }) {
            scenarios++;
            var shortcut = new BlockingShortcut();
            shortcut.Configure(key, combination, true, false);
            Assert(!shortcut.Process(key, true, ctrl, pass, true).Triggered, "Exact modifiers required");
            Assert(!shortcut.Process(key, true, combination, pass, true).Triggered, "Adding modifiers during a press does not create a command");
            shortcut.Process(key, false, combination, pass, true);
            var result = shortcut.Process(key, true, combination, pass, true);
            Assert(result.Triggered && result.SkipActions && result.Suppress == !pass, "Shortcut wins and reserves only the chosen route");
            Assert(!shortcut.IsActionDown(key, true), "A held shortcut is excluded from normal polling actions");
            Assert(shortcut.IsConsumedDown(key) == !pass, "Consumed input has hook-owned physical state");
            shortcut.Configure(key, combination, enabledAfterDown, true);
            for (int repeat = 0; repeat < 8; repeat++) {
                result = shortcut.Process(key, true, ShortcutModifiers.None, !pass, false);
                Assert(!result.Triggered && result.SkipActions && result.Suppress == !pass, "Repeat and modifier/focus/config changes preserve the physical route");
            }
            result = shortcut.Process(key, false, ShortcutModifiers.None, !pass, false);
            Assert(result.SkipActions && result.Suppress == !pass, "Release follows original shortcut down even after pause or capture");
            Assert(!shortcut.IsClaimedDown(key) && !shortcut.IsConsumedDown(key) && shortcut.IsActionDown(key, true), "Release clears claims for future normal presses");
            result = shortcut.Process(key, true, combination, pass, true);
            Assert(result.Triggered == enabledAfterDown, "New press uses the current enabled setting");
        }
        {
            var shortcut = new BlockingShortcut(); shortcut.Configure(120, ctrl, true, true);
            Assert(!shortcut.Process(120, true, ctrl, false, true).Triggered, "Key held during assignment cannot trigger");
            shortcut.Process(120, false, ctrl, false, true);
            Assert(!shortcut.Process(120, true, ctrl, false, false).SkipActions, "Capture bypass neither commands nor steals a fresh press");
            shortcut.Process(120, false, ctrl, false, false);
            Assert(!shortcut.Process(120, true, ctrl | ShortcutModifiers.Windows, false, true).Triggered, "An extra Windows modifier cannot trigger the combination");
            shortcut.Process(120, false, ctrl, false, true);
            Assert(shortcut.Process(120, true, ctrl, false, true).Triggered, "Fresh complete press re-arms");
            shortcut.Configure(121, ctrl, true, false);
            Assert(shortcut.Process(120, false, ShortcutModifiers.None, true, false).Suppress, "Rebinding cannot leak the old suppressed release");
            Assert(shortcut.Process(121, true, ctrl, false, true).Triggered, "New binding works after reconfiguration");
        }
        var clock = new Clock(); int notifications = 0;
        var mailbox = new InputMailbox(clock); mailbox.Configure(120, 121, false, false, delegate { notifications++; });
        mailbox.RequestBlockingToggle(); clock.Time = .003; mailbox.RequestBlockingToggle();
        var snapshot = mailbox.Take(false, false, false);
        Assert(snapshot.BlockingToggleCount == 2 && snapshot.HasEvent && notifications == 2 && Math.Abs(snapshot.QueueMilliseconds - 3) < .001,
            "Two quick commands remain two commands with the first event's queue latency");
        Assert(mailbox.Take(false, false, false).BlockingToggleCount == 0, "Command batch drains only once");
        mailbox.RequestBlockingToggle(); mailbox.ClearPresses();
        Assert(mailbox.Take(false, false, false).BlockingToggleCount == 0, "Capture clears stale pending commands");
        mailbox.RequestBlockingToggle(); mailbox.Configure(120, 121, false, false, null);
        Assert(mailbox.Take(false, false, false).BlockingToggleCount == 0, "Pause/rebind clears stale pending commands");

        foreach (bool original in new[] { false, true })
        foreach (int commandKey in new[] { 120, 121, 75 })
        foreach (bool master in new[] { false, true }) {
            scenarios++;
            using (var f = new Fixture(original, true)) {
                f.Router.KeyBlockingEnabled = master;
                var shortcut = new BlockingShortcut(); shortcut.Configure(commandKey, ctrl, true, false);
                var gate = new KeySuppression(); ConfigureBlocking(f, gate, false, false);
                var box = new InputMailbox(f.Clock); box.Configure(120, 121, false, false, null);
                var result = shortcut.Process(commandKey, true, ctrl, false, true);
                bool blocked = gate.Process(commandKey, true, result.SkipActions);
                if (!result.SkipActions) box.Record(commandKey, true);
                if (result.Triggered) box.RequestBlockingToggle();
                f.Advance(.2); // Polling must not turn a held Ctrl+F9/F10 into a plain mic key.
                snapshot = box.Take(true, shortcut.IsActionDown(120, commandKey == 120), shortcut.IsActionDown(121, commandKey == 121));
                f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                if ((snapshot.BlockingToggleCount & 1) != 0) f.Router.KeyBlockingEnabled = !f.Router.KeyBlockingEnabled;
                gate.UpdateBlocking(f.Router.BlockToggleKey, f.Router.BlockHoldKey);
                Assert((blocked || result.Suppress) && f.Router.KeyBlockingEnabled == !master && f.A.Writes == 0 && !f.C.HoldActive,
                    "Ctrl+normal binding changes only master blocking, including after async-state reconciliation");
                result = shortcut.Process(commandKey, false, ShortcutModifiers.None, false, true);
                gate.Process(commandKey, false, result.SkipActions);
                if (!result.SkipActions) box.Record(commandKey, false);
                snapshot = box.Take(true, false, false);
                f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                Assert(f.A.Writes == 0 && result.Suppress, "Shortcut release cannot act on the microphone");
                // Ordinary Hold is still routed and restored when master changes mid-press.
                bool downBlocked = gate.Process(121, true, false); box.Record(121, true);
                snapshot = box.Take(false, false, false);
                f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                Assert(f.C.HoldActive && f.A.Value == !original, "Normal Hold remains usable after the combination");
                f.Router.KeyBlockingEnabled = !f.Router.KeyBlockingEnabled;
                gate.UpdateBlocking(f.Router.BlockToggleKey, f.Router.BlockHoldKey);
                box.RequestBlockingToggle(); box.Record(121, false);
                Assert(gate.Process(121, false, false) == downBlocked, "Master change preserves Hold's paired down/up route");
                snapshot = box.Take(false, false, false);
                f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
                Assert(!f.C.HoldActive && f.A.Value == original && snapshot.BlockingToggleCount == 1 && f.Journal.Saves == 0,
                    "A queued command cannot erase Hold release or introduce recovery-file writes");
            }
        }
        string path = Path.Combine(Path.GetTempPath(), "apeiron-shortcut-" + Guid.NewGuid().ToString("N") + ".xml");
        try {
            File.WriteAllText(path, "<Settings><SchemaVersion>3</SchemaVersion><DeviceId>A</DeviceId><DeviceName>Микрофон</DeviceName><ToggleKey>119</ToggleKey><HoldKey>192</HoldKey><Shape>Rounded square</Shape><BlockToggleKey>true</BlockToggleKey><BlockHoldKey>false</BlockHoldKey></Settings>");
            var settings = XmlFiles.Read<Settings>(path); settings.Normalize();
            Assert(settings.SchemaVersion == 6 && settings.DeviceId == "A" && settings.DeviceName == "Микрофон" && settings.ToggleKey == 119 && settings.HoldKey == 192 &&
                !settings.KeyBlockingEnabled && settings.Shape == "Rounded square" && !settings.BlockingShortcutEnabled && settings.BlockingShortcutKey == 0,
                "Beta1 migration preserves mic/bindings/shape; unified blocking is off and command unassigned");
            settings.BlockingShortcutKey = 120; settings.BlockingShortcutModifiers = combination; settings.BlockingShortcutEnabled = true;
            XmlFiles.Write(path, settings); settings = XmlFiles.Read<Settings>(path); settings.Normalize();
            Assert(settings.BlockingShortcutEnabled && settings.BlockingShortcutKey == 120 && settings.BlockingShortcutModifiers == combination,
                "Combination persists independently of the build language");
            settings.ToggleKey = 162; settings.Normalize();
            Assert(!settings.BlockingShortcutEnabled && settings.ToggleEnabled && settings.ToggleKey == 162 && settings.HoldKey == 192,
                "Conflicting modifier disables the new shortcut without resetting existing mic hotkeys");
            settings.BlockingShortcutKey = 1; settings.BlockingShortcutEnabled = true; settings.Normalize();
            Assert(!settings.BlockingShortcutEnabled && settings.BlockingShortcutKey == 0, "Invalid shortcut normalizes safely");
            Assert(typeof(Settings).GetField("Language") == null && !File.ReadAllText(path).Contains("<Language>"), "There is no saved language selector");
        } finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
        var shapeChoice = new LocalizedChoice("Rounded square");
        Assert(shapeChoice.Id == "Rounded square" && shapeChoice.ToString() == Ui.Text("Rounded square"), "Translation does not change stable shape IDs");
        string buildLanguage = Ui.LanguageCode;
        Assert(buildLanguage == "RU" ? Ui.Text("Controls") == "Управление" && Ui.Text("Assign combination") == "Назначить сочетание" :
            buildLanguage == "EN" && Ui.Text("Controls") == "Controls", "The executable fixes its own UI language");
        Assert(BlockingShortcut.Format(120, combination) == "Ctrl + Shift + F9", "Combination display is unambiguous");
    }
}
