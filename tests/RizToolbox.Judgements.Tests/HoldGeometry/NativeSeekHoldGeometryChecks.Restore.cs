using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldGeometryChecks
{
    private static void RestoreOriginalTransforms()
    {
        using var f = new Fixture(); var p = f.Prepare();
        try
        {
            p.VerifyPrepared(); p.Execute(); Check(p.VerifyAppliedValues(), "Applied Hold geometry was not independently observed.");
            p.RestoreSelectedValues();
            Check(p.VerifyRestoredValues() && p.RestorationJournal.Count > 0
                && p.RestorationJournal.All(row => row.NativeEntered && row.NativeReturned && row.OriginalValueObserved),
                "The actual inverse did not restore every independently captured selected transform property.");
            Check(f.TransformBaseline.All(row => f.Native.GeometryTransforms[row.Key] == row.Value),
                "The Hold inverse changed an original Transform value or parent.");
            Check(p.GetJournal().ExternalEffectsUnclosed && p.HasRetainedResponsibility,
                "Visible inverse readback must not erase engine callback responsibility.");
        }
        finally { if (p.AttemptedCommands != 0) f.Retain(p); }
    }

    private static void RestoreRejectsForeignTransform()
    {
        using var f = new Fixture(); var p = f.Prepare();
        try
        {
            p.Execute();
            var reference = new IntPtr(p.GetJournal().Baseline!.Notes[0].NoteTransform);
            var previous = f.Native.GeometryTransforms[reference];
            f.Native.GeometrySetTransform(reference, previous with { Position = new NativeVector3(881, 991, 771) });
            var calls = f.Native.GeometryCalls.Count;
            ExpectFailure(p.RestoreSelectedValues);
            Check(f.Native.GeometryCalls.Count == calls && p.RestorationJournal.Count == 0,
                "An unowned transform change was overwritten by the inverse.");
        }
        finally { if (p.AttemptedCommands != 0) f.Retain(p); }
    }
}
