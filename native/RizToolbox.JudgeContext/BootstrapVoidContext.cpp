#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <limits>
#include "BootstrapVoidContext.h"
#include "ListenerContext.h"

namespace {
constexpr std::size_t slotLimit = 32;
constexpr std::size_t recordLimit = 256;
struct State {
    RizVoidBootstrapConfigV1 config{};
    RizVoidBootstrapObserverV1 observer{};
    // 0 empty, -1 configuring, 1 ready, -2 failed. None may be reused.
    volatile LONG configured = 0;
    volatile LONG attaching = 0;
    volatile LONG unknown = 0;
    PVOID volatile published = nullptr;
    std::uint64_t epoch = 0;
};
struct Record {
    State* state = nullptr;
    const RizVoidBootstrapObserverV1* observer = nullptr;
    const void* self = nullptr;
    const void* method = nullptr;
    const void* caller = nullptr;
    std::uintptr_t managedToken = 0;
    bool enterFaulted = false;
    bool originalWindow = false;
    RizListenerParentCapture listener{};
};
State states[slotLimit];
volatile LONG64 epochSource = 0;
volatile LONG epochExhausted = 0;
thread_local Record records[recordLimit]{};
thread_local std::size_t recordDepth = 0;
thread_local std::uint64_t overflowDepth = 0;
thread_local bool stackUnknown = false;

void Fault(State* state)
{
    InterlockedExchange(&state->unknown, 1);
}
bool Known(State* state)
{
    return InterlockedCompareExchange(&state->unknown, 0, 0) == 0;
}
const RizVoidBootstrapObserverV1* ReadObserver(State* state)
{
    return static_cast<const RizVoidBootstrapObserverV1*>(
        InterlockedCompareExchangePointer(&state->published, nullptr, nullptr));
}
bool Matches(const Record* record, State* state, const RizVoidFrameV1* frame)
{
    return record->state == state && record->self == frame->self
        && record->method == frame->method && record->caller == frame->caller;
}

std::uintptr_t __cdecl VoidShimEnter(const RizVoidFrameV1* frame, void* user)
{
    auto* state = static_cast<State*>(user);
    // Push even before an observer exists. An unobserved child invocation must
    // mask the parent's snapshot, and late attachment cannot adopt old calls.
    if (stackUnknown || overflowDepth != 0 || recordDepth == recordLimit) {
        Fault(state);
        if (overflowDepth == (std::numeric_limits<std::uint64_t>::max)())
            RaiseFailFastException(nullptr, nullptr, 0);
        ++overflowDepth;
        return 0; // Reserved solely for a masked overflow invocation.
    }
    auto* record = &records[recordDepth++];
    *record = Record{};
    record->state = state;
    record->self = frame->self;
    record->method = frame->method;
    record->caller = frame->caller;
    record->observer = ReadObserver(state); // Exactly one acquire snapshot.
    RizListenerContext_CaptureParent(static_cast<std::uint32_t>(state - states), state->epoch, &record->listener);
    if (record->observer) {
        __try { record->managedToken = record->observer->enter(frame, record->observer->user); }
        __except (EXCEPTION_EXECUTE_HANDLER) {
            record->enterFaulted = true;
            Fault(state);
        }
    }
    record->originalWindow = true;
    // The opaque native token is never a managed token. Zero remains valid as
    // a managed token, so it cannot be used to infer whether Enter happened.
    return reinterpret_cast<std::uintptr_t>(record);
}

void __cdecl VoidShimExit(const RizVoidFrameV1* frame, std::uintptr_t token,
    std::uint32_t flags, void* user)
{
    auto* state = static_cast<State*>(user);
    if (token == 0) {
        if (overflowDepth != 0) --overflowDepth;
        else { stackUnknown = true; Fault(state); }
        return; // Never pop a real record for a masked invocation.
    }
    if (stackUnknown || overflowDepth != 0 || recordDepth == 0
        || token != reinterpret_cast<std::uintptr_t>(&records[recordDepth - 1])
        || !Matches(&records[recordDepth - 1], state, frame)) {
        stackUnknown = true;
        Fault(state);
        return; // Corruption cannot confer a parent record's authority.
    }
    auto* record = &records[recordDepth - 1];
    record->originalWindow = false;
    __try {
        // Child cleanup precedes V1 parent Exit and cannot borrow its parent.
        RizListenerContext_CloseParent(&record->listener, flags);
        if (record->observer) {
            const auto observedFlags = flags | (record->enterFaulted ? RizVoidEnterFaulted : 0u);
            __try { record->observer->exit(frame, record->managedToken, observedFlags, record->observer->user); }
            __except (EXCEPTION_EXECUTE_HANDLER) { Fault(state); }
        }
    }
    __finally {
        // Observer SEH/C++ exceptions must not strand TLS or change the native
        // original's return/unwind. The outer Void transport owns admission.
        if (overflowDepth != 0 || recordDepth == 0 || &records[recordDepth - 1] != record) {
            stackUnknown = true;
            Fault(state);
        } else {
            *record = Record{};
            --recordDepth;
        }
    }
}

void __cdecl MidShim(const RizEventMidFrameV1* frame, void* user)
{
    auto* state = static_cast<State*>(user);
    if (stackUnknown || overflowDepth != 0 || recordDepth == 0) return;
    const auto* record = &records[recordDepth - 1];
    if (record->state != state || !record->originalWindow || record->enterFaulted
        || !record->observer
        || record->self != reinterpret_cast<const void*>(frame->gpr[RizEventMidRsi])
        || state->config.expectedMidReturnPc != reinterpret_cast<const void*>(frame->returnPc)) return;
    __try { record->observer->mid(frame, record->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(state); }
}

void RefreshDependencyUnknown(std::uint32_t slot, State* state)
{
    RizVoidStatsV1 parent{ 1, sizeof(RizVoidStatsV1), 0, 0, 0, 0, 0 };
    RizVoidGateStatsV1 gate{ 1, sizeof(RizVoidGateStatsV1), 0, 0, 0, 0, 0 };
    RizEventMidStatsV1 mid{ 1, sizeof(RizEventMidStatsV1), 0, 0, 0, 0 };
    if (RizVoidContext_GetStats(slot, &parent) != 0
        || RizVoidContext_GetGateStats(slot, &gate) != 0
        || RizEventMidContext_GetStats(slot, &mid) != 0
        || parent.enterFaults != 0 || parent.exitFaults != 0 || gate.unknown != 0 || mid.faults != 0)
        Fault(state);
}
}

bool RizVoidBootstrap_CurrentListenerParent(RizListenerCurrentParent* parent)
{
    if (!parent || stackUnknown || overflowDepth != 0 || recordDepth == 0) return false;
    auto* record = &records[recordDepth - 1];
    if (!record->originalWindow || record->enterFaulted) return false;
    parent->slot = static_cast<std::uint32_t>(record->state - states);
    parent->epoch = record->state->epoch;
    parent->self = record->self;
    parent->managedToken = record->managedToken;
    parent->capture = &record->listener;
    return true;
}
bool RizVoidBootstrap_ListenerParentKnown(std::uint32_t slot, std::uint64_t epoch)
{
    if (slot >= slotLimit || epoch == 0) return false;
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1 || state->epoch != epoch) return false;
    // Administrative listener reads must include the same lower transport
    // failures as Bootstrap GetInfo, without recursing into either GetInfo.
    RefreshDependencyUnknown(slot, state);
    return Known(state);
}
void RizVoidBootstrap_ListenerFault(std::uint32_t slot, std::uint64_t epoch)
{
    if (slot < slotLimit && states[slot].epoch == epoch) Fault(&states[slot]);
}

