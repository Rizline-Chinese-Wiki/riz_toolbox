using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime;
using static Assert;

// The Runtime lane tests actual official field/metadata publication. These
// cases exercise the production Adapter shape filter and the real ledger's
// retained graphs through the existing controlled native bridge.
internal static class NativeUnityEventCachedBooleanTests
{
    internal static readonly (string Name, Action Test)[] Cases = new (string, Action)[] {
        ("Cached Boolean leaf accepts one exact official Boolean parameter without calling the method", ExactBoolean),
        ("Cached Boolean leaf keeps static virtual generic inflated and wrong signatures opaque", RejectedShapes),
        ("Ordinary noncached Boolean invokable does not gain the fixed argument path", NoncachedBoolean),
        ("Cached Boolean leaf requires the actual official parameter class rather than a matching name", ActualParameterClass)
    }.Concat(NativeLocalUnityEventLedgerRegression.CachedBooleanCases).ToArray();

    private static readonly IntPtr ActionClass = new(1503), BooleanClass = new(1501);
    private static NativeMetadataMethod Method(params string[] parameters)
        => new(new(705), new(new(704), "Fixture.Target"), "Apply", "System.Void", Array.AsReadOnly(parameters),
            0, 0, false, false, new(new(706), 706, 0), "controlled official metadata");
    private static bool Supported(NativeMetadataMethod method, Func<IntPtr>? readParameter, bool isVirtual = false,
        long target = 703, IntPtr? actualAction = null, IntPtr? cachedBoolean = null)
        => NativeLocalUnityEventBackend.SupportedListenerSignature(method, target, isVirtual, actualAction ?? ActionClass,
            ActionClass, cachedBoolean ?? BooleanClass, readParameter);
    private static void ExactBoolean()
    {
        var reads = 0;
        Check(Supported(Method("System.Boolean"), () => { ++reads; return BooleanClass; })); Equal(1, reads);
        Check(Supported(Method(), null, cachedBoolean: IntPtr.Zero));
    }
    private static void RejectedShapes()
    {
        var good = Method("System.Boolean"); var reads = 0;
        foreach (var method in new[] { good with { Flags = 0x10 }, good with { Flags = 0x40 }, good with { Flags = 0x400 },
            good with { GenericMethod = true }, good with { InflatedMethod = true }, good with { Entry = null },
            good with { ReturnType = "System.Boolean" }, Method(), Method("System.Int32"), Method("System.Boolean", "System.Boolean") })
            Check(!Supported(method, () => { ++reads; return BooleanClass; }));
        Check(!Supported(good, () => { ++reads; return BooleanClass; }, isVirtual: true));
        Check(!Supported(good, () => { ++reads; return BooleanClass; }, target: 0));
        Check(!Supported(good, () => { ++reads; return BooleanClass; }, actualAction: new(9999)));
        Equal(0, reads);
    }
    private static void NoncachedBoolean()
    {
        var reads = 0;
        Check(!Supported(Method("System.Boolean"), () => { ++reads; return BooleanClass; }, cachedBoolean: IntPtr.Zero));
        Equal(0, reads);
    }
    private static void ActualParameterClass()
    {
        Check(!Supported(Method("System.Boolean"), null));
        Check(!Supported(Method("System.Boolean"), () => new(9999)));
        Check(Supported(Method("System.Boolean"), () => BooleanClass));
    }
}

