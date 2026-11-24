#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../OperationContext.h"

namespace {
constexpr DWORD OriginalSeh = 0xe04f5053, ObserverSeh = 0xe04f5043;
constexpr std::uint64_t Payload = 0xfedcba9876543210ULL;
constexpr std::uintptr_t TokenMask = 0xf123456789abcdefULL;
using EntryFn = decltype(&RizOperationContext_GetEntry);
using ConfigureFn = decltype(&RizOperationContext_Configure);
using StatsFn = decltype(&RizOperationContext_GetStats);
struct Owner { unsigned kind{}, slot{}; volatile LONG active = 1; };
Owner owners[3][3]{};
HMODULE helper{}; EntryFn getEntry{}; ConfigureFn configure{}; StatsFn getStats{};
volatile LONG64 checks{}, failures{}, originals{}, nativeEnters{}, nativeExits{}, sequenceChecks{}, originalExceptions{}, callbackExceptions{};
volatile LONG64 workerCalls{}, nestedCalls{}, lateConfigurations{}, logicalDetachCases{}, legitimateZeroTokens{};
SRWLOCK sequenceLock = SRWLOCK_INIT;
std::uint64_t seenSequences[1024]{}; std::size_t seenCount{};
struct Work {
    unsigned kind{}, slot{}, originalFault{}, enterFault{}, exitFault{};
    bool zeroToken{}, nullArgument{}, detach{}, returned{}, caught{}, captured{};
    DWORD thread{}, exceptionCode{}, exceptionFlags{}, parameters{};
    ULONG_PTR information[4]{}; const void* exceptionAddress{};
    std::uint64_t storage0 = 0x1111222233334444ULL, storage1 = 0x5555666677778888ULL;
    unsigned entered{}, exited{}, invoked{}, deliveries{}, completions{};
    std::uintptr_t token{}; std::uint32_t flags{};
    RizOperationFrameV1 frame{};
    Work* previous{}; Work* nested{};
    HANDLE reached{}, resume{};
};
thread_local Work* current{};
struct OriginalCpp { std::uint64_t payload; const Work* work; unsigned kind; };
struct ObserverCpp { std::uint64_t payload; };
void Check(bool condition, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!condition) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED Operation line %d: %s\n", line, expression); }
}
#define CHECK(value) Check(!!(value), #value, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto symbol = GetProcAddress(module, name); CHECK(symbol != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(symbol)); std::memcpy(&result, &symbol, sizeof(result)); return result;
}
void* Arg0(Work* work) { return work->kind > 0 && !work->nullArgument ? &work->storage0 : nullptr; }
void* Arg1(Work* work) { return work->kind > 1 && !work->nullArgument ? &work->storage1 : nullptr; }
const void* Method(Work* work) { return &work->originalFault; }
void ThrowObserver(unsigned fault)
{
    if (!fault) return;
    InterlockedIncrement64(&callbackExceptions);
    if (fault == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
    else throw ObserverCpp{ Payload };
}
std::uintptr_t __cdecl Enter(const RizOperationFrameV1* frame, void* user)
{
    auto* work = static_cast<Work*>(frame->self); auto* owner = static_cast<Owner*>(user);
    CHECK(owner == &owners[work->kind][work->slot] && owner->kind == work->kind && owner->slot == work->slot);
    CHECK(frame->abiVersion == 1 && frame->byteSize == sizeof(*frame) && frame->kind == work->kind && frame->reserved == 0);
    CHECK(frame->arg0 == Arg0(work) && frame->arg1 == Arg1(work) && frame->method == Method(work) && frame->sequence != 0);
    HMODULE callerModule{};
    CHECK(frame->caller && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(frame->caller), &callerModule) && callerModule == GetModuleHandleW(nullptr));
    AcquireSRWLockExclusive(&sequenceLock);
    bool unique = seenCount < sizeof(seenSequences) / sizeof(seenSequences[0]);
    for (std::size_t i = 0; i != seenCount; ++i) if (seenSequences[i] == frame->sequence) unique = false;
    if (unique) seenSequences[seenCount++] = frame->sequence;
    ReleaseSRWLockExclusive(&sequenceLock);
    CHECK(unique); InterlockedIncrement64(&sequenceChecks);
    work->frame = *frame; work->thread = GetCurrentThreadId(); ++work->entered;
    work->previous = current; current = work;
    work->captured = InterlockedCompareExchange(&owner->active, 0, 0) != 0;
    if (work->captured) ++work->deliveries;
    work->token = work->zeroToken || !work->captured ? 0 : reinterpret_cast<std::uintptr_t>(work) ^ TokenMask;
    InterlockedIncrement64(&nativeEnters);
    ThrowObserver(work->enterFault);
    return work->token;
}
void __cdecl Exit(const RizOperationFrameV1* frame, std::uintptr_t token, std::uint32_t flags, void* user)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(user == &owners[work->kind][work->slot] && current == work && work->thread == GetCurrentThreadId());
    CHECK(std::memcmp(frame, &work->frame, sizeof(*frame)) == 0);
    CHECK(token == (work->enterFault ? 0 : work->token));
    CHECK(flags == (work->originalFault ? RizOperationAbnormal : RizOperationReturned)
        + (work->enterFault ? RizOperationEnterFaulted : 0u));
    work->flags = flags; ++work->exited;
    // A logical observer may be inactive by now. Its already-acquired scope
    // still completes; native descriptor and physical Exit never detach.
    if (work->captured) ++work->completions;
    current = work->previous; InterlockedIncrement64(&nativeExits);
    ThrowObserver(work->exitFault);
}
void Run(Work* work);
void Original(Work* work, unsigned kind, void* first, void* second, const void* method)
{
    CHECK(current == work && kind == work->kind && first == Arg0(work) && second == Arg1(work) && method == Method(work));
    CHECK(work->entered == 1 && work->exited == 0 && GetCurrentThreadId() == work->thread);
    ++work->invoked; InterlockedIncrement64(&originals);
    if (work->detach) InterlockedExchange(&owners[work->kind][work->slot].active, 0);
    if (work->nested) { Run(work->nested); CHECK(current == work); InterlockedIncrement64(&nestedCalls); }
    if (work->originalFault == 1) {
        const ULONG_PTR parameters[]{ static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(work), work->kind };
        RaiseException(OriginalSeh, 0, 3, parameters);
    } else if (work->originalFault == 2) throw OriginalCpp{ Payload, work, work->kind };
}
__declspec(noinline) void __cdecl Original0(void* self, const void* method)
{ Original(static_cast<Work*>(self), 0, nullptr, nullptr, method); }
__declspec(noinline) void __cdecl Original1(void* self, void* arg0, const void* method)
{ Original(static_cast<Work*>(self), 1, arg0, nullptr, method); }
__declspec(noinline) void __cdecl Original2(void* self, void* arg0, void* arg1, const void* method)
{ Original(static_cast<Work*>(self), 2, arg0, arg1, method); }
void* OriginalFor(unsigned kind)
{ return kind == 0 ? reinterpret_cast<void*>(&Original0) : kind == 1 ? reinterpret_cast<void*>(&Original1) : reinterpret_cast<void*>(&Original2); }
RizOperationConfigV1 Configuration(unsigned kind, unsigned slot)
{
    owners[kind][slot].kind = kind; owners[kind][slot].slot = slot;
    return { 1, sizeof(RizOperationConfigV1), OriginalFor(kind), Enter, Exit, &owners[kind][slot] };
}
LONG Filter(EXCEPTION_POINTERS* pointers, Work* work)
{
    const auto* record = pointers->ExceptionRecord;
    work->exceptionCode = record->ExceptionCode; work->exceptionFlags = record->ExceptionFlags;
    work->parameters = record->NumberParameters; work->exceptionAddress = record->ExceptionAddress;
    for (unsigned i = 0; i != 4; ++i) work->information[i] = i < record->NumberParameters ? record->ExceptionInformation[i] : 0;
    return record->ExceptionCode == 0xe06d7363 ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER;
}
__declspec(noinline) void InvokeSeh(Work* work)
{
    __try {
        const auto entry = getEntry(work->kind, work->slot);
        if (work->kind == 0) reinterpret_cast<void(__cdecl*)(void*, const void*)>(entry)(work, Method(work));
        else if (work->kind == 1) reinterpret_cast<void(__cdecl*)(void*, void*, const void*)>(entry)(work, Arg0(work), Method(work));
        else reinterpret_cast<void(__cdecl*)(void*, void*, void*, const void*)>(entry)(work, Arg0(work), Arg1(work), Method(work));
        work->returned = true;
    } __except (Filter(GetExceptionInformation(), work)) { work->caught = true; }
}
void Run(Work* work)
{
    auto* previous = current;
    try { InvokeSeh(work); }
    catch (const OriginalCpp& error) {
        CHECK(error.payload == Payload && error.work == work && error.kind == work->kind);
        CHECK(work->exceptionCode == 0xe06d7363 && work->parameters == 4 && work->information[0] == 0x19930520
            && work->information[1] != 0 && work->information[2] != 0
            && work->information[3] == reinterpret_cast<ULONG_PTR>(GetModuleHandleW(nullptr)));
        work->caught = true;
    } catch (...) { CHECK(false); work->caught = true; }
    CHECK(current == previous && work->entered == 1 && work->exited == 1 && work->invoked == 1);
    CHECK(work->returned == (work->originalFault == 0) && work->caught == (work->originalFault != 0));
    CHECK(work->deliveries == work->completions);
    if (work->originalFault) {
        CHECK(work->exceptionAddress != nullptr);
        if (work->originalFault == 1) CHECK(work->exceptionCode == OriginalSeh && work->parameters == 3
            && work->information[0] == Payload && work->information[1] == reinterpret_cast<ULONG_PTR>(work)
            && work->information[2] == work->kind);
        InterlockedIncrement64(&originalExceptions);
    }
    if (work->zeroToken && !work->enterFault) InterlockedIncrement64(&legitimateZeroTokens);
}
DWORD WINAPI Worker(void* data) { Run(static_cast<Work*>(data)); InterlockedIncrement64(&workerCalls); return 0; }
void Join(HANDLE thread)
{
    CHECK(thread != nullptr); if (!thread) ExitProcess(2);
    const auto waited = WaitForSingleObject(thread, 10000); CHECK(waited == WAIT_OBJECT_0);
    if (waited != WAIT_OBJECT_0) ExitProcess(3); CloseHandle(thread);
}
DWORD WINAPI LateWorker(void* data)
{
    auto* work = static_cast<Work*>(data);
    CHECK(SetEvent(work->reached));
    if (WaitForSingleObject(work->resume, 10000) != WAIT_OBJECT_0) ExitProcess(3);
    Run(work); return 0;
}
void LateConfigure(unsigned kind)
{
    Work work{}; work.kind = kind; work.slot = 2;
    work.reached = CreateEventW(nullptr, TRUE, FALSE, nullptr); work.resume = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    CHECK(work.reached && work.resume);
    const auto entryBefore = getEntry(kind, 2); CHECK(entryBefore);
    const auto thread = CreateThread(nullptr, 0, LateWorker, &work, 0, nullptr); CHECK(thread);
    CHECK(WaitForSingleObject(work.reached, 10000) == WAIT_OBJECT_0);
    RizOperationStatsV1 stats{ 1, sizeof(stats), 0, 0, 0, 0, 0 };
    CHECK(getStats(kind, 2, &stats) == 0 && stats.entered == 0 && stats.inFlight == 0);
    auto config = Configuration(kind, 2); CHECK(configure(kind, 2, &config) == 0);
    config.original = nullptr; config.enter = nullptr; config.exit = nullptr; config.user = nullptr;
    CHECK(getEntry(kind, 2) == entryBefore); CHECK(SetEvent(work.resume)); Join(thread);
    CloseHandle(work.reached); CloseHandle(work.resume); InterlockedIncrement64(&lateConfigurations);
}
int Result()
{
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-operation-context-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,"
        "\"originalCalls\":%lld,\"entered\":%lld,\"exited\":%lld,\"uniqueSequenceChecks\":%lld,\"originalExceptionCases\":%lld,"
        "\"observerExceptionCases\":%lld,\"nativeWorkerCalls\":%lld,\"nestedCalls\":%lld,\"lateConfigurations\":%lld,"
        "\"logicalDetachCases\":%lld,\"legitimateZeroTokens\":%lld,\"frameSize\":64,\"kinds\":[0,1,2],\"slotsPerKind\":32,"
        "\"configuredNativeTypedCalls\":true,\"callbackFaultsContained\":true,\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,"
        "\"realDobbyPreparedCommit\":false,\"firstInstallSafetyVerified\":false,\"sequenceExhaustionInjected\":false,"
        "\"lateConfigurationBeforeFirstEntryOnly\":true,\"exceptionFlagsTransparencyVerified\":false}\n",
        failures == 0 ? "true" : "false", static_cast<long long>(checks), static_cast<long long>(failures),
        static_cast<long long>(originals), static_cast<long long>(nativeEnters), static_cast<long long>(nativeExits),
        static_cast<long long>(sequenceChecks), static_cast<long long>(originalExceptions), static_cast<long long>(callbackExceptions),
        static_cast<long long>(workerCalls), static_cast<long long>(nestedCalls), static_cast<long long>(lateConfigurations),
        static_cast<long long>(logicalDetachCases), static_cast<long long>(legitimateZeroTokens));
    return failures == 0 ? 0 : 1;
}
}
int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    CHECK(argc == 2); if (argc != 2) return Result();
    helper = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    CHECK(helper); if (!helper) return Result();
    const auto limit = Symbol<decltype(&RizOperationContext_GetLimit)>(helper, "RizOperationContext_GetLimit");
    const auto frameSize = Symbol<decltype(&RizOperationContext_GetFrameSize)>(helper, "RizOperationContext_GetFrameSize");
    getEntry = Symbol<EntryFn>(helper, "RizOperationContext_GetEntry");
    configure = Symbol<ConfigureFn>(helper, "RizOperationContext_Configure");
    getStats = Symbol<StatsFn>(helper, "RizOperationContext_GetStats");
    if (failures) return Result();
    // Kinds 0..3 are valid (3 is the single-event ABI used by Tween admission);
    // kind 4 and slot 32 are the first values outside the table.
    CHECK(limit() == 32 && frameSize() == 64 && getEntry(4, 0) == nullptr && getEntry(0, 32) == nullptr);
    for (unsigned kind = 0; kind != 3; ++kind) {
        auto config = Configuration(kind, 0); auto invalid = config;
        invalid.abiVersion = 2; CHECK(configure(kind, 0, &invalid) == 1);
        invalid = config; invalid.original = getEntry(kind, 0); CHECK(configure(kind, 0, &invalid) == 1);
        CHECK(configure(kind, 0, nullptr) == 1 && configure(kind, 32, &config) == 1);
        CHECK(configure(kind, 0, &config) == 0 && configure(kind, 0, &config) == 2);
        config = Configuration(kind, 1); CHECK(configure(kind, 1, &config) == 0);
        for (unsigned variant = 0; variant != 3; ++variant) {
            Work work{}; work.kind = kind; work.zeroToken = variant == 1; work.nullArgument = variant == 2; Run(&work);
        }
        for (unsigned fault = 1; fault != 3; ++fault) {
            Work original{}; original.kind = kind; original.originalFault = fault; Run(&original);
            Work enter{}; enter.kind = kind; enter.slot = 1; enter.enterFault = fault; Run(&enter);
            Work exit{}; exit.kind = kind; exit.slot = 1; exit.exitFault = fault; Run(&exit);
            Work combined{}; combined.kind = kind; combined.slot = 1; combined.originalFault = fault;
            combined.enterFault = 3 - fault; combined.exitFault = fault; Run(&combined);
            Work recovery{}; recovery.kind = kind; recovery.slot = 1; Run(&recovery);
        }
        Work nested{}; nested.kind = kind; Work parent{}; parent.kind = kind; parent.nested = &nested; Run(&parent);
        // Configure all primary slots first; defer cross-kind nesting below.
        LateConfigure(kind);
        Work detach{}; detach.kind = kind; detach.detach = true; Run(&detach);
        Work inactive{}; inactive.kind = kind; Run(&inactive);
        CHECK(detach.deliveries == 1 && detach.completions == 1 && inactive.deliveries == 0 && inactive.completions == 0);
        InterlockedExchange(&owners[kind][0].active, 1); InterlockedIncrement64(&logicalDetachCases);
    }
    for (unsigned kind = 0; kind != 3; ++kind) {
        Work nested{}; nested.kind = (kind + 1) % 3; Work parent{}; parent.kind = kind; parent.nested = &nested; Run(&parent);
    }
    Work workers[12]{}; HANDLE threads[12]{};
    for (unsigned index = 0; index != 12; ++index) { workers[index].kind = index % 3; threads[index] = CreateThread(nullptr, 0, Worker, &workers[index], 0, nullptr); }
    for (const auto thread : threads) Join(thread);
    for (unsigned kind = 0; kind != 3; ++kind) for (unsigned slot = 0; slot != 3; ++slot) {
        RizOperationStatsV1 stats{ 1, sizeof(stats), 0, 0, 0, 0, 0 };
        CHECK(getStats(kind, slot, &stats) == 0 && stats.entered == stats.exited && stats.inFlight == 0);
        CHECK(stats.enterFaults == (slot == 1 ? 4u : 0u) && stats.exitFaults == (slot == 1 ? 4u : 0u));
        stats.abiVersion = 2; CHECK(getStats(kind, slot, &stats) == 1);
    }
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr);
    CHECK(current == nullptr && originals == nativeEnters && nativeEnters == nativeExits && sequenceChecks == originals
        && originalExceptions == 12 && callbackExceptions == 24 && workerCalls == 12 && nestedCalls == 6
        && lateConfigurations == 3 && logicalDetachCases == 3 && legitimateZeroTokens == 3);
    return Result();
}
