using System.Runtime.ExceptionServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>One post-observation stock graph, seven independent roots and one
/// real joint native source-epoch ticket. This is source identity only: a later
/// Clock issuer still needs its completed physical baseline and own admission.</summary>
internal sealed partial class NativePlaybackFreshSourceCapture : IDisposable
{
    private readonly NativePlaybackFreshSourceCapture _registered;
    private readonly NativeRuntime _api;
    private readonly GameEventState.PlaybackRunAssociation _run;
    private readonly NativeRuntime.NativePlaybackStockTargets _stock;
    private readonly PlaybackSourceGroupLease _group;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<NativeRuntime.RootHandle> _roots = new();
    private readonly List<IntPtr> _classes = new();
    private NativePlaybackSingleReturnBridge.FreshSourceReceipt? _ticket;
    private IDisposable? _withdrawal;
    private NativePlaybackBinding? _binding;
    private NativePlaybackRateOrigin? _origin;
    private bool _stageStarted, _claimed, _closed, _rootsReleased;
    private string? _failure;
    private ExceptionDispatchInfo? _rootReleaseFailure;

    private NativePlaybackFreshSourceCapture(NativeRuntime api, GameEventState.PlaybackRunAssociation run,
        NativeRuntime.NativePlaybackStockTargets stock, PlaybackSourceGroupLease group)
        => (_registered, _api, _run, _stock, _group) = (this, api, run, stock, group);

