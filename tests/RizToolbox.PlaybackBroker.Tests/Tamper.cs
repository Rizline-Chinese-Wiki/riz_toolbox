using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class BrokerChecks
{
    private static void Tamper(string mode, string[] args, IntPtr image, IntPtr target, IntPtr method,
        NativePlaybackGetTimeHookLease lease, GetTime invoke, Read read, NativePlaybackBinding binding)
    {
        var address = mode switch
        {
            "tail-tamper" => IntPtr.Add(target, 639),
            "patch-tamper" => target,
            "relocation-tamper" => lease.OriginalAddress,
            "relocation-tail-tamper" => IntPtr.Add(lease.OriginalAddress, 9),
            "dobby-original-tamper" => lease.DobbyOriginalAddress,
            "unwind-tamper" => lease.TrampolineUnwindAddress,
            "helper-tamper" => lease.EntryAddress,
            "dobby-tamper" => NativeLibrary.GetExport(NativeLibrary.Load(args[3]), "DobbyPrepare"),
            "metadata-tamper" => method,
            "image-tamper" => IntPtr.Add(image, Marshal.ReadInt32(image, 0x3c) + 8),
            _ => throw new InvalidOperationException("Unknown actual-tamper mode.")
        };
        var original = Bytes(address, 1);
        Write(address, new[] { (byte)(original[0] ^ 1) });
        Reject(() => lease.VerifyCurrent(image, target, method), "Actual memory tamper retained current authority.");
        Check(!lease.IsActive && !lease.ReadActivity().Known, "Tamper was not sticky unknown/withdrawn.");
        Write(address, original);
        Reject(() => lease.VerifyCurrent(image, target, method), "Restoring bytes revived a faulted receipt.");
        Check(invoke((IntPtr)binding.Player, method) == 1 && read(0) == 6, "Faulted owner lost its one stock original call.");
    }
}
