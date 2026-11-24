#define RIZ_EARLY_IMPORTER_BUILD
#include "EarlyFixedShared.h"
#include "ImporterApi.h"
#include <intrin.h>

extern "C" IMAGE_DOS_HEADER __ImageBase;
#pragma comment(linker, "/EXPORT:RizFixedOwnedStart")
#pragma comment(linker, "/EXPORT:RizFixedOwnedExecute")
#pragma comment(linker, "/EXPORT:RizFixedOwnedRelease")
#pragma comment(linker, "/EXPORT:RizFixedOwnedDetach")
#pragma comment(linker, "/EXPORT:RizFixedOwnedMarker,DATA")

namespace {
constexpr unsigned WorkCapacity = 96;
struct Work;
struct Call {
    OwnedEarlyCallV2 view;
    Work* work;
    std::uint32_t returned, caught, rawReturn, exceptionCode;
    std::uint32_t exceptionFlags, exceptionParameters, reserved0, reserved1;
    std::uint64_t exceptionInformation[4];
    const void* exceptionAddress;
};
struct Work {
    OwnedEarlyPhaseReportV2 report;
    unsigned char owner[16], enumerator[16], name[16], node[0x88];
    void* outputNode;
    std::uint8_t executionOutput;
    Call calls[6];
};
// Every object touched before importer CRT initialization is zero/constant
// initialized POD. The TLS slot is a primitive pointer, without a constructor
// or destructor. No allocation, logging library, lazy singleton or lock wait
// is used by any importer initializer.
Work works[WorkCapacity]{};
volatile LONG nextWork{}, initializerMask{}, initializedPhases{};
volatile LONG64 checks{}, failures{}, originals[4]{}, phases{};
volatile LONG64 workerRequests{}, workerStarts{}, workerFinishes{};
HANDLE initializerWorker{};
__declspec(thread) Call* currentCall;
struct OriginalCpp { std::uint64_t payload, callId; std::uint32_t kind; };

std::uint64_t Module() { return reinterpret_cast<std::uint64_t>(&__ImageBase); }
std::uint64_t AtomicRead(volatile LONG64* value)
{ return static_cast<std::uint64_t>(InterlockedCompareExchange64(value, 0, 0)); }
void Record(std::uint32_t type, const Call* call, std::uint32_t result,
    std::uint64_t value0, std::uint64_t value1, const void* caller)
{
    const auto& view = call->view;
    RizEarlyRecorder_Event(type, view.phase, view.kind, view.nested, result,
        view.faultMode, Module(), reinterpret_cast<std::uint64_t>(OwnedEarlyGetTarget(view.kind)),
        0, 0, view.callId, value0, value1, reinterpret_cast<std::uint64_t>(caller));
}
void Check(Work* work, bool okay)
{
    InterlockedIncrement64(&checks); ++work->report.checks;
    if (okay) return;
    InterlockedIncrement64(&failures); ++work->report.failures;
    RizEarlyRecorder_Fail(RizEarlyHostAssertion, 1);
}
void WriteNode(Work* work)
{
    *reinterpret_cast<std::uint64_t*>(work->node + 0x10) =
        0xf123456700000000ull | work->report.phaseId;
    *reinterpret_cast<std::uint32_t*>(work->node + 0x18) = 2;
    *reinterpret_cast<void**>(work->node + 0x20) = work->enumerator;
    *reinterpret_cast<void**>(work->node + 0x58) = work->owner;
    // One loaded/attached phase reaches exactly zero, so the live integration
    // observes both values of CMP's ZF without inferring node destruction.
    *reinterpret_cast<std::int32_t*>(work->node + 0x60) =
        work->report.phase == RizEarlyHostAfterAttachPhase ? 2 : 3;
}
Call* PrepareCall(Work* work, unsigned kind, bool nested)
{
    auto* call = &work->calls[nested ? (kind == 1 ? 4 : 5) : kind];
    auto& view = call->view;
    Check(work, view.callId == 0);
    view.version = RizEarlyWorkloadVersion; view.byteSize = sizeof(view); view.phase = work->report.phase;
    view.kind = kind; view.nested = nested ? 1u : 0u;
    view.faultMode = !nested && kind == work->report.faultKind ? work->report.faultMode : 0u;
    view.callId = work->report.phaseId * 8 + static_cast<std::uint64_t>(call - work->calls) + 1;
    view.args[0] = kind == 0 ? static_cast<void*>(work->owner) : work->node;
    view.args[1] = kind == 0 ? static_cast<void*>(work->enumerator)
        : kind == 1 ? &work->executionOutput : nullptr;
    view.args[2] = kind == 0 ? work->name : nullptr;
    view.args[3] = kind == 0 ? &work->outputNode : nullptr;
    view.node = work->node; view.owner = work->owner;
    view.enumHandle = 0xf123456700000000ull | work->report.phaseId;
    view.enumCache = work->enumerator; view.enumType = 2;
    // Captured before the real prefix. Release's exact prefix decrements this
    // value before entering its C++ body; later direct calls see that change.
    view.referenceCount = *reinterpret_cast<const std::int32_t*>(work->node + 0x60);
    view.rawReturn = kind == 0 ? RizFixedOwnedStartReturn : kind == 1 ? RizFixedOwnedExecuteReturn
        : kind == 2 ? RizFixedOwnedReleaseReturn : 0;
    call->work = work;
    return call;
}
LONG Filter(EXCEPTION_POINTERS* pointers, Call* call)
{
    const auto* exception = pointers->ExceptionRecord;
    call->exceptionCode = exception->ExceptionCode;
    call->exceptionFlags = exception->ExceptionFlags;
    call->exceptionParameters = exception->NumberParameters;
    call->exceptionAddress = exception->ExceptionAddress;
    for (unsigned i = 0; i != 4; ++i)
        call->exceptionInformation[i] = i < exception->NumberParameters ? exception->ExceptionInformation[i] : 0;
    return exception->ExceptionCode == 0xe06d7363 ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER;
}
void InvokeSeh(Call* call)
{
    auto& view = call->view;
    __try {
        if (view.kind == 0)
            call->rawReturn = RizFixedOwnedStart(view.args[0], view.args[1], view.args[2], static_cast<void**>(view.args[3]));
        else if (view.kind == 1)
            call->rawReturn = RizFixedOwnedExecute(view.args[0], static_cast<std::uint8_t*>(view.args[1]));
        else if (view.kind == 2) call->rawReturn = RizFixedOwnedRelease(view.args[0]);
        else RizFixedOwnedDetach(view.args[0]);
        call->returned = 1;
    } __except (Filter(GetExceptionInformation(), call)) { call->caught = 1; }
}
void InvokeRestoring(Call* call)
{
    auto* previous = currentCall; currentCall = call;
    __try { InvokeSeh(call); }
    __finally { currentCall = previous; }
}
void Invoke(Work* work, unsigned kind, bool nested)
{
    auto* call = PrepareCall(work, kind, nested);
    try { InvokeRestoring(call); }
    catch (const OriginalCpp& original) {
        Check(work, original.payload == OwnedEarlyExceptionPayload
            && original.callId == call->view.callId && original.kind == kind);
        Check(work, call->exceptionCode == 0xe06d7363 && call->exceptionParameters == 4
            && call->exceptionInformation[0] == 0x19930520
            && call->exceptionInformation[1] && call->exceptionInformation[2] && call->exceptionInformation[3]);
        Record(RizEarlyOriginalTypedCaught, call, original.kind,
            original.payload, original.callId, nullptr);
        call->caught = 1;
    } catch (...) { Check(work, false); }
    const auto fault = call->view.faultMode;
    Check(work, call->returned == (fault ? 0u : 1u) && call->caught == (fault ? 1u : 0u));
    if (!fault) Check(work, kind == 3 || call->rawReturn == call->view.rawReturn);
    if (fault == 1) {
        Check(work, call->exceptionCode == OwnedEarlySehCode && call->exceptionParameters == 3
            && call->exceptionInformation[0] == OwnedEarlyExceptionPayload
            && call->exceptionInformation[1] == call->view.callId && call->exceptionInformation[2] == kind);
    }
    if (fault) {
        Record(RizEarlyOriginalCaught, call, call->exceptionCode,
            call->exceptionInformation[0], call->exceptionInformation[1], nullptr);
        const auto& view = call->view;
        // Preserve raw flags and all four native exception information words.
        // Across child processes only stable payload/kind fields are equal;
        // MSVC object/module pointers and call generations remain local facts.
        RizEarlyRecorder_Event(RizEarlyOriginalExceptionDetails, view.phase, view.kind,
            view.nested, call->exceptionParameters, call->exceptionFlags, Module(),
            reinterpret_cast<std::uint64_t>(OwnedEarlyGetTarget(view.kind)), 0, 0,
            view.callId, call->exceptionInformation[2], call->exceptionInformation[3],
            reinterpret_cast<std::uint64_t>(call->exceptionAddress));
    }
    if (!fault && kind == 0) Check(work, work->outputNode == work->node);
    if (!fault && kind == 1) Check(work, work->executionOutput == RizFixedOwnedExecuteOutput);
    ++work->report.returned[kind];
    if (fault) { --work->report.returned[kind]; ++work->report.caught[kind]; }
}
Call* Body(unsigned kind, void* arg0, void* arg1, void* arg2, void* arg3, const void* caller)
{
    auto* call = currentCall;
    if (!call) RizEarlyRecorder_Fail(RizEarlyHostAssertion, 1);
    auto* work = call->work; const auto& view = call->view;
    Check(work, view.kind == kind && view.args[0] == arg0 && view.args[1] == arg1
        && view.args[2] == arg2 && view.args[3] == arg3 && caller != nullptr);
    InterlockedIncrement64(&originals[kind]);
    auto* shared = RizEarlyRecorder_GetShared();
    if (!shared) RizEarlyRecorder_Fail(RizEarlyBadMapping, 1);
    InterlockedIncrement(&shared->header.targetCalls[kind]);
    ++work->report.originals[kind]; if (view.nested) ++work->report.nested[kind];
    Record(RizEarlyTargetEnter, call, 0, reinterpret_cast<std::uint64_t>(arg0),
        reinterpret_cast<std::uint64_t>(arg1), caller);
    Record(RizEarlyTargetArguments, call, 0, reinterpret_cast<std::uint64_t>(arg2),
        reinterpret_cast<std::uint64_t>(arg3), caller);
    return call;
}
void OriginalFault(Call* call, const void* caller)
{
    const auto& view = call->view;
    if (view.faultMode) {
        const auto code = view.faultMode == 1 ? OwnedEarlySehCode : 0xe06d7363u;
        Record(RizEarlyOriginalException, call, code, OwnedEarlyExceptionPayload, view.callId, caller);
        if (view.faultMode == 1) {
            const ULONG_PTR payload[]{ static_cast<ULONG_PTR>(OwnedEarlyExceptionPayload),
                static_cast<ULONG_PTR>(view.callId), view.kind };
            RaiseException(OwnedEarlySehCode, 0, 3, payload);
        } else throw OriginalCpp{ OwnedEarlyExceptionPayload, view.callId, view.kind };
    }
}
void BodyExit(Call* call, const void* caller)
{
    const auto kind = call->view.kind; auto* work = call->work;
    Record(RizEarlyTargetExit, call, call->view.rawReturn, call->view.rawReturn,
        kind == 0 ? reinterpret_cast<std::uint64_t>(work->outputNode)
        : kind == 1 ? work->executionOutput : 0, caller);
}
Work* RunPhase(std::uint32_t phase, std::uint32_t faultKind, std::uint32_t faultMode)
{
    const auto index = InterlockedIncrement(&nextWork) - 1;
    if (index < 0 || static_cast<unsigned>(index) >= WorkCapacity)
        RizEarlyRecorder_Fail(RizEarlyHostAssertion, 1);
    auto* work = &works[index]; auto& report = work->report;
    report.version = RizEarlyWorkloadVersion; report.byteSize = sizeof(report); report.phase = phase;
    report.faultKind = faultKind; report.faultMode = faultMode; report.phaseId = static_cast<std::uint64_t>(index) + 1;
    InterlockedIncrement64(&phases); WriteNode(work);
    RizEarlyRecorder_Event(RizEarlyPhaseBegin, phase, 4, faultKind, 0, faultMode,
        Module(), 0, 0, 0, report.phaseId, 0, 0, 0);
    for (unsigned kind = 0; kind != 4; ++kind) Invoke(work, kind, false);
    Check(work, report.originals[0] == 1 && report.originals[3] == 1);
    const auto nested = faultKind == 0 && faultMode ? 0u : 1u;
    Check(work, report.originals[1] == 1 + nested && report.originals[2] == 1 + nested);
    Check(work, report.nested[0] == 0 && report.nested[1] == nested
        && report.nested[2] == nested && report.nested[3] == 0);
    const auto initialReferences = phase == RizEarlyHostAfterAttachPhase ? 2 : 3;
    Check(work, *reinterpret_cast<const std::int32_t*>(work->node + 0x60)
        == initialReferences - 1 - static_cast<std::int32_t>(nested));
    RizEarlyRecorder_Event(RizEarlyPhaseEnd, phase, 4, faultKind, static_cast<std::uint32_t>(report.failures), faultMode,
        Module(), 0, 0, 0, report.phaseId, report.checks, report.failures, 0);
    return work;
}
void Initialize(std::uint32_t phase)
{
    auto* shared = RizEarlyRecorder_GetShared();
    if (!shared) RizEarlyRecorder_Fail(RizEarlyBadMapping, 1);
    InterlockedIncrement(&shared->header.importerInitializers);
    RizEarlyRecorder_Event(RizEarlyInitializerEnter, phase, 4, 0, 0, 0, Module(), 0, 0, 0, 0, 0, 0, 0);
    // Forces an ordinary Importer -> Proxy import, including for the TLS phase.
    // It never starts installation or executes importer code from the proxy.
    RizEarlyProxy_Anchor();
    RunPhase(phase, 4, 0);
    InterlockedOr(&initializerMask, static_cast<LONG>(1u << phase));
    InterlockedIncrement(&initializedPhases);
    RizEarlyRecorder_Event(RizEarlyInitializerExit, phase, 4, 0, 0, 0, Module(), 0, 0, 0, 0, 0, 0, 0);
}
DWORD WINAPI InitializerWorker(void*)
{
    InterlockedIncrement64(&workerStarts);
    RizEarlyRecorder_Event(RizEarlyWorkerStarted, RizEarlyWorkerPhase, 4, 0, 0, 1, Module(), 0, 0, 0, 0, 0, 0, 0);
    RunPhase(RizEarlyWorkerPhase, 4, 0);
    InterlockedIncrement64(&workerFinishes);
    RizEarlyRecorder_Event(RizEarlyWorkerFinished, RizEarlyWorkerPhase, 4, 0, 0, 1, Module(), 0, 0, 0, 0, 0, 0, 0);
    return 0;
}
int __cdecl CInitialize() { Initialize(RizEarlyCPhase); return 0; }
struct CppInitialize { CppInitialize() { Initialize(RizEarlyCppPhase); } };
CppInitialize cppInitializer;
}

