// Orthogonal SetPitch icall V1. No copied event or caller completion can mint a
// child/window/retry. Included after the retained watch definitions in this TU.
namespace {
bool LowerPitchBindingMatchesLocked(const Owner* owner) {
    if (!owner->LowerPitchEnabled || owner->LowerPitchWrapperSlot >= SlotCount || owner->LowerPitchLowerSlot >= SlotCount
        || owner->LowerPitchWrapperSlot == owner->LowerPitchLowerSlot) return false;
    const auto& wrapper = slots[owner->LowerPitchWrapperSlot];
    const auto& lower = slots[owner->LowerPitchLowerSlot];
    return wrapper.Configured == 1 && lower.Configured == 1 && wrapper.OwnerValue == owner && lower.OwnerValue == owner
        && wrapper.Kind == RtSourceSingle1 && wrapper.Role == RtSourceSetPitch
        && lower.Kind == RtSourceIcallSingle1 && lower.Role == RtSourceLowerSetPitch
        && wrapper.Target && wrapper.Original && lower.Target && lower.Original
        && wrapper.Target != lower.Target && wrapper.Original != lower.Original;
}
bool LowerPitchChildLocked(Owner* owner, Slot* slot, void* self, void* caller, std::uint64_t arg) {
    const auto* parent = currentScope;
    return slot->Role == RtSourceLowerSetPitch && slot->Kind == RtSourceIcallSingle1
        && LowerPitchBindingMatchesLocked(owner) && slot == &slots[owner->LowerPitchLowerSlot]
        && parent && parent->OwnerValue == owner && parent->SlotValue == &slots[owner->LowerPitchWrapperSlot]
        && parent->Frame.Kind == RtSourceSingle1 && parent->Frame.Role == RtSourceSetPitch
        && parent->Frame.Stage == RtSourceBefore && parent->Frame.Sequence && parent->Frame.Token
        && parent->LowerPitchWindow && !parent->LowerPitchConsumed && !parent->LowerPitchChildSequence
        && !parent->CallbackActive && !parent->SuppressObserver && owner->Thread == GetCurrentThreadId()
        && parent->Frame.ThreadId == GetCurrentThreadId() && parent->Frame.Self == reinterpret_cast<std::uint64_t>(self)
        && parent->Frame.ArgBits == arg && arg <= UINT32_MAX
        && caller == &RtPlaybackLowerPitchWrapperCallReturned
        && owner->Writer == parent->LowerPitchWindowWriter && owner->Source == parent->LowerPitchWindowSource
        && owner->Scheduling == parent->LowerPitchWindowScheduling;
}
bool BeginLowerPitchWrapperOriginal(Scope* scope) {
    auto* owner = scope->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool begin = owner->LowerPitchEnabled && scope->Frame.Role == RtSourceSetPitch
        && scope->Frame.Kind == RtSourceSingle1 && scope->Frame.Stage == RtSourceBefore
        && currentScope == scope && !scope->CallbackActive && !scope->SuppressObserver
        && owner->Thread == GetCurrentThreadId() && scope->Frame.ThreadId == GetCurrentThreadId()
        && LowerPitchBindingMatchesLocked(owner) && scope->SlotValue == &slots[owner->LowerPitchWrapperSlot]
        && !scope->LowerPitchWindow && !scope->LowerPitchConsumed;
    if (begin) {
        scope->LowerPitchWindow = true;
        scope->LowerPitchWindowWriter = owner->Writer; scope->LowerPitchWindowSource = owner->Source;
        scope->LowerPitchWindowScheduling = owner->Scheduling;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return begin;
}
void EndLowerPitchWrapperOriginal(Scope* scope, bool abnormal) {
    auto* owner = scope->OwnerValue;
    AcquireSRWLockExclusive(&owner->Lock);
    scope->LowerPitchWindow = false;
    // The wrapper is a pinned tail transfer. A successful original must have
    // consumed and physically drained exactly its own lower child.
    if (!scope->LowerPitchConsumed || !scope->LowerPitchChildSequence || !scope->LowerPitchChildToken
        || !scope->LowerPitchChildExited || (!abnormal && (!scope->LowerPitchChildReturned || scope->LowerPitchChildAbnormal)))
        FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock);
}
void InvalidateLowerPitchLocked(Owner* owner, Scope* scope) {
    owner->LowerPitchBlocked = true;
    auto* prior = owner->Logical && owner->Logical->Closed ? owner->Logical->Initializer : nullptr;
    if (prior && prior->Info.phase == 6 && !prior->LowerPitchInvalidated) {
        prior->LowerPitchInvalidated = true; prior->LowerPitchInvalidationExited = false;
        prior->LowerPitchInvalidationSequence = scope->Frame.Sequence;
        prior->LowerPitchInvalidationToken = scope->Frame.Token;
        prior->LowerPitchInvalidationWriter = owner->Writer; prior->LowerPitchInvalidationSource = owner->Source;
        prior->LowerPitchInvalidationPlayer = owner->PendingWatch;
        prior->LowerPitchInvalidationRole = scope->Frame.Role;
        prior->Info.phase = 5; scope->LowerPitchInvalidatedInitializer = prior;
    }
}
void LowerPitchPhysicalExitLocked(Scope* scope) {
    if (scope->LowerPitchChild) {
        auto* parent = scope->LowerPitchParent;
        if (!parent || !parent->LowerPitchWindow || !parent->LowerPitchConsumed
            || parent->OwnerValue != scope->OwnerValue || parent->Frame.Sequence != scope->Frame.ParentSequence
            || parent->LowerPitchChildSequence != scope->Frame.Sequence || parent->LowerPitchChildToken != scope->Frame.Token
            || parent->LowerPitchChildExited) FaultLocked(scope->OwnerValue);
        else {
            parent->LowerPitchChildReturned = (scope->Frame.Flags & RtSourceReturnedFlag) != 0;
            parent->LowerPitchChildAbnormal = (scope->Frame.Flags & RtSourceAbnormalFlag) != 0;
            parent->LowerPitchChildExited = true;
        }
    }
    auto* prior = scope->LowerPitchInvalidatedInitializer;
    if (prior && prior->LowerPitchInvalidated && prior->LowerPitchInvalidationSequence == scope->Frame.Sequence
        && prior->LowerPitchInvalidationToken == scope->Frame.Token) prior->LowerPitchInvalidationExited = true;
}
bool LowerPitchPendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending) {
    const auto* prior = owner->Logical && owner->Logical->Closed ? owner->Logical->Initializer : nullptr;
    // On retry the gate points to the completed fresh initializer. The actual
    // prior record remains retained in its native Previous chain.
    const auto* fresh = owner->Initializer;
    for (const auto* value = fresh; value; value = value->Previous) {
        if (!value->LowerPitchInvalidated || value->LowerPitchInvalidationPlayer != pending) continue;
        return owner->LowerPitchBlocked && pending && pending == owner->PendingWatch && pending->Value == owner
            && pending->Phase == 1 && value->Info.phase == 5 && value->LowerPitchInvalidationExited
            && !value->PhysicalInFlight && fresh && fresh != value && prior == fresh && fresh->Info.phase == 6
            && fresh->Info.ordinal > value->Info.ordinal && pending->Source != owner->Source
            && ReadFinalGraph(owner->Initializer) && InitializationResourceReadyLocked(owner, fresh);
    }
    return false;
}
bool LowerPitchPendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress) {
    const auto* replaced = player ? player->ReplacedPending : nullptr;
    if (!pending || pending != owner->PendingProgressWatch || pending->Phase != 1
        || pending->Value != owner || pending->Player != replaced || !replaced || replaced->Phase != 3) return false;
    const auto* fresh = owner->Initializer;
    for (const auto* prior = fresh; prior; prior = prior->Previous)
        if (prior->LowerPitchInvalidated && prior->LowerPitchInvalidationPlayer == replaced)
            return owner->LowerPitchBlocked && prior->Info.phase == 5 && prior->LowerPitchInvalidationExited
                && !prior->PhysicalInFlight && fresh != prior && owner->Logical && owner->Logical->Closed
                && owner->Logical->Initializer == fresh && fresh->Info.phase == 6
                && fresh->Info.ordinal > prior->Info.ordinal && fresh->Info.progress == progress
                && InitializationResourceReadyLocked(owner, fresh);
    return false;
}
bool LowerPitchPendingBackingReplaceLocked(Owner* owner, const UnityBackingWatch* pending, const PlayerWatch* player) {
    const auto* replaced = player ? player->ReplacedPending : nullptr;
    if (!pending || pending != owner->PendingBackingWatch || pending->Phase != 1 || pending->Value != owner
        || pending->Player != replaced || !replaced || replaced->Phase != 3 || !pending->Initializer) return false;
    const auto* prior = pending->Initializer; const auto* fresh = owner->Initializer;
    return owner->LowerPitchBlocked && prior->LowerPitchInvalidated && prior->LowerPitchInvalidationExited
        && prior->LowerPitchInvalidationPlayer == replaced && prior->Info.phase == 5 && !prior->PhysicalInFlight
        && fresh && fresh != prior && owner->Logical && owner->Logical->Closed && owner->Logical->Initializer == fresh
        && fresh->Info.phase == 6 && fresh->Info.ordinal > prior->Info.ordinal && InitializationResourceReadyLocked(owner, fresh);
}
}