    internal static NativePlaybackFreshSourceCapture Capture(NativeRuntime api,
        GameEventState.PlaybackRunAssociation run, NativeRuntime.NativePlaybackStockTargets stock,
        PlaybackSourceGroupLease group)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stock); ArgumentNullException.ThrowIfNull(group);
        // CapturePlaybackRun rejects the observer's uncommitted Poll mutation;
        // this rechecks that same real host-issued association before rooting.
        run.VerifyHostCurrent(); group.RequireStockFreshSource(stock);
        var capture = new NativePlaybackFreshSourceCapture(api, run, stock, group);
        try
        {
            capture.RetainGraph(); capture.VerifyGraph();
            capture._withdrawal = run.OnHostWithdrawn(capture.HostWithdrawn);
            capture._stageStarted = true;
            capture._ticket = group.StageStockFreshSource(capture, api, stock,
                capture._roots[4], capture._roots[5], capture._roots[2], capture._roots[6]);
            capture.VerifyGraph();
            return capture; // No binding or guessed SourceGeneration exists yet.
        }
        catch (Exception error)
        {
            capture.Invalidate("Fresh source staging failed: " + error.Message);
            throw;
        }
    }

    internal bool TryIssue()
    {
        Open(); using var read = EnterRead();
        if (_claimed) { VerifyCurrent(); return true; }
        try
        {
            VerifyGraph();
            if (!_ticket!.TryIssue()) return false; // Same roots, watch and native phase0 candidate.
            var native = _ticket.Read();
            _group.VerifyStockFreshSource(this, _stock, _ticket);
            VerifyNativeGraph(native.Source); VerifyGraph();
            if (!_ticket.TryClaim()) return false;
            _claimed = true;
            _binding = new(_run.RunId, _run.SceneGeneration, _run.Epoch, checked((long)native.SourceEpoch),
                _run.Controller, _run.Information, Target(2), Target(4), Target(5), Target(6), _stock.GetTime.MethodInfo.ToInt64());
            _origin = new(_run.RunId, _run.Epoch, _run.Controller, _run.Information, Target(2), Target(3));
            VerifyInstallationCurrent(); return true;
        }
        catch (Exception error) { Invalidate("Fresh source issuance failed: " + error.Message); throw; }
    }
    // A projection cannot be handed to the historical caller-supplied binding
    // constructor as admission. Its future consumer must retain this exact owner
    // and call VerifyCurrent for the same private native ticket throughout use.
    internal NativePlaybackBinding Binding { get { VerifyCurrent(); return _binding!; } }
    internal NativePlaybackRateOrigin Origin { get { VerifyCurrent(); return _origin!; } }
    internal bool RootsReleased => _rootsReleased;
    internal string? LastFailure => _failure;
    internal bool ProductionAdmission => false;

    internal void VerifyCurrent()
    {
        Open(); using var read = EnterRead();
        if (!_claimed || _binding is null || _origin is null)
            throw new GameplayStateException(ErrorCode.NotReady, "The actual native fresh-source epoch has not been claimed.");
        try
        {
            // The running contract checks this claimed ticket, current native
            // channels, backing lifetime and both real seven-root graphs.
            // Complete installation audits stay in capture/claim/adoption and
            // explicit diagnostics. This scope never outlives this one read.
            using (var verification = _group.BeginSourceOperation(this, _stock, _ticket!))
            {
                _run.VerifyCurrent(_binding); VerifyGraph(verification);
                var native = verification.VerifyFreshTicket(this, _stock, _ticket!);
                VerifyNativeGraph(native.Source);
                if (native.SourceEpoch != checked((ulong)_binding.SourceGeneration))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The retained native source epoch differs from this binding.");
                _run.VerifyCurrent(_binding); VerifyGraph(verification);
            }
            _run.VerifyCurrent(_binding); Open();
        }
        catch (Exception error) { Invalidate("The observed fresh source identity failed: " + error.Message); throw; }
    }
    internal void VerifyInstallationCurrent()
    {
        Open(); using var read = EnterRead();
        if (!_claimed || _binding is null || _origin is null)
            throw new GameplayStateException(ErrorCode.NotReady, "The actual native fresh-source epoch has not been claimed.");
        try
        {
            using (var verification = _group.BeginSourceVerification())
            {
                _run.VerifyCurrent(_binding); VerifyGraph(verification);
                var native = verification.VerifyFreshTicket(this, _stock, _ticket!);
                VerifyNativeGraph(native.Source);
                if (native.SourceEpoch != checked((ulong)_binding.SourceGeneration))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The retained native source epoch differs from this binding.");
                _run.VerifyCurrent(_binding); VerifyGraph(verification);
            }
            _run.VerifyCurrent(_binding); Open();
        }
        catch (Exception error) { Invalidate("The fresh source installation identity failed: " + error.Message); throw; }
    }
    private void VerifyNativeGraph(NativePlaybackSingleReturnBridge.FreshSourceSnapshot source)
    {
        if (source.Player != unchecked((ulong)Target(4)) || source.AudioSource != unchecked((ulong)Target(5))
            || source.Progress != unchecked((ulong)Target(2)) || source.Clip != unchecked((ulong)Target(6)))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The native joint ticket does not identify the seven retained roots.");
    }
    private void RetainGraph()
    {
        Retain(new(_run.Controller), _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"));
        Retain(new(_run.Information), _api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation"));
        if (_api.Read<IntPtr>(Pointer(0), "LevelInformation") != Pointer(1))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The observed controller information edge changed before capture.");
        Retain(_api.Read<IntPtr>(Pointer(1), "progressControl"), _api.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl"));
        Retain(_api.Read<IntPtr>(Pointer(0), "pauseUIControl"), _api.Class("Assembly-CSharp.dll", "", "PauseUIControl"));
        Retain(_api.Read<IntPtr>(Pointer(2), "<MusicPlayer>k__BackingField"), _api.Class("Assembly-CSharp.dll", "Pigeon.MUGToolkit.Core", "MUGMusicPlayer"));
        Retain(_api.Read<IntPtr>(Pointer(2), "audioSource"), _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource"));
        Retain(_api.Call(Pointer(5), "get_clip"), _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip"));
        foreach (var field in new[] {
            (0,"LevelInformation","InLevel.LevelInformation"),(0,"pauseUIControl","PauseUIControl"),
            (1,"progressControl","InLevel.ProgressControl"),(2,"levelInformation","InLevel.LevelInformation"),
            (2,"<MusicPlayer>k__BackingField","Pigeon.MUGToolkit.Core.MUGMusicPlayer"),
            (2,"audioSource","UnityEngine.AudioSource"),(4,"audioSource","UnityEngine.AudioSource") })
            if (_api.DescribeFieldType(_classes[field.Item1],field.Item2)!=field.Item3)
                throw new UnsupportedSignatureException("The fresh graph field declaration differs: "+field.Item2);
        _api.RequireInstanceFieldLayout(_classes[4], "audioSource", "UnityEngine.AudioSource", 0x10);
    }
    private void Retain(IntPtr value, IntPtr cls)
    {
        var root = _api.RetainExactObject(value, cls);
        _roots.Add(root); _classes.Add(cls);
    }
    private void VerifyGraph()
    {
        Open(); using var read = EnterRead();
        using (var verification = _group.BeginSourceVerification()) VerifyGraph(verification);
        _run.VerifyHostCurrent(); Open();
    }
    private void VerifyGraph(ExactHookBroker.PlaybackSourceInstallGroup.SourceVerificationScope verification)
    {
        verification.RequireStockProtocol(_stock);
        VerifyGraphBody();
        verification.VerifyReceivers(_api, Pointer(4), Pointer(5), Pointer(2), _stock.GetTime.MethodInfo);
        VerifyGraphActive();
    }
    private void VerifyGraph(ExactHookBroker.PlaybackSourceInstallGroup.SourceOperationScope verification)
    {
        verification.RequireStockProtocol(_stock);
        VerifyGraphBody();
        verification.VerifyReceivers(_api, Pointer(4), Pointer(5), Pointer(2), _stock.GetTime.MethodInfo);
        VerifyGraphActive();
    }
    private void VerifyGraphBody()
    {
        Open(); _run.VerifyHostCurrent();
        if (_roots.Count != 7 || _roots.Distinct(ReferenceEqualityComparer.Instance).Count() != 7)
            throw new GameplayStateException(ErrorCode.TargetExpired, "Fresh capture must retain seven independent native root handles.");
        for (var i=0;i<7;++i)
            if (_api.ObjectClass(Pointer(i))!=_classes[i]) throw new GameplayStateException(ErrorCode.TargetExpired,"A retained fresh graph class changed.");
        if (Target(0)!=_run.Controller || Target(1)!=_run.Information
            || _api.Read<IntPtr>(Pointer(0),"LevelInformation")!=Pointer(1)
            || _api.Read<IntPtr>(Pointer(0),"pauseUIControl")!=Pointer(3)
            || _api.Read<IntPtr>(Pointer(1),"progressControl")!=Pointer(2)
            || _api.Read<IntPtr>(Pointer(2),"levelInformation")!=Pointer(1)
            || _api.Read<IntPtr>(Pointer(2),"<MusicPlayer>k__BackingField")!=Pointer(4)
            || _api.Read<IntPtr>(Pointer(2),"audioSource")!=Pointer(5)
            || _api.Read<IntPtr>(Pointer(4),"audioSource")!=Pointer(5)
            || _api.Call(Pointer(5),"get_clip")!=Pointer(6))
            throw new GameplayStateException(ErrorCode.TargetExpired,"The independently retained fresh graph changed.");
    }
    private void VerifyGraphActive()
    {
        if (!_api.Unbox<bool>(_api.Call(Pointer(0),"get_isActiveAndEnabled"))
            || !_api.Unbox<bool>(_api.Call(Pointer(5),"get_isActiveAndEnabled"))
            || !_api.Unbox<bool>(_api.CallStatic(_api.Class("UnityEngine.CoreModule.dll","UnityEngine","Object"),"op_Implicit",NativeRuntime.Arg.Ref(Pointer(6)))))
            throw new GameplayStateException(ErrorCode.TargetExpired,"A fresh graph receiver is no longer active and alive.");
        _run.VerifyHostCurrent(); Open(); // A final getter may have synchronously cancelled this owner.
    }
    private IntPtr Pointer(int index) => _roots[index].Target;
    private long Target(int index) => Pointer(index).ToInt64();
    private void Registered()
    {
        if (!ReferenceEquals(_registered,this)) throw new GameplayStateException(ErrorCode.TargetExpired,"Copied fresh capture owns no roots or native ticket.");
        if (_thread!=Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread,"Fresh capture belongs to its actual owner thread.");
    }
    private void Open()
    {
        Registered();
        if (_closed) throw new GameplayStateException(ErrorCode.TargetExpired,_failure??"Fresh capture is closed.");
        _reads.VerifyOpen();
    }
    private void Invalidate(string reason)
    {
        Registered(); _closed=true; _failure??=reason; _reads.Close();
        try { _adoption?.StopNewUse(reason); } catch { /* Retained failed stop never permits root release. */ }
        _withdrawal?.Dispose(); _withdrawal=null;
        if (!TryDrainClosedRoots()) _group.RetainUnresolvedPlayerRootOwner(this,TryDrainClosedRoots);
    }
    private bool TryDrainClosedRoots()
    {
        if (_drainingRoots) return false;
        _drainingRoots = true;
        try { return TryReleaseRoots(); }
        catch (Exception error) { _failure ??= "Fresh capture cleanup could not prove retirement: " + error.Message; return false; }
        finally { _drainingRoots = false; }
    }
    private bool TryReleaseRoots()
    {
        Registered();
        if (_rootsReleased) return true;
        if (!_closed || _reads.Active != 0 || _adoption is { } adoption && !adoption.PrepareDrain()) return false;
        _rootReleaseFailure?.Throw();
        if (_stageStarted)
        {
            // Revocation does not prove drainage. Native callbacks may still
            // borrow these exact handles; unknown activity retains them forever.
            if (_ticket is not null && !_ticket.TryRelease()) return false;
            if (!_group.TryVerifyIdleForRootRelease() || !_group.TryRetirePlayerWatch(this)) return false;
        }
        Exception? first=null;
        foreach(var root in _roots.AsEnumerable().Reverse().ToArray())
        {
            try {root.Dispose();_roots.Remove(root);} catch(Exception error) {first??=error;}
        }
        if(first is not null) {_rootReleaseFailure=ExceptionDispatchInfo.Capture(first);_rootReleaseFailure.Throw();}
        _rootsReleased=true;return true;
    }
    public void Dispose() => Invalidate("The sole fresh capture was cancelled; it cannot be replaced by another initial candidate.");
}
