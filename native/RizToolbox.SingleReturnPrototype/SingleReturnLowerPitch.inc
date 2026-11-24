// Additive lower-pitch retry coordinator. A caller's fresh pointer is never a
// completion fact: only the bound source verifies the actual old/new records.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryLowerPitchInitialCapture(
    void* raw, void* initializer, void** output) {
    auto* prior = RegisteredInitialCapture(raw);
    if (!prior || !initializer || !output) return 1;
    *output = nullptr; auto* state = prior->state;
    AcquireSRWLockExclusive(&state->authorityGate); std::uint32_t result = 0;
    if (!state->lowerPitchInitializationRetry || state->initialCapture != prior || (prior->phase != 1 && prior->phase != 2)
        || state->logical || state->handoff || Published(state) || state->bootstrap || Read(&state->policyClaimed)
        || Read(&state->authorityActive) || Read(&state->authorityClosed) || !Read(&state->logicalBlocked)) result = 2;
    else if (Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
    else {
        auto* receipt = static_cast<InitialCaptureReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(InitialCaptureReceipt)));
        if (!receipt) result = 7;
        else {
            receipt->state = state; InitialCaptureRequest request{receipt, 0, 0, 0, nullptr, prior};
            result = state->lowerPitchInitializationRetry(state->sourceGate,
                reinterpret_cast<void*>(prior->source.initializer), initializer, 14, &request);
            if (!result) {
                AcquireSRWLockExclusive(&initialCaptureRegistryLock); receipt->next = initialCaptureRegistry; initialCaptureRegistry = receipt;
                ReleaseSRWLockExclusive(&initialCaptureRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryLowerPitchInitializationHandoff(
    void* raw, void* initializer, void** output) {
    auto* prior = RegisteredHandoff(raw);
    if (!prior || !initializer || !output) return 1;
    *output = nullptr; auto* state = prior->state;
    AcquireSRWLockExclusive(&state->authorityGate); std::uint32_t result = 0;
    if (!state->lowerPitchInitializationRetry || state->handoff != prior || !prior->initialization
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
            result = state->lowerPitchInitializationRetry(state->sourceGate,
                reinterpret_cast<void*>(prior->source.initializer), initializer, 15, &request);
            if (!result) {
                AcquireSRWLockExclusive(&handoffRegistryLock); receipt->next = handoffRegistry; handoffRegistry = receipt;
                ReleaseSRWLockExclusive(&handoffRegistryLock); *output = receipt;
            }
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
