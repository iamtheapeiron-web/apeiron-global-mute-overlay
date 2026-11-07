using System;

namespace Apeiron.GlobalMute {
    public struct InputSnapshot {
        public bool ToggleDown, HoldDown, TogglePressed, HoldPressed, HasEvent;
        public double QueueMilliseconds;
    }

    // Hook callbacks only record input and schedule a wake-up; they do no audio or file I/O.
    public sealed class InputMailbox {
        private readonly object sync = new object();
        private readonly bool[] down = new bool[256], pressed = new bool[256];
        private readonly double[] transition = new double[256];
        private readonly IClock clock;
        private int toggleKey, holdKey;
        private Action wakeup;
        private double? pendingSince;
        public InputMailbox(IClock clock) { this.clock = clock; }
        public void Configure(int toggle, int hold, bool toggleDown, bool holdDown, Action notify) {
            lock (sync) {
                toggleKey = toggle; holdKey = hold; wakeup = notify; pendingSince = null;
                Array.Clear(pressed, 0, pressed.Length);
                if (Valid(toggle)) { down[toggle] = toggleDown; transition[toggle] = clock.Now; }
                if (Valid(hold)) { down[hold] = holdDown; transition[hold] = clock.Now; }
            }
        }
        private static bool Valid(int key) { return key > 0 && key < 256; }
        public void Record(int key, bool state) {
            if (!Valid(key)) return;
            Action notify = null;
            lock (sync) {
                if (down[key] == state) return; // Ignore key auto-repeat.
                down[key] = state; transition[key] = clock.Now;
                if (state) pressed[key] = true;
                if (key == toggleKey || key == holdKey) {
                    if (!pendingSince.HasValue) pendingSince = clock.Now;
                    notify = wakeup;
                }
            }
            if (notify != null) notify();
        }
        private void Reconcile(int key, bool physical) {
            // A low-level hook runs BEFORE Windows updates GetAsyncKeyState.
            // The grace interval prevents that stale value from undoing a fresh event.
            // It also bounds recovery from a missed key-up or a removed hook.
            if (!Valid(key) || clock.Now - transition[key] < 0.08 || down[key] == physical) return;
            down[key] = physical; transition[key] = clock.Now;
            if (physical) pressed[key] = true;
            if (!pendingSince.HasValue) pendingSince = clock.Now;
        }
        public InputSnapshot Take(bool reconcile, bool physicalToggle, bool physicalHold) {
            lock (sync) {
                if (reconcile) { Reconcile(toggleKey, physicalToggle); Reconcile(holdKey, physicalHold); }
                var value = new InputSnapshot {
                    ToggleDown = Valid(toggleKey) && down[toggleKey], HoldDown = Valid(holdKey) && down[holdKey],
                    TogglePressed = Valid(toggleKey) && pressed[toggleKey], HoldPressed = Valid(holdKey) && pressed[holdKey],
                    HasEvent = pendingSince.HasValue,
                    QueueMilliseconds = pendingSince.HasValue ? Math.Max(0, (clock.Now - pendingSince.Value) * 1000) : 0
                };
                if (Valid(toggleKey)) pressed[toggleKey] = false;
                if (Valid(holdKey)) pressed[holdKey] = false;
                pendingSince = null; return value;
            }
        }
        public void ClearPresses() { lock (sync) { Array.Clear(pressed, 0, pressed.Length); pendingSince = null; } }
    }
}
