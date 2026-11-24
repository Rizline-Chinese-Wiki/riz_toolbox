using System.Runtime.ExceptionServices;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

internal enum SeekLineBufferDirection { Apply, Restore }

internal sealed record SeekLineBufferCommandEffect(int Sequence, SeekLineBufferDirection Direction,
    NativeRootedVector3Buffer Before, NativeRootedVector3Buffer Target, int PrefixLength,
    bool StoreAdmissionEntered, bool StoreEntered, bool StoreReturned,
    NativeRootedVector3Buffer? Readback, bool BeforeObserved, bool TargetObserved,
    string? Failure);

internal sealed record SeekLineBufferJournal(bool Prepared, bool StoreAdmissionEntered,
    bool RestorationEntered, bool RestorationFinished, bool RestoredValuesVerified,
    bool UnrecognizedState, bool SideEffectsUnknown, bool HasRetainedResponsibility,
    int AttemptedWrites, int AttemptedRestores, NativeRootedVector3Buffer? Baseline,
    NativeRootedVector3Buffer? Expected, IReadOnlyList<SeekLineBufferCommandEffect> Commands,
    NativeSeekLineRingSourceCapture? OriginalSource, object? EffectsOwner,
    object? SourceCleanupOwner)
{
    internal bool AtomicConditionalWrite => false;
    internal bool ExternalConsumersClosed => false;
    internal bool CompleteSeekPermitted => false;
}

internal sealed partial class NativeSeekLineEndpointCommands
{
    private NativeRootedVector3Buffer? _lineBufferBaseline, _lineBufferExpected;
    private readonly List<SeekLineBufferCommandEffect> _lineBufferCommands = new();
    private readonly List<int> _lineBufferApplyEntered = new();
    private int _lineBufferPending = -1, _lineBufferWrites, _lineBufferRestores;
    private bool _lineBufferPrepared, _lineBufferStoreAdmitted, _lineBufferRestorationEntered;
    private bool _lineBufferRestorationFinished, _lineBufferRestoredVerified, _lineBufferUnrecognized;

