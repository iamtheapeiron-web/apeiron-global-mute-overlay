using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Apeiron.GlobalMute;

public class Clock : IClock { public double Time; public double Now { get { return Time; } } }
public class MemoryJournal : IRestoreStore {
    public PendingRestore Value;
    public bool FailSave, FailClear;
    public int Loads, Saves, Clears;
    public PendingRestore Load() { Loads++; return Value; }
    public void Save(PendingRestore value) { Saves++; if (FailSave) throw new IOException("Journal is unwritable"); Value = value; }
    public void Clear() { Clears++; if (FailClear) throw new IOException("Journal is locked"); Value = null; }
}
public class FakeAudio : IAudio {
    public bool Value, Online = true, FailWrite, IgnoreWrite, FailAfterWrite;
    public int Writes, Reads, Validations;
    public readonly List<string> WrittenIds = new List<string>();
    public string ConnectedId { get; private set; }
    public bool HardwareMute { get { return true; } }
    public List<DeviceInfo> Enumerate() { return new List<DeviceInfo> { new DeviceInfo { Id = "A", Name = "Microphone A" }, new DeviceInfo { Id = "B", Name = "Microphone B" } }; }
    public void Connect(string id) { Check(); ConnectedId = id; }
    private void Check() { if (!Online) throw new IOException("Device disconnected"); }
    public bool GetMute() { Reads++; Check(); if (ConnectedId == null) throw new IOException("No endpoint"); return Value; }
    public void ValidateDevice() { Validations++; Check(); }
    public void SetMute(bool value) {
        Check(); if (FailWrite) throw new IOException("Mute write failed");
        if (!IgnoreWrite) Value = value;
        Writes++; WrittenIds.Add(ConnectedId);
        if (FailAfterWrite) { FailAfterWrite = false; throw new IOException("Driver failed after write"); }
    }
    public void Disconnect() { ConnectedId = null; }
    public void Dispose() { Disconnect(); }
}
public class Fixture : IDisposable {
    public Settings S = new Settings { DeviceId = "A", DeviceName = "Microphone A" };
    public FakeAudio A = new FakeAudio();
    public Clock Clock = new Clock();
    public MemoryJournal Journal = new MemoryJournal();
    public Controller C;
    public InputRouter Router;
    public OptionalRestoreStore Recovery;
    public Fixture(bool muted) : this(muted, null) { }
    public Fixture(bool muted, bool? fileRecovery) {
        A.Value = muted;
        IRestoreStore store = Journal;
        if (fileRecovery.HasValue) { Recovery = new OptionalRestoreStore(Journal, fileRecovery.Value); store = Recovery; }
        C = new Controller(S, A, Clock, store); Router = new InputRouter(C); C.Poll();
    }
    public void Advance(double seconds) { Clock.Time += seconds; }
    public void Dispose() { C.Dispose(); }
}

