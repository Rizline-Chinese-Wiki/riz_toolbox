using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;

internal static class NativeLocalInitializationSettingsRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Settings receipt accepts exact stock speed and millisecond conversion after a distinct scalar setter baseline", ExactSettings),
        ("Settings receipt rejects each changed pre-getter reference and bit pattern against the completed setter", SetterBaselineChanged),
        ("Settings receipt rejects a different setter controller or parameter identity before stock invocation", SetterIdentityChanged),
        ("Settings receipt rejects settings source value divisor or MethodInfo changes across original invocation", SettingsChanged),
        ("Settings receipt verifies each exact returned reference and both actual scalar bit patterns", ReturnedParametersChanged),
        ("Settings receipt remains unknown after one invalid attempt and rejects repeat or another run", OnceAndIdentity),
        ("Settings source requires finite positive mapped speed finite milliseconds and exact native divisor", InvalidSettings)
    };
    private static LocalRunSessionIdentity Identity(long generation = 1)
        => new("settings-fixture", new("settings.fixture", "song"), 4, "chart", generation);
    private static LocalNativeParametersObservation Baseline()
        => new(101, 102, 103, 104, 105, BitConverter.Int32BitsToSingle(unchecked((int)0x7fc12345)), -0f);
    private static NativeInactiveControllerParameterBindingObservation Setter(LocalNativeParametersObservation? value = null)
        => new(200, 300, value ?? Baseline(), new(200, 201, 202, false, true, false, true, 300, 0));
    private static NativeLocalInitializationSettings Settings()
        => new(10.25f, -125f, 1000f, 0x390A80, 0x390B90);
    private static InactiveControllerParameterInputs Input(LocalNativeParametersObservation? value = null)
        => new(300, value ?? Baseline());
    private static InactiveControllerParameterInputs Initialized()
    { var settings = Settings(); return Input(Baseline() with { Speed = settings.MappedSpeed, Offset = settings.OffsetMilliseconds / settings.OffsetDivisor }); }
    private static NativeLocalInitializationSettingsReceipt Prepared()
    { var receipt = new NativeLocalInitializationSettingsReceipt(); receipt.BeginAttempt(Identity(), 200); receipt.RecordBefore(Setter(), Input(), Settings()); return receipt; }
    private static void ExactSettings()
    {
        var receipt = Prepared(); Check(!receipt.IsVerified && !receipt.IsUnknown);
        Equal(Baseline().SpeedBits, receipt.Baseline!.SpeedBits); Equal(unchecked((int)0x80000000), receipt.Baseline.OffsetBits);
        receipt.VerifyReturned(Identity(), 200, 300, Initialized(), Settings()); Check(receipt.IsVerified && !receipt.IsUnknown);
    }
    private static IEnumerable<LocalNativeParametersObservation> Changed(LocalNativeParametersObservation p)
    {
        yield return p with { Metadata = p.Metadata + 1 }; yield return p with { CoverReference = p.CoverReference + 1 };
        yield return p with { MusicReference = p.MusicReference + 1 }; yield return p with { Chart = p.Chart + 1 };
        yield return p with { JudgeData = p.JudgeData + 1 };
        yield return p with { Speed = BitConverter.Int32BitsToSingle(p.SpeedBits ^ 1) };
        yield return p with { Offset = BitConverter.Int32BitsToSingle(p.OffsetBits ^ 1) };
    }
    private static void SetterBaselineChanged()
    {
        foreach (var changed in Changed(Baseline()))
        {
            var receipt = new NativeLocalInitializationSettingsReceipt(); receipt.BeginAttempt(Identity(), 200);
            Throws<InvalidOperationException>(() => receipt.RecordBefore(Setter(), Input(changed), Settings()));
            Check(receipt.IsUnknown && !receipt.IsVerified);
        }
    }
    private static void SetterIdentityChanged()
    {
        foreach (var setter in new[] { Setter() with { Controller = 201 }, Setter() with { Parameters = 301 } })
        {
            var receipt = new NativeLocalInitializationSettingsReceipt(); receipt.BeginAttempt(Identity(), 200);
            Throws<InvalidOperationException>(() => receipt.RecordBefore(setter, Input(), Settings())); Check(receipt.IsUnknown);
        }
    }
    private static void SettingsChanged()
    {
        var p = Settings();
        foreach (var settings in new[] { p with { MappedSpeed = 10.5f }, p with { OffsetMilliseconds = 125f },
            p with { OffsetDivisor = 100f }, p with { MappedSpeedMethod = p.MappedSpeedMethod + 1 },
            p with { OffsetMillisecondsMethod = p.OffsetMillisecondsMethod + 1 } })
        {
            var receipt = Prepared();
            Throws<InvalidOperationException>(() => receipt.VerifyReturned(Identity(), 200, 300, Initialized(), settings));
            Check(receipt.IsUnknown && !receipt.IsVerified);
        }
    }
    private static void ReturnedParametersChanged()
    {
        foreach (var changed in Changed(Initialized().Parameters))
        {
            var receipt = Prepared();
            Throws<InvalidOperationException>(() => receipt.VerifyReturned(Identity(), 200, 300, Input(changed), Settings()));
            Check(receipt.IsUnknown && !receipt.IsVerified);
        }
        foreach (var offset in new[] { -125f, -.124f })
        {
            var receipt = Prepared();
            Throws<InvalidOperationException>(() => receipt.VerifyReturned(Identity(), 200, 300,
                Initialized() with { Parameters = Initialized().Parameters with { Offset = offset } }, Settings()));
        }
    }
    private static void OnceAndIdentity()
    {
        var duplicate = Prepared(); Throws<InvalidOperationException>(() => duplicate.RecordBefore(Setter(), Input(), Settings()));
        Check(duplicate.IsUnknown); Throws<InvalidOperationException>(() => duplicate.VerifyReturned(Identity(), 200, 300, Initialized(), Settings()));
        var completed = Prepared(); completed.VerifyReturned(Identity(), 200, 300, Initialized(), Settings());
        Throws<InvalidOperationException>(() => completed.VerifyReturned(Identity(), 200, 300, Initialized(), Settings())); Check(!completed.IsVerified);
        foreach (var action in new Action<NativeLocalInitializationSettingsReceipt>[] {
            r => r.VerifyReturned(Identity(2), 200, 300, Initialized(), Settings()),
            r => r.VerifyReturned(Identity(), 201, 300, Initialized(), Settings()),
            r => r.VerifyReturned(Identity(), 200, 301, Initialized(), Settings()),
            r => r.VerifyReturned(Identity(), 200, 300, Initialized() with { ParametersIdentity = 301 }, Settings()) })
        { var receipt = Prepared(); Throws<InvalidOperationException>(() => action(receipt)); Check(receipt.IsUnknown); }
    }
    private static void InvalidSettings()
    {
        var p = Settings();
        foreach (var settings in new[] { p with { MappedSpeed = 0 }, p with { MappedSpeed = -1 },
            p with { MappedSpeed = float.NaN }, p with { MappedSpeed = float.PositiveInfinity },
            p with { OffsetMilliseconds = float.NegativeInfinity }, p with { OffsetMilliseconds = float.NaN },
            p with { OffsetDivisor = -1000f }, p with { MappedSpeedMethod = 0 }, p with { OffsetMillisecondsMethod = 0 },
            p with { OffsetMillisecondsMethod = p.MappedSpeedMethod } })
        {
            var receipt = new NativeLocalInitializationSettingsReceipt(); receipt.BeginAttempt(Identity(), 200);
            Throws<InvalidOperationException>(() => receipt.RecordBefore(Setter(), Input(), settings)); Check(receipt.IsUnknown);
        }
        var z = Settings() with { OffsetMilliseconds = -0f };
        // Negative zero is a valid stock value; its sign must survive division.
        var receiptZero = new NativeLocalInitializationSettingsReceipt(); receiptZero.BeginAttempt(Identity(), 200);
        receiptZero.RecordBefore(Setter(), Input(), z);
        receiptZero.VerifyReturned(Identity(), 200, 300, Input(Baseline() with { Speed = z.MappedSpeed, Offset = -0f }), z);
        Check(receiptZero.IsVerified);
    }
}

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] InitializationSettingsCases = {
        ("Actual initialization requires the exact adopted setter receipt before retaining Start or entering stock getter", MissingInitializationSetter),
        ("Initialization compares entry references and scalar bits to the preactivation setter receipt", InitializationSetterChanged),
        ("Initialization rejects changed actual settings and wrong computed speed offset after original", InitializationSettingsChanged),
        ("Adopted setter identity queries stay root free after activation while inactive reinspection remains unavailable", InitializationSetterIdentity)
    };
    private static void MissingInitializationSetter()
    {
        using var f = new PendingFixture(initialization: true); f.AttachController(bindParameters: false); f.Launch.NoteActivationAttempt();
        f.Backend.Start(f.Controller + 9); Equal(0, f.Backend.GetterOriginals); Equal(1, f.Backend.Roots);
        Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
    }
    private static void InitializationSetterChanged()
    {
        foreach (var mutate in new Func<LocalNativeParametersObservation, LocalNativeParametersObservation>[] {
            p => p with { Chart = p.Chart + 1 }, p => p with { Speed = 0 }, p => p with { Offset = 0f } })
        {
            using var f = new PendingFixture(initialization: true); f.AttachController();
            f.Backend.InitializationEntryOverride = mutate(f.Backend.InitializationBaseline);
            f.Launch.NoteActivationAttempt(); f.Backend.Start(f.Controller + 9); Equal(0, f.Backend.GetterOriginals);
            Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase); Check(!f.Launch.IsCommittedReady);
        }
    }
    private static void InitializationSettingsChanged()
    {
        foreach (var mutation in new Action<Backend>[] {
            b => b.InitializationAfterSettings = b.InitializationSettings with { MappedSpeed = 11f },
            b => b.InitializationReturnedOverride = b.InitializationBaseline with { Speed = 10.25f, Offset = -125f },
            b => b.InitializationReturnedOverride = b.InitializationBaseline with { Speed = 0f, Offset = -.125f } })
        {
            using var f = new PendingFixture(initialization: true); f.AttachController(); mutation(f.Backend);
            f.Launch.NoteActivationAttempt(); f.Backend.Start(f.Controller + 9); Equal(1, f.Backend.GetterOriginals);
            Equal(LocalRunLaunchCommitPhase.Unknown, f.Launch.Read().Phase);
            Throws<InvalidOperationException>(() => f.Bridge.VerifyInitializedGraph(f.Launch));
        }
    }
    private static void InitializationSetterIdentity()
    {
        using var f = new PendingFixture(initialization: true); f.AttachController(); var binding = f.ParameterBinding!;
        Check(ReferenceEquals(binding, f.Launch.ParameterBinding)); Throws<InvalidOperationException>(() => f.Launch.AttachParameterBinding(binding));
        f.Launch.NoteActivationAttempt(); f.SetterBackend!.Active = true; var reads = f.SetterBackend.NativeReads;
        Check(binding.IsBoundFor(f.Host.Sessions, f.Claim, f.Bundle, f.ControllerOwner));
        Check(!binding.IsBoundFor(f.Host.Sessions, new LocalRunSessionRegistry.CommitClaim(f.Session), f.Bundle, f.ControllerOwner));
        Check(!binding.IsBoundFor(f.Host.Sessions, f.Claim, f.Bundle, new Lease()));
        Equal(f.Controller, binding.SetterBaseline.Controller); Equal(reads, f.SetterBackend.NativeReads);
        Task.Run(() => Throws<InvalidOperationException>(() => binding.IsBoundFor(f.Host.Sessions, f.Claim, f.Bundle, f.ControllerOwner))).GetAwaiter().GetResult();
        Equal(reads, f.SetterBackend.NativeReads);
        f.Backend.Start(f.Controller + 9); Check(f.Bridge.VerifyInitializedGraph(f.Launch));
        Equal(reads, f.SetterBackend.NativeReads); Check(binding.IsBoundFor(f.Host.Sessions, f.Claim, f.Bundle, f.ControllerOwner));
    }
    private sealed partial class Backend : INativeLocalInitializationSettingsBackend
    {
        internal NativeLocalInitializationSettings InitializationSettings = new(10.25f, -125f, 1000f, 0x390A80, 0x390B90);
        internal NativeLocalInitializationSettings? InitializationAfterSettings;
        internal LocalNativeParametersObservation? InitializationEntryOverride, InitializationReturnedOverride;
        internal LocalNativeParametersObservation InitializationBaseline => new(InitializationController + 3,
            InitializationController + 10, InitializationController + 11, InitializationController + 12,
            InitializationController + 13, BitConverter.Int32BitsToSingle(unchecked((int)0x7fc12345)), -0f);
        public InactiveControllerParameterInputs ReadInitializationParameterState(IDisposable bundle)
        {
            var value = PendingControllerInitialized ? InitializationReturnedOverride ?? InitializationBaseline with {
                Speed = InitializationSettings.MappedSpeed, Offset = InitializationSettings.OffsetMilliseconds / InitializationSettings.OffsetDivisor }
                : InitializationEntryOverride ?? InitializationBaseline;
            return new(InitializationController + 4, value);
        }
        public NativeLocalInitializationSettings ReadInitializationSettings()
            => PendingControllerInitialized ? InitializationAfterSettings ?? InitializationSettings : InitializationSettings;
    }
    private sealed class InitializationSetterBackend : IInactiveControllerParameterBindingBackend
    {
        private readonly MainThreadDispatcher _main; private readonly Backend _owner;
        public IDisposable BundleResponsibility { get; }
        public IDisposable ControllerResponsibility { get; }
        internal bool Closed, Active;
        internal int NativeReads;
        private long _parameters;
        internal InitializationSetterBackend(MainThreadDispatcher main, Backend owner, IDisposable bundle, IDisposable controller)
            => (_main, _owner, BundleResponsibility, ControllerResponsibility) = (main, owner, bundle, controller);
        public bool ControllerClosed => Closed;
        public bool ControllerCleanupUnknown => false;
        public bool CallCleanupUnknown => false;
        public int RetainedCallArgumentRecords => 0;
        public void RequireSupportedBuild() => _main.VerifyAccess();
        public InactiveControllerParameterInputs ReadBundle() { NativeReads++; return new(_owner.InitializationController + 4, _owner.InitializationBaseline); }
        public NativeInactiveControllerObservation ReadController()
        { NativeReads++; var c = _owner.InitializationController; return new(c, c + 20, c + 21, false, true, Active, !Active, _parameters, 0); }
        public void ValidateNativeMetadata(IntPtr controller, IntPtr parameters, LocalNativeParametersObservation expected)
        { NativeReads++; Equal(_owner.InitializationController, controller.ToInt64()); Equal(_owner.InitializationController + 4, parameters.ToInt64()); }
        private NativeRuntime.RootHandle Root(uint id, long value)
            => new(id, _ => { }, _ => { NativeReads++; return new(value); }, _main.VerifyAccess);
        public NativeRuntime.RootHandle RetainController() => Root(111, _owner.InitializationController);
        public NativeRuntime.RootHandle RetainParameters(LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim)
            => Root(112, _owner.InitializationController + 4);
        public LocalNativeParametersObservation ReadParameters(IntPtr parameters) { NativeReads++; return _owner.InitializationBaseline; }
        public void SetParameters(IntPtr controller, IntPtr parameters) { NativeReads++; _parameters = parameters.ToInt64(); }
    }
}
