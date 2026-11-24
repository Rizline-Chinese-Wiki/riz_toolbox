#include <Windows.h>
#include <string.h>
#include <limits.h>
#include "Fixture.h"
#include "../RizToolbox.JudgeContext/VoidContext.h"

extern "C" { volatile LONG RtInitPlayInputRegister = 0; }
extern "C" { void* RtEventMidActualList4 = nullptr; }

static_assert(sizeof(void*) == 8, "The fixture requires Windows x64.");
static_assert(sizeof(RtInitRequest) == 48 && sizeof(RtInitResult) == 24 && sizeof(RtInitObservation) == 64,
    "The fixture's managed/native record layout changed.");
static_assert(sizeof(RtInitCatchResult) == 40, "The native exception receipt layout changed.");
namespace {
SRWLOCK gate = SRWLOCK_INIT;
RtInitObservation observations[RtKindCount]{};
void* volatile observerPointer{};
void* volatile observerContext{};
void* volatile referenceResult{};
volatile LONG originalFault[RtKindCount]{};
constexpr DWORD originalSehCode = 0xE04F494E;
constexpr uint64_t originalPayload = 0xBADC0FFEE0DDF00DULL;
struct OriginalCppException { uint32_t kind; uint64_t payload; };

float Float(uint32_t bits) { float result; memcpy(&result, &bits, sizeof(result)); return result; }
uint32_t Bits(float value) { uint32_t result; memcpy(&result, &value, sizeof(result)); return result; }
void Enter(RtInitKind kind, void* self, void* argument, void* method, uint32_t scalar, uint64_t returned = 0)
{
    AcquireSRWLockExclusive(&gate);
    auto& item = observations[kind];
    item.size = sizeof(item); item.kind = kind; ++item.entered;
    item.self = self; item.argument = argument; item.method = method;
    item.scalar = scalar; item.returned = returned; item.threadId = GetCurrentThreadId();
    ReleaseSRWLockExclusive(&gate);
    const auto callback = reinterpret_cast<RtInitObserver>(InterlockedCompareExchangePointer(&observerPointer, nullptr, nullptr));
    if (callback) callback(kind, InterlockedCompareExchangePointer(&observerContext, nullptr, nullptr));
}
void Exit(RtInitKind kind)
{
    AcquireSRWLockExclusive(&gate); ++observations[kind].exited; ReleaseSRWLockExclusive(&gate);
}
struct OriginalExit {
    RtInitKind kind;
    ~OriginalExit() { Exit(kind); }
};
void RaiseOriginalFault(RtInitKind kind)
{
    const auto mode = InterlockedCompareExchange(&originalFault[kind], 0, 0);
    const auto payload = originalPayload ^ static_cast<uint64_t>(kind);
    if (mode == 1) {
        const ULONG_PTR arguments[]{ static_cast<ULONG_PTR>(kind), static_cast<ULONG_PTR>(payload) };
        RaiseException(originalSehCode, 0, 2, arguments);
    }
    else if (mode == 2) throw OriginalCppException{ static_cast<uint32_t>(kind), payload };
}

// The MASM entry prefix gives Dobby an explicit, distinct patchable prologue.
// The tail jump preserves exactly the original argument registers/stack.
extern "C" void __cdecl RtInitStartEntry(void*, void*);
extern "C" void* __cdecl RtInitGetterEntry(void*, void*, void*);
extern "C" void __cdecl RtInitPlayEntry(void*, uint8_t, void*);
extern "C" void __cdecl RtInitCallPlayRaw(void* entry, void* self, uint32_t rawEdx, void* method);
extern "C" void __cdecl RtInitTooltipEntry(void*, float, void*);
extern "C" void __cdecl RtInitProgressEntry(void*, void*);
extern "C" void __cdecl RtInitModeEntry(int32_t, void*);
struct TimelineHandle { uint64_t value; };
extern "C" TimelineHandle __cdecl RtInitInstantaneousEntry(void*, void*, void*, uint32_t, float, int32_t, void*);
extern "C" void __cdecl RtInitEndRangeEntry(TimelineHandle, float, void*);
}

extern "C" void __cdecl RtInitStartBody(void* self, void* method)
{
    Enter(RtStart, self, nullptr, method, 0); OriginalExit exit{ RtStart };
    // The managed observer has already returned before an original may throw.
    // Unwinding must remain entirely in native caller/helper/original frames.
    RaiseOriginalFault(RtStart);
}
extern "C" void* __cdecl RtInitGetterBody(void* self, void* info, void* method)
{
    auto result = InterlockedCompareExchangePointer(&referenceResult, nullptr, nullptr);
    Enter(RtGetter, self, info, method, 0, reinterpret_cast<uint64_t>(result)); OriginalExit exit{ RtGetter };
    RaiseOriginalFault(RtGetter); return result;
}
extern "C" void __cdecl RtInitPlayBody(void* self, uint8_t play, void* method)
{ Enter(RtPlay, self, nullptr, method, play); Exit(RtPlay); }
extern "C" void __cdecl RtInitTooltipBody(void* text, float offset, void* method)
{ Enter(RtTooltip, text, nullptr, method, Bits(offset)); Exit(RtTooltip); }
extern "C" void __cdecl RtInitProgressBody(void* self, void* method)
{ Enter(RtProgress, self, nullptr, method, 0); Exit(RtProgress); }
extern "C" void __cdecl RtInitModeBody(int32_t mode, void* method)
{ Enter(RtMode, nullptr, nullptr, method, static_cast<uint32_t>(mode)); Exit(RtMode); }
extern "C" TimelineHandle __cdecl RtInitInstantaneousBody(void* title, void* description, void* icon,
    uint32_t priority, float offset, int32_t clip, void* method)
{
    // These old authorities are present to exercise the real shared binding.
    // New ABI assertions are made against the four entries above.
    UNREFERENCED_PARAMETER(icon); UNREFERENCED_PARAMETER(priority); UNREFERENCED_PARAMETER(clip);
    Enter(RtInstantaneous, title, description, method, Bits(offset), 0xF123456789ABCDEFULL);
    Exit(RtInstantaneous); return { 0xF123456789ABCDEFULL };
}
extern "C" void __cdecl RtInitEndRangeBody(TimelineHandle handle, float offset, void* method)
{ Enter(RtEndRange, nullptr, nullptr, method, Bits(offset), handle.value); Exit(RtEndRange); }

