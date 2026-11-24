using System.Runtime.CompilerServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

/// <summary>Real registry admission plus a controlled injection backend. These cases do not
/// assert native clone timing, actual game setter execution, settings initialization or playable LOCAL acceptance.</summary>
internal static class InactiveControllerParameterBindingRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Native inactive parameter binding rejects unsupported build before caller root targets or native work", NativeBuildAdmission),
        ("Inactive parameter binding requires exact adopted bundle current claim and already bound controller before export", ClaimAdmission),
        ("Inactive parameter injection retains independent controller and parameter roots and leaves settings unverified", SuccessfulBinding),
        ("Inactive parameter injection preserves observed scalar bits including negative zero and NaN payload", ScalarBits),
        ("Inactive parameter binding rejects changed native metadata before parameter export or mutation", MetadataFailure),
        ("Inactive controller root allocation attempt remains observable even when allocation returns no root", ControllerRetentionFailure),
        ("Inactive parameter binding rejects active parent hierarchy changed parent existing params and LevelInformation", InitialControllerState),
        ("Inactive parameter export failure keeps controller root and consumed attempt without retry", ExportFailure),
        ("Inactive parameter readback verifies each of the five exact reference identities", ReferenceReadback),
        ("Inactive parameter readback rejects changed scalars without assuming speed or offset defaults", ScalarReadback),
        ("Inactive parameter post-set validation requires inactive controller null info and exact params", PostSetterControllerState),
        ("Inactive parameter injection cannot become verified if its pending claim changes during the setter", ClaimChangedDuringSetter),
        ("Inactive parameter binding disposal requires exact session retirement and separately confirmed controller destruction", RetirementProof),
        ("Inactive parameter binding worker and duplicate reentry rejection preserve roots and one setter attempt", ThreadAndReentry),
        ("Process responsibility retains an abandoned binding until its exact verified retirement", ProcessResponsibility),
        // Last: these intentionally retain unknown process responsibility; no test-only reset exists.
        ("Inactive parameter binding independently frees both roots once and never retries an unknown free", UnknownRootFree),
        ("Inactive parameter setter exception retains mutation and argument responsibility without retry or retirement free", UnknownSetterCall)
    };

    private static void NativeBuildAdmission()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        var bundle = new OwnedNativeLocalPlayBundle(native.Api, main);
        var roots = new InactiveControllerRootRecords(main); var targetReads = 0; var observations = 0; var destroyRequested = false;
        roots.Allocate(() => new NativeRuntime.RootHandle(1, _ => { }, _ => { targetReads++; return new IntPtr(9_990_001); }, main.VerifyAccess));
        var controller = new OwnedNativeInactiveController(main, roots,
            () => { observations++; return new(9_990_001, 9_990_002, 9_990_003, false, true, false, true, 0, 0); },
            () => destroyRequested = true, () => destroyRequested,
            () => throw new InvalidOperationException("Unsupported build must not retain a caller controller."));
        controller.MarkReady(); var registry = new LocalRunSessionRegistry(main);
        var session = registry.RegisterPrepared(new(new("binding.test", "native-admission"), 1, "chart"), 9_990_000, bundle);
        var claim = registry.BeginCommit(session); registry.BindPendingController(claim, 9_990_001, controller);
        var profile = new AdapterBuildProfile("test", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
        Throws<UnsupportedSignatureException>(() => NativeInactiveControllerParameterBinding.Bind(native.Api, main, profile,
            IntPtr.Zero, registry, claim, bundle, controller));
        Task.Run(() => Throws<InvalidOperationException>(() => NativeInactiveControllerParameterBinding.Bind(native.Api, main,
            profile, IntPtr.Zero, registry, claim, bundle, controller))).GetAwaiter().GetResult();
        Equal(0, targetReads); Equal(0, observations); Equal(0, native.NewRoots); Equal(0, native.Invocations); Equal(0, native.FieldReads);
        controller.RequestDestroy(); Check(controller.TryCompleteDestroy());
        registry.BeginReturn(session); var proof = registry.VerifyConsumersEnded(session, new Verifier())!;
        Check(registry.Close(session, proof)); Check(bundle.TryCompleteDestroy());
    }
    private static void ClaimAdmission()
    {
        using (var f = new Fixture(bindController: false))
        {
            Throws<InvalidOperationException>(() => f.Bind()); Equal(0, f.Backend.NativeSteps); Equal(0, f.Backend.Exports);
        }
        using (var f = new Fixture())
        {
            Throws<InvalidOperationException>(() => NativeInactiveControllerParameterBinding.BindBackend(f.Main, f.Backend, f.Registry,
                new LocalRunSessionRegistry.CommitClaim(f.Session)));
            Equal(0, f.Backend.NativeSteps); Equal(0, f.Backend.Exports);
            f.Backend.AlternateBundle = new Lease();
            Throws<InvalidOperationException>(() => f.Bind()); Equal(0, f.Backend.NativeSteps);
            f.Backend.AlternateBundle = null; f.Registry.Commit(f.Claim);
            Throws<InvalidOperationException>(() => f.Bind()); Equal(0, f.Backend.NativeSteps);
        }
        using (var f = new Fixture())
        {
            f.Backend.SupportedBuild = false;
            Throws<UnsupportedSignatureException>(() => f.Bind()); Equal(0, f.Backend.NativeSteps); Equal(0, f.Backend.TargetReads);
            f.Backend.SupportedBuild = true; f.Bind(); Equal(1, f.Backend.Exports);
        }
    }
    private static void SuccessfulBinding()
    {
        using var f = new Fixture(); var owner = f.Bind(); var observed = owner.Read();
        Equal(f.Backend.ControllerIdentity, observed.Controller); Equal(f.Backend.Inputs.ParametersIdentity, observed.Parameters);
        Equal(2, owner.RootRecords); Equal(1, f.Backend.Exports); Equal(1, f.Backend.SetterCalls);
        Equal(0, f.Backend.Frees.Count); Check(!owner.CleanupUnknown && !owner.MutationOutcomeUnknown);
        Check(owner.RetentionAttempted && owner.MutationAttempted && owner.ParametersExportAttempted);
        Check(!observed.SettingsInitializationVerified && !observed.CanActivate);
        Check(!observed.InactiveController.ParentActiveSelf && !observed.InactiveController.ControllerActiveInHierarchy);
        Equal(0L, observed.InactiveController.LevelInformation);
        Equal(LocalRunSessionPhase.PendingCommit, f.Registry.Read(f.Session).Phase);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, observed.Controller));
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal(0, f.Backend.Frees.Count);
        f.Retire(); owner.Dispose(); owner.Dispose(); Check(owner.IsClosed);
        Equal("parameters,controller", string.Join(",", f.Backend.Frees));
        Equal(0, owner.RootRecords); Equal(1, f.Backend.SetterCalls);
    }
    private static void ScalarBits()
    {
        using var f = new Fixture();
        var speed = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var offset = BitConverter.Int32BitsToSingle(unchecked((int)0x7FC12345));
        f.Backend.Inputs = f.Backend.Inputs with { Parameters = f.Backend.Inputs.Parameters with { Speed = speed, Offset = offset } };
        f.Backend.ObservedParameters = f.Backend.Inputs.Parameters;
        var observed = f.Bind().Read();
        Equal(unchecked((int)0x80000000), observed.ReferencesAndScalars.SpeedBits);
        Equal(unchecked((int)0x7FC12345), observed.ReferencesAndScalars.OffsetBits);
        Check(!observed.SettingsInitializationVerified); Equal(1, f.Backend.SetterCalls);
    }
    private static void MetadataFailure()
    {
        using var f = new Fixture(); f.Backend.FailMetadata = true;
        var failure = f.FailedBind(); Equal(0, f.Backend.Exports); Equal(0, f.Backend.SetterCalls);
        Equal(0, failure.Responsibility.RootRecords); Check(!failure.Responsibility.RetentionAttempted && !failure.Responsibility.MutationAttempted);
        Check(ReferenceEquals(f.Owner, failure.Responsibility));
        Throws<InvalidOperationException>(() => f.Bind()); Equal(1, f.Backend.MetadataChecks);
    }
    private static void InitialControllerState()
    {
        foreach (var change in new Action<Backend>[] {
            b => b.ParentActive = true, b => b.ActiveInHierarchy = true, b => b.ParentMatches = false,
            b => b.CurrentParameters = b.Inputs.ParametersIdentity, b => b.LevelInformation = 99,
            b => b.ReportedController = b.ControllerIdentity + 100
        })
        {
            using var f = new Fixture(); change(f.Backend); var owner = f.FailedBind().Responsibility;
            Equal(0, f.Backend.Exports); Equal(0, f.Backend.SetterCalls); Equal(0, owner.RootRecords);
            Check(!owner.MutationAttempted); Equal(0, f.Backend.Frees.Count);
        }
    }
    private static void ControllerRetentionFailure()
    {
        using var f = new Fixture(); f.Backend.FailRetainController = true;
        var owner = f.FailedBind().Responsibility;
        Check(owner.RetentionAttempted && !owner.ParametersExportAttempted && !owner.MutationAttempted);
        Equal(0, owner.RootRecords); Equal(0, f.Backend.Exports); Equal(0, f.Backend.SetterCalls);
        Throws<InvalidOperationException>(() => f.Bind());
        f.Retire(); owner.Dispose(); Check(owner.IsClosed); Equal(0, f.Backend.Frees.Count);
    }
    private static void ExportFailure()
    {
        using var f = new Fixture(); f.Backend.FailExport = true;
        var owner = f.FailedBind().Responsibility;
        Equal(1, f.Backend.Exports); Equal(0, f.Backend.SetterCalls); Equal(1, owner.RootRecords);
        Check(owner.ParametersExportAttempted && !owner.MutationAttempted);
        Throws<InvalidOperationException>(() => f.Bind()); Equal(1, f.Backend.Exports); Equal(0, f.Backend.Frees.Count);
        f.Retire(); owner.Dispose(); Equal("controller", string.Join(",", f.Backend.Frees));
    }
    private static void ReferenceReadback()
    {
        for (var index = 0; index < 5; index++)
        {
            using var f = new Fixture(); var saved = index;
            f.Backend.AfterSetter = () => {
                var p = f.Backend.ObservedParameters;
                f.Backend.ObservedParameters = saved switch {
                    0 => p with { Metadata = p.Metadata + 1 }, 1 => p with { CoverReference = p.CoverReference + 1 },
                    2 => p with { MusicReference = p.MusicReference + 1 }, 3 => p with { Chart = p.Chart + 1 },
                    _ => p with { JudgeData = p.JudgeData + 1 }
                };
            };
            var owner = f.FailedBind().Responsibility; Check(owner.MutationOutcomeUnknown);
            Equal(1, f.Backend.SetterCalls); Equal(2, owner.RootRecords); Equal(0, f.Backend.Frees.Count);
            Throws<InvalidOperationException>(() => owner.Read()); Throws<InvalidOperationException>(() => f.Bind());
            Equal(1, f.Backend.SetterCalls);
        }
    }
    private static void ScalarReadback()
    {
        foreach (var speed in new[] { true, false })
        {
            using var f = new Fixture();
            f.Backend.AfterSetter = () => f.Backend.ObservedParameters = speed
                ? f.Backend.ObservedParameters with { Speed = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) }
                : f.Backend.ObservedParameters with { Offset = .125f };
            var owner = f.FailedBind().Responsibility; Check(owner.MutationOutcomeUnknown);
            Equal(1, f.Backend.SetterCalls); Equal(0, f.Backend.Frees.Count);
        }
    }
    private static void PostSetterControllerState()
    {
        foreach (var change in new Action<Backend>[] {
            b => b.ParentActive = true, b => b.ActiveInHierarchy = true, b => b.ParentMatches = false,
            b => b.LevelInformation = 9, b => b.CurrentParameters++
        })
        {
            using var f = new Fixture(); f.Backend.AfterSetter = () => change(f.Backend);
            var owner = f.FailedBind().Responsibility; Check(owner.MutationOutcomeUnknown);
            Equal(1, f.Backend.SetterCalls); Equal(2, owner.RootRecords); Equal(0, f.Backend.Frees.Count);
        }
    }
    private static void ClaimChangedDuringSetter()
    {
        using var f = new Fixture(); f.Backend.AfterSetter = () => f.Registry.Commit(f.Claim);
        var owner = f.FailedBind().Responsibility; Check(owner.MutationOutcomeUnknown);
        Equal(LocalRunSessionPhase.Committed, f.Registry.Read(f.Session).Phase);
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, f.Backend.ControllerIdentity));
        Equal(0, f.Backend.Frees.Count); Equal(1, f.Backend.SetterCalls);
    }
    private static void RetirementProof()
    {
        using var f = new Fixture(); var owner = f.Bind();
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal(0, f.Backend.Frees.Count);
        f.Registry.BeginReturn(f.Session);
        Check(f.Registry.VerifyConsumersEnded(f.Session, new Verifier(false)) is null);
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal(2, owner.RootRecords);
        // Even a supplied registry token cannot replace the separate clone owner's actual close.
        var proof = f.Registry.VerifyConsumersEnded(f.Session, new Verifier())!; Check(f.Registry.Close(f.Session, proof));
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal(0, f.Backend.Frees.Count);
        f.Backend.ControllerClosed = true; owner.Dispose(); Check(owner.IsClosed); Equal(2, f.Backend.Frees.Count);
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture(); var buildCalls = f.Backend.BuildChecks;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Bind())).GetAwaiter().GetResult();
        Equal(buildCalls, f.Backend.BuildChecks); Equal(0, f.Backend.NativeSteps);
        var rejections = 0;
        f.Backend.AfterSetter = () => { Throws<InvalidOperationException>(() => f.Bind()); rejections++; };
        var owner = f.Bind(); Equal(1, rejections); Equal(1, f.Backend.SetterCalls); var reads = f.Backend.NativeSteps;
        Task.Run(() => {
            Throws<InvalidOperationException>(() => owner.Read()); Throws<InvalidOperationException>(() => owner.Dispose());
        }).GetAwaiter().GetResult();
        Equal(reads, f.Backend.NativeSteps); Equal(0, f.Backend.Frees.Count); owner.Read();
        Equal(1, f.Backend.SetterCalls); Equal(2, owner.RootRecords);
    }
    private static void ProcessResponsibility()
    {
        using var f = new Fixture(); var weak = Abandon(f);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.TryGetTarget(out var owner)); Equal(2, owner!.RootRecords); Equal(0, f.Backend.Frees.Count);
        f.Owner = owner; f.Retire(); owner.Dispose(); Check(owner.IsClosed); Equal(2, f.Backend.Frees.Count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NativeInactiveControllerParameterBinding> Abandon(Fixture f)
    { var owner = f.Bind(); f.Owner = null; return new(owner); }
    private static void UnknownRootFree()
    {
        using var f = new Fixture(); var owner = f.Bind(); f.Backend.FailFree = "parameters"; f.Retire();
        Throws<AggregateException>(() => owner.Dispose()); Check(owner.CleanupUnknown && !owner.IsClosed);
        Equal("parameters,controller", string.Join(",", f.Backend.Frees)); Equal(1, owner.RootRecords);
        f.Backend.FailFree = null;
        Throws<InvalidOperationException>(() => owner.Dispose()); Throws<InvalidOperationException>(() => owner.Read());
        Equal("parameters,controller", string.Join(",", f.Backend.Frees)); Equal(1, owner.RootRecords);
        f.PreserveUnknown = true;
    }
    private static void UnknownSetterCall()
    {
        using var f = new Fixture(); f.Backend.ThrowAfterSetterMutation = true;
        var failure = f.FailedBind(); var owner = failure.Responsibility;
        Check(failure.InnerException is IOException); Check(owner.MutationOutcomeUnknown && owner.CleanupUnknown);
        Equal(2, owner.RootRecords); Equal(1, owner.RetainedCallArgumentRecords); Equal(0, f.Backend.Frees.Count);
        Throws<InvalidOperationException>(() => f.Bind()); Equal(1, f.Backend.SetterCalls); Equal(1, f.Backend.Exports);
        f.Retire(); Throws<InvalidOperationException>(() => owner.Dispose());
        Equal(2, owner.RootRecords); Equal(0, f.Backend.Frees.Count); f.PreserveUnknown = true;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunSessionRegistry.CommitClaim Claim;
        internal readonly Backend Backend;
        internal NativeInactiveControllerParameterBinding? Owner;
        internal bool PreserveUnknown;
        internal Fixture(bool bindController = true)
        {
            Registry = new(Main); Backend = new(Main);
            Session = Registry.RegisterPrepared(new(new("binding.test", "song"), 1, "chart"), Backend.MetadataIdentity, Backend.Bundle);
            Claim = Registry.BeginCommit(Session);
            if (bindController) Registry.BindPendingController(Claim, Backend.ControllerIdentity, Backend.Controller);
        }
        internal NativeInactiveControllerParameterBinding Bind()
            => Owner = NativeInactiveControllerParameterBinding.BindBackend(Main, Backend, Registry, Claim);
        internal NativeInactiveControllerParameterBindingException FailedBind()
        {
            try { Bind(); }
            catch (NativeInactiveControllerParameterBindingException failure) { Owner = failure.Responsibility; return failure; }
            throw new Exception("Expected explicit parameter-binding failure responsibility.");
        }
        internal void Retire()
        {
            Backend.ControllerClosed = true;
            var snapshot = Registry.Read(Session);
            if (snapshot.ProtectionRetired) return;
            Registry.BeginReturn(Session); var proof = Registry.VerifyConsumersEnded(Session, new Verifier())!;
            Check(Registry.Close(Session, proof));
        }
        public void Dispose()
        {
            try { Retire(); if (!PreserveUnknown && Owner is { IsClosed: false }) Owner.Dispose(); }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Lease : IDisposable
    {
        internal int Frees;
        public void Dispose() { if (Frees == 0) Frees++; }
    }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    {
        private readonly bool _result;
        internal Verifier(bool result = true) => _result = result;
        public bool Verify(LocalRunRetirementRequest request, out string evidence)
        { evidence = "Controlled fixture verifies all consumers and globals ended for this exact registry run; no native acceptance is claimed."; return _result; }
    }
    private sealed class Backend : IInactiveControllerParameterBindingBackend
    {
        private static long NextIdentity = 8_000_000;
        private readonly MainThreadDispatcher _main;
        private bool _gatePassed, _exportConsumed;
        internal readonly Lease Bundle = new(), Controller = new();
        internal Lease? AlternateBundle;
        internal readonly long MetadataIdentity, ControllerIdentity;
        internal long ReportedController, CurrentParameters, LevelInformation;
        internal bool SupportedBuild = true, ParentActive, ActiveInHierarchy, ParentMatches = true;
        internal bool FailMetadata, FailExport, FailRetainController, ThrowAfterSetterMutation;
        internal int BuildChecks, NativeSteps, MetadataChecks, Exports, SetterCalls, TargetReads;
        internal readonly List<string> Frees = new();
        internal string? FailFree;
        internal Action? AfterSetter;
        internal InactiveControllerParameterInputs Inputs;
        internal LocalNativeParametersObservation ObservedParameters;
        public bool ControllerClosed { get; set; }
        public bool ControllerCleanupUnknown { get; set; }
        public bool CallCleanupUnknown { get; private set; }
        public int RetainedCallArgumentRecords { get; private set; }
        public IDisposable BundleResponsibility => AlternateBundle ?? Bundle;
        public IDisposable ControllerResponsibility => Controller;
        internal Backend(MainThreadDispatcher main)
        {
            _main = main; MetadataIdentity = Interlocked.Add(ref NextIdentity, 16); ControllerIdentity = MetadataIdentity + 1;
            ReportedController = ControllerIdentity;
            ObservedParameters = new(MetadataIdentity, MetadataIdentity + 3, MetadataIdentity + 4, MetadataIdentity + 5, MetadataIdentity + 6, 0, 0);
            Inputs = new(MetadataIdentity + 2, ObservedParameters);
        }
        public void RequireSupportedBuild()
        {
            _main.VerifyAccess(); BuildChecks++;
            if (!SupportedBuild) throw new UnsupportedSignatureException("Controlled unsupported build gate.");
            _gatePassed = true;
        }
        private void NativeStep()
        { _main.VerifyAccess(); if (!_gatePassed) throw new Exception("Backend accessed before build gate."); NativeSteps++; }
        public InactiveControllerParameterInputs ReadBundle() { NativeStep(); return Inputs; }
        public NativeInactiveControllerObservation ReadController()
        { NativeStep(); return new(ReportedController, ControllerIdentity + 7, ControllerIdentity + 8, ParentActive, true,
            ActiveInHierarchy, ParentMatches, CurrentParameters, LevelInformation); }
        public void ValidateNativeMetadata(IntPtr controller, IntPtr parameters, LocalNativeParametersObservation expected)
        {
            NativeStep(); MetadataChecks++;
            if (FailMetadata) throw new UnsupportedSignatureException("Controlled changed class/field/MethodInfo metadata.");
            Equal(ControllerIdentity, controller.ToInt64()); Equal(Inputs.ParametersIdentity, parameters.ToInt64());
        }
        public NativeRuntime.RootHandle RetainController() { NativeStep(); if (FailRetainController) throw new IOException("Unknown controller root allocation."); return Root(1, ControllerIdentity, "controller"); }
        public NativeRuntime.RootHandle RetainParameters(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
        {
            NativeStep(); registry.VerifyPendingBundleClaim(claim, MetadataIdentity, Bundle);
            Equal(ControllerIdentity, registry.Read(claim.Session).ControllerIdentity);
            if (_exportConsumed) throw new InvalidOperationException("Bundle export is one-shot.");
            _exportConsumed = true; Exports++;
            if (FailExport) throw new IOException("Controlled parameter-root allocation failed after recording the session.");
            return Root(2, Inputs.ParametersIdentity, "parameters");
        }
        private NativeRuntime.RootHandle Root(uint id, long pointer, string name)
            => new(id, _ => { Frees.Add(name); if (FailFree == name) throw new IOException("Unknown controlled root free."); },
                _ => { NativeStep(); TargetReads++; return new IntPtr(pointer); }, _main.VerifyAccess);
        public LocalNativeParametersObservation ReadParameters(IntPtr parameters)
        { NativeStep(); Equal(Inputs.ParametersIdentity, parameters.ToInt64()); return ObservedParameters; }
        public void SetParameters(IntPtr controller, IntPtr parameters)
        {
            NativeStep(); SetterCalls++; Equal(1, SetterCalls);
            Equal(ControllerIdentity, controller.ToInt64()); Equal(Inputs.ParametersIdentity, parameters.ToInt64());
            CurrentParameters = parameters.ToInt64(); AfterSetter?.Invoke();
            if (ThrowAfterSetterMutation)
            {
                CallCleanupUnknown = true; RetainedCallArgumentRecords = 1;
                throw new IOException("Controlled native setter/temporary cleanup failed after possible mutation.");
            }
        }
    }
}