std::uint32_t __cdecl RtPlaybackLowerPitchVersion() { return 1; }
int __cdecl RtPlaybackSourceConfigureLowerPitch(void* raw, std::uint32_t wrapperSlot, std::uint32_t lowerSlot) {
    auto* owner = FindOwner(raw);
    if (!owner || wrapperSlot >= SlotCount || lowerSlot >= SlotCount || wrapperSlot == lowerSlot) return 0;
    AcquireSRWLockExclusive(&configureLock); AcquireSRWLockExclusive(&owner->Lock);
    const auto& wrapper = slots[wrapperSlot]; const auto& lower = slots[lowerSlot];
    const bool valid = owner->Thread == GetCurrentThreadId() && !owner->WasArmed && !owner->LowerPitchEnabled
        && !owner->InFlight && owner->Flags == RtSourceDormant && !owner->Faults
        && wrapper.Configured == 1 && lower.Configured == 1 && wrapper.OwnerValue == owner && lower.OwnerValue == owner
        && wrapper.Kind == RtSourceSingle1 && wrapper.Role == RtSourceSetPitch
        && lower.Kind == RtSourceIcallSingle1 && lower.Role == RtSourceLowerSetPitch
        && wrapper.Target && wrapper.Original && lower.Target && lower.Original
        && wrapper.Target != lower.Target && wrapper.Original != lower.Original;
    if (valid) { owner->LowerPitchWrapperSlot = wrapperSlot; owner->LowerPitchLowerSlot = lowerSlot; owner->LowerPitchEnabled = true; }
    ReleaseSRWLockExclusive(&owner->Lock); ReleaseSRWLockExclusive(&configureLock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadLowerPitchInitializationInvalidation(void* raw, void* enumerator,
    std::uint64_t yieldSequence, RtPlaybackLowerPitchInitializationInfoV1* info) {
    auto* owner = FindOwner(raw);
    if (!owner || !enumerator || !yieldSequence || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockShared(&owner->Lock);
    const Initialization* found = nullptr;
    for (auto* value = owner->Initializer; value; value = value->Previous)
        if (value->Info.enumerator == reinterpret_cast<std::uint64_t>(enumerator) && value->Info.yieldSequence == yieldSequence) { found = value; break; }
    if (found) *info = RtPlaybackLowerPitchInitializationInfoV1{1, sizeof(*info), found->LowerPitchInvalidated ? 1u : 0u,
        found->LowerPitchInvalidated ? (found->LowerPitchInvalidationRole == RtSourceEngineSetPitch ? 2u : 1u) : 0u,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
        reinterpret_cast<std::uint64_t>(found), found->Info.ordinal, found->Info.enumerator, found->Info.progress,
        found->Info.yieldSequence, found->LowerPitchInvalidationSequence, found->LowerPitchInvalidationToken,
        found->LowerPitchInvalidationWriter, found->LowerPitchInvalidationSource, reinterpret_cast<std::uint64_t>(found->LowerPitchInvalidationPlayer)};
    ReleaseSRWLockShared(&owner->Lock); return found ? 1 : 0;
}
std::uint32_t __cdecl RtPlaybackSourceRetryLowerPitchInitialization(void* raw, void* prior, void* fresh,
    std::uint32_t action, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical && owner.Logical == raw) { gate = owner.Logical; break; }
    if (!gate || !prior || !fresh || !request || (action != 14 && action != 15)) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    const auto* predecessor = gate->Initializer; auto* candidate = owner->Initializer;
    std::uint32_t result = 0;
    if (!owner->LowerPitchEnabled || owner->Flags != RtSourceArmed || owner->Faults || owner->Thread != GetCurrentThreadId()) result = 3;
    else if (!gate->Closed || !owner->FreshBlocked || !owner->LowerPitchBlocked
        || !predecessor || predecessor != prior || predecessor == candidate || !predecessor->LowerPitchInvalidated
        || !predecessor->LowerPitchInvalidationExited || predecessor->Info.phase != 5
        || !predecessor->LowerPitchInvalidationSequence || !predecessor->LowerPitchInvalidationToken
        || !predecessor->LowerPitchInvalidationWriter || predecessor->LowerPitchInvalidationWriter > owner->Writer
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
