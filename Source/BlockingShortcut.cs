using System;

namespace Apeiron.GlobalMute {
    [Flags]
    public enum ShortcutModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

    public struct ShortcutResult {
        public bool Triggered, SkipActions, Suppress;
    }

    // One route and one command per physical press, independent of auto-repeat,
    // modifier release order, reconfiguration or a change of foreground window.
    public sealed class BlockingShortcut {
        private readonly object sync = new object();
        private readonly bool[] down = new bool[256], claimed = new bool[256], consumed = new bool[256];
        private int key;
        private ShortcutModifiers modifiers;
        private bool enabled;

        public static bool IsModifier(int vk) {
            return vk == 16 || vk == 17 || vk == 18 || vk == 91 || vk == 92 || vk >= 160 && vk <= 165;
        }
        public static bool IsValid(int vk, ShortcutModifiers mods) {
            return vk >= 8 && vk <= 254 && vk != 27 && !IsModifier(vk) &&
                mods != ShortcutModifiers.None && (mods & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0 &&
                !(vk == 46 && (mods & (ShortcutModifiers.Control | ShortcutModifiers.Alt)) == (ShortcutModifiers.Control | ShortcutModifiers.Alt));
        }
        public static bool UsesModifierKey(int vk, ShortcutModifiers mods) {
            return (vk == 17 || vk == 162 || vk == 163) && (mods & ShortcutModifiers.Control) != 0 ||
                (vk == 18 || vk == 164 || vk == 165) && (mods & ShortcutModifiers.Alt) != 0 ||
                (vk == 16 || vk == 160 || vk == 161) && (mods & ShortcutModifiers.Shift) != 0;
        }
        public static bool Conflicts(Settings settings, ShortcutModifiers mods) {
            return settings.ToggleEnabled && UsesModifierKey(settings.ToggleKey, mods) ||
                settings.HoldEnabled && UsesModifierKey(settings.HoldKey, mods);
        }
        public static string Format(int vk, ShortcutModifiers mods) {
            if (!IsValid(vk, mods)) return Ui.Text("Not assigned");
            return ((mods & ShortcutModifiers.Control) != 0 ? "Ctrl + " : "") +
                ((mods & ShortcutModifiers.Alt) != 0 ? "Alt + " : "") +
                ((mods & ShortcutModifiers.Shift) != 0 ? "Shift + " : "") +
                KeyNames.Format(vk, ((System.Windows.Forms.Keys)vk).ToString());
        }
        public void Configure(int vk, ShortcutModifiers mods, bool active, bool physicalDown) {
            lock (sync) {
                key = vk; modifiers = mods; enabled = active && IsValid(vk, mods);
                if (vk > 0 && vk < 256 && !claimed[vk]) down[vk] = physicalDown;
            }
        }
        public ShortcutResult Process(int vk, bool state, ShortcutModifiers physicalModifiers, bool passThrough, bool commandAllowed) {
            if (vk <= 0 || vk >= 256) return new ShortcutResult();
            lock (sync) {
                bool trigger = false;
                if (state) {
                    if (!down[vk]) {
                        claimed[vk] = enabled && commandAllowed && vk == key && physicalModifiers == modifiers;
                        consumed[vk] = claimed[vk] && !passThrough;
                        trigger = claimed[vk]; down[vk] = true;
                    }
                    return new ShortcutResult { Triggered = trigger, SkipActions = claimed[vk], Suppress = consumed[vk] };
                }
                var result = new ShortcutResult { SkipActions = claimed[vk], Suppress = consumed[vk] };
                down[vk] = claimed[vk] = consumed[vk] = false; return result;
            }
        }
        public bool NeedsModifiers(int vk, bool state) {
            if (vk <= 0 || vk >= 256 || !state) return false;
            lock (sync) { return enabled && vk == key && !down[vk]; }
        }
        public bool IsClaimedDown(int vk) {
            if (vk <= 0 || vk >= 256) return false;
            lock (sync) { return down[vk] && claimed[vk]; }
        }
        public bool IsConsumedDown(int vk) {
            if (vk <= 0 || vk >= 256) return false;
            lock (sync) { return down[vk] && consumed[vk]; }
        }
        public bool IsActionDown(int vk, bool physicalDown) { return physicalDown && !IsClaimedDown(vk); }
    }
}
