using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeVoidAdmissionAttemptStatus { Acquired, Rejected, Unknown }
internal enum NativeVoidAdmissionPhase : uint { Open, Closing, Closed }
internal sealed record NativeVoidAdmissionAttempt(NativeVoidAdmissionAttemptStatus Status, NativeVoidAdmissionLease? Lease, string? Failure);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeVoidAdmissionReceipt
{
    internal uint Version, Size, Slot, OwnerThreadId;
    internal ulong Epoch, Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeVoidAdmissionGateStatistics
{
    internal uint Version, Size;
    internal ulong Active, Parked, Epoch;
    internal NativeVoidAdmissionPhase Phase;
    internal uint Unknown;
    internal bool IsValid => Version == 1 && Size == 40 && Phase is NativeVoidAdmissionPhase.Open or NativeVoidAdmissionPhase.Closing or NativeVoidAdmissionPhase.Closed
        && Unknown <= 1;
}

/// <summary>Optional internal admission capability. The ordinary void observation
/// interface has no new mandatory methods or arbitrary write/patch authority.</summary>
internal interface INativeVoidAdmissionBridge
{
    NativeVoidAdmissionAttempt TryAcquireAdmission(uint timeoutMilliseconds);
}

/// <summary>Typed protocol seam for bounded lease regression. Real production
/// calls use only the bridge's private export adapter, never SDK raw pointers.</summary>
internal interface INativeVoidAdmissionProtocol
{
    uint Slot { get; }
    uint CurrentOsThreadId { get; }
    bool Ready { get; }
    uint Close(uint timeoutMilliseconds, ref NativeVoidAdmissionReceipt receipt);
    uint Verify(NativeVoidAdmissionReceipt receipt);
    uint Release(NativeVoidAdmissionReceipt receipt);
    uint ReadStatistics(out NativeVoidAdmissionGateStatistics statistics);
    void ReportUnknown(string failure);
}

/// <summary>One immutable issued slot/epoch/OS-thread responsibility. An unknown
/// outcome or attempted write retains the closed gate; Dispose cannot reopen it.
/// This lease proves only admission of an already configured native void slot.
/// It is not a barrier for the first parent detour installation or a publisher.</summary>
internal sealed class NativeVoidAdmissionLease : IDisposable
{
    private enum LeaseState { Preparing, Acquired, Rejected, Released, Unknown }
    private static readonly object OwnerGate = new();
    private static readonly List<NativeVoidAdmissionLease> Owners = new();
    private readonly object _gate = new();
    private readonly INativeVoidAdmissionProtocol _protocol;
    private readonly uint _slot, _ownerOsThreadId;
    private NativeVoidAdmissionReceipt _receipt;
    private LeaseState _state;
    private bool _writeAttempted;
    private string? _failure;
    private NativeVoidAdmissionLease(INativeVoidAdmissionProtocol protocol)
    {
        _protocol = protocol; _slot = protocol.Slot; _ownerOsThreadId = protocol.CurrentOsThreadId;
        _receipt = new() { Version = 1, Size = 32, Slot = _slot };
    }
    internal bool Unknown { get { lock (_gate) return _state == LeaseState.Unknown; } }
    internal bool Released { get { lock (_gate) return _state == LeaseState.Released; } }
    internal bool WriteAttempted { get { lock (_gate) return _writeAttempted; } }
    internal string? Failure { get { lock (_gate) return _failure; } }
    internal uint Slot => _slot;
    internal ulong Epoch { get { lock (_gate) return _receipt.Epoch; } }

