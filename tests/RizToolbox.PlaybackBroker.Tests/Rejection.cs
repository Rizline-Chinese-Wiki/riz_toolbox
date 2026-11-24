using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime.Hooks;

internal static partial class BrokerChecks
{
    private static void Rejection(string mode, IntPtr image, IntPtr target, IntPtr method,
        ref string bodyHash, ref string imageHash, Func<NativePlaybackGetTimeHookLease> install, string[] args)
    {
        switch (mode)
        {
            case "fake-engine-reject": break;
            case "wrong-image-reject": imageHash = new string('0', 64); break;
            case "wrong-body-reject": bodyHash = new string('0', 64); break;
            case "precapture-dobby-reject":
                Write(IntPtr.Add(NativeLibrary.Load(args[3]), 0x3870), new byte[] { 0xcb }); break;
            case "precapture-helper-reject":
                var entry = NativeLibrary.GetExport(NativeLibrary.Load(args[2]), "RizSingleReturnPrototype_GetEntry");
                Write(entry, new[] { (byte)(Marshal.ReadByte(entry) ^ 1) }); break;
            case "precapture-source-tail-reject":
                Write(IntPtr.Add(target, 639), new byte[] { 0xcb });
                bodyHash = Convert.ToHexString(SHA256.HashData(Bytes(target, 640))).ToLowerInvariant(); break;
            case "precapture-source-consumer-reject":
                var consumer = NativeLibrary.GetExport(image, "RizOwnedPlayback_Read");
                Write(consumer, new[] { (byte)(Marshal.ReadByte(consumer) ^ 1) }); break;
            case "prefix-reject":
                Write(target, new byte[] { 0x90 }); bodyHash = Convert.ToHexString(SHA256.HashData(Bytes(target, 640))).ToLowerInvariant(); break;
            case "mi-static-reject": Marshal.WriteByte(method, 76, 0x96); break;
            case "mi-generic-reject": Marshal.WriteByte(method, 83, 1); break;
            case "mi-inflated-reject": Marshal.WriteByte(method, 83, 2); break;
            case "mi-params-reject": Marshal.WriteByte(method, 82, 1); break;
            case "mi-entry-reject": Marshal.WriteIntPtr(method, IntPtr.Add(target, 5)); break;
            case "mi-return-reject": Marshal.WriteInt32(Marshal.ReadIntPtr(method, 40), 8, 0x000d0000); break;
            default: throw new InvalidOperationException("Unknown preflight rejection mode.");
        }
        Reject(() => install(), "The invalid preflight unexpectedly issued a concrete lease.");
        Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "Preflight loaded game code.");
    }
}
