using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Adapter.Visual;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record LocalBundleJudgeObservation(float JudgeTime, float NormalChallengeTime, float MaxChallengeTime, float BadTime);
internal sealed record LocalNativePlayBundleObservation(long ParametersIdentity, LocalNativeParametersObservation Parameters,
    LocalNativeMetadataObservation Metadata, LocalBundleJudgeObservation Judge, long Texture, long Sprite, long Audio,
    int AudioFrames, int AudioChannels, int AudioFrequency, int CoverWidth, int CoverHeight, int ChartLines,
    int ChartNotes, long EstimatedUploadBytes, bool CanLaunch = false);

/// <summary>Creates the complete per-run resource graph. A caller supplies verified real judge settings;
/// this factory neither guesses the selected judging mode nor activates a controller.</summary>
internal sealed class NativeLocalPlayBundleFactory
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile; private readonly IntPtr _module;
    internal NativeLocalPlayBundleFactory(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile, IntPtr module)
        => (_api, _main, _profile, _module) = (api, main, profile, module);

    internal OwnedNativeLocalPlayBundle CreateBorrowing(LocalPlayRequest request, LocalNativeMetadataDescriptor descriptor,
        NativeRuntime.RootHandle judgeSource)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(judgeSource);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("LOCAL bundles require the pinned Windows x64 Unity build.");
        var path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var file = File.OpenRead(path)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                throw new UnsupportedSignatureException("LOCAL bundle image differs from its pinned profile.");
        // Reserve before retaining CPU/native responsibility; failures after adoption carry
        // the owner back to the caller and remain strongly process-owned even if abandoned.
        var owner = new OwnedNativeLocalPlayBundle(_api, _main);
        try { owner.Build(request, descriptor, judgeSource, _profile, _module); return owner; }
        catch (Exception error) { owner.MarkCreationFailed(error.Message); throw new NativeLocalPlayBundleCreationException(owner, error); }
    }
}

internal sealed class NativeLocalPlayBundleCreationException : InvalidOperationException
{
    internal OwnedNativeLocalPlayBundle Responsibility { get; }
    internal NativeLocalPlayBundleCreationException(OwnedNativeLocalPlayBundle responsibility, Exception cause)
        : base("LOCAL bundle construction failed; explicit bounded cleanup responsibility is retained.", cause) => Responsibility = responsibility;
}

