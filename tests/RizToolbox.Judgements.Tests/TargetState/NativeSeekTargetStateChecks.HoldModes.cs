using System.Reflection;
using System.Runtime.ExceptionServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekTargetStateChecks
{
    internal static readonly (string Name, Action Run)[] HoldModeCases =
    {
        ("Hold epoch reads exact backing fields for all observed mode pairs", HoldModePairs),
        ("Hold epoch rejects a changed observed mode without a setter or alias", HoldModeChanges),
        ("Hold epoch rejects incorrect backing-field Boolean storage metadata", HoldModeMetadata)
    };

    // Only the actual private mode-read method is exercised here. The real
    // NativeRuntime, backend RunValue and target reader own their roots. This
    // controlled constructor does not issue installed observer access, execute
    // epoch Prepare/Start/Stop, or authorize a complete Seek.
    private sealed class HoldModeFixture : IDisposable
    {
        private readonly MainThreadDispatcher _main = new();
        internal readonly Fixture Source = new();
        private readonly NativeJudgementBackend.SeekHoldEpoch _epoch;
        private readonly MethodInfo _verify;
        internal HoldModeFixture()
        {
            Source.Native.AddType("UnityEngine.Coroutine", 18, false, IntPtr.Size);
            Source.Backend.InitializeHoldLifecycle(_main);
            var target = Capture(Source, 0);
            var run = Source.Backend.CaptureRun() ?? throw new InvalidOperationException("No independent actual epoch RunValue.");
            _epoch = Source.Own(new NativeJudgementBackend.SeekHoldEpoch(
                Source.Backend, null!, run, target, Source.Native.TargetProfile));
            typeof(NativeJudgementBackend).GetField("_seekHoldEpoch", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Source.Backend, _epoch);
            var type = typeof(NativeJudgementBackend.SeekHoldEpoch);
            var judge = (NativeRuntime.RootHandle)type.GetMethod("Adopt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(_epoch, new object[] { Source.Native.JudgeOwner, Source.Native.TargetProfile.JudgeClass })!;
            type.GetField("_judge", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_epoch, judge);
            _verify = type.GetMethod("VerifyModes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        }
        internal void Baseline(bool autoPlay, bool freeHold)
        {
            Source.Native.TargetSet(Source.Native.JudgeOwner, "<AutoPlay>k__BackingField", autoPlay);
            Source.Native.TargetSet(Source.Native.JudgeOwner, "<FreeHold>k__BackingField", freeHold);
            var type = typeof(NativeJudgementBackend.SeekHoldEpoch);
            type.GetField("_autoPlay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_epoch, autoPlay);
            type.GetField("_freeHold", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_epoch, freeHold);
        }
        internal void Verify()
        {
            try { _verify.Invoke(_epoch, null); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        public void Dispose()
        {
            Baseline(false, false);
            try { Source.Dispose(); }
            finally { _main.Dispose(); }
        }
    }

    private static void HoldModePairs()
    {
        using var f = new HoldModeFixture(); var n = f.Source.Native;
        var roots = n.ActiveRoots; var fields = n.FieldWrites;
        foreach (var autoPlay in new[] { false, true })
            foreach (var freeHold in new[] { false, true })
            { f.Baseline(autoPlay, freeHold); f.Verify(); }
        Check(n.ActiveRoots == roots && n.FieldWrites == fields && n.GameplayCommandCalls == 0,
            "Exact epoch mode reads changed roots, wrote a field or dispatched a gameplay command.");
        foreach (var alias in new[] { "AutoPlay", "FreeHold" })
            Expect<UnsupportedSignatureException>(() => n.GameplayApi.InspectExactDeclaredReadField(
                n.TargetProfile.JudgeClass, alias, "System.Boolean", false));
    }

    private static void HoldModeChanges()
    {
        using var f = new HoldModeFixture(); var n = f.Source.Native;
        foreach (var name in new[] { "<AutoPlay>k__BackingField", "<FreeHold>k__BackingField" })
        {
            f.Baseline(false, false); n.TargetSet(n.JudgeOwner, name, true);
            var roots = n.ActiveRoots; var fields = n.FieldWrites;
            var error = Expect<InvalidOperationException>(f.Verify);
            Check(error.Message == "The actual Hold input mode changed during this epoch."
                && n.ActiveRoots == roots && n.FieldWrites == fields && n.TargetBoolean(n.JudgeOwner, name),
                "A changed actual Hold mode was adopted, overwritten or rejected for unrelated metadata.");
            f.Baseline(false, false); f.Verify();
        }
    }

    private static void HoldModeMetadata()
    {
        using var f = new HoldModeFixture(); var n = f.Source.Native;
        var metadata = (Dictionary<IntPtr, (string Name, IntPtr Type, int Offset)>)Field(n, "_fieldMetadata");
        foreach (var (name, offset) in new[] { ("<AutoPlay>k__BackingField", 0x79), ("<FreeHold>k__BackingField", 0x7a) })
        {
            var field = n.GameplayApi.InspectExactDeclaredReadField(n.TargetProfile.JudgeClass, name, "System.Boolean", false, offset);
            var original = metadata[field.Field]; var roots = n.ActiveRoots; var fields = n.FieldWrites;
            try
            {
                metadata[field.Field] = (original.Name, original.Type, original.Offset + 1);
                Expect<UnsupportedSignatureException>(f.Verify);
                metadata[field.Field] = (original.Name, n.Int32, original.Offset);
                Expect<UnsupportedSignatureException>(f.Verify);
            }
            finally { metadata[field.Field] = original; }
            f.Verify();
            Check(n.ActiveRoots == roots && n.FieldWrites == fields,
                "Invalid Boolean mode metadata created a native store or lost the original roots.");
        }
    }
}
