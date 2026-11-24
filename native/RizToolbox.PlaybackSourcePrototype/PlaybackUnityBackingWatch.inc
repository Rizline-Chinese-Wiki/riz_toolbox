// Orthogonal Unity backing identity V1. No IL2CPP call or callback is made here.
// Object+0x10 and Il2CppClass+0xb8 are pinned by the fixed GetInstanceID body.
namespace {
struct UnityBackingSample {
    std::uint64_t SourceNative, ClipNative;
    std::int32_t Offset, SourceId, ClipId;
    bool Stable;
};
struct UnityBackingWatch {
    Owner* Value; UnityBackingWatch* Next; PlayerWatch* Player;
    Initialization* Initializer;
    std::uint64_t Ordinal, Source, SourceClass, Clip, ClipClass, SourceFence;
    UnityBackingSample Sample;
    std::uint32_t Phase, Reason; // 1 staged, 2 active, 3 invalidated, 4 retired
};
bool ValidUnityInstanceIdOffset(std::int32_t offset) {
    return offset >= 0 && offset <= 0x400 && (offset & 3) == 0;
}
// A false return is an unreadable/invalid physical object, not a known lifetime
// transition. Zero and changed native identities remain readable observations.
bool ReadUnityBackingSample(const Owner* owner, const UnityBackingWatch* watch, UnityBackingSample* sample) {
    __try {
        if (!owner->UnityObjectClass || !watch->Source || !watch->Clip
            || *reinterpret_cast<const std::uint64_t*>(watch->Source) != watch->SourceClass
            || *reinterpret_cast<const std::uint64_t*>(watch->Clip) != watch->ClipClass) return false;
        const auto statics = *reinterpret_cast<const std::uint64_t*>(owner->UnityObjectClass + 0xb8);
        if (!statics) return false;
        const auto offset = *reinterpret_cast<const std::int32_t*>(statics);
        if (!ValidUnityInstanceIdOffset(offset) || offset != owner->UnityInstanceIdOffset) return false;
        sample->Offset = offset;
        sample->SourceNative = *reinterpret_cast<const std::uint64_t*>(watch->Source + 0x10);
        sample->ClipNative = *reinterpret_cast<const std::uint64_t*>(watch->Clip + 0x10);
        sample->SourceId = sample->SourceNative ? *reinterpret_cast<const std::int32_t*>(sample->SourceNative + offset) : 0;
        sample->ClipId = sample->ClipNative ? *reinterpret_cast<const std::int32_t*>(sample->ClipNative + offset) : 0;
        // A class or wrapper store during either native-ID read must reject
        // this observation too. No engine/metadata callback is used here.
        if (*reinterpret_cast<const std::uint64_t*>(watch->Source) != watch->SourceClass
            || *reinterpret_cast<const std::uint64_t*>(watch->Clip) != watch->ClipClass) return false;
        sample->Stable = *reinterpret_cast<const std::uint64_t*>(watch->Source + 0x10) == sample->SourceNative
            && *reinterpret_cast<const std::uint64_t*>(watch->Clip + 0x10) == sample->ClipNative
            && (!sample->SourceNative || *reinterpret_cast<const std::int32_t*>(sample->SourceNative + offset) == sample->SourceId)
            && (!sample->ClipNative || *reinterpret_cast<const std::int32_t*>(sample->ClipNative + offset) == sample->ClipId);
        return *reinterpret_cast<const std::uint64_t*>(owner->UnityObjectClass + 0xb8) == statics
            && *reinterpret_cast<const std::int32_t*>(statics) == offset;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool EqualUnityBackingSample(const UnityBackingSample& left, const UnityBackingSample& right) {
    return left.Stable && right.Stable && left.SourceNative == right.SourceNative && left.ClipNative == right.ClipNative
        && left.Offset == right.Offset && left.SourceId == right.SourceId && left.ClipId == right.ClipId;
}
std::uint32_t UnityBackingChangeReason(const UnityBackingWatch* watch,
    const UnityBackingSample& first, const UnityBackingSample& second) {
    if (!EqualUnityBackingSample(first, second)) return 5;
    return first.SourceNative != watch->Sample.SourceNative ? 1u
        : first.SourceId != watch->Sample.SourceId ? 2u
        : first.ClipNative != watch->Sample.ClipNative ? 3u : 4u;
}
void InvalidateUnityBackingLocked(Owner* owner, std::uint32_t reason) {
    auto* pending = owner->PendingBackingWatch;
    // A newly staged capture has its own one-time boundary even when a
    // predecessor already blocked ordinary freshness. Never advance repeatedly
    // for an already tombstoned watch.
    const bool pendingLive = pending && pending->Phase == 1;
    if (owner->BackingBlocked && !pendingLive) return;
    owner->BackingBlocked = true;
    // Independent source continuity: old writers/prepared/observations cannot
    // become current again when the same wrapper happens to return later.
    if (owner->Source == UINT64_MAX) { FaultLocked(owner); return; }
    ++owner->Source;
    if (owner->BackingWatch && owner->BackingWatch->Phase == 2) {
        owner->BackingWatch->Phase = 3; owner->BackingWatch->Reason = reason;
    }
    if (pendingLive) {
        pending->Phase = 3; pending->Reason = reason;
        auto* initializer = pending->Initializer;
        // Staging borrowed the exact phase-six initializer. Only its actual
        // native watch may mark this interruption, never a caller boolean or a
        // resource writer. The original/child journals and roots still drain.
        if (initializer && owner->Logical && owner->Logical->Closed
            && owner->Logical->Initializer == initializer && initializer->Info.phase == 6
            && !initializer->BackingInvalidated) {
            initializer->BackingInvalidated = true;
            initializer->BackingInvalidationWatch = pending;
            initializer->BackingInvalidationSourceFence = owner->Source;
            initializer->Info.phase = 5;
        }
    }
}
// Called with Owner.Lock exclusive. 1 is usable; 2 is a recognized source
// boundary; 3 is sticky physical Unknown. No writable caller pair grants this.
int CheckUnityBackingLocked(Owner* owner) {
    if (!owner->UnityObjectClass) return 1; // non-Unity owned fixture scope
    if (owner->Thread != GetCurrentThreadId()) { FaultLocked(owner); return 3; }
    if (owner->Faults || owner->Flags != RtSourceArmed) return 3;
    // A pending graph must still be watched while the previous graph is
    // blocked. Its death needs a new certified retry, not permanent NotReady.
    const auto* pending = owner->PendingBackingWatch;
    if (pending && pending->Phase == 1) {
        if (pending->Value != owner || pending->Player != owner->PendingWatch
            || !pending->Player || pending->Player->Phase != 1) return 0;
        UnityBackingSample first{}, second{};
        if (!ReadUnityBackingSample(owner, pending, &first) || !ReadUnityBackingSample(owner, pending, &second)) {
            FaultLocked(owner); return 3;
        }
        if (!EqualUnityBackingSample(first, second) || !EqualUnityBackingSample(first, pending->Sample)) {
            InvalidateUnityBackingLocked(owner, UnityBackingChangeReason(pending, first, second));
            return owner->Faults ? 3 : 2;
        }
    }
    if (owner->BackingBlocked) return 2;
    const auto* watch = owner->BackingWatch;
    if (!watch) return owner->Watch ? 0 : 1; // no active source yet
    if (watch->Value != owner || watch->Phase != 2 || watch->Player != owner->Watch) return 0;
    UnityBackingSample first{}, second{};
    if (!ReadUnityBackingSample(owner, watch, &first) || !ReadUnityBackingSample(owner, watch, &second)) {
        FaultLocked(owner); return 3;
    }
    if (!EqualUnityBackingSample(first, second) || !EqualUnityBackingSample(first, watch->Sample)) {
        InvalidateUnityBackingLocked(owner, UnityBackingChangeReason(watch, first, second));
        return owner->Faults ? 3 : 2;
    }
    return 1;
}
bool UnityBackingWatchReadyLocked(Owner* owner, const PlayerWatch* player) {
    if (!owner->UnityObjectClass) return true;
    const auto* watch = owner->PendingBackingWatch;
    if (!(watch && watch->Value == owner && watch->Player == player && watch->Phase == 1
        && watch->Source == player->AudioSource && watch->SourceClass == player->AudioClass
        && watch->SourceFence == owner->Source)) return false;
    UnityBackingSample first{}, second{};
    if (!ReadUnityBackingSample(owner, watch, &first) || !ReadUnityBackingSample(owner, watch, &second)) {
        FaultLocked(owner); return false;
    }
    if (!EqualUnityBackingSample(first, second) || !EqualUnityBackingSample(first, watch->Sample)) {
        InvalidateUnityBackingLocked(owner, UnityBackingChangeReason(watch, first, second)); return false;
    }
    return true;
}
bool UnityBackingWatchOpenedLocked(Owner* owner) {
    if (!owner->UnityObjectClass) return true;
    return !owner->BackingBlocked && CheckUnityBackingLocked(owner) == 1;
}
void CommitUnityBackingWatchLocked(Owner* owner, PlayerWatch* player) {
    if (!owner->UnityObjectClass) return;
    auto* watch = owner->PendingBackingWatch;
    if (!watch || watch->Player != player || watch->Phase != 1) { FaultLocked(owner); return; }
    if (owner->BackingWatch) owner->BackingWatch->Phase = 4;
    watch->Phase = 2; owner->BackingWatch = watch; owner->PendingBackingWatch = nullptr;
    owner->BackingBlocked = false;
}
bool UnityBackingPendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending) {
    const auto* gate = owner->Logical;
    auto* fresh = owner->Initializer;
    const auto* dead = owner->PendingBackingWatch;
    const auto* prior = dead ? dead->Initializer : nullptr;
    return owner->UnityObjectClass && owner->BackingBlocked && pending && pending == owner->PendingWatch
        && pending->Value == owner && pending->Phase == 1 && pending->Gate == gate
        && dead && dead->Value == owner && dead->Player == pending && dead->Phase == 3
        && prior && prior->BackingInvalidated && prior->Info.phase == 5
        && prior->BackingInvalidationWatch == dead && !prior->PhysicalInFlight
        && gate && gate->Closed && fresh && gate->Initializer == fresh && fresh != prior
        && fresh->Info.phase == 6 && fresh->Info.ordinal > prior->Info.ordinal
        && pending->Source != owner->Source && ReadFinalGraph(fresh)
        && InitializationResourceReadyLocked(owner, fresh);
}
bool UnityBackingPendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress) {
    // ProgressWatch is defined later in the TU. Its exact predecessor/graph is
    // checked by that module; this helper supplies only the independent backing
    // certification for the already replaced PlayerWatch.
    (void)pending;
    const auto* replaced = player ? player->ReplacedPending : nullptr;
    const auto* dead = owner->PendingBackingWatch;
    const auto* prior = dead ? dead->Initializer : nullptr;
    const auto* fresh = owner->Initializer;
    return owner->UnityObjectClass && owner->BackingBlocked && player && player == owner->PendingWatch
        && player->Value == owner && player->Phase == 1 && replaced && replaced->Phase == 3
        && dead && dead->Value == owner && dead->Phase == 3 && dead->Player == replaced
        && prior && prior->BackingInvalidated && prior->Info.phase == 5
        && prior->BackingInvalidationWatch == dead && !prior->PhysicalInFlight
        && fresh && fresh != prior && owner->Logical && owner->Logical->Closed
        && owner->Logical->Initializer == fresh && fresh->Info.phase == 6
        && fresh->Info.ordinal > prior->Info.ordinal && fresh->Info.progress == progress
        && InitializationResourceReadyLocked(owner, fresh);
}
UnityBackingWatch* FindUnityBackingWatchLocked(Owner* owner, void* receipt) {
    for (auto* watch = owner->BackingRecords; watch; watch = watch->Next) if (watch == receipt) return watch;
    return nullptr;
}
}
std::uint32_t __cdecl RtPlaybackUnityBackingWatchVersion() { return 1; }
int __cdecl RtPlaybackSourceConfigureUnityBackingWatch(void* raw, void* objectClass, void* clipClass, std::int32_t instanceIdOffset) {
    auto* owner = FindOwner(raw);
    if (!owner || !objectClass || !clipClass || !ValidUnityInstanceIdOffset(instanceIdOffset)) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    bool valid = owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed && !owner->Faults
        && !owner->InFlight && owner->WatchClass && owner->WatchAudioClass && !owner->Watch && !owner->PendingWatch
        && !owner->UnityObjectClass && !owner->BackingRecords;
    __try {
        const auto statics = *reinterpret_cast<const std::uint64_t*>(reinterpret_cast<std::uint64_t>(objectClass) + 0xb8);
        valid = valid && statics && *reinterpret_cast<const std::int32_t*>(statics) == instanceIdOffset;
    } __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (valid) { owner->UnityObjectClass = reinterpret_cast<std::uint64_t>(objectClass);
        owner->UnityClipClass = reinterpret_cast<std::uint64_t>(clipClass); owner->UnityInstanceIdOffset = instanceIdOffset; }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceStageUnityBackingWatch(void* raw, void* playerReceipt, void* independentlyRootedClip, void** receipt) {
    auto* owner = FindOwner(raw); if (!owner || !playerReceipt || !independentlyRootedClip || !receipt) return 0;
    *receipt = nullptr; AcquireSRWLockExclusive(&owner->Lock);
    auto* player = owner->PendingWatch;
    const auto* initializer = owner->Logical ? owner->Logical->Initializer : nullptr;
    const bool actualRecovery = initializer && initializer->Info.phase == 6
        && initializer->Info.clip == reinterpret_cast<std::uint64_t>(independentlyRootedClip);
    const bool boundary = !owner->Logical && !owner->Watch && !owner->FreshBlocked
        || owner->Logical && owner->Logical->Closed && (!owner->BackingBlocked || actualRecovery);
    UnityBackingWatch candidate{};
    candidate.Source = player ? player->AudioSource : 0;
    candidate.SourceClass = reinterpret_cast<std::uint64_t>(owner->WatchAudioClass);
    candidate.Clip = reinterpret_cast<std::uint64_t>(independentlyRootedClip); candidate.ClipClass = owner->UnityClipClass;
    UnityBackingSample first{}, second{};
    const bool lowerReplace = LowerPitchPendingBackingReplaceLocked(owner, owner->PendingBackingWatch, player)
        || AudioPendingBackingReplaceLocked(owner, owner->PendingBackingWatch, player);
    const bool valid = owner->UnityObjectClass && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && !owner->InFlight && boundary && player && player == playerReceipt && player->Phase == 1
        && player->Value == owner && player->Source == owner->Source && owner->BackingOrdinal < JournalCount
        && (!owner->PendingBackingWatch || owner->PendingBackingWatch->Phase >= 3 || lowerReplace)
        && ReadUnityBackingSample(owner, &candidate, &first) && ReadUnityBackingSample(owner, &candidate, &second)
        && EqualUnityBackingSample(first, second) && first.SourceNative && first.ClipNative && first.SourceId && first.ClipId;
    if (valid) {
        auto* watch = new(std::nothrow) UnityBackingWatch{};
        if (watch) { if (lowerReplace) owner->PendingBackingWatch->Phase = 4;
            *watch = candidate; watch->Value = owner; watch->Next = owner->BackingRecords; watch->Player = player;
            watch->Initializer = initializer && initializer->Info.phase == 6 ? owner->Logical->Initializer : nullptr;
            watch->Ordinal = ++owner->BackingOrdinal; watch->SourceFence = owner->Source; watch->Sample = first; watch->Phase = 1;
            owner->BackingRecords = owner->PendingBackingWatch = watch; *receipt = watch; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return *receipt ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadUnityBackingWatch(void* raw, void* receipt, RtPlaybackUnityBackingInfoV1* info) {
    auto* owner = FindOwner(raw); if (!owner || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockShared(&owner->Lock);
    const auto* watch = FindUnityBackingWatchLocked(owner, receipt);
    if (watch) *info = RtPlaybackUnityBackingInfoV1{1, sizeof(*info), watch->Phase,
        owner->BackingWatch == watch && watch->Phase == 2 && !owner->BackingBlocked ? 1u : 0u,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch, reinterpret_cast<std::uint64_t>(watch), watch->Ordinal,
        reinterpret_cast<std::uint64_t>(watch->Player), watch->Source, watch->SourceClass, watch->Sample.SourceNative,
        watch->Clip, watch->ClipClass, watch->Sample.ClipNative, watch->SourceFence,
        watch->Sample.Offset, watch->Sample.SourceId, watch->Sample.ClipId, watch->Reason};
    ReleaseSRWLockShared(&owner->Lock); return watch ? 1 : 0;
}
int __cdecl RtPlaybackSourceRetireUnityBackingWatch(void* raw, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock); auto* watch = FindUnityBackingWatchLocked(owner, receipt);
    const bool valid = watch && owner->Thread == GetCurrentThreadId() && !owner->Faults
        && !(owner->Flags & RtSourceUnknown) && !owner->InFlight;
    if (valid) { watch->Phase = 4; if (owner->PendingBackingWatch == watch) owner->PendingBackingWatch = nullptr; }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadBackingInitializationInvalidation(void* raw, void* enumerator,
    std::uint64_t yieldSequence, RtPlaybackBackingInitializationInfoV1* info) {
    auto* owner = FindOwner(raw);
    if (!owner || !enumerator || !yieldSequence || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    if (owner->Thread != GetCurrentThreadId()) { ReleaseSRWLockExclusive(&owner->Lock); return 0; }
    // This polling query can discover a pending loss before another physical
    // source entry. Known invalid records stay readable for retained cleanup.
    if (owner->Flags == RtSourceArmed && !owner->Faults) CheckUnityBackingLocked(owner);
    const Initialization* found = nullptr;
    for (auto* value = owner->Initializer; value; value = value->Previous)
        if (value->Info.enumerator == reinterpret_cast<std::uint64_t>(enumerator)
            && value->Info.yieldSequence == yieldSequence) { found = value; break; }
    if (found) {
        const auto* badge = found->BackingInvalidated ? found->BackingInvalidationWatch : nullptr;
        *info = RtPlaybackBackingInitializationInfoV1{1, sizeof(*info), found->BackingInvalidated ? 1u : 0u,
            badge ? badge->Reason : 0u, reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
            reinterpret_cast<std::uint64_t>(found), found->Info.ordinal, found->Info.enumerator,
            found->Info.progress, found->Info.yieldSequence, reinterpret_cast<std::uint64_t>(badge),
            badge ? badge->Ordinal : 0u, found->BackingInvalidated ? found->BackingInvalidationSourceFence : 0u};
    }
    ReleaseSRWLockExclusive(&owner->Lock); return found ? 1 : 0;
}
std::uint32_t __cdecl RtPlaybackSourceRetryBackingInitialization(void* raw, void* prior, void* fresh,
    std::uint32_t action, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical && owner.Logical == raw) { gate = owner.Logical; break; }
    if (!gate || !prior || !fresh || !request || (action != 12 && action != 13)) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    const auto* predecessor = gate->Initializer;
    auto* candidate = owner->Initializer;
    const auto* badge = predecessor ? predecessor->BackingInvalidationWatch : nullptr;
    std::uint32_t result = 0;
    if (!owner->UnityObjectClass || owner->Flags != RtSourceArmed || owner->Faults
        || owner->Thread != GetCurrentThreadId()) result = 3;
    else if (!gate->Closed || !owner->FreshBlocked || !owner->BackingBlocked
        || !predecessor || predecessor != prior || predecessor == candidate
        || !predecessor->BackingInvalidated || predecessor->Info.phase != 5
        || !badge || FindUnityBackingWatchLocked(owner, const_cast<UnityBackingWatch*>(badge)) != badge
        || badge->Value != owner || (badge->Phase != 3 && badge->Phase != 4) || badge->Initializer != predecessor
        || badge->Reason < 1 || badge->Reason > 5
        || !predecessor->BackingInvalidationSourceFence
        || predecessor->BackingInvalidationSourceFence > owner->Source
        || !candidate || candidate != fresh || candidate->Info.phase != 4
        || candidate->Info.ordinal <= predecessor->Info.ordinal
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
