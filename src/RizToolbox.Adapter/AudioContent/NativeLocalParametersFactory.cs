using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Copied constructor output. Scalar defaults are observations, never assumed or overwritten.</summary>
internal sealed record LocalNativeParametersObservation(long Metadata, long CoverReference, long MusicReference,
    long Chart, long JudgeData, float Speed, float Offset)
{
    internal int SpeedBits => BitConverter.SingleToInt32Bits(Speed);
    internal int OffsetBits => BitConverter.SingleToInt32Bits(Offset);
}

/// <summary>
/// Creates only InsideLevelParams and its GC root. All five inputs remain borrowed: their callers
/// must keep their owners active until this handle is disposed. No resource ownership is transferred,
/// no wrapper Release/Unity Destroy is called, and this factory grants no ability to launch a level.
/// </summary>
internal sealed class NativeLocalParametersFactory
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private const string Sprite = "UnityEngine.Sprite", Audio = "UnityEngine.AudioClip";
    private static readonly string[] ConstructorTypes = {
        "ILevelMetadata", "IResourceReference<UnityEngine.Sprite>",
        "IResourceReference<UnityEngine.AudioClip>", "InLevel.Chart", "JudgeData"
    };
    private static readonly string[] ReferenceFields = { "metadata", "coverReference", "musicReference", "chart", "judgeData" };
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private bool _buildVerified;

    internal NativeLocalParametersFactory(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr gameAssemblyModule)
        => (_api, _main, _profile, _module) = (api, main, profile, gameAssemblyModule);

    internal BorrowedNativeLocalParameters CreateBorrowing(OwnedNativeLocalMetadata metadata,
        LocalNativeMetadataDescriptor expectedDescriptor, NativeResourceReferenceHandle cover,
        NativeResourceReferenceHandle audio, NativeRuntime.RootHandle chart, NativeRuntime.RootHandle judgeData)
    {
        // No caller object, RootHandle.Target or ownership operation precedes this gate.
        _main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(expectedDescriptor);
        ArgumentNullException.ThrowIfNull(cover); ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(chart); ArgumentNullException.ThrowIfNull(judgeData);
        if (metadata.Descriptor != expectedDescriptor)
            throw new ArgumentException("Metadata owner differs from the exact expected LOCAL descriptor/revision.", nameof(expectedDescriptor));
        if (cover.ResourceType != Sprite || audio.ResourceType != Audio)
            throw new UnsupportedSignatureException("InsideLevelParams requires Sprite and AudioClip wrapper owners in that order.");
        if (metadata.IsDisposed || metadata.CleanupUnknown || cover.IsReleased || cover.CleanupFailed || audio.IsReleased || audio.CleanupFailed)
            throw new ObjectDisposedException("borrowed input owner");

        // The unsupported-build gate precedes even gchandle target lookups on caller roots.
        RequireBuild();
        IntPtr[] BorrowedPointers() => new[] { metadata.Pointer, cover.Pointer, audio.Pointer, chart.Target, judgeData.Target };
        var roots = new BorrowedLocalParametersRoots(_main);
        try
        {
            // All five inputs are rooted before native metadata inspection can allocate.
            var guards = new NativeRuntime.RootHandle[5];
            var inputPointers = BorrowedPointers();
            for (var i = 0; i < guards.Length; i++)
            {
                var pointer = inputPointers[i];
                guards[i] = roots.Allocate(() => _api.Root(pointer));
            }
            var parametersClass = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
            var metadataClass = _api.Class("Assembly-CSharp.dll", "", "LevelMetadata");
            var chartClass = _api.Class("Assembly-CSharp.dll", "InLevel", "Chart");
            var judgeClass = _api.Class("Assembly-CSharp.dll", "", "JudgeData");
            if (_api.ObjectClass(guards[0].Target) != metadataClass || _api.ObjectClass(guards[3].Target) != chartClass
                || _api.ObjectClass(guards[4].Target) != judgeClass)
                throw new UnsupportedSignatureException("Borrowed metadata, Chart or JudgeData differs from its exact native class.");

            var constructor = _api.InspectDeclaredMethod(parametersClass, ".ctor", ConstructorTypes, "System.Void", _module);
            if (constructor.Entry is not { Rva: 0x375300 })
                throw new UnsupportedSignatureException("InsideLevelParams constructor has no verified pinned entry at RVA 0x375300: " + constructor.EntryStatus);
            for (var i = 0; i < guards.Length; i++)
            {
                var fieldClass = _api.InspectMetadataFieldClass(parametersClass, ReferenceFields[i], ConstructorTypes[i]).FieldClass.Class;
                var parameterClass = _api.InspectMethodParameterClass(constructor.MethodInfo, (uint)i, ConstructorTypes[i]).Class;
                if (fieldClass != parameterClass)
                    throw new UnsupportedSignatureException("InsideLevelParams field and constructor parameter metadata disagree.");
                _api.RequireAssignable(parameterClass, _api.ObjectClass(guards[i].Target));
            }

            var unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            var unityAlive = _api.Method(unityObject, "op_Implicit", 1, true, new[] { "UnityEngine.Object" });
            var coverGetter = Getter(guards[1].Target, Sprite);
            var audioGetter = Getter(guards[2].Target, Audio);
            var coverResource = roots.Allocate(cover.BorrowResource);
            var audioResource = roots.Allocate(audio.BorrowResource);
            var coverPointer = coverResource.Target; var audioPointer = audioResource.Target;
            var spriteClass = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Sprite");
            var audioClass = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");

            void VerifyBorrowed()
            {
                var current = BorrowedPointers(); // Owner disposal is meaningful even if params still references its objects.
                if (metadata.Descriptor != expectedDescriptor)
                    throw new TargetExpiredException("Borrowed LOCAL descriptor changed.");
                for (var i = 0; i < current.Length; i++)
                    if (current[i] != inputPointers[i])
                        throw new TargetExpiredException("Borrowed InsideLevelParams input identity changed.");
                // Caller-owned wrapper resource roots remain active. Require each getter to keep
                // returning that original checked identity before inspecting its Unity liveness.
                if (_api.Invoke(coverGetter, current[1]) != coverPointer || _api.Invoke(audioGetter, current[2]) != audioPointer
                    || _api.ObjectClass(coverPointer) != spriteClass || _api.ObjectClass(audioPointer) != audioClass)
                    throw new UnsupportedSignatureException("Borrowed wrapper getter changed its exact Sprite/AudioClip resource.");
                if (!_api.Unbox<bool>(_api.Invoke(unityAlive, IntPtr.Zero, A.Ref(coverPointer)))
                    || !_api.Unbox<bool>(_api.Invoke(unityAlive, IntPtr.Zero, A.Ref(audioPointer))))
                    throw new TargetExpiredException("Borrowed InsideLevelParams resource was destroyed.");
                metadata.Read(); // Revalidate the isolated native graph against its own descriptor.
            }
            VerifyBorrowed();
            var parameters = roots.Allocate(() => _api.NewExact(parametersClass, ConstructorTypes,
                A.Ref(guards[0].Target), A.Ref(guards[1].Target), A.Ref(guards[2].Target),
                A.Ref(guards[3].Target), A.Ref(guards[4].Target)));
            LocalNativeParametersObservation Read()
            {
                if (_api.ObjectClass(parameters.Target) != parametersClass)
                    throw new UnsupportedSignatureException("InsideLevelParams construction returned a different exact class.");
                var values = ReferenceFields.Select(name => _api.Read<IntPtr>(parameters.Target, name)).ToArray();
                if (!values.SequenceEqual(BorrowedPointers()))
                    throw new UnsupportedSignatureException("InsideLevelParams did not preserve all five borrowed references.");
                // Read<float> verifies the native scalar layout. Do not set or guess its defaults.
                return new(values[0].ToInt64(), values[1].ToInt64(), values[2].ToInt64(), values[3].ToInt64(),
                    values[4].ToInt64(), _api.Read<float>(parameters.Target, "speed"), _api.Read<float>(parameters.Target, "offset"));
            }
            var owner = new BorrowedNativeLocalParameters(_main, roots, parameters, VerifyBorrowed, Read);
            owner.Read();
            roots.KeepOnly(parameters); // Success leaves exactly one new root; all caller roots remain untouched.
            return owner;
        }
        catch (Exception failure)
        {
            if (!roots.CleanupUnknown)
                try { roots.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Borrowed parameter creation failed and root cleanup is unknown.", failure, cleanup); }
            throw;
        }

        IntPtr Getter(IntPtr wrapper, string resourceType)
        {
            var cls = _api.ObjectClass(wrapper);
            if (_api.InspectMetadataClass(cls).TypeName != "ResourceReference<" + resourceType + ">")
                throw new UnsupportedSignatureException("Borrowed input is not the exact ResourceReference closed wrapper.");
            var getter = _api.InspectDeclaredMethod(cls, "get_Resource", Array.Empty<string>(), resourceType, _module);
            if (getter.Entry is null)
                throw new UnsupportedSignatureException("Borrowed wrapper getter has no verified pinned executable entry.");
            return getter.MethodInfo;
        }
    }

    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Borrowed LOCAL parameters require the pinned Windows x64 Unity 2022.3.62f2 build.");
        if (_buildVerified) return;
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(file); using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            throw new UnsupportedSignatureException("Borrowed LOCAL parameters game image fingerprint differs from the pinned profile.");
        _buildVerified = true;
    }
}

