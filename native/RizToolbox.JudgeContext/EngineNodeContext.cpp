#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <limits>
#include <utility>
#include "EngineNodeContext.h"
#ifdef RIZ_ENGINE_WAIT_CONTEXT
#include "EngineWaitContext.h"
#endif
#ifdef RIZ_ENGINE_INLINE_PROBE
#include "MidContext.h"

extern "C" void RizEngineInlineProbe_Entry();
#endif

namespace {
constexpr std::size_t limit = 32;
struct State {
    RizEngineNodeConfigV1 config{};
    volatile LONG configured = 0;
    std::uint64_t epoch{};
    RizEngineNodeObserverV1 observer{};
    void* volatile publishedObserver = nullptr;
    volatile LONG attachAttempted = 0, unknown = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0;
    volatile LONG64 enterFaults = 0, exitFaults = 0, snapshotFaults = 0, outputFaults = 0;
};
State states[4][limit];
volatile LONG64 sequenceSource = 0;
volatile LONG64 epochSource = 0;
#ifdef RIZ_ENGINE_INLINE_PROBE
constexpr std::size_t inlineThreadLimit = 64;
constexpr std::uint32_t inlineDepthLimit = 128;
struct InlineState {
    RizEngineInlineConfigV1 config{};
    std::uint64_t epoch{};
    volatile LONG configured = 0, attachAttempted = 0, unknown = 0;
    RizEngineInlineObserverV1 observer{};
    void* volatile publishedObserver = nullptr;
    volatile LONG64 startsEntered = 0, startsExited = 0, startsInFlight = 0;
    volatile LONG64 begins = 0, endsFree = 0, endsRetained = 0, probesInFlight = 0;
    volatile LONG64 faults = 0, snapshotFaults = 0, orphanProbes = 0, unmatchedStarts = 0, events = 0;
};
struct InlineThread {
    volatile LONG threadId = 0;
    void* volatile top = nullptr;
};
struct InlineStartScope {
    State* state = nullptr;
    RizEngineNodeFrameV1* frame = nullptr;
    InlineState* family = nullptr;
    const RizEngineInlineObserverV1* observer = nullptr;
    InlineThread* thread = nullptr;
    InlineStartScope* parent = nullptr;
    DWORD threadId = 0;
    std::uint32_t depth = 0, phase = 0, snapshotFlags = 0, endRawReturn = 0;
    bool began = false, ended = false, callbackActive = false;
    bool unknown = false;
    void* node = nullptr;
    void* owner = nullptr;
    std::uint64_t enumHandle = 0;
    void* enumCache = nullptr;
    std::uint32_t enumType = 0;
    std::int32_t referenceCount = 0;
};
InlineState inlineStates[limit];
InlineThread inlineThreads[inlineThreadLimit];
SRWLOCK inlineConfigurationLock = SRWLOCK_INIT;
volatile LONG inlineUnknown = 0;
volatile LONG64 inlineEpochSource = 0, inlineEventSource = 0;
#endif

std::uint64_t NextPositive(volatile LONG64* source)
{
    auto current = InterlockedCompareExchange64(source, 0, 0);
    for (;;) {
        if (current < 0 || current == (std::numeric_limits<LONG64>::max)()) return 0;
        const auto next = current + 1;
        const auto seen = InterlockedCompareExchange64(source, next, current);
        if (seen == current) return static_cast<std::uint64_t>(next);
        current = seen;
    }
}
#ifdef RIZ_ENGINE_INLINE_PROBE
void FaultInline(InlineState* family, InlineStartScope* scope = nullptr)
{
    InterlockedExchange(&inlineUnknown, 1);
    if (scope) scope->unknown = true;
    if (family) {
        InterlockedExchange(&family->unknown, 1);
        InterlockedIncrement64(&family->faults);
    }
}
InlineThread* FindInlineThread(DWORD threadId)
{
    for (auto& thread : inlineThreads)
        if (static_cast<DWORD>(InterlockedCompareExchange(&thread.threadId, 0, 0)) == threadId) return &thread;
    return nullptr;
}
InlineThread* ClaimInlineThread(DWORD threadId)
{
    if (auto* existing = FindInlineThread(threadId)) return existing;
    for (auto& thread : inlineThreads)
        if (InterlockedCompareExchange(&thread.threadId, static_cast<LONG>(threadId), 0) == 0) return &thread;
    return nullptr;
}
InlineStartScope* ReadInlineTop(InlineThread* thread)
{
    return thread ? static_cast<InlineStartScope*>(
        InterlockedCompareExchangePointer(&thread->top, nullptr, nullptr)) : nullptr;
}
InlineState* InlineFamily(State* state)
{
    for (auto& family : inlineStates)
        if (InterlockedCompareExchange(&family.configured, 0, 0) == 1
            && &states[RizEngineNodeStart][family.config.startSlot] == state) return &family;
    return nullptr;
}
void PushInlineStart(InlineStartScope* scope, State* state, RizEngineNodeFrameV1* frame)
{
    scope->state = state; scope->frame = frame; scope->threadId = GetCurrentThreadId();
    scope->family = InlineFamily(state);
    if (scope->family) {
        auto* family = scope->family;
        scope->observer = static_cast<const RizEngineInlineObserverV1*>(
            InterlockedCompareExchangePointer(&family->publishedObserver, nullptr, nullptr));
        InterlockedIncrement64(&family->startsEntered); InterlockedIncrement64(&family->startsInFlight);
        if (frame->sequence == 0 || family->config.mainThreadId != scope->threadId
            || family->config.expectedStartEpoch != state->epoch || family->config.expectedStartTarget != state->config.target)
            FaultInline(family, scope);
    }
    // Every real Start occupies a physical mask, even without an inline family
    // or observer. The stack object is owned by this exact native Dispatch.
    scope->thread = ClaimInlineThread(scope->threadId);
    if (!scope->thread) {
        if (scope->family) InterlockedIncrement64(&scope->family->unmatchedStarts);
        FaultInline(scope->family, scope); return;
    }
    scope->parent = ReadInlineTop(scope->thread);
    scope->depth = scope->parent ? scope->parent->depth + 1u : 1u;
    if (scope->depth == 0 || scope->depth > inlineDepthLimit) FaultInline(scope->family, scope);
    InterlockedExchangePointer(&scope->thread->top, scope);
}
bool InlineParentMatched(const InlineStartScope* scope)
{
    return scope->family && scope->thread && ReadInlineTop(scope->thread) == scope
        && scope->threadId == scope->family->config.mainThreadId
        && scope->depth != 0 && scope->depth <= inlineDepthLimit;
}
void EmitInline(InlineStartScope* scope, std::uint32_t kind, std::uint32_t probeSlot,
    const void* relayReturnPc, const RizEngineNodeCompletionV1* completion, std::uint32_t extraFlags = 0)
{
    auto* family = scope->family;
    if (!family) return;
    RizEngineInlineFrameV1 record{};
    record.abiVersion = 1; record.byteSize = sizeof(record); record.kind = kind;
    record.flags = scope->snapshotFlags | extraFlags;
    if (InlineParentMatched(scope)) record.flags |= RizEngineInlineParentMatched;
    if (scope->unknown || InterlockedCompareExchange(&family->unknown, 0, 0) != 0) record.flags |= RizEngineInlineUnknown;
    record.startSlot = family->config.startSlot;
    record.probeSlot = static_cast<std::uint32_t>(family - inlineStates);
    record.threadId = scope->threadId; record.depth = scope->depth; record.epoch = family->epoch;
    record.startSequence = scope->frame->sequence;
    record.parentStartSequence = scope->parent ? scope->parent->frame->sequence : 0;
    record.eventSequence = NextPositive(&inlineEventSource);
    record.node = scope->node;
    record.owner = scope->began ? scope->owner : scope->frame->arg0;
    record.enumHandle = scope->enumHandle;
    record.enumCache = scope->began ? scope->enumCache : scope->frame->arg1;
    record.enumType = scope->enumType; record.referenceCount = scope->referenceCount;
    record.sitePc = probeSlot < 3 ? family->config.sites[probeSlot].sitePc : family->config.expectedStartTarget;
    record.relayReturnPc = relayReturnPc; record.outputSlot = scope->frame->arg3;
    if (kind == RizEngineInlineEndFree || kind == RizEngineInlineEndRetained)
        record.rawStartReturn = scope->endRawReturn;
    if (completion) {
        record.startExitFlags = completion->flags; record.rawStartReturn = completion->returnByte;
        record.startOutputNode = completion->startOutputNode;
        if ((completion->outputFlags & RizEngineNodeSnapshotRequested) != 0) record.flags |= RizEngineInlineOutputRequested;
        if ((completion->outputFlags & RizEngineNodeSnapshotValid) != 0) record.flags |= RizEngineInlineOutputRead;
        if ((completion->outputFlags & RizEngineNodeSnapshotFaulted) != 0) record.flags |= RizEngineInlineOutputFaulted;
    }
    if (record.eventSequence == 0) { FaultInline(family, scope); record.flags |= RizEngineInlineUnknown; }
    InterlockedIncrement64(&family->events);
    if (!scope->observer) return; // Frozen before typed Enter; never borrow a later Attach.
    const bool priorCallback = scope->callbackActive; scope->callbackActive = true;
    __try { scope->observer->callback(&record, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { FaultInline(family, scope); }
    scope->callbackActive = priorCallback;
}
void FinishInlineStart(InlineStartScope* scope, const RizEngineNodeCompletionV1* completion)
{
    scope->phase = 2;
    std::uint32_t flags = 0;
    if ((completion->flags & RizEngineNodeAbnormal) != 0) { flags |= RizEngineInlineAbnormal; FaultInline(scope->family, scope); }
    if ((completion->flags & RizEngineNodeEnterFaulted) != 0) { flags |= RizEngineInlineObserverFaulted; FaultInline(scope->family, scope); }
    if ((completion->outputFlags & RizEngineNodeSnapshotFaulted) != 0) FaultInline(scope->family, scope);
    if (scope->began && !scope->ended) { flags |= RizEngineInlineMissingEnd; FaultInline(scope->family, scope); }
    if (scope->ended && ((completion->flags & (RizEngineNodeReturned | RizEngineNodeReturnByteValid))
            != (RizEngineNodeReturned | RizEngineNodeReturnByteValid)
        || completion->returnByte != scope->endRawReturn
        || completion->outputFlags != (RizEngineNodeSnapshotRequested | RizEngineNodeSnapshotValid)
        || completion->startOutputNode != nullptr))
        FaultInline(scope->family, scope);
    EmitInline(scope, RizEngineInlineStartExited, 0xffffffffu, nullptr, completion, flags);
}
void PopInlineStart(InlineStartScope* scope)
{
    if (scope->thread) {
        if (ReadInlineTop(scope->thread) != scope) FaultInline(scope->family, scope);
        // A malformed observation must not orphan this real stack object. The
        // actual original is unwinding; restoring its captured parent is exact.
        InterlockedExchangePointer(&scope->thread->top, scope->parent);
        if (!scope->parent) InterlockedExchange(&scope->thread->threadId, 0);
    }
    scope->phase = 3;
    if (scope->family) {
        InterlockedIncrement64(&scope->family->startsExited); InterlockedDecrement64(&scope->family->startsInFlight);
    }
}
void CopyInlineBegin(InlineStartScope* scope, const RizEventMidFrameV1* machine)
{
    scope->began = true; scope->snapshotFlags = RizEngineInlineSnapshotRequested;
    scope->node = reinterpret_cast<void*>(machine->gpr[RizEventMidRbx]);
    const auto address = reinterpret_cast<std::uintptr_t>(scope->node);
    if (!address || address > (std::numeric_limits<std::uintptr_t>::max)() - 0x63) {
        scope->snapshotFlags |= RizEngineInlineSnapshotFaulted;
        InterlockedIncrement64(&scope->family->snapshotFaults); FaultInline(scope->family, scope); return;
    }
    __try {
        const auto owner = *reinterpret_cast<void* volatile*>(address + 0x58);
        const auto handle = *reinterpret_cast<volatile std::uint64_t*>(address + 0x10);
        const auto type = *reinterpret_cast<volatile std::uint32_t*>(address + 0x18);
        const auto cache = *reinterpret_cast<void* volatile*>(address + 0x20);
        const auto references = *reinterpret_cast<volatile std::int32_t*>(address + 0x60);
        scope->owner = owner; scope->enumHandle = handle; scope->enumType = type;
        scope->enumCache = cache; scope->referenceCount = references;
        scope->snapshotFlags |= RizEngineInlineSnapshotValid;
        if (!owner || owner != scope->frame->arg0 || type != 2 || !cache || cache != scope->frame->arg1 || references != 1
            || reinterpret_cast<void*>(machine->gpr[RizEventMidR12]) != scope->frame->arg3)
            FaultInline(scope->family, scope);
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        scope->owner = nullptr; scope->enumHandle = 0; scope->enumType = 0; scope->enumCache = nullptr; scope->referenceCount = 0;
        scope->snapshotFlags |= RizEngineInlineSnapshotFaulted;
        InterlockedIncrement64(&scope->family->snapshotFaults); FaultInline(scope->family, scope);
    }
}
#endif
const RizEngineNodeObserverV1* SnapshotObserver(State* state)
{
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) return nullptr;
    return static_cast<const RizEngineNodeObserverV1*>(
        InterlockedCompareExchangePointer(&state->publishedObserver, nullptr, nullptr));
}
void CopyBefore(State* state, RizEngineNodeFrameV1* frame)
{
    if (frame->kind == RizEngineNodeStart) return;
    frame->node = frame->arg0;
    frame->snapshotFlags = RizEngineNodeSnapshotRequested;
    const auto address = reinterpret_cast<std::uintptr_t>(frame->node);
    if (address > (std::numeric_limits<std::uintptr_t>::max)() - 0x63) {
        frame->snapshotFlags |= RizEngineNodeSnapshotFaulted;
        InterlockedIncrement64(&state->snapshotFaults); InterlockedExchange(&state->unknown, 1);
        return;
    }
    __try {
        // Copy into locals: a fault never publishes a partial identity. These
        // are five bounded scalar reads, not a coherent concurrency snapshot,
        // script-handle resolution, owner traversal or an object root.
        const auto owner = *reinterpret_cast<void* volatile*>(address + 0x58);
        const auto handle = *reinterpret_cast<volatile std::uint64_t*>(address + 0x10);
        const auto type = *reinterpret_cast<volatile std::uint32_t*>(address + 0x18);
        const auto cache = *reinterpret_cast<void* volatile*>(address + 0x20);
        const auto references = *reinterpret_cast<volatile std::int32_t*>(address + 0x60);
        frame->owner = owner; frame->enumHandle = handle; frame->enumType = type;
        frame->enumCache = cache; frame->referenceCount = references;
        frame->snapshotFlags |= RizEngineNodeSnapshotValid;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        frame->owner = nullptr; frame->enumHandle = 0; frame->enumType = 0;
        frame->enumCache = nullptr; frame->referenceCount = 0;
        frame->snapshotFlags |= RizEngineNodeSnapshotFaulted;
        InterlockedIncrement64(&state->snapshotFaults); InterlockedExchange(&state->unknown, 1);
    }
}
void CopyStartOutput(State* state, const RizEngineNodeFrameV1* frame, RizEngineNodeCompletionV1* completion)
{
    completion->outputFlags = RizEngineNodeSnapshotRequested;
    __try {
        // The output slot is the actual R9 pointer; the pointed-to node might
        // already have been freed by inline first-execution cleanup. Do not
        // dereference it, even for a null check of a node field.
        completion->startOutputNode = *reinterpret_cast<void* volatile*>(frame->arg3);
        completion->outputFlags |= RizEngineNodeSnapshotValid;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        completion->outputFlags |= RizEngineNodeSnapshotFaulted;
        InterlockedIncrement64(&state->outputFaults); InterlockedExchange(&state->unknown, 1);
    }
}
std::uintptr_t Enter(State* state, const RizEngineNodeFrameV1* frame,
    const RizEngineNodeObserverV1* observer, bool* faulted)
{
    if (!observer) return 0;
    __try { return observer->enter(frame, observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true; InterlockedIncrement64(&state->enterFaults);
        InterlockedExchange(&state->unknown, 1); return 0;
    }
}
void Exit(State* state, const RizEngineNodeFrameV1* frame, std::uintptr_t token,
    const RizEngineNodeCompletionV1* completion, const RizEngineNodeObserverV1* observer)
{
    if (!observer) return;
    __try { observer->exit(frame, token, completion, observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedIncrement64(&state->exitFaults); InterlockedExchange(&state->unknown, 1);
    }
}
std::uint32_t Dispatch(State* state, RizEngineNodeFrameV1* frame, const RizEngineNodeObserverV1* observer)
{
    // Configure never publishes entry code. An unconfigured entry has no
    // original to call and must not have been published by its caller.
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1)
        RaiseFailFastException(nullptr, nullptr, 0);
    InterlockedIncrement64(&state->inFlight); InterlockedIncrement64(&state->entered);
    if (frame->sequence == 0) InterlockedExchange(&state->unknown, 1);
    std::uintptr_t token = 0; bool enterFaulted = false, returned = false;
    std::uint32_t result = 0;
    RizEngineNodeCompletionV1 completion{ 1, sizeof(completion), 0, 0, nullptr, 0, 0 };
#if defined(RIZ_ENGINE_INLINE_PROBE) || defined(RIZ_ENGINE_WAIT_CONTEXT)
    const DWORD incomingLastError = GetLastError();
    DWORD outgoingLastError = incomingLastError;
#endif
#ifdef RIZ_ENGINE_WAIT_CONTEXT
    RizEngineWaitPrivate::BoundaryStorage waitBoundary;
    RizEngineWaitPrivate::BeginNode(&waitBoundary, frame, state->config.target);
#endif
#ifdef RIZ_ENGINE_INLINE_PROBE
    InlineStartScope inlineScope{};
    if (frame->kind == RizEngineNodeStart) PushInlineStart(&inlineScope, state, frame);
#endif
    __try {
        CopyBefore(state, frame);
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        // Register the physical boundary before the existing typed observer
        // captures its own call. Only that observer owns its Complete/Dispose.
        RizEngineWaitPrivate::Entered(&waitBoundary);
#endif
        token = Enter(state, frame, observer, &enterFaulted);
#ifdef RIZ_ENGINE_INLINE_PROBE
        if (frame->kind == RizEngineNodeStart) {
            if (enterFaulted) FaultInline(inlineScope.family, &inlineScope);
            EmitInline(&inlineScope, RizEngineInlineStartEntered, 0xffffffffu, nullptr, nullptr,
                enterFaulted ? RizEngineInlineObserverFaulted : 0u);
            inlineScope.phase = 1;
        }
#endif
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        RizEngineWaitPrivate::Original(&waitBoundary);
#endif
#if defined(RIZ_ENGINE_INLINE_PROBE) || defined(RIZ_ENGINE_WAIT_CONTEXT)
        SetLastError(incomingLastError);
#endif
        if (frame->kind == RizEngineNodeStart)
            result = reinterpret_cast<std::uint8_t(__cdecl*)(void*, void*, void*, void*)>(state->config.original)
                (frame->arg0, frame->arg1, frame->arg2, frame->arg3);
        else if (frame->kind == RizEngineNodeExecute)
            result = reinterpret_cast<std::uint32_t(__cdecl*)(void*, void*)>(state->config.original)
                (frame->arg0, frame->arg1);
        else if (frame->kind == RizEngineNodeRelease)
            result = reinterpret_cast<std::uint8_t(__cdecl*)(void*)>(state->config.original)(frame->arg0);
        else
            reinterpret_cast<void(__cdecl*)(void*)>(state->config.original)(frame->arg0);
#if defined(RIZ_ENGINE_INLINE_PROBE) || defined(RIZ_ENGINE_WAIT_CONTEXT)
        outgoingLastError = GetLastError();
#endif
        returned = true;
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        RizEngineWaitPrivate::Closing(&waitBoundary);
#endif
#ifdef RIZ_ENGINE_INLINE_PROBE
        if (frame->kind == RizEngineNodeStart) inlineScope.phase = 2;
#endif
        if (frame->kind == RizEngineNodeStart) CopyStartOutput(state, frame, &completion);
    }
    __finally {
#if defined(RIZ_ENGINE_INLINE_PROBE) || defined(RIZ_ENGINE_WAIT_CONTEXT)
        const DWORD finalLastError = returned ? outgoingLastError : GetLastError();
#endif
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        if (!returned) RizEngineWaitPrivate::Closing(&waitBoundary);
#endif
        if (!returned) InterlockedExchange(&state->unknown, 1);
        completion.flags = (returned ? RizEngineNodeReturned : RizEngineNodeAbnormal)
            | (enterFaulted ? RizEngineNodeEnterFaulted : 0u);
        if (returned && frame->kind != RizEngineNodeDetach) {
            completion.flags |= RizEngineNodeReturnByteValid; completion.returnByte = result;
        }
        // A potentially freed node is never read here. Frame and completion
        // are native stack scalar copies shared by this one Enter/Exit pair.
#ifdef RIZ_ENGINE_INLINE_PROBE
        // The real typed Enter already ran; its physical context remains open
        // through side StartExited, then typed Exit owns Complete/Dispose.
        if (frame->kind == RizEngineNodeStart) FinishInlineStart(&inlineScope, &completion);
#endif
        Exit(state, frame, token, &completion, observer);
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        // Wait duties can inspect the completed/disposed typed call here;
        // they never perform that call's completion a second time.
        RizEngineWaitPrivate::CompleteNode(&waitBoundary, &completion);
#endif
#ifdef RIZ_ENGINE_INLINE_PROBE
        // Typed Exit may reenter another real Start while its own physical
        // context is still open. Retain our native parent mask through it.
        if (frame->kind == RizEngineNodeStart) PopInlineStart(&inlineScope);
#endif
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        RizEngineWaitPrivate::Pop(&waitBoundary);
#endif
        InterlockedIncrement64(&state->exited); InterlockedDecrement64(&state->inFlight);
#if defined(RIZ_ENGINE_INLINE_PROBE) || defined(RIZ_ENGINE_WAIT_CONTEXT)
        SetLastError(finalLastError);
#endif
    }
    return result;
}
template<std::size_t Slot> __declspec(noinline) std::uint8_t __cdecl StartEntry(
    void* owner, void* enumerator, void* nameArgument, void* outputSlot)
{
    const auto* observer = SnapshotObserver(&states[RizEngineNodeStart][Slot]);
    RizEngineNodeFrameV1 frame{ 1, sizeof(frame), RizEngineNodeStart, 0,
        owner, enumerator, nameArgument, outputSlot, _ReturnAddress(), NextPositive(&sequenceSource) };
    return static_cast<std::uint8_t>(Dispatch(&states[RizEngineNodeStart][Slot], &frame, observer));
}
template<std::size_t Slot> __declspec(noinline) std::uint32_t __cdecl ExecuteEntry(void* node, void* outputByteSlot)
{
    const auto* observer = SnapshotObserver(&states[RizEngineNodeExecute][Slot]);
    RizEngineNodeFrameV1 frame{ 1, sizeof(frame), RizEngineNodeExecute, 0,
        node, outputByteSlot, nullptr, nullptr, _ReturnAddress(), NextPositive(&sequenceSource) };
    return Dispatch(&states[RizEngineNodeExecute][Slot], &frame, observer);
}
template<std::size_t Slot> __declspec(noinline) std::uint8_t __cdecl ReleaseEntry(void* node)
{
    const auto* observer = SnapshotObserver(&states[RizEngineNodeRelease][Slot]);
    RizEngineNodeFrameV1 frame{ 1, sizeof(frame), RizEngineNodeRelease, 0,
        node, nullptr, nullptr, nullptr, _ReturnAddress(), NextPositive(&sequenceSource) };
    return static_cast<std::uint8_t>(Dispatch(&states[RizEngineNodeRelease][Slot], &frame, observer));
}
template<std::size_t Slot> __declspec(noinline) void __cdecl DetachEntry(void* node)
{
    const auto* observer = SnapshotObserver(&states[RizEngineNodeDetach][Slot]);
    RizEngineNodeFrameV1 frame{ 1, sizeof(frame), RizEngineNodeDetach, 0,
        node, nullptr, nullptr, nullptr, _ReturnAddress(), NextPositive(&sequenceSource) };
    Dispatch(&states[RizEngineNodeDetach][Slot], &frame, observer);
}
using StartFunction = std::uint8_t(__cdecl*)(void*, void*, void*, void*);
using ExecuteFunction = std::uint32_t(__cdecl*)(void*, void*);
using ReleaseFunction = std::uint8_t(__cdecl*)(void*);
using DetachFunction = void(__cdecl*)(void*);
template<std::size_t... Slots> constexpr auto StartEntries(std::index_sequence<Slots...>)
{ return std::array<StartFunction, sizeof...(Slots)>{ &StartEntry<Slots>... }; }
template<std::size_t... Slots> constexpr auto ExecuteEntries(std::index_sequence<Slots...>)
{ return std::array<ExecuteFunction, sizeof...(Slots)>{ &ExecuteEntry<Slots>... }; }
template<std::size_t... Slots> constexpr auto ReleaseEntries(std::index_sequence<Slots...>)
{ return std::array<ReleaseFunction, sizeof...(Slots)>{ &ReleaseEntry<Slots>... }; }
template<std::size_t... Slots> constexpr auto DetachEntries(std::index_sequence<Slots...>)
{ return std::array<DetachFunction, sizeof...(Slots)>{ &DetachEntry<Slots>... }; }
constexpr auto startEntries = StartEntries(std::make_index_sequence<limit>{});
constexpr auto executeEntries = ExecuteEntries(std::make_index_sequence<limit>{});
constexpr auto releaseEntries = ReleaseEntries(std::make_index_sequence<limit>{});
constexpr auto detachEntries = DetachEntries(std::make_index_sequence<limit>{});
bool IsEntry(void* pointer)
{
    for (std::size_t slot = 0; slot != limit; ++slot)
        if (pointer == reinterpret_cast<void*>(startEntries[slot]) || pointer == reinterpret_cast<void*>(executeEntries[slot])
            || pointer == reinterpret_cast<void*>(releaseEntries[slot]) || pointer == reinterpret_cast<void*>(detachEntries[slot])) return true;
    return false;
}
std::uint64_t ReadCounter(volatile LONG64* counter)
{ return static_cast<std::uint64_t>(InterlockedCompareExchange64(counter, 0, 0)); }
}

#ifdef RIZ_ENGINE_INLINE_PROBE
// Called only by EngineInlineProbeEntry.asm, using its actual CALL return slot.
// The relay owns displaced-instruction replay; this function observes scalars.
extern "C" void __cdecl RizEngineInlineProbe_Dispatch(const RizEventMidFrameV1* machine)
{
    const DWORD savedLastError = GetLastError();
    InlineState* selected = nullptr;
    std::uint32_t probeSlot = 0;
    bool anyConfigured = false;
    if (machine && machine->abiVersion == 1 && machine->byteSize == sizeof(*machine)
        && machine->reserved0 == 0 && machine->reserved1 == 0) {
        for (auto& family : inlineStates) {
            if (InterlockedCompareExchange(&family.configured, 0, 0) != 1) continue;
            anyConfigured = true;
            for (std::uint32_t site = 0; site != 3; ++site)
                if (family.config.sites[site].relayReturnPc == reinterpret_cast<const void*>(machine->returnPc))
                { selected = &family; probeSlot = site; break; }
            if (selected) break;
        }
    }
    const auto threadId = GetCurrentThreadId();
    auto* scope = ReadInlineTop(FindInlineThread(threadId));
    if (!selected) {
        if (scope && scope->family) { InterlockedIncrement64(&scope->family->orphanProbes); FaultInline(scope->family, scope); }
        else if (anyConfigured) FaultInline(nullptr);
        SetLastError(savedLastError); return;
    }
    InterlockedIncrement64(&selected->probesInFlight);
    __try {
        if (!scope || scope->family != selected || scope->phase != 1 || scope->callbackActive || !InlineParentMatched(scope)) {
            InterlockedIncrement64(&selected->orphanProbes); FaultInline(selected);
            if (scope) FaultInline(scope->family, scope);
        }
        else if (probeSlot == 0) {
            InterlockedIncrement64(&selected->begins);
            if (scope->began || scope->ended) FaultInline(selected, scope);
            else CopyInlineBegin(scope, machine);
            EmitInline(scope, RizEngineInlineBegin, probeSlot, reinterpret_cast<const void*>(machine->returnPc), nullptr);
        }
        else {
            if (probeSlot == 1) InterlockedIncrement64(&selected->endsFree);
            else InterlockedIncrement64(&selected->endsRetained);
            // Both native endpoints are after potential destruction. Compare
            // only saved register values and the Begin cache, never node memory.
            if (!scope->began || scope->ended
                || scope->snapshotFlags != (RizEngineInlineSnapshotRequested | RizEngineInlineSnapshotValid)
                || reinterpret_cast<void*>(machine->gpr[RizEventMidRbx]) != scope->node
                || reinterpret_cast<void*>(machine->gpr[RizEventMidR12]) != scope->frame->arg3)
                FaultInline(selected, scope);
            scope->ended = true;
            scope->endRawReturn = static_cast<std::uint32_t>(machine->gpr[RizEventMidRax] & 0xffu);
            EmitInline(scope, probeSlot == 1 ? RizEngineInlineEndFree : RizEngineInlineEndRetained,
                probeSlot, reinterpret_cast<const void*>(machine->returnPc), nullptr);
        }
    }
    __finally {
        InterlockedDecrement64(&selected->probesInFlight);
        SetLastError(savedLastError);
    }
}
#endif

RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetAbiVersion() { return 2; }
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetLimit() { return static_cast<std::uint32_t>(limit); }
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetFrameSize() { return sizeof(RizEngineNodeFrameV1); }
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetCompletionSize() { return sizeof(RizEngineNodeCompletionV1); }
RIZEC_EXPORT void* __cdecl RizEngineNodeContext_GetEntry(std::uint32_t kind, std::uint32_t slot)
{
    if (kind > RizEngineNodeDetach || slot >= limit) return nullptr;
    return kind == RizEngineNodeStart ? reinterpret_cast<void*>(startEntries[slot])
        : kind == RizEngineNodeExecute ? reinterpret_cast<void*>(executeEntries[slot])
        : kind == RizEngineNodeRelease ? reinterpret_cast<void*>(releaseEntries[slot])
        : reinterpret_cast<void*>(detachEntries[slot]);
}
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_Configure(std::uint32_t kind, std::uint32_t slot,
    const RizEngineNodeConfigV1* config)
{
    if (kind > RizEngineNodeDetach || slot >= limit || !config || config->abiVersion != 1
        || config->byteSize != sizeof(*config) || !config->target || !config->original
        || config->target == config->original || IsEntry(config->target) || IsEntry(config->original)) return 1;
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    const auto epoch = NextPositive(&epochSource);
    if (epoch == 0) {
        InterlockedExchange(&state->unknown, 1); InterlockedExchange(&state->configured, -2); return 3;
    }
    state->config = *config; state->epoch = epoch;
    InterlockedExchange(&state->configured, 1); return 0;
}
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_Attach(std::uint32_t kind, std::uint32_t slot,
    std::uint64_t epoch, const RizEngineNodeObserverV1* observer)
{
    if (kind > RizEngineNodeDetach || slot >= limit || !observer || observer->abiVersion != 1
        || observer->byteSize != sizeof(*observer) || !observer->enter || !observer->exit || epoch == 0) return 1;
    auto* state = &states[kind][slot];
    if (InterlockedCompareExchange(&state->unknown, 0, 0) != 0) return 5;
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) return 2;
    if (state->epoch != epoch) return 3;
    if (InterlockedCompareExchange(&state->attachAttempted, 1, 0) != 0) return 4;
    state->observer = *observer;
    InterlockedExchangePointer(&state->publishedObserver, &state->observer); return 0;
}
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInfo(std::uint32_t kind, std::uint32_t slot,
    RizEngineNodeInfoV1* info)
{
    if (kind > RizEngineNodeDetach || slot >= limit || !info || info->abiVersion != 1
        || info->byteSize != sizeof(*info)) return 1;
    auto* state = &states[kind][slot];
    const auto configured = InterlockedCompareExchange(&state->configured, 0, 0);
    RizEngineNodeInfoV1 result{}; result.abiVersion = 1; result.byteSize = sizeof(result);
    result.kind = kind; result.slot = slot; result.entry = RizEngineNodeContext_GetEntry(kind, slot);
    result.entered = ReadCounter(&state->entered); result.exited = ReadCounter(&state->exited);
    result.inFlight = ReadCounter(&state->inFlight); result.enterFaults = ReadCounter(&state->enterFaults);
    result.exitFaults = ReadCounter(&state->exitFaults); result.snapshotFaults = ReadCounter(&state->snapshotFaults);
    result.outputFaults = ReadCounter(&state->outputFaults);
    if (configured == 1) {
        result.target = state->config.target; result.original = state->config.original; result.epoch = state->epoch;
        result.attached = SnapshotObserver(state) ? 1u : 0u;
    }
    result.unknown = InterlockedCompareExchange(&state->unknown, 0, 0) != 0 || configured < 0 ? 1u : 0u;
#ifdef RIZ_ENGINE_INLINE_PROBE
    if (InterlockedCompareExchange(&inlineUnknown, 0, 0) != 0) result.unknown = 1;
#endif
    *info = result; return configured == 0 ? 2u : configured != 1 ? 3u : 0u;
}

#ifdef RIZ_ENGINE_INLINE_PROBE
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineAbiVersion() { return 1; }
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineLimit() { return static_cast<std::uint32_t>(limit); }
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineFrameSize() { return sizeof(RizEngineInlineFrameV1); }
RIZEIC_EXPORT void* __cdecl RizEngineNodeContext_GetInlineEntry() { return reinterpret_cast<void*>(&RizEngineInlineProbe_Entry); }
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_ConfigureInline(std::uint32_t slot, const RizEngineInlineConfigV1* config)
{
    if (slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || config->startSlot >= limit || !config->mainThreadId || !config->expectedStartEpoch
        || !config->expectedStartTarget || config->reserved) return 1;
    for (std::uint32_t site = 0; site != 3; ++site) {
        if (!config->sites[site].sitePc || !config->sites[site].relayReturnPc) return 1;
        for (std::uint32_t prior = 0; prior != site; ++prior)
            if (config->sites[prior].sitePc == config->sites[site].sitePc
                || config->sites[prior].relayReturnPc == config->sites[site].relayReturnPc) return 1;
    }
    auto* start = &states[RizEngineNodeStart][config->startSlot];
    if (InterlockedCompareExchange(&start->configured, 0, 0) != 1
        || start->epoch != config->expectedStartEpoch || start->config.target != config->expectedStartTarget) return 1;
    AcquireSRWLockExclusive(&inlineConfigurationLock);
    auto* family = &inlineStates[slot];
    std::uint32_t result = 0;
    if (InterlockedCompareExchange(&family->configured, 0, 0) != 0) result = 2;
    else {
        for (const auto& other : inlineStates) {
            if (other.configured == 0) continue;
            if (other.config.startSlot == config->startSlot) { result = 3; break; }
            for (const auto& oldSite : other.config.sites)
                for (const auto& newSite : config->sites)
                    if (oldSite.sitePc == newSite.sitePc || oldSite.relayReturnPc == newSite.relayReturnPc) result = 3;
            if (result != 0) break;
        }
        if (result == 0) {
            InterlockedExchange(&family->configured, -1);
            family->config = *config; family->epoch = NextPositive(&inlineEpochSource);
            if (family->epoch == 0) { FaultInline(family); InterlockedExchange(&family->configured, -2); result = 4; }
            else InterlockedExchange(&family->configured, 1);
        }
    }
    ReleaseSRWLockExclusive(&inlineConfigurationLock); return result;
}
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_AttachInline(std::uint32_t slot, std::uint64_t epoch,
    const RizEngineInlineObserverV1* observer)
{
    if (slot >= limit || !observer || observer->abiVersion != 1 || observer->byteSize != sizeof(*observer)
        || !observer->callback || !epoch) return 1;
    auto* family = &inlineStates[slot];
    if (InterlockedCompareExchange(&family->unknown, 0, 0) != 0) return 5;
    if (InterlockedCompareExchange(&family->configured, 0, 0) != 1) return 2;
    if (family->epoch != epoch) return 3;
    if (InterlockedCompareExchange(&family->attachAttempted, 1, 0) != 0) return 4;
    family->observer = *observer;
    InterlockedExchangePointer(&family->publishedObserver, &family->observer); return 0;
}
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineInfo(std::uint32_t slot, RizEngineInlineInfoV1* info)
{
    if (slot >= limit || !info || info->abiVersion != 1 || info->byteSize != sizeof(*info)) return 1;
    auto* family = &inlineStates[slot];
    const auto configured = InterlockedCompareExchange(&family->configured, 0, 0);
    RizEngineInlineInfoV1 result{};
    result.abiVersion = 1; result.byteSize = sizeof(result); result.slot = slot;
    result.configured = configured == 1 ? 1u : 0u;
    result.attached = InterlockedCompareExchangePointer(&family->publishedObserver, nullptr, nullptr) ? 1u : 0u;
    result.unknown = InterlockedCompareExchange(&family->unknown, 0, 0) != 0 || configured < 0 ? 1u : 0u;
    result.entry = RizEngineNodeContext_GetInlineEntry();
    if (configured == 1) {
        result.mainThreadId = family->config.mainThreadId; result.epoch = family->epoch;
        result.startEpoch = family->config.expectedStartEpoch; result.startTarget = family->config.expectedStartTarget;
    }
    result.startsEntered = ReadCounter(&family->startsEntered); result.startsExited = ReadCounter(&family->startsExited);
    result.startsInFlight = ReadCounter(&family->startsInFlight); result.begins = ReadCounter(&family->begins);
    result.endsFree = ReadCounter(&family->endsFree); result.endsRetained = ReadCounter(&family->endsRetained);
    result.probesInFlight = ReadCounter(&family->probesInFlight); result.faults = ReadCounter(&family->faults);
    result.snapshotFaults = ReadCounter(&family->snapshotFaults); result.orphanProbes = ReadCounter(&family->orphanProbes);
    result.unmatchedStarts = ReadCounter(&family->unmatchedStarts); result.events = ReadCounter(&family->events);
    *info = result; return configured == 0 ? 2u : configured != 1 ? 3u : 0u;
}
#endif
