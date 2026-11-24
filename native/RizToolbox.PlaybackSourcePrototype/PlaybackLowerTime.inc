// Private lower-time V1. Actual scopes retain child and interruption evidence.
namespace {
bool LowerTimeBindingMatchesLocked(const Owner* owner) {
    if (!owner->LowerTimeEnabled || owner->LowerTimeWrapperSlot >= SlotCount
        || owner->LowerTimeLowerSlot >= SlotCount || owner->LowerTimeWrapperSlot == owner->LowerTimeLowerSlot) return false;
    const auto& wrapper = slots[owner->LowerTimeWrapperSlot];
    const auto& lower = slots[owner->LowerTimeLowerSlot];
    return wrapper.Configured == 1 && lower.Configured == 1
        && wrapper.OwnerValue == owner && lower.OwnerValue == owner
        && wrapper.Kind == RtSourceSingle1 && wrapper.Role == RtSourceSetTime
        && lower.Kind == RtSourceIcallSingle1 && lower.Role == RtSourceLowerSetTime
        && wrapper.Target && wrapper.Original && lower.Target && lower.Original
        && wrapper.Target != lower.Target && wrapper.Original != lower.Original;
}
bool LowerTimeChildLocked(Owner* owner, Slot* slot, void* self, void* caller, std::uint64_t arg) {
    const auto* parent = currentScope;
    return slot->Role == RtSourceLowerSetTime && slot->Kind == RtSourceIcallSingle1
        && LowerTimeBindingMatchesLocked(owner) && slot == &slots[owner->LowerTimeLowerSlot]
        && parent && parent->OwnerValue == owner && parent->SlotValue == &slots[owner->LowerTimeWrapperSlot]
        && parent->Frame.Kind == RtSourceSingle1 && parent->Frame.Role == RtSourceSetTime
        && parent->Frame.Stage == RtSourceBefore && parent->Frame.Sequence && parent->Frame.Token
        && parent->LowerTimeWindow && !parent->LowerTimeConsumed && !parent->LowerTimeChildSequence
        && !parent->CallbackActive && !parent->SuppressObserver && owner->Thread == GetCurrentThreadId()
        && parent->Frame.ThreadId == GetCurrentThreadId() && parent->Frame.Self == reinterpret_cast<std::uint64_t>(self)
        && parent->Frame.ArgBits == arg && arg <= UINT32_MAX && caller == &RtPlaybackLowerTimeWrapperCallReturned
        && owner->Writer == parent->LowerTimeWindowWriter && owner->Source == parent->LowerTimeWindowSource
        && owner->Scheduling == parent->LowerTimeWindowScheduling;
}
bool BeginLowerTimeWrapperOriginal(Scope* scope) {
    auto* owner = scope->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool begin = scope->Frame.Role == RtSourceSetTime && scope->Frame.Kind == RtSourceSingle1
        && scope->Frame.Stage == RtSourceBefore && currentScope == scope && !scope->CallbackActive
        && !scope->SuppressObserver && owner->Thread == GetCurrentThreadId()
        && scope->Frame.ThreadId == GetCurrentThreadId() && LowerTimeBindingMatchesLocked(owner)
        && scope->SlotValue == &slots[owner->LowerTimeWrapperSlot] && !scope->LowerTimeWindow && !scope->LowerTimeConsumed;
    if (begin) {
        scope->LowerTimeWindow = true;
        scope->LowerTimeWindowWriter = owner->Writer; scope->LowerTimeWindowSource = owner->Source;
        scope->LowerTimeWindowScheduling = owner->Scheduling;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return begin;
}
void EndLowerTimeWrapperOriginal(Scope* scope, bool abnormal) {
    auto* owner = scope->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    scope->LowerTimeWindow = false;
    if (!scope->LowerTimeConsumed || !scope->LowerTimeChildSequence || !scope->LowerTimeChildToken
        || !scope->LowerTimeChildExited || (!abnormal && (!scope->LowerTimeChildReturned || scope->LowerTimeChildAbnormal))
        || owner->Writer != scope->LowerTimeWindowWriter || owner->Source != scope->LowerTimeWindowSource
        || owner->Scheduling != scope->LowerTimeWindowScheduling)
        FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock);
}
void InvalidateAudioInterruptionLocked(Owner* owner, Scope* scope,
    std::uint64_t writer, std::uint64_t source, std::uint64_t scheduling) {
    if (!owner->LowerTimeEnabled || scope->LowerTimeChild) return;
    auto* prior = owner->Logical && owner->Logical->Closed ? owner->Logical->Initializer : nullptr;
    const bool direct = scope->Frame.Role == RtSourceLowerSetTime;
    const bool stagedWrapper = scope->Frame.Role == RtSourceSetTime && prior && prior->Info.phase == 6;
    if (!direct && !stagedWrapper) return;
    owner->AudioBlocked = true;
    if (prior && prior->Info.phase == 6 && !prior->AudioInvalidated) {
        prior->AudioInvalidated = true;
        prior->AudioWriterBefore = writer; prior->AudioSourceBefore = source; prior->AudioStampBefore = scheduling;
        prior->AudioPlayer = owner->PendingWatch; prior->Info.phase = 5;
        scope->AudioInvalidatedInitializer = prior;
    }
}
void CaptureAudioInterruptionFrameLocked(Scope* scope) {
    auto* prior = scope->AudioInvalidatedInitializer;
    if (prior) prior->AudioAttempt = scope->Frame;
}
void LowerTimePhysicalExitLocked(Scope* scope) {
    auto* owner = scope->OwnerValue;
    if (scope->LowerTimeChild) {
        auto* parent = scope->LowerTimeParent;
        if (!parent || !parent->LowerTimeWindow || !parent->LowerTimeConsumed
            || parent->OwnerValue != owner || parent->Frame.Sequence != scope->Frame.ParentSequence
            || parent->LowerTimeChildSequence != scope->Frame.Sequence || parent->LowerTimeChildToken != scope->Frame.Token
            || parent->LowerTimeChildExited) FaultLocked(owner);
        else {
            parent->LowerTimeChildReturned = (scope->Frame.Flags & RtSourceReturnedFlag) != 0;
            parent->LowerTimeChildAbnormal = (scope->Frame.Flags & RtSourceAbnormalFlag) != 0;
            parent->LowerTimeChildExited = true;
        }
    }
    auto* prior = scope->AudioInvalidatedInitializer;
    if (prior && prior->AudioAttempt.Sequence == scope->Frame.Sequence && prior->AudioAttempt.Token == scope->Frame.Token)
        prior->AudioAttempt = scope->Frame;
}
bool AudioAttemptExitedNormally(const Initialization* prior) {
    if (!prior || !prior->AudioInvalidated || prior->Info.phase != 5) return false;
    const auto& frame = prior->AudioAttempt;
    const bool role = (frame.Role == RtSourceSetTime && frame.Kind == RtSourceSingle1)
        || (frame.Role == RtSourceLowerSetTime && frame.Kind == RtSourceIcallSingle1 && frame.IncomingMethod == 0);
    return role && frame.Sequence && frame.Token && prior->AudioStampBefore != UINT64_MAX
        && frame.SchedulingSequence == prior->AudioStampBefore + 1
        && frame.WriterSequence == prior->AudioWriterBefore && frame.SourceSequence == prior->AudioSourceBefore
        && (frame.Flags & (RtSourceReturnedFlag | RtSourcePhysicalExitedFlag)) == (RtSourceReturnedFlag | RtSourcePhysicalExitedFlag)
        && !(frame.Flags & (RtSourceUnknown | RtSourceAbnormalFlag | RtSourceWithdrawn | RtSourceLowerTimeChildFlag));
}
bool AudioFreshReplacementLocked(Owner* owner, const Initialization* prior) {
    const auto* fresh = owner->Initializer;
    return owner->AudioBlocked && AudioAttemptExitedNormally(prior) && !prior->PhysicalInFlight
        && fresh && fresh != prior && owner->Logical && owner->Logical->Closed
        && owner->Logical->Initializer == fresh && fresh->Info.phase == 6 && fresh->Info.ordinal > prior->Info.ordinal
        && InitializationResourceReadyLocked(owner, fresh);
}
bool AudioPendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending) {
    if (!pending || pending != owner->PendingWatch || pending->Value != owner || pending->Phase != 1) return false;
    for (auto* prior = owner->Initializer; prior; prior = prior->Previous)
        if (prior->AudioPlayer == pending && AudioFreshReplacementLocked(owner, prior))
            return pending->Source != owner->Source && ReadFinalGraph(owner->Initializer);
    return false;
}
bool AudioPendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress) {
    const auto* replaced = player ? player->ReplacedPending : nullptr;
    if (!pending || pending != owner->PendingProgressWatch || pending->Value != owner || pending->Phase != 1
        || !replaced || replaced->Phase != 3 || pending->Player != replaced) return false;
    for (auto* prior = owner->Initializer; prior; prior = prior->Previous)
        if (prior->AudioPlayer == replaced && AudioFreshReplacementLocked(owner, prior))
            return owner->Initializer->Info.progress == progress;
    return false;
}
bool AudioPendingBackingReplaceLocked(Owner* owner, const UnityBackingWatch* pending, const PlayerWatch* player) {
    const auto* replaced = player ? player->ReplacedPending : nullptr;
    return pending && pending == owner->PendingBackingWatch && pending->Value == owner && pending->Phase == 1
        && replaced && replaced->Phase == 3 && pending->Player == replaced && pending->Initializer
        && pending->Initializer->AudioPlayer == replaced && AudioFreshReplacementLocked(owner, pending->Initializer);
}
}

