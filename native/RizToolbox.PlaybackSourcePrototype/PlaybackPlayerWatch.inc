// Orthogonal, private PlayerWatch V2. Included in the Source helper TU only.
namespace {
struct PlayerWatch {
    Owner* Value; PlayerWatch* Next; PlayerWatch* Expected; LogicalGate* Gate;
    std::uint64_t Ordinal, Player, Class, Cut, Writer, Source, Scheduling;
    std::uint64_t AudioSource, AudioClass, AudioAttempt;
    std::uint32_t Phase; // 1 staged, 2 active, 3 retired; records never reused
    Initialization* ResourceInitializer;
    PlayerWatch* ReplacedPending;
};
PlayerWatch* FindPlayerWatchLocked(Owner* owner, void* raw) {
    for (auto* value = owner->WatchRecords; value; value = value->Next) if (value == raw) return value;
    return nullptr;
}
bool PlayerClassMatches(std::uint64_t player, std::uint64_t cls) {
    __try { return player && cls && *reinterpret_cast<const std::uint64_t*>(player) == cls; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool PlayerAudioMatches(const PlayerWatch* watch) {
    __try {
        return PlayerClassMatches(watch->Player, watch->Class)
            && PlayerClassMatches(watch->AudioSource, watch->AudioClass)
            && *reinterpret_cast<const std::uint64_t*>(watch->Player + 0x10) == watch->AudioSource;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool StockStopRelevantLocked(Owner* owner, Slot* slot, void* self, void* method, void* caller) {
    auto* parent = currentScope;
    const auto* watch = owner->Watch;
    if (slot->Role != RtSourceAudioStop || slot->Kind != RtSourceVoid0 || method
        || owner->Thread != GetCurrentThreadId() || owner->Flags != RtSourceArmed || owner->Faults
        || owner->FreshBlocked || (owner->Logical && owner->Logical->Closed)
        || !parent || parent->OwnerValue != owner || parent->CallbackActive || parent->SuppressObserver
        || parent->StockStopSeen || parent->Frame.Stage != RtSourceBefore
        || parent->Frame.ParentSequence || parent->Frame.Depth != 1 || parent->Frame.ThreadId != owner->Thread
        || parent->Frame.Flags != RtSourceArmed || parent->Frame.Kind != RtSourceSingle1
        || (parent->Frame.Role != RtSourceReplay && parent->Frame.Role != RtSourceResume)
        || !watch || watch->Value != owner || watch->Phase != 2 || owner->PendingWatch
        || watch->Player != parent->Frame.Self || watch->AudioSource != reinterpret_cast<std::uint64_t>(self)
        || !PlayerAudioMatches(watch) || watch->AudioAttempt != owner->DirectAudioAttempt || watch->Source != owner->Source
        || owner->Writer != parent->Frame.WriterSequence || owner->Source != parent->Frame.SourceSequence
        || owner->Scheduling != parent->Frame.SchedulingSequence) return false;
    const auto index = parent->Frame.Role == RtSourceReplay ? 0u : 1u;
    return owner->StockStopCallers[index] && owner->StockStopMethods[index]
        && owner->StockStopCallers[index] == reinterpret_cast<std::uint64_t>(caller)
        && (!parent->Frame.IncomingMethod || parent->Frame.IncomingMethod == owner->StockStopMethods[index]);
}
bool PlayerWatchRelevantLocked(Owner* owner, void* self) {
    const auto receiver = reinterpret_cast<std::uint64_t>(self);
    const auto* watch = owner->Watch;
    if (!watch || watch->Phase != 2 || watch->Player != receiver) {
        // Stage already borrows the exact source owner's independently rooted
        // player. Attempts before source-open must invalidate that capture too.
        watch = owner->PendingWatch;
        if (!watch || watch->Phase != 1 || watch->Player != receiver) return false;
    }
    // Compare self before dereferencing it: arbitrary foreign receiver/value/MI
    // never need to be valid IL2CPP objects for transparent original forwarding.
    if (!PlayerClassMatches(watch->Player, watch->Class)) FaultLocked(owner);
    return true;
}
bool AudioWatchRelevantLocked(Owner* owner, void* self) {
    const auto receiver = reinterpret_cast<std::uint64_t>(self);
    const auto* watch = owner->Watch;
    if (!watch || watch->Phase != 2 || watch->AudioSource != receiver) {
        watch = owner->PendingWatch;
        if (!watch || watch->Phase != 1 || watch->AudioSource != receiver) return false;
    }
    // Exact rooted identity is checked before class dereference. A random
    // foreign address, even with the right class or MI, remains transparent.
    if (!PlayerClassMatches(watch->AudioSource, watch->AudioClass)) FaultLocked(owner);
    return true;
}
bool LegacyAudioRelevantLocked(Owner* owner, void* self) {
    if (AudioWatchRelevantLocked(owner, self)) return true;
    const auto* initialization = owner->Initializer;
    // Host-only initialization has no seven-root watch yet. Its actual native
    // parent records the source reached through the retained Progress object.
    // Keep that exact source's writes observable across AL1/AL0, including the
    // real SetClip child. InitializationEnter still verifies parent/MI/CALLsite
    // and the single child; this match issues neither a watch nor a completion.
    if (!owner->FreshBlocked || !initialization || initialization->Info.phase < 1
        || initialization->Info.phase >= 7 || !initialization->Info.source
        || initialization->Info.source != reinterpret_cast<std::uint64_t>(self)) return false;
    if (!PlayerClassMatches(initialization->Info.source, reinterpret_cast<std::uint64_t>(owner->WatchAudioClass)))
        FaultLocked(owner);
    return true;
}
bool PlayerWatchReadyLocked(Owner* owner) {
    const auto* watch = owner->PendingWatch;
    return watch && watch->Value == owner && watch->Phase == 1 && watch->Expected == owner->Watch
        && watch->Gate == owner->Logical && watch->Class == reinterpret_cast<std::uint64_t>(owner->WatchClass)
        && watch->AudioClass == reinterpret_cast<std::uint64_t>(owner->WatchAudioClass)
        && watch->AudioAttempt == owner->DirectAudioAttempt
        && (!watch->Gate || watch->Gate->Closed && watch->Gate->CutSequence == watch->Cut)
        && watch->Writer == owner->Writer && watch->Source == owner->Source && watch->Scheduling == owner->Scheduling
        && owner->InFlight == 0 && PlayerAudioMatches(watch) && ProgressWatchReadyLocked(owner, watch)
        && UnityBackingWatchReadyLocked(owner, watch);
}
bool PlayerWatchOpenedLocked(Owner* owner) {
    const auto* watch = owner->Watch;
    return watch && watch->Value == owner && watch->Phase == 2 && !owner->PendingWatch
        && watch->Class == reinterpret_cast<std::uint64_t>(owner->WatchClass)
        && watch->AudioClass == reinterpret_cast<std::uint64_t>(owner->WatchAudioClass)
        && watch->AudioAttempt == owner->DirectAudioAttempt
        // Pause/Play/Replay and rate writes may legitimately advance the other
        // fences after source-open. A constructor attempt advances Source at
        // Enter, before the actual reference store.
        && watch->Source == owner->Source
        && PlayerAudioMatches(watch) && ProgressWatchOpenedLocked(owner, watch) && UnityBackingWatchOpenedLocked(owner);
}
void CommitPlayerWatchLocked(Owner* owner) {
    auto* watch = owner->PendingWatch;
    CommitUnityBackingWatchLocked(owner, watch);
    CommitProgressWatchLocked(owner, watch);
    if (owner->Watch) owner->Watch->Phase = 3;
    watch->Phase = 2; owner->Watch = watch; owner->PendingWatch = nullptr;
}
}
std::uint32_t __cdecl RtPlaybackPlayerWatchVersion() { return 2; }
std::uint32_t __cdecl RtPlaybackStockStopVersion() { return 1; }
namespace {
bool DirectStopCallMatches(void* rawCaller, void* target) {
    __try {
        const auto* caller = static_cast<const unsigned char*>(rawCaller);
        return caller && target && caller[-5] == 0xe8
            && caller + *reinterpret_cast<const std::int32_t*>(caller - 4) == target;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
}
int __cdecl RtPlaybackSourceConfigureStockStop(void* raw, void* replayCaller, void* resumeCaller,
    void* replayMethod, void* resumeMethod) {
    auto* owner = FindOwner(raw);
    if (!owner || !replayCaller || !resumeCaller || replayCaller == resumeCaller
        || !replayMethod || !resumeMethod || replayMethod == resumeMethod) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    const Slot* replay = nullptr; const Slot* resume = nullptr; const Slot* stop = nullptr;
    unsigned replayCount = 0, resumeCount = 0, stopCount = 0;
    for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1) {
        if (slot.Role == RtSourceReplay) { replay = &slot; ++replayCount; }
        if (slot.Role == RtSourceResume) { resume = &slot; ++resumeCount; }
        if (slot.Role == RtSourceAudioStop) { stop = &slot; ++stopCount; }
    }
    const bool valid = owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && !owner->InFlight && !owner->Watch && !owner->PendingWatch
        && !owner->StockStopCallers[0] && !owner->StockStopCallers[1]
        && owner->WatchClass && owner->WatchAudioClass && replayCount == 1 && resumeCount == 1 && stopCount == 1
        && replay && resume && stop && replay->Kind == RtSourceSingle1 && resume->Kind == RtSourceSingle1
        && stop->Kind == RtSourceVoid0 && DirectStopCallMatches(replayCaller, stop->Target)
        && DirectStopCallMatches(resumeCaller, stop->Target);
    if (valid) {
        owner->StockStopCallers[0] = reinterpret_cast<std::uint64_t>(replayCaller);
        owner->StockStopCallers[1] = reinterpret_cast<std::uint64_t>(resumeCaller);
        owner->StockStopMethods[0] = reinterpret_cast<std::uint64_t>(replayMethod);
        owner->StockStopMethods[1] = reinterpret_cast<std::uint64_t>(resumeMethod);
    }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceConfigurePlayerWatch(void* raw, void* cls, void* audioClass) {
    auto* owner = FindOwner(raw); if (!owner || !cls || !audioClass) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    bool ctor = false;
    for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1 && slot.Role == RtSourcePlayerConstructor) ctor = true;
    const bool valid = ctor && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && !owner->InFlight && !owner->WatchClass && !owner->WatchAudioClass && !owner->Watch && !owner->PendingWatch;
    if (valid) { owner->WatchClass = cls; owner->WatchAudioClass = audioClass; }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceStagePlayerWatch(void* raw, void* player, void* audioSource, void** receipt) {
    auto* owner = FindOwner(raw); if (!owner || !player || !audioSource || !receipt) return 0;
    *receipt = nullptr; AcquireSRWLockExclusive(&owner->Lock);
    const bool initial = !owner->Logical && !owner->Watch && !owner->FreshBlocked;
    const bool transfer = owner->Logical && owner->Logical->Closed;
    const bool matchesInitialization = !transfer || !owner->Logical->Initializer
        || (owner->Logical->Initializer->Info.phase == 6
            && owner->Logical->Initializer->Info.player == reinterpret_cast<std::uint64_t>(player)
            && owner->Logical->Initializer->Info.source == reinterpret_cast<std::uint64_t>(audioSource)
            && ReadFinalGraph(owner->Logical->Initializer));
    PlayerWatch candidate{};
    candidate.Player = reinterpret_cast<std::uint64_t>(player); candidate.Class = reinterpret_cast<std::uint64_t>(owner->WatchClass);
    candidate.AudioSource = reinterpret_cast<std::uint64_t>(audioSource); candidate.AudioClass = reinterpret_cast<std::uint64_t>(owner->WatchAudioClass);
    auto* previousPending = owner->PendingWatch;
    const bool replacePending = previousPending && (ResourcePendingPlayerReplaceLocked(owner, previousPending)
        || UnityBackingPendingPlayerReplaceLocked(owner, previousPending)
        || LowerPitchPendingPlayerReplaceLocked(owner, previousPending)
        || AudioPendingPlayerReplaceLocked(owner, previousPending));
    const bool valid = owner->WatchClass && owner->WatchAudioClass && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && !owner->InFlight && (!previousPending || replacePending) && (initial || transfer)
        && matchesInitialization
        && owner->WatchOrdinal < JournalCount && PlayerAudioMatches(&candidate);
    if (valid) {
        auto* watch = new(std::nothrow) PlayerWatch{};
        if (watch) {
            watch->Value = owner; watch->Next = owner->WatchRecords; watch->Expected = owner->Watch;
            watch->Gate = owner->Logical; watch->Ordinal = ++owner->WatchOrdinal;
            watch->ResourceInitializer = owner->ResourceEnabled && owner->Logical ? owner->Logical->Initializer : nullptr;
            watch->ReplacedPending = replacePending ? previousPending : nullptr;
            watch->Player = reinterpret_cast<std::uint64_t>(player); watch->Class = reinterpret_cast<std::uint64_t>(owner->WatchClass);
            watch->AudioSource = candidate.AudioSource; watch->AudioClass = candidate.AudioClass; watch->AudioAttempt = owner->DirectAudioAttempt;
            watch->Cut = owner->Logical ? owner->Logical->CutSequence : 0;
            watch->Writer = owner->Writer; watch->Source = owner->Source; watch->Scheduling = owner->Scheduling; watch->Phase = 1;
            if (replacePending) previousPending->Phase = 3;
            owner->WatchRecords = owner->PendingWatch = watch; *receipt = watch;
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return *receipt ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadPlayerWatch(void* raw, void* receipt, RtPlaybackPlayerWatchInfoV2* info) {
    auto* owner = FindOwner(raw); if (!owner || !info || info->Version != 2 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockShared(&owner->Lock); const auto* watch = FindPlayerWatchLocked(owner, receipt);
    if (watch) *info = RtPlaybackPlayerWatchInfoV2{2, sizeof(*info), watch->Phase, owner->Watch == watch && watch->Phase == 2 ? 1u : 0u,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch, reinterpret_cast<std::uint64_t>(watch), watch->Ordinal,
        watch->Player, watch->Class, reinterpret_cast<std::uint64_t>(watch->Expected), watch->Cut, watch->Writer, watch->Source, watch->Scheduling,
        watch->AudioSource, watch->AudioClass};
    ReleaseSRWLockShared(&owner->Lock); return watch ? 1 : 0;
}
int __cdecl RtPlaybackSourceActivateInitialPlayerWatch(void* raw, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    const bool valid = owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed && !owner->Faults
        && !owner->Logical && !owner->Watch && !owner->FreshBlocked && owner->PendingWatch == receipt && PlayerWatchReadyLocked(owner);
    if (valid) CommitPlayerWatchLocked(owner);
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceRetirePlayerWatch(void* raw, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock); auto* watch = FindPlayerWatchLocked(owner, receipt);
    bool valid = watch && owner->Thread == GetCurrentThreadId();
    if (valid && watch->Phase != 3) {
        valid = !owner->InFlight && !(owner->Flags & RtSourceUnknown) && !owner->Faults;
        if (valid) {
            watch->Phase = 3;
            if (owner->PendingWatch == watch) owner->PendingWatch = nullptr;
            // Keep owner->Watch as its immutable inactive predecessor identity.
            // The next source-open can compare Expected without retaining or
            // dereferencing an already retired player's pointer.
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