extern "C" void NTAPI OwnedEarlyTlsCallback(PVOID, DWORD reason, PVOID)
{ if (reason == DLL_PROCESS_ATTACH) Initialize(RizEarlyTlsPhase); }
#pragma section(".CRT$XLB", long, read)
extern "C" __declspec(allocate(".CRT$XLB")) PIMAGE_TLS_CALLBACK const OwnedEarlyTlsSlot = OwnedEarlyTlsCallback;
#pragma comment(linker, "/INCLUDE:_tls_used")
#pragma comment(linker, "/INCLUDE:OwnedEarlyTlsSlot")
#pragma section(".CRT$XIU", long, read)
extern "C" __declspec(allocate(".CRT$XIU")) int(__cdecl* const OwnedEarlyCInitializerSlot)() = CInitialize;
#pragma comment(linker, "/INCLUDE:OwnedEarlyCInitializerSlot")

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    Initialize(RizEarlyDllPhase);
    auto* shared = RizEarlyRecorder_GetShared();
    if (shared->header.config.mode == RizEarlyWorker) {
        InterlockedIncrement64(&workerRequests);
        RizEarlyRecorder_Event(RizEarlyWorkerRequested, RizEarlyDllPhase, 4, 0, 0, 1, Module(), 0, 0, 0, 0, 0, 0, 0);
        // Deliberate fixture branch: request only, never join under loader lock.
        initializerWorker = CreateThread(nullptr, 0, InitializerWorker, nullptr, 0, nullptr);
        if (!initializerWorker) RizEarlyRecorder_Fail(RizEarlyHostAssertion, 1);
    }
    return TRUE;
}

