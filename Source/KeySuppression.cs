using System;

namespace Apeiron.GlobalMute {
    // Decisions are latched for an entire physical press. A blocked down must have
    // a blocked up even if focus, key bindings or options change while it is held.
    public sealed class KeySuppression {
        private readonly object sync = new object();
        private readonly bool[] down = new bool[256], blocked = new bool[256];
        private int toggleKey, holdKey;
        private bool toggleEnabled, holdEnabled, blockToggle, blockHold;
        public static bool CanBlock(int key) { return key >= 5 && key <= 254; } // Never block primary mouse clicks.
        public void Configure(int toggle, int hold, bool toggleOn, bool holdOn, bool consumeToggle, bool consumeHold,
            bool toggleDown, bool holdDown) {
            lock (sync) {
                toggleKey = toggle; holdKey = hold; toggleEnabled = toggleOn; holdEnabled = holdOn;
                blockToggle = consumeToggle; blockHold = consumeHold;
                Seed(toggle, toggleDown); Seed(hold, holdDown);
            }
        }
        private void Seed(int key, bool state) {
            if (key <= 0 || key >= 256 || blocked[key]) return;
            down[key] = state; // A key already held before capture remains pass-through until release.
        }
        public void UpdateBlocking(bool consumeToggle, bool consumeHold) {
            lock (sync) { blockToggle = consumeToggle; blockHold = consumeHold; }
        }
        public bool Process(int key, bool state, bool passThroughContext) {
            if (key <= 0 || key >= 256) return false;
            lock (sync) {
                if (state) {
                    if (!down[key]) {
                        bool selected = toggleEnabled && key == toggleKey && blockToggle || holdEnabled && key == holdKey && blockHold;
                        blocked[key] = selected && CanBlock(key) && !passThroughContext;
                        down[key] = true;
                    }
                    return blocked[key];
                }
                bool result = down[key] && blocked[key];
                down[key] = false; blocked[key] = false; return result;
            }
        }
        public bool IsBlockedDown(int key) {
            if (key <= 0 || key >= 256) return false;
            lock (sync) { return down[key] && blocked[key]; }
        }
    }
}
