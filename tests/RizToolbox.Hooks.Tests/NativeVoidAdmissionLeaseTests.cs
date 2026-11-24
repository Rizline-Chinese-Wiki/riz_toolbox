using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

/// <summary>Typed lease protocol regressions only. Actual native admission races,
/// wakeups and original forwarding are tested by the separate native fixture.</summary>
internal static class NativeVoidAdmissionLeaseTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Native admission typed receipt and statistics preserve the exact 32 and 40 byte ABI", Layout),
        ("Native admission issues a private lease only after exact receipt and closed zero-active verification", Acquisition),
        ("Native admission rejects unready invalid self-close busy and timeout attempts without reopening", Rejected),
        ("Native admission uncertain Close preserves a process-owned unusable responsibility", CloseUnknown),
        ("Native admission malformed or mismatched receipts cannot authorize verification or Release", InvalidReceipts),
        ("Native admission unknown active stale and unissued gates retain responsibility without Release", InvalidStatistics),
        ("Native admission wrong-thread Dispose does not reopen or consume the issuing thread lease", WrongThread),
        ("Native admission verified unwritten Dispose releases exactly once and stale leases never reopen", DisposeAndReplay),
        ("Native admission writes and retained unknown outcomes prevent Dispose from opening the gate", WriteRetention),
        ("Native admission rejected or throwing Release becomes unknown and is never retried", ReleaseUnknown),
        ("Native admission protocol and bridge owner survive GC while closed responsibility remains", Lifetime)
    };
    private static void Check(bool value, string message = "Native admission lease check failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static NativeVoidAdmissionLease Acquire(Protocol protocol)
    {
        var result = NativeVoidAdmissionLease.TryAcquire(protocol, 5);
        Check(result.Status == NativeVoidAdmissionAttemptStatus.Acquired && result.Lease is not null && result.Failure is null);
        return result.Lease!;
    }
    private sealed class Protocol : INativeVoidAdmissionProtocol
    {
        internal readonly List<string> Calls = new();
        internal uint SlotValue = 7, OsThread = 123, CloseStatus, VerifyStatus, ReleaseStatus, ReadStatus;
        internal bool IsReady = true, ThrowClose, ThrowVerify, ThrowRead, ThrowRelease, ThrowReport;
        internal int Reports;
        internal string? ReportedFailure;
        internal Func<NativeVoidAdmissionReceipt, NativeVoidAdmissionReceipt>? RewriteReceipt;
        internal NativeVoidAdmissionReceipt Issued;
        internal NativeVoidAdmissionGateStatistics Statistics = new() { Version = 1, Size = 40, Phase = NativeVoidAdmissionPhase.Closed, Epoch = 111, Parked = 3 };
        public uint Slot => SlotValue;
        public uint CurrentOsThreadId => OsThread;
        public bool Ready => IsReady;
        public uint Close(uint timeoutMilliseconds, ref NativeVoidAdmissionReceipt receipt)
        {
            Calls.Add("close:" + timeoutMilliseconds); if (ThrowClose) throw new IOException("Close transport failure");
            if (CloseStatus != 0) return CloseStatus;
            receipt = new() { Version = 1, Size = 32, Slot = Slot, OwnerThreadId = CurrentOsThreadId, Epoch = Statistics.Epoch };
            if (RewriteReceipt is not null) receipt = RewriteReceipt(receipt); Issued = receipt; return 0;
        }
        public uint Verify(NativeVoidAdmissionReceipt receipt)
        {
            Calls.Add("verify"); Check(receipt.Equals(Issued)); if (ThrowVerify) throw new IOException("Verify transport failure"); return VerifyStatus;
        }
        public uint Release(NativeVoidAdmissionReceipt receipt)
        {
            Calls.Add("release"); Check(receipt.Equals(Issued)); if (ThrowRelease) throw new IOException("Release transport failure"); return ReleaseStatus;
        }
        public uint ReadStatistics(out NativeVoidAdmissionGateStatistics statistics)
        { Calls.Add("stats"); if (ThrowRead) throw new IOException("Statistics transport failure"); statistics = Statistics; return ReadStatus; }
        public void ReportUnknown(string failure)
        {
            Reports++; ReportedFailure = failure;
            if (ThrowReport) throw new IOException("Unknown reporting failed");
        }
    }
    private static void Layout()
    {
        Check(Marshal.SizeOf<NativeVoidAdmissionReceipt>() == 32 && Marshal.OffsetOf<NativeVoidAdmissionReceipt>(nameof(NativeVoidAdmissionReceipt.Slot)).ToInt32() == 8
            && Marshal.OffsetOf<NativeVoidAdmissionReceipt>(nameof(NativeVoidAdmissionReceipt.OwnerThreadId)).ToInt32() == 12
            && Marshal.OffsetOf<NativeVoidAdmissionReceipt>(nameof(NativeVoidAdmissionReceipt.Epoch)).ToInt32() == 16
            && Marshal.OffsetOf<NativeVoidAdmissionReceipt>(nameof(NativeVoidAdmissionReceipt.Reserved)).ToInt32() == 24);
        Check(Marshal.SizeOf<NativeVoidAdmissionGateStatistics>() == 40 && Marshal.OffsetOf<NativeVoidAdmissionGateStatistics>(nameof(NativeVoidAdmissionGateStatistics.Active)).ToInt32() == 8
            && Marshal.OffsetOf<NativeVoidAdmissionGateStatistics>(nameof(NativeVoidAdmissionGateStatistics.Parked)).ToInt32() == 16
            && Marshal.OffsetOf<NativeVoidAdmissionGateStatistics>(nameof(NativeVoidAdmissionGateStatistics.Epoch)).ToInt32() == 24
            && Marshal.OffsetOf<NativeVoidAdmissionGateStatistics>(nameof(NativeVoidAdmissionGateStatistics.Phase)).ToInt32() == 32
            && Marshal.OffsetOf<NativeVoidAdmissionGateStatistics>(nameof(NativeVoidAdmissionGateStatistics.Unknown)).ToInt32() == 36);
        Check(typeof(NativeVoidAdmissionLease).GetConstructors().Length == 0, "Lease gained a public forgeable constructor.");
    }
    private static void Acquisition()
    {
        var protocol = new Protocol(); var lease = Acquire(protocol);
        Check(protocol.Calls.SequenceEqual(new[] { "close:5", "verify", "stats" }) && lease.Slot == 7 && lease.Epoch == 111
            && !lease.Unknown && !lease.Released && !lease.WriteAttempted && lease.VerifyClosed());
        Check(protocol.Statistics.Parked == 3, "The issued closed lease incorrectly required parked physical calls to drain.");
        Check(lease.ReleaseUnmodified() && lease.Released && protocol.Calls.Count(value => value == "release") == 1);
    }
    private static void Rejected()
    {
        foreach (var stage in new[] { "timeoutbound", "unready", "slot", "thread" })
        {
            var protocol = new Protocol(); if (stage == "unready") protocol.IsReady = false; if (stage == "slot") protocol.SlotValue = 32; if (stage == "thread") protocol.OsThread = 0;
            var result = NativeVoidAdmissionLease.TryAcquire(protocol, stage == "timeoutbound" ? 10001u : 0);
            Check(result.Status == NativeVoidAdmissionAttemptStatus.Rejected && result.Lease is null && protocol.Calls.Count == 0);
        }
        foreach (var status in new uint[] { 1, 2, 3, 4 })
        {
            var protocol = new Protocol { CloseStatus = status }; var result = NativeVoidAdmissionLease.TryAcquire(protocol, 0);
            Check(result.Status == NativeVoidAdmissionAttemptStatus.Rejected && result.Lease is null && protocol.Calls.SequenceEqual(new[] { "close:0" }));
        }
    }
    private static void CloseUnknown()
    {
        foreach (var stage in new[] { "sticky", "unknownstatus", "throw" })
        {
            var protocol = new Protocol { CloseStatus = stage == "sticky" ? 5u : 99u, ThrowClose = stage == "throw" };
            var result = NativeVoidAdmissionLease.TryAcquire(protocol, 1);
            Check(result.Status == NativeVoidAdmissionAttemptStatus.Unknown && result.Lease is { Unknown: true } && result.Failure is not null);
            result.Lease!.Dispose(); Check(!result.Lease.VerifyClosed() && !result.Lease.ReleaseUnmodified() && !protocol.Calls.Contains("release")
                && protocol.Reports == 1 && protocol.ReportedFailure == result.Lease.Failure);
        }
    }
    private static void InvalidReceipts()
    {
        foreach (var stage in new[] { "version", "size", "slot", "thread", "epoch", "reserved" })
        {
            var protocol = new Protocol { RewriteReceipt = receipt => {
                switch (stage) { case "version": receipt.Version = 2; break; case "size": receipt.Size = 40; break; case "slot": receipt.Slot++; break;
                    case "thread": receipt.OwnerThreadId++; break; case "epoch": receipt.Epoch = 0; break; default: receipt.Reserved = 1; break; }
                return receipt;
            } };
            var result = NativeVoidAdmissionLease.TryAcquire(protocol, 1);
            Check(result.Status == NativeVoidAdmissionAttemptStatus.Unknown && result.Lease is { Unknown: true }); result.Lease!.Dispose();
            Check(!protocol.Calls.Contains("verify") && !protocol.Calls.Contains("release"), "Malformed receipt reached an authority-granting native operation.");
        }
    }
    private static void InvalidStatistics()
    {
        foreach (var stage in new[] { "verify", "verifythrow", "readthrow", "readstatus", "version", "size", "phase", "active", "epoch", "unknown", "unknownvalue" })
        {
            var protocol = new Protocol();
            switch (stage) {
                case "verify": protocol.VerifyStatus = 5; break; case "verifythrow": protocol.ThrowVerify = true; break;
                case "readthrow": protocol.ThrowRead = true; break; case "readstatus": protocol.ReadStatus = 1; break;
                case "version": protocol.Statistics.Version = 2; break; case "size": protocol.Statistics.Size = 56; break;
                case "phase": protocol.Statistics.Phase = NativeVoidAdmissionPhase.Open; break; case "active": protocol.Statistics.Active = 1; break;
                case "epoch": protocol.RewriteReceipt = receipt => { receipt.Epoch++; return receipt; }; break;
                case "unknown": protocol.Statistics.Unknown = 1; break; default: protocol.Statistics.Unknown = 99; break;
            }
            var result = NativeVoidAdmissionLease.TryAcquire(protocol, 2);
            Check(result.Status == NativeVoidAdmissionAttemptStatus.Unknown && result.Lease is { Unknown: true }); result.Lease!.Dispose(); Check(!protocol.Calls.Contains("release"));
        }
    }
    private static void WrongThread()
    {
        var protocol = new Protocol(); var lease = Acquire(protocol); var count = protocol.Calls.Count; protocol.OsThread++;
        lease.Dispose(); Check(!lease.VerifyClosed() && !lease.ReleaseUnmodified() && !lease.MarkWriteAttempted() && !lease.Unknown && protocol.Calls.Count == count && protocol.Reports == 0);
        protocol.OsThread--; Check(lease.VerifyClosed() && lease.ReleaseUnmodified(), "Wrong-thread access consumed the correct OS-thread receipt.");
    }
    private static void DisposeAndReplay()
    {
        var protocol = new Protocol(); var lease = Acquire(protocol); lease.Dispose(); lease.Dispose();
        Check(lease.Released && !lease.VerifyClosed() && !lease.ReleaseUnmodified() && !lease.MarkWriteAttempted() && protocol.Calls.Count(value => value == "release") == 1);
        protocol.Statistics.Epoch++;
        var next = Acquire(protocol); Check(next.Epoch != lease.Epoch && !lease.VerifyClosed());
        var calls = protocol.Calls.Count; lease.Dispose(); Check(protocol.Calls.Count == calls && next.VerifyClosed()); next.Dispose();
    }
    private static void WriteRetention()
    {
        var writtenProtocol = new Protocol(); var written = Acquire(writtenProtocol); Check(written.MarkWriteAttempted() && written.WriteAttempted);
        Check(!written.MarkWriteAttempted()); written.Dispose(); Check(!written.ReleaseUnmodified() && !writtenProtocol.Calls.Contains("release") && written.VerifyClosed() && writtenProtocol.Reports == 0);
        var unknownProtocol = new Protocol(); var unknown = Acquire(unknownProtocol); unknown.RetainUnknown("Mid write outcome was not confirmed.");
        unknown.RetainUnknown("Second report must not re-enter the owner.");
        unknown.Dispose(); Check(unknown.Unknown && !unknown.VerifyClosed() && !unknown.ReleaseUnmodified() && !unknown.MarkWriteAttempted() && !unknownProtocol.Calls.Contains("release")
            && unknownProtocol.Reports == 1 && unknownProtocol.ReportedFailure == "Mid write outcome was not confirmed.");
        var reporterProtocol = new Protocol { ThrowReport = true }; var reporter = Acquire(reporterProtocol);
        reporter.RetainUnknown(new string('x', 900)); reporter.Dispose(); reporter.RetainUnknown("Must not retry failed reporter.");
        Check(reporter.Unknown && reporterProtocol.Reports == 1 && reporter.Failure?.Length == 768 && !reporterProtocol.Calls.Contains("release"));
    }
    private static void ReleaseUnknown()
    {
        foreach (var stage in new[] { "status", "throw", "verifylost", "activelost" })
        {
            var protocol = new Protocol(); var lease = Acquire(protocol);
            if (stage == "status") protocol.ReleaseStatus = 7; if (stage == "throw") protocol.ThrowRelease = true;
            if (stage == "verifylost") protocol.VerifyStatus = 4; if (stage == "activelost") protocol.Statistics.Active = 1;
            Check(!lease.ReleaseUnmodified() && lease.Unknown); var count = protocol.Calls.Count; lease.Dispose(); Check(protocol.Calls.Count == count);
            Check(protocol.Calls.Count(value => value == "release") == (stage is "status" or "throw" ? 1 : 0));
            Check(protocol.Reports == 1 && protocol.ReportedFailure == lease.Failure);
        }
    }
    private static void Lifetime()
    {
        var protocol = new Protocol(); var weak = new WeakReference(protocol); var lease = Acquire(protocol); lease.RetainUnknown("Preserve process-owned callback roots.");
        protocol = null!; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.IsAlive && lease.Unknown); lease.Dispose();
    }
}