    private T LineBufferOperation<T>(Func<IRetainedSeekLineBufferAccess, T> action)
    {
        CheckThread(); ArgumentNullException.ThrowIfNull(action);
        if (_rendererPreparationEntered)
            throw new InvalidOperationException("Manual scratch operations cannot interleave the original renderer journal.");
        if (_busy) throw new InvalidOperationException("The original line buffer cannot synchronously reenter its command owner.");
        // A prior world receipt is historical after any separate scratch
        // operation. The caller must own a fresh projection before entry.
        _appliedVerified = false; _restoredVerified = false; _lastIssuedWorldObservation = null;
        _lineBufferRestoredVerified = false;
        _busy = true; Exception? failure = null;
        try
        {
            Local();
            var source = _source ?? throw new InvalidOperationException("Prepare the original endpoint owner first.");
            return source.WithVerifiedLineBufferOperation(this, (_, access) =>
            {
                Local(); var result = action(access); Local(); return result;
            });
        }
        catch (Exception error)
        {
            _targetOwnershipAcknowledged = false;
            failure = error; _failure ??= error.Message; AdoptCleanup(error);
            if (_lineBufferStoreAdmitted)
            {
                _unknown = true; _retained = true; Attach(error); AttachLineBuffer(error);
            }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposed)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) ExceptionDispatchInfo.Capture(cleanup).Throw();
                    failure.Data["SeekLineBufferOperationCleanup"] = cleanup;
                }
            }
        }
    }

    private static bool SameBuffer(NativeRootedVector3Buffer first, NativeRootedVector3Buffer second)
    {
        if (first.Array != second.Array || first.Class != second.Class
            || first.ElementClass != second.ElementClass || first.Length != second.Length
            || first.Items.Count != second.Items.Count) return false;
        for (var i = 0; i < first.Items.Count; ++i)
            if (first.Items[i] != second.Items[i]) return false;
        return true;
    }

    private void MarkLineBufferUnrecognized()
    {
        _lineBufferUnrecognized = true; _lineBufferRestoredVerified = false;
        _failure ??= "The complete original linePositions array differs from every admitted command state.";
        if (_lineBufferStoreAdmitted) { _unknown = true; _retained = true; }
    }

    private void RequireLineBufferKnown()
    {
        if (_lineBufferUnrecognized)
            throw new NativeVector3BufferConflict("An unrecognized original scratch state forbids another store or inverse.");
    }

    private NativeRootedVector3Buffer ObserveLineBuffer(IRetainedSeekLineBufferAccess access)
    {
        Local(); var observed = access.ReadOriginalLinePositions(); Local();
        if (_lineBufferExpected is not null && !SameBuffer(_lineBufferExpected, observed))
        {
            MarkLineBufferUnrecognized(); RequireLineBufferKnown();
        }
        return observed;
    }

    /// <summary>Retains the actual original LineControl scratch in this same
    /// source/endpoint owner. The baseline includes the entire unused tail.</summary>
    internal NativeRootedVector3Buffer PrepareOriginalLinePositionsBuffer()
        => LineBufferOperation(access =>
        {
            if (!_prepared || _lineBufferPrepared || _lineBufferStoreAdmitted)
                throw new InvalidOperationException("The original line buffer is prepared once by its endpoint owner.");
            var first = access.ReadOriginalLinePositions();
            var second = access.ReadOriginalLinePositions();
            if (!SameBuffer(first, second))
                throw new NativeVector3BufferConflict("The complete original scratch changed during preparation.");
            _lineBufferBaseline = second; _lineBufferExpected = second; _lineBufferPrepared = true;
            return second;
        });

    internal bool VerifyOriginalLinePositionsBuffer()
    {
        var verified = LineBufferOperation(access =>
        {
            if (!_lineBufferPrepared) throw new InvalidOperationException("Prepare the original line buffer first.");
            ObserveLineBuffer(access); RequireLineBufferKnown(); return true;
        });
        return verified;
    }

    internal NativeVector3BufferWriteObservation WriteOriginalLinePositionsPrefix(
        IReadOnlyList<NativeVector3BufferBits> prefix)
        => LineBufferOperation(access =>
        {
            if (!_lineBufferPrepared || _lineBufferRestorationEntered)
                throw new InvalidOperationException("Prepare the original line buffer before forward prefix writes.");
            RequireLineBufferKnown();
            var owned = CopyLineBufferPrefix(prefix);
            ObserveLineBuffer(access);
            return StoreLineBuffer(access, owned, SeekLineBufferDirection.Apply);
        });

    private NativeVector3BufferBits[] CopyLineBufferPrefix(IReadOnlyList<NativeVector3BufferBits> prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix); Local();
        var count = prefix.Count;
        if (count < 1 || count > (_lineBufferExpected?.Length ?? 0))
            throw new ArgumentOutOfRangeException(nameof(prefix), "A nonempty prefix must fit the actual original scratch length.");
        var owned = new NativeVector3BufferBits[count];
        for (var i = 0; i < count; ++i)
        {
            Local(); owned[i] = prefix[i]; Local();
            if (!float.IsFinite(BitConverter.Int32BitsToSingle(owned[i].X))
                || !float.IsFinite(BitConverter.Int32BitsToSingle(owned[i].Y))
                || !float.IsFinite(BitConverter.Int32BitsToSingle(owned[i].Z)))
                throw new ArgumentException("Forward Vector3 prefix components must be finite.", nameof(prefix));
        }
        if (prefix.Count != count) throw new InvalidOperationException("The selected Vector3 prefix changed while copying.");
        Local(); return owned;
    }

    private static NativeRootedVector3Buffer TargetBuffer(NativeRootedVector3Buffer before,
        NativeVector3BufferBits[] prefix)
    {
        var items = before.Items.ToArray();
        Array.Copy(prefix, items, prefix.Length);
        return new(before.Array, before.Class, before.ElementClass, before.Length, Array.AsReadOnly(items));
    }

    private void ResolveLineBuffer(IRetainedSeekLineBufferAccess access, int index)
    {
        var command = _lineBufferCommands[index];
        var actual = access.ReadOriginalLinePositions();
        var before = SameBuffer(actual, command.Before);
        var target = SameBuffer(actual, command.Target);
        _lineBufferCommands[index] = command with
        { Readback = actual, BeforeObserved = before, TargetObserved = target };
        // A callback may have changed storage to our planned target before
        // Runtime entered Marshal.Copy. Its matching bits are still external
        // state and must never become one of this owner's inverse commands.
        if (!before && !target || target && !before && !command.StoreEntered)
            MarkLineBufferUnrecognized();
        else _lineBufferExpected = actual;
        _lineBufferPending = -1;
    }

    private void ConfirmLineBufferStoreEntered(int index, SeekLineBufferDirection direction)
    {
        if (_lineBufferCommands[index].StoreEntered) return;
        _lineBufferCommands[index] = _lineBufferCommands[index] with { StoreEntered = true };
        if (direction == SeekLineBufferDirection.Apply) _lineBufferApplyEntered.Add(index);
    }

    private NativeVector3BufferWriteObservation StoreLineBuffer(IRetainedSeekLineBufferAccess access,
        NativeVector3BufferBits[] prefix, SeekLineBufferDirection direction)
    {
        ObserveLineBuffer(access); RequireLineBufferKnown();
        var before = _lineBufferExpected ?? throw new InvalidOperationException("The original scratch baseline is absent.");
        if (prefix.Length < 1 || prefix.Length > before.Length
            || _lineBufferCommands.Count >= NativeSeekLineRingSourceCapture.MaximumDynamicOccurrences * 2)
            throw new NotSupportedException("The original scratch command exceeds its complete bounded plan.");
        var target = TargetBuffer(before, prefix);
        var index = _lineBufferCommands.Count;
        _lineBufferCommands.Add(new(index, direction, before, target, prefix.Length,
            false, false, false, null, false, false, null));
        var admitted = false; Exception? failure = null;
        NativeVector3BufferWriteObservation? result = null;
        try
        {
            void BeforeStore()
            {
                Local();
                if (admitted || _lineBufferPending >= 0)
                {
                    MarkLineBufferUnrecognized();
                    throw new InvalidOperationException("The original scratch store admission entered twice.");
                }
                // Publish the exact owner and journal before Runtime's final
                // source/array checks and before Marshal.Copy can be entered.
                admitted = true; _lineBufferPending = index;
                _targetOwnershipAcknowledged = false;
                _lineBufferStoreAdmitted = true; _unknown = true; _retained = true;
                if (direction == SeekLineBufferDirection.Apply)
                    ++_lineBufferWrites;
                else ++_lineBufferRestores;
                _lineBufferCommands[index] = _lineBufferCommands[index] with { StoreAdmissionEntered = true };
                Local();
            }
            result = access.WriteOriginalLinePositionsPrefix(before, prefix, BeforeStore);
            if (!admitted || !result.StoreEntered || !SameBuffer(result.Before, before)
                || !SameBuffer(result.After, target))
                throw new NativeVector3BufferConflict("The original scratch write did not return its complete commanded state.");
            ConfirmLineBufferStoreEntered(index, direction);
            _lineBufferCommands[index] = _lineBufferCommands[index] with { StoreReturned = true };
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error.Data["LineVector3BufferStoreEntered"] is true)
                ConfirmLineBufferStoreEntered(index, direction);
        }
        if (admitted && _cleanupFailure is null)
        {
            try { ResolveLineBuffer(access, index); RequireLineBufferKnown(); }
            catch (Exception observation)
            {
                MarkLineBufferUnrecognized();
                if (failure is not null && !ReferenceEquals(failure, observation))
                    failure.Data["SeekLineBufferObservationFailure"] = observation;
                failure ??= observation;
            }
        }
        if (failure is null && !_lineBufferCommands[index].TargetObserved)
            failure = new NativeVector3BufferConflict("The original scratch no longer has the commanded complete storage after write.");
        if (failure is null) return result!;
        _lineBufferCommands[index] = _lineBufferCommands[index] with { Failure = failure.Message };
        _failure ??= failure.Message;
        if (admitted) { Attach(failure); AttachLineBuffer(failure); }
        ExceptionDispatchInfo.Capture(failure).Throw();
        throw new InvalidOperationException("Unreachable original scratch command failure.");
    }

    /// <summary>Selected inverse in reverse entered order. It runs only while
    /// each complete actual array equals its last recognized command state.
    /// External aliases and consumers remain unclosed after restoration.</summary>
    internal void RestoreOriginalLinePositionsBuffer()
    {
        RevokeTargetOwnershipAcknowledgement();
        var restored = LineBufferOperation(access =>
        {
            if (!_lineBufferPrepared || _lineBufferRestorationEntered)
                throw new InvalidOperationException("Original scratch restoration is once-only after preparation.");
            _lineBufferRestorationEntered = true;
            ObserveLineBuffer(access); RequireLineBufferKnown();
            for (var i = _lineBufferApplyEntered.Count - 1; i >= 0; --i)
            {
                ObserveLineBuffer(access); RequireLineBufferKnown();
                var command = _lineBufferCommands[_lineBufferApplyEntered[i]];
                var current = _lineBufferExpected!;
                if (SameBuffer(current, command.Before)) continue;
                if (!SameBuffer(current, command.Target))
                { MarkLineBufferUnrecognized(); RequireLineBufferKnown(); }
                var prefix = command.Before.Items.Take(command.PrefixLength).ToArray();
                StoreLineBuffer(access, prefix, SeekLineBufferDirection.Restore);
            }
            var final = ObserveLineBuffer(access); RequireLineBufferKnown();
            return _lineBufferBaseline is not null && SameBuffer(final, _lineBufferBaseline);
        });
        _lineBufferRestorationFinished = restored;
    }

    internal bool VerifyRestoredOriginalLinePositionsBuffer()
    {
        var verified = LineBufferOperation(access =>
        {
            if (!_lineBufferRestorationEntered || !_lineBufferRestorationFinished)
                throw new InvalidOperationException("Restore the selected original scratch before verification.");
            var current = ObserveLineBuffer(access); RequireLineBufferKnown();
            return _lineBufferBaseline is not null && SameBuffer(current, _lineBufferBaseline);
        });
        _lineBufferRestoredVerified = verified;
        return verified;
    }

    private SeekLineBufferJournal LineBufferJournalCore()
        => new(_lineBufferPrepared, _lineBufferStoreAdmitted, _lineBufferRestorationEntered,
            _lineBufferRestorationFinished, _lineBufferRestoredVerified, _lineBufferUnrecognized,
            _lineBufferStoreAdmitted || _lineBufferUnrecognized, _retained,
            _lineBufferWrites, _lineBufferRestores, _lineBufferBaseline, _lineBufferExpected,
            Array.AsReadOnly(_lineBufferCommands.ToArray()), _source,
            _lineBufferStoreAdmitted ? this : null, _sourceCleanupOwner);

    internal SeekLineBufferJournal GetOriginalLineBufferJournal()
    { Diagnostic(); return LineBufferJournalCore(); }

    private void AttachLineBuffer(Exception error)
    {
        if (!_lineBufferStoreAdmitted) return;
        error.Data["SeekLineBufferEffectsOwner"] = this;
        if (_source is not null) error.Data["SeekLineBufferOriginalSource"] = _source;
        if (_lineBufferBaseline is not null) error.Data["SeekLineBufferBaseline"] = _lineBufferBaseline;
        error.Data["SeekLineBufferJournal"] = LineBufferJournalCore();
    }
}