internal static partial class NativeLocalUnityEventLedgerRegression
{
    internal static readonly (string Name, Action Test)[] CachedBooleanCases = {
        ("Listener graph equality detects a changed cached Boolean with the same delegate and list version", CachedBooleanChanged),
        ("Inline listener comparison rejects a different cached Boolean at the same actual row identity", CachedBooleanInlineMismatch),
        ("Listener graph freeze retains the argument and target method when the later source changes", CachedBooleanFrozen),
        ("Listener graph rejects incomplete closed Boolean argument provenance", CachedBooleanInvalid),
        ("Parsed Boolean listeners preserve ambiguous persistent candidates and keep nonempty effects unaccounted", CachedBooleanStillPending)
    };
    private static NativeUnityEventInvokable BooleanRow(bool value, long identity = 501)
    {
        var argument = new NativeUnityEventCachedBooleanArgument(1501, value, 1502, 1503, 1504, 1505, 1506);
        var leaf = Leaf(1701) with { Class = argument.ActionClass, Opaque = false,
            Signature = "Fixture.Target.Apply(System.Boolean)->System.Void" };
        return new(identity, 502, false, new[] { leaf }, leaf.Delegate, 0, argument);
    }
    private static void SetBooleanRows(Fixture fixture, params NativeUnityEventInvokable[] rows)
    {
        var graph = fixture.Backend.Graphs[0];
        fixture.Backend.Graphs[0] = graph with { ExecutingCalls = graph.ExecutingCalls with { Rows = rows } };
    }
    private static void CachedBooleanChanged()
    {
        using var f = new Fixture(initialInvokable: BooleanRow(false));
        f.Backend.DuringInvoke = receiver => { ObserveEffectsInline(f, receiver); SetBooleanRows(f, BooleanRow(true)); };
        f.Backend.Call(101); var row = f.Owner.Read().EventSnapshots.Single();
        Check(!f.Owner.Read().Unknown && row.ChangedSinceCapture);
        Check(!row.Pre!.ExecutingCalls.Rows.Single().CachedBoolean!.Value);
        Check(row.Post!.ExecutingCalls.Rows.Single().CachedBoolean!.Value);
        Equal(row.Pre.ExecutingCalls.Version, row.Post.ExecutingCalls.Version);
        Equal(row.Pre.ExecutingCalls.Rows.Single().Delegate, row.Post.ExecutingCalls.Rows.Single().Delegate);
    }
    private static void CachedBooleanInlineMismatch()
    {
        using var f = new Fixture(initialInvokable: BooleanRow(false)); var graph = f.Backend.Graphs[0];
        f.Backend.InlineOverride = new(graph, graph.ExecutingCalls with { Rows = new[] { BooleanRow(true) } });
        f.Backend.DuringInvoke = receiver => Throws<InvalidOperationException>(() => ObserveEffectsInline(f, receiver));
        f.Backend.Call(101);
        Check(f.Owner.Read().Unknown && f.Owner.Read().Pending);
        Equal(1, f.Backend.InlineAdoptions); Check(f.Owner.Read().EventSnapshots.Single().Inline is null);
    }
    private static void CachedBooleanFrozen()
    {
        using var f = new Fixture(initialInvokable: BooleanRow(false)); var value = BooleanRow(false); var leaves = value.Leaves.ToList();
        SetBooleanRows(f, value with { Leaves = leaves });
        f.Backend.DuringInvoke = receiver => { ObserveEffectsInline(f, receiver); leaves.Clear(); SetBooleanRows(f, BooleanRow(true)); };
        f.Backend.Call(101); var row = f.Owner.Read().EventSnapshots.Single();
        var before = row.Inline!.Executing.Rows.Single();
        Check(!before.CachedBoolean!.Value && before.Leaves.Count == 1);
        Equal(703L, before.Leaves.Single().Target); Equal(705L, before.Leaves.Single().MethodInfo);
        Check(row.Post!.ExecutingCalls.Rows.Single().CachedBoolean!.Value && !f.Owner.Read().Unknown);
    }
    private static void CachedBooleanInvalid()
    {
        foreach (var mode in new[] { "argument-class", "parent", "action-class", "argument-field", "same-field", "invoke-method", "leaf-class" })
        {
            using var f = new Fixture(); var row = BooleanRow(false); var argument = row.CachedBoolean!;
            if (mode == "argument-class") argument = argument with { BooleanClass = 0 };
            if (mode == "parent") argument = argument with { InvokableParentClass = 0 };
            if (mode == "action-class") argument = argument with { ActionClass = 0 };
            if (mode == "argument-field") argument = argument with { ArgumentField = 0 };
            if (mode == "same-field") argument = argument with { ArgumentField = argument.DelegateField };
            if (mode == "invoke-method") argument = argument with { ActionInvokeMethod = 0 };
            if (mode == "leaf-class") row = row with { Leaves = new[] { row.Leaves.Single() with { Class = 9999 } } };
            SetBooleanRows(f, row with { CachedBoolean = argument }); f.Backend.Call(101);
            Check(f.Owner.Read().Unknown && f.Owner.Read().Pending);
        }
    }
    private static void CachedBooleanStillPending()
    {
        using var f = new Fixture(); SetBooleanRows(f, BooleanRow(false), BooleanRow(false, 601));
        var graph = f.Backend.Graphs[0]; var candidate = graph.Candidates.Single() with {
            Target = 703, Mode = 6, BoolArgument = false };
        f.Backend.Graphs[0] = graph with { Candidates = new[] { candidate, candidate with { Object = 1801 } } };
        f.Backend.DuringInvoke = receiver => ObserveEffectsInline(f, receiver); f.Backend.Call(101);
        var state = f.Owner.Read(); var post = state.EventSnapshots.Single().Post!;
        Equal(2, post.ExecutingCalls.Rows.Count); Equal(2, post.Candidates.Count);
        Check(post.ExecutingCalls.Rows.All(row => row.CachedBoolean is { Value: false } && !row.Opaque));
        var effects = f.Owner.ReadListenerEffects();
        Check(!state.Unknown && state.Pending && !state.MutationClosed && !effects.AllInvokedEffectsAccounted
            && effects.UnaccountedInvocations == 1 && !effects.FutureMembershipWritersClosed);
        Check(f.Owner.ReadListenerWriterSources().State != NativeLocalActiveConsumerProofState.Proven);
    }
}
