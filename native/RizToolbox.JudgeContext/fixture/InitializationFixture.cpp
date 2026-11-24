#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "../InitializationContext.h"

extern "C" void __cdecl RizInitializationFixture_StartTarget(void*, const void*);
extern "C" void* __cdecl RizInitializationFixture_GetterTarget(void*, void*, const void*);

namespace {
constexpr DWORD OriginalSehCode = 0xE1730211, CallbackSehCode = 0xE1730212;
constexpr std::uint64_t CppPayload = 0xF123456789ABCDEFULL;
enum Mode : unsigned {
    Normal = 0, Bare = 1, Nested = 2, Skip = 4, EnterSeh = 8, ExitSeh = 16,
    OriginalSeh = 32, OriginalCpp = 64, EnterCpp = 128, ExitCpp = 256,
    Replace = 512, ReturnNull = 1024, NonOneEnter = 2048, ZeroToken = 4096,
    NullOriginal = 8192, NullMethod = 16384, NullInformation = 32768,
    HoldEnter = 65536, HoldExit = 131072
};
struct OriginalCppException { std::uint64_t payload; void* self; void* information; const void* method; };
struct CallbackCppException { std::uint64_t payload; };
struct Expected {
    std::uint32_t kind; unsigned mode;
    void* self; void* information; const void* method;
    void* returned; std::uintptr_t token;
    unsigned originalCalls, enterCalls, exitCalls;
    std::uint32_t exitFlags;
};
struct Owner { std::uint32_t kind; std::uint64_t seal; };
using ConfigureFn = decltype(&RizInitializationContext_Configure);
using StatsFn = decltype(&RizInitializationContext_GetStats);
using GetEntryFn = decltype(&RizInitializationContext_GetEntry);
using SizeFn = decltype(&RizInitializationContext_GetConfigSize);
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
HMODULE helper, dobby;
ConfigureFn configure;
StatsFn getStats;
void* entries[2];
Owner owners[2]{ {0, CppPayload}, {1, CppPayload} };
void* targets[2]{ reinterpret_cast<void*>(&RizInitializationFixture_StartTarget), reinterpret_cast<void*>(&RizInitializationFixture_GetterTarget) };
volatile LONG64 failures = 0, checks = 0, originals[2]{}, enters[2]{}, exits[2]{}, sequence = 0, abnormal[2]{};
thread_local Expected* expectedStack[16]{};
thread_local unsigned expectedDepth = 0;
thread_local bool sawHelperUnwind = false;
HANDLE observerReached, observerContinue;

void Check(bool value, const char* text, int line)
{
    InterlockedIncrement64(&checks);
    if (!value) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED line %d: %s\n", line, text); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto address = GetProcAddress(module, name); CHECK(address != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(address)); std::memcpy(&result, &address, sizeof(result)); return result;
}
RizInitializationStatsV1 Stats(std::uint32_t kind)
{
    RizInitializationStatsV1 stats{ 1, sizeof(stats) }; CHECK(getStats(kind, &stats) == 0); return stats;
}
Expected* Current() { CHECK(expectedDepth > 0 && expectedDepth <= 16); return expectedStack[expectedDepth - 1]; }
void CheckArguments(Expected* expected, void* self, void* information, const void* method)
{
    CHECK(self == expected->self && information == expected->information && method == expected->method);
}
void Pause()
{
    CHECK(SetEvent(observerReached)); CHECK(WaitForSingleObject(observerContinue, 5000) == WAIT_OBJECT_0);
}
void* Execute(std::uint32_t kind, unsigned mode);
std::uint32_t __cdecl Enter(void* self, void* information, const void* method, std::uintptr_t* token, void* user)
{
    auto* expected = Current(); auto* owner = static_cast<Owner*>(user);
    CHECK(owner == &owners[expected->kind] && owner->seal == CppPayload);
    CheckArguments(expected, self, information, method);
    CHECK(token && *token == 0); CHECK(Stats(expected->kind).inFlight >= 1);
    ++expected->enterCalls; InterlockedIncrement64(&enters[expected->kind]);
    expected->token = expected->mode & ZeroToken ? 0 : static_cast<std::uintptr_t>(InterlockedIncrement64(&sequence));
    *token = expected->token; // Partial callback token is still paired after fault.
    if (expected->mode & HoldEnter) Pause();
    if (expected->mode & EnterSeh) RaiseException(CallbackSehCode, 0, 0, nullptr);
    if (expected->mode & EnterCpp) throw CallbackCppException{ CppPayload };
    return expected->mode & Skip ? 0u : expected->mode & NonOneEnter ? 2u : 1u;
}
void* __cdecl Exit(void* self, void* information, const void* method, void* returned,
    std::uintptr_t token, std::uint32_t flags, void* user)
{
    auto* expected = Current();
    CHECK(user == &owners[expected->kind]); CheckArguments(expected, self, information, method);
    CHECK(token == expected->token && expected->exitCalls == 0);
    const bool enterFault = !!(expected->mode & (EnterSeh | EnterCpp));
    const bool skipped = enterFault || !!(expected->mode & (Skip | NonOneEnter));
    const bool originalAbnormal = !skipped && !!(expected->mode & (OriginalSeh | OriginalCpp));
    const auto expectedFlags = (enterFault ? RizInitializationEnterFaulted : 0u)
        | (skipped ? RizInitializationSkipped : originalAbnormal ? RizInitializationAbnormal : RizInitializationReturned);
    CHECK(flags == expectedFlags);
    CHECK(returned == (!skipped && !originalAbnormal && expected->kind == RizInitializationGetter ? expected->returned : nullptr));
    ++expected->exitCalls; expected->exitFlags = flags;
    InterlockedIncrement64(&exits[expected->kind]);
    if (originalAbnormal) InterlockedIncrement64(&abnormal[expected->kind]);
    CHECK(Stats(expected->kind).inFlight >= 1);
    if (expected->mode & HoldExit) Pause();
    if (expected->mode & ExitSeh) RaiseException(CallbackSehCode, 0, 0, nullptr);
    if (expected->mode & ExitCpp) throw CallbackCppException{ CppPayload };
    if (expected->mode & ReturnNull) return nullptr;
    // On Skip/EnterFault this deliberately non-null return MUST NOT manufacture
    // successful initialization; the helper enforces a native null abort.
    if (skipped || expected->mode & Replace) return reinterpret_cast<void*>(0x7ABCDEF012345670ULL);
    return returned;
}
void OriginalBody(std::uint32_t kind, void* self, void* information, const void* method)
{
    auto* expected = Current(); CHECK(expected->kind == kind); CheckArguments(expected, self, information, method);
    ++expected->originalCalls; InterlockedIncrement64(&originals[kind]);
    if (!(expected->mode & Bare)) CHECK(Stats(kind).inFlight >= 1);
    if (expected->mode & Nested) {
        const auto retained = expected->token;
        Execute(kind, Normal | ZeroToken); Execute(kind ^ 1u, Normal);
        CHECK(Current() == expected && expected->token == retained && Stats(kind).inFlight == 1);
    }
    if (expected->mode & OriginalCpp) throw OriginalCppException{ CppPayload, self, information, method };
    if (expected->mode & OriginalSeh) {
        const ULONG_PTR payload[]{ 0x123456789ABCDEF0ULL, 0xFEDCBA9876543210ULL };
        RaiseException(OriginalSehCode, 0, 2, payload);
    }
}
void Fill(Expected* expected, std::uint32_t kind, unsigned mode)
{
    *expected = {}; expected->kind = kind; expected->mode = mode;
    expected->self = reinterpret_cast<void*>(0x12345678ABCDEF00ULL + kind * 16);
    expected->information = kind && !(mode & NullInformation) ? reinterpret_cast<void*>(0x23456789BCDEF010ULL) : nullptr;
    expected->method = mode & NullMethod ? nullptr : reinterpret_cast<void*>(0x3456789ACDEF0120ULL + kind * 16);
    expected->returned = mode & NullOriginal ? nullptr : reinterpret_cast<void*>(0x456789ABDEF01230ULL);
}
// POD locals: the fixture also balances its own TLS stack for either original
// exception without introducing a catch between the helper and native caller.
void* Execute(std::uint32_t kind, unsigned mode)
{
    Expected expected{}; Fill(&expected, kind, mode); void* result = nullptr;
    CHECK(expectedDepth < 16); expectedStack[expectedDepth++] = &expected;
    __try {
        if (kind == RizInitializationStart) reinterpret_cast<RizInitializationStartV1>(targets[kind])(expected.self, expected.method);
        else result = reinterpret_cast<RizInitializationGetterV1>(targets[kind])(expected.self, expected.information, expected.method);
    }
    __finally {
        const bool bare = !!(mode & Bare), enterFault = !!(mode & (EnterSeh | EnterCpp));
        const bool skipped = !bare && (enterFault || !!(mode & (Skip | NonOneEnter)));
        CHECK(expected.originalCalls == (skipped ? 0u : 1u));
        CHECK(expected.enterCalls == (bare ? 0u : 1u) && expected.exitCalls == (bare ? 0u : 1u));
        expectedStack[--expectedDepth] = nullptr;
    }
    if (kind == RizInitializationGetter) {
        const bool nullResult = !!(mode & (Skip | NonOneEnter | EnterSeh | EnterCpp | ExitSeh | ExitCpp | ReturnNull | NullOriginal));
        CHECK(result == (nullResult ? nullptr : mode & Replace ? reinterpret_cast<void*>(0x7ABCDEF012345670ULL) : expected.returned));
    }
    return result;
}
int OriginalFilter(EXCEPTION_POINTERS* exception)
{
    CHECK(exception->ExceptionRecord->ExceptionCode == OriginalSehCode && exception->ExceptionRecord->NumberParameters == 2);
    CHECK(exception->ExceptionRecord->ExceptionInformation[0] == 0x123456789ABCDEF0ULL);
    CHECK(exception->ExceptionRecord->ExceptionInformation[1] == 0xFEDCBA9876543210ULL);
    auto context = *exception->ContextRecord; sawHelperUnwind = false;
    for (unsigned index = 0; index != 96 && context.Rip && context.Rsp; ++index) {
        DWORD64 imageBase = 0; const auto function = RtlLookupFunctionEntry(context.Rip, &imageBase, nullptr);
        if (imageBase == reinterpret_cast<DWORD64>(helper)) sawHelperUnwind = true;
        if (function) {
            PVOID data = nullptr; DWORD64 frame = 0;
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, context.Rip, function, &context, &data, &frame, nullptr);
        } else { context.Rip = *reinterpret_cast<const DWORD64*>(context.Rsp); context.Rsp += 8; }
    }
    CHECK(sawHelperUnwind); return EXCEPTION_EXECUTE_HANDLER;
}
void ExecuteSeh(std::uint32_t kind, unsigned additional)
{
    bool caught = false;
    __try { Execute(kind, OriginalSeh | additional); }
    __except (OriginalFilter(GetExceptionInformation())) { caught = true; }
    CHECK(caught && sawHelperUnwind && expectedDepth == 0 && Stats(kind).inFlight == 0);
}
void ExecuteCpp(std::uint32_t kind, unsigned additional)
{
    bool caught = false;
    try { Execute(kind, OriginalCpp | additional); }
    catch (const OriginalCppException& exception) {
        Expected expected{}; Fill(&expected, kind, Normal);
        CHECK(exception.payload == CppPayload && exception.self == expected.self
            && exception.information == expected.information && exception.method == expected.method);
        caught = true;
    }
    CHECK(caught && expectedDepth == 0 && Stats(kind).inFlight == 0);
}
struct Work { std::uint32_t kind; unsigned mode; };
DWORD WINAPI Worker(void* context)
{
    const auto* work = static_cast<Work*>(context);
    if (work->mode & OriginalSeh) ExecuteSeh(work->kind, work->mode & ~OriginalSeh);
    else if (work->mode & OriginalCpp) ExecuteCpp(work->kind, work->mode & ~OriginalCpp);
    else Execute(work->kind, work->mode);
    CHECK(expectedDepth == 0); return 0;
}
void WorkerRun(std::uint32_t kind, unsigned mode, bool observeInFlight)
{
    Work work{ kind, mode }; ResetEvent(observerReached); ResetEvent(observerContinue);
    auto thread = CreateThread(nullptr, 0, Worker, &work, 0, nullptr); CHECK(thread != nullptr);
    if (!thread) return;
    if (observeInFlight) {
        CHECK(WaitForSingleObject(observerReached, 5000) == WAIT_OBJECT_0);
        const auto inProgress = Stats(kind); CHECK(inProgress.inFlight == 1 && inProgress.entered == inProgress.exited + 1);
        CHECK(SetEvent(observerContinue));
    }
    CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
    CHECK(Stats(kind).inFlight == 0);
}
struct ConfigureWork {
    std::uint32_t kind;
    const RizInitializationConfigV1* config;
    HANDLE start;
    std::uint32_t result;
};
DWORD WINAPI ConfigureWorker(void* context)
{
    auto* work = static_cast<ConfigureWork*>(context);
    CHECK(WaitForSingleObject(work->start, 5000) == WAIT_OBJECT_0);
    work->result = configure(work->kind, work->config); return 0;
}
void ConfigureRace(std::uint32_t kind, const RizInitializationConfigV1* config)
{
    constexpr unsigned count = 8;
    ConfigureWork work[count]{}; HANDLE threads[count]{};
    auto start = CreateEventW(nullptr, TRUE, FALSE, nullptr); CHECK(start != nullptr);
    for (unsigned index = 0; index != count; ++index) {
        work[index] = {kind, config, start, 99};
        threads[index] = CreateThread(nullptr, 0, ConfigureWorker, &work[index], 0, nullptr); CHECK(threads[index] != nullptr);
    }
    CHECK(SetEvent(start));
    CHECK(WaitForMultipleObjects(count, threads, TRUE, 10000) == WAIT_OBJECT_0);
    unsigned configuredCount = 0;
    for (unsigned index = 0; index != count; ++index) {
        CHECK(work[index].result == 0 || work[index].result == 2);
        if (work[index].result == 0) ++configuredCount;
        CloseHandle(threads[index]);
    }
    CHECK(configuredCount == 1); CloseHandle(start);
}
void ConcurrentKinds()
{
    Work work[2]{{0, HoldEnter}, {1, HoldEnter}}; HANDLE threads[2]{};
    ResetEvent(observerReached); ResetEvent(observerContinue);
    for (unsigned index = 0; index != 2; ++index) {
        threads[index] = CreateThread(nullptr, 0, Worker, &work[index], 0, nullptr); CHECK(threads[index] != nullptr);
    }
    // Observe two independent configurations with callbacks genuinely in flight
    // on distinct native threads, before allowing either finally to complete.
    const auto deadline = GetTickCount64() + 4000;
    while ((Stats(0).inFlight != 1 || Stats(1).inFlight != 1) && GetTickCount64() < deadline) Sleep(1);
    CHECK(Stats(0).inFlight == 1 && Stats(1).inFlight == 1);
    CHECK(SetEvent(observerContinue));
    CHECK(WaitForMultipleObjects(2, threads, TRUE, 10000) == WAIT_OBJECT_0);
    for (const auto thread : threads) CloseHandle(thread);
    CHECK(Stats(0).inFlight == 0 && Stats(1).inFlight == 0);
}
}