/// <summary>Owns only the new parameter GC root, with explicitly borrowed resource/input owners.</summary>
internal sealed class BorrowedNativeLocalParameters : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly BorrowedLocalParametersRoots _roots;
    private readonly NativeRuntime.RootHandle _parameters;
    private readonly Action _verifyBorrowed;
    private readonly Func<LocalNativeParametersObservation> _read;
    private LocalNativeParametersObservation? _observation;
    internal BorrowedNativeLocalParameters(IMainThreadDispatcher main, BorrowedLocalParametersRoots roots,
        NativeRuntime.RootHandle parameters, Action verifyBorrowed, Func<LocalNativeParametersObservation> read)
        => (_main, _roots, _parameters, _verifyBorrowed, _read) = (main, roots, parameters, verifyBorrowed, read);
    internal bool IsDisposed => _roots.IsDisposed;
    internal bool CleanupUnknown => _roots.CleanupUnknown;
    internal int RootCount { get { _main.VerifyAccess(); return _roots.RootCount; } }
    internal IntPtr Pointer { get { VerifyActive(); return _parameters.Target; } }
    internal LocalNativeParametersObservation Observation
    { get { VerifyActive(); return _observation ?? throw new InvalidOperationException("No parameter observation is available."); } }
    private void VerifyActive() { _main.VerifyAccess(); _roots.VerifyActive(); _verifyBorrowed(); }
    internal LocalNativeParametersObservation Read()
    { VerifyActive(); _observation = null; return _observation = _read(); }
    public void Dispose() { _main.VerifyAccess(); _observation = null; _roots.Dispose(); }
}

