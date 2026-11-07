namespace Apeiron.GlobalMute {
    // Only a confirmed privilege mismatch triggers a warning. Unknown is not false.
    public sealed class ElevationNotice {
        public readonly bool? OwnElevation;
        public int ProcessId { get; private set; }
        public string ProcessName { get; private set; }
        public bool? ForegroundElevation { get; private set; }
        public bool AskedThisSession { get; private set; }
        public ElevationNotice(bool? ownElevation) { OwnElevation = ownElevation; ProcessName = ""; }
        public void Observe(int processId, string processName, bool? elevated) {
            if (processId <= 0) return;
            ProcessId = processId; ProcessName = processName ?? "Application"; ForegroundElevation = elevated;
        }
        public bool Mismatch { get { return OwnElevation == false && ForegroundElevation == true; } }
        public bool ShouldAsk(bool enabled, bool busy) { return Mismatch && enabled && !busy && !AskedThisSession; }
        public bool ShouldAskAtStartup(bool enabled, bool busy) { return OwnElevation != true && enabled && !busy && !AskedThisSession; }
        public void MarkAsked() { AskedThisSession = true; }
    }
}