extern "C" __declspec(noinline) std::uint8_t __cdecl RizFixedBodyStart(void* owner,
    void* enumerator, void* nameArgument, void** outputNode)
{
    auto* call = Body(0, owner, enumerator, nameArgument, outputNode, _ReturnAddress());
    OriginalFault(call, _ReturnAddress());
    Invoke(call->work, 1, true); Invoke(call->work, 2, true);
    *outputNode = call->work->node; BodyExit(call, _ReturnAddress());
    return static_cast<std::uint8_t>(call->view.rawReturn);
}
extern "C" __declspec(noinline) std::uint32_t __cdecl RizFixedBodyExecute(void* node, std::uint8_t* output)
{
    auto* call = Body(1, node, output, nullptr, nullptr, _ReturnAddress());
    OriginalFault(call, _ReturnAddress());
    *output = RizFixedOwnedExecuteOutput; BodyExit(call, _ReturnAddress()); return call->view.rawReturn;
}
extern "C" __declspec(noinline) std::uint8_t __cdecl RizFixedBodyRelease(void* node,
    std::uint32_t afterCount, std::uint8_t zeroFlag)
{
    auto* call = Body(2, node, nullptr, nullptr, nullptr, _ReturnAddress());
    Check(call->work, afterCount == static_cast<std::uint32_t>(call->view.referenceCount - 1)
        && afterCount == *reinterpret_cast<const std::uint32_t*>(call->work->node + 0x60)
        && zeroFlag == (afterCount == 0 ? 1u : 0u));
    const auto& view = call->view;
    RizEarlyRecorder_Event(RizEarlyTargetDiagnostics, view.phase, view.kind, view.nested,
        afterCount, zeroFlag, Module(), reinterpret_cast<std::uint64_t>(OwnedEarlyGetTarget(2)),
        0, 0, view.callId, static_cast<std::uint32_t>(view.referenceCount), afterCount,
        reinterpret_cast<std::uint64_t>(_ReturnAddress()));
    OriginalFault(call, _ReturnAddress());
    BodyExit(call, _ReturnAddress()); return static_cast<std::uint8_t>(call->view.rawReturn);
}
extern "C" __declspec(noinline) void __cdecl RizFixedBodyDetach(void* node, std::uint32_t clearedEbp)
{
    auto* call = Body(3, node, nullptr, nullptr, nullptr, _ReturnAddress());
    Check(call->work, clearedEbp == 0);
    const auto& view = call->view;
    RizEarlyRecorder_Event(RizEarlyTargetDiagnostics, view.phase, view.kind, view.nested,
        clearedEbp, 0, Module(), reinterpret_cast<std::uint64_t>(OwnedEarlyGetTarget(3)),
        0, 0, view.callId, static_cast<std::uint32_t>(view.referenceCount),
        *reinterpret_cast<const std::uint32_t*>(call->work->node + 0x60),
        reinterpret_cast<std::uint64_t>(_ReturnAddress()));
    OriginalFault(call, _ReturnAddress()); BodyExit(call, _ReturnAddress());
}

