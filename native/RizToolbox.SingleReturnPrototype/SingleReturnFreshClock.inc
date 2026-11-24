// One initial Clock belongs to the original claimed FreshSource ticket. Policy
// bytes and journal selectors carry data; only these retained records own it.
namespace {
struct FreshClockReceipt {
    State* state;
    FreshSourceReceipt* source;
    RizFreshClockBaselineV1 baseline;
    RizMusicClockPolicyV1 initial;
    const RizMusicClockPolicyV1* current;
    LogicalSession* logical;
    std::uint32_t phase; // 0 preparing, 1 dormant, 2 armed, 3 closed, 4 transferred
};
void* volatile freshClockRegistry[limit]{};
FreshClockReceipt* RegisteredFreshClock(void* raw) {
    if (!raw) return nullptr;
    for (auto& slot : freshClockRegistry) {
        auto* value = static_cast<FreshClockReceipt*>(InterlockedCompareExchangePointer(&slot, nullptr, nullptr));
        if (raw == value) return value;
    }
    return nullptr;
}
bool FreshClockTransferred(State* state) { return state->freshClock && state->freshClock->phase == 4; }
bool FreshClockBlocksRelease(State* state) { return state->freshClock && state->freshClock->phase != 3 && state->freshClock->phase != 4; }
std::uint32_t FreshClockJointCommit(void* raw, std::uint32_t action, const RizFreshSourceSnapshotV1* snapshot, void* gate) {
    auto* receipt = static_cast<FreshClockReceipt*>(raw);
    auto* state = receipt->state; auto* source = receipt->source;
    if (!snapshot || state->freshSourceCapture != source || source->phase != 2 || !source->epoch
        || source->state != state || source->sourceOwner != state->freshOwner
        || !SameFreshLifetime(source->source, *snapshot)) return 6;
    if (Read(&state->configured) != 1 || Read(&state->unknown) || Read(&state->physicalClosed)) return 3;
    if (Read(&state->authorityClosed) || Read(&state->logicalBlocked)) return 4;
    if (action != 2 && Read64(&state->inFlight)) return 8;
    if (action == 1) {
        if (receipt->phase || state->freshClock || state->bootstrap || state->logical || state->handoff
            || state->initialCapture || state->sourceGate || Published(state) || Read(&state->policyClaimed)
            || Read(&state->authorityActive) || Read(&state->observerEnabled) != 1) return 2;
        if (receipt->initial.self != reinterpret_cast<void*>(snapshot->player)) return 6;
        state->freshClock = receipt; receipt->phase = 1; receipt->current = &receipt->initial;
        InterlockedExchangePointer(&state->publishedPolicy, &receipt->initial);
        InterlockedExchange(&state->policyClaimed, 1);
        InterlockedExchange(&state->authorityActive, 0);
        InterlockedExchangePointer(&freshClockRegistry[source->slot], receipt);
        return 0;
    }
    if (state->freshClock != receipt || (receipt->phase != 1 && receipt->phase != 2)
        || Read(&state->policyClaimed) != 1 || !Published(state)) return 2;
    if (action == 3) {
        if (receipt->phase != 1 || Published(state) != &receipt->initial || Read(&state->authorityActive)) return 2;
        if (!gate || state->sourceGate || state->logical || !state->sourceAction || !state->initializationBegin
            || !state->resourceInitializationRetry || !state->backingInitializationRetry
            || !state->lowerPitchInitializationRetry || !state->audioInterruptionRetry) return 6;
        auto* session = static_cast<LogicalSession*>(HeapAlloc(GetProcessHeap(), 0, sizeof(LogicalSession)));
        if (!session) return 7;
        // The initial source already owns its identity. Arm retains that exact
        // epoch as the logical predecessor; it does not mint a bootstrap.
        session->epoch = source->epoch; receipt->logical = session;
        state->logical = session; state->sourceGate = gate;
        receipt->phase = 2; InterlockedExchange(&state->authorityActive, 1);
    } else if (action == 4 && (receipt->phase != 2 || Read(&state->authorityActive) != 1)) return 2;
    return 0;
}
std::uint32_t EnterFreshClock(FreshClockReceipt* receipt, std::uint32_t action) {
    auto* state = receipt->state;
    if (!state->freshClockEnter || !state->freshLeave) return 6;
    return state->freshClockEnter(state->freshOwner, receipt->source->playerWatch,
        action == 1 || action == 3 ? &receipt->baseline : nullptr, action, FreshClockJointCommit, receipt,
        action == 3 ? JointCommit : nullptr, action == 3 ? state : nullptr);
}
bool FreshClockFresh(State* state) {
    const auto result = EnterFreshClock(state->freshClock, 2);
    if (result && result != 8 && result != 10) {
        InterlockedExchange(&state->authorityActive, 0);
        InterlockedExchange(&state->authorityClosed, 1);
        InterlockedExchange(&state->logicalBlocked, 1);
        if (result == 3) InterlockedExchange(&state->unknown, 1);
    }
    return result == 0; // Holds Source.Owner.Lock exactly once until LeaveFresh.
}
bool SameClockIdentity(const RizMusicClockPolicyV1& a, const RizMusicClockPolicyV1& b) {
    return a.self == b.self && a.method == b.method && a.runHigh == b.runHigh && a.runLow == b.runLow
        && a.controllerGeneration == b.controllerGeneration && a.readClock == b.readClock
        && a.clockOwner == b.clockOwner && a.permittedNullCaller == b.permittedNullCaller;
}
std::uint32_t FreshClockCleanupThread(FreshClockReceipt* receipt) {
    auto* source = receipt->source;
    // Action 4 is the existing owner-thread check and does not require the old
    // graph to remain live. Cleanup must work after native lifetime revocation.
    return source->coordinate(source->sourceOwner, source->playerWatch, 4, FreshSourceJointCommit, source);
}
std::uint32_t CommitFreshClockPredecessor(State* state, HandoffReceipt* handoff, const RizLogicalSourceSnapshot* source) {
    auto* fresh = state->freshClock;
    if (!fresh || FreshClockTransferred(state)) return handoff->freshPredecessor ? 2u : 0u;
    // A previously armed but normally withdrawn source can already have
    // retired its roots. Its retained native logical record is the predecessor;
    // the source helper independently proves the actual handoff/initializer.
    if (handoff->freshPredecessor != fresh || (fresh->phase != 2 && fresh->phase != 3)
        || !fresh->logical || state->logical != fresh->logical || handoff->oldSession != fresh->logical
        || fresh->logical->epoch != fresh->source->epoch || Published(state) != fresh->current
        || handoff->oldPolicy != fresh->current || source->owner != reinterpret_cast<std::uint64_t>(fresh->source->sourceOwner)
        || source->physicalEpoch != fresh->source->source.ownerEpoch) return 6;
    fresh->phase = 4; return 0;
}
std::uint32_t BeginFreshClockHandoff(FreshClockReceipt* receipt, std::uint64_t expected, void* initializer, void** output,
    bool crossRunInitialization = false) {
    if (!receipt || !expected || !output) return 1;
    __try { *output = nullptr; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    auto result = FreshClockCleanupThread(receipt);
    if (!result) {
        if (state->freshClock != receipt || (receipt->phase != 2 && receipt->phase != 3)
            || !receipt->logical || state->logical != receipt->logical || !state->sourceGate
            || !state->sourceAction || !state->initializationBegin || state->handoff) result = 2;
        else if (!Published(state) || Published(state) != receipt->current || receipt->current->policyGeneration != expected) result = 5;
        else if (Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
        else {
            auto* handoff = static_cast<HandoffReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(HandoffReceipt)));
            if (!handoff) result = 7;
            else {
                handoff->state = state; handoff->oldSession = receipt->logical;
                handoff->oldPolicy = receipt->current; handoff->freshPredecessor = receipt;
                handoff->crossRunInitialization = crossRunInitialization;
                JointRequest request{handoff, nullptr};
                result = initializer ? state->initializationBegin(state->sourceGate, initializer, &request)
                    : state->sourceAction(state->sourceGate, 0, &request);
                if (!result) {
                    AcquireSRWLockExclusive(&handoffRegistryLock); handoff->next = handoffRegistry; handoffRegistry = handoff;
                    ReleaseSRWLockExclusive(&handoffRegistryLock); *output = handoff;
                } else HeapFree(GetProcessHeap(), 0, handoff); // No successful registration occurred.
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_FreshClockVersion() { return 2; }
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PrepareFreshMusicClock(void* rawSource,
    const RizFreshClockBaselineV1* baseline, const RizMusicClockPolicyV1* policy, void** output) {
    auto* source = RegisteredFreshSource(rawSource);
    if (!source || !baseline || !policy || !output) return 1;
    RizFreshClockBaselineV1 basis{}; RizMusicClockPolicyV1 initial{};
    __try { *output = nullptr; basis = *baseline; initial = *policy; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    if (!ValidInitialPolicy(initial) || initial.phase != RizClockPaused) return 1;
    auto* state = source->state; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    auto* receipt = static_cast<FreshClockReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(FreshClockReceipt)));
    if (!receipt) result = 7;
    else {
        receipt->state = state; receipt->source = source; receipt->baseline = basis; receipt->initial = initial;
        result = EnterFreshClock(receipt, 1);
        if (!result) { state->freshLeave(state->freshOwner); *output = receipt; }
        else HeapFree(GetProcessHeap(), 0, receipt); // Never registered or published.
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadFreshMusicClock(void* raw,
    RizMusicClockBootstrapInfoV1* info) {
    auto* receipt = RegisteredFreshClock(raw);
    if (!receipt || !BootstrapInfoHeader(info)) return 1;
    auto* state = receipt->state; AcquireSRWLockShared(&state->authorityGate);
    auto status = receipt->phase == 3 ? FreshClockCleanupThread(receipt) : EnterFreshClock(receipt, 2);
    if (!status) {
        const auto* current = Published(state);
        const RizMusicClockBootstrapInfoV1 value{1, sizeof(*info), receipt->source->slot, receipt->phase,
            receipt->source->epoch, receipt->initial.policyGeneration, current ? current->policyGeneration : 0,
            Read64(&state->inFlight), receipt, static_cast<std::uint32_t>(Read(&state->unknown)), 0,
            current ? *current : RizMusicClockPolicyV1{}};
        if (!WriteBootstrapInfo(info, &value)) status = 1;
        if (receipt->phase != 3) state->freshLeave(state->freshOwner);
    }
    ReleaseSRWLockShared(&state->authorityGate); return status;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ArmFreshMusicClock(void* raw) {
    auto* receipt = RegisteredFreshClock(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    const auto status = EnterFreshClock(receipt, 3);
    if (!status) state->freshLeave(state->freshOwner);
    ReleaseSRWLockExclusive(&state->authorityGate); return status;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PublishFreshMusicClock(void* raw,
    std::uint64_t expected, const RizMusicClockPolicyV1* policy) {
    auto* receipt = RegisteredFreshClock(raw); if (!receipt || !policy || !expected) return 1;
    RizMusicClockPolicyV1 next{};
    __try { next = *policy; } __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    if (!ValidInitialPolicy(next) || next.policyGeneration <= expected) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    auto status = EnterFreshClock(receipt, 4);
    if (!status) {
        const auto* current = Published(state);
        if (!current || current->policyGeneration != expected) status = 5;
        else if (!SameClockIdentity(*current, next)) status = 6;
        else {
            auto* node = static_cast<State::PolicyNode*>(HeapAlloc(GetProcessHeap(), 0, sizeof(State::PolicyNode)));
            if (!node) status = 7;
            else { node->value = next; node->previous = state->retainedPolicies; state->retainedPolicies = node;
                receipt->current = &node->value; InterlockedExchangePointer(&state->publishedPolicy, &node->value); }
        }
        state->freshLeave(state->freshOwner);
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return status;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_WithdrawFreshMusicClock(void* raw, std::uint64_t expected) {
    auto* receipt = RegisteredFreshClock(raw); if (!receipt || !expected) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    auto status = FreshClockCleanupThread(receipt); const auto* current = Published(state);
    if (!status) {
        if (state->freshClock != receipt || receipt->phase == 0 || receipt->phase == 4) status = 2;
        else if (!current || current->policyGeneration != expected) status = 5;
        else { receipt->phase = 3; InterlockedExchange(&state->authorityActive, 0);
            InterlockedExchange(&state->authorityClosed, 1); InterlockedIncrement64(&state->withdrawals); }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return status;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginFreshMusicClockHandoff(void* raw,
    std::uint64_t expected, void** output) {
    return BeginFreshClockHandoff(RegisteredFreshClock(raw), expected, nullptr, output);
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginFreshMusicClockInitializationHandoff(void* raw,
    std::uint64_t expected, void* initializer, void** output) {
    if (!initializer) return 1;
    return BeginFreshClockHandoff(RegisteredFreshClock(raw), expected, initializer, output);
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BeginFreshMusicClockCrossRunInitializationHandoff(void* raw,
    std::uint64_t expected, void* initializer, void** output) {
    if (!initializer) return 1;
    return BeginFreshClockHandoff(RegisteredFreshClock(raw), expected, initializer, output, true);
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_CancelFreshMusicClock(void* raw) {
    auto* receipt = RegisteredFreshClock(raw); if (!receipt) return 1;
    auto* state = receipt->state; AcquireSRWLockExclusive(&state->authorityGate);
    auto status = FreshClockCleanupThread(receipt);
    if (!status) {
        if (state->freshClock != receipt || receipt->phase != 1 || Published(state) != &receipt->initial) status = 2;
        else { receipt->phase = 3; InterlockedExchange(&state->authorityActive, 0);
            InterlockedExchange(&state->authorityClosed, 1); InterlockedIncrement64(&state->withdrawals); }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return status;
}
