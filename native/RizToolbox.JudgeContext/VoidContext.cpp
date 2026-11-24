#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <limits>
#include <utility>
#include "VoidContext.h"

namespace {
constexpr std::size_t limit = 32;
struct State {
    RizVoidConfigV1 config{};
    volatile LONG configured = 0; // 0 empty, -1 publishing, 1 ready; never reset.
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, enterFaults = 0, exitFaults = 0;
    SRWLOCK admissionLock = SRWLOCK_INIT;
    CONDITION_VARIABLE admissionChanged = CONDITION_VARIABLE_INIT;
    std::uint64_t active = 0, parked = 0, epoch = 0;
    std::uint32_t phase = RizVoidGateOpen;
    DWORD ownerThreadId = 0;
    volatile LONG admissionUnknown = 0;
};
State states[limit];
volatile LONG64 epochSource = 0;
volatile LONG epochExhausted = 0;
thread_local std::uint64_t admittedDepth[limit]{};
thread_local std::uint64_t admittedTotal = 0;

void AdmissionFault(State* state)
{
    InterlockedExchange(&state->admissionUnknown, 1);
}
bool AdmissionKnown(State* state)
{
    return InterlockedCompareExchange(&state->admissionUnknown, 0, 0) == 0;
}
void ReopenUnissued(State* state)
{
    state->phase = RizVoidGateOpen;
    state->ownerThreadId = 0;
    WakeAllConditionVariable(&state->admissionChanged);
}
void Admit(State* state, std::size_t slot)
{
    AcquireSRWLockExclusive(&state->admissionLock);
    bool parked = false;
    while (state->phase != RizVoidGateOpen && admittedDepth[slot] == 0) {
        if (!parked) {
            if (state->parked == (std::numeric_limits<std::uint64_t>::max)()) {
                AdmissionFault(state);
                RaiseFailFastException(nullptr, nullptr, 0);
            }
            ++state->parked; parked = true;
            WakeAllConditionVariable(&state->admissionChanged);
        }
        // No managed callback, object access, allocation or broker lock occurs
        // while parked. Sleep atomically releases/reacquires this native lock.
        if (!SleepConditionVariableSRW(&state->admissionChanged, &state->admissionLock, INFINITE, 0)) {
            AdmissionFault(state);
            // Before a receipt is issued no code-write permission exists. An
            // issued closure must stay closed even after a native wait failure.
            if (state->phase == RizVoidGateClosing) ReopenUnissued(state);
        }
    }
    if (parked) {
        if (state->parked == 0) AdmissionFault(state);
        else --state->parked;
    }
    if (state->active == (std::numeric_limits<std::uint64_t>::max)()
        || admittedDepth[slot] == (std::numeric_limits<std::uint64_t>::max)()
        || admittedTotal == (std::numeric_limits<std::uint64_t>::max)()) {
        AdmissionFault(state);
        RaiseFailFastException(nullptr, nullptr, 0);
    }
    ++state->active; ++admittedDepth[slot]; ++admittedTotal;
    ReleaseSRWLockExclusive(&state->admissionLock);
}
void LeaveAdmission(State* state, std::size_t slot)
{
    AcquireSRWLockExclusive(&state->admissionLock);
    if (state->active == 0 || admittedDepth[slot] == 0 || admittedTotal == 0) AdmissionFault(state);
    else { --state->active; --admittedDepth[slot]; --admittedTotal; }
    WakeAllConditionVariable(&state->admissionChanged);
    ReleaseSRWLockExclusive(&state->admissionLock);
}

std::uint32_t ValidateClosedReceipt(State* state, const RizVoidGateReceiptV1* receipt)
{
    // Called while admissionLock is held. The epoch source is shared by ALL
    // slots, so changing only receipt.slot cannot match another issued gate.
    if (receipt->ownerThreadId != GetCurrentThreadId() || state->ownerThreadId != receipt->ownerThreadId) return 3;
    if (receipt->epoch == 0 || receipt->epoch != state->epoch) return 4;
    if (state->phase != RizVoidGateClosed) return 5;
    if (state->active != 0) return 6;
    if (!AdmissionKnown(state)) return 7;
    return 0;
}

std::uintptr_t Enter(State* state, const RizVoidFrameV1* frame, bool* faulted)
{
    __try { return state->config.enter(frame, state->config.user); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true;
        InterlockedIncrement64(&state->enterFaults);
        return 0;
    }
}
void Exit(State* state, const RizVoidFrameV1* frame, std::uintptr_t token, std::uint32_t flags)
{
    __try { state->config.exit(frame, token, flags, state->config.user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { InterlockedIncrement64(&state->exitFaults); }
}

// POD-only locals preserve native SEH/C++ unwind. No handler surrounds the
// original: it executes once and its exception continues to the native caller.
__declspec(noinline) void Dispatch(State* state, std::size_t slot, const RizVoidFrameV1* frame)
{
    if (InterlockedCompareExchange(&state->configured, 1, 1) != 1)
        RaiseFailFastException(nullptr, nullptr, 0);
    InterlockedIncrement64(&state->inFlight);
    InterlockedIncrement64(&state->entered);
    std::uintptr_t token = 0;
    bool enterFaulted = false, returned = false, admitted = false;
    __try {
        Admit(state, slot); admitted = true;
        token = Enter(state, frame, &enterFaulted);
        state->config.original(frame->self, frame->method);
        returned = true;
    }
    __finally {
        const auto flags = (returned ? RizVoidReturned : RizVoidAbnormal)
            | (enterFaulted ? RizVoidEnterFaulted : 0u);
        if (admitted) {
            // Keep admission/TLS live during Exit, including synchronous same-
            // slot recursion. No lock is held while any CLR callback executes.
            Exit(state, frame, token, flags);
            LeaveAdmission(state, slot);
        }
        InterlockedIncrement64(&state->exited);
        InterlockedDecrement64(&state->inFlight);
    }
}

template<std::size_t Slot> __declspec(noinline) void __cdecl Entry(void* self, const void* method)
{
    const RizVoidFrameV1 frame{ 1, sizeof(RizVoidFrameV1), 0, 0, self, method, _ReturnAddress() };
    Dispatch(&states[Slot], Slot, &frame);
}
template<std::size_t... Slots> auto Entries(std::index_sequence<Slots...>)
{
    return std::array<void*, sizeof...(Slots)>{ reinterpret_cast<void*>(&Entry<Slots>)... };
}
const auto entries = Entries(std::make_index_sequence<limit>{});
}

RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetLimit()
{
    return static_cast<std::uint32_t>(limit);
}
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetFrameSize()
{
    return sizeof(RizVoidFrameV1);
}
RIZVC_EXPORT void* __cdecl RizVoidContext_GetEntry(std::uint32_t slot)
{
    return slot < limit ? entries[slot] : nullptr;
}
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Configure(std::uint32_t slot, const RizVoidConfigV1* config)
{
    if (slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->original || !config->enter || !config->exit) return 1;
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    state->config = *config;
    InterlockedExchange(&state->configured, 1); // Publish the immutable copy with a full barrier.
    return 0;
}
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetStats(std::uint32_t slot, RizVoidStatsV1* statistics)
{
    if (slot >= limit || !statistics || statistics->abiVersion != 1 || statistics->byteSize != sizeof(*statistics)) return 1;
    auto* state = &states[slot];
    statistics->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    statistics->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    statistics->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    statistics->enterFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->enterFaults, 0, 0));
    statistics->exitFaults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exitFaults, 0, 0));
    return 0;
}

RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Close(std::uint32_t slot, std::uint32_t timeoutMs, RizVoidGateReceiptV1* receipt)
{
    if (slot >= limit || timeoutMs > 10000 || !receipt || receipt->abiVersion != 1
        || receipt->byteSize != sizeof(*receipt) || receipt->reserved != 0
        || InterlockedCompareExchange(&states[slot].configured, 1, 1) != 1) return 1;
    receipt->slot = 0; receipt->ownerThreadId = 0; receipt->epoch = 0;
    if (admittedTotal != 0) return 2;
    auto* state = &states[slot];
    const ULONGLONG started = GetTickCount64();
    AcquireSRWLockExclusive(&state->admissionLock);
    if (!AdmissionKnown(state)) { ReleaseSRWLockExclusive(&state->admissionLock); return 5; }
    if (state->phase != RizVoidGateOpen) { ReleaseSRWLockExclusive(&state->admissionLock); return 3; }
    const auto epoch = InterlockedIncrement64(&epochSource);
    if (epoch <= 0 || InterlockedCompareExchange(&epochExhausted, 0, 0) != 0) {
        InterlockedExchange(&epochExhausted, 1); AdmissionFault(state);
        ReleaseSRWLockExclusive(&state->admissionLock); return 5;
    }
    state->epoch = static_cast<std::uint64_t>(epoch);
    state->ownerThreadId = GetCurrentThreadId();
    state->phase = RizVoidGateClosing;
    while (state->active != 0) {
        const auto elapsed = GetTickCount64() - started;
        if (elapsed >= timeoutMs) {
            ReopenUnissued(state); ReleaseSRWLockExclusive(&state->admissionLock); return 4;
        }
        const auto remaining = static_cast<DWORD>(timeoutMs - elapsed);
        if (!SleepConditionVariableSRW(&state->admissionChanged, &state->admissionLock, remaining, 0)) {
            if (GetLastError() != ERROR_TIMEOUT) AdmissionFault(state);
        }
        if (!AdmissionKnown(state) || state->phase != RizVoidGateClosing) {
            ReopenUnissued(state); ReleaseSRWLockExclusive(&state->admissionLock); return 5;
        }
    }
    if (!AdmissionKnown(state)) {
        ReopenUnissued(state); ReleaseSRWLockExclusive(&state->admissionLock); return 5;
    }
    state->phase = RizVoidGateClosed;
    receipt->slot = slot; receipt->ownerThreadId = state->ownerThreadId; receipt->epoch = state->epoch;
    ReleaseSRWLockExclusive(&state->admissionLock);
    return 0;
}
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Verify(const RizVoidGateReceiptV1* receipt)
{
    if (!receipt || receipt->abiVersion != 1 || receipt->byteSize != sizeof(*receipt) || receipt->reserved != 0) return 1;
    if (receipt->slot >= limit) return 2;
    auto* state = &states[receipt->slot];
    AcquireSRWLockShared(&state->admissionLock);
    const auto result = ValidateClosedReceipt(state, receipt);
    ReleaseSRWLockShared(&state->admissionLock);
    return result;
}
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Release(const RizVoidGateReceiptV1* receipt)
{
    if (!receipt || receipt->abiVersion != 1 || receipt->byteSize != sizeof(*receipt) || receipt->reserved != 0) return 1;
    if (receipt->slot >= limit) return 2;
    auto* state = &states[receipt->slot];
    AcquireSRWLockExclusive(&state->admissionLock);
    const auto result = ValidateClosedReceipt(state, receipt);
    if (result == 0) ReopenUnissued(state);
    ReleaseSRWLockExclusive(&state->admissionLock);
    return result;
}
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetGateStats(std::uint32_t slot, RizVoidGateStatsV1* statistics)
{
    if (slot >= limit || !statistics || statistics->abiVersion != 1 || statistics->byteSize != sizeof(*statistics)) return 1;
    auto* state = &states[slot];
    AcquireSRWLockShared(&state->admissionLock);
    statistics->active = state->active; statistics->parked = state->parked; statistics->epoch = state->epoch;
    statistics->phase = state->phase; statistics->unknown = AdmissionKnown(state) ? 0u : 1u;
    ReleaseSRWLockShared(&state->admissionLock);
    return 0;
}
