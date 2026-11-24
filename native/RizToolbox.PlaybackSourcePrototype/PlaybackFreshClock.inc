namespace {
const RtPlaybackSourceFrame* FreshClockJournalLocked(Owner* owner, std::uint64_t sequence, std::uint64_t token) {
    if (!sequence || !token) return nullptr;
    for (const auto& row : owner->Journal)
        if (row.State == 2 && row.Frame.Sequence == sequence && row.Frame.Token == token) return &row.Frame;
    return nullptr;
}
bool FreshClockExited(const RtPlaybackSourceFrame* frame, Owner* owner) {
    const auto good = RtSourceArmed | RtSourceReturnedFlag | RtSourcePhysicalExitedFlag;
    const auto bad = RtSourceDormant | RtSourceUnknown | RtSourceAbnormalFlag | RtSourceWithdrawn;
    return frame && frame->Version == 4 && frame->Size == sizeof(*frame) && !frame->Reserved
        && frame->Owner == reinterpret_cast<std::uint64_t>(owner) && frame->OwnerEpoch == owner->Epoch
        && frame->ThreadId == owner->Thread && frame->Stage == RtSourcePhysicalExit
        && (frame->Flags & good) == good && !(frame->Flags & bad);
}
bool FreshClockBaselineLocked(Owner* owner, const PlayerWatch* player, const ProgressWatch* progress,
    const RizFreshClockBaselineV1& b) {
    if (b.version != 1 || b.size != sizeof(b) || b.reserved || b.ownerEpoch != owner->Epoch
        || b.rootSlot >= SlotCount || b.leafSlot >= SlotCount || b.rootSlot == b.leafSlot || !b.leafCaller
        || owner->Writer != b.writer || owner->Source != b.source || owner->Scheduling != b.scheduling) return false;
    const auto* root = FreshClockJournalLocked(owner, b.rootSequence, b.rootToken);
    const auto* leaf = FreshClockJournalLocked(owner, b.leafSequence, b.leafToken);
    if (!FreshClockExited(root, owner) || !FreshClockExited(leaf, owner)
        || root->ParentSequence || root->Depth != 1 || root->Slot != b.rootSlot || root->Role != b.rootRole
        || !((root->Role == RtSourcePause && root->Kind == RtSourceVoid0 && root->Self == player->Player)
            || (root->Role == RtSourceProgressPlay && root->Kind == RtSourceBool1 && root->ArgBits <= 1 && root->Self == progress->Progress))
        || leaf->Role != RtSourcePauseLeaf || leaf->Kind != RtSourceVoid0 || leaf->Slot != b.leafSlot
        || leaf->Depth != root->Depth + 1 || leaf->ParentSequence != root->Sequence
        || leaf->Self != player->AudioSource || leaf->IncomingMethod || leaf->Caller != b.leafCaller
        || root->WriterSequence != b.writer || leaf->WriterSequence != b.writer
        || root->SourceSequence != b.source || leaf->SourceSequence != b.source
        || root->SchedulingSequence == UINT64_MAX || root->SchedulingSequence + 1 != leaf->SchedulingSequence
        || leaf->SchedulingSequence != b.scheduling) return false;
    const auto& rslot = slots[b.rootSlot]; const auto& lslot = slots[b.leafSlot];
    if (rslot.Configured != 1 || lslot.Configured != 1 || rslot.OwnerValue != owner || lslot.OwnerValue != owner
        || rslot.Role != root->Role || rslot.Kind != root->Kind || lslot.Role != leaf->Role || lslot.Kind != leaf->Kind) return false;
    unsigned children = 0;
    for (const auto& row : owner->Journal) {
        if (!row.State || row.Frame.ParentSequence != root->Sequence) continue;
        if (&row.Frame != leaf || row.State != 2) return false;
        ++children;
    }
    return children == 1;
}
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackFreshClockVersion() { return 2; }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceEnterFreshClock(
    void* raw, void* rawWatch, const RizFreshClockBaselineV1* baseline, std::uint32_t action,
    RizFreshClockCommit commit, void* request, RizLogicalCommit logicalCommit, void* logicalContext) {
    auto* owner = FindOwner(raw);
    if (!owner || !rawWatch || !commit || !request || action < 1 || action > 4) return 1;
    if (action == 3 ? (!logicalCommit || !logicalContext) : (logicalCommit || logicalContext)) return 1;
    RizFreshClockBaselineV1 basis{};
    if (action == 1 || action == 3) {
        if (!baseline) return 1;
        __try { basis = *baseline; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    } else if (baseline) return 1;
    AcquireSRWLockExclusive(&owner->Lock);
    std::uint32_t result = 0;
    auto* player = FindPlayerWatchLocked(owner, rawWatch);
    auto* progress = owner->ProgressWatchValue; auto* backing = owner->BackingWatch;
    if (owner->Thread != GetCurrentThreadId()) result = 10;
    else if (owner->Flags != RtSourceArmed || owner->Faults) result = 3;
    else if (owner->Logical && (owner->Logical->FreshClock != request || owner->Logical->Closed)) result = 4;
    else if (action == 3 && owner->Logical) result = 2;
    else if (owner->FreshBlocked || owner->LowerPitchBlocked || owner->AudioBlocked || owner->BackingBlocked) result = 4;
    else if (!owner->LowerPitchEnabled || !owner->EnginePitchEnabled || !owner->LowerTimeEnabled || !owner->ResourceEnabled
        || !player || !progress || !backing || !player->Ordinal || !progress->Ordinal || !backing->Ordinal
        || owner->Watch != player || owner->PendingWatch || owner->PendingProgressWatch || owner->PendingBackingWatch
        || progress->Value != owner || backing->Value != owner || progress->Player != player || backing->Player != player
        || backing->Source != player->AudioSource || backing->SourceFence != player->Source
        || CheckUnityBackingLocked(owner) != 1 || !PlayerWatchOpenedLocked(owner)) result = 6;
    // Subsequent publication runs after the real source original returned but
    // may still owe its finally. Only initial Prepare/Arm need joint drain;
    // the GetTime coordinator independently excludes its own live borrowers.
    else if ((action == 1 || action == 3) && owner->InFlight) result = 8;
    else if ((action == 1 || action == 3) && !FreshClockBaselineLocked(owner, player, progress, basis)) result = 6;
    else {
        const RizFreshSourceSnapshotV1 snapshot{
            reinterpret_cast<std::uint64_t>(owner), owner->Epoch,
            reinterpret_cast<std::uint64_t>(player), player->Ordinal,
            reinterpret_cast<std::uint64_t>(progress), progress->Ordinal,
            reinterpret_cast<std::uint64_t>(backing), backing->Ordinal,
            player->Player, player->AudioSource, progress->Progress, backing->Clip,
            backing->Sample.SourceNative, backing->Sample.ClipNative, player->Source, player->AudioAttempt,
            owner->Writer, owner->Scheduling, owner->Entered, owner->Exited,
            backing->Sample.Offset, backing->Sample.SourceId, backing->Sample.ClipId, 0};
        auto* gate = action == 3 ? new(std::nothrow) LogicalGate{owner, logicalCommit, logicalContext, false, 0, nullptr, request} : nullptr;
        if (action == 3 && !gate) result = 7;
        else {
            result = commit(request, action, &snapshot, gate);
            if (!result && gate) owner->Logical = gate;
            else if (gate) delete gate; // Never registered or observed by a borrower.
        }
    }
    // Success deliberately retains this ONE acquisition for the existing
    // LeaveFresh. Never call CoordinateFresh/ReadReceipt while holding it.
    if (result) ReleaseSRWLockExclusive(&owner->Lock);
    return result;
}

namespace {
std::uint32_t CompletedInitializerLocked(Owner* owner, void* progress, void* expected, std::uint32_t phase = 4) {
    if (owner->Thread != GetCurrentThreadId()) return 10;
    if (owner->Flags != RtSourceArmed || owner->Faults) return 3;
    if (!owner->WatchProgressClass || !PlayerClassMatches(reinterpret_cast<std::uint64_t>(progress),
        reinterpret_cast<std::uint64_t>(owner->WatchProgressClass))) return 6;
    if (owner->InFlight) return 8;
    auto* value = owner->Initializer;
    // Capture may run at post-Poll before this rooted Progress has completed
    // Start. Only this selection entry reports that known absence as pending;
    // verification of an already-issued private record remains strict.
    if (!expected && phase == 4) {
        if (!RelevantProgressWatchLocked(owner, reinterpret_cast<std::uint64_t>(progress))) return 9;
        if (!value || value->Info.progress != reinterpret_cast<std::uint64_t>(progress)
            || value->Info.phase < 4 || value->Info.phase == 7 && !owner->FreshBlocked) return 8;
    }
    // The existing initializer state machine alone recorded the actual normal
    // yield, clip child, final AL0 and physical exits. This entry selects that
    // completed record; it does not replay or recreate any execution evidence.
    if ((phase != 4 && phase != 6 && phase != 7) || !value || value->Info.phase != phase || value->PhysicalInFlight
        || (phase == 7 ? owner->FreshBlocked : !owner->FreshBlocked)
        || expected && expected != value || value->Info.progress != reinterpret_cast<std::uint64_t>(progress)
        || !value->Info.yieldSequence || !value->Info.yieldToken || !value->Info.finalSequence || !value->Info.finalToken
        || !value->Info.childSequence || !value->Info.childToken || !value->ChildNormal || value->Children != 1
        || value->Info.yieldSequence >= value->Info.finalSequence || value->Info.finalSequence >= value->Info.childSequence
        || owner->Source != value->Info.sourceFence || !ReadFinalGraph(value)
        || (phase != 7 && (owner->Writer != value->Info.writer || owner->Scheduling != value->Info.scheduling
            || !InitializationResourceReadyLocked(owner, value)))
        || (phase == 6 && (!owner->Logical || !owner->Logical->Closed || owner->Logical->Initializer != value))
        || (phase == 7 && (!owner->Logical || owner->Logical->Closed || !PlayerWatchOpenedLocked(owner)
            || owner->Watch->Player != value->Info.player || owner->Watch->AudioSource != value->Info.source))
        || !owner->WatchProgressClass || !PlayerClassMatches(value->Info.progress, reinterpret_cast<std::uint64_t>(owner->WatchProgressClass))) return 6;
    return 0;
}
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackCompletedInitializerVersion() { return 1; }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceCaptureCompletedInitializer(
    void* raw, void* progress, void** output) {
    auto* owner = FindOwner(raw); if (!owner || !progress || !output) return 1;
    __try { *output = nullptr; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    AcquireSRWLockExclusive(&owner->Lock);
    auto result = CompletedInitializerLocked(owner, progress, nullptr);
    if (!result) {
        __try { *output = owner->Initializer; } __except (EXCEPTION_EXECUTE_HANDLER) { result = 1; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceReadCompletedInitializer(
    void* raw, void* progress, void* receipt, std::uint32_t phase, RizPlaybackInitializationInfoV2* output) {
    auto* owner = FindOwner(raw); if (!owner || !progress || !receipt || !output) return 1;
    __try { if (output->version != 2 || output->size != sizeof(*output)) return 1; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    AcquireSRWLockExclusive(&owner->Lock);
    auto result = CompletedInitializerLocked(owner, progress, receipt, phase);
    if (!result) {
        auto value = owner->Initializer->Info; value.receipt = reinterpret_cast<std::uint64_t>(receipt);
        value.blocked = owner->FreshBlocked ? 1u : 0u;
        __try { *output = value; } __except (EXCEPTION_EXECUTE_HANDLER) { result = 1; }
    }
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceVerifyCompletedInitializer(
    void* raw, void* progress, void* receipt) {
    auto* owner = FindOwner(raw); if (!owner || !progress || !receipt) return 1;
    AcquireSRWLockExclusive(&owner->Lock);
    const auto result = CompletedInitializerLocked(owner, progress, receipt);
    ReleaseSRWLockExclusive(&owner->Lock); return result;
}