RIZVC_EXPORT std::uint32_t __cdecl RizVoidBootstrap_Configure(std::uint32_t slot, const RizVoidBootstrapConfigV1* config)
{
    if (slot >= slotLimit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->target || !config->original || !config->expectedMidReturnPc) return 1;
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    state->config = *config;
    const auto epoch = InterlockedIncrement64(&epochSource);
    if (epoch <= 0 || InterlockedCompareExchange(&epochExhausted, 0, 0) != 0) {
        InterlockedExchange(&epochExhausted, 1);
        Fault(state);
        InterlockedExchange(&state->configured, -2);
        return 3;
    }
    state->epoch = static_cast<std::uint64_t>(epoch);
    const RizVoidConfigV1 parent{ 1, sizeof(RizVoidConfigV1), config->original, &VoidShimEnter, &VoidShimExit, state };
    const RizEventMidConfigV1 mid{ 1, sizeof(RizEventMidConfigV1), config->expectedMidReturnPc, &MidShim, state, 0 };
    // Any partial claim stays owned forever. No machine instruction is changed
    // here, and none of these callbacks can become live through Configure.
    if (RizVoidContext_Configure(slot, &parent) != 0 || RizEventMidContext_Configure(slot, &mid) != 0) {
        Fault(state);
        InterlockedExchange(&state->configured, -2);
        return 3;
    }
    InterlockedExchange(&state->configured, 1);
    return 0;
}

RIZVC_EXPORT std::uint32_t __cdecl RizVoidBootstrap_Attach(std::uint32_t slot, std::uint64_t epoch,
    const RizVoidBootstrapObserverV1* observer)
{
    if (slot >= slotLimit || !observer || observer->abiVersion != 1 || observer->byteSize != sizeof(*observer)
        || !observer->enter || !observer->exit || !observer->mid) return 1;
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, 1, 1) != 1) return Known(state) ? 2u : 5u;
    if (epoch == 0 || epoch != state->epoch) return 3;
    RefreshDependencyUnknown(slot, state);
    if (!Known(state)) return 5;
    if (InterlockedCompareExchange(&state->attaching, -1, 0) != 0) return 4;
    if (!Known(state)) {
        // A fault racing attachment permanently consumes the attempt; no
        // retry is allowed to reinterpret that uncertain observer boundary.
        InterlockedExchange(&state->attaching, -2);
        return 5;
    }
    state->observer = *observer;
    InterlockedExchangePointer(&state->published, &state->observer);
    InterlockedExchange(&state->attaching, 1);
    return 0;
}

RIZVC_EXPORT std::uint32_t __cdecl RizVoidBootstrap_GetInfo(std::uint32_t slot, RizVoidBootstrapInfoV1* info)
{
    if (slot >= slotLimit || !info || info->abiVersion != 1 || info->byteSize != sizeof(*info)
        || info->reserved != 0) return 1;
    auto* state = &states[slot];
    const auto phase = InterlockedCompareExchange(&state->configured, 0, 0);
    info->slot = slot;
    info->attached = 0;
    info->target = nullptr;
    info->original = nullptr;
    info->entry = nullptr;
    info->epoch = 0;
    info->reserved = 0;
    if (phase == 1 || phase == -2) {
        info->target = state->config.target;
        info->original = state->config.original;
        info->entry = RizVoidContext_GetEntry(slot);
        info->epoch = state->epoch;
    }
    if (phase == 1) {
        RefreshDependencyUnknown(slot, state);
        info->attached = ReadObserver(state) ? 1u : 0u;
    }
    info->unknown = Known(state) ? 0u : 1u;
    return phase == 1 ? 0u : 2u;
}