/// <summary>Bounded responsibility for new GC roots only. Failed frees remain process-owned and are not retried.</summary>
internal sealed class BorrowedLocalParametersRoots : IDisposable
{
    private const int MaximumOwners = 32, MaximumRoots = 8;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<BorrowedLocalParametersRoots> ProcessOwners = new();
    private static bool _unknownCleanup;
    private readonly IMainThreadDispatcher _main;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[MaximumRoots];
    private int _count;
    private bool _disposeStarted;
    private Exception? _failure;
    internal BorrowedLocalParametersRoots(IMainThreadDispatcher main)
    {
        _main = main; _main.VerifyAccess();
        lock (ProcessGate)
        {
            if (_unknownCleanup || ProcessOwners.Count >= MaximumOwners)
                throw new InvalidOperationException("Borrowed parameter root capacity is exhausted or a prior free is unknown.");
            ProcessOwners.Add(this);
        }
    }
    internal bool IsDisposed => _disposeStarted;
    internal bool CleanupUnknown => _failure is not null;
    internal int RootCount => _roots.Count(x => x is not null);
    internal void VerifyActive()
    { _main.VerifyAccess(); if (_disposeStarted) throw new ObjectDisposedException(nameof(BorrowedNativeLocalParameters)); }
    internal NativeRuntime.RootHandle Allocate(Func<NativeRuntime.RootHandle> create)
    {
        VerifyActive();
        if (_count >= MaximumRoots) throw new InvalidOperationException("Borrowed parameter creation exceeds eight new GC roots.");
        var root = create(); _roots[_count++] = root; return root;
    }
    internal void KeepOnly(NativeRuntime.RootHandle keep)
    {
        VerifyActive();
        if (!_roots.Any(root => ReferenceEquals(root, keep))) throw new ArgumentException("Parameter root is not owned by this reservation.", nameof(keep));
        var failures = FreeExcept(keep);
        if (failures.Count == 0) return;
        // Creation cannot succeed after a temporary free is unknown. Independently attempt
        // the output root too, then preserve failed records and quota without any retry.
        failures.AddRange(FreeExcept(null, keep));
        Fail(failures);
    }
    private List<Exception> FreeExcept(NativeRuntime.RootHandle? keep, NativeRuntime.RootHandle? only = null)
    {
        var failures = new List<Exception>();
        for (var i = _count - 1; i >= 0; i--)
        {
            var root = _roots[i];
            if (root is null || ReferenceEquals(root, keep) || only is not null && !ReferenceEquals(root, only)) continue;
            try { root.Dispose(); _roots[i] = null; } catch (Exception error) { failures.Add(error); }
        }
        return failures;
    }
    private void Fail(List<Exception> failures)
    {
        _disposeStarted = true;
        _failure = new AggregateException("Borrowed parameter GC root cleanup is unknown; process responsibility is retained.", failures);
        lock (ProcessGate) _unknownCleanup = true;
        throw _failure;
    }
    public void Dispose()
    {
        _main.VerifyAccess();
        if (_failure is not null) throw new InvalidOperationException("Unknown parameter root free will not be retried.", _failure);
        if (_disposeStarted) return;
        _disposeStarted = true; var failures = FreeExcept(null);
        if (failures.Count != 0) Fail(failures);
        lock (ProcessGate) ProcessOwners.Remove(this);
    }
}
