// A first fresh graph receives the same native epoch domain used by bootstrap,
// InitialCapture and handoff. Watch ordinals never substitute for source epochs.
namespace {
bool FreshClockBlocksRelease(State* state);
struct FreshSourceReceipt {
    State* state;
    FreshSourceReceipt* next;
    void* sourceOwner;
    void* playerWatch;
    RizFreshSourceCoordinate coordinate;
    RizFreshSourceCoordinateWithDiagnostic coordinateWithDiagnostic;
    RizFreshSourceSnapshotV1 source;
    std::uint64_t epoch;
    std::uint32_t slot, phase;
};
FreshSourceReceipt* freshSourceRegistry = nullptr;
SRWLOCK freshSourceRegistryLock = SRWLOCK_INIT;
FreshSourceReceipt* RegisteredFreshSource(void* raw) {
    FreshSourceReceipt* found = nullptr;
    AcquireSRWLockShared(&freshSourceRegistryLock);
    for (auto* value = freshSourceRegistry; value; value = value->next)
        if (value == raw) { found = value; break; }
    ReleaseSRWLockShared(&freshSourceRegistryLock);
    return found;
}
bool SameFreshLifetime(const RizFreshSourceSnapshotV1& left, const RizFreshSourceSnapshotV1& right) {
    // Writer/Scheduling advance on legitimate Pause/Resume/rate writes. Source,
    // constructor/audio-attempt and all three never-reused watch identities do not.
    return left.owner == right.owner && left.ownerEpoch == right.ownerEpoch
        && left.playerWatch == right.playerWatch && left.playerOrdinal == right.playerOrdinal
        && left.progressWatch == right.progressWatch && left.progressOrdinal == right.progressOrdinal
        && left.backingWatch == right.backingWatch && left.backingOrdinal == right.backingOrdinal
        && left.player == right.player && left.audioSource == right.audioSource
        && left.progress == right.progress && left.clip == right.clip
        && left.sourceNative == right.sourceNative && left.clipNative == right.clipNative
        && left.sourceFence == right.sourceFence && left.audioAttempt == right.audioAttempt
        && left.instanceIdOffset == right.instanceIdOffset && left.sourceId == right.sourceId && left.clipId == right.clipId
        && !left.reserved && !right.reserved;
}
std::uint32_t FreshSourceJointCommit(void* raw, std::uint32_t action, const RizFreshSourceSnapshotV1* source) {
    auto* receipt = static_cast<FreshSourceReceipt*>(raw);
    auto* state = receipt ? receipt->state : nullptr;
    if (!state || state->freshSourceCapture != receipt || !source
        || source->owner != reinterpret_cast<std::uint64_t>(state->freshOwner)
        || source->playerWatch != reinterpret_cast<std::uint64_t>(receipt->playerWatch)) return 6;
    if (action == 1) {
        if (receipt->phase != 0 || receipt->epoch || state->logical || state->handoff || state->initialCapture
            || state->sourceGate || Published(state) || state->bootstrap || Read(&state->policyClaimed)
            || Read(&state->authorityActive) || Read(&state->authorityClosed) || Read(&state->logicalBlocked)) return 2;
        const auto epoch = NextBootstrapEpoch();
        if (!epoch) return 7;
        receipt->source = *source; receipt->epoch = epoch; receipt->phase = 1;
        return 0;
    }
    if (receipt->phase != 1 && receipt->phase != 2) return 2;
    if (!SameFreshLifetime(receipt->source, *source)) return 6;
    if (action == 3) {
        if (receipt->phase != 1 || Published(state) || state->bootstrap || state->logical || state->initialCapture
            || state->handoff || Read(&state->policyClaimed) || Read(&state->authorityActive)) return 2;
        receipt->phase = 2;
    }
    return 0;
}
std::uint32_t FreshSourceSingleReturnMaskLocked(State* state) {
    return (Read(&state->authorityClosed) ? RizFreshSourceAuthorityClosed : 0u)
        | (Read(&state->logicalBlocked) ? RizFreshSourceLogicalBlocked : 0u);
}
void CaptureSingleReturnFreshDiagnosticLocked(State* state, std::uint32_t action,
    std::uint32_t status, RizFreshSourceFailureV2* diagnostic) {
    if (!diagnostic) return;
    RizFreshSourceFailureV2 value{};
    value.version = 2; value.size = sizeof(value); value.valid = 1;
    value.status = status;
    value.origin = status ? RizFreshSourceDiagnosticSingleReturnPrecheck : RizFreshSourceDiagnosticNone;
    value.action = action; value.singleReturnMask = FreshSourceSingleReturnMaskLocked(state);
    *diagnostic = value;
}
std::uint32_t CoordinateFreshSourceReceiptLocked(FreshSourceReceipt* receipt, std::uint32_t action,
    RizFreshSourceFailureV2* diagnostic) {
    if (!diagnostic)
        return receipt->coordinate(receipt->sourceOwner, receipt->playerWatch, action, FreshSourceJointCommit, receipt);
    // Both addresses were captured from the original bound module. A diagnostic
    // read never falls back to the legacy coordinator or performs another query.
    if (!receipt->coordinateWithDiagnostic
        || receipt->coordinateWithDiagnostic != receipt->state->freshSourceCoordinateWithDiagnostic) {
        CaptureSingleReturnFreshDiagnosticLocked(receipt->state, action, 6, diagnostic); return 6;
    }
    const auto result = receipt->coordinateWithDiagnostic(receipt->sourceOwner, receipt->playerWatch,
        action, FreshSourceJointCommit, receipt, diagnostic);
    diagnostic->singleReturnMask = FreshSourceSingleReturnMaskLocked(receipt->state);
    return result;
}
std::uint32_t VerifyFreshSourceLocked(FreshSourceReceipt* receipt, std::uint32_t action,
    RizFreshSourceFailureV2* diagnostic = nullptr) {
    auto* state = receipt->state;
    if (state->freshSourceCapture != receipt || receipt->sourceOwner != state->freshOwner) {
        CaptureSingleReturnFreshDiagnosticLocked(state, action, 6, diagnostic); return 6;
    }
    if (Read(&state->configured) != 1 || Read(&state->unknown) || Read(&state->physicalClosed)) {
        CaptureSingleReturnFreshDiagnosticLocked(state, action, 3, diagnostic); return 3;
    }
    if (Read(&state->authorityClosed) || Read(&state->logicalBlocked)) {
        CaptureSingleReturnFreshDiagnosticLocked(state, action, 4, diagnostic); return 4;
    }
    if ((action == 1 || action == 3) && Read64(&state->inFlight)) {
        CaptureSingleReturnFreshDiagnosticLocked(state, action, 8, diagnostic); return 8;
    }
    return CoordinateFreshSourceReceiptLocked(receipt, action, diagnostic);
}
std::uint32_t ReadFreshSourceCore(void* raw, RizFreshSourceInfoV1* output, RizFreshSourceFailureV2* diagnostic) {
    auto* receipt = RegisteredFreshSource(raw);
    if (!receipt || !output) return 1;
    __try { if (output->version != 1 || output->size != sizeof(*output)) return 1; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    // A pending candidate has no source epoch or active-watch receipt to read.
    // Inspecting it must preserve the exact Busy retry, never retire its watch.
    auto result = CoordinateFreshSourceReceiptLocked(receipt, 4, diagnostic);
    if (!result) {
        if (receipt->phase == 0) {
            result = 8; CaptureSingleReturnFreshDiagnosticLocked(state, 2, result, diagnostic);
        } else result = VerifyFreshSourceLocked(receipt, 2, diagnostic);
    }
    if (result && result != 8 && result != 10 && receipt->phase != 3) receipt->phase = 4;
    if (!result) {
        const RizFreshSourceInfoV1 value{1, sizeof(*output), receipt->slot, receipt->phase, receipt, receipt->epoch, receipt->source};
        __try { *output = value; }
        __except (EXCEPTION_EXECUTE_HANDLER) {
            result = 1; CaptureSingleReturnFreshDiagnosticLocked(state, 2, result, diagnostic);
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_FreshSourceVersion() { return 1; }
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_FreshSourceDiagnosticVersion() { return 2; }
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_IssueFreshSource(
    std::uint32_t slot, void* sourceOwner, void* playerWatch, void** output) {
    if (slot >= limit || !sourceOwner || !playerWatch || !output) return 1;
    __try { *output = nullptr; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    if (state->freshOwner != sourceOwner || !state->freshSourceCoordinate) result = 6;
    else if ((result = state->freshSourceCoordinate(sourceOwner, playerWatch, 4, FreshSourceJointCommit, state)) != 0) {}
    else if (state->freshSourceCapture && (state->freshSourceCapture->playerWatch != playerWatch
        || state->freshSourceCapture->sourceOwner != sourceOwner || state->freshSourceCapture->phase != 0)) result = 2;
    else {
        auto* receipt = state->freshSourceCapture;
        if (!receipt) {
            receipt = static_cast<FreshSourceReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(FreshSourceReceipt)));
            if (!receipt) result = 7;
            else {
                receipt->state = state; receipt->slot = slot; receipt->sourceOwner = sourceOwner;
                receipt->playerWatch = playerWatch; receipt->coordinate = state->freshSourceCoordinate;
                receipt->coordinateWithDiagnostic = state->freshSourceCoordinateWithDiagnostic;
                state->freshSourceCapture = receipt;
                AcquireSRWLockExclusive(&freshSourceRegistryLock);
                receipt->next = freshSourceRegistry; freshSourceRegistry = receipt;
                ReleaseSRWLockExclusive(&freshSourceRegistryLock);
            }
        }
        if (receipt) {
            result = VerifyFreshSourceLocked(receipt, 1);
            // Busy keeps this exact candidate and watches. Every other failure
            // is terminal; no fresh slot, root graph or watch can replace it.
            if (result && result != 8 && result != 10) receipt->phase = 4;
            *output = receipt;
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadFreshSource(void* raw, RizFreshSourceInfoV1* output) {
    return ReadFreshSourceCore(raw, output, nullptr);
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadFreshSourceWithDiagnostic(void* raw,
    RizFreshSourceInfoV1* output, RizFreshSourceFailureV2* diagnostic) {
    if (!diagnostic) return 1;
    __try {
        if (diagnostic->version != 2 || diagnostic->size != sizeof(*diagnostic)) return 1;
        const RizFreshSourceFailureV2 empty{2, sizeof(*diagnostic)};
        *diagnostic = empty;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    RizFreshSourceFailureV2 value{};
    value.version = 2; value.size = sizeof(value);
    const auto result = ReadFreshSourceCore(raw, output, &value);
    value.status = result;
    // V1 output remains undefined on rejection. Only this independently valid
    // diagnostic record may be consumed when the original read status is nonzero.
    __try { *diagnostic = value; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ClaimFreshSource(void* raw) {
    auto* receipt = RegisteredFreshSource(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    auto result = receipt->coordinate(receipt->sourceOwner, receipt->playerWatch, 4, FreshSourceJointCommit, receipt);
    if (!result) result = VerifyFreshSourceLocked(receipt, 3);
    if (result && result != 8 && result != 10 && result != 2) receipt->phase = 4;
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReleaseFreshSource(void* raw) {
    auto* receipt = RegisteredFreshSource(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    auto result = receipt->coordinate(receipt->sourceOwner, receipt->playerWatch, 4, FreshSourceJointCommit, receipt);
    if (!result) result = state->freshSourceCapture != receipt ? 6u :
        (!FreshClockTransferred(state) && Read(&state->authorityActive)) || FreshClockBlocksRelease(state) ? 2u : Read64(&state->inFlight) ? 8u : 0u;
    // This revokes only the epoch ticket. It does not certify Source drain or
    // watch retirement and never permits managed roots to be released alone.
    if (!result) receipt->phase = 3;
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
