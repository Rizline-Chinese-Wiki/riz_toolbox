using RizToolbox.Adapter.Gameplay;
using RizToolbox.Judgements.Tests.NoteFlags;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    internal static readonly (string Name, Action Run)[] LineBufferCases =
    {
        ("Line ring source original scratch keeps one physical array, complete tail and raw inverse", LineBufferPrefixAndRestore),
        ("Line ring source original scratch refuses nonfinite forward prefix before storage", LineBufferNonfinite),
        ("Line ring source original scratch refuses field replacement after store admission", LineBufferAssociationCallback),
        ("Line ring source original scratch journals a completed store when reader postcheck fails", LineBufferPostcheckFailure),
        ("Line ring source original scratch retains unknown temporary root cleanup before store admission", LineBufferTemporaryCleanup),
        ("Line ring source original scratch retains entered store after its exact temporary pin free fails", LineBufferAfterStoreCleanup),
        ("Line ring source original scratch never inverses an external target written before its own store", LineBufferExternalTargetBeforeStore)
    };

    private static readonly NativeVector3BufferBits[] OriginalScratch =
    {
        new(0, unchecked((int)0x80000000), 0x3f800000),
        new(0x40000000, 0x40400000, 0x40800000),
        new(unchecked((int)0x7fc12345), 0x40a00000, 0x40c00000),
        new(0x40e00000, 0x41000000, unchecked((int)0x80000000)),
        new(0x41100000, 0x41200000, 0x41300000)
    };
    private static readonly NativeVector3BufferBits[] FirstPrefix =
    { new(0x3f000000, 0x3f400000, 0x3f800000), new(0x40000000, 0x40080000, 0x40100000) };
    private static readonly NativeVector3BufferBits[] SecondPrefix =
    { new(0x40400000, 0x40800000, 0x40a00000) };

    private static WriterFixture LineBufferFixture()
        => new(configure: n => n.ConfigureLineBuffer(OriginalScratch));

    private static void LineBufferPrefixAndRestore()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        var original = writer.PrepareOriginalLinePositionsBuffer();
        Check(original.Array == n.LineBufferArray && n.LineBufferAssociation == n.LineBufferArray
            && original.Items.SequenceEqual(OriginalScratch)
            && f.Source!.Snapshot.DynamicLines.Count == 2,
            "The real reader substituted a scratch array, missed its tail or collapsed duplicate dynamic occurrences.");
        var first = writer.WriteOriginalLinePositionsPrefix(FirstPrefix);
        Check(first.Before.Array == n.LineBufferArray && first.After.Array == n.LineBufferArray
            && first.After.Items.Take(FirstPrefix.Length).SequenceEqual(FirstPrefix)
            && first.After.Items.Skip(FirstPrefix.Length).SequenceEqual(OriginalScratch.Skip(FirstPrefix.Length))
            && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(first.After.Items)
            && original.Items.SequenceEqual(OriginalScratch),
            "The first original array prefix write altered the physical tail, root or historical baseline.");
        var second = writer.WriteOriginalLinePositionsPrefix(SecondPrefix);
        Check(second.After.Array == first.After.Array && second.After.Items[0] == SecondPrefix[0]
            && second.After.Items.Skip(1).SequenceEqual(first.After.Items.Skip(1))
            && writer.VerifyOriginalLinePositionsBuffer(),
            "The next occurrence replaced the shared array or changed an unwritten slot.");
        writer.RestoreOriginalLinePositionsBuffer();
        Check(writer.VerifyRestoredOriginalLinePositionsBuffer()
            && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(OriginalScratch),
            "Reverse entered prefix stores did not restore every original raw bit, including NaN and negative zero tail.");
        var journal = writer.GetOriginalLineBufferJournal();
        Check(journal.Prepared && journal.AttemptedWrites == 2 && journal.AttemptedRestores == 2
            && journal.Commands.Count == 4 && journal.Commands.All(c => c.StoreAdmissionEntered && c.StoreEntered
                && c.StoreReturned && c.TargetObserved)
            && journal.Commands.Skip(2).Select(c => c.PrefixLength).SequenceEqual(new[] { 1, 2 })
            && journal.RestoredValuesVerified && journal.HasRetainedResponsibility
            && ReferenceEquals(journal.OriginalSource, f.Source) && ReferenceEquals(journal.EffectsOwner, writer),
            "The original scratch journal lost actual entry order or owner after selected restoration.");
        var refusal = Reject(writer.Dispose);
        Check(ReferenceEquals(Data(refusal, "SeekLineBufferEffectsOwner"), writer)
            && ReferenceEquals(Data(refusal, "SeekLineBufferOriginalSource"), f.Source)
            && n.LineBufferAssociation == n.LineBufferArray,
            "Selected restoration released the original shared scratch effects or source owner.");
        f.Retain();
    }

    private static void LineBufferNonfinite()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        writer.PrepareOriginalLinePositionsBuffer();
        var bad = new NativeVector3BufferBits(unchecked((int)0x7fc00001), 0, 0);
        Check(Reject(() => writer.WriteOriginalLinePositionsPrefix(new[] { bad })) is ArgumentException
            && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(OriginalScratch),
            "A nonfinite forward prefix entered physical scratch storage.");
        var journal = writer.GetOriginalLineBufferJournal();
        Check(journal.Commands.Count == 0 && !journal.StoreAdmissionEntered && !journal.HasRetainedResponsibility,
            "A rejected forward prefix published an entered command.");
    }

    private static void LineBufferAssociationCallback()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        writer.PrepareOriginalLinePositionsBuffer();
        var replacement = n.LineBufferNewArray(OriginalScratch);
        var replaced = false;
        n.AfterCanvasSourceRead = (name, owner) =>
        {
            if (name != "linePositions" || owner != n.LineRingControl || replaced
                || Field(writer, "_lineBufferStoreAdmitted") is not true) return;
            replaced = true; n.LineBufferReplaceAssociation(replacement);
        };
        var error = Reject(() => writer.WriteOriginalLinePositionsPrefix(FirstPrefix));
        n.AfterCanvasSourceRead = null;
        var journal = writer.GetOriginalLineBufferJournal();
        Check(replaced && journal.StoreAdmissionEntered && journal.HasRetainedResponsibility
            && journal.Commands.Count == 1 && !journal.Commands[0].StoreEntered
            && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(OriginalScratch)
            && n.LineBufferAssociation == replacement
            && ReferenceEquals(Data(error, "SeekLineBufferEffectsOwner"), writer)
            && ReferenceEquals(Data(error, "SeekLineBufferOriginalSource"), f.Source),
            "A callback replaced the LineControl field after admission without a retained rejection before storage.");
        f.Retain();
    }

    private static void LineBufferPostcheckFailure()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        writer.PrepareOriginalLinePositionsBuffer();
        var freeAtAdmission = -1;
        n.AfterCanvasSourceRead = (name, owner) =>
        {
            if (freeAtAdmission < 0 && name == "linePositions" && owner == n.LineRingControl
                && Field(writer, "_lineBufferStoreAdmitted") is true)
                freeAtAdmission = n.FreeHandleAttempts;
        };
        n.LineRingSourceProfile.OnVerify = () =>
        {
            if (freeAtAdmission >= 0 && n.FreeHandleAttempts > freeAtAdmission
                && n.LineBufferReadBits(n.LineBufferArray)[0] == FirstPrefix[0])
                throw new InvalidOperationException("Synthetic reader post-Runtime source check withdrew.");
        };
        var error = Reject(() => writer.WriteOriginalLinePositionsPrefix(FirstPrefix));
        n.AfterCanvasSourceRead = null; n.LineRingSourceProfile.OnVerify = null;
        var journal = writer.GetOriginalLineBufferJournal();
        Check(freeAtAdmission >= 0 && journal.Commands.Count == 1 && journal.Commands[0].StoreEntered
            && journal.StoreAdmissionEntered && journal.HasRetainedResponsibility
            && error.Data["LineVector3BufferStoreEntered"] is true
            && n.LineBufferReadBits(n.LineBufferArray).Take(FirstPrefix.Length).SequenceEqual(FirstPrefix)
            && n.LineBufferReadBits(n.LineBufferArray).Skip(FirstPrefix.Length).SequenceEqual(OriginalScratch.Skip(FirstPrefix.Length))
            && ReferenceEquals(Data(error, "SeekLineBufferEffectsOwner"), writer),
            "The reader lost an already completed store when a later source check failed.");
        f.Retain();
    }

    private static void LineBufferTemporaryCleanup()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        writer.PrepareOriginalLinePositionsBuffer();
        n.FailFreeHandle = checked(n.LineBufferNewestHandle + 1);
        var error = Reject(() => writer.WriteOriginalLinePositionsPrefix(FirstPrefix));
        var journal = writer.GetOriginalLineBufferJournal();
        Check(!journal.StoreAdmissionEntered && journal.Commands.Count == 0
            && journal.HasRetainedResponsibility && n.LineBufferReadBits(n.LineBufferArray).SequenceEqual(OriginalScratch)
            && ReferenceEquals(Data(error, "SeekLineEndpointCleanupOwner"), writer)
            && Data(error, "SeekLineEndpointSourceCleanupOwner") is not null,
            "Unknown pre-store temporary root cleanup lost the original owner or entered storage.");
        f.Retain();
    }

    private static void LineBufferAfterStoreCleanup()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        writer.PrepareOriginalLinePositionsBuffer();
        var permanent = n.LineBufferActiveHandles(n.LineBufferArray);
        Check(permanent.Length == 1, "Preparation did not retain exactly one original scratch root.");
        uint failedTemporaryPin = 0;
        n.AfterCanvasSourceRead = (name, owner) =>
        {
            if (failedTemporaryPin != 0 || name != "linePositions" || owner != n.LineRingControl
                || Field(writer, "_lineBufferStoreAdmitted") is not true) return;
            // At this point Runtime has its temporary pinned root alive and
            // BeforeStore has published the command owner. Select that exact
            // active handle, never a later unrelated free or another array.
            failedTemporaryPin = n.LineBufferFailActiveTemporaryPin(n.LineBufferArray, permanent[0]);
        };
        var error = Reject(() => writer.WriteOriginalLinePositionsPrefix(FirstPrefix));
        n.AfterCanvasSourceRead = null;
        var journal = writer.GetOriginalLineBufferJournal();
        Check(failedTemporaryPin > permanent[0] && n.FailFreeHandle == failedTemporaryPin
            && journal.Commands.Count == 1 && journal.Commands[0].StoreAdmissionEntered
            && journal.Commands[0].StoreEntered && !journal.Commands[0].StoreReturned
            && journal.HasRetainedResponsibility && journal.SourceCleanupOwner is not null
            && error.Data["LineVector3BufferStoreEntered"] is true
            && error.Data["LineVector3BufferWriteObservation"] is NativeVector3BufferWriteObservation completed
            && completed.After.Array == n.LineBufferArray
            && n.LineBufferReadBits(n.LineBufferArray).Take(FirstPrefix.Length).SequenceEqual(FirstPrefix)
            && n.LineBufferReadBits(n.LineBufferArray).Skip(FirstPrefix.Length).SequenceEqual(OriginalScratch.Skip(FirstPrefix.Length))
            && ReferenceEquals(Data(error, "SeekLineBufferEffectsOwner"), writer)
            && ReferenceEquals(Data(error, "SeekLineBufferOriginalSource"), f.Source)
            && Data(error, "NativePublicationCleanupOwner") is not null,
            "A successful physical store plus exact temporary-pin cleanup failure lost either retained responsibility.");
        NoNative(n, () => Reject(() => writer.WriteOriginalLinePositionsPrefix(SecondPrefix)));
        NoNative(n, () => Reject(writer.Dispose));
        f.Retain();
    }

    private static void LineBufferExternalTargetBeforeStore()
    {
        using var f = LineBufferFixture(); var n = f.Native; var writer = f.Prepare();
        writer.PrepareOriginalLinePositionsBuffer();
        var changed = false;
        n.AfterCanvasSourceRead = (name, owner) =>
        {
            if (changed || name != "linePositions" || owner != n.LineRingControl
                || Field(writer, "_lineBufferStoreAdmitted") is not true) return;
            changed = true;
            // Simulate another alias consumer writing precisely our target
            // between BeforeStore and Runtime's final full expected read.
            n.LineBufferOverwritePrefix(n.LineBufferArray, FirstPrefix);
        };
        var error = Reject(() => writer.WriteOriginalLinePositionsPrefix(FirstPrefix));
        n.AfterCanvasSourceRead = null;
        var journal = writer.GetOriginalLineBufferJournal();
        Check(changed && error.Data["LineVector3BufferStoreEntered"] is false
            && journal.Commands.Count == 1 && journal.Commands[0].StoreAdmissionEntered
            && !journal.Commands[0].StoreEntered && journal.Commands[0].TargetObserved
            && !journal.Commands[0].BeforeObserved && journal.UnrecognizedState
            && journal.HasRetainedResponsibility && journal.AttemptedRestores == 0
            && n.LineBufferReadBits(n.LineBufferArray).Take(FirstPrefix.Length).SequenceEqual(FirstPrefix)
            && n.LineBufferReadBits(n.LineBufferArray).Skip(FirstPrefix.Length).SequenceEqual(OriginalScratch.Skip(FirstPrefix.Length)),
            "The externally written target was mistaken for our own entered store.");
        Reject(writer.RestoreOriginalLinePositionsBuffer);
        var afterRefusal = writer.GetOriginalLineBufferJournal();
        Check(afterRefusal.AttemptedRestores == 0 && afterRefusal.Commands.Count == 1
            && n.LineBufferReadBits(n.LineBufferArray).Take(FirstPrefix.Length).SequenceEqual(FirstPrefix),
            "The inverse touched externally written target storage without own store-entry proof.");
        f.Retain();
    }
}
