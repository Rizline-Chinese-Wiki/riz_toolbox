using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LowerImageGuardMemory
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", EntryPoint = "VirtualQuery", SetLastError = true)]
    private static extern nuint LowerImageGuardVirtualQuery(IntPtr address, out LowerImageGuardMemory memory, nuint size);
    [DllImport("kernel32.dll", EntryPoint = "VirtualProtect", SetLastError = true)]
    private static extern bool LowerImageGuardVirtualProtect(IntPtr address, nuint length, uint protect, out uint prior);
    [DllImport("kernel32.dll", EntryPoint = "FlushInstructionCache", SetLastError = true)]
    private static extern bool LowerImageGuardFlush(IntPtr process, IntPtr address, nuint length);
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static extern IntPtr LowerImageGuardProcess();
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    private static extern bool LowerImageGuardLoaded(uint flags, IntPtr address, out IntPtr module);

    private static uint LowerImageGuardProtection(IntPtr address)
    {
        Check(LowerImageGuardVirtualQuery(address, out var info, (nuint)Marshal.SizeOf<LowerImageGuardMemory>()) != 0,
            "Could not read the actual owned image page protection.");
        return info.Protect;
    }
    private static void LowerImageGuardWriteByte(IntPtr address, byte value, uint expectedProtection)
    {
        Check(LowerImageGuardVirtualProtect(address, 1, 0x40, out var prior) && prior == expectedProtection,
            "Could not enter the controlled writable owned body page.");
        try
        {
            Marshal.WriteByte(address, value);
            Check(LowerImageGuardFlush(LowerImageGuardProcess(), address, 1), "Could not flush the controlled owned image byte.");
        }
        finally
        {
            Check(LowerImageGuardVirtualProtect(address, 1, prior, out _), "Could not restore the original owned body page protection.");
        }
        Check(LowerImageGuardProtection(address) == expectedProtection, "The controlled body write retained writable protection.");
    }

    private sealed class LowerImageGuardTransport
    {
        private readonly ConstructorWorkerReadStats _single;
        private readonly ConstructorWorkerReadSource _source;
        private readonly uint _slot;
        private readonly IntPtr _owner;
        internal LowerImageGuardTransport(ConstructorContext native)
        {
            var getTimeOwner = Private<object>(native.Group.GetTimeLease, "_owner");
            var bridge = Private<NativePlaybackSingleReturnBridge>(getTimeOwner, "_bridge");
            _slot = Private<uint>(bridge, "_slot");
            _single = Export<ConstructorWorkerReadStats>(Private<IntPtr>(bridge, "_library"), "RizSingleReturnPrototype_GetStats");
            var hub = Private<NativePlaybackStockSourceHub>(Private<object>(native.Group, "_owner"), "_hub");
            _owner = hub.NativeOwner;
            _source = Export<ConstructorWorkerReadSource>(hub.NativeLibraryHandle, "RtPlaybackSourceReadOwner");
        }
        internal (ConstructorWorkerSingleStats Single, ConstructorWorkerSourceInfo Source) Read()
        {
            var single = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
            var source = new ConstructorWorkerSourceInfo();
            Check(_single(_slot, ref single) == 0 && _source(_owner, ref source) == 1,
                "The retained native helper could not read its actual physical authority.");
            return (single, source);
        }
    }

    private static void LowerImageGuardAssertSticky(ConstructorContext native, LowerImageGuardTransport transport,
        ulong lowerCalls, LowerFixtureRead readLower)
    {
        Check(!native.Group.IsActive && !native.Group.GetTimeLease.IsActive,
            "Changed lower evidence left either the common group or GetTime owner active.");
        Reject(native.Group.VerifyCurrent, "Restoring the physical bytes revived sticky group authority.");
        foreach (var role in native.Targets.Roles)
        {
            var lease = native.Group.SourceLease(role);
            Check(!lease.IsActive, "One source physical member survived a common lower image fault: " + role);
            Reject(lease.VerifyCurrent, "One source member verified after its common group fault: " + role);
        }
        var raw = transport.Read();
        Check(raw.Single.AuthorityActive == 0 && raw.Single.Unknown == 1 && raw.Single.InFlight == 0
            && (raw.Source.Flags & 4) != 0 && (raw.Source.Flags & 1) == 0 && raw.Source.Faults > 0
            && raw.Source.InFlight == 0 && raw.Source.Entered == raw.Source.Exited,
            "Common image rejection did not synchronously revoke both actual native authorities as sticky Unknown.");
        native.Router.PumpCompleted(); native.Group.PumpCompleted();
        Check(native.Group.ReadActivity() is { Known: false, InFlight: 0 } && native.Api.ActiveRoots == 7
            && !native.Router.RootsReleased && native.CallbackFailure is null,
            "Physical Unknown lost its exact seven-root owner or invented physical drain authority.");
        Check(readLower(0) == lowerCalls && readLower(1) == lowerCalls,
            "The controlled image/cache mutation executed the lower body.");
        native.Dispose();
        Check(!native.Router.RootsReleased && native.Api.ActiveRoots == 7 && native.Group.ReadActivity() is { Known: false, InFlight: 0 },
            "Dispose/Pump turned the image-Unknown root responsibility into a release receipt.");
        var final = transport.Read();
        Check(final.Single.AuthorityActive == 0 && final.Single.Unknown == 1
            && (final.Source.Flags & 4) != 0 && readLower(0) == lowerCalls,
            "Late cleanup revived native authority or executed the rejected lower body.");
        Reject(native.Group.VerifyCurrent, "Late cleanup revived the sticky common image fault.");
    }

    private static int RunLowerPitchManagedImageGuards(string[] a, string mode)
    {
        ConstructorContext? native = null;
        var disposed = false;
        try
        {
            Check(a.Length == 5 && mode is "lower-pitch-image-wrong-pair" or "lower-pitch-image-body-byte"
                or "lower-pitch-image-cache-entry" or "lower-pitch-image-loader-release",
                "Unknown isolated managed lower image guard mode.");
            Check(Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256,
                "Image guard requires the existing exact owned wrapper image.");
            if (mode == "lower-pitch-image-wrong-pair")
            {
                using var main = new MainThreadDispatcher();
                var broker = new ExactHookBroker(Path.GetFullPath(a[4]), main, new Logger());
                var wrapper = NativeLibrary.Load(Path.GetFullPath(a[1]));
                try
                {
                    Check(broker.ReadActualPlaybackDobbyCallCounts() == (0, 0), "Wrong-pair test did not begin before all actual Dobby calls.");
                    Reject(() => PlaybackSourceGroupTargets.ResolveOwnedLowerPitchFixture(wrapper, wrapper),
                        "A wrapper module used as its own lower image passed the managed fixed target preflight.");
                    Check(broker.ReadActualPlaybackDobbyCallCounts() == (0, 0), "Wrong-pair preflight reached actual Dobby Prepare/Commit.");
                }
                finally { NativeLibrary.Free(wrapper); }
                Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                    wrongPairRejected = true, actualPrepare = 0, actualCommit = 0, retainedRoots = 0,
                    gameExecuted = false, installed = false, productionAdmission = false }));
                return 0;
            }

            native = new ConstructorContext(a);
            Check(native.Targets.HasLowerPitch && native.Targets.SourceRoleCount == 20 && native.PhysicalMemberCount == 21
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21),
                "Image guard did not install the actual complete two-image, twenty-one-entry group.");
            native.Arm(); native.Group.VerifyCurrent();
            Check(native.Api.ActiveRoots == 7 && native.Group.GetTimeLease.ReadCurrent().Active,
                "Image guard did not begin after an actual Pause and value-bearing Clock.");
            var readLower = Export<LowerFixtureRead>(native.LowerModule, "StockLowerFixtureRead");
            var lowerCalls = readLower(0);
            Check(readLower(1) == lowerCalls, "The initial owned route has an unreturned or unstored lower call.");
            var transport = new LowerImageGuardTransport(native);
            var initial = transport.Read();
            Check(initial.Single.AuthorityActive == 1 && initial.Single.Unknown == 0 && initial.Source.Flags == 1
                && initial.Source.InFlight == 0, "The exact native helpers were not armed before the guard operation.");
            var rootsRetained = false; var byteRestored = false; var protectionRestored = false; var cacheRestored = false;
            var callerLowerFreed = false; var issuerLoaderHeld = false; var realRouteVerified = false;
            var bodyOffset = -1; var patchedPrefix = 0; uint originalProtection = 0;
            if (mode == "lower-pitch-image-body-byte")
            {
                var lowerLease = native.Group.SourceLease(NativePlaybackSourceRole.LowerSetPitch);
                var lower = native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address;
                patchedPrefix = lowerLease.PatchedByteCount; bodyOffset = lowerLease.OriginalBodyLength - 1;
                Check(lowerLease.OriginalBodyLength == 495 && bodyOffset == 494 && bodyOffset >= patchedPrefix,
                    "The controlled byte is not outside the installed prefix inside the full fixed lower body.");
                var address = IntPtr.Add(lower, bodyOffset); var original = Marshal.ReadByte(address);
                originalProtection = LowerImageGuardProtection(address);
                Check(original == 0x90 && (originalProtection & 0xff) == 0x20,
                    "Expected the owned lower's nonexecuted final NOP with original execute/read protection.");
                try
                {
                    LowerImageGuardWriteByte(address, (byte)(original ^ 1), originalProtection);
                    Check(Marshal.ReadByte(address) != original && LowerImageGuardProtection(address) == originalProtection,
                        "The controlled full-body edit or its original page protection was not actual.");
                    Reject(native.Group.VerifyCurrent, "Group VerifyCurrent ignored an actual one-byte edit outside the lower detour prefix.");
                    Check(!native.Group.IsActive && transport.Read().Single.AuthorityActive == 0,
                        "The lower body mismatch did not revoke group/native Clock before returning.");
                }
                finally
                {
                    LowerImageGuardWriteByte(address, original, originalProtection);
                    byteRestored = Marshal.ReadByte(address) == original;
                    protectionRestored = LowerImageGuardProtection(address) == originalProtection;
                    Console.WriteLine(JsonSerializer.Serialize(new { guardRestore = "lower-body-byte", byteRestored,
                        protectionRestored, bodyOffset, patchedPrefix, originalProtection }));
                }
                Check(byteRestored && protectionRestored, "The body-byte fixture failed to restore the exact byte/protection in finally.");
                LowerImageGuardAssertSticky(native, transport, lowerCalls, readLower); disposed = true; rootsRetained = true;
            }
            else if (mode == "lower-pitch-image-cache-entry")
            {
                var issuer = native.Targets.LowerPitch!; var cache = issuer.CacheAddress;
                var original = Marshal.ReadIntPtr(cache); originalProtection = LowerImageGuardProtection(cache);
                var foreignEntry = NativeLibrary.GetExport(native.Module, "StockSetClip");
                Check(original == issuer.Address && foreignEntry != original && (originalProtection & 0xff) is 4 or 8,
                    "Cache guard needs its naturally bound actual lower and a foreign owned entry in writable cache data.");
                try
                {
                    Marshal.WriteIntPtr(cache, foreignEntry);
                    Check(Marshal.ReadIntPtr(cache) == foreignEntry, "The writable wrapper cache did not actually change.");
                    Reject(native.Group.VerifyCurrent, "Group VerifyCurrent accepted a writable wrapper cache redirected to a foreign entry.");
                    Check(!native.Group.IsActive && transport.Read().Single.AuthorityActive == 0,
                        "The wrong cache entry did not synchronously revoke the common group and native Clock.");
                }
                finally
                {
                    Marshal.WriteIntPtr(cache, original);
                    cacheRestored = Marshal.ReadIntPtr(cache) == original;
                    protectionRestored = LowerImageGuardProtection(cache) == originalProtection;
                    Console.WriteLine(JsonSerializer.Serialize(new { guardRestore = "wrapper-lower-cache", cacheRestored,
                        protectionRestored, originalProtection }));
                }
                Check(cacheRestored && protectionRestored, "The wrong cache fixture did not restore pointer/protection in finally.");
                LowerImageGuardAssertSticky(native, transport, lowerCalls, readLower); disposed = true; rootsRetained = true;
            }
            else
            {
                var lower = native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address;
                NativeLibrary.Free(native.LowerModule); callerLowerFreed = true;
                // Flags 4|2 inspect the image containing this address without
                // creating any new loader reference after the caller's Free.
                Check(LowerImageGuardLoaded(6, lower, out var stillLoaded) && stillLoaded == native.LowerModule,
                    "Releasing the normal caller lower handle unloaded the issuer-owned active image.");
                native.Group.VerifyCurrent(); issuerLoaderHeld = true;
                var before = native.Group.ReadSourceHealth(); var clockBefore = transport.Read().Single;
                native.Stock(NativePlaybackSourceRole.Replay, .125f);
                Check(readLower(0) == lowerCalls + 2 && readLower(1) == lowerCalls + 2
                    && native.Group.ReadSourceHealth().WriterSequence == before.WriterSequence + 2,
                    "The retained lower image did not execute actual Replay wrapper/lower reset and writer reapply after caller Free.");
                var value = native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo);
                var clockAfter = transport.Read().Single;
                Check(float.IsFinite(value) && native.Group.GetTimeLease.ReadCurrent().Active
                    && clockAfter.OriginalCalls == clockBefore.OriginalCalls + 1 && clockAfter.Applied == clockBefore.Applied + 1
                    && clockAfter.Unknown == 0 && native.CallbackFailure is null,
                    "The retained image did not preserve value-bearing actual GetTime/Clock after caller Free.");
                native.Group.VerifyCurrent(); realRouteVerified = true;
                native.Dispose(); disposed = true;
                Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0
                    && native.Observations.All(value => value.IsReleased) && native.CallbackFailure is null,
                    "The normal caller-Free route did not actually drain all roots and physical observations.");
            }
            Check(native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21), "A guard operation installed a replacement physical group.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 21, actualCommit = 21, retainedRoots = native.Api.ActiveRoots, rootsRetained,
                bodyLength = 495, bodyOffset, patchedPrefix, byteRestored, cacheRestored, protectionRestored,
                originalProtection, callerLowerFreed, issuerLoaderHeld, realRouteVerified,
                lowerBodyExecutedDuringMutation = false, stickyAuthorityRevoked = rootsRetained,
                gameExecuted = false, installed = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
        finally { if (!disposed) native?.Dispose(); }
    }
}