extern "C" void __cdecl RizInitializationFixture_StartBody(void* self, const void* method)
{ OriginalBody(RizInitializationStart, self, nullptr, method); }
extern "C" void* __cdecl RizInitializationFixture_GetterBody(void* self, void* information, const void* method)
{ OriginalBody(RizInitializationGetter, self, information, method); return Current()->returned; }

int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) { std::fprintf(stderr, "Usage: InitializationContext.Fixture.exe helper.dll dobby.dll\n"); return 2; }
    helper = LoadLibraryW(argv[1]); dobby = LoadLibraryW(argv[2]); CHECK(helper && dobby);
    if (!helper || !dobby) return 2;
    configure = Symbol<ConfigureFn>(helper, "RizInitializationContext_Configure");
    getStats = Symbol<StatsFn>(helper, "RizInitializationContext_GetStats");
    auto getEntry = Symbol<GetEntryFn>(helper, "RizInitializationContext_GetEntry");
    auto size = Symbol<SizeFn>(helper, "RizInitializationContext_GetConfigSize");
    auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    CHECK(size() == 0x28 && getEntry(2) == nullptr && configure(2, nullptr) == 1);
    CHECK(getStats(2, nullptr) == 1 && configure(0, nullptr) == 1);
    observerReached = CreateEventW(nullptr, TRUE, FALSE, nullptr); observerContinue = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    CHECK(observerReached && observerContinue);
    for (std::uint32_t kind = 0; kind != 2; ++kind) {
        entries[kind] = getEntry(kind); CHECK(entries[kind] != nullptr);
        CHECK(Stats(kind).entered == 0 && Stats(kind).inFlight == 0);
        RizInitializationConfigV1 invalid{ 2, sizeof(invalid), targets[kind], Enter, Exit, &owners[kind] };
        CHECK(configure(kind, &invalid) == 1); invalid.abiVersion = 1; --invalid.byteSize; CHECK(configure(kind, &invalid) == 1);
        invalid.byteSize = sizeof(invalid); invalid.original = nullptr; CHECK(configure(kind, &invalid) == 1);
        unsigned char originalBytes[32]{}; std::memcpy(originalBytes, targets[kind], sizeof(originalBytes));
        Execute(kind, Bare);
        void* trampoline = nullptr; CHECK(prepare(targets[kind], entries[kind], &trampoline) == 0 && trampoline);
        CHECK(std::memcmp(originalBytes, targets[kind], sizeof(originalBytes)) == 0);
        Execute(kind, Bare); // Prepare does not publish the replacement.
        RizInitializationConfigV1 config{ 1, sizeof(config), trampoline, Enter, Exit, &owners[kind] };
        ConfigureRace(kind, &config); CHECK(configure(kind, &config) == 2);
        config = {}; // Helper copied the immutable configuration, not its address.
        Execute(kind, Bare); // Configure also does not publish the native detour.
        CHECK(Stats(kind).entered == 0); CHECK(commit(targets[kind]) == 0);
        CHECK(std::memcmp(originalBytes, targets[kind], sizeof(originalBytes)) != 0);
        Execute(kind, Normal);
    }
    for (std::uint32_t kind = 0; kind != 2; ++kind) {
        for (const auto mode : { Normal, NullMethod, NullInformation, ZeroToken, Replace, ReturnNull, NullOriginal,
                Skip, NonOneEnter, EnterSeh, EnterCpp, ExitSeh, ExitCpp, Nested }) Execute(kind, mode);
        ExecuteSeh(kind, Normal); ExecuteSeh(kind, ExitSeh); ExecuteSeh(kind, ExitCpp);
        ExecuteCpp(kind, Normal); ExecuteCpp(kind, ExitSeh); ExecuteCpp(kind, ExitCpp);
        WorkerRun(kind, Normal, false); WorkerRun(kind, Nested, false);
        WorkerRun(kind, HoldEnter, true); WorkerRun(kind, HoldExit, true);
        WorkerRun(kind, OriginalSeh | ExitSeh, false); WorkerRun(kind, OriginalCpp | ExitCpp, false);
        RizInitializationConfigV1 second{ 1, sizeof(second), targets[kind], Enter, Exit, &owners[kind] };
        CHECK(configure(kind, &second) == 2);
        const auto stats = Stats(kind);
        CHECK(stats.entered == stats.exited && stats.inFlight == 0 && stats.entered == static_cast<std::uint64_t>(enters[kind])
            && stats.exited == static_cast<std::uint64_t>(exits[kind]));
        CHECK(stats.enterFaults == 2 && stats.exitFaults == 8 && stats.skipped == 4 && abnormal[kind] == 8);
    }
    ConcurrentKinds();
    const auto startStats = Stats(0), getterStats = Stats(1);
    CHECK(startStats.entered == startStats.exited && getterStats.entered == getterStats.exited
        && startStats.inFlight == 0 && getterStats.inFlight == 0);
    CHECK(expectedDepth == 0); CloseHandle(observerReached); CloseHandle(observerContinue);
    std::printf("{\"kind\":\"native-initialization-context-unwind-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,\"realDobby\":true,\"prepareConfigureCommit\":true,\"configureRace\":true,\"typedStart\":true,\"typedGetter\":true,\"originalSehPreserved\":true,\"originalCppPreserved\":true,\"nativeWorker\":true,\"callbacksContained\":true,\"start\":{\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"enterFaults\":%llu,\"exitFaults\":%llu,\"skipped\":%llu},\"getter\":{\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"enterFaults\":%llu,\"exitFaults\":%llu,\"skipped\":%llu},\"gameRuntimeVerified\":false}\n",
        failures ? "false" : "true", checks, failures,
        startStats.entered, startStats.exited, startStats.inFlight, startStats.enterFaults, startStats.exitFaults, startStats.skipped,
        getterStats.entered, getterStats.exited, getterStats.inFlight, getterStats.enterFaults, getterStats.exitFaults, getterStats.skipped);
    return failures ? 1 : 0;
}
