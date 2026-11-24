#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <limits>
#include <utility>
#include "EngineSchedulerContext.h"
#ifdef RIZ_ENGINE_WAIT_CONTEXT
#include "EngineWaitContext.h"
#endif
#include "MidContext.h"

extern "C" void RizEngineSchedulerRow_Entry();

namespace {
constexpr std::size_t limit = 32, kindCount = 6, threadLimit = 64;
constexpr std::uint32_t depthLimit = 128;
using RawOriginal = std::uint64_t(__cdecl*)(void*, void*, void*, void*);
struct State {
    RizEngineSchedulerConfigV1 config{};
    RizEngineSchedulerObserverV1 observer{};
    void* volatile publishedObserver = nullptr;
    std::uint64_t epoch = 0;
    volatile LONG configured = 0, attachAttempted = 0, unknown = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, faults = 0, snapshotFaults = 0;
    volatile LONG64 rowBegins = 0, rowEnds = 0, rowsInFlight = 0, unmatchedRows = 0;
};
struct Thread { volatile LONG id = 0; void* volatile top = nullptr; };
struct Scope {
    State* state;
    const RizEngineSchedulerObserverV1* observer;
    Thread* thread;
    Scope* parent;
    RizEngineSchedulerFrameV1 frame;
    RizEngineSchedulerRowFrameV1 row;
    RizEngineSchedulerFrameV1 pendingConsume;
    std::uint64_t rowRsp, rowBegins, rowEnds;
    std::uint32_t phase;
    bool unknown, callbackActive, rowActive, pending;
#ifdef RIZ_ENGINE_WAIT_CONTEXT
    RizEngineWaitPrivate::BoundaryStorage* waitBoundary;
#endif
};
State states[kindCount][limit];
Thread threads[threadLimit];
SRWLOCK configurationLock = SRWLOCK_INIT;
volatile LONG globalUnknown = 0;
volatile LONG64 epochSource = 0, sequenceSource = 0, eventSource = 0;

std::uint64_t Next(volatile LONG64* source)
{
    auto current = InterlockedCompareExchange64(source, 0, 0);
    for (;;) {
        if (current < 0 || current == (std::numeric_limits<LONG64>::max)()) return 0;
        const auto next = current + 1;
        const auto found = InterlockedCompareExchange64(source, next, current);
        if (found == current) return static_cast<std::uint64_t>(next);
        current = found;
    }
}
std::uint64_t Count(volatile LONG64* source)
{ return static_cast<std::uint64_t>(InterlockedCompareExchange64(source, 0, 0)); }
void Fault(State* state, Scope* scope = nullptr)
{
    InterlockedExchange(&globalUnknown, 1);
    if (state) { InterlockedExchange(&state->unknown, 1); InterlockedIncrement64(&state->faults); }
    if (scope) scope->unknown = true;
}
Thread* FindThread(DWORD id)
{
    for (auto& thread : threads)
        if (static_cast<DWORD>(InterlockedCompareExchange(&thread.id, 0, 0)) == id) return &thread;
    return nullptr;
}
Thread* ClaimThread(DWORD id)
{
    if (auto* existing = FindThread(id)) return existing;
    for (auto& thread : threads)
        if (InterlockedCompareExchange(&thread.id, static_cast<LONG>(id), 0) == 0) return &thread;
    return nullptr;
}
Scope* Top(Thread* thread)
{ return thread ? static_cast<Scope*>(InterlockedCompareExchangePointer(&thread->top, nullptr, nullptr)) : nullptr; }
void Push(Scope* scope)
{
    scope->thread = ClaimThread(scope->frame.threadId);
    if (!scope->thread) { Fault(scope->state, scope); return; }
    scope->parent = Top(scope->thread);
    scope->frame.depth = scope->parent ? scope->parent->frame.depth + 1u : 1u;
    scope->frame.parentSequence = scope->parent ? scope->parent->frame.sequence : 0;
    scope->frame.rootSequence = scope->parent ? scope->parent->frame.rootSequence : scope->frame.sequence;
    if (!scope->frame.depth || scope->frame.depth > depthLimit) Fault(scope->state, scope);
    InterlockedExchangePointer(&scope->thread->top, scope);
}
void Pop(Scope* scope)
{
    if (!scope->thread) return;
    if (Top(scope->thread) != scope) { Fault(scope->state, scope); return; }
    InterlockedExchangePointer(&scope->thread->top, scope->parent);
}
std::uint64_t Read64(const void* value, std::size_t offset)
{ return *reinterpret_cast<const volatile std::uint64_t*>(static_cast<const unsigned char*>(value) + offset); }
std::uint32_t Read32(const void* value, std::size_t offset)
{ return *reinterpret_cast<const volatile std::uint32_t*>(static_cast<const unsigned char*>(value) + offset); }
void* ReadPointer(const void* value, std::size_t offset)
{ return reinterpret_cast<void*>(Read64(value, offset)); }
void* Offset(void* value, std::size_t offset)
{ return reinterpret_cast<void*>(reinterpret_cast<std::uintptr_t>(value) + offset); }
bool CopyRecord(const void* source, RizEngineSchedulerRecordV1* target)
{
    RizEngineSchedulerRecordV1 copy{};
    __try {
        copy.dueTimeBits = Read64(source, 0); copy.frame = static_cast<std::int64_t>(Read64(source, 8));
        copy.intervalBits = Read32(source, 0x10);
        copy.repeat = *(static_cast<const volatile unsigned char*>(source) + 0x14);
        copy.payload = ReadPointer(source, 0x18); copy.callback = ReadPointer(source, 0x20);
        copy.cleanup = ReadPointer(source, 0x28); copy.ownerId = Read32(source, 0x30);
        copy.category = Read32(source, 0x34); copy.creationTag = Read32(source, 0x38);
        *target = copy; return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool KnownCallback(const void* value)
{
    if (!value) return false;
    for (std::uint32_t slot = 0; slot != limit; ++slot) {
        auto* state = &states[RizEngineSchedulerCallback][slot];
        if (InterlockedCompareExchange(&state->configured, 0, 0) == 1 && value == state->config.target) return true;
    }
    return false;
}
bool CopyNode(void* node, void** owner, std::uint64_t* handle, void** cache,
    std::uint32_t* type, std::int32_t* references)
{
    __try {
        const auto actualOwner = ReadPointer(node, 0x58);
        const auto actualHandle = Read64(node, 0x10);
        const auto actualType = Read32(node, 0x18);
        const auto actualCache = ReadPointer(node, 0x20);
        const auto actualReferences = static_cast<std::int32_t>(Read32(node, 0x60));
        *owner = actualOwner; *handle = actualHandle; *cache = actualCache;
        *type = actualType; *references = actualReferences; return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void SnapshotFault(Scope* scope)
{ InterlockedIncrement64(&scope->state->snapshotFaults); Fault(scope->state, scope); }
void SnapshotNode(Scope* scope, void* node)
{
    auto* frame = &scope->frame;
    frame->nodeSnapshotFlags = RizEngineSchedulerSnapshotRequested;
    if (CopyNode(node, &frame->nodeOwner, &frame->enumHandle, &frame->enumCache, &frame->enumType, &frame->referenceCount))
        frame->nodeSnapshotFlags |= RizEngineSchedulerSnapshotValid;
    else { frame->nodeSnapshotFlags |= RizEngineSchedulerSnapshotFaulted; SnapshotFault(scope); }
}
void ReadRowInput(Scope* scope)
{
    auto* frame = &scope->frame;
    switch (frame->kind) {
    case RizEngineSchedulerRegister:
        frame->tree = frame->arg0; frame->sourceRecord = frame->arg2; frame->outputSlot = frame->arg1; break;
    case RizEngineSchedulerRemove:
        frame->manager = frame->arg0; frame->tree = Offset(frame->arg0, 0x30);
        frame->entry = frame->arg2; frame->sourceRecord = frame->arg1;
        if (frame->sourceRecord != Offset(frame->entry, 0x20)) Fault(scope->state, scope);
        break;
    case RizEngineSchedulerConsume:
        frame->manager = frame->arg0; frame->tree = Offset(frame->arg0, 0x30);
        frame->entry = frame->arg2; frame->sourceRecord = Offset(frame->entry, 0x20); break;
    case RizEngineSchedulerPump: case RizEngineSchedulerDrain:
        frame->manager = frame->arg0; frame->tree = Offset(frame->arg0, 0x30); return;
    case RizEngineSchedulerCallback:
        frame->record.payload = frame->arg1; frame->record.callback = scope->state->config.target;
        SnapshotNode(scope, frame->arg1); return;
    default: Fault(scope->state, scope); return;
    }
    frame->flags |= RizEngineSchedulerSnapshotRequested;
    if (CopyRecord(frame->sourceRecord, &frame->record)) {
        frame->flags |= RizEngineSchedulerSnapshotValid;
        if (KnownCallback(frame->record.callback)) SnapshotNode(scope, frame->record.payload);
    }
    else { frame->flags |= RizEngineSchedulerSnapshotFaulted; SnapshotFault(scope); }
}
bool CanUseSource(Scope* scope, Scope* source, bool direct)
{
#ifdef RIZ_ENGINE_WAIT_CONTEXT
    return scope->waitBoundary && source
        && RizEngineWaitPrivate::CanUseSchedulerSource(scope->waitBoundary, &source->frame, direct);
#else
    (void)scope; (void)source; (void)direct;
    return true;
#endif
}
void AttachSource(Scope* scope)
{
    auto* frame = &scope->frame;
    auto* parent = scope->parent;
    if (frame->kind == RizEngineSchedulerCallback && parent
        && parent->frame.kind == RizEngineSchedulerPump && parent->phase == 1 && !parent->callbackActive && parent->pending
        && CanUseSource(scope, parent, true)) {
        const auto* pending = &parent->pendingConsume;
        frame->sourceSequence = pending->sequence;
        frame->flags |= RizEngineSchedulerSourceConsume | RizEngineSchedulerSnapshotCached
            | (pending->flags & (RizEngineSchedulerSnapshotRequested | RizEngineSchedulerSnapshotValid | RizEngineSchedulerSnapshotFaulted));
        frame->tree = pending->tree; frame->manager = pending->manager; frame->entry = pending->entry;
        frame->sourceRecord = pending->sourceRecord; frame->record = pending->record;
        if (frame->arg1 != pending->record.payload || pending->record.callback != scope->state->config.target) {
            Fault(parent->state, parent); Fault(scope->state, scope);
        }
        parent->pending = false; return;
    }
    // Only the nearest real Drain may supply a row. An intervening foreign or
    // rowless Drain masks an older one, even when payload addresses coincide.
    for (auto* at = parent; at; at = at->parent) {
        // An observer's reentrant child is not an original-function descendant
        // of this row. Every intervening actual scope must remain in original
        // phase; never skip its Enter/Exit/row callback to borrow an ancestor.
        if (at->phase != 1 || at->callbackActive) return;
        if (at->frame.kind == RizEngineSchedulerDrain) {
            if (at->rowActive && CanUseSource(scope, at, false)) {
                frame->sourceSequence = at->row.rowSequence; frame->flags |= RizEngineSchedulerSourceDrainRow;
            }
            return;
        }
    }
}
std::uintptr_t Enter(Scope* scope)
{
    if (!scope->observer) return 0;
    const auto prior = scope->callbackActive; scope->callbackActive = true;
    std::uintptr_t token = 0;
    __try { token = scope->observer->enter(&scope->frame, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(scope->state, scope); }
    scope->callbackActive = prior; return token;
}
void Exit(Scope* scope, std::uintptr_t token, const RizEngineSchedulerCompletionV1* completion)
{
    if (!scope->observer) return;
    const auto prior = scope->callbackActive; scope->callbackActive = true;
    __try { scope->observer->exit(&scope->frame, token, completion, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(scope->state, scope); }
    scope->callbackActive = prior;
}
void EmitRow(Scope* scope, std::uint32_t kind, const void* returnPc)
{
    RizEngineSchedulerRowFrameV1 record;
    __movsb(reinterpret_cast<unsigned char*>(&record), reinterpret_cast<const unsigned char*>(&scope->row), sizeof(record));
    record.kind = kind; record.eventSequence = Next(&eventSource);
    if (kind < 2) {
        record.sitePc = scope->state->config.sites[kind].sitePc; record.relayReturnPc = returnPc;
    }
    else { record.sitePc = nullptr; record.relayReturnPc = nullptr; }
    if (!record.eventSequence) Fault(scope->state, scope);
    if (scope->unknown || InterlockedCompareExchange(&globalUnknown, 0, 0)) record.flags |= RizEngineSchedulerUnknown;
    if (!scope->observer || !scope->observer->row) return;
    const auto prior = scope->callbackActive; scope->callbackActive = true;
    __try { scope->observer->row(&record, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(scope->state, scope); }
    scope->callbackActive = prior;
}
void FinishRows(Scope* scope, RizEngineSchedulerCompletionV1* completion)
{
    if (scope->rowActive) {
        Fault(scope->state, scope); InterlockedIncrement64(&scope->state->unmatchedRows);
        completion->flags |= RizEngineSchedulerMissingRow;
        EmitRow(scope, RizEngineSchedulerRowAbnormal, nullptr);
        scope->rowActive = false; InterlockedDecrement64(&scope->state->rowsInFlight);
    }
    if (scope->pending) { Fault(scope->state, scope); scope->pending = false; }
    completion->rowBegins = scope->rowBegins; completion->rowEnds = scope->rowEnds;
}
void TransferConsume(Scope* scope)
{
    auto* parent = scope->parent;
    if (scope->frame.kind != RizEngineSchedulerConsume || !parent
        || parent->frame.kind != RizEngineSchedulerPump || parent->phase != 1 || parent->callbackActive
        || !CanUseSource(scope, parent, true)) return;
    // The generic scheduler also consumes foreign records. Their callback does
    // not enter this family's known Coroutine callback channel, so no such
    // completion can be expected. Keep the physical Consume mask and snapshot,
    // but reserve the pending handoff only for the actually configured target.
    if ((scope->frame.flags & (RizEngineSchedulerSnapshotRequested | RizEngineSchedulerSnapshotValid
            | RizEngineSchedulerSnapshotFaulted))
            != (RizEngineSchedulerSnapshotRequested | RizEngineSchedulerSnapshotValid)
        || !KnownCallback(scope->frame.record.callback)) return;
    if (parent->pending || scope->frame.manager != parent->frame.arg0) Fault(parent->state, parent);
    __movsb(reinterpret_cast<unsigned char*>(&parent->pendingConsume),
        reinterpret_cast<const unsigned char*>(&scope->frame), sizeof(scope->frame));
    parent->pending = true;
}
__declspec(noinline) std::uint64_t Dispatch(State* state, const RizEngineSchedulerObserverV1* observer,
    std::uint32_t kind, std::uint32_t slot, void* a0, void* a1, void* a2, void* a3, const void* caller, DWORD incomingError)
{
    Scope scope;
    __stosb(reinterpret_cast<unsigned char*>(&scope), 0, sizeof(scope));
    scope.state = state; scope.observer = observer;
    auto* frame = &scope.frame;
    frame->abiVersion = 1; frame->byteSize = sizeof(*frame); frame->kind = kind; frame->slot = slot;
    frame->arg0 = a0; frame->arg1 = a1; frame->arg2 = a2; frame->arg3 = a3; frame->caller = caller;
    frame->epoch = state->epoch; frame->sequence = Next(&sequenceSource); frame->threadId = GetCurrentThreadId();
    InterlockedIncrement64(&state->entered); InterlockedIncrement64(&state->inFlight);
    Push(&scope);
    if (!frame->sequence || InterlockedCompareExchange(&state->configured, 0, 0) != 1) Fault(state, &scope);
    if (frame->threadId == state->config.mainThreadId) frame->flags |= RizEngineSchedulerMainThread;
    else Fault(state, &scope);
    if (scope.thread && Top(scope.thread) == &scope && frame->depth && frame->depth <= depthLimit)
        frame->flags |= RizEngineSchedulerParentMatched;
#ifdef RIZ_ENGINE_WAIT_CONTEXT
    RizEngineWaitPrivate::BoundaryStorage waitBoundary;
    RizEngineWaitPrivate::BeginScheduler(&waitBoundary, frame, state->config.target);
    scope.waitBoundary = &waitBoundary;
#endif
    ReadRowInput(&scope); AttachSource(&scope);
    if (scope.unknown || InterlockedCompareExchange(&globalUnknown, 0, 0)) frame->flags |= RizEngineSchedulerUnknown;
#ifdef RIZ_ENGINE_WAIT_CONTEXT
    RizEngineWaitPrivate::Entered(&waitBoundary);
#endif
    SetLastError(incomingError);
    const auto token = Enter(&scope);
    RizEngineSchedulerCompletionV1 completion{};
    completion.abiVersion = 1; completion.byteSize = sizeof(completion);
    if (scope.unknown) completion.flags |= RizEngineSchedulerEnterFaulted;
    std::uint64_t raw = 0; bool returned = false;
    DWORD originalError = incomingError;
    scope.phase = 1;
#ifdef RIZ_ENGINE_WAIT_CONTEXT
    RizEngineWaitPrivate::Original(&waitBoundary);
#endif
    SetLastError(incomingError);
    __try {
        raw = reinterpret_cast<RawOriginal>(state->config.original)(a0, a1, a2, a3);
        originalError = GetLastError(); returned = true;
    }
    __finally {
        if (!returned) originalError = GetLastError();
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        // Mask every side/typed closing callback, including abnormal unwind.
        RizEngineWaitPrivate::Closing(&waitBoundary);
#endif
        if (!returned) { Fault(state, &scope); completion.flags |= RizEngineSchedulerAbnormal; }
        scope.phase = 2;
        if (returned) {
            completion.flags |= RizEngineSchedulerReturned | RizEngineSchedulerRawRaxValid;
            completion.rawRax = raw;
            if (kind == RizEngineSchedulerRegister) {
                completion.outputFlags = RizEngineSchedulerSnapshotRequested;
                __try { completion.outputEntry = ReadPointer(a1, 0); completion.outputFlags |= RizEngineSchedulerSnapshotValid; }
                __except (EXCEPTION_EXECUTE_HANDLER) { completion.outputFlags |= RizEngineSchedulerSnapshotFaulted; SnapshotFault(&scope); }
            }
            TransferConsume(&scope);
        }
        FinishRows(&scope, &completion);
        if (scope.unknown || InterlockedCompareExchange(&globalUnknown, 0, 0)) completion.flags |= RizEngineSchedulerExitUnknown;
        SetLastError(originalError); Exit(&scope, token, &completion);
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        RizEngineWaitPrivate::CompleteScheduler(&waitBoundary, &completion);
#endif
        Pop(&scope); InterlockedIncrement64(&state->exited); InterlockedDecrement64(&state->inFlight);
#ifdef RIZ_ENGINE_WAIT_CONTEXT
        RizEngineWaitPrivate::Pop(&waitBoundary);
#endif
        SetLastError(originalError);
    }
    return raw;
}
template<std::size_t Kind, std::size_t Slot>
__declspec(noinline) std::uint64_t __cdecl Entry(void* a0, void* a1, void* a2, void* a3)
{
    const DWORD incomingError = GetLastError();
    auto* state = &states[Kind][Slot];
    const auto* observer = static_cast<const RizEngineSchedulerObserverV1*>(
        InterlockedCompareExchangePointer(&state->publishedObserver, nullptr, nullptr));
    return Dispatch(state, observer, static_cast<std::uint32_t>(Kind), static_cast<std::uint32_t>(Slot),
        a0, a1, a2, a3, _ReturnAddress(), incomingError);
}
template<std::size_t Kind, std::size_t... Slots> constexpr auto Entries(std::index_sequence<Slots...>)
{ return std::array<RawOriginal, sizeof...(Slots)>{ &Entry<Kind, Slots>... }; }
constexpr auto registerEntries = Entries<RizEngineSchedulerRegister>(std::make_index_sequence<limit>{});
constexpr auto removeEntries = Entries<RizEngineSchedulerRemove>(std::make_index_sequence<limit>{});
constexpr auto consumeEntries = Entries<RizEngineSchedulerConsume>(std::make_index_sequence<limit>{});
constexpr auto pumpEntries = Entries<RizEngineSchedulerPump>(std::make_index_sequence<limit>{});
constexpr auto drainEntries = Entries<RizEngineSchedulerDrain>(std::make_index_sequence<limit>{});
constexpr auto callbackEntries = Entries<RizEngineSchedulerCallback>(std::make_index_sequence<limit>{});
bool IsEntry(const void* value)
{
    if (value == reinterpret_cast<const void*>(&RizEngineSchedulerRow_Entry)) return true;
    for (std::uint32_t kind = 0; kind != kindCount; ++kind)
        for (std::uint32_t slot = 0; slot != limit; ++slot)
            if (value == RizEngineSchedulerContext_GetEntry(kind, slot)) return true;
    return false;
}
void BeginRow(Scope* scope, const RizEventMidFrameV1* machine)
{
    if (scope->rowActive) {
        Fault(scope->state, scope); InterlockedIncrement64(&scope->state->unmatchedRows); return;
    }
    RizEngineSchedulerRowFrameV1 row;
    __stosb(reinterpret_cast<unsigned char*>(&row), 0, sizeof(row));
    row.abiVersion = 1; row.byteSize = sizeof(row); row.kind = RizEngineSchedulerRowBegin;
    row.slot = scope->frame.slot; row.threadId = scope->frame.threadId; row.depth = scope->frame.depth;
    row.epoch = scope->frame.epoch; row.dispatchSequence = scope->frame.sequence;
    row.parentSequence = scope->frame.parentSequence; row.rootSequence = scope->frame.rootSequence;
    row.rowSequence = Next(&sequenceSource); row.tree = scope->frame.tree; row.manager = scope->frame.manager;
    row.entry = reinterpret_cast<void*>(machine->gpr[RizEventMidRbx]);
    row.flags = RizEngineSchedulerSnapshotRequested | RizEngineSchedulerParentMatched;
    if (row.threadId == scope->state->config.mainThreadId) row.flags |= RizEngineSchedulerMainThread;
    if (!row.rowSequence) Fault(scope->state, scope);
    if (CopyRecord(Offset(row.entry, 0x20), &row.record)) {
        row.flags |= RizEngineSchedulerSnapshotValid;
        if (KnownCallback(row.record.callback)) {
            row.nodeSnapshotFlags = RizEngineSchedulerSnapshotRequested;
            if (CopyNode(row.record.payload, &row.nodeOwner, &row.enumHandle, &row.enumCache, &row.enumType, &row.referenceCount))
                row.nodeSnapshotFlags |= RizEngineSchedulerSnapshotValid;
            else { row.nodeSnapshotFlags |= RizEngineSchedulerSnapshotFaulted; SnapshotFault(scope); }
        }
    }
    else { row.flags |= RizEngineSchedulerSnapshotFaulted; SnapshotFault(scope); }
    __movsb(reinterpret_cast<unsigned char*>(&scope->row), reinterpret_cast<const unsigned char*>(&row), sizeof(row));
    scope->rowActive = true; scope->rowRsp = machine->gpr[RizEventMidRsp];
    ++scope->rowBegins; InterlockedIncrement64(&scope->state->rowBegins); InterlockedIncrement64(&scope->state->rowsInFlight);
    EmitRow(scope, RizEngineSchedulerRowBegin, reinterpret_cast<const void*>(machine->returnPc));
}
void EndRow(Scope* scope, const RizEventMidFrameV1* machine)
{
    if (!scope->rowActive) {
        Fault(scope->state, scope); InterlockedIncrement64(&scope->state->unmatchedRows); return;
    }
    // The entry and payload can already be freed/reused. Check only cached
    // identities against preserved register values, never dereference them.
    if (scope->row.entry != reinterpret_cast<void*>(machine->gpr[RizEventMidRbx])
        || scope->rowRsp != machine->gpr[RizEventMidRsp]) Fault(scope->state, scope);
    ++scope->rowEnds; InterlockedIncrement64(&scope->state->rowEnds);
    EmitRow(scope, RizEngineSchedulerRowEnd, reinterpret_cast<const void*>(machine->returnPc));
    scope->rowActive = false; InterlockedDecrement64(&scope->state->rowsInFlight);
}
}

extern "C" void __cdecl RizEngineSchedulerRow_Dispatch(const RizEventMidFrameV1* machine)
{
    const DWORD incomingError = GetLastError();
    auto* scope = Top(FindThread(GetCurrentThreadId()));
    State* selected = nullptr; std::uint32_t site = 0;
    __try {
        if (machine && machine->abiVersion == 1 && machine->byteSize == sizeof(*machine)
            && !machine->reserved0 && !machine->reserved1) {
            for (auto& state : states[RizEngineSchedulerDrain]) {
                if (InterlockedCompareExchange(&state.configured, 0, 0) != 1) continue;
                for (std::uint32_t i = 0; i != 2; ++i)
                    if (state.config.sites[i].relayReturnPc == reinterpret_cast<const void*>(machine->returnPc))
                    { selected = &state; site = i; break; }
                if (selected) break;
            }
        }
        if (!selected || !scope || scope->state != selected || scope->frame.kind != RizEngineSchedulerDrain
            || scope->phase != 1 || scope->callbackActive || !scope->thread || Top(scope->thread) != scope
            || !scope->frame.sequence || !scope->frame.depth || scope->frame.depth > depthLimit
            || reinterpret_cast<void*>(machine->gpr[RizEventMidRbp]) != scope->frame.arg0) {
            Fault(selected, scope);
            if (scope && scope->state != selected) Fault(scope->state, scope);
            if (selected) InterlockedIncrement64(&selected->unmatchedRows);
        }
        else {
            SetLastError(incomingError);
            if (!site) BeginRow(scope, machine); else EndRow(scope, machine);
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        Fault(selected, scope); if (scope && scope->state != selected) Fault(scope->state, scope);
    }
    SetLastError(incomingError);
}

RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetAbiVersion() { return 1; }
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetLimit() { return static_cast<std::uint32_t>(limit); }
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetFrameSize() { return sizeof(RizEngineSchedulerFrameV1); }
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetCompletionSize() { return sizeof(RizEngineSchedulerCompletionV1); }
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetRowFrameSize() { return sizeof(RizEngineSchedulerRowFrameV1); }
RIZESC_EXPORT void* __cdecl RizEngineSchedulerContext_GetEntry(std::uint32_t kind, std::uint32_t slot)
{
    if (kind >= kindCount || slot >= limit) return nullptr;
    const RawOriginal entries[] = { registerEntries[slot], removeEntries[slot], consumeEntries[slot],
        pumpEntries[slot], drainEntries[slot], callbackEntries[slot] };
    return reinterpret_cast<void*>(entries[kind]);
}
RIZESC_EXPORT void* __cdecl RizEngineSchedulerContext_GetDrainRowEntry() { return reinterpret_cast<void*>(&RizEngineSchedulerRow_Entry); }
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_Configure(std::uint32_t kind, std::uint32_t slot,
    const RizEngineSchedulerConfigV1* supplied)
{
    const DWORD incomingError = GetLastError();
    RizEngineSchedulerConfigV1 config{}; bool valid = false;
    __try { if (supplied) { config = *supplied; valid = true; } }
    __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (!valid || kind >= kindCount || slot >= limit || config.abiVersion != 1 || config.byteSize != sizeof(config)
        || !config.mainThreadId || config.reserved || !config.target || !config.original
        || config.target == config.original || IsEntry(config.target) || IsEntry(config.original))
    { SetLastError(incomingError); return 1; }
    for (std::uint32_t i = 0; i != 2; ++i)
        if (kind == RizEngineSchedulerDrain ? (!config.sites[i].sitePc || !config.sites[i].relayReturnPc)
            : (config.sites[i].sitePc || config.sites[i].relayReturnPc))
        { SetLastError(incomingError); return 1; }
    if (kind == RizEngineSchedulerDrain && (config.sites[0].sitePc == config.sites[1].sitePc
        || config.sites[0].relayReturnPc == config.sites[1].relayReturnPc)) { SetLastError(incomingError); return 1; }
    AcquireSRWLockExclusive(&configurationLock);
    auto* state = &states[kind][slot]; std::uint32_t result = 0;
    if (InterlockedCompareExchange(&state->configured, 0, 0)) result = 2;
    else {
        for (auto& table : states) for (auto& other : table) {
            if (InterlockedCompareExchange(&other.configured, 0, 0) == 0) continue;
            if (other.config.target == config.target || other.config.original == config.original
                || other.config.target == config.original || other.config.original == config.target) result = 3;
            if (kind == RizEngineSchedulerDrain && &table == &states[RizEngineSchedulerDrain])
                for (const auto& oldSite : other.config.sites) for (const auto& newSite : config.sites)
                    if (oldSite.sitePc == newSite.sitePc || oldSite.relayReturnPc == newSite.relayReturnPc) result = 3;
        }
        if (!result) {
            InterlockedExchange(&state->configured, -1); state->config = config; state->epoch = Next(&epochSource);
            if (!state->epoch) { Fault(state); InterlockedExchange(&state->configured, -2); result = 4; }
            else InterlockedExchange(&state->configured, 1);
        }
    }
    ReleaseSRWLockExclusive(&configurationLock); SetLastError(incomingError); return result;
}
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_Attach(std::uint32_t kind, std::uint32_t slot,
    std::uint64_t epoch, const RizEngineSchedulerObserverV1* supplied)
{
    const DWORD incomingError = GetLastError();
    RizEngineSchedulerObserverV1 observer{}; bool valid = false;
    __try { if (supplied) { observer = *supplied; valid = true; } }
    __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (!valid || kind >= kindCount || slot >= limit || observer.abiVersion != 1 || observer.byteSize != sizeof(observer)
        || !observer.enter || !observer.exit || !epoch
        || (kind == RizEngineSchedulerDrain ? !observer.row : observer.row != nullptr)) { SetLastError(incomingError); return 1; }
    auto* state = &states[kind][slot]; std::uint32_t result = 0;
    if (InterlockedCompareExchange(&globalUnknown, 0, 0) || InterlockedCompareExchange(&state->unknown, 0, 0)) result = 5;
    else if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) result = 2;
    else if (state->epoch != epoch) result = 3;
    else if (InterlockedCompareExchange(&state->attachAttempted, 1, 0)) result = 4;
    else { state->observer = observer; InterlockedExchangePointer(&state->publishedObserver, &state->observer); }
    SetLastError(incomingError); return result;
}
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetInfo(std::uint32_t kind, std::uint32_t slot,
    RizEngineSchedulerInfoV1* supplied)
{
    const DWORD incomingError = GetLastError(); bool valid = false;
    __try { valid = supplied && supplied->abiVersion == 1 && supplied->byteSize == sizeof(*supplied); }
    __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (!valid || kind >= kindCount || slot >= limit) { SetLastError(incomingError); return 1; }
    auto* state = &states[kind][slot]; const auto configured = InterlockedCompareExchange(&state->configured, 0, 0);
    RizEngineSchedulerInfoV1 info;
    __stosb(reinterpret_cast<unsigned char*>(&info), 0, sizeof(info));
    info.abiVersion = 1; info.byteSize = sizeof(info); info.kind = kind; info.slot = slot;
    info.configured = configured == 1 ? 1u : 0u;
    info.attached = InterlockedCompareExchangePointer(&state->publishedObserver, nullptr, nullptr) ? 1u : 0u;
    info.unknown = configured < 0 || InterlockedCompareExchange(&globalUnknown, 0, 0) || InterlockedCompareExchange(&state->unknown, 0, 0) ? 1u : 0u;
    info.entry = RizEngineSchedulerContext_GetEntry(kind, slot); info.rowEntry = RizEngineSchedulerContext_GetDrainRowEntry();
    if (configured == 1) {
        info.mainThreadId = state->config.mainThreadId; info.target = state->config.target;
        info.original = state->config.original; info.epoch = state->epoch;
    }
    info.entered = Count(&state->entered); info.exited = Count(&state->exited); info.inFlight = Count(&state->inFlight);
    info.faults = Count(&state->faults); info.snapshotFaults = Count(&state->snapshotFaults);
    info.rowBegins = Count(&state->rowBegins); info.rowEnds = Count(&state->rowEnds);
    info.rowsInFlight = Count(&state->rowsInFlight); info.unmatchedRows = Count(&state->unmatchedRows);
    std::uint32_t result = configured == 0 ? 2u : configured != 1 ? 3u : 0u;
    __try { __movsb(reinterpret_cast<unsigned char*>(supplied), reinterpret_cast<const unsigned char*>(&info), sizeof(info)); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(state); result = 1; }
    SetLastError(incomingError); return result;
}
