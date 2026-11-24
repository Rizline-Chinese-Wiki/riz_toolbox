namespace RizToolbox.Judgements.Tests.CanvasBasis;

internal static partial class NativeSeekCanvasBasisChecks
{
    private static void RestoredOwnershipCleanup()
    {
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); basis.Execute(); basis.RestoreSelectedValues();
            Check(basis.VerifyRestoredValues(), "The actual original Canvas basis did not read back.");
            basis.AcknowledgeVerifiedRestoredOwnership(); basis.Dispose();
            var positions = basis.OriginalPositionOwner.GetJournal();
            Check(!basis.HasRetainedResponsibility && !positions.HasRetainedResponsibility
                && positions.ExternalEffectsUnclosed && positions.SideEffectsUnknown
                && positions.AttemptedRestores > 0 && positions.Commands.All(row => row.NativeReturned && row.TargetObserved),
                "Confirmed Canvas inverse retained duplicate roots or erased historical Unity effects.");
            // Fixture disposal independently requires every actual handle to reach zero.
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); basis.Execute(); basis.RestoreSelectedValues();
            Check(basis.VerifyRestoredValues(), "The Canvas failure fixture did not first restore its actual baseline.");
            var original = f.Native.CanvasPositionValue(0);
            f.Native.CanvasPositionSet(0, original with { X = original.X + 100 });
            var roots = f.Native.CanvasBasisCurrentRoots();
            Reject(basis.AcknowledgeVerifiedRestoredOwnership);
            var error = Reject(basis.Dispose);
            OriginalRootsRemain(f.Native, roots, "A changed restored Canvas position released its original roots.");
            f.Preserve(basis, error);
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); basis.Execute(); basis.RestoreSelectedValues();
            Check(basis.VerifyRestoredValues(), "The Canvas readback fixture did not restore its original baseline.");
            var roots = f.Native.CanvasBasisCurrentRoots(); var fired = false;
            f.Native.BeforeCanvasCacheScalar = _ => throw new InvalidOperationException("Restored ownership must not dispatch a cache store.");
            f.Native.AfterCanvasSourceRead = (_, _) =>
            {
                if (fired) return; fired = true;
                throw new IOException("Synthetic original restored-source readback failed before ownership acknowledgement.");
            };
            Reject(basis.AcknowledgeVerifiedRestoredOwnership); f.Clear();
            Check(fired && !basis.GetJournal().RestoredValuesVerified,
                "A failed fresh inverse readback retained its earlier restored ownership proof.");
            var failureCount = basis.GetJournal().Failures.Count;
            NoNative(f.Native, () => Reject(basis.AcknowledgeVerifiedRestoredOwnership));
            Check(basis.GetJournal().Failures.Count > failureCount,
                "A repeated failed inverse acknowledgement erased the original restored-source failure.");
            OriginalRootsRemain(f.Native, roots, "A failed inverse readback released original Canvas source roots.");
            var error = Reject(basis.Dispose); f.Preserve(basis, error);
        }
    }

    private static void TargetOwnershipCleanup()
    {
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); basis.Execute();
            Check(basis.VerifyAppliedValues(), "The real Canvas target did not read back.");
            var before = basis.OriginalPositionOwner.GetJournal();
            Check(before.ExternalEffectsUnclosed && before.Commands.All(row => row.NativeReturned && row.TargetObserved),
                "The cleanup test did not enter and return actual typed setter calls.");
            basis.AcknowledgeVerifiedTargetOwnership(); basis.Dispose();
            var after = basis.GetJournal(); var positions = basis.OriginalPositionOwner.GetJournal();
            Check(!after.HasRetainedResponsibility && !positions.HasRetainedResponsibility
                && positions.ExternalEffectsUnclosed && positions.SideEffectsUnknown,
                "Known duplicate-root release erased engine history or retained the original successful owner.");
            // Fixture disposal independently checks every native root reaches zero.
        }
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); basis.Execute(); basis.AcknowledgeVerifiedTargetOwnership();
            basis.RevokeTargetOwnershipAcknowledgement();
            var roots = f.Native.CanvasBasisCurrentRoots();
            var error = Reject(basis.Dispose);
            OriginalRootsRemain(f.Native, roots, "Revoked target ownership released original recovery roots.");
            f.Preserve(basis, error);
        }
    }
}
