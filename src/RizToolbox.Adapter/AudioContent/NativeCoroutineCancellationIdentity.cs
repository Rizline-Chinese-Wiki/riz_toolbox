namespace RizToolbox.Adapter.AudioContent;

internal enum NativeCoroutineCancellationReferenceKind { Enumerator, CoroutineHandle }

/// <summary>Value-only observation of a currently retained coroutine record.
/// It grants no Stop call, cancellation-completed, root release or run-ended
/// authority. ObservedHandle survives failed adoption; only HandleConfirmed can
/// be used to match a handle. Initialization uses its once-only owner/role with
/// Generation=0; reusable tracker slots retain their actual generation.</summary>
internal readonly record struct NativeCoroutineCancellationIdentity(object Owner, long Receiver, int Slot, long Generation,
    long Enumerator, long ObservedHandle, bool HandleConfirmed, bool StartReturned, bool Published, bool Retiring,
    bool NaturalTerminal, bool Known)
{
    internal bool MatchesReference(NativeCoroutineCancellationReferenceKind kind, long receiver, long argument)
        => Known && Published && !Retiring && Enumerator != 0 && receiver != 0 && Receiver == receiver && argument != 0
        && (kind switch {
            NativeCoroutineCancellationReferenceKind.Enumerator => Enumerator == argument,
            NativeCoroutineCancellationReferenceKind.CoroutineHandle => HandleConfirmed && StartReturned && ObservedHandle == argument,
            _ => false
        });

    // A snapshot taken during synchronous first MoveNext may precede the later
    // Start return/handle. Its exact enum generation stays current when that
    // handle is added; an already observed handle must never change identity.
    internal bool SameRecord(NativeCoroutineCancellationIdentity previous)
        => Known && Published && !Retiring && previous.Known && previous.Published && !previous.Retiring
        && ReferenceEquals(Owner, previous.Owner) && Receiver == previous.Receiver && Slot == previous.Slot
        && Generation == previous.Generation && Enumerator != 0 && Enumerator == previous.Enumerator
        && (previous.ObservedHandle == 0 || ObservedHandle == previous.ObservedHandle)
        && (!previous.HandleConfirmed || HandleConfirmed);
}
