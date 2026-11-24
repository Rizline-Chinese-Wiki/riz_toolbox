using System.Reflection;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldGeometryChecks
{
    private static void AcknowledgedOwnership()
    {
        using var f = new Fixture(); var roots = f.Native.ActiveRoots; var p = f.Prepare();
        p.Execute(); p.AcknowledgeVerifiedTransformOwnership(); p.Dispose();
        Check(f.Native.ActiveRoots == roots && !p.HasRetainedResponsibility && p.GetJournal().ExternalEffectsUnclosed,
            "Known returned target values must release only independent roots while retaining historical external-effect evidence.");
    }

    private static void RevokedOwnership()
    {
        using var f = new Fixture(); var p = f.Prepare();
        try
        {
            p.Execute(); p.AcknowledgeVerifiedTransformOwnership();
            var copy = (NativeSeekHoldGeometryCommands)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(p, null)!;
            var before = Calls.Read(f.Native);
            ExpectFailure(copy.AcknowledgeVerifiedTransformOwnership);
            ExpectFailure(copy.RevokeTransformOwnershipAcknowledgement);
            Check(Calls.Read(f.Native) == before, "A copied acknowledgement reached native observation.");
            p.RevokeTransformOwnershipAcknowledgement();
        }
        finally { if (p.AttemptedCommands != 0) f.Retain(p); }
    }

    private static void ComposedHoldState()
    {
        using var f = new Fixture { ExpectHoldStateWrites = true }; var p = f.Prepare();
        try
        {
            p.Execute();
            using var state = f.Run.PrepareSeekHoldState(f.Native.HoldStateProfile, p.GetJournal().HoldStateTargets!);
            state.Commit(); Check(state.Confirm().Observed == true && p.VerifyAppliedTransforms(),
                "The actual scalar/list owner invalidated the distinct applied Hold Transform owner.");
            state.Restore(); Check(state.ConfirmRestored().OriginalStateRestored, "The actual Hold state inverse failed.");
            p.RestoreSelectedValues(); Check(p.VerifyRestoredValues(), "Hold geometry did not restore after its scalar owner.");
            p.AcknowledgeRestoredTransformOwnership(); p.Dispose();
        }
        catch
        {
            if (p.AttemptedCommands != 0 && !p.GetJournal().Disposed) f.Retain(p);
            throw;
        }
    }
}
