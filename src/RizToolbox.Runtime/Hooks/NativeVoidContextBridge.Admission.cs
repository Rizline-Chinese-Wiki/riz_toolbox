using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeVoidContextBridge : INativeVoidAdmissionBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AdmissionCloseNative(uint slot, uint timeoutMilliseconds, ref NativeVoidAdmissionReceipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AdmissionReceiptNative(ref NativeVoidAdmissionReceipt receipt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AdmissionReadNative(uint slot, ref NativeVoidAdmissionGateStatistics statistics);
    private AdmissionCloseNative _admissionClose = null!;
    private AdmissionReceiptNative _admissionVerify = null!, _admissionRelease = null!;
    private AdmissionReadNative _admissionRead = null!;
    private AdmissionProtocol? _admissionProtocol;
    private void InitializeAdmission()
    {
        _admissionClose = Export<AdmissionCloseNative>("RizVoidContext_Close");
        _admissionVerify = Export<AdmissionReceiptNative>("RizVoidContext_Verify");
        _admissionRelease = Export<AdmissionReceiptNative>("RizVoidContext_Release");
        _admissionRead = Export<AdmissionReadNative>("RizVoidContext_GetGateStats");
        if (Marshal.SizeOf<NativeVoidAdmissionReceipt>() != 32 || Marshal.SizeOf<NativeVoidAdmissionGateStatistics>() != 40)
            throw new UnsupportedSignatureException("Native void admission receipt/statistics layout differs from version 1.");
    }
    public NativeVoidAdmissionAttempt TryAcquireAdmission(uint timeoutMilliseconds)
    {
        var protocol = Volatile.Read(ref _admissionProtocol);
        if (protocol is null)
        {
            var created = new AdmissionProtocol(this);
            protocol = Interlocked.CompareExchange(ref _admissionProtocol, created, null) ?? created;
        }
        var result = NativeVoidAdmissionLease.TryAcquire(protocol, timeoutMilliseconds);
        if (result.Status == NativeVoidAdmissionAttemptStatus.Unknown) Interlocked.Exchange(ref _unknown, 1);
        return result;
    }
    private ExactHookBroker.ManagedBoundaryActivity ReadAdmissionActivity()
    {
        try
        {
            var value = new NativeVoidAdmissionGateStatistics { Version = 1, Size = 40 };
            if (_admissionRead(_slot, ref value) != 0 || !value.IsValid || value.Unknown != 0 || value.Active > long.MaxValue
                || value.Parked > (ulong)long.MaxValue - value.Active)
            { Interlocked.Exchange(ref _unknown, 1); return new(0, false); }
            return new((long)(value.Active + value.Parked), Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error) { Fault("Native void admission statistics read failed.", error); return new(0, false); }
    }
    private sealed class AdmissionProtocol : INativeVoidAdmissionProtocol
    {
        private readonly NativeVoidContextBridge _owner;
        internal AdmissionProtocol(NativeVoidContextBridge owner) => _owner = owner;
        public uint Slot => _owner._slot;
        public uint CurrentOsThreadId => GetCurrentThreadId();
        public bool Ready => _owner._configured && Volatile.Read(ref _owner._unknown) == 0;
        public uint Close(uint timeoutMilliseconds, ref NativeVoidAdmissionReceipt receipt)
            => _owner._admissionClose(_owner._slot, timeoutMilliseconds, ref receipt);
        public uint Verify(NativeVoidAdmissionReceipt receipt) => _owner._admissionVerify(ref receipt);
        public uint Release(NativeVoidAdmissionReceipt receipt) => _owner._admissionRelease(ref receipt);
        public uint ReadStatistics(out NativeVoidAdmissionGateStatistics statistics)
        {
            statistics = new() { Version = 1, Size = 40 };
            return _owner._admissionRead(_owner._slot, ref statistics);
        }
        public void ReportUnknown(string failure) => Interlocked.Exchange(ref _owner._unknown, 1);
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
