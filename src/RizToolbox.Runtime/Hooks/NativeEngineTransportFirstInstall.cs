using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

// Sole managed V5 publication owner. No loader, Configure, source write or
// permission setter exists. An immutable native record is necessary but never
// sufficient: loaded modules, held files, graph, Kernel tuples, source coverage
// and actual runtime-function registrations are independently inspected.
internal sealed partial class NativeEngineTransportFirstInstall : INativeEnginePublicationLifetime
{
    internal static bool ProductionStartupAdmissionKnown => IsProductionProfileAdmitted(NativeEngineTransportProfile.Read()) && IsRizlineHostProcess();
    private static bool IsProductionProfileAdmitted(NativeEngineTransportProfileData? profile)
        => profile is not null
        && profile.ProductionAdmissionAuthorized
        && string.Equals(profile.FixedContractSha256, NativeEngineTransportFixedFacts.FixedContractSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(profile.InteriorClosureSha256, NativeEngineTransportFixedFacts.StageAInteriorClosureSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(profile.ColdTraceSha256, NativeEngineTransportFixedFacts.StageAColdTraceSha256, StringComparison.OrdinalIgnoreCase);
    private static bool IsRizlineHostProcess()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return false;
        var processPath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(processPath)
            && string.Equals(Path.GetFileName(processPath), "Rizline.exe", StringComparison.OrdinalIgnoreCase);
    }
    private static readonly object OwnersGate = new();
    private static readonly Dictionary<string, NativeEngineTransportFirstInstall> Owners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RejectedOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<object> PermanentRoots = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ScalarNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NodeInfoNative(uint kind, uint slot, ref NativeEngineNodeInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InlineInfoNative(uint slot, ref NativeEngineInlineInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SchedulerInfoNative(uint kind, uint slot, ref NativeEngineSchedulerInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitInfoNative(uint slot, ref NativeEngineWaitInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NodeEntryNative(uint kind, uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative();
    private readonly object _gate = new();
    private readonly ModuleFacts[] _modules;
    private readonly uint _expectedProfile;
    private readonly ReadNative _read;
    private readonly ScalarNative _verify;
    private readonly NodeInfoNative _nodeInfo;
    private readonly InlineInfoNative _inlineInfo;
    private readonly SchedulerInfoNative _schedulerInfo;
    private readonly WaitInfoNative _waitInfo;
    private readonly NodeEntryNative _nodeEntry, _schedulerEntry, _waitEntry;
    private readonly EntryNative _inlineEntry, _rowEntry, _phaseEntry;
    private readonly NativeEngineTransportPublication _publication;
    private readonly NativeEngineTransportPublicationV5 _wire;
    private readonly List<Function>[] _sourceFunctions = new List<Function>[13];
    private readonly Function[] _originalFunctions = new Function[13];
    private readonly bool[] _attached = new bool[12];
    private int _unknown, _adoption;
    private byte[]? _adoptionActivity;
    private int _verificationDepth;
    public bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal bool OwnedPublication => _expectedProfile == 1;
    public IntPtr KernelModule => _modules[1].Handle;
    public string KernelPath => _modules[1].Path;
    public ulong TargetModule => _wire.TargetModule;

    internal static NativeEngineTransportFirstInstall RequireReady()
    {
        if (!ProductionStartupAdmissionKnown)
            throw new UnsupportedSignatureException("CapabilityUnavailable: V5 game startup execution-window admission remains unproven.");
        RequirePlatform();
        var profile = NativeEngineTransportProfile.Read()
            ?? throw new UnsupportedSignatureException("CapabilityUnavailable: no complete compiled V5 production fingerprints.");
        var executable = Canonical(Environment.ProcessPath ?? throw new UnsupportedSignatureException("The process image is unavailable."));
        if (!string.Equals(Path.GetFileName(executable), "Rizline.exe", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The production V5 owner requires the fixed game executable.");
        var directory = Path.GetDirectoryName(executable)!;
        var paths = new[] { "RizToolbox.EngineBootstrap.dll", "RizToolbox.EngineNodeKernel.dll", "UnityPlayer.dll", "Rizline.exe", "winhttp.dll" }
            .Select(name => Canonical(Path.Combine(directory, name))).ToArray();
        return Acquire(paths, Enumerable.Range(0, 5).Select(profile.Hash).ToArray(), 2);
    }

    // Exact parent-frozen owned builds only. Hashes are independently supplied
    // by the test parent; they are never copied from a publication or SDK DTO.
    internal static NativeEngineTransportFirstInstall RequireOwnedPublished(string[] absolutePaths, string[] exactHashes)
    {
        RequirePlatform(); ArgumentNullException.ThrowIfNull(absolutePaths); ArgumentNullException.ThrowIfNull(exactHashes);
        if (absolutePaths.Length != 5 || exactHashes.Length != 5)
            throw new ArgumentException("Owned V5 adoption requires five exact independently frozen image paths and hashes.");
        var paths = absolutePaths.Select(Canonical).ToArray(); var hashes = exactHashes.Select(Digest).ToArray();
        if (GetModuleHandle("UnityPlayer.dll") != IntPtr.Zero || GetModuleHandle("GameAssembly.dll") != IntPtr.Zero
            || string.Equals(Path.GetFileName(paths[3]), "Rizline.exe", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Canonical(Environment.ProcessPath ?? ""), paths[3], StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("An owned V5 fixture cannot adopt an actual game process.");
        return Acquire(paths, hashes, 1);
    }
    private static void RequirePlatform()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("V5 publication adoption requires Windows x64.");
    }
    private static NativeEngineTransportFirstInstall Acquire(string[] paths, string[] hashes, uint profile)
    {
        var key = paths[0];
        lock (OwnersGate)
        {
            if (RejectedOwners.Contains(key)) throw new UnsupportedSignatureException("This V5 publication owner is permanently Unknown.");
            try
            {
                if (!Owners.TryGetValue(key, out var owner)) { owner = new(paths, hashes, profile); Owners.Add(key, owner); }
                else if (owner._expectedProfile != profile
                    || !owner._modules.Select(x => x.Path).SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)
                    || !owner._modules.Select(x => x.Hash).SequenceEqual(hashes, StringComparer.Ordinal))
                    throw new UnsupportedSignatureException("The retained V5 owner has different independently supplied build facts.");
                owner.VerifyReady(); return owner;
            }
            catch { RejectedOwners.Add(key); throw; }
        }
    }
    private NativeEngineTransportFirstInstall(string[] paths, string[] hashes, uint profile)
    {
        _expectedProfile = profile;
        _modules = paths.Select((path, index) => new ModuleFacts(path, hashes[index], index == 3)).ToArray();
        if (_modules.Select(x => x.Handle).Distinct().Count() != 5)
            throw new UnsupportedSignatureException("V5 adoption requires five distinct already-loaded MEM_IMAGE modules.");
        _modules[0].RequireExportSet(PublicationExports); _modules[1].RequireExportSet(KernelExports);
        if (Export<ScalarNative>(_modules[0], PublicationExports[0])() != 5)
            throw new UnsupportedSignatureException("The actual Bootstrap publication export does not implement ABI5.");
        _read = Export<ReadNative>(_modules[0], PublicationExports[1]); _verify = Export<ScalarNative>(_modules[0], PublicationExports[2]);
        _nodeInfo = Export<NodeInfoNative>(_modules[1], "RizEngineNodeContext_GetInfo");
        _inlineInfo = Export<InlineInfoNative>(_modules[1], "RizEngineNodeContext_GetInlineInfo");
        _schedulerInfo = Export<SchedulerInfoNative>(_modules[1], "RizEngineSchedulerContext_GetInfo");
        _waitInfo = Export<WaitInfoNative>(_modules[1], "RizEngineWaitContext_GetInfo");
        _nodeEntry = Export<NodeEntryNative>(_modules[1], "RizEngineNodeContext_GetEntry");
        _schedulerEntry = Export<NodeEntryNative>(_modules[1], "RizEngineSchedulerContext_GetEntry");
        _waitEntry = Export<NodeEntryNative>(_modules[1], "RizEngineWaitContext_GetEntry");
        _inlineEntry = Export<EntryNative>(_modules[1], "RizEngineNodeContext_GetInlineEntry");
        _rowEntry = Export<EntryNative>(_modules[1], "RizEngineSchedulerContext_GetDrainRowEntry");
        _phaseEntry = Export<EntryNative>(_modules[1], "RizEngineWaitContext_GetPhaseEntry");
        VerifyAbi(); _publication = Read(); _wire = _publication.CopyWire(); VerifyShape();
        for (var i = 0; i < 13; ++i)
        {
            var fact = NativeEngineTransportFixedFacts.Function(i);
            _sourceFunctions[i] = _modules[2].RangeFamily(fact.Begin, fact.End);
            var originalRva = checked((uint)(_wire.Typed[i].Original - Address(_modules[0].Handle)));
            var originals = _modules[0].RangeFamily(originalRva, checked(originalRva + fact.PrefixBytes + 10));
            if (originals.Count != 1) throw new UnsupportedSignatureException("Each fixed original requires its exact single static runtime function.");
            _originalFunctions[i] = originals[0];
        }
        lock (OwnersGate) PermanentRoots.Add(this); // Keep modules/files/delegates through every later partial Attach or refusal.
        VerifyReady();
    }
    private static readonly string[] PublicationExports = {
        "RizEngineTransport_GetVersion", "RizEngineTransport_GetInfoV5", "RizEngineTransport_VerifyV5", "RizEngineTransport_AbortAfterPublication"
    };
    private static readonly string[] KernelExports = {
        "RizEngineNodeContext_GetAbiVersion", "RizEngineNodeContext_GetLimit", "RizEngineNodeContext_GetFrameSize", "RizEngineNodeContext_GetCompletionSize",
        "RizEngineNodeContext_GetEntry", "RizEngineNodeContext_Configure", "RizEngineNodeContext_Attach", "RizEngineNodeContext_GetInfo",
        "RizEngineNodeContext_GetInlineAbiVersion", "RizEngineNodeContext_GetInlineLimit", "RizEngineNodeContext_GetInlineFrameSize",
        "RizEngineNodeContext_GetInlineEntry", "RizEngineNodeContext_ConfigureInline", "RizEngineNodeContext_AttachInline", "RizEngineNodeContext_GetInlineInfo",
        "RizEngineSchedulerContext_GetAbiVersion", "RizEngineSchedulerContext_GetLimit", "RizEngineSchedulerContext_GetFrameSize",
        "RizEngineSchedulerContext_GetCompletionSize", "RizEngineSchedulerContext_GetRowFrameSize", "RizEngineSchedulerContext_GetEntry",
        "RizEngineSchedulerContext_GetDrainRowEntry", "RizEngineSchedulerContext_Configure", "RizEngineSchedulerContext_Attach", "RizEngineSchedulerContext_GetInfo",
        "RizEngineWaitContext_GetAbiVersion", "RizEngineWaitContext_GetLimit", "RizEngineWaitContext_GetFrameSize", "RizEngineWaitContext_GetCompletionSize",
        "RizEngineWaitContext_GetPhaseFrameSize", "RizEngineWaitContext_GetEntry", "RizEngineWaitContext_GetPhaseEntry", "RizEngineWaitContext_Configure",
        "RizEngineWaitContext_Attach", "RizEngineWaitContext_GetInfo"
    };
    private static T Export<T>(ModuleFacts module, string name) where T : Delegate
    {
        var expected = checked(Address(module.Handle) + module.ExportRva(name));
        if (!NativeLibrary.TryGetExport(module.Handle, name, out var pointer) || Address(pointer) != expected)
            throw new UnsupportedSignatureException("The already-loaded V5 export differs from its independent file-backed EAT.");
        Memory(pointer, 1, module.Handle, 0x20); return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    private NativeEngineTransportPublication Read()
    {
        var bytes = new byte[NativeEngineTransportPublicationLayout.ByteSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 5); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), NativeEngineTransportPublicationLayout.Magic);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            if (RetryBusy(() => _read(buffer)) != 0) throw new UnsupportedSignatureException("The actual V5 publication is invalid, busy or unavailable.");
            Marshal.Copy(buffer, bytes, 0, bytes.Length); return NativeEngineTransportPublication.Decode(bytes);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static uint RetryBusy(Func<uint> call)
    {
        var start = Environment.TickCount64;
        while (true) { var result = call(); if (result != 2 || Environment.TickCount64 - start >= 5000) return result; Thread.Sleep(1); }
    }
    public void MarkUnknown() => Interlocked.Exchange(ref _unknown, 1);
    public void VerifyReady()
    {
        lock (_gate)
        {
            if (Unknown) throw new UnsupportedSignatureException("V5 publication ownership is permanently Unknown.");
            try
            {
                if (_verificationDepth != 0)
                {
                    // The enclosing synchronous operation performs the full
                    // native/file/image verification on both boundaries. Each
                    // nested bridge still reads the real immutable publication
                    // and independent live image/source/relay/unwind/graph and
                    // Kernel identities; only repeated full-file SHA is shared.
                    VerifyLivePublication();
                    return;
                }
                foreach (var module in _modules) module.VerifyCurrent();
                var before = Read();
                if (!before.SameBytes(_publication) || RetryBusy(() => _verify()) != 0)
                    throw new UnsupportedSignatureException("V5 native Verify did not confirm the same retained publication.");
                var after = Read();
                if (!after.SameBytes(before) || !after.SameBytes(_publication))
                    throw new UnsupportedSignatureException("The full V5 publication changed across native read-only verification.");
                VerifyShape(); VerifyModulesAndGraph(); VerifyTypedAndCoverage(); VerifySitesAndResources(); VerifyRuntimeFunctions();
                VerifyFamilies(out _);
            }
            catch { MarkUnknown(); throw; }
        }
    }
    internal NativeEngineNodeChannelIdentity NodeIdentity(NativeEngineNodeKind kind)
    { VerifyReady(); return NodeIdentityUnchecked(kind); }
    private NativeEngineNodeChannelIdentity NodeIdentityUnchecked(NativeEngineNodeKind kind)
    {
        if ((uint)kind >= 4) throw new ArgumentOutOfRangeException(nameof(kind));
        var row = _wire.Typed[(int)kind]; return new(kind, row.HelperSlot, Pointer(row.Target), Pointer(row.Original), Pointer(row.Entry), row.HelperEpoch);
    }
    internal NativeEngineInlineChannelIdentity InlineIdentity()
    { VerifyReady(); return InlineIdentityUnchecked(); }
    private NativeEngineInlineChannelIdentity InlineIdentityUnchecked()
    {
        var family = _wire.Families[4];
        NativeEngineInlineSite Site(int i) => new() { SitePc = Pointer(_wire.Sites[i].SitePc), RelayReturnPc = Pointer(_wire.Sites[i].RelayReturnPc) };
        return new(family.Slot, family.Epoch, family.MainOsThread, Pointer(_wire.Sites[0].ProbeEntry), NodeIdentityUnchecked(NativeEngineNodeKind.Start), Site(0), Site(1), Site(2));
    }
    internal NativeEngineSchedulerChannelIdentity SchedulerIdentity(NativeEngineSchedulerKind kind)
    { VerifyReady(); return SchedulerIdentityUnchecked(kind); }
    private NativeEngineSchedulerChannelIdentity SchedulerIdentityUnchecked(NativeEngineSchedulerKind kind)
    {
        if ((uint)kind >= 6) throw new ArgumentOutOfRangeException(nameof(kind));
        var row = _wire.Typed[(int)kind + 4]; var family = _wire.Families[(int)kind + 5];
        NativeEngineSchedulerSite Site(int i) => new() { SitePc = Pointer(_wire.Sites[i].SitePc), RelayReturnPc = Pointer(_wire.Sites[i].RelayReturnPc) };
        return new(kind, family.Slot, family.Epoch, family.MainOsThread, Pointer(row.Target), Pointer(row.Original), Pointer(row.Entry),
            Pointer(_wire.Sites[3].ProbeEntry), kind == NativeEngineSchedulerKind.Drain ? Site(3) : default, kind == NativeEngineSchedulerKind.Drain ? Site(4) : default);
    }
    internal NativeEngineWaitFamilyIdentity WaitIdentity()
    { VerifyReady(); return WaitIdentityUnchecked(); }
    private NativeEngineWaitFamilyIdentity WaitIdentityUnchecked()
    {
        var family = _wire.Families[11];
        var order = new[] { 10, 11, 12, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var targets = order.Select(i => Pointer(_wire.Typed[i].Target)).ToArray();
        var originals = Enumerable.Range(10, 3).Select(i => Pointer(_wire.Typed[i].Original)).ToArray();
        var entries = Enumerable.Range(10, 3).Select(i => Pointer(_wire.Typed[i].Entry)).ToArray();
        var sites = Enumerable.Range(5, 18).Select(i => new NativeEngineWaitSite { SitePc = Pointer(_wire.Sites[i].SitePc), RelayReturnPc = Pointer(_wire.Sites[i].RelayReturnPc) }).ToArray();
        return new(family.Slot, family.Epoch, family.MainOsThread, targets, originals, entries, Pointer(_wire.Sites[5].ProbeEntry), sites);
    }
    internal void BeginFullAdoption()
    {
        lock (_gate)
        {
            try
            {
                VerifyReady();
                if (_adoption != 0 || _attached.Any(x => x)) throw new UnsupportedSignatureException("V5 whole-family adoption is one fresh permanent attempt.");
                VerifyFamilies(out var activity);
                if (Enumerable.Range(0, 12).Any(i => !FamilyIdle(i))) throw new UnsupportedSignatureException("A V5 native family is already in flight at the adoption boundary.");
                _adoptionActivity = activity; _adoption = 1;
            }
            catch { MarkUnknown(); throw; }
        }
    }
    internal void CompleteFullAdoption()
    {
        lock (_gate)
        {
            try
            {
                VerifyReady(); VerifyFamilies(out var activity);
                if (_adoption != 1 || _adoptionActivity is null || !_attached.All(x => x)
                    || !_adoptionActivity.AsSpan().SequenceEqual(activity))
                    throw new UnsupportedSignatureException("Native work crossed a partial V5 Attach window or a family was not attached.");
                _adoption = 2;
            }
            catch { MarkUnknown(); throw; }
        }
    }
    private static IntPtr Pointer(ulong value) => new(unchecked((long)value));
    private static bool IsZero(ReadOnlySpan<byte> values) { foreach (var value in values) if (value != 0) return false; return true; }
    private static void Require(bool value, string reason) { if (!value) throw new UnsupportedSignatureException(reason); }
}
