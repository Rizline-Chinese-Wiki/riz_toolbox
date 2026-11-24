// Included in the Single helper TU; never linked into the production V7 helper.
namespace {
struct LogicalSession { std::uint64_t epoch; };
std::uint32_t InitialCaptureJointCommit(State*, void*, std::uint32_t, const RizLogicalSourceSnapshot*, void*);
std::uint32_t FinalizeInitialCaptureBinding(State*, void*, void*);
struct HandoffReceipt {
    State* state; HandoffReceipt* next;
    LogicalSession* oldSession; LogicalSession* newSession;
    const RizMusicClockPolicyV1* oldPolicy; const RizMusicClockPolicyV1* newPolicy;
    RizLogicalSourceSnapshot source;
    std::uint64_t revision; std::uint32_t phase;
    bool initialization;
    bool crossRunInitialization = false;
    FreshClockReceipt* freshPredecessor = nullptr;
};
std::uint32_t CommitFreshClockPredecessor(State*, HandoffReceipt*, const RizLogicalSourceSnapshot*);
struct JointRequest {
    HandoffReceipt* receipt; const RizMusicClockPolicyV1* policy;
    std::uint64_t capturePlayer = 0, captureSource = 0, captureClip = 0;
    HandoffReceipt* superseded = nullptr;
};
SRWLOCK handoffRegistryLock = SRWLOCK_INIT;
HandoffReceipt* handoffRegistry = nullptr;
HandoffReceipt* RegisteredHandoff(void* raw) {
    HandoffReceipt* found = nullptr;
    AcquireSRWLockShared(&handoffRegistryLock);
    for (auto* item = handoffRegistry; item; item = item->next) if (item == raw) { found = item; break; }
    ReleaseSRWLockShared(&handoffRegistryLock); return found;
}
std::uint32_t __cdecl JointCommit(void* context, void* gate, std::uint32_t action,
    const RizLogicalSourceSnapshot* source, void* raw) {
    auto* state = static_cast<State*>(context); auto* request = static_cast<JointRequest*>(raw);
    if (action >= 7 && action != 13 && action != 15 && action != 17) return InitialCaptureJointCommit(state, gate, action, source, raw);
    if (!request) return 1;
    auto* receipt = request->receipt;
    // Called only synchronously by the bound helper while both actual entry
    // gates are exclusive. Neither callback performs a wait or calls managed code.
    if (!receipt || receipt->state != state || gate != state->sourceGate || !source) return 1;
    if (Read(&state->unknown) || Read(&state->physicalClosed) || Read(&state->configured) != 1) return 3;
    if (action == 13 || action == 15 || action == 17) {
        const auto* prior = request->superseded;
        // Source has proved this exact prior initializer was interrupted only
        // by a resource write and that the new actual initializer is complete.
        // A replacement gets a fresh opaque object; the old graph never changes.
        if (!prior || prior == receipt || state->handoff != prior || prior->state != state
            || !prior->initialization || (prior->phase != 1 && prior->phase != 2)
            || receipt->crossRunInitialization != prior->crossRunInitialization
            || receipt->oldSession != state->logical || receipt->oldPolicy != prior->oldPolicy
            || Published(state) != prior->oldPolicy || prior->newPolicy
            || !Read(&state->logicalBlocked) || !Read(&state->authorityClosed) || Read(&state->authorityActive)
            || source->owner != reinterpret_cast<std::uint64_t>(state->freshOwner)
            || source->physicalEpoch != prior->source.physicalEpoch
            || !source->initializer || source->initializer == prior->source.initializer
            || !source->initializedPlayer || !source->initializedSource || !source->initializedClip) return 6;
        // A valid audio retry keeps the exact prior receipt until both actual
        // entry gates drain. Identity failures above remain hard rejections.
        if (source->inFlight || Read64(&state->inFlight)) return action == 17 ? 8u : 6u;
        request->superseded->phase = 6; // permanent tombstone, never accepted by any action
        receipt->initialization = true; receipt->source = *source; receipt->phase = 1;
        state->handoff = receipt; return 0;
    }
    if (action == 0 || action == 5) {
        if (state->handoff || state->logical != receipt->oldSession || Published(state) != receipt->oldPolicy) return 5;
        if (action == 5 && (!source->initializer || !source->initializedPlayer || !source->initializedSource
            || !source->initializedClip || source->owner != reinterpret_cast<std::uint64_t>(state->freshOwner))) return 6;
        const auto freshResult = CommitFreshClockPredecessor(state, receipt, source);
        if (freshResult) return freshResult;
        receipt->initialization = action == 5;
        InterlockedExchange(&state->logicalBlocked, 1); state->handoff = receipt; receipt->phase = 1; receipt->source = *source;
        InterlockedExchange(&state->authorityActive, 0); InterlockedExchange(&state->authorityClosed, 1);
        InterlockedIncrement64(&state->withdrawals); return 0;
    }
    if (state->handoff != receipt || source->inFlight != 0 || Read64(&state->inFlight) != 0) return 8;
    if (action == 6) {
        if (!receipt->initialization || receipt->phase != 2 || state->logical != receipt->newSession
            || source->initializer != receipt->source.initializer || !Read(&state->logicalBlocked)
            || Published(state) != receipt->oldPolicy || Read(&state->authorityActive)
            || request->capturePlayer != source->initializedPlayer || request->captureSource != source->initializedSource
            || request->captureClip != source->initializedClip) return 6;
        return 0;
    }
    if (action == 1) {
        if (receipt->phase != 1 || state->logical != receipt->oldSession || Published(state) != receipt->oldPolicy
            || Read(&state->authorityActive) != 0 || receipt->oldPolicy->policyGeneration == (std::numeric_limits<std::uint64_t>::max)()) return 5;
        auto* next = static_cast<LogicalSession*>(HeapAlloc(GetProcessHeap(), 0, sizeof(LogicalSession)));
        const auto epoch = NextBootstrapEpoch();
        if (!next || !epoch) return 7; // allocation retained, never reused
        next->epoch = epoch; receipt->newSession = next; receipt->revision = receipt->oldPolicy->policyGeneration + 1;
        receipt->source = *source; receipt->phase = 2; state->logical = next; return 0;
    }
    if (state->logical != receipt->newSession) return 5;
    if (action == 4) {
        if (receipt->phase != 2 || Published(state) != receipt->oldPolicy
            || Read(&state->authorityActive) || !Read(&state->logicalBlocked)) return 5;
        receipt->phase = 3; receipt->source = *source; return 0;
    }
    if (action == 2) {
        const auto* policy = request->policy;
        if (receipt->phase != 3 || Published(state) != receipt->oldPolicy || !policy || !ValidInitialPolicy(*policy)
            || policy->policyGeneration != receipt->revision || policy->method != receipt->oldPolicy->method
            || policy->readClock != receipt->oldPolicy->readClock || policy->clockOwner != receipt->oldPolicy->clockOwner
            || policy->permittedNullCaller != receipt->oldPolicy->permittedNullCaller
            || (receipt->initialization
                ? (receipt->crossRunInitialization
                    ? policy->runHigh == receipt->oldPolicy->runHigh && policy->runLow == receipt->oldPolicy->runLow
                    : policy->runHigh != receipt->oldPolicy->runHigh || policy->runLow != receipt->oldPolicy->runLow)
                    || policy->self != reinterpret_cast<void*>(receipt->source.initializedPlayer)
                : policy->runHigh == receipt->oldPolicy->runHigh && policy->runLow == receipt->oldPolicy->runLow)) return 6;
        auto* node = static_cast<State::PolicyNode*>(HeapAlloc(GetProcessHeap(), 0, sizeof(State::PolicyNode)));
        if (!node) return 7;
        node->value = *policy; node->previous = state->retainedPolicies; state->retainedPolicies = node;
        receipt->newPolicy = &node->value; receipt->phase = 4; receipt->source = *source;
        InterlockedExchangePointer(&state->publishedPolicy, &node->value);
        // Only the logical closure belonged to the retired session. physicalClosed
        // and Unknown are never cleared by a transfer, prepare, or Arm.
        InterlockedExchange(&state->authorityClosed, 0); return 0;
    }
    if (action == 3) {
        if (receipt->phase != 4 || Published(state) != receipt->newPolicy || Read(&state->authorityClosed)
            || Read(&state->authorityActive)) return 5;
        InterlockedExchange(&state->logicalBlocked, 0); receipt->phase = 5; receipt->source = *source; state->handoff = nullptr;
        InterlockedExchange(&state->authorityActive, 1); return 0;
    }
    return 1;
}
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BindLogicalSource(std::uint32_t slot, void* library, void* owner) {
    if (slot >= limit || !library || !owner) return 1;
    auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    if (state->freshSourceCapture) result = 2; // First FreshClock has no successor transfer protocol yet.
    else if (state->initialCapture) result = FinalizeInitialCaptureBinding(state, library, owner);
    else if (state->sourceGate || !Published(state) || !state->bootstrap || state->bootstrap->phase != RizMusicClockArmed) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed) || Read(&state->configured) != 1) result = 3;
    else {
        const auto bind = reinterpret_cast<RizLogicalBind>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceBindLogicalGate"));
        const auto action = reinterpret_cast<RizLogicalAction>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceLogicalAction"));
        auto* session = static_cast<LogicalSession*>(HeapAlloc(GetProcessHeap(), 0, sizeof(LogicalSession)));
        const auto epoch = NextBootstrapEpoch();
        const auto initialize = reinterpret_cast<RizPlaybackInitializationBegin>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceBeginInitializationHandoff"));
        if (!bind || !action || !initialize || !session || !epoch) result = 7;
        else {
            session->epoch = epoch; auto* gate = bind(owner, JointCommit, state);
            if (!gate) result = 6;
            else { state->sourceGate = gate; state->sourceAction = action; state->initializationBegin = initialize; state->logical = session;
                state->resourceInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryResourceInitialization"));
                state->backingInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryBackingInitialization"));
                state->lowerPitchInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryLowerPitchInitialization"));
                state->audioInterruptionRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryAudioInterruptedInitialization")); }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginLogicalHandoff(std::uint32_t slot,
    std::uint64_t expected, void** output) {
    if (slot >= limit || !expected || !output) return 1;
    *output = nullptr; auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0; auto* current = Published(state);
    if (state->freshClock && !FreshClockTransferred(state)) result = 2;
    else if (!state->sourceGate || !state->logical || state->handoff) result = 2;
    else if (!current || current->policyGeneration != expected) result = 5;
    else {
        auto* receipt = static_cast<HandoffReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(HandoffReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; receipt->oldSession = state->logical; receipt->oldPolicy = current;
            JointRequest request{receipt, nullptr}; result = state->sourceAction(state->sourceGate, 0, &request);
            if (result == 0) {
                AcquireSRWLockExclusive(&handoffRegistryLock); receipt->next = handoffRegistry; handoffRegistry = receipt;
                ReleaseSRWLockExclusive(&handoffRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_AdmitLogicalHandoff(void* raw) {
    auto* receipt = RegisteredHandoff(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    JointRequest request{receipt, nullptr}; const auto result = state->sourceAction(state->sourceGate, 1, &request);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PrepareTransferredClock(void* raw, const RizMusicClockPolicyV1* policy) {
    auto* receipt = RegisteredHandoff(raw); if (!receipt || !policy) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    JointRequest request{receipt, policy}; const auto result = state->sourceAction(state->sourceGate, 2, &request);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_OpenTransferredSource(void* raw) {
    auto* receipt = RegisteredHandoff(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    JointRequest request{receipt, nullptr}; const auto result = state->sourceAction(state->sourceGate, 4, &request);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ArmTransferredClock(void* raw) {
    auto* receipt = RegisteredHandoff(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    JointRequest request{receipt, nullptr}; const auto result = state->sourceAction(state->sourceGate, 3, &request);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadLogicalHandoff(void* raw, RizLogicalHandoffInfoV1* info) {
    auto* receipt = RegisteredHandoff(raw); if (!receipt || !info || info->version != 1 || info->size != sizeof(*info)) return 1;
    auto* state = receipt->state; AcquireSRWLockShared(&state->authorityGate);
    *info = RizLogicalHandoffInfoV1{1, sizeof(*info), receipt->phase, 0, receipt, receipt->oldSession->epoch,
        receipt->newSession ? receipt->newSession->epoch : 0, receipt->oldPolicy->policyGeneration, receipt->revision,
        Published(state) ? Published(state)->policyGeneration : 0, Read64(&state->inFlight), receipt->source.inFlight,
        receipt->source.writer, receipt->source.source, receipt->source.scheduling};
    ReleaseSRWLockShared(&state->authorityGate); return 0;
}

namespace {
std::uint32_t BeginInitializationHandoff(std::uint32_t slot,
    std::uint64_t expected, void* initializer, void** output, bool crossRun) {
    if (slot >= limit || !expected || !initializer || !output) return 1;
    *output = nullptr; auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0; auto* current = Published(state);
    if (state->freshClock && !FreshClockTransferred(state)) result = 2;
    else if (!state->sourceGate || !state->initializationBegin || !state->logical || state->handoff) result = 2;
    else if (!current || current->policyGeneration != expected) result = 5;
    else {
        auto* receipt = static_cast<HandoffReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(HandoffReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; receipt->oldSession = state->logical; receipt->oldPolicy = current;
            receipt->crossRunInitialization = crossRun;
            JointRequest request{receipt, nullptr};
            result = state->initializationBegin(state->sourceGate, initializer, &request);
            if (!result) {
                AcquireSRWLockExclusive(&handoffRegistryLock); receipt->next = handoffRegistry; handoffRegistry = receipt;
                ReleaseSRWLockExclusive(&handoffRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginInitializationHandoff(std::uint32_t slot,
    std::uint64_t expected, void* initializer, void** output) {
    return BeginInitializationHandoff(slot, expected, initializer, output, false);
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginCrossRunInitializationHandoff(std::uint32_t slot,
    std::uint64_t expected, void* initializer, void** output) {
    return BeginInitializationHandoff(slot, expected, initializer, output, true);
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_VerifyInitializationCapture(void* raw,
    void* sourceOwner, std::uint64_t player, std::uint64_t source, std::uint64_t clip) {
    auto* receipt = RegisteredHandoff(raw);
    if (!receipt || !receipt->initialization || receipt->state->freshOwner != sourceOwner) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    JointRequest request{receipt, nullptr, player, source, clip};
    const auto result = state->sourceAction(state->sourceGate, 6, &request);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}

RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryResourceInitializationHandoff(
    void* raw, void* initializer, void** output) {
    auto* prior = RegisteredHandoff(raw);
    if (!prior || !initializer || !output) return 1;
    *output = nullptr; auto* state = prior->state;
    AcquireSRWLockExclusive(&state->authorityGate); std::uint32_t result = 0;
    if (!state->resourceInitializationRetry || state->handoff != prior || !prior->initialization
        || (prior->phase != 1 && prior->phase != 2) || !state->logical || prior->newPolicy
        || Published(state) != prior->oldPolicy || !Read(&state->logicalBlocked)
        || !Read(&state->authorityClosed) || Read(&state->authorityActive)) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
    else {
        auto* receipt = static_cast<HandoffReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(HandoffReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; receipt->oldSession = state->logical; receipt->oldPolicy = prior->oldPolicy;
            receipt->crossRunInitialization = prior->crossRunInitialization;
            JointRequest request{receipt, nullptr, 0, 0, 0, prior};
            result = state->resourceInitializationRetry(state->sourceGate,
                reinterpret_cast<void*>(prior->source.initializer), initializer, 13, &request);
            if (!result) {
                AcquireSRWLockExclusive(&handoffRegistryLock); receipt->next = handoffRegistry; handoffRegistry = receipt;
                ReleaseSRWLockExclusive(&handoffRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}

RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryBackingInitializationHandoff(
    void* raw, void* initializer, void** output) {
    auto* prior = RegisteredHandoff(raw);
    if (!prior || !initializer || !output) return 1;
    *output = nullptr; auto* state = prior->state;
    AcquireSRWLockExclusive(&state->authorityGate); std::uint32_t result = 0;
    if (!state->backingInitializationRetry || state->handoff != prior || !prior->initialization
        || (prior->phase != 1 && prior->phase != 2) || !state->logical || prior->newPolicy
        || Published(state) != prior->oldPolicy || !Read(&state->logicalBlocked)
        || !Read(&state->authorityClosed) || Read(&state->authorityActive)) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
    else {
        auto* receipt = static_cast<HandoffReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(HandoffReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; receipt->oldSession = state->logical; receipt->oldPolicy = prior->oldPolicy;
            receipt->crossRunInitialization = prior->crossRunInitialization;
            JointRequest request{receipt, nullptr, 0, 0, 0, prior};
            result = state->backingInitializationRetry(state->sourceGate,
                reinterpret_cast<void*>(prior->source.initializer), initializer, 13, &request);
            if (!result) {
                AcquireSRWLockExclusive(&handoffRegistryLock); receipt->next = handoffRegistry; handoffRegistry = receipt;
                ReleaseSRWLockExclusive(&handoffRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
