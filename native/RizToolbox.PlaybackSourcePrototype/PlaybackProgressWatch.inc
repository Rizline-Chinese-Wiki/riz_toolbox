// Private ProgressWatch V1. Included after PlayerWatch in the same source TU.
namespace {
struct ProgressWatch {
    Owner* Value; ProgressWatch* Next; ProgressWatch* Expected; PlayerWatch* Player;
    std::uint64_t Ordinal, Progress, Class;
    std::uint32_t Phase; // 1 staged, 2 active, 3 retired; records never reused.
    ProgressResourceState* Resource;
};
ProgressWatch* FindProgressWatchLocked(Owner* owner, void* receipt) {
    for (auto* value = owner->ProgressWatchRecords; value; value = value->Next)
        if (value == receipt) return value;
    return nullptr;
}
ProgressWatch* RelevantProgressWatchLocked(Owner* owner, std::uint64_t progress) {
    auto* value = owner->ProgressWatchValue;
    if (!value || value->Phase != 2 || value->Progress != progress) {
        value = owner->PendingProgressWatch;
        if (!value || value->Phase != 1 || value->Progress != progress) return nullptr;
    }
    return value;
}
bool ProgressGraphMatches(const ProgressWatch* watch) {
    __try {
        return watch && PlayerClassMatches(watch->Progress, watch->Class) && watch->Player
            && *reinterpret_cast<const std::uint64_t*>(watch->Progress + 0x38) == watch->Player->Player
            && *reinterpret_cast<const std::uint64_t*>(watch->Progress + 0x28) == watch->Player->AudioSource;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool ProgressRelevantLocked(Owner* owner, void* self) {
    const auto* watch = RelevantProgressWatchLocked(owner, reinterpret_cast<std::uint64_t>(self));
    if (!watch) return false;
    if (!PlayerClassMatches(watch->Progress, watch->Class)) FaultLocked(owner);
    return true;
}
bool ReadStartProgress(void* self, std::uint64_t* progress) {
    __try {
        if (!self) return false;
        *progress = *reinterpret_cast<const std::uint64_t*>(static_cast<unsigned char*>(self) + 0x20);
        return *progress != 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool StartProgressRelevantLocked(Owner* owner, void* self) {
    std::uint64_t progress{};
    if (!ReadStartProgress(self, &progress)) return false;
    const auto* watch = RelevantProgressWatchLocked(owner, progress);
    if (!watch) return false;
    // The actual body reaches a retained current Progress. A wrong enum class
    // cannot turn that physical attempt into a transparent foreign operation.
    if (!PlayerClassMatches(reinterpret_cast<std::uint64_t>(self), reinterpret_cast<std::uint64_t>(owner->WatchStartClass))
        || !PlayerClassMatches(watch->Progress, watch->Class)) FaultLocked(owner);
    return true;
}
bool ProgressWatchReadyLocked(Owner* owner, const PlayerWatch* player) {
    if (!owner->WatchProgressClass) return true; // explicitly unconfigured owned fixture scope.
    const auto* value = owner->PendingProgressWatch;
    return value && value->Value == owner && value->Phase == 1 && value->Expected == owner->ProgressWatchValue
        && value->Player == player && value->Class == reinterpret_cast<std::uint64_t>(owner->WatchProgressClass)
        && ProgressGraphMatches(value);
}
bool ProgressWatchOpenedLocked(Owner* owner, const PlayerWatch* player) {
    if (!owner->WatchProgressClass) return true;
    const auto* value = owner->ProgressWatchValue;
    return value && value->Value == owner && value->Phase == 2 && !owner->PendingProgressWatch
        && value->Player == player && value->Class == reinterpret_cast<std::uint64_t>(owner->WatchProgressClass)
        && ProgressGraphMatches(value);
}
void CommitProgressWatchLocked(Owner* owner, PlayerWatch* player) {
    if (!owner->WatchProgressClass) return;
    auto* watch = owner->PendingProgressWatch;
    // The caller holds the same lock and has already checked PlayerWatchReady.
    if (!watch || watch->Player != player) { FaultLocked(owner); return; }
    if (owner->ProgressWatchValue) owner->ProgressWatchValue->Phase = 3;
    watch->Phase = 2; owner->ProgressWatchValue = watch; owner->PendingProgressWatch = nullptr;
}
ProgressWatch* CreateProgressWatchLocked(Owner* owner, std::uint64_t progress, PlayerWatch* player, std::uint32_t phase) {
    if (owner->ProgressWatchOrdinal >= JournalCount) return nullptr;
    auto* watch = new(std::nothrow) ProgressWatch{};
    if (!watch) return nullptr;
    watch->Value = owner; watch->Next = owner->ProgressWatchRecords; watch->Expected = owner->ProgressWatchValue;
    watch->Player = player; watch->Ordinal = ++owner->ProgressWatchOrdinal;
    watch->Progress = progress; watch->Class = reinterpret_cast<std::uint64_t>(owner->WatchProgressClass); watch->Phase = phase;
    if (!AttachProgressResourceLocked(owner, watch)) { delete watch; return nullptr; }
    owner->ProgressWatchRecords = watch;
    return watch;
}
}
std::uint32_t __cdecl RtPlaybackProgressWatchVersion() { return 1; }
int __cdecl RtPlaybackSourceConfigureProgressWatch(void* raw, void* progressClass, void* startClass) {
    auto* owner = FindOwner(raw); if (!owner || !progressClass || !startClass) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    bool play = false, setter = false, start = false;
    for (const auto& slot : slots) if (slot.OwnerValue == owner && slot.Configured == 1) {
        play |= slot.Role == RtSourceProgressPlay; setter |= slot.Role == RtSourceProgressMusicPlayer; start |= slot.Role == RtSourceProgressStartMoveNext;
    }
    const bool valid = play && setter && start && owner->WatchClass && owner->WatchAudioClass
        && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed && !owner->Faults && !owner->InFlight
        && !owner->WatchProgressClass && !owner->WatchStartClass && !owner->Watch && !owner->PendingWatch;
    if (valid) { owner->WatchProgressClass = progressClass; owner->WatchStartClass = startClass; }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceBindHostProgressWatch(void* raw, void* progress, void** receipt) {
    auto* owner = FindOwner(raw); if (!owner || !progress || !receipt) return 0;
    *receipt = nullptr; AcquireSRWLockExclusive(&owner->Lock);
    const bool valid = owner->WatchProgressClass && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && !owner->InFlight && !owner->Logical && !owner->Watch && !owner->PendingWatch && !owner->FreshBlocked
        && !owner->ProgressWatchValue && !owner->PendingProgressWatch
        && PlayerClassMatches(reinterpret_cast<std::uint64_t>(progress), reinterpret_cast<std::uint64_t>(owner->WatchProgressClass));
    if (valid) {
        auto* watch = CreateProgressWatchLocked(owner, reinterpret_cast<std::uint64_t>(progress), nullptr, 2);
        if (watch) { owner->ProgressWatchValue = watch; *receipt = watch; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return *receipt ? 1 : 0;
}
int __cdecl RtPlaybackSourceStageProgressWatch(void* raw, void* progress, void* playerReceipt, void** receipt) {
    auto* owner = FindOwner(raw); if (!owner || !progress || !playerReceipt || !receipt) return 0;
    *receipt = nullptr; AcquireSRWLockExclusive(&owner->Lock);
    auto* player = FindPlayerWatchLocked(owner, playerReceipt);
    ProgressWatch candidate{}; candidate.Player = player; candidate.Progress = reinterpret_cast<std::uint64_t>(progress);
    candidate.Class = reinterpret_cast<std::uint64_t>(owner->WatchProgressClass);
    auto* previousPending = owner->PendingProgressWatch;
    const bool backingReplace = previousPending && player && player->ReplacedPending
        && previousPending->Value == owner && previousPending->Phase == 1
        && previousPending->Player == player->ReplacedPending
        && previousPending->Progress == candidate.Progress && previousPending->Class == candidate.Class
        && UnityBackingPendingProgressReplaceLocked(owner, previousPending, player, candidate.Progress);
    const bool replacePending = previousPending
        && (ResourcePendingProgressReplaceLocked(owner, previousPending, player, candidate.Progress) || backingReplace
            || LowerPitchPendingProgressReplaceLocked(owner, previousPending, player, candidate.Progress)
            || AudioPendingProgressReplaceLocked(owner, previousPending, player, candidate.Progress));
    const bool valid = owner->WatchProgressClass && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && !owner->InFlight && player && player == owner->PendingWatch && player->Phase == 1
        && (!previousPending || replacePending) && ProgressGraphMatches(&candidate);
    if (valid) {
        auto* watch = CreateProgressWatchLocked(owner, candidate.Progress, player, 1);
        if (watch) {
            if (replacePending) previousPending->Phase = 3;
            owner->PendingProgressWatch = watch; *receipt = watch;
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return *receipt ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadProgressWatch(void* raw, void* receipt, RtPlaybackProgressWatchInfoV1* info) {
    auto* owner = FindOwner(raw); if (!owner || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockShared(&owner->Lock); const auto* watch = FindProgressWatchLocked(owner, receipt);
    if (watch) *info = RtPlaybackProgressWatchInfoV1{1, sizeof(*info), watch->Phase,
        owner->ProgressWatchValue == watch && watch->Phase == 2 ? 1u : 0u,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch, reinterpret_cast<std::uint64_t>(watch), watch->Ordinal,
        watch->Progress, watch->Class, reinterpret_cast<std::uint64_t>(watch->Expected), reinterpret_cast<std::uint64_t>(watch->Player),
        watch->Player ? watch->Player->Player : 0, watch->Player ? watch->Player->AudioSource : 0};
    ReleaseSRWLockShared(&owner->Lock); return watch ? 1 : 0;
}
int __cdecl RtPlaybackSourceRetireProgressWatch(void* raw, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock); auto* watch = FindProgressWatchLocked(owner, receipt);
    bool valid = watch && owner->Thread == GetCurrentThreadId();
    if (valid && watch->Phase != 3) {
        valid = !owner->InFlight && !(owner->Flags & RtSourceUnknown) && !owner->Faults;
        if (valid) {
            watch->Phase = 3;
            if (owner->PendingProgressWatch == watch) owner->PendingProgressWatch = nullptr;
            RetireProgressResourceWatchLocked(owner, watch);
            // The active pointer remains an immutable predecessor. A late
            // retirement never clears a newer owner's progress record.
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}

// Private Progress resource V1, included in the Source helper TU after watches.
// Owner.Lock covers classification, revision, initializer binding and source-
// open consumption. Reference pointer bits are never dereferenced here: only
// an actual retained Progress watch lends access to its +0x30 reference field.
namespace {
struct ProgressResourceState {
    Owner* Value;
    std::uint64_t Identity, Progress, Class, Revision;
};
struct ProgressResourceTicket {
    Owner* Value;
    ProgressResourceTicket* Next;
    ProgressResourceState* Resource;
    ProgressWatch* Watch;
    Initialization* Initializer;
    std::uint64_t Ordinal, Revision, Reference;
    std::uint32_t Phase;
};
bool ReadProgressReference(const ProgressWatch* watch, std::uint64_t* reference) {
    __try {
        if (!watch || !PlayerClassMatches(watch->Progress, watch->Class)) return false;
        *reference = *reinterpret_cast<const std::uint64_t*>(watch->Progress + 0x30);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool AttachProgressResourceLocked(Owner* owner, ProgressWatch* watch) {
    if (!owner->ResourceEnabled) return true; // explicit historical owned profile.
    const auto* retained = RelevantProgressWatchLocked(owner, watch->Progress);
    if (retained && retained->Resource && retained->Class == watch->Class) {
        // A host->source or active->pending handoff of the SAME retained root
        // shares the state, even though the private watch receipt changes.
        watch->Resource = retained->Resource;
        return true;
    }
    if (owner->ResourceStateOrdinal == UINT64_MAX) { FaultLocked(owner); return false; }
    auto* state = new(std::nothrow) ProgressResourceState{};
    if (!state) return false;
    state->Value = owner; state->Identity = ++owner->ResourceStateOrdinal;
    state->Progress = watch->Progress; state->Class = watch->Class; state->Revision = 1;
    watch->Resource = state;
    return true;
}
ProgressResourceTicket* FindProgressResourceTicketLocked(Owner* owner, void* receipt) {
    for (auto* value = owner->ResourceTickets; value; value = value->Next)
        if (value == receipt) return value;
    return nullptr;
}
bool ResourceStateMatchesLocked(Owner* owner, const ProgressResourceState* state,
    std::uint64_t revision, std::uint64_t reference) {
    if (!state || state->Value != owner || state->Revision != revision) return false;
    const auto* watch = RelevantProgressWatchLocked(owner, state->Progress);
    std::uint64_t actual{};
    return watch && watch->Value == owner && watch->Resource == state && watch->Class == state->Class
        && ReadProgressReference(watch, &actual) && actual == reference;
}
bool InitializationResourceMatchesLocked(Owner* owner, const Initialization* value) {
    return !owner->ResourceEnabled || (value
        && ResourceStateMatchesLocked(owner, value->Resource, value->ResourceRevision, value->ResourceReference));
}
bool SnapshotInitializationResourceLocked(Owner* owner, Initialization* value) {
    if (!owner->ResourceEnabled) return true;
    const auto* watch = RelevantProgressWatchLocked(owner, value->Info.progress);
    std::uint64_t reference{};
    if (!watch || !watch->Resource || !ReadProgressReference(watch, &reference)) return false;
    value->Resource = watch->Resource; value->ResourceRevision = watch->Resource->Revision;
    value->ResourceReference = reference;
    return true;
}
bool ResourceTicketCurrentLocked(Owner* owner, const ProgressResourceTicket* ticket) {
    return ticket && ticket->Value == owner && (ticket->Phase == 1 || ticket->Phase == 2)
        && owner->Flags == RtSourceArmed && !owner->Faults
        && ResourceStateMatchesLocked(owner, ticket->Resource, ticket->Revision, ticket->Reference)
        && (!ticket->Initializer || (ticket->Initializer == owner->Initializer
            && ticket->Initializer->ResourceTicket == ticket && ticket->Initializer->Info.phase < 7
            && ticket->Initializer->Info.phase != 5 && InitializationResourceMatchesLocked(owner, ticket->Initializer)));
}
bool InitializationResourceReadyLocked(Owner* owner, const Initialization* value) {
    if (!owner->ResourceEnabled) return true;
    const auto* ticket = value ? value->ResourceTicket : nullptr;
    return value && value == owner->Initializer && !value->PhysicalInFlight
        && ticket && ticket->Phase == 2 && ticket->Initializer == value
        && ticket->Resource == value->Resource && ticket->Revision == value->ResourceRevision
        && ticket->Reference == value->ResourceReference && ResourceTicketCurrentLocked(owner, ticket);
}
bool ResourcePendingPlayerReplaceLocked(Owner* owner, const PlayerWatch* pending) {
    const auto* gate = owner->Logical;
    const auto* fresh = owner->Initializer;
    const auto* prior = pending ? pending->ResourceInitializer : nullptr;
    const auto* progress = owner->PendingProgressWatch;
    return owner->ResourceEnabled && pending && pending->Value == owner && pending == owner->PendingWatch
        && pending->Phase == 1 && pending->Gate == gate && gate && gate->Closed && fresh
        && gate->Initializer == fresh && fresh->Info.phase == 6 && InitializationResourceReadyLocked(owner, fresh)
        && prior && prior != fresh && prior->ResourceInvalidated && prior->Info.phase == 5 && !prior->PhysicalInFlight
        && prior->Info.ordinal < fresh->Info.ordinal
        && (!progress || (progress->Value == owner && progress->Phase == 1 && progress->Player == pending
            && progress->Resource == fresh->Resource && progress->Progress == fresh->Info.progress));
}
bool ResourcePendingProgressReplaceLocked(Owner* owner, const ProgressWatch* pending,
    const PlayerWatch* player, std::uint64_t progress) {
    const auto* fresh = owner->Initializer;
    const auto* replaced = player ? player->ReplacedPending : nullptr;
    const auto* prior = replaced ? replaced->ResourceInitializer : nullptr;
    return owner->ResourceEnabled && pending && pending->Value == owner && pending == owner->PendingProgressWatch
        && pending->Phase == 1 && player && player->Value == owner && player == owner->PendingWatch && player->Phase == 1
        && replaced && replaced->Phase == 3 && pending->Player == replaced
        && prior && prior->ResourceInvalidated && prior->Info.phase == 5 && !prior->PhysicalInFlight
        && fresh && owner->Logical && owner->Logical->Closed && owner->Logical->Initializer == fresh
        && player->ResourceInitializer == fresh && fresh->Info.phase == 6 && InitializationResourceReadyLocked(owner, fresh)
        && fresh->Info.ordinal > prior->Info.ordinal && fresh->Info.progress == progress
        && pending->Progress == progress && pending->Resource == fresh->Resource;
}
void ConsumeInitializationResourceLocked(Owner* owner, Initialization* value) {
    if (!owner->ResourceEnabled || !value) return;
    // Called only after the original joint Commit succeeded under Owner.Lock.
    // The resulting current AudioClip authority no longer depends on this
    // future-resource credential, so later +30 writes cannot revoke it.
    if (!InitializationResourceReadyLocked(owner, value)) { FaultLocked(owner); return; }
    value->ResourceTicket->Phase = 3;
}
void InvalidateProgressResourceLocked(Owner* owner, ProgressResourceState* state) {
    for (auto* ticket = owner->ResourceTickets; ticket; ticket = ticket->Next)
        if (ticket->Resource == state && (ticket->Phase == 1 || ticket->Phase == 2)) ticket->Phase = 4;
    auto* value = owner->Initializer;
    if (value && value->Resource == state && value->Info.phase < 7) {
        if (value->Info.phase != 5) value->ResourceInvalidated = true;
        value->Info.phase = 5;
        // FreshBlocked remains until a genuinely new actual Start completes.
        // Neither this invalidation nor zero-inflight issues completion.
    }
}
void AdvanceProgressResourceLocked(Owner* owner, void* self) {
    auto* watch = RelevantProgressWatchLocked(owner, reinterpret_cast<std::uint64_t>(self));
    if (!watch || !watch->Resource) { FaultLocked(owner); return; }
    auto* state = watch->Resource;
    if (state->Revision == UINT64_MAX) FaultLocked(owner); else ++state->Revision;
    // Every relevant observed attempt invalidates BEFORE the original store,
    // including same/null/ABA values, worker calls, and stores that later throw.
    InvalidateProgressResourceLocked(owner, state);
}
void RetireProgressResourceWatchLocked(Owner* owner, const ProgressWatch* watch) {
    if (!owner->ResourceEnabled || !watch->Resource) return;
    const auto* current = RelevantProgressWatchLocked(owner, watch->Progress);
    if (!current || current->Resource != watch->Resource)
        InvalidateProgressResourceLocked(owner, watch->Resource);
}
bool BindInitializationResourceLocked(Owner* owner, ProgressResourceTicket* ticket) {
    auto* scope = currentScope;
    auto* value = owner->Initializer;
    if (!ResourceTicketCurrentLocked(owner, ticket) || !value || value->Info.phase != 1
        || !scope || !scope->CallbackActive || scope->SuppressObserver || scope->OwnerValue != owner
        || scope->InitializerValue != value || scope->Frame.Role != RtSourceProgressStartMoveNext
        || scope->Frame.Stage != RtSourceBefore || scope->Frame.ParentSequence || scope->Frame.Depth != 1
        || scope->Frame.Sequence != value->Info.yieldSequence || scope->Frame.Token != value->Info.yieldToken
        || ticket->Resource != value->Resource || ticket->Revision != value->ResourceRevision
        || ticket->Reference != value->ResourceReference) return false;
    if (ticket->Phase == 2) return ticket->Initializer == value && value->ResourceTicket == ticket;
    if (ticket->Phase != 1 || ticket->Initializer || value->ResourceTicket) return false;
    ticket->Initializer = value; ticket->Phase = 2; value->ResourceTicket = ticket;
    return true;
}
void ProgressResourceInformationLocked(Owner* owner, const ProgressResourceTicket* ticket,
    RtPlaybackProgressResourceInfoV1* info) {
    const auto* state = ticket->Resource;
    *info = RtPlaybackProgressResourceInfoV1{1, sizeof(*info), ticket->Phase,
        ResourceTicketCurrentLocked(owner, ticket) ? 1u : 0u,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch, reinterpret_cast<std::uint64_t>(ticket), ticket->Ordinal,
        reinterpret_cast<std::uint64_t>(ticket->Watch), state->Progress, state->Class, state->Identity,
        ticket->Revision, ticket->Reference, reinterpret_cast<std::uint64_t>(ticket->Initializer),
        ticket->Initializer ? ticket->Initializer->Info.ordinal : 0};
}
}
std::uint32_t __cdecl RtPlaybackProgressResourceVersion() { return 1; }
int __cdecl RtPlaybackSourceReadProgressResource(void* raw, void* receipt, RtPlaybackProgressResourceInfoV1* info) {
    auto* owner = FindOwner(raw);
    if (!owner || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockExclusive(&owner->Lock);
    auto* watch = FindProgressWatchLocked(owner, receipt); std::uint64_t reference{};
    const bool live = owner->ResourceEnabled && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && watch && watch->Value == owner && watch->Resource
        && ((owner->ProgressWatchValue == watch && watch->Phase == 2) || (owner->PendingProgressWatch == watch && watch->Phase == 1));
    const bool valid = live && ReadProgressReference(watch, &reference);
    if (live && !valid) FaultLocked(owner);
    if (valid) *info = RtPlaybackProgressResourceInfoV1{1, sizeof(*info), 0, 1,
        reinterpret_cast<std::uint64_t>(owner), owner->Epoch, 0, 0, reinterpret_cast<std::uint64_t>(watch),
        watch->Progress, watch->Class, watch->Resource->Identity, watch->Resource->Revision, reference, 0, 0};
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceStageProgressResource(void* raw, void* receipt, std::uint64_t revision,
    void* rootedReference, void** output) {
    auto* owner = FindOwner(raw); if (!owner || !output) return 0;
    *output = nullptr; AcquireSRWLockExclusive(&owner->Lock);
    auto* watch = FindProgressWatchLocked(owner, receipt);
    const bool live = owner->ResourceEnabled && owner->Thread == GetCurrentThreadId() && owner->Flags == RtSourceArmed
        && !owner->Faults && watch && watch->Value == owner && watch->Resource
        && ((owner->ProgressWatchValue == watch && watch->Phase == 2) || (owner->PendingProgressWatch == watch && watch->Phase == 1));
    const bool valid = live && ResourceStateMatchesLocked(owner, watch->Resource, revision, reinterpret_cast<std::uint64_t>(rootedReference))
        && owner->ResourceTicketOrdinal != UINT64_MAX;
    if (valid) {
        auto* ticket = new(std::nothrow) ProgressResourceTicket{};
        if (ticket) {
            ticket->Value = owner; ticket->Next = owner->ResourceTickets; ticket->Resource = watch->Resource; ticket->Watch = watch;
            ticket->Ordinal = ++owner->ResourceTicketOrdinal; ticket->Revision = revision;
            ticket->Reference = reinterpret_cast<std::uint64_t>(rootedReference); ticket->Phase = 1;
            owner->ResourceTickets = ticket;
            // A staged future record gains initializer authority only through
            // the exact actual Start state0 Before callback. Outside that TLS
            // context Stage issues a future credential with no source authority.
            BindInitializationResourceLocked(owner, ticket);
            *output = ticket;
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return *output ? 1 : 0;
}
int __cdecl RtPlaybackSourceReadProgressResourceTicket(void* raw, void* receipt, RtPlaybackProgressResourceInfoV1* info) {
    auto* owner = FindOwner(raw); if (!owner || !info || info->Version != 1 || info->Size != sizeof(*info)) return 0;
    AcquireSRWLockShared(&owner->Lock); const auto* ticket = FindProgressResourceTicketLocked(owner, receipt);
    // Always readable after invalidation/Unknown/withdrawal for owner drain.
    if (ticket) ProgressResourceInformationLocked(owner, ticket, info);
    ReleaseSRWLockShared(&owner->Lock); return ticket ? 1 : 0;
}
int __cdecl RtPlaybackSourceBindInitializationResource(void* raw, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock); auto* ticket = FindProgressResourceTicketLocked(owner, receipt);
    const bool valid = owner->Thread == GetCurrentThreadId() && BindInitializationResourceLocked(owner, ticket);
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
int __cdecl RtPlaybackSourceRetireProgressResource(void* raw, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner) return 0;
    AcquireSRWLockExclusive(&owner->Lock); auto* ticket = FindProgressResourceTicketLocked(owner, receipt);
    bool valid = ticket && owner->Thread == GetCurrentThreadId();
    if (valid && ticket->Phase != 5) {
        // Normal group withdrawal permanently removes source authority, but
        // known physical exit still permits the retained reference to drain.
        // Unknown/faulted owners keep every outstanding resource obligation.
        valid = !(owner->Flags & RtSourceUnknown) && !owner->Faults && !owner->InFlight
            && (!ticket->Initializer || !ticket->Initializer->PhysicalInFlight);
        if (valid) {
            if (ticket->Initializer && ticket->Phase == 2 && ticket->Initializer->Info.phase < 7)
                ticket->Initializer->Info.phase = 5;
            ticket->Phase = 5;
        }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return valid ? 1 : 0;
}
std::uint32_t __cdecl RtPlaybackSourceRetryResourceInitialization(void* raw, void* prior, void* fresh,
    std::uint32_t action, void* request) {
    LogicalGate* gate = nullptr;
    for (auto& owner : owners) if (owner.Logical && owner.Logical == raw) { gate = owner.Logical; break; }
    if (!gate || !prior || !fresh || !request || (action != 12 && action != 13)) return 1;
    auto* owner = gate->Value; AcquireSRWLockExclusive(&owner->Lock);
    const auto* predecessor = gate->Initializer;
    auto* candidate = owner->Initializer; std::uint32_t result = 0;
    if (!owner->ResourceEnabled || owner->Flags != RtSourceArmed || owner->Faults || owner->Thread != GetCurrentThreadId()) result = 3;
    else if (!gate->Closed || !owner->FreshBlocked || !predecessor || predecessor != prior
        || predecessor == candidate || !predecessor->ResourceInvalidated || predecessor->Info.phase != 5
        || !candidate || candidate != fresh || candidate->Info.phase != 4) result = 2;
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
