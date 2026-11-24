using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] AliveGuardCases =
    {
        ("Hold alive guard reads actual one byte false and true without consuming the original root", AliveGuardValues),
        ("Hold alive guard checks fresh exact declared signatures without a warm method cache", AliveGuardSignatures),
        ("Hold alive guard stops at each observed method metadata export", AliveGuardMetadata),
        ("Hold alive guard stops actual backend lookup after callback disposal", AliveGuardBackendMetadata),
        ("Hold alive guard stops boxed result consumption after actual invoke disposal", AliveGuardBackendInvoke),
        ("Hold alive guard retains an unknown temporary argument root without retry", AliveGuardUnknownCleanup)
    };

    private static IntPtr AliveGuardObject(FakeNative n)
        => n.GameplayApi.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");

    private static void AliveGuardValues()
    {
        using var f = new Fixture(); var n = f.Native; var api = n.GameplayApi; var cls = AliveGuardObject(n);
        using var root = api.Root(n.FlagNotes[2]); var roots = n.ActiveRoots;
        n.DeadFlagNote = n.FlagNotes[2];
        Check(!api.ReadHoldCaptureAlive(root, cls, () => { }), "A one-byte native false became true.");
        n.DeadFlagNote = IntPtr.Zero;
        Check(api.ReadHoldCaptureAlive(root, cls, () => { }) && root.Target == n.FlagNotes[2]
            && n.ActiveRoots == roots && f.Snapshot().Records == 0 && !f.Snapshot().FullCoverageKnown,
            "The guarded liveness read lost its actual value or caller root, or manufactured Hold coverage.");
    }

    private static void AliveGuardSignatures()
    {
        foreach (var fault in Enum.GetValues<FakeNative.HoldAliveSignatureFault>())
        {
            using var f = new Fixture(); var n = f.Native; var cls = AliveGuardObject(n);
            // A successful warm lookup must not authorize changed metadata.
            n.GameplayApi.InspectHoldCaptureAliveMethod(cls, () => { }); n.HoldAliveFaultSignature(fault);
            n.HoldSourceGuardExportLog.Clear();
            Expect<UnsupportedSignatureException>(() => n.GameplayApi.InspectHoldCaptureAliveMethod(cls, () => { }));
            Check(!n.HoldSourceGuardExportLog.Contains("il2cpp_runtime_invoke"), "A changed liveness signature reached invocation.");
        }
        using var foreign = new Fixture(); var other = foreign.Native.GameplayApi.Class("mscorlib.dll", "System", "Object");
        Expect<UnsupportedSignatureException>(() => foreign.Native.GameplayApi.InspectHoldCaptureAliveMethod(other, () => { }));
    }

    private static void AliveGuardMetadata()
    {
        string[] exports;
        using (var f = new Fixture())
        {
            var n = f.Native; var cls = AliveGuardObject(n); n.HoldSourceGuardExportLog.Clear();
            n.GameplayApi.InspectHoldCaptureAliveMethod(cls, () => { });
            exports = n.HoldSourceGuardExportLog.Distinct().ToArray();
            Check(exports.Contains("il2cpp_method_get_class") && exports.Contains("il2cpp_method_get_param")
                && exports.Contains("il2cpp_method_get_return_type") && exports.Contains("il2cpp_method_is_inflated"),
                "The exact liveness inspector skipped its actual method signature exports.");
        }
        foreach (var selected in exports)
        {
            using var f = new Fixture(); var n = f.Native; var cls = AliveGuardObject(n);
            var withdrawn = false; var boundary = -1; var failure = new InvalidOperationException("Expected Hold alive metadata withdrawal.");
            n.HoldSourceGuardExportLog.Clear();
            n.AfterHoldSourceGuardExport = name =>
            {
                if (name != selected) return;
                n.AfterHoldSourceGuardExport = null; withdrawn = true; boundary = n.HoldSourceGuardExportLog.Count;
            };
            var error = Expect<InvalidOperationException>(() => n.GameplayApi.InspectHoldCaptureAliveMethod(cls,
                () => { if (withdrawn) throw failure; }));
            n.AfterHoldSourceGuardExport = null;
            Check(withdrawn && ReferenceEquals(error, failure) && boundary >= 0
                && n.HoldSourceGuardExportLog.Skip(boundary).All(name => name == "il2cpp_free"),
                "Liveness metadata continued after withdrawal at " + selected);
        }
    }

    private static void AliveGuardBackendMetadata() => AliveGuardBackendWithdrawal(false);
    private static void AliveGuardBackendInvoke() => AliveGuardBackendWithdrawal(true);

    private static void AliveGuardBackendWithdrawal(bool duringInvoke)
    {
        using var f = new Fixture(); var n = f.Native; var armed = false; var closed = false; var boundary = -1;
        var invokesBefore = n.NoteViewInvocations;
        n.HoldSourceGuardExportLog.Clear();
        n.AfterHoldSourceGuardExport = name =>
        {
            // Discovery now inspects its method too. Arm only after its actual
            // native invocation, so this still targets the liveness operator.
            if (name == "il2cpp_method_is_inflated" && n.NoteViewInvocations > invokesBefore) armed = true;
            if (!armed || duringInvoke && name != "il2cpp_runtime_invoke") return;
            n.AfterHoldSourceGuardExport = null; closed = true; f.Backend.Dispose(); boundary = n.HoldSourceGuardExportLog.Count;
        };
        var result = f.Start.Invoke(f.StartFrame()); n.AfterHoldSourceGuardExport = null;
        Check(armed && closed && boundary >= 0 && result == unchecked((nuint)n.HoldHandle.ToInt64())
            && f.Start.Originals == 1 && f.Snapshot().Records == 0 && !f.Snapshot().FullCoverageKnown
            && n.HoldSourceGuardExportLog.Skip(boundary).All(name => name is "il2cpp_gchandle_free" or "il2cpp_free")
            && !n.HoldSourceGuardExportLog.Skip(boundary).Contains("il2cpp_object_unbox"),
            "Actual Hold liveness withdrawal consumed later metadata/results or suppressed the original Start.");
        if (duringInvoke)
            Check(n.HoldSourceGuardExportLog.Take(boundary).Contains("il2cpp_runtime_invoke"), "The native liveness call was never entered.");
    }

    private static void AliveGuardUnknownCleanup()
    {
        using var f = new Fixture(); var n = f.Native; var cls = AliveGuardObject(n);
        using var root = n.GameplayApi.Root(n.FlagNotes[2]); var original = n.NoteViewHandlesFor(n.FlagNotes[2]).ToArray();
        uint failed = 0;
        n.AfterHoldSourceGuardExport = name =>
        {
            if (name != "il2cpp_gchandle_new") return;
            n.AfterHoldSourceGuardExport = null;
            failed = n.NoteViewHandlesFor(n.FlagNotes[2]).Except(original).Single(); n.FailFreeHandle = failed;
        };
        var error = Expect<AggregateException>(() => n.GameplayApi.ReadHoldCaptureAlive(root, cls, () => { }));
        n.AfterHoldSourceGuardExport = null; f.RetainStorage = true; UnknownOwners.Add(f);
        Check(failed != 0 && error.Data["NativePublicationCleanupOwner"] is IDisposable
            && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
            "An unknown liveness argument-root free lost its exact responsibility.");
        var attempts = n.FreeHandleAttempts;
        Expect<InvalidOperationException>(() => ((IDisposable)error.Data["NativePublicationCleanupOwner"]!).Dispose());
        Check(n.FreeHandleAttempts == attempts && root.Target == n.FlagNotes[2],
            "Unknown liveness cleanup retried a free or consumed the original caller root.");
    }
}
