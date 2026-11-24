// Native-backing SetPitch V1. This module never calls IL2CPP and never treats
// incoming native self as a managed object. All authority stays in private TLS,
// immutable registrations, retained watch/initializer records, and Owner.Lock.
namespace {
bool EnginePitchCallMatchesLocked(const Owner* owner) {
    if (!owner->EnginePitchEnabled || owner->EnginePitchLowerSlot >= SlotCount
        || owner->EnginePitchEngineSlot >= SlotCount) return false;
    const auto& lower = slots[owner->EnginePitchLowerSlot];
    const auto& engine = slots[owner->EnginePitchEngineSlot];
    const auto begin = reinterpret_cast<std::uintptr_t>(lower.Target);
    const auto returned = reinterpret_cast<std::uintptr_t>(owner->EnginePitchCaller);
    __try {
        if (returned < begin + 5 || returned > begin + 495) return false;
        const auto* call = reinterpret_cast<const unsigned char*>(returned - 5);
        const auto displacement = *reinterpret_cast<const std::int32_t*>(call + 1);
        return call[0] == 0xe8 && reinterpret_cast<const void*>(returned + displacement) == engine.Target;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool EnginePitchBindingMatchesLocked(const Owner* owner) {
    if (!LowerPitchBindingMatchesLocked(owner) || !owner->EnginePitchEnabled
        || owner->EnginePitchLowerSlot != owner->LowerPitchLowerSlot
        || owner->EnginePitchEngineSlot >= SlotCount
        || owner->EnginePitchEngineSlot == owner->EnginePitchLowerSlot
        || owner->EnginePitchEngineSlot == owner->LowerPitchWrapperSlot) return false;
    const auto& engine = slots[owner->EnginePitchEngineSlot];
    const auto& lower = slots[owner->EnginePitchLowerSlot];
    return engine.Configured == 1 && engine.OwnerValue == owner
        && engine.Kind == RtSourceIcallSingle1 && engine.Role == RtSourceEngineSetPitch
        && engine.Target && engine.Original && engine.Target != lower.Target
        && engine.Original != lower.Original && EnginePitchCallMatchesLocked(owner);
}
bool EnginePitchInitializerGraphLocked(Owner* owner, const Initialization* value) {
    if (!value || value->Info.owner != reinterpret_cast<std::uint64_t>(owner)
        || value->Info.ownerEpoch != owner->Epoch || !value->Info.ordinal
        || !owner->WatchProgressClass || !owner->WatchAudioClass || !owner->UnityObjectClass) return false;
    const auto* progress = RelevantProgressWatchLocked(owner, value->Info.progress);
    if (!progress || progress->Value != owner || !progress->Ordinal
        || progress->Class != reinterpret_cast<std::uint64_t>(owner->WatchProgressClass)) return false;
    __try {
        return PlayerClassMatches(progress->Progress, progress->Class)
            && *reinterpret_cast<const std::uint64_t*>(progress->Progress + 0x28) == value->Info.source
            && *reinterpret_cast<const std::uint64_t*>(progress->Progress + 0x38) == value->Info.player
            && PlayerClassMatches(value->Info.player, reinterpret_cast<std::uint64_t>(owner->WatchClass))
            && *reinterpret_cast<const std::uint64_t*>(value->Info.player + 0x10) == value->Info.source;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool CaptureEnginePitchInitializationLocked(Owner* owner, Initialization* value) {
    if (!owner->EnginePitchEnabled) return true;
    // An actual completed Start already supplied its one real SetClip child.
    // Borrow the separately retained Progress root before any seven-root watch
    // exists. Merely configuring native pointers cannot populate this record.
    if (value != owner->Initializer || value->Info.phase != 4 || !value->ChildNormal
        || !value->Info.finalSequence || !value->Info.finalToken || value->Children != 1
        || !EnginePitchInitializerGraphLocked(owner, value)) { FaultLocked(owner); return false; }
    UnityBackingWatch probe{};
    probe.Source = value->Info.source; probe.SourceClass = reinterpret_cast<std::uint64_t>(owner->WatchAudioClass);
    probe.Clip = value->Info.clip; probe.ClipClass = owner->UnityClipClass;
    UnityBackingSample first{}, second{};
    if (!ReadUnityBackingSample(owner, &probe, &first) || !ReadUnityBackingSample(owner, &probe, &second)
        || !EqualUnityBackingSample(first, second) || !first.SourceNative || !first.ClipNative
        || !first.SourceId || !first.ClipId) { FaultLocked(owner); return false; }
    value->EnginePitchSourceNative = first.SourceNative; value->EnginePitchClipNative = first.ClipNative;
    value->EnginePitchOffset = first.Offset; value->EnginePitchSourceId = first.SourceId;
    value->EnginePitchClipId = first.ClipId; value->EnginePitchIdentity = true;
    return true;
}
// This is only a prefilter over an already retained wrapper, never a read from
// incoming native self. A matching old identity remains relevant when class,
// cachedPtr, ID, or a read boundary has changed.
bool EnginePitchReceiverMatches(std::uint64_t receiver, std::uint64_t wrapper, std::uint64_t native) {
    if (!receiver || !wrapper) return false;
    if (receiver == native) return true;
    __try { return *reinterpret_cast<const std::uint64_t*>(wrapper + 0x10) == receiver; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void EnginePitchAssociation(Owner* owner, Scope* scope, std::uint32_t kind, std::uint64_t wrapper,
    std::uint64_t cls, std::uint64_t receiver, void* receipt, std::uint64_t ordinal,
    std::int32_t offset, std::int32_t id) {
    scope->EnginePitchAssociation = RtPlaybackEnginePitchAssociationInfoV1{1,
        sizeof(RtPlaybackEnginePitchAssociationInfoV1), kind, 1,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch, 0, 0, wrapper, cls, receiver,
        reinterpret_cast<std::uint64_t>(receipt), ordinal, offset, id};
}
void RefreshEnginePitchAssociationLocked(Owner* owner, Scope* scope) {
    auto& association = scope->EnginePitchAssociation;
    if (!association.Kind) return;
    if (owner->Thread != GetCurrentThreadId()) {
        association.State = 3; FaultLocked(owner); return;
    }
    UnityBackingWatch probe{}; const UnityBackingWatch* watch = nullptr;
    const Initialization* initializer = nullptr;
    UnityBackingSample expected{};
    if (association.Kind == 1 || association.Kind == 2) {
        watch = FindUnityBackingWatchLocked(owner, reinterpret_cast<void*>(association.Receipt));
        if (!watch || watch->Value != owner || watch->Ordinal != association.Ordinal
            || watch->Source != association.Wrapper || watch->SourceClass != association.WrapperClass) {
            association.State = 3; FaultLocked(owner); return;
        }
        probe = *watch; expected = watch->Sample;
    } else if (association.Kind == 3) {
        for (auto* value = owner->Initializer; value; value = value->Previous)
            if (reinterpret_cast<std::uint64_t>(value) == association.Receipt) { initializer = value; break; }
        if (!initializer || !initializer->EnginePitchIdentity || initializer->Info.ordinal != association.Ordinal
            || initializer->Info.source != association.Wrapper || !EnginePitchInitializerGraphLocked(owner, initializer)) {
            association.State = 3; FaultLocked(owner); return;
        }
        probe.Source = initializer->Info.source; probe.SourceClass = association.WrapperClass;
        probe.Clip = initializer->Info.clip; probe.ClipClass = owner->UnityClipClass;
        expected = UnityBackingSample{initializer->EnginePitchSourceNative, initializer->EnginePitchClipNative,
            initializer->EnginePitchOffset, initializer->EnginePitchSourceId, initializer->EnginePitchClipId, true};
    } else { association.State = 3; FaultLocked(owner); return; }
    UnityBackingSample first{}, second{};
    if (!ReadUnityBackingSample(owner, &probe, &first) || !ReadUnityBackingSample(owner, &probe, &second)) {
        association.State = 3; FaultLocked(owner); return;
    }
    if (!EqualUnityBackingSample(first, second) || !EqualUnityBackingSample(first, expected)
        || first.SourceNative != association.NativeSelf) {
        // A readable replacement is a source boundary even when this call uses
        // the old native self. It must never be reclassified as foreign.
        probe.Sample = expected;
        InvalidateUnityBackingLocked(owner, UnityBackingChangeReason(&probe, first, second));
        association.State = owner->Faults ? 3u : 2u;
    } else association.State = owner->Faults || (owner->Flags & RtSourceUnknown) ? 3u : 1u;
}
bool EnginePitchRelevantLocked(Owner* owner, void* self, Scope* scope) {
    const auto receiver = reinterpret_cast<std::uint64_t>(self);
    if (!owner->EnginePitchEnabled || !receiver) return false;
    const UnityBackingWatch* selected = nullptr; std::uint32_t kind = 0;
    const auto* active = owner->BackingWatch; const auto* pending = owner->PendingBackingWatch;
    if (active && (active->Phase == 2 || active->Phase == 3) && active->Value == owner
        && EnginePitchReceiverMatches(receiver, active->Source, active->Sample.SourceNative)) { selected = active; kind = 1; }
    if (pending && (pending->Phase == 1 || pending->Phase == 3) && pending->Value == owner
        && EnginePitchReceiverMatches(receiver, pending->Source, pending->Sample.SourceNative)) {
        if (selected && selected->Source != pending->Source) {
            EnginePitchAssociation(owner, scope, 2, pending->Source, pending->SourceClass, receiver,
                const_cast<UnityBackingWatch*>(pending), pending->Ordinal, pending->Sample.Offset, pending->Sample.SourceId);
            scope->EnginePitchAssociation.State = 3; FaultLocked(owner); return true;
        }
        selected = pending; kind = 2;
    }
    if (selected) {
        EnginePitchAssociation(owner, scope, kind, selected->Source, selected->SourceClass, receiver,
            const_cast<UnityBackingWatch*>(selected), selected->Ordinal, selected->Sample.Offset, selected->Sample.SourceId);
        RefreshEnginePitchAssociationLocked(owner, scope); return true;
    }
    // The latest actual completed initializer is still retained across phase6,
    // its phase5 invalidation, and fresh retry. It is never a bare pointer cache.
    auto* value = owner->Initializer;
    if (!owner->FreshBlocked || !value || !value->EnginePitchIdentity
        || value->Info.phase < 4 || value->Info.phase >= 7
        || !EnginePitchReceiverMatches(receiver, value->Info.source, value->EnginePitchSourceNative)) return false;
    EnginePitchAssociation(owner, scope, 3, value->Info.source, reinterpret_cast<std::uint64_t>(owner->WatchAudioClass),
        receiver, value, value->Info.ordinal, value->EnginePitchOffset, value->EnginePitchSourceId);
    RefreshEnginePitchAssociationLocked(owner, scope); return true;
}
// Only the original predicate evaluates these operands. Diagnostic projection
// later copies these masks; it never repeats a getter or comparison.
bool RecordEnginePitchDiagnostic(std::uint32_t& tested, std::uint32_t& matched,
    std::uint32_t bit, bool value) {
    const auto flag = std::uint32_t{1} << bit;
    tested |= flag;
    if (value) matched |= flag;
    return value;
}
bool BeginEnginePitchLowerOriginal(Scope* scope) {
    auto* owner = scope->OwnerValue;
    const auto test = [scope](std::uint32_t bit, bool value) {
        return RecordEnginePitchDiagnostic(scope->EnginePitchBeginTested, scope->EnginePitchBeginMatched, bit, value);
    };
    if (!test(0, scope->Frame.Role == RtSourceLowerSetPitch) || !test(1, owner->EnginePitchEnabled)) return false;
    AcquireSRWLockExclusive(&owner->Lock);
    Scope association{};
    // Resolve the wrapper only through the private graph. No caller-supplied
    // native pointer or bool is accepted as delegation evidence.
    std::uint64_t native = 0;
    __try { native = *reinterpret_cast<const std::uint64_t*>(scope->Frame.Self + 0x10); }
    __except (EXCEPTION_EXECUTE_HANDLER) { FaultLocked(owner); }
    const bool relevant = test(2, native != 0)
        && test(3, EnginePitchRelevantLocked(owner, reinterpret_cast<void*>(native), &association));
    const bool begin = test(4, relevant)
        && test(5, association.EnginePitchAssociation.State == 1)
        && test(6, association.EnginePitchAssociation.Wrapper == scope->Frame.Self)
        && test(7, owner->Flags == RtSourceArmed)
        && test(8, !owner->Faults)
        && test(9, owner->Thread == GetCurrentThreadId())
        && test(10, currentScope == scope)
        && test(11, scope->Frame.Stage == RtSourceBefore)
        && test(12, scope->Frame.ThreadId == owner->Thread)
        && test(13, scope->Frame.Sequence != 0)
        && test(14, scope->Frame.Token != 0)
        && test(15, !scope->CallbackActive)
        && test(16, !scope->SuppressObserver)
        && test(17, !scope->EnginePitchWindow)
        && test(18, !scope->EnginePitchConsumed)
        && test(19, scope->SlotValue == &slots[owner->EnginePitchLowerSlot])
        && test(20, EnginePitchBindingMatchesLocked(owner));
    if (begin) {
        scope->EnginePitchWindow = true; scope->EnginePitchNativeSelf = native;
        scope->EnginePitchWindowMode = 1;
        scope->EnginePitchWindowWriter = owner->Writer; scope->EnginePitchWindowSource = owner->Source;
        scope->EnginePitchWindowScheduling = owner->Scheduling;
    }
    ReleaseSRWLockExclusive(&owner->Lock); return begin;
}
bool EnginePitchChildLocked(Owner* owner, Slot* slot, void* self, void* caller, std::uint64_t arg, Scope* child) {
    const auto* parent = currentScope;
    std::uint32_t identityTested = 0, identityMatched = 0;
    if (!RecordEnginePitchDiagnostic(identityTested, identityMatched, 0, slot->Role == RtSourceEngineSetPitch)
        || !RecordEnginePitchDiagnostic(identityTested, identityMatched, 1, slot->Kind == RtSourceIcallSingle1)) return false;
    // Other roles/kinds never populate a child diagnostic on their Scope.
    child->EnginePitchChildTested = identityTested; child->EnginePitchChildMatched = identityMatched;
    const auto test = [child](std::uint32_t bit, bool value) {
        return RecordEnginePitchDiagnostic(child->EnginePitchChildTested, child->EnginePitchChildMatched, bit, value);
    };
    const bool lowerChild = test(2, EnginePitchBindingMatchesLocked(owner))
        && test(3, slot == &slots[owner->EnginePitchEngineSlot])
        && test(4, parent != nullptr)
        && test(5, parent->OwnerValue == owner)
        && test(6, parent->SlotValue == &slots[owner->EnginePitchLowerSlot])
        && test(7, parent->Frame.Kind == RtSourceIcallSingle1)
        && test(8, parent->Frame.Role == RtSourceLowerSetPitch)
        && test(9, parent->Frame.OwnerEpoch == owner->Epoch)
        && test(10, parent->Frame.Stage == RtSourceBefore)
        && test(11, parent->Frame.Sequence != 0)
        && test(12, parent->Frame.Token != 0)
        && test(13, parent->EnginePitchWindow)
        && test(14, !parent->EnginePitchConsumed)
        && test(15, !parent->EnginePitchChildSequence)
        && test(16, !parent->CallbackActive)
        && test(17, !parent->SuppressObserver)
        && test(18, owner->Thread == GetCurrentThreadId())
        && test(19, parent->Frame.ThreadId == GetCurrentThreadId())
        && test(20, caller == owner->EnginePitchCaller)
        && test(21, parent->Frame.ArgBits == arg)
        && test(22, arg <= UINT32_MAX)
        && test(23, parent->EnginePitchNativeSelf == reinterpret_cast<std::uint64_t>(self))
        && test(24, child->EnginePitchAssociation.State == 1)
        && test(25, child->EnginePitchAssociation.Wrapper == parent->Frame.Self)
        && test(26, owner->Writer == parent->EnginePitchWindowWriter)
        && test(27, owner->Source == parent->EnginePitchWindowSource)
        && test(28, owner->Scheduling == parent->EnginePitchWindowScheduling);
    return lowerChild || ScheduledEnginePitchChildLocked(owner, slot, self, caller, arg, child);
}
void EndEnginePitchLowerOriginal(Scope* scope, bool abnormal) {
    auto* owner = scope->OwnerValue; AcquireSRWLockExclusive(&owner->Lock);
    scope->EnginePitchWindow = false;
    scope->EnginePitchWindowMode = 0;
    // Nonfinite lower calls may return without any engine child. An unused
    // window expires here; consumed children still owe their exact physical exit.
    if (scope->EnginePitchConsumed && (!scope->EnginePitchChildSequence || !scope->EnginePitchChildToken
        || !scope->EnginePitchChildExited
        || (!abnormal && (!scope->EnginePitchChildReturned || scope->EnginePitchChildAbnormal)))) FaultLocked(owner);
    if (!scope->EnginePitchConsumed && (scope->EnginePitchChildSequence || scope->EnginePitchChildToken
        || scope->EnginePitchChildExited)) FaultLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock);
}
void EnginePitchPhysicalExitLocked(Scope* scope) {
    if (!scope->EnginePitchChild) return;
    auto* parent = scope->EnginePitchParent;
    if (!parent || !parent->EnginePitchWindow || !parent->EnginePitchConsumed || parent->OwnerValue != scope->OwnerValue
        || parent->Frame.Sequence != scope->Frame.ParentSequence
        || parent->EnginePitchChildSequence != scope->Frame.Sequence || parent->EnginePitchChildToken != scope->Frame.Token
        || parent->EnginePitchChildExited) { FaultLocked(scope->OwnerValue); return; }
    parent->EnginePitchChildReturned = (scope->Frame.Flags & RtSourceReturnedFlag) != 0;
    parent->EnginePitchChildAbnormal = (scope->Frame.Flags & RtSourceAbnormalFlag) != 0;
    parent->EnginePitchChildExited = true;
}
}

std::uint32_t __cdecl RtPlaybackEnginePitchVersion() { return 1; }
int __cdecl RtPlaybackSourceConfigureEnginePitch(void* raw, std::uint32_t lowerSlot,
    std::uint32_t engineSlot, void* exactCallReturn) {
    auto* owner = FindOwner(raw);
    if (!owner || lowerSlot >= SlotCount || engineSlot >= SlotCount || lowerSlot == engineSlot || !exactCallReturn) return 0;
    AcquireSRWLockExclusive(&configureLock); AcquireSRWLockExclusive(&owner->Lock);
    bool valid = owner->Thread == GetCurrentThreadId() && !owner->WasArmed && !owner->EnginePitchEnabled
        && !owner->InFlight && owner->Flags == RtSourceDormant && !owner->Faults
        && LowerPitchBindingMatchesLocked(owner) && owner->LowerPitchLowerSlot == lowerSlot
        && engineSlot != owner->LowerPitchWrapperSlot;
    if (valid) {
        owner->EnginePitchLowerSlot = lowerSlot; owner->EnginePitchEngineSlot = engineSlot;
        owner->EnginePitchCaller = exactCallReturn; owner->EnginePitchEnabled = true;
        valid = EnginePitchBindingMatchesLocked(owner);
        if (!valid) { owner->EnginePitchEnabled = false; owner->EnginePitchCaller = nullptr; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); ReleaseSRWLockExclusive(&configureLock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadEnginePitchAssociation(void* raw, std::uint64_t sequence,
    std::uint64_t token, RtPlaybackEnginePitchAssociationInfoV1* info) {
    auto* owner = FindOwner(raw); auto* scope = currentScope;
    if (!owner || !scope || !info || info->Version != 1 || info->Size != sizeof(*info)
        || !scope->CallbackActive || scope->SuppressObserver || scope->OwnerValue != owner
        || scope->Frame.Role != RtSourceEngineSetPitch || scope->Frame.Kind != RtSourceIcallSingle1
        || scope->Frame.ThreadId != GetCurrentThreadId() || owner->Thread != GetCurrentThreadId()
        || !sequence || !token || scope->Frame.Sequence != sequence || scope->Frame.Token != token
        || scope->Frame.OwnerEpoch != owner->Epoch) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    RefreshEnginePitchAssociationLocked(owner, scope);
    const auto& association = scope->EnginePitchAssociation;
    const bool valid = association.Kind && association.Sequence == sequence && association.Token == token
        && association.OwnerAddress == reinterpret_cast<std::uint64_t>(owner) && association.OwnerEpoch == owner->Epoch
        && association.NativeSelf == scope->Frame.Self && association.Receipt && association.Ordinal;
    if (valid) *info = association;
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
