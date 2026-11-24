// Additive private first-capture V1; this never invents an old policy/session.
namespace {
struct InitialCaptureReceipt {
    State* state; InitialCaptureReceipt* next;
    RizLogicalSourceSnapshot source;
    std::uint64_t epoch; std::uint32_t phase;
};
struct InitialCaptureRequest {
    InitialCaptureReceipt* receipt;
    std::uint64_t player = 0, source = 0, clip = 0;
    LogicalSession* logical = nullptr;
    InitialCaptureReceipt* superseded = nullptr;
};
SRWLOCK initialCaptureRegistryLock = SRWLOCK_INIT;
InitialCaptureReceipt* initialCaptureRegistry = nullptr;
InitialCaptureReceipt* RegisteredInitialCapture(void* raw) {
    InitialCaptureReceipt* found = nullptr;
    AcquireSRWLockShared(&initialCaptureRegistryLock);
    for (auto* value = initialCaptureRegistry; value; value = value->next) if (value == raw) { found = value; break; }
    ReleaseSRWLockShared(&initialCaptureRegistryLock); return found;
}
std::uint32_t InitialCaptureJointCommit(State* state, void* gate, std::uint32_t action,
    const RizLogicalSourceSnapshot* source, void* raw) {
    auto* request = static_cast<InitialCaptureRequest*>(raw);
    auto* receipt = request ? request->receipt : nullptr;
    if (!receipt || receipt->state != state || gate != state->sourceGate || !source
        || source->owner != reinterpret_cast<std::uint64_t>(state->freshOwner)) return 1;
    if (Read(&state->unknown) || Read(&state->physicalClosed) || Read(&state->configured) != 1) return 3;
    if (action == 12 || action == 14 || action == 16) {
        auto* prior = request->superseded;
        if (!prior || prior == receipt || prior->state != state || state->initialCapture != prior
            || (prior->phase != 1 && prior->phase != 2) || state->logical || state->handoff
            || Published(state) || state->bootstrap || Read(&state->policyClaimed)
            || Read(&state->authorityActive) || Read(&state->authorityClosed) || !Read(&state->logicalBlocked)
            || source->physicalEpoch != prior->source.physicalEpoch
            || !source->initializer || source->initializer == prior->source.initializer
            || !source->initializedPlayer || !source->initializedSource || !source->initializedClip) return 6;
        // A valid audio retry keeps the exact prior receipt until both actual
        // entry gates drain. Identity failures above remain hard rejections.
        if (source->inFlight || Read64(&state->inFlight)) return action == 16 ? 8u : 6u;
        prior->phase = 6;
        receipt->source = *source; receipt->phase = 1; state->initialCapture = receipt;
        return 0;
    }
    if (action == 7) {
        if (state->initialCapture || state->logical || state->handoff || Published(state) || state->bootstrap
            || Read(&state->policyClaimed) || Read(&state->authorityActive) || Read(&state->authorityClosed)
            || !source->initializer || !source->initializedPlayer || !source->initializedSource || !source->initializedClip) return 5;
        receipt->source = *source; receipt->phase = 1; state->initialCapture = receipt;
        InterlockedExchange(&state->logicalBlocked, 1); return 0;
    }
    if (state->initialCapture != receipt || source->inFlight || Read64(&state->inFlight)) return 8;
    if (source->initializer != receipt->source.initializer || source->owner != receipt->source.owner
        || source->physicalEpoch != receipt->source.physicalEpoch
        || source->initializedPlayer != receipt->source.initializedPlayer
        || source->initializedSource != receipt->source.initializedSource
        || source->initializedClip != receipt->source.initializedClip) return 6;
    if (action == 11) {
        if (receipt->phase != 3 || state->logical || state->handoff || !request->logical
            || !state->bootstrap || state->bootstrap->phase != RizMusicClockArmed || !Published(state)
            || Published(state) != state->bootstrap->initialPolicy || !Read(&state->authorityActive)
            || Read(&state->logicalBlocked) || Read(&state->authorityClosed)) return 5;
        // The first real publication has armed. Adopt its actual retained joint
        // owner for subsequent normal InitV2/cross-run transfers without rebinding.
        state->logical = request->logical; receipt->phase = 4; receipt->source = *source; return 0;
    }
    if (Published(state) || state->bootstrap || Read(&state->policyClaimed) || state->logical || state->handoff
        || Read(&state->authorityActive) || Read(&state->authorityClosed) || !Read(&state->logicalBlocked)) return 5;
    if (action == 8) {
        if (receipt->phase != 1) return 2;
        const auto epoch = NextBootstrapEpoch(); if (!epoch) return 7;
        receipt->epoch = epoch; receipt->source = *source; receipt->phase = 2; return 0;
    }
    if (action == 9) {
        if (receipt->phase != 2 || request->player != source->initializedPlayer
            || request->source != source->initializedSource || request->clip != source->initializedClip) return 6;
        return 0;
    }
    if (action == 10) {
        if (receipt->phase != 2) return 2;
        receipt->source = *source; receipt->phase = 3;
        InterlockedExchange(&state->logicalBlocked, 0); return 0;
    }
    return 1;
}
std::uint32_t FinalizeInitialCaptureBinding(State* state, void* library, void* owner) {
    if (!library || owner != state->freshOwner || !state->initialCapture || state->initialCapture->phase != 3
        || !state->sourceGate || !state->sourceAction) return 2;
    // Recheck the exact library's exported coordinator rather than accepting a
    // same-address caller's request to replace the established native gate.
    if (reinterpret_cast<RizLogicalAction>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceLogicalAction")) != state->sourceAction) return 6;
    auto* session = static_cast<LogicalSession*>(HeapAlloc(GetProcessHeap(), 0, sizeof(LogicalSession)));
    const auto epoch = NextBootstrapEpoch(); if (!session || !epoch) return 7;
    session->epoch = epoch;
    InitialCaptureRequest request{state->initialCapture, 0, 0, 0, session};
    return state->sourceAction(state->sourceGate, 11, &request);
}
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_InitialCaptureVersion() { return 1; }
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BindInitialCaptureSource(std::uint32_t slot, void* library, void* owner) {
    if (slot >= limit || !library || !owner) return 1;
    auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    if (state->freshSourceCapture || state->sourceGate || state->initialCapture || state->logical || state->handoff || Published(state) || state->bootstrap
        || Read(&state->policyClaimed) || Read(&state->authorityClosed) || Read(&state->authorityActive)
        || state->freshOwner != owner) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed) || Read(&state->configured) != 1) result = 3;
    else {
        const auto bind = reinterpret_cast<RizLogicalBind>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceBindLogicalGate"));
        const auto action = reinterpret_cast<RizLogicalAction>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceLogicalAction"));
        const auto initialize = reinterpret_cast<RizPlaybackInitializationBegin>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceBeginInitialCapture"));
        const auto handoff = reinterpret_cast<RizPlaybackInitializationBegin>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceBeginInitializationHandoff"));
        const auto version = reinterpret_cast<std::uint32_t(__cdecl*)()>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackInitialCaptureVersion"));
        if (!bind || !action || !initialize || !handoff || !version || version() != 1) result = 7;
        else {
            auto* gate = bind(owner, JointCommit, state);
            if (!gate) result = 6;
            else { state->sourceGate = gate; state->sourceAction = action; state->initialCaptureBegin = initialize; state->initializationBegin = handoff;
                state->resourceInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryResourceInitialization"));
                state->backingInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryBackingInitialization"));
                state->lowerPitchInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryLowerPitchInitialization"));
                state->audioInterruptionRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryAudioInterruptedInitialization")); }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginInitialCapture(std::uint32_t slot, void* initializer, void** output) {
    if (slot >= limit || !initializer || !output) return 1;
    *output = nullptr; auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    if (!state->sourceGate || !state->initialCaptureBegin || state->initialCapture || state->logical || state->handoff
        || Published(state) || state->bootstrap || Read(&state->policyClaimed)) result = 2;
    else {
        auto* receipt = static_cast<InitialCaptureReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(InitialCaptureReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; InitialCaptureRequest request{receipt};
            result = state->initialCaptureBegin(state->sourceGate, initializer, &request);
            if (!result) {
                AcquireSRWLockExclusive(&initialCaptureRegistryLock); receipt->next = initialCaptureRegistry; initialCaptureRegistry = receipt;
                ReleaseSRWLockExclusive(&initialCaptureRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ActInitialCapture(void* raw, std::uint32_t action,
    void* sourceOwner, std::uint64_t player, std::uint64_t source, std::uint64_t clip) {
    auto* receipt = RegisteredInitialCapture(raw);
    if (!receipt || action < 8 || action > 10 || receipt->state->freshOwner != sourceOwner) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    InitialCaptureRequest request{receipt, player, source, clip};
    const auto result = state->sourceAction(state->sourceGate, action, &request);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadInitialCapture(void* raw, RizPlaybackInitialCaptureInfoV1* info) {
    auto* receipt = RegisteredInitialCapture(raw);
    if (!receipt || !info || info->version != 1 || info->size != sizeof(*info)) return 1;
    auto* state = receipt->state; AcquireSRWLockShared(&state->authorityGate);
    const auto& source = receipt->source;
    const auto result = Read(&state->unknown) || Read(&state->physicalClosed) ? 3u : 0u;
    if (!result) *info = RizPlaybackInitialCaptureInfoV1{1, sizeof(*info), receipt->phase, 0, receipt,
        receipt->epoch, Read64(&state->inFlight), source.inFlight, source.owner, source.physicalEpoch,
        source.writer, source.source, source.scheduling, source.initializer, source.initializedPlayer, source.initializedSource, source.initializedClip};
    ReleaseSRWLockShared(&state->authorityGate); return result;
}

RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryResourceInitialCapture(
    void* raw, void* initializer, void** output) {
    auto* prior = RegisteredInitialCapture(raw);
    if (!prior || !initializer || !output) return 1;
    *output = nullptr; auto* state = prior->state;
    AcquireSRWLockExclusive(&state->authorityGate); std::uint32_t result = 0;
    if (!state->resourceInitializationRetry || state->initialCapture != prior || (prior->phase != 1 && prior->phase != 2)
        || state->logical || state->handoff || Published(state) || state->bootstrap || Read(&state->policyClaimed)
        || Read(&state->authorityActive) || Read(&state->authorityClosed) || !Read(&state->logicalBlocked)) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
    else {
        auto* receipt = static_cast<InitialCaptureReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(InitialCaptureReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; InitialCaptureRequest request{receipt, 0, 0, 0, nullptr, prior};
            result = state->resourceInitializationRetry(state->sourceGate,
                reinterpret_cast<void*>(prior->source.initializer), initializer, 12, &request);
            if (!result) {
                AcquireSRWLockExclusive(&initialCaptureRegistryLock); receipt->next = initialCaptureRegistry; initialCaptureRegistry = receipt;
                ReleaseSRWLockExclusive(&initialCaptureRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}

// Same coordinator transition as the resource retry, with an independently
// bound source export that must prove the exact staged backing's interruption.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryBackingInitialCapture(
    void* raw, void* initializer, void** output) {
    auto* prior = RegisteredInitialCapture(raw);
    if (!prior || !initializer || !output) return 1;
    *output = nullptr; auto* state = prior->state;
    AcquireSRWLockExclusive(&state->authorityGate); std::uint32_t result = 0;
    if (!state->backingInitializationRetry || state->initialCapture != prior || (prior->phase != 1 && prior->phase != 2)
        || state->logical || state->handoff || Published(state) || state->bootstrap || Read(&state->policyClaimed)
        || Read(&state->authorityActive) || Read(&state->authorityClosed) || !Read(&state->logicalBlocked)) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
    else {
        auto* receipt = static_cast<InitialCaptureReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(InitialCaptureReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; InitialCaptureRequest request{receipt, 0, 0, 0, nullptr, prior};
            result = state->backingInitializationRetry(state->sourceGate,
                reinterpret_cast<void*>(prior->source.initializer), initializer, 12, &request);
            if (!result) {
                AcquireSRWLockExclusive(&initialCaptureRegistryLock); receipt->next = initialCaptureRegistry; initialCaptureRegistry = receipt;
                ReleaseSRWLockExclusive(&initialCaptureRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
