using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Apeiron.GlobalMute {
    public class DeviceInfo {
        public string Id, Name;
        public bool IsDefault;
        public override string ToString() { return Name + (IsDefault ? " (Windows default)" : ""); }
    }
    public interface IAudio : IDisposable {
        List<DeviceInfo> Enumerate();
        void Connect(string deviceId);
        void Disconnect();
        string ConnectedId { get; }
        bool HardwareMute { get; }
        bool GetMute();
        void ValidateDevice();
        void SetMute(bool muted);
    }
    public interface IClock { double Now { get; } }
    public class MonotonicClock : IClock {
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        public double Now { get { return watch.Elapsed.TotalSeconds; } }
    }
    public struct OverlayView {
        public bool Visible, Muted, Placement;
    }
    public class ActionTiming {
        public string Action;
        public double ReadMilliseconds, JournalMilliseconds, WriteMilliseconds, ConfirmMilliseconds, TotalMilliseconds;
    }

    // The real controller is also used by the portable tests; no Windows calls here.
    public class Controller : IDisposable {
        public readonly Settings Settings;
        private readonly IAudio audio;
        private readonly IClock clock;
        private readonly IRestoreStore store;
        public bool Ready { get; private set; }
        public bool HoldActive { get; private set; }
        public bool? Muted { get; private set; }
        public string Error { get; private set; }
        public PendingRestore Pending { get; private set; }
        public ActionTiming LastTiming { get; private set; }
        public bool HardwareMute { get { return Ready && audio.HardwareMute; } }
        private bool normalVisible, normalMuted;
        private double hideAt, previewUntil, nextConnect;
        private bool preview, previewMuted;
        public bool Placement { get; set; }
        public bool HasPending { get { return Pending != null; } }

        public Controller(Settings settings, IAudio audio, IClock clock, IRestoreStore store) {
            Settings = settings; this.audio = audio; this.clock = clock; this.store = store;
            Pending = store.Load(); Error = "";
            // A pending Hold belongs to its exact original endpoint, including after a crash.
            if (Pending != null) {
                settings.DeviceId = Pending.DeviceId; settings.DeviceName = Pending.DeviceName;
            }
        }
        public bool SelectDevice(DeviceInfo device) {
            if (device.Id == Settings.DeviceId) { Reconnect(); return Ready; }
            ReleaseHold();
            if (HasPending) {
                Error = "Restore Hold on the previous microphone before choosing another device.";
                return false;
            }
            audio.Disconnect(); Ready = false; Muted = null;
            Settings.DeviceId = device.Id; Settings.DeviceName = device.Name;
            Reconnect(); return true; // Selection is retained even if this device disappears.
        }
        public void Reconnect() {
            if (HoldActive) ReleaseHold();
            audio.Disconnect(); Ready = false; Muted = null; normalVisible = false;
            nextConnect = 0; Poll();
        }
        public void Poll() {
            if (!Ready) {
                if (String.IsNullOrEmpty(Settings.DeviceId) || clock.Now < nextConnect) return;
                nextConnect = clock.Now + 1.5;
                try {
                    audio.Connect(Settings.DeviceId);
                    Ready = true;
                    if (HasPending) {
                        if (Pending.DeviceId != Settings.DeviceId)
                            throw new InvalidOperationException("Hold recovery belongs to another microphone. Select that device to restore it.");
                        RestorePending();
                    }
                    Muted = audio.GetMute(); Error = ""; RestoreBaseOverlay();
                } catch (Exception ex) { LoseDevice(ex); }
                return;
            }
            try {
                audio.ValidateDevice();
                if (HasPending && !HoldActive) { RestorePending(); RestoreBaseOverlay(); }
                bool current = audio.GetMute();
                if (HoldActive) {
                    bool expected = !Pending.Muted;
                    if (current != expected) SetConfirmed(expected);
                    Muted = expected;
                } else if (!Muted.HasValue || current != Muted.Value) {
                    Muted = current; ApplyNormal(current);
                }
            } catch (Exception ex) { LoseDevice(ex); }
        }
        private bool ReadMute(ActionTiming timing) {
            var watch = Stopwatch.StartNew();
            try { return audio.GetMute(); }
            finally { if (timing != null) timing.ReadMilliseconds += watch.Elapsed.TotalMilliseconds; }
        }
        private void SetConfirmed(bool value, ActionTiming timing = null) {
            var watch = Stopwatch.StartNew();
            try { audio.SetMute(value); }
            finally { if (timing != null) timing.WriteMilliseconds += watch.Elapsed.TotalMilliseconds; }
            bool confirmed; watch.Restart();
            try { confirmed = audio.GetMute(); }
            finally { if (timing != null) timing.ConfirmMilliseconds += watch.Elapsed.TotalMilliseconds; }
            if (confirmed != value)
                throw new InvalidOperationException("Windows did not confirm the requested mute state.");
        }
        public void Toggle() {
            if (!Ready || HoldActive || HasPending) return;
            var timing = new ActionTiming { Action = "Toggle" }; var watch = Stopwatch.StartNew();
            try { bool value = !ReadMute(timing); SetConfirmed(value, timing); Muted = value; Error = ""; ApplyNormal(value); }
            catch (Exception ex) { LoseDevice(ex); }
            finally { timing.TotalMilliseconds = watch.Elapsed.TotalMilliseconds; LastTiming = timing; }
        }
        public void BeginHold() {
            if (!Ready || HoldActive || HasPending || !Settings.HoldEnabled) return;
            var timing = new ActionTiming { Action = "Hold press" }; var watch = Stopwatch.StartNew();
            try { BeginHoldCore(timing); }
            finally { timing.TotalMilliseconds = watch.Elapsed.TotalMilliseconds; LastTiming = timing; }
        }
        private void BeginHoldCore(ActionTiming timing) {
            PendingRestore pending;
            try {
                pending = new PendingRestore { DeviceId = Settings.DeviceId,
                    DeviceName = Settings.DeviceName, Muted = ReadMute(timing) };
                // Never change the mic until its restoration has been written successfully.
                var journalWatch = Stopwatch.StartNew();
                try { store.Save(pending); }
                finally { timing.JournalMilliseconds += journalWatch.Elapsed.TotalMilliseconds; }
            } catch (Exception ex) { Error = "Hold could not start: " + ex.Message; return; }
            Pending = pending;
            try {
                SetConfirmed(!pending.Muted, timing);
                HoldActive = true; Muted = !pending.Muted; Error = ""; ApplyHoldOverlay();
            } catch (Exception ex) {
                // A failed write may have reached the driver: always attempt restoration.
                try { RestorePending(timing); } catch { }
                LoseDevice(ex);
            }
        }
        private void RestorePending(ActionTiming timing = null) {
            if (!HasPending) return;
            if (audio.ConnectedId != Pending.DeviceId)
                throw new InvalidOperationException("Cannot restore Hold to a different microphone.");
            bool original = Pending.Muted;
            if (ReadMute(timing) != original) SetConfirmed(original, timing);
            var watch = Stopwatch.StartNew();
            try { store.Clear(); } // Keep recovery and block new controls if clearing fails.
            finally { if (timing != null) timing.JournalMilliseconds += watch.Elapsed.TotalMilliseconds; }
            Pending = null; Muted = original;
        }
        public void ReleaseHold() {
            if (!HoldActive && !HasPending) return;
            var timing = new ActionTiming { Action = "Hold release" }; var watch = Stopwatch.StartNew();
            try { ReleaseHoldCore(timing); }
            finally { timing.TotalMilliseconds = watch.Elapsed.TotalMilliseconds; LastTiming = timing; }
        }
        private void ReleaseHoldCore(ActionTiming timing) {
            HoldActive = false;
            if (Ready) {
                try { RestorePending(timing); Error = ""; }
                catch (Exception ex) { LoseDevice(ex); }
            }
            RestoreBaseOverlay();
        }
        private void LoseDevice(Exception ex) {
            HoldActive = false; Ready = false; Muted = null; normalVisible = false;
            Error = ex.Message; audio.Disconnect(); nextConnect = clock.Now + 1.5;
        }
        public void RulesChanged() {
            if (!Settings.HoldEnabled) ReleaseHold();
            if (HoldActive) ApplyHoldOverlay(); else RestoreBaseOverlay();
        }
        public void RestoreBaseOverlay() {
            normalVisible = Ready && Muted == true && Settings.Normal == NormalRule.KeepMuted;
            normalMuted = Muted == true; hideAt = Double.PositiveInfinity;
        }
        private void ApplyNormal(bool muted) {
            normalMuted = muted;
            normalVisible = Settings.Normal == NormalRule.OnChange ||
                (Settings.Normal == NormalRule.MutedOnly && muted) ||
                (Settings.Normal == NormalRule.UnmutedOnly && !muted) ||
                (Settings.Normal == NormalRule.KeepMuted && muted);
            hideAt = Settings.Normal == NormalRule.KeepMuted ? Double.PositiveInfinity : clock.Now + Settings.PopupSeconds;
        }
        private void ApplyHoldOverlay() {
            if (!Muted.HasValue) { normalVisible = false; return; }
            if (Settings.DuringHold == HoldRule.FollowNormal) ApplyNormal(Muted.Value);
            else {
                normalVisible = Settings.DuringHold == HoldRule.Always;
                normalMuted = Muted.Value; hideAt = Double.PositiveInfinity;
            }
        }
        public void Preview(bool muted) {
            if (Placement) return;
            preview = true; previewMuted = muted; previewUntil = clock.Now + Settings.PopupSeconds;
        }
        public void BeginPlacement() { preview = false; Placement = true; }
        public void EndPlacement() { Placement = false; }
        public OverlayView View {
            get {
                if (Placement) return new OverlayView { Visible = true, Muted = Muted ?? true, Placement = true };
                if (preview && clock.Now >= previewUntil) preview = false;
                if (preview) return new OverlayView { Visible = true, Muted = previewMuted };
                return new OverlayView { Visible = normalVisible && clock.Now < hideAt, Muted = normalMuted };
            }
        }
        public void Suspend() {
            ReleaseHold(); audio.Disconnect(); Ready = false; Muted = null; normalVisible = false;
            preview = false; nextConnect = clock.Now + 0.5;
        }
        public void Dispose() { ReleaseHold(); audio.Dispose(); }
    }

    public class InputRouter {
        private readonly Controller controller;
        private bool toggleWasDown, holdWasDown, suppressToggle, suppressHold;
        public bool Paused { get; private set; }
        // Like Pause, the tray override lasts only for this process. Per-key choices stay saved in Settings.
        public bool KeyBlockingEnabled { get; set; }
        public bool ToggleEnabled { get { return !Paused && controller.Settings.ToggleEnabled; } }
        public bool HoldEnabled { get { return !Paused && controller.Settings.HoldEnabled; } }
        public bool BlockToggleKey { get { return KeyBlockingEnabled && ToggleEnabled && controller.Settings.BlockToggleKey && KeySuppression.CanBlock(controller.Settings.ToggleKey); } }
        public bool BlockHoldKey { get { return KeyBlockingEnabled && HoldEnabled && controller.Settings.BlockHoldKey && KeySuppression.CanBlock(controller.Settings.HoldKey); } }
        public InputRouter(Controller controller) { this.controller = controller; KeyBlockingEnabled = true; }
        public void SetPaused(bool paused, bool toggle, bool hold) {
            if (Paused == paused) return;
            Paused = paused;
            if (paused) controller.ReleaseHold();
            // Resuming must not turn a key already held for another app into a hotkey press.
            Sync(toggle, hold);
        }
        public void Sync(bool toggle, bool hold) {
            toggleWasDown = toggle; holdWasDown = hold;
            suppressToggle = toggle; suppressHold = hold;
        }
        public void Tick(bool toggle, bool hold, bool togglePress, bool holdPress) {
            if (Paused) { Sync(toggle, hold); return; }
            bool hp = !suppressHold && HoldEnabled && (holdPress || hold && !holdWasDown);
            bool tp = !suppressToggle && ToggleEnabled && (togglePress || toggle && !toggleWasDown);
            if (!hold) suppressHold = false;
            if (!toggle) suppressToggle = false;
            if (controller.HoldActive && (!hold || !HoldEnabled)) controller.ReleaseHold();
            if (hp) {
                controller.BeginHold();
                if (!hold) controller.ReleaseHold(); // A press and release between timer ticks.
            }
            if (tp && !controller.HoldActive && !hp) controller.Toggle();
            toggleWasDown = toggle; holdWasDown = hold;
        }
    }
}