    internal static NativeVoidAdmissionAttempt TryAcquire(INativeVoidAdmissionProtocol protocol, uint timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        if (timeoutMilliseconds > 10000 || protocol.Slot >= 32 || !protocol.Ready || protocol.CurrentOsThreadId == 0)
            return new(NativeVoidAdmissionAttemptStatus.Rejected, null, "Native admission requires a ready exact slot, current OS thread and at most 10000 ms.");
        var lease = new NativeVoidAdmissionLease(protocol);
        // Keep the protocol (and real bridge callbacks/DLL) before native Close.
        // Uncertain Close must not discard the receipt or be retried by Dispose.
        lock (OwnerGate) Owners.Add(lease);
        try
        {
            var status = protocol.Close(timeoutMilliseconds, ref lease._receipt);
            if (status != 0)
            {
                if (status is 1 or 2 or 3 or 4)
                {
                    lease._state = LeaseState.Rejected;
                    lock (OwnerGate) Owners.Remove(lease);
                    return new(NativeVoidAdmissionAttemptStatus.Rejected, null, "Native admission Close rejected with status " + status + ".");
                }
                lease.MakeUnknown("Native admission Close has an unknown outcome: " + status + ".");
                return new(NativeVoidAdmissionAttemptStatus.Unknown, lease, lease._failure);
            }
            lease._state = LeaseState.Acquired;
            if (!lease.VerifyCore()) return new(NativeVoidAdmissionAttemptStatus.Unknown, lease, lease._failure);
            return new(NativeVoidAdmissionAttemptStatus.Acquired, lease, null);
        }
        catch (Exception error)
        {
            lease.MakeUnknown("Native admission Close or verification threw: " + error.Message);
            return new(NativeVoidAdmissionAttemptStatus.Unknown, lease, lease._failure);
        }
    }
    internal bool VerifyClosed()
    {
        lock (_gate)
        {
            if (_state != LeaseState.Acquired || _protocol.CurrentOsThreadId != _ownerOsThreadId) return false;
            return VerifyCore();
        }
    }
    private bool VerifyCore()
    {
        try
        {
            if (_receipt.Version != 1 || _receipt.Size != 32 || _receipt.Reserved != 0 || _receipt.Slot != _slot
                || _receipt.OwnerThreadId != _ownerOsThreadId || _receipt.Epoch == 0 || _protocol.Slot != _slot
                || _protocol.CurrentOsThreadId != _ownerOsThreadId || !_protocol.Ready)
            { MakeUnknown("Native admission receipt differs from its issuing slot or OS thread."); return false; }
            var status = _protocol.Verify(_receipt);
            if (status != 0) { MakeUnknown("Native admission receipt verification failed: " + status + "."); return false; }
            var read = _protocol.ReadStatistics(out var statistics);
            if (read != 0 || !statistics.IsValid || statistics.Unknown != 0 || statistics.Phase != NativeVoidAdmissionPhase.Closed
                || statistics.Active != 0 || statistics.Epoch != _receipt.Epoch)
            { MakeUnknown("Native admission does not have a known issued closed gate with zero active calls."); return false; }
            // Parked entries are physical native calls and may be nonzero here.
            // They have not been admitted to the protected original window.
            return true;
        }
        catch (Exception error) { MakeUnknown("Native admission verification threw: " + error.Message); return false; }
    }
    internal bool MarkWriteAttempted()
    {
        lock (_gate)
        {
            if (_state != LeaseState.Acquired || _writeAttempted || _protocol.CurrentOsThreadId != _ownerOsThreadId || !VerifyCore()) return false;
            _writeAttempted = true;
            return true;
        }
    }
    internal void RetainUnknown(string failure)
    { lock (_gate) MakeUnknown(string.IsNullOrWhiteSpace(failure) ? "Native admission responsibility is unconfirmed." : failure); }
    private void MakeUnknown(string failure)
    {
        var first = _state != LeaseState.Unknown;
        _state = LeaseState.Unknown;
        _failure ??= failure.Length <= 768 ? failure : failure[..768];
        // This lane only reports managed scalar uncertainty. A reporter fault
        // cannot clear the local unknown state, release the root or open a gate.
        if (first) try { _protocol.ReportUnknown(_failure); } catch { }
    }
    internal bool ReleaseUnmodified()
    {
        lock (_gate)
        {
            if (_state != LeaseState.Acquired || _writeAttempted || _protocol.CurrentOsThreadId != _ownerOsThreadId) return false;
            if (!VerifyCore()) return false;
            try
            {
                var result = _protocol.Release(_receipt);
                if (result != 0) { MakeUnknown("Native admission Release was not confirmed: " + result + "."); return false; }
                _state = LeaseState.Released;
                lock (OwnerGate) Owners.Remove(this);
                return true;
            }
            catch (Exception error) { MakeUnknown("Native admission Release threw: " + error.Message); return false; }
        }
    }
    public void Dispose()
    {
        // No finalizer releases an OS-thread-owned native receipt. Only a still
        // verified, unwritten lease can reopen here; uncertain writes stay closed.
        ReleaseUnmodified();
    }
}