RIZ_EARLY_IMPORTER_API void* __cdecl OwnedEarlyGetTarget(std::uint32_t kind)
{
    return kind == 0 ? reinterpret_cast<void*>(&RizFixedOwnedStart) : kind == 1 ? reinterpret_cast<void*>(&RizFixedOwnedExecute)
        : kind == 2 ? reinterpret_cast<void*>(&RizFixedOwnedRelease) : kind == 3 ? reinterpret_cast<void*>(&RizFixedOwnedDetach) : nullptr;
}
RIZ_EARLY_IMPORTER_API const OwnedEarlyCallV2* __cdecl OwnedEarlyGetCurrent()
{ return currentCall ? &currentCall->view : nullptr; }
RIZ_EARLY_IMPORTER_API std::uint32_t __cdecl OwnedEarlyRunPhase(std::uint32_t phase,
    std::uint32_t faultKind, std::uint32_t faultMode, OwnedEarlyPhaseReportV2* report)
{
    if (!report || report->version != RizEarlyWorkloadVersion || report->byteSize != sizeof(*report)
        || report->reserved || phase < RizEarlyHostPhase || faultKind > 4 || faultMode > 2
        || ((faultKind == 4) != (faultMode == 0))
        || InterlockedCompareExchange(&initializerMask, 0, 0) != 0x1e)
        return 1;
    if (faultMode && phase != RizEarlySehPhase && phase != RizEarlyCppExceptionPhase
        && phase != RizEarlyBaselineSehPhase && phase != RizEarlyBaselineCppPhase) return 1;
    const auto* work = RunPhase(phase, faultKind, faultMode); *report = work->report; return 0;
}
RIZ_EARLY_IMPORTER_API std::uint32_t __cdecl OwnedEarlyGetSnapshot(OwnedEarlySnapshotV2* snapshot)
{
    if (!snapshot || snapshot->version != RizEarlyWorkloadVersion || snapshot->byteSize != sizeof(*snapshot)) return 1;
    snapshot->initializerMask = static_cast<std::uint32_t>(InterlockedCompareExchange(&initializerMask, 0, 0));
    snapshot->initializedPhases = static_cast<std::uint32_t>(InterlockedCompareExchange(&initializedPhases, 0, 0));
    snapshot->checks = AtomicRead(&checks); snapshot->failures = AtomicRead(&failures);
    for (unsigned kind = 0; kind != 4; ++kind) snapshot->originals[kind] = AtomicRead(&originals[kind]);
    snapshot->phases = AtomicRead(&phases); snapshot->workerRequests = AtomicRead(&workerRequests);
    snapshot->workerStarts = AtomicRead(&workerStarts); snapshot->workerFinishes = AtomicRead(&workerFinishes);
    for (auto& value : snapshot->reserved) value = 0;
    return 0;
}
RIZ_EARLY_IMPORTER_API void* __cdecl OwnedEarlyGetInitializerWorker() { return initializerWorker; }
