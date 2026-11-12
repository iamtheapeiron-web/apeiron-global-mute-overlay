using System;

namespace Apeiron.GlobalMute {
    [Flags]
    public enum ShortcutModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

    public struct ShortcutResult {
        public bool Triggered, SkipActions, Suppress;
    }

    // A command owns only its completing key. Its down/up route remains latched
    // across repeat, focus changes, pause and reconfiguration. A modifier pair
    // never delays the first modifier and must be fully released before reuse.
    public sealed class BlockingShortcut {
        private readonly object sync = new object();
        private readonly bool[] down = new bool[256], claimed = new bool[256], consumed = new bool[256];
        private readonly bool[] pairLatched = new bool[256];
        private int key, secondKey;
        private ShortcutModifiers modifiers;
        private bool enabled;

        public static bool IsModifier(int vk) {
            return vk == 16 || vk == 17 || vk == 18 || vk == 91 || vk == 92 || vk >= 160 && vk <= 165;
        }
        public static int PairLeft(int first, int second) {
            int left = Math.Min(first, second), right = Math.Max(first, second);
            return (left == 160 || left == 162 || left == 164) && right == left + 1 ? left : 0;
        }
        public static bool IsValid(int vk, ShortcutModifiers mods) { return IsValid(vk, mods, 0); }
        public static bool IsValid(int vk, ShortcutModifiers mods, int second) {
            if (second != 0) return mods == ShortcutModifiers.None && PairLeft(vk, second) != 0;
            return vk >= 8 && vk <= 254 && vk != 27 && !IsModifier(vk) &&
                (mods & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0 &&
                !(vk == 46 && (mods & (ShortcutModifiers.Control | ShortcutModifiers.Alt)) == (ShortcutModifiers.Control | ShortcutModifiers.Alt));
        }
        public static bool UsesModifierKey(int vk, ShortcutModifiers mods) {
            return (vk == 17 || vk == 162 || vk == 163) && (mods & ShortcutModifiers.Control) != 0 ||
                (vk == 18 || vk == 164 || vk == 165) && (mods & ShortcutModifiers.Alt) != 0 ||
                (vk == 16 || vk == 160 || vk == 161) && (mods & ShortcutModifiers.Shift) != 0;
        }
        public static bool ConflictsWithAction(int actionKey, int vk, ShortcutModifiers mods, int second) {
            int left = PairLeft(vk, second);
            if (left != 0) return actionKey == left || actionKey == left + 1 || actionKey == (left == 160 ? 16 : left == 162 ? 17 : 18);
            return mods == ShortcutModifiers.None ? actionKey == vk : UsesModifierKey(actionKey, mods);
        }
        public static bool Conflicts(Settings settings, int vk, ShortcutModifiers mods, int second) {
            // A single command key stays distinct even from a disabled action's
            // stored key. Modifier chords may share their ordinary completing key.
            bool single = second == 0 && mods == ShortcutModifiers.None;
            return (single || settings.ToggleEnabled) && ConflictsWithAction(settings.ToggleKey, vk, mods, second) ||
                (single || settings.HoldEnabled) && ConflictsWithAction(settings.HoldKey, vk, mods, second);
        }
        public static string Format(int vk, ShortcutModifiers mods) { return Format(vk, mods, 0); }
        public static string Format(int vk, ShortcutModifiers mods, int second) {
            if (!IsValid(vk, mods, second)) return Ui.Text("Not assigned");
            int left = PairLeft(vk, second);
            if (left != 0) return KeyName(left) + " + " + KeyName(left + 1);
            return ((mods & ShortcutModifiers.Control) != 0 ? "Ctrl + " : "") +
                ((mods & ShortcutModifiers.Alt) != 0 ? "Alt + " : "") +
                ((mods & ShortcutModifiers.Shift) != 0 ? "Shift + " : "") + KeyName(vk);
        }
        private static string KeyName(int vk) { return KeyNames.Format(vk, ((System.Windows.Forms.Keys)vk).ToString()); }
        public void Configure(int vk, ShortcutModifiers mods, bool active, bool physicalDown) {
            Configure(vk, mods, 0, active, physicalDown, false);
        }
        public void Configure(int vk, ShortcutModifiers mods, int second, bool active, bool firstDown, bool secondDown) {
            lock (sync) {
                key = vk; secondKey = second; modifiers = mods; enabled = active && IsValid(vk, mods, second);
                if (vk > 0 && vk < 256 && !claimed[vk]) down[vk] = firstDown;
                if (second > 0 && second < 256 && !claimed[second]) down[second] = secondDown;
                int left = PairLeft(vk, second);
                if (left != 0) {
                    if (down[left] && down[left + 1]) pairLatched[left] = true;
                    else if (!down[left] && !down[left + 1]) pairLatched[left] = false;
                }
            }
        }
        public ShortcutResult Process(int vk, bool state, ShortcutModifiers physicalModifiers, bool passThrough, bool commandAllowed) {
            return Process(vk, state, physicalModifiers, passThrough, commandAllowed, null);
        }
        public ShortcutResult Process(int vk, bool state, ShortcutModifiers physicalModifiers, bool passThrough, bool commandAllowed, bool? partnerPhysicalDown) {
            if (vk <= 0 || vk >= 256) return new ShortcutResult();
            lock (sync) {
                bool trigger = false;
                int left = PairLeft(key, secondKey);
                bool pairCandidate = left != 0 && (vk == key || vk == secondKey);
                if (state) {
                    if (!down[vk]) {
                        int partner = pairCandidate ? (vk == key ? secondKey : key) : 0;
                        // The other modifier's asynchronous state is already current.
                        // Repair a missed pass-through edge, but never overwrite a
                        // suppressed key's hook-owned state.
                        if (partner != 0 && partnerPhysicalDown.HasValue && !claimed[partner]) {
                            down[partner] = partnerPhysicalDown.Value;
                            if (!down[partner]) pairLatched[left] = false;
                        }
                        bool matches = pairCandidate ? down[partner] && !pairLatched[left] :
                            secondKey == 0 && vk == key && physicalModifiers == modifiers;
                        claimed[vk] = enabled && commandAllowed && matches;
                        consumed[vk] = claimed[vk] && !passThrough;
                        trigger = claimed[vk]; down[vk] = true;
                        if (trigger && pairCandidate) pairLatched[left] = true;
                    }
                    return new ShortcutResult { Triggered = trigger, SkipActions = claimed[vk], Suppress = consumed[vk] };
                }
                var result = new ShortcutResult { SkipActions = claimed[vk], Suppress = consumed[vk] };
                down[vk] = claimed[vk] = consumed[vk] = false;
                int releasedLeft = vk >= 160 && vk <= 165 ? vk - (vk % 2) : 0;
                if (releasedLeft != 0 && !down[releasedLeft] && !down[releasedLeft + 1]) pairLatched[releasedLeft] = false;
                return result;
            }
        }
        public int PartnerToCheck(int vk, bool state) {
            if (vk <= 0 || vk >= 256 || !state) return 0;
            lock (sync) {
                if (!enabled || down[vk] || PairLeft(key, secondKey) == 0) return 0;
                return vk == key ? secondKey : vk == secondKey ? key : 0;
            }
        }
        public bool NeedsModifiers(int vk, bool state) {
            if (vk <= 0 || vk >= 256 || !state) return false;
            lock (sync) { return enabled && secondKey == 0 && vk == key && !down[vk]; }
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