public static class Tests {
    private static int assertions, scenarios;
    private static void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception("Assertion " + assertions + ": " + message); }
    private static bool ExpectedPopup(NormalRule rule, bool muted) {
        return rule == NormalRule.OnChange || rule == NormalRule.MutedOnly && muted || rule == NormalRule.UnmutedOnly && !muted || rule == NormalRule.KeepMuted && muted;
    }
    private static void Matrix() {
        foreach (bool original in new[] { false, true })
        foreach (NormalRule normal in Enum.GetValues(typeof(NormalRule)))
        foreach (HoldRule hold in Enum.GetValues(typeof(HoldRule)))
        foreach (double duration in new[] { 0.2, 1.0, 1.7, 2.5, 10.0 }) {
            scenarios++;
            using (var f = new Fixture(original)) {
                f.S.Normal = normal; f.S.DuringHold = hold; f.S.PopupSeconds = duration; f.C.RulesChanged();
                f.Router.Tick(true, true, true, true);
                Assert(f.C.HoldActive && f.A.Value == !original, "Hold wins simultaneous press");
                Assert(f.Journal.Value.Muted == original && f.Journal.Value.DeviceId == "A", "Original state journalled");
                bool visible = hold == HoldRule.Always || hold == HoldRule.FollowNormal && ExpectedPopup(normal, !original);
                Assert(f.C.View.Visible == visible && f.C.View.Muted == !original, "Hold overlay on press");
                f.Advance(duration - 0.01); Assert(f.C.View.Visible == visible, "Popup visible before expiry");
                f.Advance(0.01);
                bool persistent = hold == HoldRule.Always || hold == HoldRule.FollowNormal && normal == NormalRule.KeepMuted && !original;
                Assert(f.C.View.Visible == persistent, "Exact expiry while key held");
                f.A.Value = original; f.C.Poll(); Assert(f.A.Value == !original && f.C.HoldActive, "External change during Hold corrected");
                f.Router.Tick(false, true, false, false); f.Router.Tick(true, true, true, false);
                Assert(f.A.Value == !original && f.C.HoldActive, "Toggle ignored during Hold");
                f.Router.Tick(false, true, false, false); f.Router.Tick(true, false, true, false);
                Assert(!f.C.HoldActive && f.A.Value == !original && !f.C.HasPending, "Release then Toggle same tick");
                Assert(f.C.View.Visible == ExpectedPopup(normal, !original), "Normal rule after Toggle");
                f.Advance(duration); Assert(f.C.View.Visible == (normal == NormalRule.KeepMuted && !original), "Transient does not outlive duration");
            }
            using (var f = new Fixture(original)) {
                f.S.Normal = normal; f.S.DuringHold = hold; f.C.RulesChanged();
                f.C.BeginHold(); f.C.ReleaseHold();
                Assert(f.A.Value == original && !f.C.HasPending, "Short Hold restores base");
                Assert(f.C.View.Visible == (normal == NormalRule.KeepMuted && original), "Release creates no extra popup");
                f.Router.Tick(false, false, false, true);
                Assert(!f.C.HoldActive && f.A.Value == original && !f.C.HasPending, "Complete Hold pulse between ticks");
            }
        }
        foreach (bool original in new[] { false, true })
        foreach (NormalRule rule in Enum.GetValues(typeof(NormalRule))) {
            using (var f = new Fixture(original)) {
                f.S.Normal = rule; f.C.Toggle();
                Assert(f.A.Value == !original, "Ordinary Toggle");
                Assert(f.C.View.Visible == ExpectedPopup(rule, !original), "Ordinary Toggle overlay");
                f.A.Value = original; f.C.Poll();
                Assert(f.C.Muted == original && f.C.View.Visible == ExpectedPopup(rule, original), "External normal mute observed");
            }
        }
    }
    private static void FailureCases() {
        foreach (bool original in new[] { false, true }) {
            using (var f = new Fixture(original)) {
                f.C.BeginHold(); f.A.Online = false; f.C.Poll();
                Assert(!f.C.Ready && !f.C.HoldActive && f.C.HasPending && !f.C.View.Visible, "Device loss hides unknown state");
                int writes = f.A.Writes; f.C.Toggle(); f.C.BeginHold();
                Assert(f.A.Writes == writes, "No writes while device unavailable");
                Assert(!f.C.SelectDevice(new DeviceInfo { Id = "B", Name = "B" }), "Device switch blocked until old Hold restored");
                Assert(f.S.DeviceId == "A" && f.C.Pending.DeviceId == "A", "Original endpoint stays pinned");
                f.A.Online = true; f.Advance(2); f.C.Poll();
                Assert(f.C.Ready && f.A.Value == original && !f.C.HasPending, "Reconnect restores the exact original state");
                foreach (var id in f.A.WrittenIds) Assert(id == "A", "No accidental write to another device");
            }
            using (var f = new Fixture(original)) {
                f.C.BeginHold(); f.A.FailWrite = true; f.C.ReleaseHold();
                Assert(f.C.HasPending && !f.C.Ready, "Failed release retained");
                f.A.FailWrite = false; f.Advance(2); f.C.Poll();
                Assert(f.A.Value == original && !f.C.HasPending, "Failed release retried on reconnect");
            }
            using (var f = new Fixture(original)) {
                f.Journal.FailSave = true; f.C.BeginHold();
                Assert(!f.C.HoldActive && !f.C.HasPending && f.A.Writes == 0 && f.A.Value == original, "Unwritable journal prevents Hold");
            }
            using (var f = new Fixture(original)) {
                f.A.FailAfterWrite = true; f.C.BeginHold();
                Assert(!f.C.HoldActive && f.A.Value == original && !f.C.HasPending, "Ambiguous driver failure restored");
            }
            using (var f = new Fixture(original)) {
                f.A.IgnoreWrite = true; f.C.BeginHold();
                Assert(!f.C.Ready && !f.C.HoldActive && f.A.Value == original, "Unconfirmed mute not presented as success");
            }
            using (var f = new Fixture(original)) {
                f.C.BeginHold(); f.Journal.FailClear = true; f.C.ReleaseHold();
                Assert(f.C.HasPending && f.A.Value == original, "Clear failure keeps recovery while restoring physical state");
                f.Journal.FailClear = false; f.Advance(2); f.C.Poll();
                Assert(!f.C.HasPending && f.C.Ready && f.A.Value == original, "Locked journal can recover");
            }
            using (var f = new Fixture(original)) {
                f.C.BeginHold(); f.S.HoldEnabled = false; f.C.RulesChanged();
                Assert(!f.C.HoldActive && f.A.Value == original, "Disabling Hold restores immediately");
                f.S.HoldEnabled = true; f.C.BeginHold(); f.C.Suspend();
                Assert(!f.C.Ready && !f.C.HoldActive && f.A.Value == original, "Suspend restores before disconnect");
            }
            using (var f = new Fixture(original)) {
                f.C.BeginHold(); f.C.SelectDevice(new DeviceInfo { Id = "B", Name = "B" });
                Assert(f.A.Value == original && f.S.DeviceId == "B" && !f.C.HasPending, "Switch restores old device before changing target");
                Assert(f.A.WrittenIds.TrueForAll(delegate(string s) { return s == "A"; }), "Switch never restores old state onto new device");
            }
            using (var f = new Fixture(original)) {
                f.C.BeginHold(); f.C.Dispose();
                Assert(f.A.Value == original && f.Journal.Value == null, "Normal exit restores Hold");
            }
            var settings = new Settings { DeviceId = "B" };
            var audio = new FakeAudio { Value = !original };
            var journal = new MemoryJournal { Value = new PendingRestore { DeviceId = "A", DeviceName = "Original", Muted = original } };
            using (var c = new Controller(settings, audio, new Clock(), journal)) {
                Assert(settings.DeviceId == "A", "Crash recovery overrides an older saved selection");
                c.Poll(); Assert(c.Ready && audio.Value == original && !c.HasPending, "Recovery survives process restart");
            }
        }
        using (var f = new Fixture(false)) {
            f.S.ToggleEnabled = false; f.Router.Tick(true, false, true, false);
            Assert(f.A.Writes == 0, "Disabled hotkey does not toggle");
            f.A.Value = true; f.C.Poll(); Assert(f.C.Muted == true && f.C.View.Visible, "Monitoring remains active with hotkey disabled");
            f.C.Toggle(); Assert(!f.A.Value, "Manual button works independently of hotkey enable");
        }
        using (var f = new Fixture(false)) {
            f.Router.Sync(true, true); f.Router.Tick(true, true, true, true);
            Assert(f.A.Writes == 0, "Keys held during capture/startup suppressed, even with repeat");
            f.Router.Tick(false, false, false, false); f.Router.Tick(false, true, false, true);
            Assert(f.C.HoldActive, "Hold rearms after full release");
        }
    }
    private static void PreviewCases() {
        foreach (double seconds in new[] { 0.2, 1.7, 10.0 }) {
            using (var f = new Fixture(true)) {
                f.S.Normal = NormalRule.KeepMuted; f.S.PopupSeconds = seconds; f.C.RulesChanged(); f.C.Preview(false);
                Assert(f.C.View.Visible && !f.C.View.Muted, "Preview overrides persistent state");
                f.Advance(seconds); Assert(f.C.View.Visible && f.C.View.Muted, "Preview duration restores persistent state");
                f.C.Preview(true); f.C.BeginHold(); f.Advance(seconds);
                Assert(f.C.HoldActive && f.C.View.Visible && !f.C.View.Muted, "Preview expiry reveals current Hold");
                f.C.ReleaseHold();
                f.C.BeginPlacement(); f.C.Preview(false); f.Advance(100);
                Assert(f.C.View.Visible && f.C.View.Placement && f.C.View.Muted, "Placement ignores preview duration");
                f.C.EndPlacement(); Assert(f.C.View.Visible && f.C.View.Muted, "Finish placement restores live persistent layer");
            }
        }
        using (var f = new Fixture(false)) {
            f.C.Toggle(); f.C.BeginPlacement(); f.Advance(10); f.C.EndPlacement();
            Assert(!f.C.View.Visible, "Placement does not revive expired popup");
            f.C.Preview(true); f.A.Online = false; f.C.Poll(); f.Advance(10);
            Assert(!f.C.View.Visible, "Unknown device state hidden after explicit preview expires");
        }
    }
    private static void RandomSequences() {
        var random = new Random(637113);
        using (var f = new Fixture(false)) {
            for (int i = 0; i < 20000; i++) {
                int action = random.Next(9);
                bool before = f.A.Value, wasHold = f.C.HoldActive;
                switch (action) {
                    case 0: f.C.Toggle(); if (f.C.Ready && !wasHold) Assert(f.A.Value == !before, "Random normal Toggle inverts"); break;
                    case 1: f.C.BeginHold(); break;
                    case 2: f.C.ReleaseHold(); break;
                    case 3: f.A.Value = !f.A.Value; f.C.Poll(); break;
                    case 4: f.Advance(random.NextDouble() * 3); break;
                    case 5: f.S.Normal = (NormalRule)random.Next(5); f.S.DuringHold = (HoldRule)random.Next(3); f.C.RulesChanged(); break;
                    case 6: f.C.Preview(random.Next(2) == 1); break;
                    case 7: f.C.BeginPlacement(); f.C.EndPlacement(); break;
                    case 8: f.C.Suspend(); f.Advance(2); f.C.Poll(); break;
                }
                Assert(!f.C.HoldActive || f.C.HasPending && f.A.Value == !f.C.Pending.Muted, "Random Hold has exact inverse and recovery");
                Assert(f.C.HoldActive || !f.C.HasPending, "Random connected state has no stale recovery");
                Assert(!f.C.Ready || f.C.Muted == f.A.Value, "Random known state matches driver");
            }
        }
    }
    private static void StorageAndInterop() {
        string directory = Path.Combine(Path.GetTempPath(), "apeiron-global-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string path = Path.Combine(directory, "settings.xml");
            var s = new Settings { DeviceId = "{opaque:id}", DeviceName = "Microphone 名称 < & >", PopupSeconds = 1.7, X = -1600 };
            XmlFiles.Write(path, s); var loaded = XmlFiles.Read<Settings>(path); loaded.Normalize();
            Assert(loaded.DeviceId == s.DeviceId && loaded.DeviceName == s.DeviceName && loaded.PopupSeconds == 1.7 && loaded.X == -1600, "Settings round-trip Unicode and fractions");
            s.PopupSeconds = 99; s.HoldKey = s.ToggleKey; s.IconColor = "Red"; XmlFiles.Write(path, s);
            loaded = XmlFiles.Read<Settings>(path); loaded.Normalize();
            Assert(loaded.PopupSeconds == 10 && !loaded.HoldEnabled && loaded.IconColor == "#FFFFFF", "Invalid settings normalized");
            Assert(File.Exists(path + ".bak"), "Atomic save keeps backup");
            var journal = new RestoreStore(Path.Combine(directory, "hold.xml"));
            journal.Save(new PendingRestore { DeviceId = "A", Muted = true });
            Assert(journal.Load().DeviceId == "A" && journal.Load().Muted, "Journal round-trip");
            journal.Clear(); Assert(journal.Load() == null, "Journal removed after restoration");
            File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Settings>&x;</Settings>");
            bool failed = false; try { XmlFiles.Read<Settings>(path); } catch { failed = true; }
            Assert(failed, "External entities disabled");
        } finally { Directory.Delete(directory, true); }
        Assembly production = typeof(Controller).Assembly;
        // Reference values verified against Microsoft's Core Audio interop definitions:
        // https://github.com/microsoft/psi/blob/master/Sources/Audio/Microsoft.Psi.Audio.Windows/ComInterop/Guids.cs
        var expectedIds = new Dictionary<string, string> {
            { "MMDeviceEnumerator", "BCDE0395-E52F-467C-8E3D-C4579291692E" },
            { "IMMDeviceEnumerator", "A95664D2-9614-4F35-A746-DE8DB63617E6" },
            { "IMMDeviceCollection", "0BD7A1BE-7A1A-44DB-8397-CC5392387B5E" },
            { "IMMDevice", "D666063F-1587-4E43-81F1-B948E807363F" },
            { "IMMEndpoint", "1BE09788-6894-4089-8586-9A2A6C265AC5" },
            { "IPropertyStore", "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99" },
            { "IAudioEndpointVolume", "5CDF2C82-841E-4546-9722-0CF74078229A" }
        };
        foreach (var pair in expectedIds) {
            Type type = production.GetType("Apeiron.GlobalMute." + pair.Key);
            Assert(type.GUID == new Guid(pair.Value), "Windows interface identity: " + pair.Key);
            Assert(type.IsImport, "Native COM import: " + pair.Key);
        }
        var collection = production.GetType("Apeiron.GlobalMute.IMMDeviceCollection");
        Assert(collection.GetMethods().Length == 2 && collection.GetMethods()[0].Name == "GetCount" &&
            collection.GetMethods()[1].Name == "Item", "Collection COM vtable order");
        foreach (var pair in expectedIds) {
            Type type = production.GetType("Apeiron.GlobalMute." + pair.Key);
            if (!type.IsInterface) continue;
            Assert(((InterfaceTypeAttribute)Attribute.GetCustomAttribute(type, typeof(InterfaceTypeAttribute))).Value ==
                ComInterfaceType.InterfaceIsIUnknown, "IUnknown interface type: " + pair.Key);
            foreach (var method in type.GetMethods()) {
                Assert((method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0,
                    "HRESULT preserved: " + pair.Key + "." + method.Name);
            }
        }
        Assert(Marshal.SizeOf(production.GetType("Apeiron.GlobalMute.PropVariant")) == 24, "x64 PROPVARIANT size");
        Assert(Marshal.SizeOf(production.GetType("Apeiron.GlobalMute.PropertyKey")) == 20, "PROPERTYKEY size");
        var endpoint = production.GetType("Apeiron.GlobalMute.IAudioEndpointVolume");
        Assert(endpoint.GUID.ToString() == "5cdf2c82-841e-4546-9722-0cf74078229a", "Endpoint volume IID");
        Assert(endpoint.GetMethods().Length == 18, "Complete endpoint volume vtable");
        Assert(endpoint.GetMethods()[11].Name == "SetMute" && endpoint.GetMethods()[12].Name == "GetMute", "Correct mute vtable slots");
        using (var stream = production.GetManifestResourceStream("APEIRON.ico")) Assert(stream != null && stream.Length > 0, "App icon embedded");
        using (var stream = production.GetManifestResourceStream("APEIRON_icon.png")) Assert(stream != null && stream.Length > 0, "About image embedded");
    }
    private static void ImmediateInputCases() {
        var clock = new Clock(); int wakeups = 0;
        var mailbox = new InputMailbox(clock);
        mailbox.Configure(120, 121, false, false, delegate { wakeups++; });
        mailbox.Record(121, true);
        Assert(wakeups == 1, "Hook edge wakes consumer without waiting for a UI timer");
        var value = mailbox.Take(true, false, false);
        Assert(value.HoldDown && value.HoldPressed, "Fresh hook state survives stale GetAsyncKeyState");
        mailbox.Record(121, true); Assert(wakeups == 1, "Auto-repeat does not enqueue another press");
        clock.Time = 0.04; value = mailbox.Take(true, false, false);
        Assert(value.HoldDown && !value.HoldPressed, "No false release during async-state grace interval");
        clock.Time = 0.081; value = mailbox.Take(true, false, false);
        Assert(!value.HoldDown && value.HasEvent, "Missed release repaired by periodic physical-state check");
        clock.Time = 0.2; mailbox.Record(121, true); mailbox.Record(121, false);
        value = mailbox.Take(true, false, true);
        Assert(!value.HoldDown && value.HoldPressed, "Short tap retained and stale physical down cannot latch Hold");
        Assert(!mailbox.Take(false, false, false).HoldPressed, "Press consumed once");
        mailbox.Configure(120, 121, false, false, delegate { wakeups++; });
        mailbox.Record(120, true); clock.Time += 0.007;
        value = mailbox.Take(false, false, false);
        Assert(value.HasEvent && Math.Abs(value.QueueMilliseconds - 7) < 0.0001, "Diagnostic queue latency measures actual event age");
        mailbox.Configure(120, 121, true, true, delegate { wakeups++; });
        value = mailbox.Take(false, false, false);
        Assert(value.ToggleDown && value.HoldDown && !value.TogglePressed && !value.HoldPressed && !value.HasEvent,
            "Binding capture seeds held keys without creating actions");
        mailbox.Record(100, true); value = mailbox.Take(false, false, false);
        Assert(!value.HasEvent && !value.TogglePressed && !value.HoldPressed, "Unassigned key does not wake control processing");
        using (var f = new Fixture(false)) {
            var box = new InputMailbox(f.Clock);
            box.Configure(120, 121, false, false, delegate { });
            box.Record(121, true); value = box.Take(true, false, false);
            f.Router.Tick(value.ToggleDown, value.HoldDown, value.TogglePressed, value.HoldPressed);
            Assert(f.C.HoldActive && f.A.Value, "Immediate hook delivery starts Hold despite not-yet-updated Windows key state");
            box.Record(121, false); value = box.Take(true, false, true);
            f.Router.Tick(value.ToggleDown, value.HoldDown, value.TogglePressed, value.HoldPressed);
            Assert(!f.C.HoldActive && !f.A.Value && !f.C.HasPending, "Immediate release restores without waiting for timer");
            box.Record(121, true); box.Record(121, false); value = box.Take(false, false, false);
            f.Router.Tick(value.ToggleDown, value.HoldDown, value.TogglePressed, value.HoldPressed);
            Assert(!f.C.HoldActive && !f.A.Value && !f.C.HasPending, "Coalesced complete tap restores correctly");
            box.Record(120, true); box.Record(121, true); value = box.Take(false, false, false);
            f.Router.Tick(value.ToggleDown, value.HoldDown, value.TogglePressed, value.HoldPressed);
            Assert(f.C.HoldActive && f.A.Value && f.C.Pending.Muted == false, "Hook batching preserves Hold priority");
        }
        using (var f = new Fixture(false)) {
            f.A.Reads = 0; f.A.Validations = 0; f.C.Toggle();
            Assert(f.A.Reads == 2 && f.A.Validations == 0, "Toggle reads and confirms without redundant endpoint validation");
            Assert(f.C.LastTiming.Action == "Toggle" && f.C.LastTiming.TotalMilliseconds >= 0, "Toggle timing recorded");
            f.A.Reads = 0; f.C.BeginHold();
            Assert(f.A.Reads == 2 && f.A.Validations == 0, "Hold press reads and confirms without repeated health queries");
            Assert(f.C.LastTiming.Action == "Hold press" && f.C.LastTiming.JournalMilliseconds >= 0, "Hold journal and audio timing recorded");
            f.C.ReleaseHold(); Assert(f.C.LastTiming.Action == "Hold release", "Release timing recorded");
            f.C.Poll(); Assert(f.A.Validations == 1, "Periodic monitoring still validates endpoint availability");
        }
    }
    private static void OptionalRecoveryCases() {
        Assert(!new Settings().FileRecoveryEnabled && new Settings().OfferAdministratorRestart, "New defaults: memory recovery and admin notice enabled");
        foreach (bool original in new[] { false, true })
        foreach (bool file in new[] { false, true })
        foreach (NormalRule normal in Enum.GetValues(typeof(NormalRule)))
        foreach (HoldRule hold in Enum.GetValues(typeof(HoldRule)))
        foreach (double seconds in new[] { 0.2, 1.7 }) {
            scenarios++;
            using (var f = new Fixture(original, file)) {
                f.S.Normal = normal; f.S.DuringHold = hold; f.S.PopupSeconds = seconds;
                f.C.BeginHold();
                Assert(f.C.HoldActive && f.A.Value == !original && f.C.HasPending, "Optional recovery keeps Hold behavior");
                Assert(f.Journal.Saves == (file ? 1 : 0) && f.Recovery.HasPersistedRecovery == file, "Only enabled file recovery writes at Hold press");
                f.Advance(seconds); f.C.ReleaseHold();
                Assert(f.A.Value == original && !f.C.HasPending && f.Recovery.CanRestartSafely, "Optional recovery restores before release completes");
                Assert(f.Journal.Clears == (file ? 1 : 0) && f.Journal.Loads == 1, "Memory Hold has no disk reads/writes/deletes after startup");
                f.C.BeginHold(); f.A.Online = false; f.C.Poll();
                Assert(f.C.HasPending && !f.C.Ready && f.Recovery.CanRestartSafely == file, "Restart protection matches retained recovery");
                f.A.Online = true; f.Advance(2); f.C.Poll();
                Assert(f.A.Value == original && !f.C.HasPending, "Disconnect/reconnect works in either recovery mode");
            }
        }
        using (var f = new Fixture(false, false)) {
            for (int i = 0; i < 200; i++) { f.C.BeginHold(); f.C.ReleaseHold(); }
            Assert(f.Journal.Loads == 1 && f.Journal.Saves == 0 && f.Journal.Clears == 0, "200 memory-only Hold cycles perform zero disk-store operations");
            f.C.BeginHold(); f.Recovery.SetEnabled(true);
            Assert(f.C.HoldActive && f.A.Value && f.Recovery.Enabled && f.Journal.Value.Muted == false, "Enable file mode during Hold persists the correct original state");
            f.Recovery.SetEnabled(false);
            Assert(f.C.HoldActive && f.A.Value && !f.Recovery.Enabled && !f.Recovery.HasPersistedRecovery && f.C.HasPending, "Disable file mode keeps active in-memory Hold");
            int saves = f.Journal.Saves, clears = f.Journal.Clears;
            f.C.ReleaseHold();
            Assert(!f.A.Value && f.Journal.Saves == saves && f.Journal.Clears == clears, "Release after disabling does not touch disk");
        }
        using (var f = new Fixture(true, false)) {
            f.C.BeginHold(); f.Journal.FailSave = true;
            bool failed = false; try { f.Recovery.SetEnabled(true); } catch (IOException) { failed = true; }
            Assert(failed && !f.Recovery.Enabled && f.C.HoldActive && !f.A.Value && f.C.HasPending, "Failed opt-in preserves memory mode and Hold");
            f.Journal.FailSave = false; f.C.ReleaseHold(); Assert(f.A.Value, "Release succeeds after failed file opt-in");
        }
        using (var f = new Fixture(false, true)) {
            f.C.BeginHold(); f.Journal.FailClear = true;
            bool failed = false; try { f.Recovery.SetEnabled(false); } catch (IOException) { failed = true; }
            Assert(failed && f.Recovery.Enabled && f.Recovery.HasPersistedRecovery && f.C.HoldActive, "Failed opt-out retains the file and previous mode");
            f.Journal.FailClear = false; f.C.ReleaseHold(); Assert(!f.A.Value, "Release after failed file opt-out");
        }
        var disk = new MemoryJournal { Value = new PendingRestore { DeviceId = "A", DeviceName = "Old device", Muted = true } };
        var optional = new OptionalRestoreStore(disk, false);
        var s = new Settings { DeviceId = "B" }; var a = new FakeAudio { Value = false };
        using (var c = new Controller(s, a, new Clock(), optional)) {
            Assert(c.HasPending && s.DeviceId == "A" && optional.HasPersistedRecovery, "Upgrade keeps an old pending file even with new default off");
            c.Poll(); Assert(c.Ready && a.Value && !c.HasPending && disk.Clears == 1, "Upgrade recovery completes on its exact old device");
            c.BeginHold(); Assert(disk.Saves == 0, "New Holds use the selected memory-only default after legacy recovery");
        }
        string directory = Path.Combine(Path.GetTempPath(), "apeiron-optional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string path = Path.Combine(directory, "recovery.xml");
            var store = new OptionalRestoreStore(new RestoreStore(path), false);
            var settings = new Settings { DeviceId = "A", DeviceName = "Device" };
            var audio = new FakeAudio();
            using (var c = new Controller(settings, audio, new Clock(), store)) { c.Poll(); c.BeginHold(); c.ReleaseHold(); }
            Assert(Directory.GetFiles(directory).Length == 0, "Real memory-only recovery creates no files or temporary files");
            store.SetEnabled(true); store.Save(new PendingRestore { DeviceId = "A", Muted = true });
            var reopened = new OptionalRestoreStore(new RestoreStore(path), false);
            Assert(reopened.Load().Muted && reopened.HasPersistedRecovery, "Enabled file survives recreation with option off");
            reopened.Clear(); Assert(Directory.GetFiles(directory).Length == 0, "Recovered file removed cleanly");
            string config = Path.Combine(directory, "settings.xml");
            File.WriteAllText(config, "<Settings><SchemaVersion>1</SchemaVersion><DeviceId>A</DeviceId><ToggleKey>119</ToggleKey><HoldKey>192</HoldKey></Settings>");
            var loaded = XmlFiles.Read<Settings>(config); loaded.Normalize();
            Assert(loaded.SchemaVersion == 3 && !loaded.FileRecoveryEnabled && loaded.OfferAdministratorRestart && loaded.HoldKey == 192 && loaded.ToggleKey == 119,
                "Older settings keep custom keys and adopt the new optional defaults");
            loaded.FileRecoveryEnabled = true; loaded.OfferAdministratorRestart = false; XmlFiles.Write(config, loaded);
            loaded = XmlFiles.Read<Settings>(config);
            Assert(loaded.FileRecoveryEnabled && !loaded.OfferAdministratorRestart, "Both new options persist correctly");
        } finally { Directory.Delete(directory, true); }
    }
    private static void ElevationNoticeCases() {
        foreach (bool? own in new bool?[] { null, false, true })
        foreach (bool? foreground in new bool?[] { null, false, true }) {
            var notice = new ElevationNotice(own); notice.Observe(123, "Game", foreground);
            bool expected = own == false && foreground == true;
            Assert(notice.Mismatch == expected, "Only a confirmed admin mismatch triggers a banner");
            Assert(notice.ShouldAsk(true, false) == expected, "Prompt follows actual privilege mismatch");
            Assert(!notice.ShouldAsk(false, false) && !notice.ShouldAsk(true, true), "Disabled or busy prompts suppressed");
            Assert(notice.ShouldAskAtStartup(true, false) == (own != true), "Startup question does not require an elevated foreground app");
            Assert(!notice.ShouldAskAtStartup(false, false) && !notice.ShouldAskAtStartup(true, true), "Startup respects disabled/busy states");
        }
        var state = new ElevationNotice(false); state.Observe(123, "Game", true);
        Assert(state.ShouldAsk(true, false), "First elevated game offers restart");
        state.MarkAsked(); Assert(state.Mismatch && !state.ShouldAsk(true, false), "Declining keeps warning without repeated prompts");
        Assert(!state.ShouldAskAtStartup(true, false), "Startup question also obeys the once-per-session limit");
        state.Observe(456, "Another game", true); Assert(!state.ShouldAsk(true, false), "Once-per-session prompt limit across apps");
        state.Observe(0, "Overlay", false); Assert(state.ProcessName == "Another game" && state.Mismatch, "Opening the overlay keeps the actionable mismatch notice");
        state.Observe(789, "Browser", false); Assert(!state.Mismatch, "Known normal foreground clears warning");
        state.Observe(777, "Protected game", null); Assert(!state.Mismatch, "Inaccessible token stays unknown, not guessed elevated");
    }
    private static void SuppressionCases() {
        Assert(new Settings().BlockHoldKey && !new Settings().BlockToggleKey, "Hold blocking defaults on; Toggle blocking remains optional");
        var gate = new KeySuppression();
        gate.Configure(120, 192, true, true, false, true, false, false);
        Assert(gate.Process(192, true, false), "Grave key down blocked outside settings");
        Assert(gate.Process(192, true, false) && gate.IsBlockedDown(192), "Grave repeats stay blocked and retain the actual held state");
        Assert(gate.Process(192, false, false) && !gate.IsBlockedDown(192), "Grave key up also blocked and releases captured state");
        Assert(!gate.Process(120, true, false) && !gate.Process(120, false, false), "Toggle passes through when its option is off");
        Assert(!gate.Process(65, true, false) && !gate.Process(65, false, false), "Unassigned keys unaffected");
        Assert(!gate.Process(192, true, true), "Settings/capture context receives its own key down");
        Assert(!gate.Process(192, false, false), "A down passed in settings never gets an unmatched blocked up after focus change");
        Assert(gate.Process(192, true, false), "Next external press is blocked");
        gate.Configure(119, 191, false, false, false, false, false, false);
        Assert(gate.Process(192, true, true) && gate.Process(192, false, true), "Old blocked press stays balanced across rebind, disable and focus change");
        Assert(!gate.Process(192, true, false) && !gate.Process(192, false, false), "Old key is normal after its original release");
        gate.Configure(120, 192, true, true, true, true, false, true);
        Assert(!gate.Process(192, true, false) && !gate.Process(192, false, false), "Key held before enabling capture is not partially swallowed");
        Assert(gate.Process(192, true, false) && gate.Process(192, false, false), "Blocking arms only after that full release");
        foreach (int mouse in new[] { 1, 2, 4 }) {
            gate.Configure(120, mouse, true, true, true, true, false, false);
            Assert(!gate.Process(mouse, true, false) && !gate.Process(mouse, false, false), "Primary mouse clicks never blocked");
        }
        foreach (int mouse in new[] { 5, 6 }) {
            gate.Configure(120, mouse, true, true, true, true, false, false);
            Assert(gate.Process(mouse, true, false) && gate.Process(mouse, false, false), "Side mouse buttons can be reserved");
        }
        gate.Configure(120, 192, false, false, true, true, false, false);
        Assert(!gate.Process(192, true, false) && !gate.Process(192, false, false), "Disabled Hold never blocks a new press");
        gate.Configure(120, 192, true, true, true, true, false, false);
        Assert(gate.Process(120, true, false) && gate.Process(120, false, false), "Toggle blocking works when enabled");
        var clock = new Clock(); var mailbox = new InputMailbox(clock);
        gate.Configure(120, 192, true, true, false, true, false, false);
        mailbox.Configure(120, 192, false, false, delegate { });
        using (var f = new Fixture(false, false)) {
            bool block = gate.Process(192, true, false); mailbox.Record(192, true);
            var snapshot = mailbox.Take(true, false, gate.IsBlockedDown(192));
            f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
            Assert(block && f.C.HoldActive && f.A.Value, "Blocking still delivers the press to Hold");
            clock.Time = 3;
            snapshot = mailbox.Take(true, false, gate.IsBlockedDown(192));
            f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
            Assert(f.C.HoldActive && f.A.Value, "Suppressed key remains held beyond physical-state fallback even if OS async state is up");
            block = gate.Process(192, false, false); mailbox.Record(192, false);
            snapshot = mailbox.Take(true, false, gate.IsBlockedDown(192));
            f.Router.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
            Assert(block && !f.C.HoldActive && !f.A.Value && !f.C.HasPending, "Blocked release restores the mic correctly");
        }
        string path = Path.Combine(Path.GetTempPath(), "apeiron-keys-" + Guid.NewGuid().ToString("N") + ".xml");
        try {
            File.WriteAllText(path, "<Settings><SchemaVersion>2</SchemaVersion><HoldKey>192</HoldKey><OfferAdministratorRestart>true</OfferAdministratorRestart><FileRecoveryEnabled>false</FileRecoveryEnabled></Settings>");
            var settings = XmlFiles.Read<Settings>(path); settings.Normalize();
            Assert(settings.HoldKey == 192 && settings.BlockHoldKey && !settings.FileRecoveryEnabled && settings.OfferAdministratorRestart && settings.SchemaVersion == 3,
                "Build-4 settings upgrade preserves grave binding and enables Hold blocking");
            settings.BlockHoldKey = false; settings.BlockToggleKey = true; XmlFiles.Write(path, settings);
            settings = XmlFiles.Read<Settings>(path); Assert(!settings.BlockHoldKey && settings.BlockToggleKey, "Both blocking preferences persist");
        } finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
    }
    private static void PauseCases() {
        foreach (bool original in new[] { false, true })
        foreach (bool fileMode in new[] { false, true })
        foreach (bool toggleOn in new[] { false, true })
        foreach (bool holdOn in new[] { false, true })
        foreach (bool blockToggle in new[] { false, true })
        foreach (bool blockHold in new[] { false, true }) {
            scenarios++;
            using (var f = new Fixture(original, fileMode)) {
                f.S.ToggleEnabled = toggleOn; f.S.HoldEnabled = holdOn;
                f.S.BlockToggleKey = blockToggle; f.S.BlockHoldKey = blockHold;
                f.Router.Tick(false, true, false, true);
                Assert(f.C.HoldActive == holdOn, "Hold enable preference applies before pause");
                f.Router.SetPaused(true, false, true);
                Assert(f.Router.Paused && !f.Router.ToggleEnabled && !f.Router.HoldEnabled, "Pause disables both effective hotkeys");
                Assert(!f.C.HoldActive && !f.C.HasPending && f.A.Value == original, "Pause immediately restores either original Hold state");
                Assert(f.S.ToggleEnabled == toggleOn && f.S.HoldEnabled == holdOn &&
                    f.S.BlockToggleKey == blockToggle && f.S.BlockHoldKey == blockHold, "Pause preserves all four configured choices");
                int writes = f.A.Writes, saves = f.Journal.Saves, clears = f.Journal.Clears;
                for (int i = 0; i < 5; i++) {
                    f.Router.Tick(true, true, true, true); f.Router.Tick(false, false, false, false);
                    f.Router.Tick(false, false, true, true); // Coalesced taps queued while paused.
                }
                f.Router.SetPaused(true, false, false);
                Assert(f.A.Writes == writes && f.Journal.Saves == saves && f.Journal.Clears == clears,
                    "Paused presses, repeats, queued taps and repeated pause do no audio or recovery writes");
                f.A.Value = !original; f.C.Poll();
                Assert(f.C.Muted == !original && f.C.View.Visible, "External mute monitoring and overlay continue during pause");
                f.C.Toggle(); Assert(f.A.Value == original, "Manual mute control remains available during pause");
                writes = f.A.Writes;
                f.Router.SetPaused(false, true, true);
                f.Router.Tick(true, true, true, true);
                Assert(!f.Router.Paused && f.A.Writes == writes, "Resume ignores keys already held, including pending repeats");
                Assert(f.Router.ToggleEnabled == toggleOn && f.Router.HoldEnabled == holdOn, "Resume honors each enable preference");
                f.Router.Tick(false, false, false, false);
                f.Router.Tick(true, false, true, false);
                bool normal = toggleOn ? !original : original;
                Assert(f.A.Value == normal, "Only enabled Toggle responds to a fresh post-resume press");
                f.Router.Tick(false, false, false, false);
                f.Router.Tick(false, true, false, true);
                Assert(f.C.HoldActive == holdOn && f.A.Value == (holdOn ? !normal : normal), "Only enabled Hold responds after resume");
                f.Router.Tick(false, false, false, false);
                Assert(!f.C.HoldActive && f.A.Value == normal && !f.C.HasPending, "Post-resume Hold restores normally");
            }
        }
        foreach (bool original in new[] { false, true }) {
            using (var f = new Fixture(original, false)) {
                f.Router.Tick(false, true, false, true); f.A.Online = false;
                f.Router.SetPaused(true, false, true);
                Assert(f.Router.Paused && !f.C.HoldActive && f.C.HasPending && f.C.Pending.DeviceId == "A",
                    "A disconnected microphone retains exact-device recovery when pausing");
                f.A.Online = true; f.Advance(2); f.C.Poll();
                Assert(f.Router.Paused && f.C.Ready && !f.C.HasPending && f.A.Value == original,
                    "Reconnect completes recovery without resuming hotkeys");
            }
            using (var f = new Fixture(original, true)) {
                f.C.BeginHold(); f.A.FailWrite = true;
                f.Router.SetPaused(true, false, true);
                Assert(f.Router.Paused && !f.C.HoldActive && f.C.HasPending && f.Recovery.HasPersistedRecovery,
                    "A failed pause-time restore retains its file recovery");
                f.A.FailWrite = false; f.Advance(2); f.C.Poll();
                Assert(f.A.Value == original && !f.C.HasPending && f.Router.Paused, "Pause-time restore failure retries while paused");
            }
        }
        using (var f = new Fixture(false, false)) {
            f.S.HoldKey = 192; f.S.BlockToggleKey = true;
            var gate = new KeySuppression();
            gate.Configure(120, 192, f.Router.ToggleEnabled, f.Router.HoldEnabled, true, true, false, false);
            Assert(gate.Process(192, true, false), "A pre-pause Hold press is blocked");
            f.Router.Tick(false, true, false, true);
            f.Router.SetPaused(true, false, true);
            gate.Configure(120, 192, f.Router.ToggleEnabled, f.Router.HoldEnabled, true, true, false, true);
            Assert(!f.A.Value && gate.Process(192, true, false) && gate.Process(192, false, false),
                "Pause restores the mic while preserving the original press's balanced routing");
            Assert(!gate.Process(120, true, false) && !gate.Process(120, false, false), "Paused Toggle passes through despite its saved blocking choice");
            Assert(!gate.Process(192, true, false), "A new paused Hold press passes through despite its saved blocking choice");
            f.Router.SetPaused(false, false, true);
            gate.Configure(120, 192, f.Router.ToggleEnabled, f.Router.HoldEnabled, true, true, false, true);
            f.Router.Tick(false, true, false, true);
            Assert(!f.C.HoldActive && !gate.Process(192, true, false) && !gate.Process(192, false, false),
                "Resume preserves a paused pass-through press until release, without starting Hold");
            f.Router.Tick(false, false, false, false);
            Assert(gate.Process(192, true, false), "The next post-resume press uses the saved blocking preference");
            f.Router.Tick(false, true, false, true);
            f.S.BlockHoldKey = false;
            gate.Configure(120, 192, f.Router.ToggleEnabled, f.Router.HoldEnabled, true, false, false, true);
            Assert(f.C.HoldActive && gate.Process(192, false, false), "Turning off blocking keeps an active Hold and balances its release");
            f.Router.Tick(false, false, false, false);
            Assert(!gate.Process(192, true, false), "The next press reflects the new blocking choice");
            f.Router.Tick(false, true, false, true);
            Assert(f.C.HoldActive && f.A.Value, "Turning off blocking leaves the Hold action enabled");
            gate.Process(192, false, false); f.Router.Tick(false, false, false, false);
        }
    }
    private static void ConfigureBlocking(Fixture f, KeySuppression gate, bool toggleDown, bool holdDown) {
        gate.Configure(f.S.ToggleKey, f.S.HoldKey, f.Router.ToggleEnabled, f.Router.HoldEnabled,
            f.Router.BlockToggleKey, f.Router.BlockHoldKey, toggleDown, holdDown);
    }
    private static void MasterBlockingCases() {
        foreach (bool original in new[] { false, true })
        foreach (bool toggleOn in new[] { false, true })
        foreach (bool holdOn in new[] { false, true })
        foreach (bool toggleBlock in new[] { false, true })
        foreach (bool holdBlock in new[] { false, true })
        foreach (bool paused in new[] { false, true })
        foreach (bool master in new[] { false, true }) {
            scenarios++;
            using (var f = new Fixture(original, false)) {
                f.S.HoldKey = 192; f.S.ToggleEnabled = toggleOn; f.S.HoldEnabled = holdOn;
                f.S.BlockToggleKey = toggleBlock; f.S.BlockHoldKey = holdBlock;
                f.Router.KeyBlockingEnabled = master; f.Router.SetPaused(paused, false, false);
                var gate = new KeySuppression(); ConfigureBlocking(f, gate, false, false);
                bool consumeToggle = !paused && master && toggleOn && toggleBlock;
                bool consumeHold = !paused && master && holdOn && holdBlock;
                Assert(gate.Process(120, true, false) == consumeToggle, "Global blocking routes a fresh Toggle press using the saved individual choice");
                f.Router.Tick(true, false, true, false);
                bool normal = !paused && toggleOn ? !original : original;
                Assert(f.A.Value == normal, "Global blocking never disables the Toggle action");
                int writes = f.A.Writes;
                gate.Process(120, true, false); f.Router.Tick(true, false, false, false);
                Assert(f.A.Writes == writes && gate.Process(120, false, false) == consumeToggle,
                    "Toggle repeats do not retrigger audio, and its release has matching routing");
                f.Router.Tick(false, false, false, false);
                Assert(gate.Process(192, true, false) == consumeHold, "Global blocking routes a fresh Hold press independently");
                f.Router.Tick(false, true, false, true);
                bool active = !paused && holdOn;
                Assert(f.C.HoldActive == active && f.A.Value == (active ? !normal : normal), "Global blocking never disables the Hold action");
                Assert(gate.Process(192, false, false) == consumeHold, "Hold release remains paired with its down");
                f.Router.Tick(false, false, false, false);
                Assert(!f.C.HoldActive && !f.C.HasPending && f.A.Value == normal, "Hold restores while master blocking is on or off");
                writes = f.A.Writes; int saves = f.Journal.Saves, clears = f.Journal.Clears;
                f.Router.KeyBlockingEnabled = !master; ConfigureBlocking(f, gate, false, false);
                Assert(gate.Process(120, true, false) == (!paused && !master && toggleOn && toggleBlock) &&
                    gate.Process(120, false, false) == (!paused && !master && toggleOn && toggleBlock), "Master switch updates Toggle routing for the next press");
                Assert(gate.Process(192, true, false) == (!paused && !master && holdOn && holdBlock) &&
                    gate.Process(192, false, false) == (!paused && !master && holdOn && holdBlock), "Master switch updates Hold routing for the next press");
                Assert(f.A.Writes == writes && f.Journal.Saves == saves && f.Journal.Clears == clears, "Changing master blocking performs no audio or recovery writes");
                Assert(f.S.BlockToggleKey == toggleBlock && f.S.BlockHoldKey == holdBlock &&
                    f.S.ToggleEnabled == toggleOn && f.S.HoldEnabled == holdOn, "Master and pause retain all individual preferences");
            }
        }
        foreach (bool original in new[] { false, true })
        foreach (bool blockBefore in new[] { false, true }) {
            using (var f = new Fixture(original, false)) {
                f.S.HoldKey = 192; f.S.BlockToggleKey = true;
                f.Router.KeyBlockingEnabled = blockBefore;
                var gate = new KeySuppression(); ConfigureBlocking(f, gate, false, false);
                Assert(gate.Process(192, true, false) == blockBefore, "Initial Hold uses the current master routing");
                f.Router.Tick(false, true, false, true);
                int writes = f.A.Writes, saves = f.Journal.Saves, clears = f.Journal.Clears;
                f.Router.KeyBlockingEnabled = !blockBefore; f.Router.Sync(false, true);
                ConfigureBlocking(f, gate, false, true); f.Router.Tick(false, true, false, false);
                Assert(f.C.HoldActive && f.A.Value == !original && f.A.Writes == writes &&
                    f.Journal.Saves == saves && f.Journal.Clears == clears, "Changing master blocking during Hold leaves the active temporary state intact");
                Assert(gate.Process(192, true, false) == blockBefore && gate.Process(192, false, false) == blockBefore,
                    "Mid-Hold master change retains repeats/up routing for the original press");
                f.Router.Tick(false, false, false, false);
                Assert(!f.C.HoldActive && f.A.Value == original && !f.C.HasPending, "Release after a mid-Hold master change restores either base state");
                Assert(gate.Process(192, true, false) == !blockBefore, "The following Hold press uses the changed master routing");
                f.Router.Tick(false, true, false, true);
                f.Router.SetPaused(true, false, true);
                ConfigureBlocking(f, gate, false, true);
                Assert(!f.C.HoldActive && f.A.Value == original, "Pause still restores Hold after a master switch");
                gate.Process(192, false, false); f.Router.Tick(false, false, false, false);
                f.Router.KeyBlockingEnabled = true; ConfigureBlocking(f, gate, false, false);
                Assert(!gate.Process(192, true, false), "Enabling master while paused keeps new keys pass-through");
                f.Router.SetPaused(false, false, true); ConfigureBlocking(f, gate, false, true);
                f.Router.Tick(false, true, false, true);
                Assert(!f.C.HoldActive && !gate.Process(192, false, false), "Unpausing cannot turn a paused key press into Hold or an unmatched blocked up");
                f.Router.Tick(false, false, false, false);
                Assert(gate.Process(192, true, false) && gate.Process(192, false, false), "After release, resumed master blocking applies to the next press");
            }
        }
        using (var f = new Fixture(false, false)) {
            f.S.ToggleKey = 1; f.S.HoldKey = 4; f.S.BlockToggleKey = true;
            Assert(!f.Router.BlockToggleKey && !f.Router.BlockHoldKey, "Master never makes primary mouse clicks blockable");
        }
        string path = Path.Combine(Path.GetTempPath(), "apeiron-rc-session-" + Guid.NewGuid().ToString("N") + ".xml");
        try {
            using (var f = new Fixture(false, false)) {
                f.S.HoldKey = 192; f.S.ToggleKey = 119; f.S.BlockToggleKey = true; f.S.BlockHoldKey = false;
                f.Router.KeyBlockingEnabled = false; f.Router.SetPaused(true, false, false);
                XmlFiles.Write(path, f.S);
                var loaded = XmlFiles.Read<Settings>(path); loaded.Normalize();
                using (var fresh = new Controller(loaded, new FakeAudio(), new Clock(), new MemoryJournal())) {
                    var restarted = new InputRouter(fresh);
                    Assert(!restarted.Paused && restarted.KeyBlockingEnabled, "A new session clears pause and re-enables master blocking");
                    Assert(loaded.HoldKey == 192 && loaded.ToggleKey == 119 && loaded.BlockToggleKey && !loaded.BlockHoldKey,
                        "Saving during pause/master bypass preserves actual key bindings and per-key preferences");
                }
            }
        } finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
        Assert(KeyNames.Format(192, "Oemtilde") == "` / ~ (US)", "The user's grave binding has a readable, explicitly US-labelled caption");
        Assert(KeyNames.Format(49, "D1") == "1" && KeyNames.Format(97, "NumPad1") == "Numpad 1", "Number row and numpad remain distinguishable in key captions");
        Assert(KeyNames.Format(17, "ControlKey") == "Ctrl" && KeyNames.Format(162, "LControlKey") == "Left Ctrl" &&
            KeyNames.Format(163, "RControlKey") == "Right Ctrl", "Generic and left/right modifiers keep distinct readable captions");
        var assembly = typeof(Controller).Assembly;
        Assert(assembly.GetName().Version.ToString() == "0.1.0.8" &&
            ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute))).InformationalVersion == "0.1.0-beta.1",
            "Delivery assembly revision and public beta version agree");
    }
    public static int Main() {
        try {
            Matrix(); FailureCases(); PreviewCases(); RandomSequences(); StorageAndInterop(); ImmediateInputCases(); OptionalRecoveryCases(); ElevationNoticeCases(); SuppressionCases(); PauseCases(); MasterBlockingCases();
            Console.WriteLine("PASS: " + scenarios + " parameter combinations; 20,000 seeded actions; " + assertions + " assertions against the compiled production EXE.");
            Console.WriteLine("Real Windows Core Audio, global input/UAC, GUI/DPI, tray/taskbar, startup and games were not executed.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
