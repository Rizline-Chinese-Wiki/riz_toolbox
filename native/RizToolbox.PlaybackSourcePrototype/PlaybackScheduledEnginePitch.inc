// PlayScheduled's fixed native Play path can reapply the current pitch without
// becoming a second semantic writer. Only its actual original-call window can
// delegate one physical EngineSetPitch child; raw configuration is not a ticket.
namespace {
bool ScheduledEnginePitchCallMatchesLocked(const Owner* owner) {
    if (!owner->ScheduledEnginePitchEnabled || !owner->EnginePitchEnabled
        || owner->EnginePitchEngineSlot >= SlotCount || !owner->ScheduledEngineReapplyBody
        || owner->ScheduledEngineReapplyLength < 5 || !owner->ScheduledEnginePitchCaller) return false;
    const auto begin = reinterpret_cast<std::uintptr_t>(owner->ScheduledEngineReapplyBody);
    const auto returned = reinterpret_cast<std::uintptr_t>(owner->ScheduledEnginePitchCaller);
    if (returned < begin || returned - begin < 5
        || returned - begin > owner->ScheduledEngineReapplyLength) return false;
    __try {
        const auto* call = reinterpret_cast<const unsigned char*>(returned - 5);
        const auto displacement = *reinterpret_cast<const std::int32_t*>(call + 1);
        return call[0] == 0xe8 && reinterpret_cast<const void*>(returned + displacement)
            == slots[owner->EnginePitchEngineSlot].Target;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool ScheduledEnginePitchBindingMatchesLocked(const Owner* owner) {
    if (!owner->ScheduledEnginePitchEnabled || owner->ScheduledEnginePitchSlot >= SlotCount
        || !owner->ScheduledEnginePlayCache || !owner->ScheduledEngineNativePlay
        || !EnginePitchBindingMatchesLocked(owner)
        || owner->ScheduledEnginePitchSlot == owner->EnginePitchEngineSlot) return false;
    const auto& schedule = slots[owner->ScheduledEnginePitchSlot];
    return schedule.Configured == 1 && schedule.OwnerValue == owner
        && schedule.Kind == RtSourceDouble1 && schedule.Role == RtSourcePlayScheduled
        && schedule.Target && schedule.Original && ScheduledEnginePitchCallMatchesLocked(owner);
}
bool ScheduledEnginePlayCacheMatchesLocked(const Owner* owner, bool resolved) {
    if (!owner->ScheduledEnginePlayCache || !owner->ScheduledEngineNativePlay) return false;
    __try {
        const auto* cache = reinterpret_cast<const volatile std::uintptr_t*>(owner->ScheduledEnginePlayCache);
        const auto first = *cache;
        const auto second = *cache;
        const auto expected = reinterpret_cast<std::uintptr_t>(owner->ScheduledEngineNativePlay);
        return first == second && (first == expected || (!resolved && first == 0));
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
const UnityBackingWatch* ScheduledEngineBackingLocked(const Owner* owner, std::uint64_t wrapper) {
    const UnityBackingWatch* selected = nullptr;
    const auto* active = owner->BackingWatch;
    if (active && active->Value == owner && active->Phase == 2 && active->Source == wrapper
        && active->SourceFence == owner->Source && active->Player && active->Player == owner->Watch
        && active->Player->Value == owner && active->Player->Phase == 2
        && active->Player->AudioSource == wrapper && active->Player->Source == owner->Source) selected = active;
    const auto* pending = owner->PendingBackingWatch;
    if (pending && pending->Value == owner && pending->Phase == 1 && pending->Source == wrapper
        && pending->SourceFence == owner->Source && pending->Player && pending->Player == owner->PendingWatch
        && pending->Player->Value == owner && pending->Player->Phase == 1
        && pending->Player->AudioSource == wrapper && pending->Player->Source == owner->Source) selected = pending;
    return selected;
}
bool ReadScheduledEnginePitchBits(std::uint64_t native, std::uint32_t* bits) {
    __try {
        const auto* field = reinterpret_cast<const volatile std::uint32_t*>(native + 0x1f4);
        const auto first = *field;
        const auto second = *field;
        if (first != second) return false;
        *bits = first; return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool BeginScheduledOriginal(Scope* scope) {
    auto* owner = scope->OwnerValue;
    if (scope->Frame.Role != RtSourcePlayScheduled || !owner->ScheduledEnginePitchEnabled) return false;
    AcquireSRWLockExclusive(&owner->Lock);
    bool begin = owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed && !owner->Faults
        && !owner->FreshBlocked && !owner->LowerPitchBlocked && !owner->AudioBlocked && !owner->BackingBlocked
        && (!owner->Logical || !owner->Logical->Closed)
        && currentScope == scope && scope->Frame.Kind == RtSourceDouble1 && scope->Frame.Stage == RtSourceBefore
        && (scope->Frame.Flags & RtSourceArmed) != 0
        && (scope->Frame.Flags & (RtSourceDormant | RtSourceUnknown | RtSourceWithdrawn)) == 0
        && scope->Frame.OwnerEpoch == owner->Epoch && scope->Frame.ThreadId == owner->Thread
        && scope->Frame.Sequence && scope->Frame.Token && !scope->CallbackActive && !scope->SuppressObserver
        && owner->Writer == scope->Frame.WriterSequence
        && owner->Source == scope->Frame.SourceSequence
        && owner->Scheduling == scope->Frame.SchedulingSequence
        && !scope->EnginePitchWindow && !scope->EnginePitchWindowMode && !scope->EnginePitchConsumed
        && !scope->EnginePitchChildSequence && !scope->EnginePitchChildToken && !scope->EnginePitchChildExited
        && scope->SlotValue == &slots[owner->ScheduledEnginePitchSlot]
        && ScheduledEnginePitchBindingMatchesLocked(owner) && ScheduledEnginePlayCacheMatchesLocked(owner, false);
    Scope association{};
    std::uint32_t pitchBits = 0;
    if (begin) {
        // The incoming self is a wrapper. NativeSelf is borrowed only from this
        // owner's retained watch, then revalidated through its private receipt.
        const auto* watch = ScheduledEngineBackingLocked(owner, scope->Frame.Self);
        begin = watch && watch->Ordinal && watch->Sample.SourceNative
            && EnginePitchRelevantLocked(owner, reinterpret_cast<void*>(watch->Sample.SourceNative), &association)
            && association.EnginePitchAssociation.State == 1
            && association.EnginePitchAssociation.Receipt == reinterpret_cast<std::uint64_t>(watch)
            && association.EnginePitchAssociation.Ordinal == watch->Ordinal
            && association.EnginePitchAssociation.Wrapper == scope->Frame.Self
            && association.EnginePitchAssociation.NativeSelf == watch->Sample.SourceNative;
        if (begin && !ReadScheduledEnginePitchBits(association.EnginePitchAssociation.NativeSelf, &pitchBits)) {
            FaultLocked(owner); begin = false;
        }
    }
    if (begin) {
        scope->EnginePitchWindow = true; scope->EnginePitchWindowMode = 2;
        scope->EnginePitchNativeSelf = association.EnginePitchAssociation.NativeSelf;
        scope->ScheduledEngineAssociation = association.EnginePitchAssociation;
        scope->ScheduledEnginePitchBits = pitchBits;
        scope->ScheduledEngineInputBits = scope->Frame.ArgBits;
        scope->EnginePitchWindowWriter = owner->Writer; scope->EnginePitchWindowSource = owner->Source;
        scope->EnginePitchWindowScheduling = owner->Scheduling;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return begin;
}
bool ScheduledEnginePitchChildLocked(Owner* owner, Slot* slot, void* self, void* caller,
    std::uint64_t arg, const Scope* child) {
    const auto* parent = currentScope;
    if (!owner->ScheduledEnginePitchEnabled || owner->Thread != GetCurrentThreadId()
        || owner->Flags != RtSourceArmed || owner->Faults
        || owner->FreshBlocked || owner->LowerPitchBlocked || owner->AudioBlocked || owner->BackingBlocked
        || (owner->Logical && owner->Logical->Closed)
        || !ScheduledEnginePitchBindingMatchesLocked(owner)
        || slot != &slots[owner->EnginePitchEngineSlot] || slot->Role != RtSourceEngineSetPitch || slot->Kind != RtSourceIcallSingle1
        || !parent || parent->OwnerValue != owner || parent->SlotValue != &slots[owner->ScheduledEnginePitchSlot]
        || parent->Frame.Kind != RtSourceDouble1 || parent->Frame.Role != RtSourcePlayScheduled
        || (parent->Frame.Flags & RtSourceArmed) == 0
        || (parent->Frame.Flags & (RtSourceDormant | RtSourceUnknown | RtSourceWithdrawn)) != 0
        || parent->Frame.OwnerEpoch != owner->Epoch || parent->Frame.Stage != RtSourceBefore
        || parent->Frame.ThreadId != owner->Thread || !parent->Frame.Sequence || !parent->Frame.Token
        || child->Frame.OwnerEpoch != owner->Epoch || child->Frame.ThreadId != owner->Thread
        || child->Frame.ParentSequence != parent->Frame.Sequence || child->Frame.Depth != parent->Frame.Depth + 1
        || !parent->EnginePitchWindow || parent->EnginePitchWindowMode != 2
        || parent->EnginePitchConsumed || parent->EnginePitchChildSequence || parent->EnginePitchChildToken || parent->EnginePitchChildExited
        || parent->CallbackActive || parent->SuppressObserver || child->SuppressObserver
        || caller != owner->ScheduledEnginePitchCaller || arg > UINT32_MAX || arg != parent->ScheduledEnginePitchBits
        || parent->Frame.ArgBits != parent->ScheduledEngineInputBits
        || parent->EnginePitchNativeSelf != reinterpret_cast<std::uint64_t>(self)
        || child->EnginePitchAssociation.State != 1
        || child->EnginePitchAssociation.Wrapper != parent->Frame.Self
        || child->EnginePitchAssociation.NativeSelf != parent->EnginePitchNativeSelf
        || child->EnginePitchAssociation.Kind != parent->ScheduledEngineAssociation.Kind
        || child->EnginePitchAssociation.Receipt != parent->ScheduledEngineAssociation.Receipt
        || child->EnginePitchAssociation.Ordinal != parent->ScheduledEngineAssociation.Ordinal
        || child->EnginePitchAssociation.WrapperClass != parent->ScheduledEngineAssociation.WrapperClass
        || child->EnginePitchAssociation.InstanceIdOffset != parent->ScheduledEngineAssociation.InstanceIdOffset
        || child->EnginePitchAssociation.SourceId != parent->ScheduledEngineAssociation.SourceId
        || owner->Writer != parent->EnginePitchWindowWriter || owner->Source != parent->EnginePitchWindowSource
        || owner->Scheduling != parent->EnginePitchWindowScheduling) return false;
    // The first original is allowed to resolve its zero cache naturally. The
    // actual native child must already have resolved this exact Play target.
    return ScheduledEnginePlayCacheMatchesLocked(owner, true);
}
void EndScheduledOriginal(Scope* scope, bool abnormal) {
    auto* owner = scope->OwnerValue; AcquireSRWLockExclusive(&owner->Lock);
    const bool window = scope->EnginePitchWindow && scope->EnginePitchWindowMode == 2;
    scope->EnginePitchWindow = false; scope->EnginePitchWindowMode = 0;
    // Even if the parent unwinds abnormally, a consumed child must have
    // completed its own normal return and physical exit first.
    (void)abnormal;
    if (!window || (scope->EnginePitchConsumed && (!scope->EnginePitchChildSequence || !scope->EnginePitchChildToken
        || !scope->EnginePitchChildExited || !scope->EnginePitchChildReturned || scope->EnginePitchChildAbnormal)))
        FaultLocked(owner);
    // Some legal Play paths do not reapply pitch. Zero children grants no
    // completion; a consumed child always retains its exact physical duty.
    if (!scope->EnginePitchConsumed && (scope->EnginePitchChildSequence || scope->EnginePitchChildToken
        || scope->EnginePitchChildExited)) FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock);
}
}

std::uint32_t __cdecl RtPlaybackScheduledEnginePitchVersion() { return 1; }
int __cdecl RtPlaybackSourceConfigureScheduledEnginePitch(void* raw,
    std::uint32_t scheduleSlot, std::uint32_t engineSlot, void* exactEngineCallReturn,
    void* reapplyBody, std::uint32_t reapplyLength, void* playCache, void* nativePlayTarget) {
    auto* owner = FindOwner(raw);
    if (!owner || scheduleSlot >= SlotCount || engineSlot >= SlotCount || scheduleSlot == engineSlot
        || !exactEngineCallReturn || !reapplyBody || reapplyLength < 5 || !playCache || !nativePlayTarget) return 0;
    AcquireSRWLockExclusive(&configureLock); AcquireSRWLockExclusive(&owner->Lock);
    bool valid = owner->Thread == GetCurrentThreadId() && !owner->WasArmed && !owner->ScheduledEnginePitchEnabled
        && !owner->InFlight && owner->Flags == RtSourceDormant && !owner->Faults
        && EnginePitchBindingMatchesLocked(owner) && engineSlot == owner->EnginePitchEngineSlot;
    if (valid) {
        owner->ScheduledEnginePitchSlot = scheduleSlot; owner->ScheduledEnginePitchCaller = exactEngineCallReturn;
        owner->ScheduledEngineReapplyBody = reapplyBody; owner->ScheduledEngineReapplyLength = reapplyLength;
        owner->ScheduledEnginePlayCache = playCache; owner->ScheduledEngineNativePlay = nativePlayTarget;
        owner->ScheduledEnginePitchEnabled = true;
        valid = ScheduledEnginePitchBindingMatchesLocked(owner) && ScheduledEnginePlayCacheMatchesLocked(owner, false);
        if (!valid) {
            owner->ScheduledEnginePitchEnabled = false; owner->ScheduledEnginePitchSlot = 0;
            owner->ScheduledEnginePitchCaller = nullptr; owner->ScheduledEngineReapplyBody = nullptr;
            owner->ScheduledEngineReapplyLength = 0; owner->ScheduledEnginePlayCache = nullptr;
            owner->ScheduledEngineNativePlay = nullptr;
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); ReleaseSRWLockExclusive(&configureLock); return valid ? 1 : 0;
}
