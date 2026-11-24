// Included after the actual player/progress/backing definitions. This additive
// coordinator never replaces an initialization or logical-handoff receipt.
namespace {
void CaptureSourceFreshDiagnosticLocked(const Owner* owner, std::uint32_t action,
    std::uint32_t status, std::uint32_t origin, RizFreshSourceFailureV2* diagnostic) {
    if (!diagnostic) return;
    RizFreshSourceFailureV2 value{};
    value.version = 2; value.size = sizeof(value); value.valid = 1;
    value.status = status; value.origin = status ? origin : RizFreshSourceDiagnosticNone; value.action = action;
    if (owner) {
        value.sourceMaskValid = 1;
        value.sourceBlockMask = (owner->FreshBlocked ? RizFreshSourceFreshBlocked : 0u)
            | (owner->LowerPitchBlocked ? RizFreshSourceLowerPitchBlocked : 0u)
            | (owner->AudioBlocked ? RizFreshSourceAudioBlocked : 0u)
            | (owner->BackingBlocked ? RizFreshSourceBackingBlocked : 0u)
            | (owner->Logical && owner->Logical->Closed ? RizFreshSourceLogicalClosed : 0u);
        // This is the current real scope, not a historical blocker-setter stamp.
        if (currentScope && currentScope->OwnerValue == owner) {
            const auto& frame = currentScope->Frame;
            value.scopeValid = 1; value.role = frame.Role; value.kind = frame.Kind;
            value.depth = frame.Depth; value.sequence = frame.Sequence;
            value.parentSequence = frame.ParentSequence;
            // Reference arguments can be rooted IL2CPP object addresses. Only
            // scalar parameters belong in this diagnostic projection.
            if (frame.Kind == RtSourceSingle1 || frame.Kind == RtSourceDouble1
                || frame.Kind == RtSourceBool1 || frame.Kind == RtSourceUInt64_1 || frame.Kind == RtSourceIcallSingle1)
                value.argBits = frame.ArgBits;
            value.childFlags = frame.Flags & (RtSourceLowerPitchChildFlag | RtSourceEnginePitchChildFlag
                | RtSourceLowerTimeChildFlag | RtSourceStockStopFlag);
            if (frame.Role == RtSourceEngineSetPitch && frame.Kind == RtSourceIcallSingle1) {
                value.engineChildTestedMask = currentScope->EnginePitchChildTested;
                value.engineChildMatchedMask = currentScope->EnginePitchChildMatched;
                // This parent belongs to the current physical scope only. The
                // masks were recorded by the original predicates, not replayed.
                if (const auto* parent = currentScope->Parent) {
                    const auto& prior = parent->Frame;
                    value.parentRole = prior.Role; value.parentKind = prior.Kind;
                    value.parentStage = prior.Stage; value.parentFlags = prior.Flags;
                    value.parentDepth = prior.Depth;
                    value.parentEngineBeginTestedMask = parent->EnginePitchBeginTested;
                    value.parentEngineBeginMatchedMask = parent->EnginePitchBeginMatched;
                    if (prior.Kind == RtSourceSingle1 || prior.Kind == RtSourceDouble1
                        || prior.Kind == RtSourceBool1 || prior.Kind == RtSourceUInt64_1 || prior.Kind == RtSourceIcallSingle1)
                        value.parentArgBits = prior.ArgBits;
                }
                // No module query or address leaves this projection. Only an
                // in-body offset from the configured lower entry is disclosed.
                if (owner->EnginePitchLowerSlot < SlotCount) {
                    const auto begin = reinterpret_cast<std::uint64_t>(slots[owner->EnginePitchLowerSlot].Target);
                    if (begin && frame.Caller >= begin && frame.Caller - begin <= 495) {
                        value.callerInLowerBody = 1;
                        value.callerLowerOffset = static_cast<std::uint32_t>(frame.Caller - begin);
                    }
                }
            }
        }
    }
    *diagnostic = value;
}
std::uint32_t CoordinateFreshSourceCore(void* raw, void* rawWatch, std::uint32_t action,
    RizFreshSourceCommit commit, void* request, RizFreshSourceFailureV2* diagnostic) {
    auto* owner = FindOwner(raw);
    if (!owner || !rawWatch || !commit || !request || action < 1 || action > 4) {
        CaptureSourceFreshDiagnosticLocked(nullptr, action, 1, RizFreshSourceDiagnosticSourceCoordinator, diagnostic);
        return 1;
    }
    AcquireSRWLockExclusive(&owner->Lock);
    // Thread rejection must precede any candidate allocation or tombstone.
    // Diagnostic collection must not inspect source flags or TLS on this path.
    if (owner->Thread != GetCurrentThreadId()) {
        CaptureSourceFreshDiagnosticLocked(nullptr, action, 10, RizFreshSourceDiagnosticSourceCoordinator, diagnostic);
        ReleaseSRWLockExclusive(&owner->Lock); return 10;
    }
    if (action == 4) {
        CaptureSourceFreshDiagnosticLocked(nullptr, action, 0, RizFreshSourceDiagnosticNone, diagnostic);
        ReleaseSRWLockExclusive(&owner->Lock); return 0;
    }
    std::uint32_t result = 0;
    std::uint32_t origin = RizFreshSourceDiagnosticSourceCoordinator;
    auto* player = FindPlayerWatchLocked(owner, rawWatch);
    const bool issuing = action == 1;
    auto* progress = issuing ? owner->PendingProgressWatch : owner->ProgressWatchValue;
    auto* backing = issuing ? owner->PendingBackingWatch : owner->BackingWatch;
    if (owner->Flags != RtSourceArmed || owner->Faults) result = 3;
    else if (!owner->LowerPitchEnabled || !owner->EnginePitchEnabled || !owner->LowerTimeEnabled
        || !owner->ResourceEnabled || !owner->WatchClass || !owner->WatchAudioClass || !owner->WatchProgressClass
        || !owner->UnityObjectClass || !owner->UnityClipClass) result = 6;
    else if (owner->FreshBlocked || owner->LowerPitchBlocked || owner->AudioBlocked || owner->BackingBlocked
        || owner->Logical && owner->Logical->Closed) result = 4;
    else if ((issuing || action == 3) && owner->InFlight) result = 8;
    else if (!player || !progress || !backing || !player->Ordinal || !progress->Ordinal || !backing->Ordinal
        || progress->Value != owner || backing->Value != owner || progress->Player != player || backing->Player != player
        || backing->Source != player->AudioSource || backing->SourceFence != player->Source) result = 6;
    else if (issuing && (owner->Logical || owner->Watch || owner->PendingWatch != player
        || player->Expected || player->Gate || player->Cut || progress->Expected
        || !PlayerWatchReadyLocked(owner))) result = 6;
    else if (!issuing && (owner->Watch != player || owner->PendingWatch || owner->PendingProgressWatch
        || owner->PendingBackingWatch || CheckUnityBackingLocked(owner) != 1 || !PlayerWatchOpenedLocked(owner))) result = 6;
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
        // The native GetTime coordinator records its epoch first while neither
        // receipt nor watch is visible to managed code. Commit cannot fail.
        result = commit(request, action, &snapshot);
        origin = RizFreshSourceDiagnosticJointCommit;
        if (result == 0 && issuing) CommitPlayerWatchLocked(owner);
    }
    CaptureSourceFreshDiagnosticLocked(owner, action, result, origin, diagnostic);
    ReleaseSRWLockExclusive(&owner->Lock);
    return result;
}
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackFreshSourceVersion() { return 1; }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackFreshSourceDiagnosticVersion() { return 2; }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceCoordinateFresh(
    void* raw, void* rawWatch, std::uint32_t action, RizFreshSourceCommit commit, void* request) {
    return CoordinateFreshSourceCore(raw, rawWatch, action, commit, request, nullptr);
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtPlaybackSourceCoordinateFreshWithDiagnostic(
    void* raw, void* rawWatch, std::uint32_t action, RizFreshSourceCommit commit, void* request,
    RizFreshSourceFailureV2* diagnostic) {
    if (!diagnostic) return 1;
    __try {
        if (diagnostic->version != 2 || diagnostic->size != sizeof(*diagnostic)) return 1;
        const RizFreshSourceFailureV2 empty{2, sizeof(*diagnostic)};
        *diagnostic = empty;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    RizFreshSourceFailureV2 value{};
    const auto result = CoordinateFreshSourceCore(raw, rawWatch, action, commit, request, &value);
    __try { *diagnostic = value; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    return result;
}