std::uint32_t __cdecl RtPlaybackLowerTimeVersion() { return 1; }
std::uint32_t __cdecl RtPlaybackAudioInterruptionVersion() { return 1; }
int __cdecl RtPlaybackSourceConfigureLowerTime(void* raw, std::uint32_t wrapperSlot, std::uint32_t lowerSlot) {
    auto* owner = FindOwner(raw);
    if (!owner || wrapperSlot >= SlotCount || lowerSlot >= SlotCount || wrapperSlot == lowerSlot) return 0;
    AcquireSRWLockExclusive(&configureLock); AcquireSRWLockExclusive(&owner->Lock);
    const auto& wrapper = slots[wrapperSlot]; const auto& lower = slots[lowerSlot];
    const bool valid = owner->Thread == GetCurrentThreadId() && !owner->WasArmed && !owner->LowerTimeEnabled
        && !owner->InFlight && owner->Flags == RtSourceDormant && !owner->Faults
        && wrapper.Configured == 1 && lower.Configured == 1 && wrapper.OwnerValue == owner && lower.OwnerValue == owner
        && wrapper.Kind == RtSourceSingle1 && wrapper.Role == RtSourceSetTime
        && lower.Kind == RtSourceIcallSingle1 && lower.Role == RtSourceLowerSetTime
        && wrapper.Target && wrapper.Original && lower.Target && lower.Original
        && wrapper.Target != lower.Target && wrapper.Original != lower.Original;
    if (valid) { owner->LowerTimeWrapperSlot = wrapperSlot; owner->LowerTimeLowerSlot = lowerSlot; owner->LowerTimeEnabled = true; }
    ReleaseSRWLockExclusive(&owner->Lock); ReleaseSRWLockExclusive(&configureLock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadAudioInterruption(void* raw, void* enumerator,
    std::uint64_t yieldSequence, RtPlaybackAudioInterruptionInfoV1* info) {
    auto* owner = FindOwner(raw);
    if (!owner || !enumerator || !yieldSequence || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockShared(&owner->Lock);
    const Initialization* found = nullptr;
    for (auto* value = owner->Initializer; owner->LowerTimeEnabled && value; value = value->Previous)
        if (value->Info.enumerator == reinterpret_cast<std::uint64_t>(enumerator) && value->Info.yieldSequence == yieldSequence) {
            found = value; break;
        }
    if (found) {
        RtPlaybackAudioInterruptionInfoV1 actual{};
        actual.Version = 1; actual.Size = sizeof(actual); actual.Invalidated = found->AudioInvalidated ? 1u : 0u;
        actual.Owner = reinterpret_cast<std::uint64_t>(owner); actual.OwnerEpoch = owner->Epoch;
        actual.Initializer = reinterpret_cast<std::uint64_t>(found); actual.InitializerOrdinal = found->Info.ordinal;
        actual.Enumerator = found->Info.enumerator; actual.Progress = found->Info.progress; actual.YieldSequence = found->Info.yieldSequence;
        if (found->AudioInvalidated) {
            const auto& attempt = found->AudioAttempt;
            actual.Reason = attempt.Role == RtSourceSetTime ? 1u : 2u; actual.FenceKind = 2; actual.AttemptRole = attempt.Role;
            actual.ActualFlags = attempt.Flags; actual.AttemptSequence = attempt.Sequence; actual.AttemptToken = attempt.Token;
            actual.Writer = attempt.WriterSequence; actual.Source = attempt.SourceSequence; actual.Scheduling = attempt.SchedulingSequence;
            actual.ActualStamp = attempt.SchedulingSequence; actual.PlayerWatchReceipt = reinterpret_cast<std::uint64_t>(found->AudioPlayer);
        }
        *info = actual;
    }
    ReleaseSRWLockShared(&owner->Lock); return found ? 1 : 0;
}
std::uint32_t __cdecl RtPlaybackSourceRetryAudioInterruptedInitialization(void* raw, void* prior, void* fresh,
    std::uint32_t action, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical && owner.Logical == raw) { gate = owner.Logical; break; }
    if (!gate || !prior || !fresh || !request || (action != 16 && action != 17)) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    const auto* predecessor = gate->Initializer; auto* candidate = owner->Initializer;
    std::uint32_t result = 0;
    if (!owner->LowerTimeEnabled || owner->Flags != RtSourceArmed || owner->Faults || owner->Thread != GetCurrentThreadId()) result = 3;
    else if (!gate->Closed || !owner->FreshBlocked || !owner->AudioBlocked
        || !predecessor || predecessor != prior || predecessor == candidate || !AudioAttemptExitedNormally(predecessor)
        || predecessor->AudioAttempt.Owner != reinterpret_cast<std::uint64_t>(owner) || predecessor->AudioAttempt.OwnerEpoch != owner->Epoch
        || predecessor->AudioAttempt.SchedulingSequence > owner->Scheduling
        || !candidate || candidate != fresh || candidate->Info.phase != 4 || candidate->Info.ordinal <= predecessor->Info.ordinal
        || candidate->Info.progress != predecessor->Info.progress) result = 2;
    else if (owner->InFlight || predecessor->PhysicalInFlight || candidate->PhysicalInFlight
        || owner->Writer != candidate->Info.writer || owner->Source != candidate->Info.sourceFence
        || owner->Scheduling != candidate->Info.scheduling || !ReadFinalGraph(candidate)
        || !InitializationResourceReadyLocked(owner, candidate)) result = 8;
    else {
        const RizLogicalSourceSnapshot snapshot{reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
            owner->Entered, owner->Exited, owner->InFlight, owner->Writer, owner->Source, owner->Scheduling,
            reinterpret_cast<std::uint64_t>(candidate), candidate->Info.player, candidate->Info.source, candidate->Info.clip};
        result = gate->Commit(gate->Context, gate, action, &snapshot, request);
        if (!result) { gate->Initializer = candidate; candidate->Info.phase = 6; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}