/// <summary>One upload owner and one native graph per run. Wrappers are non-destroying;
/// only this owner destroys its Texture/Sprite/Audio, after verified consumers end.</summary>
internal sealed class OwnedNativeLocalPlayBundle : IDisposable
{
    private const int MaximumBundles = 32;
    private const long MaximumProcessUploadBytes = 128L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly HashSet<OwnedNativeLocalPlayBundle> ProcessOwners = new();
    private static long ProcessUploadBytes;
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    // Fixed records are allocated before any native allocation. Root/free failures must
    // not lose a transferred object behind a growing collection allocation.
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[5];
    private readonly NativePublicationArgumentRecords _callArguments;
    private LocalPlayBundleInputs? _inputs;
    private OwnedNativeLocalMetadata? _metadata;
    private NativeResourceReferenceHandle? _cover, _music;
    private BorrowedNativeLocalParameters? _parameters;
    private NativeRuntime.RootHandle? _upload;
    private IntPtr _unityObject;
    private long _bytes;
    private bool _ready, _destroyRequested, _freeAttempted, _closed, _unknown, _unconfirmedCall, _buildAttempted, _budgetReserved, _uploadFreeAttempted;
    private LocalRunSessionRegistry? _registry;
    private LocalRunSessionRegistry.SessionHandle? _session;
    private LocalBundleJudgeObservation? _judge;
    internal bool IsClosed { get { _main.VerifyAccess(); return _closed; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _unknown || _unconfirmedCall || _callArguments.CleanupUnknown; } }
    internal bool DestroyRequested { get { _main.VerifyAccess(); return _destroyRequested; } }
    internal int DirectRootRecords { get { _main.VerifyAccess(); return _roots.Count(x => x is not null) + (_upload is null ? 0 : 1); } }
    internal int RetainedCallArgumentRecords => _callArguments.Count;
    internal string? Failure { get; private set; }
    internal long MetadataIdentity { get { GuardRead(); return _metadata!.NativeIdentity; } }
    internal LocalSongSelectionSnapshot Selection { get { GuardRead(); return _inputs!.Selection; } }
    internal OwnedNativeLocalPlayBundle(NativeRuntime api, IMainThreadDispatcher main)
    {
        (_api, _main) = (api, main); main.VerifyAccess();
        _callArguments = new(main);
        lock (Gate)
        {
            if (ProcessOwners.Count >= MaximumBundles) throw new InvalidOperationException("Live/unknown LOCAL bundles exhaust the 32-owner process quota.");
            ProcessOwners.Add(this);
        }
    }
    internal void Build(LocalPlayRequest request, LocalNativeMetadataDescriptor descriptor, NativeRuntime.RootHandle judgeSource,
        AdapterBuildProfile profile, IntPtr module)
    {
        _main.VerifyAccess(); if (_buildAttempted || _destroyRequested) throw new InvalidOperationException("Bundle construction is one-shot.");
        _buildAttempted = true;
        _inputs = LocalPlayBundleInputs.CaptureBorrowing(_main, request, descriptor);
        ReserveUploads(_inputs.EstimatedNativeUploadBytes);
        _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var judgeClass = _api.Class("Assembly-CSharp.dll", "", "JudgeData");
        if (_api.ObjectClass(judgeSource.Target) != judgeClass)
            throw new UnsupportedSignatureException("Bundle judge source must be an exact caller-retained JudgeData.");
        var fields = new[] { "judgeTime", "normalPerfectJudgeTimeInChallengeTime", "maxPerfectJudgeTimeInChallengeTime", "badTime" };
        for (var i = 0; i < fields.Length; i++) _api.RequireInstanceFieldLayout(judgeClass, fields[i], "System.Single", 0x10 + i * 4);
        var judgeValues = fields.Select(name => _api.Read<float>(judgeSource.Target, name)).ToArray();
        if (judgeValues.Any(value => !float.IsFinite(value) || value <= 0))
            throw new UnsupportedSignatureException("Zero/default or nonfinite judging windows cannot form a playable resource graph; provide real selected settings.");
        var ctor = _api.InspectDeclaredMethod(judgeClass, ".ctor", Array.Empty<string>(), "System.Void", module);
        if (ctor.Entry is not { Rva: 0x36C310 }) throw new UnsupportedSignatureException("JudgeData constructor is not pinned.");
        _unconfirmedCall = true;
        _metadata = new NativeLocalMetadataFactory(_api, _main, profile, module).Create(_inputs.Descriptor);
        _unconfirmedCall = false;
        var image = _inputs.Cover; var pcm = _inputs.Audio;
        var texture = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Texture2D");
        ConstructRoot(0, texture, new[] { "System.Int32", "System.Int32" }, A.Value(image.Width), A.Value(image.Height));
        if (!_api.Unbox<bool>(_api.CallTyped(Root(0), "Reinitialize", new[] { "System.Int32", "System.Int32", "UnityEngine.TextureFormat", "System.Boolean" },
                A.Value(image.Width), A.Value(image.Height), A.Value(UnityEngine.TextureFormat.RGBA32), A.Value(false)))
            || _api.Unbox<UnityEngine.TextureFormat>(_api.Call(Root(0), "get_format")) != UnityEngine.TextureFormat.RGBA32)
            throw new InvalidDataException("LOCAL cover RGBA32 storage was refused.");
        var pixels = new byte[checked(image.Width * image.Height * 4)];
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
        {
            var c = image.Pixels[y * image.Width + x]; var at = ((image.Height - y - 1) * image.Width + x) * 4;
            pixels[at] = (byte)Math.Round(c.R * 255); pixels[at + 1] = (byte)Math.Round(c.G * 255);
            pixels[at + 2] = (byte)Math.Round(c.B * 255); pixels[at + 3] = (byte)Math.Round(c.Alpha * 255);
        }
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try { Marshal.Copy(pixels, 0, buffer, pixels.Length); _api.CallTyped(Root(0), "LoadRawTextureData", new[] { "System.IntPtr", "System.Int32" }, A.Value(buffer), A.Value(pixels.Length)); }
        finally { Marshal.FreeHGlobal(buffer); }
        _api.Call(Root(0), "Apply", A.Value(false), A.Value(true));
        var sprite = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Sprite");
        var createSprite = _api.Method(sprite, "Create", 4, true, new[] { "UnityEngine.Rect", "UnityEngine.Vector2", "System.Single", "UnityEngine.Texture2D" });
        InvokeRoot(1, createSprite, A.Value(new NativeRect(0, 0, image.Width, image.Height)),
            A.Value(new Vec2(.5f, .5f)), A.Value(100f), A.Ref(Root(0)));
        var audio = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        var createAudio = _api.Method(audio, "Create", 5, true, new[] { "System.String", "System.Int32", "System.Int32", "System.Int32", "System.Boolean" });
        InvokeRoot(2, createAudio, _api.Str("RizToolbox.LOCAL." + Guid.NewGuid().ToString("N")),
            A.Value(pcm.SampleFrames), A.Value(pcm.Channels), A.Value(pcm.Frequency), A.Value(false));
        RetainUpload(publish => _api.CreateSingleArrayWithPublication(pcm.Samples, publish));
        if (!_api.Unbox<bool>(_api.CallTyped(Root(2), "SetData", new[] { "System.Single[]", "System.Int32" }, A.Ref(_upload!.Target), A.Value(0))))
            throw new InvalidDataException("LOCAL PCM upload was refused.");
        // Mark before free; an unknown free remains in its slot and is never retried.
        ReleaseUpload();
        var chartClass = _api.Class("Assembly-CSharp.dll", "InLevel", "Chart");
        var json = _api.Class("UnityEngine.JSONSerializeModule.dll", "UnityEngine", "JsonUtility");
        var fromJson = _api.Method(json, "FromJson", 2, true, new[] { "System.String", "System.Type" });
        InvokeRoot(3, fromJson, _api.Str(_inputs.Chart.Json), A.Ref(_api.Type(chartClass)));
        var chartBpm = _api.Read<float>(Root(3), "bPM");
        if (_api.ObjectClass(Root(3)) != chartClass || _api.Read<int>(Root(3), "fileVersion") != 0
            || !float.IsFinite(chartBpm) || Math.Abs(chartBpm - _inputs.Chart.Bpm) > .0001f
            || _api.Unbox<int>(_api.Call(_api.Read<IntPtr>(Root(3), "lines"), "get_Count")) != _inputs.Chart.Lines)
            throw new InvalidDataException("Fresh per-run Chart did not preserve its selected validated source.");
        ConstructRoot(4, judgeClass, Array.Empty<string>());
        if (Root(4) == judgeSource.Target) throw new InvalidDataException("Bundle judge settings must be an independent owned copy.");
        for (var i = 0; i < fields.Length; i++)
        {
            _api.WriteExact(_roots[4]!, judgeClass, fields[i], "System.Single", A.Value(judgeValues[i]));
            if (BitConverter.SingleToInt32Bits(_api.Read<float>(Root(4), fields[i])) != BitConverter.SingleToInt32Bits(judgeValues[i]))
                throw new InvalidDataException("Copied per-run judge window changed.");
        }
        _judge = new(judgeValues[0], judgeValues[1], judgeValues[2], judgeValues[3]);
        var references = new NativeResourceReferenceFactory(_api, _main, profile);
        _unconfirmedCall = true; _cover = references.CreateSprite(Root(1), false); _unconfirmedCall = false;
        _unconfirmedCall = true; _music = references.CreateAudio(Root(2), false); _unconfirmedCall = false;
        _unconfirmedCall = true;
        _parameters = new NativeLocalParametersFactory(_api, _main, profile, module).CreateBorrowing(
            _metadata, _inputs.Descriptor, _cover, _music, _roots[3]!, _roots[4]!);
        _unconfirmedCall = false; _ready = true; Read();
    }
    internal void InvokeRoot(int slot, IntPtr method, params A[] args)
    {
        _main.VerifyAccess();
        if (_destroyRequested || _closed || _unknown || _unconfirmedCall) throw new InvalidOperationException("Bundle creation is stopped or unconfirmed.");
        if (_roots[slot] is not null) throw new InvalidOperationException("Bundle root slot is already occupied.");
        _callArguments.Begin(args);
        _unconfirmedCall = true;
        try { _api.InvokeReferenceWithPublication(method, IntPtr.Zero, pointer => _roots[slot] = _api.Root(pointer), args); }
        catch { _callArguments.Fail(); throw; }
        _callArguments.Complete();
        _unconfirmedCall = false;
    }
    internal void ConstructRoot(int slot, IntPtr cls, string[] parameterTypes, params A[] args)
    {
        _main.VerifyAccess();
        if (_destroyRequested || _closed || _unknown || _unconfirmedCall) throw new InvalidOperationException("Bundle creation is stopped or unconfirmed.");
        if (_roots[slot] is not null) throw new InvalidOperationException("Bundle root slot is already occupied.");
        _callArguments.Begin(args);
        _unconfirmedCall = true;
        try { _api.NewExactWithPublication(cls, parameterTypes, root => _roots[slot] = root, args); }
        catch { _callArguments.Fail(); throw; }
        _callArguments.Complete();
        _unconfirmedCall = false;
    }
    internal void ReserveUploads(long bytes)
    {
        _main.VerifyAccess();
        if (_budgetReserved || _destroyRequested || _closed) throw new InvalidOperationException("Bundle upload reservation is one-shot.");
        lock (Gate)
        {
            if (bytes <= 0 || bytes > 64L * 1024 * 1024 || bytes > MaximumProcessUploadBytes - ProcessUploadBytes)
                throw new InvalidOperationException("LOCAL upload estimates exceed per-bundle/process limits, including unknown cleanup.");
            _bytes = bytes; ProcessUploadBytes += bytes; _budgetReserved = true;
        }
    }
    /// <summary>Records the temporary pinned PCM upload independently of SetData success.</summary>
    internal void RetainUpload(Action<Action<NativeRuntime.RootHandle>> create)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(create);
        if (_upload is not null || _uploadFreeAttempted || _destroyRequested || _closed || _unknown || _unconfirmedCall)
            throw new InvalidOperationException("The bundle upload slot is already consumed or cleanup is unknown.");
        _unconfirmedCall = true;
        create(root => _upload = root);
        if (_upload is null) throw new InvalidOperationException("Upload allocation returned without publishing its pinned output.");
        _unconfirmedCall = false;
    }
    internal void ReleaseUpload()
    {
        _main.VerifyAccess();
        if (_uploadFreeAttempted) throw new InvalidOperationException("An unknown upload free cannot be retried.");
        if (_upload is null) return;
        _uploadFreeAttempted = true; _unconfirmedCall = true;
        _upload.Dispose(); _upload = null; _unconfirmedCall = false;
    }
    internal void MarkCreationFailed(string message) { _main.VerifyAccess(); _ready = false; Failure = message; }
    private IntPtr Root(int slot) => _roots[slot]!.Target;
    private bool Alive(IntPtr pointer) => _api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(pointer)));
    private void GuardRead()
    {
        _main.VerifyAccess(); if (!_ready || _destroyRequested || _closed || _unknown || _unconfirmedCall)
            throw new ObjectDisposedException(nameof(OwnedNativeLocalPlayBundle));
    }
    internal LocalNativePlayBundleObservation Read()
    {
        GuardRead(); var image = _inputs!.Cover; var pcm = _inputs.Audio;
        if (!Alive(Root(0)) || !Alive(Root(1)) || !Alive(Root(2))) throw new TargetExpiredException("An owned bundle resource was destroyed.");
        var width = _api.Unbox<int>(_api.Call(Root(0), "get_width")); var height = _api.Unbox<int>(_api.Call(Root(0), "get_height"));
        var frames = _api.Unbox<int>(_api.Call(Root(2), "get_samples")); var channels = _api.Unbox<int>(_api.Call(Root(2), "get_channels"));
        var frequency = _api.Unbox<int>(_api.Call(Root(2), "get_frequency"));
        if (width != image.Width || height != image.Height || frames != pcm.SampleFrames || channels != pcm.Channels || frequency != pcm.Frequency)
            throw new InvalidDataException("Owned bundle image/audio geometry changed.");
        var names = new[] { "judgeTime", "normalPerfectJudgeTimeInChallengeTime", "maxPerfectJudgeTimeInChallengeTime", "badTime" };
        var values = names.Select(name => _api.Read<float>(Root(4), name)).ToArray();
        var actualJudge = new LocalBundleJudgeObservation(values[0], values[1], values[2], values[3]);
        var chartBpm = _api.Read<float>(Root(3), "bPM");
        if (actualJudge != _judge || _api.Read<int>(Root(3), "fileVersion") != 0
            || !float.IsFinite(chartBpm) || Math.Abs(chartBpm - _inputs.Chart.Bpm) > .0001f
            || _api.Unbox<int>(_api.Call(_api.Read<IntPtr>(Root(3), "lines"), "get_Count")) != _inputs.Chart.Lines)
            throw new InvalidDataException("Per-run Chart or copied judge settings changed.");
        return new(_parameters!.Pointer.ToInt64(), _parameters.Read(), _metadata!.Read(), _judge!, Root(0).ToInt64(), Root(1).ToInt64(), Root(2).ToInt64(),
            frames, channels, frequency, width, height, _inputs.Chart.Lines, _inputs.Chart.Notes, _bytes);
    }

    /// <summary>Records consumer responsibility before exporting a parameter root. Session.Close
    /// must retire protection with its exact verified token before Dispose can request destruction.</summary>
    internal NativeRuntime.RootHandle RetainParametersForPendingSession(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
    {
        GuardRead(); ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(claim);
        if (_session is not null) throw new InvalidOperationException("A bundle can feed only one native run; retry requires a fresh bundle.");
        registry.VerifyPendingBundleClaim(claim, _metadata!.NativeIdentity, this);
        _registry = registry; _session = claim.Session;
        return _api.Root(_parameters!.Pointer);
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_closed || _destroyRequested) return;
        if (_session is not null)
        {
            var snapshot = _registry!.Read(_session);
            if (!snapshot.ProtectionRetired || !snapshot.CleanupAttempted || string.IsNullOrWhiteSpace(snapshot.ConsumerEndEvidence))
                throw new InvalidOperationException("Attached bundle consumers have not ended under the exact session's verified retirement.");
        }
        _destroyRequested = true; _ready = false;
        var failures = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        Attempt(() => _parameters?.Dispose()); Attempt(() => _cover?.Dispose()); Attempt(() => _music?.Dispose());
        foreach (var slot in new[] { 1, 2, 0 })
            if (_roots[slot] is { } root) Attempt(() => {
                if (Alive(root.Target)) _api.Invoke(_api.Method(_unityObject, "Destroy", 1, true, new[] { "UnityEngine.Object" }), IntPtr.Zero, A.Ref(root.Target));
            });
        if (failures.Count != 0) { _unknown = true; Failure = "Bundle destruction request has an unknown outcome."; throw new AggregateException(Failure, failures); }
    }
    internal bool TryCompleteDestroy()
    {
        _main.VerifyAccess(); if (_closed) return true;
        if (!_destroyRequested || _unknown || _unconfirmedCall || _freeAttempted) return false;
        try { foreach (var slot in new[] { 0, 1, 2 }) if (_roots[slot] is { } root && Alive(root.Target)) return false; }
        catch (Exception error) { _unknown = true; Failure = "Bundle destruction observation is unknown; no frees are attempted."; throw new InvalidOperationException(Failure, error); }
        _freeAttempted = true; var failures = new List<Exception>();
        if (_upload is not null)
        {
            if (_uploadFreeAttempted) failures.Add(new InvalidOperationException("A previously attempted upload free is unknown and cannot be retried."));
            else
            {
                try { ReleaseUpload(); } catch (Exception error) { failures.Add(error); }
            }
        }
        for (var i = _roots.Length - 1; i >= 0; i--)
            if (_roots[i] is { } root) try { root.Dispose(); _roots[i] = null; } catch (Exception error) { failures.Add(error); }
        try { _metadata?.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count == 0) try { _inputs?.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0 || DirectRootRecords != 0) { _unknown = true; Failure = "Bundle cleanup is unknown; native frees will not be retried."; throw new AggregateException(Failure, failures); }
        _closed = true;
        lock (Gate) { ProcessUploadBytes -= _bytes; ProcessOwners.Remove(this); }
        return true;
    }
}