RT_INIT_API void* __cdecl RtInitTarget(uint32_t kind)
{
    switch (kind) {
    case RtStart: return reinterpret_cast<void*>(&RtInitStartEntry);
    case RtGetter: return reinterpret_cast<void*>(&RtInitGetterEntry);
    case RtPlay: return reinterpret_cast<void*>(&RtInitPlayEntry);
    case RtTooltip: return reinterpret_cast<void*>(&RtInitTooltipEntry);
    case RtProgress: return reinterpret_cast<void*>(&RtInitProgressEntry);
    case RtMode: return reinterpret_cast<void*>(&RtInitModeEntry);
    case RtInstantaneous: return reinterpret_cast<void*>(&RtInitInstantaneousEntry);
    case RtEndRange: return reinterpret_cast<void*>(&RtInitEndRangeEntry);
    default: return nullptr;
    }
}
RT_INIT_API uint32_t __cdecl RtInitReset()
{
    AcquireSRWLockExclusive(&gate);
    for (const auto& item : observations) if (item.entered != item.exited) { ReleaseSRWLockExclusive(&gate); return 0; }
    memset(observations, 0, sizeof(observations));
    for (auto& mode : originalFault) InterlockedExchange(&mode, 0);
    ReleaseSRWLockExclusive(&gate); return 1;
}
RT_INIT_API uint32_t __cdecl RtInitRead(uint32_t kind, RtInitObservation* observation)
{
    if (kind >= RtKindCount || !observation || observation->size != sizeof(*observation)) return 0;
    AcquireSRWLockShared(&gate); *observation = observations[kind]; ReleaseSRWLockShared(&gate);
    observation->size = sizeof(*observation); observation->kind = kind; return 1;
}
RT_INIT_API void __cdecl RtInitSetObserver(RtInitObserver observer, void* context)
{
    // Only set at a quiescent test boundary. The DLL and delegates stay loaded
    // for process lifetime, including after logical binding detachment.
    InterlockedExchangePointer(&observerContext, context);
    InterlockedExchangePointer(&observerPointer, reinterpret_cast<void*>(observer));
}
RT_INIT_API void __cdecl RtInitSetReference(void* reference) { InterlockedExchangePointer(&referenceResult, reference); }
RT_INIT_API uint32_t __cdecl RtInitPlayInputBits()
{ return static_cast<uint32_t>(InterlockedCompareExchange(&RtInitPlayInputRegister, 0, 0)); }
RT_INIT_API uint32_t __cdecl RtInitSetFault(uint32_t kind, uint32_t mode)
{
    if ((kind != RtStart && kind != RtGetter) || mode > 2) return 0;
    InterlockedExchange(&originalFault[kind], static_cast<LONG>(mode)); return 1;
}
namespace {
bool ValidRequest(const RtInitRequest* request)
{
    return request && request->size == sizeof(*request) && request->kind < RtKindCount && request->entry && request->reserved == 0;
}
void* InvokeRaw(const RtInitRequest* request)
{
    switch (request->kind) {
    case RtStart: case RtProgress:
        reinterpret_cast<void(__cdecl*)(void*, void*)>(request->entry)(request->self, request->method); break;
    case RtGetter:
        return reinterpret_cast<void*(__cdecl*)(void*, void*, void*)>(request->entry)(request->self, request->argument, request->method);
    case RtPlay:
        // Preserve the complete EDX poison. Only DL belongs to the Boolean.
        RtInitCallPlayRaw(request->entry, request->self, request->scalar, request->method); break;
    case RtTooltip:
        reinterpret_cast<void(__cdecl*)(void*, float, void*)>(request->entry)(request->self, Float(request->scalar), request->method); break;
    case RtMode:
        reinterpret_cast<void(__cdecl*)(int32_t, void*)>(request->entry)(static_cast<int32_t>(request->scalar), request->method); break;
    case RtInstantaneous:
        return reinterpret_cast<void*>(reinterpret_cast<TimelineHandle(__cdecl*)(void*, void*, void*, uint32_t, float, int32_t, void*)>(request->entry)
            (request->self, request->argument, nullptr, 0, Float(request->scalar), 0, request->method).value);
    case RtEndRange:
        reinterpret_cast<void(__cdecl*)(TimelineHandle, float, void*)>(request->entry)({ reinterpret_cast<uint64_t>(request->argument) }, Float(request->scalar), request->method); break;
    }
    return nullptr;
}
LONG CatchSeh(EXCEPTION_POINTERS* exception, RtInitCatchResult* result)
{
    const auto record = exception->ExceptionRecord;
    // Let the C++ runtime deliver its typed object to the outer native catch.
    if (record->ExceptionCode == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;
    result->exceptionKind = 1; result->exceptionCode = record->ExceptionCode;
    if (record->ExceptionCode == originalSehCode && record->NumberParameters == 2) {
        result->originalKind = static_cast<uint32_t>(record->ExceptionInformation[0]);
        result->payload = static_cast<uint64_t>(record->ExceptionInformation[1]); result->status = 2;
    }
    else result->status = 3;
    return EXCEPTION_EXECUTE_HANDLER;
}
void InvokeWithSehCatch(const RtInitRequest* request, RtInitCatchResult* result)
{
    __try { result->reference = InvokeRaw(request); result->status = 1; }
    __except (CatchSeh(GetExceptionInformation(), result)) { }
}
}
RT_INIT_API uint32_t __cdecl RtInitInvoke(const RtInitRequest* request, RtInitResult* result)
{
    if (!ValidRequest(request) || !result || result->size != sizeof(*result)) return 0;
    result->status = 0; result->exceptionCode = 0; result->reference = nullptr; result->threadId = GetCurrentThreadId();
    __try {
        result->reference = InvokeRaw(request);
        result->status = 1;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { result->exceptionCode = GetExceptionCode(); }
    return result->status;
}
RT_INIT_API uint32_t __cdecl RtInitInvokeCatch(const RtInitRequest* request, RtInitCatchResult* result)
{
    if (!ValidRequest(request) || !result || result->size != sizeof(*result)) return 0;
    memset(result, 0, sizeof(*result)); result->size = sizeof(*result); result->threadId = GetCurrentThreadId();
    try { InvokeWithSehCatch(request, result); }
    catch (const OriginalCppException& error) {
        result->status = 2; result->exceptionKind = 2; result->exceptionCode = 0xE06D7363;
        result->originalKind = error.kind; result->payload = error.payload;
    }
    catch (...) { result->status = 3; }
    return result->status;
}
namespace {
struct WorkerRequest { const RtInitRequest* request; RtInitResult* result; };
DWORD WINAPI Worker(void* context)
{
    auto work = static_cast<WorkerRequest*>(context);
    return RtInitInvoke(work->request, work->result);
}
}
RT_INIT_API uint32_t __cdecl RtInitInvokeWorker(const RtInitRequest* request, RtInitResult* result)
{
    WorkerRequest work{ request, result };
    auto thread = CreateThread(nullptr, 0, Worker, &work, 0, nullptr);
    if (!thread) return 0;
    // Finite offline calls only. Joining is required: request/result/observer
    // storage must not outlive this synchronous export.
    const auto joined = WaitForSingleObject(thread, INFINITE); CloseHandle(thread);
    return joined == WAIT_OBJECT_0 ? result->status : 0;
}

// Independent typed Judge target. It does not change the existing eight-kind
// initialization probe or use a managed original/caller to test native drain.
namespace {
struct JudgeActivityRequest { void* entry; void* self; void* method; int32_t judge; uint32_t deltaBits; };
static_assert(sizeof(JudgeActivityRequest) == 32);
HANDLE judgeResume{};
volatile LONG judgeActive{}, judgeMode{};
constexpr uint32_t judgeKind = 0x4a43;
extern "C" void __cdecl RtJudgeActivityEntry(void*, int32_t, float, void*);
void JudgeCallWithSeh(const JudgeActivityRequest* request, RtInitCatchResult* result)
{
    __try {
        reinterpret_cast<void(__cdecl*)(void*, int32_t, float, void*)>(request->entry)
            (request->self, request->judge, Float(request->deltaBits), request->method);
        result->status = 1;
    } __except (CatchSeh(GetExceptionInformation(), result)) { }
}
}
extern "C" void __cdecl RtJudgeActivityBody(void*, int32_t, float, void*)
{
    InterlockedIncrement(&judgeActive);
    __try {
        if (WaitForSingleObject(judgeResume, 10000) != WAIT_OBJECT_0)
            RaiseException(0xE04A544F, 0, 0, nullptr);
        const auto mode = InterlockedCompareExchange(&judgeMode, 0, 0);
        if (mode == 1) {
            const ULONG_PTR args[]{ judgeKind, static_cast<ULONG_PTR>(originalPayload) };
            RaiseException(originalSehCode, 0, 2, args);
        }
        else if (mode == 2) throw OriginalCppException{ judgeKind, originalPayload };
    } __finally { InterlockedDecrement(&judgeActive); }
}
RT_INIT_API void* __cdecl RtJudgeActivityTarget() { return reinterpret_cast<void*>(&RtJudgeActivityEntry); }
RT_INIT_API uint32_t __cdecl RtJudgeActivityControl(uint32_t mode, uint32_t block)
{
    if (mode > 2 || block > 1 || InterlockedCompareExchange(&judgeActive, 0, 0) != 0) return 0;
    if (!judgeResume) judgeResume = CreateEvent(nullptr, TRUE, TRUE, nullptr);
    if (!judgeResume) return 0;
    InterlockedExchange(&judgeMode, static_cast<LONG>(mode));
    return (block ? ResetEvent(judgeResume) : SetEvent(judgeResume)) ? 1u : 0u;
}
RT_INIT_API uint32_t __cdecl RtJudgeActivityActive() { return static_cast<uint32_t>(InterlockedCompareExchange(&judgeActive, 0, 0)); }
RT_INIT_API void __cdecl RtJudgeActivityResume() { SetEvent(judgeResume); }
RT_INIT_API uint32_t __cdecl RtJudgeActivityInvoke(const JudgeActivityRequest* request, RtInitCatchResult* result)
{
    if (!request || !request->entry || !result || result->size != sizeof(*result)) return 0;
    memset(result, 0, sizeof(*result)); result->size = sizeof(*result); result->threadId = GetCurrentThreadId();
    try { JudgeCallWithSeh(request, result); }
    catch (const OriginalCppException& error) {
        result->status = 2; result->exceptionKind = 2; result->exceptionCode = 0xE06D7363;
        result->originalKind = error.kind; result->payload = error.payload;
    }
    catch (...) { result->status = 3; }
    return result->status;
}
namespace {
struct JudgeWorkerRequest { const JudgeActivityRequest* request; RtInitCatchResult* result; };
DWORD WINAPI JudgeWorker(void* context)
{
    const auto work = static_cast<JudgeWorkerRequest*>(context);
    return RtJudgeActivityInvoke(work->request, work->result);
}
}
RT_INIT_API uint32_t __cdecl RtJudgeActivityInvokeWorker(const JudgeActivityRequest* request, RtInitCatchResult* result)
{
    JudgeWorkerRequest work{ request, result };
    const auto thread = CreateThread(nullptr, 0, JudgeWorker, &work, 0, nullptr);
    if (!thread) return 0;
    const auto joined = WaitForSingleObject(thread, INFINITE); CloseHandle(thread);
    return joined == WAIT_OBJECT_0 ? result->status : 0;
}

namespace {
RtInitObservation coroutineObservations[3]{};
void* coroutineResults[3]{}; void* coroutineNested{}; void* coroutineNestedMethod{};
void* namedFactory{}; void* namedFactoryMethod{}; void* namedMove{}; void* namedMoveMethod{};
extern "C" void* __cdecl RtCoroutineStartEntry(void*, void*, void*);
extern "C" uint8_t __cdecl RtCoroutineMoveEntry(void*, void*);
extern "C" void* __cdecl RtCoroutineFactoryEntry(void*, void*);
extern "C" void* __cdecl RtCoroutineCallStart(void*, void*, void*, void*);
extern "C" uint8_t __cdecl RtCoroutineCallMove(void*, void*, void*);
extern "C" void* __cdecl RtCoroutineCallFactory(void*, void*, void*);
extern "C" char RtCoroutineStartCaller, RtCoroutineMoveCaller, RtCoroutineFactoryCaller;
struct CoroutineExit {
    uint32_t kind;
    ~CoroutineExit() { AcquireSRWLockExclusive(&gate); ++coroutineObservations[kind].exited; ReleaseSRWLockExclusive(&gate); }
};
void CoroutineEntered(uint32_t kind, void* self, void* argument, void* method)
{
    AcquireSRWLockExclusive(&gate); auto& record = coroutineObservations[kind];
    record.size = sizeof(record); record.kind = kind; ++record.entered;
    record.self = self; record.argument = argument; record.method = method; record.threadId = GetCurrentThreadId(); ReleaseSRWLockExclusive(&gate);
}
void CoroutineInvokeSeh(uint32_t kind, void* entry, void* self, void* argument, void* method, RtInitCatchResult* result)
{
    __try {
        result->reference = kind == 0 ? RtCoroutineCallStart(entry, self, argument, method) : kind == 2 ? RtCoroutineCallFactory(entry, self, method)
            : reinterpret_cast<void*>(static_cast<uintptr_t>(RtCoroutineCallMove(entry, self, method)));
        result->status = 1;
    } __except (CatchSeh(GetExceptionInformation(), result)) { }
}
}
extern "C" void* __cdecl RtCoroutineStartBody(void* self, void* argument, void* method)
{
    CoroutineEntered(0, self, argument, method); CoroutineExit exit{ 0 };
    RtJudgeActivityBody(self, 0, 0, method); // Existing native block/SEH/C++ original fixture; no patched Judge entry.
    if (namedFactory) {
        auto* enumerator = RtCoroutineCallFactory(namedFactory, self, namedFactoryMethod);
        if (namedMove) RtCoroutineCallMove(namedMove, enumerator, namedMoveMethod);
    }
    if (coroutineNested) RtCoroutineCallMove(coroutineNested, argument, coroutineNestedMethod);
    return coroutineResults[0];
}
extern "C" uint8_t __cdecl RtCoroutineMoveBody(void* self, void* method)
{
    CoroutineEntered(1, self, nullptr, method); CoroutineExit exit{ 1 };
    RtJudgeActivityBody(self, 0, 0, method);
    return static_cast<uint8_t>(reinterpret_cast<uintptr_t>(coroutineResults[1]));
}
extern "C" void* __cdecl RtCoroutineFactoryBody(void* self, void* method)
{
    CoroutineEntered(2, self, nullptr, method); CoroutineExit exit{ 2 };
    RtJudgeActivityBody(self, 0, 0, method);
    return coroutineResults[2];
}
RT_INIT_API void* __cdecl RtCoroutineTarget(uint32_t kind)
{ return kind == 0 ? reinterpret_cast<void*>(&RtCoroutineStartEntry) : kind == 1 ? reinterpret_cast<void*>(&RtCoroutineMoveEntry)
    : kind == 2 ? reinterpret_cast<void*>(&RtCoroutineFactoryEntry) : nullptr; }
RT_INIT_API void* __cdecl RtCoroutineExpectedCaller(uint32_t kind)
{ return kind == 0 ? &RtCoroutineStartCaller : kind == 1 ? &RtCoroutineMoveCaller : kind == 2 ? &RtCoroutineFactoryCaller : nullptr; }
RT_INIT_API uint32_t __cdecl RtCoroutineControl(uint32_t kind, void* result, void* nested, void* nestedMethod)
{
    if (kind > 2) return 0;
    AcquireSRWLockExclusive(&gate);
    if (coroutineObservations[kind].entered != coroutineObservations[kind].exited) { ReleaseSRWLockExclusive(&gate); return 0; }
    coroutineResults[kind] = result;
    if (kind == 0) { coroutineNested = nested; coroutineNestedMethod = nestedMethod; }
    ReleaseSRWLockExclusive(&gate); return 1;
}
RT_INIT_API uint32_t __cdecl RtCoroutineRead(uint32_t kind, RtInitObservation* value)
{
    if (kind > 2 || !value || value->size != sizeof(*value)) return 0;
    AcquireSRWLockShared(&gate); *value = coroutineObservations[kind]; ReleaseSRWLockShared(&gate); return 1;
}
RT_INIT_API uint32_t __cdecl RtCoroutineInvoke(uint32_t kind, void* entry, void* self, void* argument, void* method, RtInitCatchResult* result)
{
    if (kind > 2 || !entry || !result || result->size != sizeof(*result)) return 0;
    memset(result, 0, sizeof(*result)); result->size = sizeof(*result); result->threadId = GetCurrentThreadId();
    try { CoroutineInvokeSeh(kind, entry, self, argument, method, result); }
    catch (const OriginalCppException& error) {
        result->status = 2; result->exceptionKind = 2; result->exceptionCode = 0xE06D7363;
        result->originalKind = error.kind; result->payload = error.payload;
    }
    catch (...) { result->status = 3; }
    return result->status;
}
RT_INIT_API uint32_t __cdecl RtCoroutineNamedControl(void* factory, void* factoryMethod, void* move, void* moveMethod)
{
    AcquireSRWLockExclusive(&gate);
    for (const auto& observation : coroutineObservations) if (observation.entered != observation.exited) { ReleaseSRWLockExclusive(&gate); return 0; }
    namedFactory = factory; namedFactoryMethod = factoryMethod; namedMove = move; namedMoveMethod = moveMethod;
    ReleaseSRWLockExclusive(&gate); return 1;
}
namespace {
struct CoroutineWorkerRequest { uint32_t kind; void* entry; void* self; void* argument; void* method; RtInitCatchResult* result; };
DWORD WINAPI CoroutineWorker(void* data)
{
    const auto work = static_cast<CoroutineWorkerRequest*>(data);
    return RtCoroutineInvoke(work->kind, work->entry, work->self, work->argument, work->method, work->result);
}
}
RT_INIT_API uint32_t __cdecl RtCoroutineInvokeWorker(uint32_t kind, void* entry, void* self, void* argument, void* method, RtInitCatchResult* result)
{
    CoroutineWorkerRequest work{ kind, entry, self, argument, method, result };
    const auto thread = CreateThread(nullptr, 0, CoroutineWorker, &work, 0, nullptr); if (!thread) return 0;
    const auto joined = WaitForSingleObject(thread, INFINITE); CloseHandle(thread); return joined == WAIT_OBJECT_0 ? result->status : 0;
}

// Independent void0 fixtures. The old initialization and coroutine kind tables
// retain their original layouts and meanings. These objects never enter a game.
namespace {
constexpr uint32_t voidKindCount = 5, voidKindBase = 0x5600;
constexpr uintptr_t voidCallbackToken = 0xFEDCBA9876543210ULL;
RtInitObservation voidObservations[voidKindCount]{};
void* voidNested[voidKindCount]{}, * voidNestedSelf[voidKindCount]{}, * voidNestedMethod[voidKindCount]{};
HANDLE voidResume[voidKindCount]{};
volatile LONG voidModes[voidKindCount]{};
volatile LONG eventMidPublication{}; // 0 absent, -1 claimed/failed, 1 published; never reset.
void* eventMidRelay{}; // Retained even if protection/flush/publication becomes unknown.
HANDLE eventMidBeforeResume{}; // Fixture-only, independent of voidResume[4].
volatile LONG eventMidBeforeReached{}, eventMidBeforeActive{};
thread_local unsigned voidDepth{};
extern "C" void __cdecl RtVoidEntry0(void*, const void*);
extern "C" void __cdecl RtVoidEntry1(void*, const void*);
extern "C" void __cdecl RtVoidEntry2(void*, const void*);
extern "C" void __cdecl RtVoidEntry3(void*, const void*);
extern "C" void __cdecl RtVoidEntry4(void*, const void*);
extern "C" void __cdecl RtVoidCall0(void*, void*, const void*);
extern "C" void __cdecl RtVoidCall1(void*, void*, const void*);
extern "C" void __cdecl RtVoidCall2(void*, void*, const void*);
extern "C" void __cdecl RtVoidCall3(void*, void*, const void*);
extern "C" void __cdecl RtVoidCall4(void*, void*, const void*);
extern "C" char RtVoidCaller0, RtVoidCaller1, RtVoidCaller2, RtVoidCaller3, RtVoidCaller4;
extern "C" char RtEventMidSite4, RtEventMidReturn4;
using VoidCaller = void(__cdecl*)(void*, void*, const void*);
VoidCaller voidCallers[voidKindCount]{ RtVoidCall0, RtVoidCall1, RtVoidCall2, RtVoidCall3, RtVoidCall4 };
struct VoidExit {
    uint32_t kind;
    ~VoidExit() {
        --voidDepth;
        AcquireSRWLockExclusive(&gate); ++voidObservations[kind].exited; ReleaseSRWLockExclusive(&gate);
        if (kind == 4) InterlockedDecrement(&eventMidBeforeActive);
    }
};
void VoidBody(uint32_t kind, void* self, const void* method)
{
    AcquireSRWLockExclusive(&gate); auto& record = voidObservations[kind];
    record.size = sizeof(record); record.kind = kind; ++record.entered;
    if (kind == 4) ++record.returned; // Continuation passed the actual mid CALL once.
    record.self = self; record.argument = nullptr; record.method = const_cast<void*>(method);
    record.threadId = GetCurrentThreadId(); ReleaseSRWLockExclusive(&gate);
    ++voidDepth; VoidExit exit{ kind };
    if (WaitForSingleObject(voidResume[kind], 10000) != WAIT_OBJECT_0)
        RaiseException(0xE056544F, 0, 0, nullptr);
    if (voidDepth == 1 && voidNested[kind])
        voidCallers[kind](voidNested[kind], voidNestedSelf[kind], voidNestedMethod[kind]);
    const auto mode = InterlockedCompareExchange(&voidModes[kind], 0, 0);
    const auto payload = originalPayload ^ static_cast<uint64_t>(kind);
    if (mode == 1) {
        const ULONG_PTR args[]{ voidKindBase + kind, static_cast<ULONG_PTR>(payload) };
        RaiseException(originalSehCode, 0, 2, args);
    }
    else if (mode == 2) throw OriginalCppException{ voidKindBase + kind, payload };
}
void VoidInvokeSeh(uint32_t kind, void* entry, void* self, const void* method, RtInitCatchResult* result)
{
    __try { voidCallers[kind](entry, self, method); result->status = 1; }
    __except (CatchSeh(GetExceptionInformation(), result)) { }
}
struct VoidWorkerRequest { uint32_t kind; void* entry; void* self; const void* method; RtInitCatchResult* result; };
struct VoidCallbackCppException { uint64_t payload; };
uint32_t voidCallbackMode{};
uintptr_t voidCallbackReadings[9]{};
void RecordVoidCallback(const RizVoidFrameV1* frame, void* user, bool entering, uintptr_t token, uint32_t flags)
{
    AcquireSRWLockExclusive(&gate);
    ++voidCallbackReadings[entering ? 0 : 1];
    if (!frame || frame->abiVersion != 1 || frame->byteSize != sizeof(*frame) || frame->reserved0 || frame->reserved1)
        ++voidCallbackReadings[8];
    else {
        voidCallbackReadings[4] = reinterpret_cast<uintptr_t>(frame->self);
        voidCallbackReadings[5] = reinterpret_cast<uintptr_t>(frame->method);
        voidCallbackReadings[6] = reinterpret_cast<uintptr_t>(frame->caller);
    }
    voidCallbackReadings[7] = reinterpret_cast<uintptr_t>(user);
    if (!entering) { voidCallbackReadings[2] = flags; voidCallbackReadings[3] = token; }
    ReleaseSRWLockExclusive(&gate);
}
uintptr_t __cdecl VoidCallbackEnter(const RizVoidFrameV1* frame, void* user)
{
    RecordVoidCallback(frame, user, true, 0, 0);
    if (voidCallbackMode & 1) RaiseException(0xE0564342, 0, 0, nullptr);
    if (voidCallbackMode & 2) throw VoidCallbackCppException{ originalPayload };
    return voidCallbackToken;
}
void __cdecl VoidCallbackExit(const RizVoidFrameV1* frame, uintptr_t token, uint32_t flags, void* user)
{
    RecordVoidCallback(frame, user, false, token, flags);
    if (voidCallbackMode & 4) RaiseException(0xE0564342, 0, 0, nullptr);
    if (voidCallbackMode & 8) throw VoidCallbackCppException{ originalPayload };
}
}
extern "C" void __cdecl RtVoidBody0(void* self, const void* method) { VoidBody(0, self, method); }
extern "C" void __cdecl RtVoidBody1(void* self, const void* method) { VoidBody(1, self, method); }
extern "C" void __cdecl RtVoidBody2(void* self, const void* method) { VoidBody(2, self, method); }
extern "C" void __cdecl RtVoidBody3(void* self, const void* method) { VoidBody(3, self, method); }
extern "C" void __cdecl RtVoidBody4(void* self, const void* method) { VoidBody(4, self, method); }
extern "C" void __cdecl RtEventMidBeforeBody4(void*, const void*)
{
    AcquireSRWLockExclusive(&gate);
    InterlockedIncrement(&eventMidBeforeActive);
    const auto resume = eventMidBeforeResume;
    if (resume) InterlockedCompareExchange(&eventMidBeforeReached, 1, 0);
    ReleaseSRWLockExclusive(&gate);
    bool continued = false;
    __try {
        if (resume && WaitForSingleObject(resume, 10000) != WAIT_OBJECT_0) {
            InterlockedExchange(&eventMidBeforeReached, 2);
            RaiseException(0xE0564254, 0, 0, nullptr);
        }
        continued = true;
    }
    __finally {
        // Normal completion is balanced by VoidBody's native RAII exit after
        // the mid CALL. A timeout before that body balances here instead.
        if (!continued) InterlockedDecrement(&eventMidBeforeActive);
    }
}
RT_INIT_API uint32_t __cdecl RtEventMidControlBefore(uint32_t kind, uint32_t block)
{
    if (kind != 4 || block > 1) return 0;
    AcquireSRWLockExclusive(&gate);
    if (InterlockedCompareExchange(&eventMidBeforeActive, 0, 0) != 0
        || voidObservations[4].entered != voidObservations[4].exited) {
        ReleaseSRWLockExclusive(&gate); return 0;
    }
    if (!eventMidBeforeResume) eventMidBeforeResume = CreateEvent(nullptr, TRUE, TRUE, nullptr);
    if (!eventMidBeforeResume) { ReleaseSRWLockExclusive(&gate); return 0; }
    const auto changed = block ? ResetEvent(eventMidBeforeResume) : SetEvent(eventMidBeforeResume);
    if (changed) InterlockedExchange(&eventMidBeforeReached, 0);
    ReleaseSRWLockExclusive(&gate); return changed ? 1u : 0u;
}
RT_INIT_API uint32_t __cdecl RtEventMidBeforeState(uint32_t kind)
{ return kind == 4 ? static_cast<uint32_t>(InterlockedCompareExchange(&eventMidBeforeReached, 0, 0)) : 0u; }
RT_INIT_API void __cdecl RtEventMidResumeBefore(uint32_t kind)
{
    if (kind != 4) return;
    AcquireSRWLockShared(&gate);
    if (eventMidBeforeResume) SetEvent(eventMidBeforeResume);
    ReleaseSRWLockShared(&gate);
}
RT_INIT_API void* __cdecl RtVoidTarget(uint32_t kind)
{
    switch (kind) {
    case 0: return reinterpret_cast<void*>(&RtVoidEntry0); case 1: return reinterpret_cast<void*>(&RtVoidEntry1);
    case 2: return reinterpret_cast<void*>(&RtVoidEntry2); case 3: return reinterpret_cast<void*>(&RtVoidEntry3);
    case 4: return reinterpret_cast<void*>(&RtVoidEntry4);
    default: return nullptr;
    }
}
RT_INIT_API void* __cdecl RtVoidExpectedCaller(uint32_t kind)
{
    switch (kind) {
    case 0: return &RtVoidCaller0; case 1: return &RtVoidCaller1;
    case 2: return &RtVoidCaller2; case 3: return &RtVoidCaller3;
    case 4: return &RtVoidCaller4;
    default: return nullptr;
    }
}
RT_INIT_API uint32_t __cdecl RtVoidControl(uint32_t kind, uint32_t mode, uint32_t block,
    void* nested, void* nestedSelf, void* nestedMethod)
{
    if (kind >= voidKindCount || mode > 2 || block > 1) return 0;
    AcquireSRWLockExclusive(&gate);
    if (voidObservations[kind].entered != voidObservations[kind].exited) { ReleaseSRWLockExclusive(&gate); return 0; }
    if (!voidResume[kind]) voidResume[kind] = CreateEvent(nullptr, TRUE, TRUE, nullptr);
    if (!voidResume[kind]) { ReleaseSRWLockExclusive(&gate); return 0; }
    InterlockedExchange(&voidModes[kind], static_cast<LONG>(mode));
    voidNested[kind] = nested; voidNestedSelf[kind] = nestedSelf; voidNestedMethod[kind] = nestedMethod;
    const auto result = block ? ResetEvent(voidResume[kind]) : SetEvent(voidResume[kind]);
    ReleaseSRWLockExclusive(&gate); return result ? 1u : 0u;
}
RT_INIT_API void __cdecl RtVoidResume(uint32_t kind) { if (kind < voidKindCount) SetEvent(voidResume[kind]); }
RT_INIT_API uint32_t __cdecl RtVoidRead(uint32_t kind, RtInitObservation* value)
{
    if (kind >= voidKindCount || !value || value->size != sizeof(*value)) return 0;
    AcquireSRWLockShared(&gate); *value = voidObservations[kind]; ReleaseSRWLockShared(&gate);
    value->size = sizeof(*value); value->kind = kind; return 1;
}
RT_INIT_API uint32_t __cdecl RtVoidInvoke(uint32_t kind, void* entry, void* self, const void* method, RtInitCatchResult* result)
{
    if (kind >= voidKindCount || !entry || !result || result->size != sizeof(*result)) return 0;
    if (kind == 4 && InterlockedCompareExchange(&eventMidPublication, 1, 1) != 1) return 0;
    memset(result, 0, sizeof(*result)); result->size = sizeof(*result); result->threadId = GetCurrentThreadId();
    try { VoidInvokeSeh(kind, entry, self, method, result); }
    catch (const OriginalCppException& error) {
        result->status = 2; result->exceptionKind = 2; result->exceptionCode = 0xE06D7363;
        result->originalKind = error.kind; result->payload = error.payload;
    }
    catch (...) { result->status = 3; }
    return result->status;
}
namespace {
DWORD WINAPI VoidWorker(void* data)
{
    const auto work = static_cast<VoidWorkerRequest*>(data);
    return RtVoidInvoke(work->kind, work->entry, work->self, work->method, work->result);
}
}
RT_INIT_API uint32_t __cdecl RtVoidInvokeWorker(uint32_t kind, void* entry, void* self, const void* method, RtInitCatchResult* result)
{
    VoidWorkerRequest work{ kind, entry, self, method, result };
    const auto thread = CreateThread(nullptr, 0, VoidWorker, &work, 0, nullptr); if (!thread) return 0;
    const auto joined = WaitForSingleObject(thread, INFINITE); CloseHandle(thread);
    return joined == WAIT_OBJECT_0 ? result->status : 0;
}
RT_INIT_API void* __cdecl RtVoidCallbackAddress(uint32_t kind)
{ return kind == 0 ? reinterpret_cast<void*>(&VoidCallbackEnter) : kind == 1 ? reinterpret_cast<void*>(&VoidCallbackExit) : nullptr; }
RT_INIT_API uint32_t __cdecl RtVoidCallbackControl(uint32_t mode)
{
    if (mode > 15) return 0;
    AcquireSRWLockExclusive(&gate); voidCallbackMode = mode; memset(voidCallbackReadings, 0, sizeof(voidCallbackReadings)); ReleaseSRWLockExclusive(&gate); return 1;
}
RT_INIT_API uintptr_t __cdecl RtVoidCallbackRead(uint32_t field)
{
    if (field >= 9) return 0;
    AcquireSRWLockShared(&gate); const auto value = voidCallbackReadings[field]; ReleaseSRWLockShared(&gate); return value;
}

namespace {
bool EventMidExecutable(const void* address)
{
    MEMORY_BASIC_INFORMATION information{};
    if (!address || !VirtualQuery(address, &information, sizeof(information)) || information.State != MEM_COMMIT
        || (information.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
    const auto protection = information.Protect & 0xff;
    return protection == PAGE_EXECUTE || protection == PAGE_EXECUTE_READ
        || protection == PAGE_EXECUTE_READWRITE || protection == PAGE_EXECUTE_WRITECOPY;
}
void* EventMidAllocateRelay(const void* site)
{
    SYSTEM_INFO information{}; GetSystemInfo(&information);
    const auto granularity = static_cast<uintptr_t>(information.dwAllocationGranularity);
    const auto origin = reinterpret_cast<uintptr_t>(site) & ~(granularity - 1);
    for (uintptr_t index = 1; index != 4096; ++index) {
        const auto distance = index * granularity;
        const uintptr_t candidates[]{ origin >= distance ? origin - distance : 0, origin + distance };
        for (const auto candidate : candidates) {
            if (!candidate || candidate < reinterpret_cast<uintptr_t>(information.lpMinimumApplicationAddress)
                || candidate > reinterpret_cast<uintptr_t>(information.lpMaximumApplicationAddress) - 4096) continue;
            auto* allocated = VirtualAlloc(reinterpret_cast<void*>(candidate), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            if (allocated) return allocated;
        }
    }
    return nullptr;
}
}
RT_INIT_API void* __cdecl RtEventMidExpectedReturn(uint32_t kind)
{ return kind == 4 ? &RtEventMidReturn4 : nullptr; }
RT_INIT_API uint32_t __cdecl RtEventMidConfigure(uint32_t kind, void* helperEntry, void* actualList)
{
    if (kind != 4 || !helperEntry || !actualList) return 0;
    AcquireSRWLockExclusive(&gate);
    const bool available = voidObservations[4].entered == 0 && voidObservations[4].exited == 0
        && InterlockedCompareExchange(&eventMidPublication, -1, 0) == 0;
    ReleaseSRWLockExclusive(&gate);
    if (!available) return 0;
    // The rest of this function runs before any physical kind-4 call. It is a
    // bounded fixture-only publication, not a concurrent production patch API.
    auto* target = reinterpret_cast<unsigned char*>(&RtVoidEntry4);
    auto* site = reinterpret_cast<unsigned char*>(&RtEventMidSite4);
    auto* returned = reinterpret_cast<unsigned char*>(&RtEventMidReturn4);
    if (site != target + 0x13b || returned != target + 0x140 || !EventMidExecutable(helperEntry)) return 0;
    for (unsigned index = 0; index != 32; ++index) if (target[index] != 0x90) return 0;
    const unsigned char original[]{ 0x0f,0x1f,0x44,0x00,0x00 };
    if (memcmp(site, original, sizeof(original)) != 0) return 0;
    unsigned char surrounding[21]{}; memcpy(surrounding, site - 8, sizeof(surrounding));
    eventMidRelay = EventMidAllocateRelay(site);
    if (!eventMidRelay) return 0;
    auto* relay = static_cast<unsigned char*>(eventMidRelay);
    const unsigned char tailJump[]{ 0xff,0x25,0,0,0,0 };
    memcpy(relay, tailJump, sizeof(tailJump)); memcpy(relay + 6, &helperEntry, sizeof(helperEntry));
    DWORD previous = 0;
    if (!VirtualProtect(relay, 4096, PAGE_EXECUTE_READ, &previous)
        || !FlushInstructionCache(GetCurrentProcess(), relay, 14)) return 0;
    const auto delta = reinterpret_cast<intptr_t>(relay) - reinterpret_cast<intptr_t>(returned);
    if (delta < INT32_MIN || delta > INT32_MAX) return 0;
    const auto relative = static_cast<int32_t>(delta);
    InterlockedExchangePointer(&RtEventMidActualList4, actualList);
    if (!VirtualProtect(site, 5, PAGE_EXECUTE_READWRITE, &previous)) return 0;
    site[0] = 0xe8; memcpy(site + 1, &relative, sizeof(relative));
    DWORD ignored = 0;
    if (!VirtualProtect(site, 5, previous, &ignored) || !FlushInstructionCache(GetCurrentProcess(), site, 5)) return 0;
    if (site[0] != 0xe8 || memcmp(site + 1, &relative, sizeof(relative)) != 0
        || memcmp(site - 8, surrounding, 8) != 0 || memcmp(site + 5, surrounding + 13, 8) != 0
        || memcmp(relay, tailJump, sizeof(tailJump)) != 0 || memcmp(relay + 6, &helperEntry, sizeof(helperEntry)) != 0) return 0;
    for (unsigned index = 0; index != 32; ++index) if (target[index] != 0x90) return 0;
    InterlockedExchange(&eventMidPublication, 1); return 1;
}
