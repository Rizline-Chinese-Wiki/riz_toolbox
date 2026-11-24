namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void WriterRestoredOwnershipCleanup()
    {
        using (var f = new WriterFixture(parentEqual: false))
        {
            var writer = f.Prepare(); writer.Execute(); writer.RestoreSelectedValues();
            Check(writer.VerifyRestoredValues(), "The actual endpoint parent and local baseline did not restore.");
            writer.AcknowledgeVerifiedRestoredOwnership(); writer.Dispose();
            var journal = writer.GetJournal();
            Check(!journal.HasRetainedResponsibility && journal.ExternalEffectsUnclosed && journal.SideEffectsUnknown
                && journal.AttemptedParentRestores > 0 && journal.AttemptedLocalRestores > 0
                && journal.Commands.All(row => row.NativeReturned && row.TargetObserved),
                "Confirmed parent/local inverse retained duplicate roots or erased its actual command history.");
            // Fixture disposal independently requires every actual handle to reach zero.
        }
        using (var f = new WriterFixture())
        {
            var writer = f.Prepare(); writer.Execute(); writer.RestoreSelectedValues();
            Check(writer.VerifyRestoredValues(), "The endpoint failure fixture did not first restore its baseline.");
            var pointer = new IntPtr(f.Targets[0].Transform);
            var original = f.Native.GeometryTransforms[pointer];
            f.Native.GeometrySetTransform(pointer, original with { Position = original.Position with { X = original.Position.X + 100 } });
            var roots = f.Native.CanvasBasisCurrentRoots();
            Reject(writer.AcknowledgeVerifiedRestoredOwnership); Reject(writer.Dispose);
            OriginalRootsRemain(f.Native, roots, "A changed original endpoint released its actual source roots.");
            f.Retain();
        }
    }

    private static void WriterTargetOwnershipCleanup()
    {
        using (var f = new WriterFixture())
        {
            var writer = f.Prepare(); writer.Execute();
            Check(writer.VerifyAppliedValues(), "Actual endpoint targets did not read back.");
            writer.AcknowledgeVerifiedTargetOwnership(); writer.Dispose();
            var journal = writer.GetJournal();
            Check(!journal.HasRetainedResponsibility && journal.ExternalEffectsUnclosed && journal.SideEffectsUnknown
                && journal.Commands.All(row => row.NativeReturned && row.TargetObserved),
                "Successful endpoint cleanup erased its history or retained duplicate roots.");
            // The real reader/shared-source fixture verifies zero remaining roots.
        }
        using (var f = new WriterFixture())
        {
            var writer = f.Prepare(); writer.Execute(); writer.AcknowledgeVerifiedTargetOwnership();
            writer.RevokeTargetOwnershipAcknowledgement();
            var roots = f.Native.CanvasBasisCurrentRoots(); Reject(writer.Dispose);
            OriginalRootsRemain(f.Native, roots, "Revoked line target ownership dropped actual inverse roots.");
            f.Retain();
        }
    }
}
