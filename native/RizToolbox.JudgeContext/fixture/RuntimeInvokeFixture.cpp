#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../RuntimeInvokeContext.h"

extern "C" void* RtRuntimeInvokeTarget(const void*, void*, void**, void**);
extern "C" void* RtRuntimeInvokeCall(void*, const void*, void*, void**, void**);
extern "C" unsigned char RtRuntimeInvokeCallerReturn;

namespace {
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using ConfigureFn = decltype(&RizRuntimeInvokeContext_Configure);
using AttachFn = decltype(&RizRuntimeInvokeContext_Attach);
using InfoFn = decltype(&RizRuntimeInvokeContext_GetInfo);
ConfigureFn configure;
AttachFn attach;
InfoFn getInfo;
constexpr std::uint64_t Payload = 0xBADC0FFEFACE1234ULL;
constexpr DWORD OriginalSeh = 0xE052494F, ObserverSeh = 0xE0524943;
volatile LONG64 checks{}, failures{}, originalCalls{}, afterCalls{}, unobservedCalls{}, observedCalls{};
volatile LONG64 recursiveCalls{}, originalExceptions{}, observerExceptions{};
std::uintptr_t marker = 0xDAD0BEEFCAFE1234ULL;
struct CppOriginal { std::uint64_t payload; const void* method; void* self; void** arguments; void** exception; };
struct CppObserver { std::uint64_t payload; };
struct Work {
    bool observed{}, hold{}, recurse{}, nullArguments{}, nullException{}, returned{}, caught{};
    std::uint32_t mode{}, faultKind{};
    const void* method{};
    void* self{};
    void* arguments[3]{};
    void* exception{};
    void* expectedResult{};
    void* actualResult{};
    HANDLE reached{}, proceed{};
    DWORD thread{}, exceptionCode{};
    std::uint64_t exceptionPayload{};
    volatile LONG originalEntered{}, originalExited{}, after{};
};
thread_local Work* current = nullptr;

void Check(bool condition, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!condition) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED runtime invoke line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto pointer = GetProcAddress(module, name); CHECK(pointer != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(pointer)); std::memcpy(&result, &pointer, sizeof(result)); return result;
}
HANDLE Event()
{ const auto handle = CreateEventW(nullptr, TRUE, FALSE, nullptr); CHECK(handle); return handle; }
void Init(Work* work, bool observed)
{
    *work = {}; work->observed = observed;
    work->method = reinterpret_cast<const void*>(0xABCD1234FEDC0080ULL);
    work->self = reinterpret_cast<void*>(0xDEADBEEFCAFE0100ULL);
    work->arguments[0] = reinterpret_cast<void*>(0xAAAA1122BBBB3344ULL);
    work->arguments[1] = reinterpret_cast<void*>(0xCCCC5566DDDD7788ULL);
    work->arguments[2] = reinterpret_cast<void*>(0xEEEE99AAFFFFBBCCULL);
    work->expectedResult = reinterpret_cast<void*>(0xF123456789ABCDE0ULL);
    work->reached = Event(); work->proceed = Event();
}
void CloseEvents(Work* work) { CloseHandle(work->reached); CloseHandle(work->proceed); }
void Join(HANDLE thread)
{
    CHECK(thread != nullptr); if (!thread) return;
    const auto result = WaitForSingleObject(thread, 10000); CHECK(result == WAIT_OBJECT_0);
    if (result != WAIT_OBJECT_0) ExitProcess(3); CloseHandle(thread);
}
RizRuntimeInvokeInfoV1 Info(std::uint32_t status = 0)
{ RizRuntimeInvokeInfoV1 value{1, sizeof(value)}; CHECK(getInfo(&value) == status); return value; }
void Drained(std::uint64_t expectedCalls, std::uint64_t expectedFaults)
{
    const auto info = Info();
    CHECK(info.entered == expectedCalls && info.exited == expectedCalls && info.inFlight == 0);
    CHECK(info.observerFaults == expectedFaults && info.unknown == (expectedFaults ? 1u : 0u));
    CHECK(current == nullptr);
}
LONG CatchSeh(EXCEPTION_POINTERS* exception, Work* work)
{
    const auto* record = exception->ExceptionRecord;
    if (record->ExceptionCode == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;
    CHECK(record->ExceptionCode == OriginalSeh && record->NumberParameters == 3
        && record->ExceptionInformation[0] == Payload
        && record->ExceptionInformation[1] == reinterpret_cast<ULONG_PTR>(work->method)
        && record->ExceptionInformation[2] == reinterpret_cast<ULONG_PTR>(work->self));
    work->caught = true; work->exceptionCode = record->ExceptionCode;
    work->exceptionPayload = record->NumberParameters ? record->ExceptionInformation[0] : 0;
    return EXCEPTION_EXECUTE_HANDLER;
}
void ExecuteSeh(Work* work)
{
    __try {
        work->actualResult = RtRuntimeInvokeCall(reinterpret_cast<void*>(&RtRuntimeInvokeTarget), work->method, work->self,
            work->nullArguments ? nullptr : work->arguments, work->nullException ? nullptr : &work->exception);
        work->returned = true;
    }
    __except (CatchSeh(GetExceptionInformation(), work)) { }
}
void Execute(Work* work)
{
    auto* previous = current; current = work; work->thread = GetCurrentThreadId();
    try { ExecuteSeh(work); }
    catch (const CppOriginal& exception) {
        CHECK(exception.payload == Payload && exception.method == work->method && exception.self == work->self
            && exception.arguments == (work->nullArguments ? nullptr : work->arguments)
            && exception.exception == (work->nullException ? nullptr : &work->exception));
        work->caught = true; work->exceptionCode = 0xE06D7363; work->exceptionPayload = exception.payload;
    }
    CHECK(current == work && work->originalEntered == 1 && work->originalExited == 1);
    CHECK(work->returned == (work->mode == 0) && work->caught == (work->mode != 0));
    CHECK(work->after == (work->observed && work->mode == 0 ? 1 : 0));
    if (work->mode) {
        CHECK(work->exceptionCode == (work->mode == 1 ? OriginalSeh : 0xE06D7363) && work->exceptionPayload == Payload);
        InterlockedIncrement64(&originalExceptions);
    } else CHECK(work->actualResult == work->expectedResult);
    CHECK(work->exception == (work->nullException ? nullptr : &marker));
    if (work->observed) InterlockedIncrement64(&observedCalls); else InterlockedIncrement64(&unobservedCalls);
    current = previous;
}
DWORD WINAPI Worker(void* data) { Execute(static_cast<Work*>(data)); return 0; }
void __cdecl After(const RizRuntimeInvokeFrameV1* frame, void* user)
{
    auto* work = current; CHECK(work); if (!work) return;
    CHECK(work->observed && user == &marker && work->mode == 0);
    CHECK(frame->abiVersion == 1 && frame->byteSize == sizeof(*frame) && frame->reserved == 0);
    CHECK(frame->method == work->method && frame->self == work->self
        && frame->arguments == (work->nullArguments ? nullptr : work->arguments)
        && frame->exception == (work->nullException ? nullptr : &work->exception));
    CHECK(frame->result == work->expectedResult && frame->caller == &RtRuntimeInvokeCallerReturn);
    CHECK(frame->osThreadId == GetCurrentThreadId() && frame->osThreadId == work->thread);
    CHECK(work->originalEntered == 1 && work->originalExited == 1 && InterlockedIncrement(&work->after) == 1);
    CHECK(work->exception == (work->nullException ? nullptr : &marker));
    if (!work->nullArguments) CHECK(frame->arguments[0] == reinterpret_cast<void*>(0xAAAA1122BBBB3344ULL)
        && frame->arguments[1] == reinterpret_cast<void*>(0xCCCC5566DDDD7788ULL)
        && frame->arguments[2] == reinterpret_cast<void*>(0xEEEE99AAFFFFBBCCULL));
    InterlockedIncrement64(&afterCalls);
    if (work->faultKind) {
        InterlockedIncrement64(&observerExceptions);
        if (work->faultKind == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
        else throw CppObserver{Payload};
    }
}
}

extern "C" void* __cdecl RtRuntimeInvokeBody(const void* method, void* self, void** arguments, void** exception)
{
    auto* work = current; CHECK(work); if (!work) return nullptr;
    CHECK(method == work->method && self == work->self && arguments == (work->nullArguments ? nullptr : work->arguments)
        && exception == (work->nullException ? nullptr : &work->exception));
    CHECK(InterlockedIncrement(&work->originalEntered) == 1); InterlockedIncrement64(&originalCalls);
    struct Leave { Work* work; ~Leave() { InterlockedIncrement(&work->originalExited); } } leave{work};
    if (exception) *exception = &marker;
    if (work->hold) { CHECK(SetEvent(work->reached)); CHECK(WaitForSingleObject(work->proceed, 10000) == WAIT_OBJECT_0); }
    if (work->recurse) {
        Work child{}; Init(&child, true); child.method = reinterpret_cast<const void*>(0xABCD1234FEDC0090ULL);
        Execute(&child); CloseEvents(&child); CHECK(current == work); InterlockedIncrement64(&recursiveCalls);
    }
    if (work->mode == 1) {
        const ULONG_PTR payload[]{static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(method), reinterpret_cast<ULONG_PTR>(self)};
        RaiseException(OriginalSeh, 0, 3, payload);
    } else if (work->mode == 2) throw CppOriginal{Payload, method, self, arguments, exception};
    return work->expectedResult;
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) { std::fprintf(stderr, "Usage: RuntimeInvoke.Fixture.exe existing-helper.dll inspected-dobby.dll\n"); return 2; }
    const auto helper = LoadLibraryW(argv[1]), dobby = LoadLibraryW(argv[2]); CHECK(helper && dobby); if (!helper || !dobby) return 2;
    configure = Symbol<ConfigureFn>(helper, "RizRuntimeInvokeContext_Configure");
    attach = Symbol<AttachFn>(helper, "RizRuntimeInvokeContext_Attach");
    getInfo = Symbol<InfoFn>(helper, "RizRuntimeInvokeContext_GetInfo");
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    if (!configure || !attach || !getInfo || !prepare || !commit) return 2;
    auto initial = Info(2); CHECK(initial.entry && !initial.target && !initial.original && initial.epoch == 0
        && initial.attached == 0 && initial.unknown == 0 && initial.entered == 0 && initial.exited == 0 && initial.inFlight == 0 && initial.observerFaults == 0);
    const RizRuntimeInvokeObserverV1 observer{1, sizeof(observer), &After, &marker};
    CHECK(attach(1, &observer) == 2);
    RizRuntimeInvokeConfigV1 bad{1, sizeof(bad), nullptr, nullptr}; CHECK(configure(&bad) == 1);
    void* original{}; auto* target = reinterpret_cast<void*>(&RtRuntimeInvokeTarget); unsigned char prefix[32]{};
    std::memcpy(prefix, target, sizeof(prefix)); CHECK(prepare(target, initial.entry, &original) == 0 && original);
    CHECK(std::memcmp(prefix, target, sizeof(prefix)) == 0);
    const RizRuntimeInvokeConfigV1 config{1, sizeof(config), target, reinterpret_cast<RizRuntimeInvokeOriginalV1>(original)};
    CHECK(configure(&config) == 0 && configure(&config) == 2);
    const auto configured = Info(); CHECK(configured.target == target && reinterpret_cast<void*>(configured.original) == original
        && configured.entry == initial.entry && configured.epoch != 0 && configured.attached == 0 && configured.unknown == 0);
    CHECK(commit(target) == 0 && std::memcmp(prefix, target, sizeof(prefix)) != 0);
    Work before{}; Init(&before, false); Execute(&before); CloseEvents(&before); Drained(1, 0);
    Work old{}; Init(&old, false); old.hold = true;
    const auto thread = CreateThread(nullptr, 0, Worker, &old, 0, nullptr); CHECK(thread);
    CHECK(WaitForSingleObject(old.reached, 5000) == WAIT_OBJECT_0);
    CHECK(old.originalEntered == 1 && old.originalExited == 0 && old.after == 0);
    CHECK(Info().inFlight == 1);
    auto invalidObserver = observer; invalidObserver.after = nullptr;
    CHECK(attach(configured.epoch, &invalidObserver) == 1 && attach(configured.epoch + 1, &observer) == 3);
    CHECK(Info().attached == 0 && attach(configured.epoch, &observer) == 0);
    CHECK(Info().attached == 1 && attach(configured.epoch, &observer) == 4);
    CHECK(SetEvent(old.proceed)); Join(thread); CloseEvents(&old); Drained(2, 0);
    CHECK(old.returned && old.actualResult == old.expectedResult && old.after == 0);
    for (std::uint32_t mode = 0; mode != 3; ++mode) {
        Work work{}; Init(&work, true); work.mode = mode; work.recurse = true;
        const auto worker = CreateThread(nullptr, 0, Worker, &work, 0, nullptr); Join(worker); CloseEvents(&work);
        Drained(4 + mode * 2, 0);
    }
    for (std::uint32_t fault = 1; fault != 3; ++fault) {
        Work work{}; Init(&work, true); work.faultKind = fault; Execute(&work); CloseEvents(&work);
        Drained(7 + fault * 2, fault);
        Work clean{}; Init(&clean, true);
        if (fault == 1) clean.expectedResult = nullptr;
        else { clean.nullArguments = true; clean.nullException = true; }
        Execute(&clean); CloseEvents(&clean); Drained(8 + fault * 2, fault);
    }
    const auto final = Info();
    CHECK(originalCalls == 12 && observedCalls == 10 && unobservedCalls == 2 && afterCalls == 8
        && recursiveCalls == 3 && originalExceptions == 2 && observerExceptions == 2);
    CHECK(final.attached == 1 && final.unknown == 1 && final.entered == 12 && final.exited == 12 && final.inFlight == 0 && final.observerFaults == 2);
    CHECK(attach(configured.epoch, &observer) == 5);
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-runtime-invoke-after-transport\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,\"originalCalls\":%lld,\"afterCalls\":%lld,\"oldUnobservedCalls\":%lld,\"observedCalls\":%lld,\"recursiveCalls\":%lld,\"originalExceptions\":%lld,\"observerExceptions\":%lld,\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"observerFaults\":%llu,\"fullPointers\":true,\"exactNativeCaller\":true,\"nativeOriginalOnce\":true,\"lateAttachSnapshot\":true,\"firstInstallSafetyVerified\":false,\"gameRuntimeVerified\":false}\n",
        failures ? "false" : "true", checks, failures, originalCalls, afterCalls, unobservedCalls, observedCalls, recursiveCalls,
        originalExceptions, observerExceptions, final.entered, final.exited, final.inFlight, final.observerFaults);
    return failures ? 1 : 0;
}
