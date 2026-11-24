#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../JudgeContext.h"

struct alignas(16) RegisterImage { std::uint64_t gpr[16]; RizJudgeXmmV1 xmm[16]; };
static_assert(sizeof(RegisterImage) == 0x180);
extern "C" void RizFixture_Invoke(void*, const RegisterImage*, RegisterImage*);
extern "C" char RizFixture_ReturnSite;

namespace {
constexpr DWORD OriginalException = 0xe1230042;
constexpr DWORD ObserverException = 0xe1230043;
enum Mode : unsigned { Normal=0, Unknown=1, Reentry=2, OriginalSeh=4, Disable=8, EnterSeh=16, ExitSeh=32, OriginalCpp=64 };
struct OriginalCppException { std::uint64_t payload; };
using ConfigureFn = decltype(&RizJudgeContext_Configure);
using GetEntryFn = decltype(&RizJudgeContext_GetEntry);
using GetFrameSizeFn = decltype(&RizJudgeContext_GetFrameSize);
using SetEnabledFn = decltype(&RizJudgeContext_SetEnabled);
using GetStatsFn = decltype(&RizJudgeContext_GetStats);
using ReadConfigurationFn = decltype(&RizJudgeContext_ReadConfigurationV1);
ConfigureFn configure;
SetEnabledFn setEnabled;
GetStatsFn getStats;
ReadConfigurationFn readConfiguration;
void* entry;
void* bridgeEntry;
HMODULE library;
volatile LONG64 failures=0, checks=0, originalCalls=0, enterCalls=0, exitCalls=0, tokenSequence=0;
volatile LONG64 abnormalExits=0, zeroTokenExits=0;
int receiverObject, methodObject, userObject;
struct Expected { RegisterImage registers; unsigned mode; std::uint64_t entryRsp; };
struct Retained { std::uintptr_t token; const RizJudgeFrameV1* address; RizJudgeFrameV1 copy; };
thread_local Expected* expectedStack[16]{};
thread_local unsigned expectedDepth=0;
thread_local Retained contexts[16]{};
thread_local unsigned contextDepth=0;
thread_local bool sawBridgeUnwind=false, sawCallerUnwind=false;

void Check(bool value, const char* expression, int line) {
    InterlockedIncrement64(&checks);
    if (!value) {
        InterlockedIncrement64(&failures);
        std::fprintf(stderr, "FAILED line %d: %s\n", line, expression);
    }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)

template<class T> T Symbol(const char* name) {
    const auto address = GetProcAddress(library, name);
    CHECK(address != nullptr);
    static_assert(sizeof(T) == sizeof(address));
    T result{};
    std::memcpy(&result, &address, sizeof(result));
    return result;
}
std::uint32_t FloatBits(float value) { std::uint32_t bits; std::memcpy(&bits, &value, 4); return bits; }
void Fill(Expected* expected, unsigned mode, std::uint32_t deltaBits) {
    *expected = {};
    expected->mode = mode;
    for (unsigned i=0; i<16; ++i) {
        expected->registers.gpr[i] = 0x1111111100000000ull + i * 0x102030405ull;
        expected->registers.xmm[i] = {0x0123456700000000ull + i*0x201030405ull, 0xfedcba9800000000ull + i};
    }
    expected->registers.gpr[RizRcx] = reinterpret_cast<std::uintptr_t>(&receiverObject);
    expected->registers.gpr[RizRdx] = 0xabcddcba80000005ull; // signed enum32, high bits must not affect argument
    expected->registers.gpr[RizR9] = reinterpret_cast<std::uintptr_t>(&methodObject);
    expected->registers.xmm[2].low = (expected->registers.xmm[2].low & 0xffffffff00000000ull) | deltaBits;
}
void Execute(unsigned, std::uint32_t);

std::uintptr_t __fastcall Enter(const RizJudgeFrameV1* frame, void* user) {
    InterlockedIncrement64(&enterCalls);
    CHECK(user == &userObject);
    CHECK(expectedDepth > 0 && expectedDepth <= 16);
    auto* expected = expectedStack[expectedDepth-1];
    CHECK(frame->abiVersion == 1 && frame->byteSize == 0x1a0 && frame->reserved == 0);
    CHECK(frame->gpr[RizRsp] == frame->entryStackPointer);
    CHECK((frame->entryStackPointer & 15) == 8);
    CHECK(*reinterpret_cast<const std::uint64_t*>(frame->entryStackPointer) == frame->returnAddress);
    if (expected->mode & Unknown) {
        CHECK(frame->returnAddress != reinterpret_cast<std::uintptr_t>(&RizFixture_ReturnSite));
        return 0; // precise source not recognized: no identity context, still call original
    }
    CHECK(frame->returnAddress == reinterpret_cast<std::uintptr_t>(&RizFixture_ReturnSite));
    expected->entryRsp = frame->entryStackPointer;
    for (unsigned i=0; i<16; ++i) {
        if (i != RizRsp) CHECK(frame->gpr[i] == expected->registers.gpr[i]);
        CHECK(frame->xmm[i].low == expected->registers.xmm[i].low);
        CHECK(frame->xmm[i].high == expected->registers.xmm[i].high);
    }
    if (expected->mode & EnterSeh) {
        // No retained context has been published. An observer must clean up its own
        // partial allocation if it fails before returning a token.
        RaiseException(ObserverException, 0, 0, nullptr);
    }
    CHECK(contextDepth < 16);
    const auto token = static_cast<std::uintptr_t>(InterlockedIncrement64(&tokenSequence));
    contexts[contextDepth++] = {token, frame, *frame};
    return token;
}
void __fastcall Exit(const RizJudgeFrameV1* frame, std::uintptr_t token, std::uint32_t flags, void* user) {
    InterlockedIncrement64(&exitCalls);
    CHECK(user == &userObject);
    CHECK(expectedDepth > 0);
    const auto mode = expectedStack[expectedDepth-1]->mode;
    const bool abnormal = !!(mode & (OriginalSeh|OriginalCpp));
    CHECK(flags & (abnormal ? RizOriginalAbnormal : RizOriginalReturned));
    CHECK(!(flags & (abnormal ? RizOriginalReturned : RizOriginalAbnormal)));
    CHECK(!!(flags & RizEnterFaulted) == !!(mode & EnterSeh));
    if (flags & RizOriginalAbnormal) InterlockedIncrement64(&abnormalExits);
    if (token) {
        CHECK(contextDepth > 0);
        auto* retained = &contexts[contextDepth-1];
        CHECK(retained->token == token && retained->address == frame);
        CHECK(std::memcmp(&retained->copy, frame, sizeof(*frame)) == 0);
        *retained = {};
        --contextDepth; // cleanup precedes observer fault; bridge still completes finally
    } else {
        InterlockedIncrement64(&zeroTokenExits);
        CHECK(mode & (Unknown|EnterSeh));
    }
    if (mode & ExitSeh) RaiseException(ObserverException, 0, 0, nullptr);
}

__declspec(noinline) void __fastcall Original(void* receiver, std::int32_t kind, float delta, const void* method) {
    InterlockedIncrement64(&originalCalls);
    CHECK(expectedDepth > 0);
    const auto* expected = expectedStack[expectedDepth-1];
    CHECK(receiver == &receiverObject && method == &methodObject);
    CHECK(static_cast<std::uint32_t>(kind) == 0x80000005u);
    CHECK(FloatBits(delta) == static_cast<std::uint32_t>(expected->registers.xmm[2].low));
    if (expected->mode & Reentry) {
        CHECK(contextDepth == 1);
        const auto outer = contexts[0].token;
        Execute(Normal, 0xc0200000u);
        CHECK(contextDepth == 1 && contexts[0].token == outer);
    }
    if (expected->mode & Disable) CHECK(setEnabled(0) == 0);
    if (expected->mode & OriginalCpp) throw OriginalCppException{0x12345678abcdef01ull};
    if (expected->mode & OriginalSeh) {
        const ULONG_PTR args[2] = {0x123456789abcdef0ull, 0xfedcba9876543210ull};
        RaiseException(OriginalException, 0, 2, args);
    }
}

// Only POD locals: native __finally tears down the fixture's expected invocation
// after an original SEH exception crosses both assembly and DLL frames.
void Execute(unsigned mode, std::uint32_t bits) {
    Expected expected{};
    RegisterImage after{};
    Fill(&expected, mode, bits);
    CHECK(expectedDepth < 16);
    expectedStack[expectedDepth++] = &expected;
    __try {
        if (mode & Unknown) {
            float delta; std::memcpy(&delta, &bits, 4);
            reinterpret_cast<RizJudgeOriginalV1>(entry)(&receiverObject, static_cast<std::int32_t>(0x80000005u), delta, &methodObject);
        } else {
            RizFixture_Invoke(entry, &expected.registers, &after);
            // The helper intentionally also restores volatile registers; checking all
            // of them here is stronger than the Windows nonvolatile ABI requirement.
            for (unsigned i=0; i<16; ++i) {
                if (i != RizRsp) CHECK(after.gpr[i] == expected.registers.gpr[i]);
                CHECK(after.xmm[i].low == expected.registers.xmm[i].low);
                CHECK(after.xmm[i].high == expected.registers.xmm[i].high);
            }
            if (expected.entryRsp) CHECK(after.gpr[RizRsp] == expected.entryRsp+8);
            CHECK((after.gpr[RizRsp] & 15) == 0);
        }
    } __finally {
        expectedStack[--expectedDepth] = nullptr;
    }
}

int ExceptionFilter(EXCEPTION_POINTERS* exception) {
    CHECK(exception->ExceptionRecord->ExceptionCode == OriginalException);
    CHECK(exception->ExceptionRecord->NumberParameters == 2);
    CHECK(exception->ExceptionRecord->ExceptionInformation[0] == 0x123456789abcdef0ull);
    CHECK(exception->ExceptionRecord->ExceptionInformation[1] == 0xfedcba9876543210ull);
    auto context = *exception->ContextRecord;
    sawBridgeUnwind = sawCallerUnwind = false;
    for (unsigned i=0; i<96 && context.Rip && context.Rsp; ++i) {
        DWORD64 base = 0;
        const auto function = RtlLookupFunctionEntry(context.Rip, &base, nullptr);
        if (function) {
            const auto begin = base + function->BeginAddress;
            if (begin == reinterpret_cast<DWORD64>(bridgeEntry)) sawBridgeUnwind = true;
            if (begin == reinterpret_cast<DWORD64>(&RizFixture_Invoke)) sawCallerUnwind = true;
            PVOID handlerData = nullptr;
            DWORD64 establisherFrame = 0;
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, base, context.Rip, function, &context, &handlerData, &establisherFrame, nullptr);
        } else {
            context.Rip = *reinterpret_cast<const DWORD64*>(context.Rsp);
            context.Rsp += 8;
        }
    }
    CHECK(sawBridgeUnwind && sawCallerUnwind);
    return EXCEPTION_EXECUTE_HANDLER;
}
void ExecuteSeh(unsigned mode) {
    bool caught = false;
    __try { Execute(mode | OriginalSeh, 0x3e000000u); }
    __except (ExceptionFilter(GetExceptionInformation())) { caught = true; }
    CHECK(caught && sawBridgeUnwind && sawCallerUnwind);
    CHECK(contextDepth == 0 && expectedDepth == 0);
}
void ExecuteCpp() {
    bool caught=false;
    try { Execute(OriginalCpp, 0x3e000000u); }
    catch (const OriginalCppException& exception) {
        CHECK(exception.payload == 0x12345678abcdef01ull);
        caught=true;
    }
    CHECK(caught && contextDepth == 0 && expectedDepth == 0);
}
RizJudgeStatsV1 Stats() {
    RizJudgeStatsV1 result{}; result.abiVersion=1; result.byteSize=sizeof(result);
    CHECK(getStats(&result) == 0); return result;
}
void CheckConfiguration(const RizJudgeConfigV1& configured, std::uint32_t enabled) {
    RizJudgeConfigurationV1 result{}; result.abiVersion=1; result.byteSize=sizeof(result);
    CHECK(readConfiguration(&result) == 0);
    CHECK(result.abiVersion == 1);
    CHECK(result.byteSize == sizeof(result));
    CHECK(result.configured == 1);
    CHECK(result.enabled == enabled);
    CHECK(result.entry == bridgeEntry);
    CHECK(result.configuration.abiVersion == configured.abiVersion);
    CHECK(result.configuration.byteSize == configured.byteSize);
    CHECK(result.configuration.original == configured.original);
    CHECK(result.configuration.enter == configured.enter);
    CHECK(result.configuration.exit == configured.exit);
    CHECK(result.configuration.userContext == configured.userContext);
    CHECK(result.configuration.initiallyEnabled == configured.initiallyEnabled);
    CHECK(result.configuration.reserved == configured.reserved);
}
void Paired(unsigned mode, std::uint32_t bits=0x3e000000u) {
    const auto before=Stats();
    const auto calls=originalCalls;
    Execute(mode, bits);
    const auto count=(mode & Reentry) ? 2u : 1u;
    const auto after=Stats();
    CHECK(originalCalls == calls+count);
    CHECK(after.entered == before.entered+count && after.exited == before.exited+count);
    CHECK(after.inFlight == 0 && contextDepth == 0 && expectedDepth == 0);
}
DWORD WINAPI ThreadWorker(void*) {
    for (unsigned i=0; i<64; ++i) Execute(Normal, 0x3d000000u+i);
    CHECK(expectedDepth == 0 && contextDepth == 0);
    return 0;
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 2 && argc != 3) { std::fprintf(stderr,"Usage: JudgeContext.Fixture.exe absolute-helper-path [pinned-dobby-path]\n"); return 2; }
    library=LoadLibraryW(argv[1]);
    CHECK(library != nullptr);
    if (!library) return 2;
    configure=Symbol<ConfigureFn>("RizJudgeContext_Configure");
    setEnabled=Symbol<SetEnabledFn>("RizJudgeContext_SetEnabled");
    getStats=Symbol<GetStatsFn>("RizJudgeContext_GetStats");
    readConfiguration=Symbol<ReadConfigurationFn>("RizJudgeContext_ReadConfigurationV1");
    const auto getEntry=Symbol<GetEntryFn>("RizJudgeContext_GetEntry");
    const auto getFrameSize=Symbol<GetFrameSizeFn>("RizJudgeContext_GetFrameSize");
    if (!configure || !setEnabled || !getStats || !readConfiguration || !getEntry || !getFrameSize) return 2;
    CHECK(getFrameSize() == sizeof(RizJudgeFrameV1));
    entry=getEntry(); bridgeEntry=entry; CHECK(entry != nullptr);
    static_assert(sizeof(RizJudgeConfigurationV1) == 0x48);
    static_assert(offsetof(RizJudgeConfigurationV1, configuration) == 0x18);
    RizJudgeConfigurationV1 configuration{}; configuration.abiVersion=1; configuration.byteSize=sizeof(configuration);
    CHECK(readConfiguration(&configuration) == 2);
    CHECK(readConfiguration(nullptr) == 1);
    configuration.abiVersion=2; CHECK(readConfiguration(&configuration) == 1); configuration.abiVersion=1;
    configuration.byteSize=sizeof(configuration)-1; CHECK(readConfiguration(&configuration) == 1);
    CHECK(configure(nullptr) == 1 && setEnabled(1) == 1);
    RizJudgeConfigV1 config{1,sizeof(RizJudgeConfigV1),&Original,&Enter,&Exit,&userObject,1,0};
    using PrepareFn = int(__cdecl*)(void*,void*,void**);
    using CommitFn = int(__cdecl*)(void*);
    CommitFn commit=nullptr;
    unsigned char targetBefore[32]{};
    if (argc == 3) {
        const auto dobby=LoadLibraryW(argv[2]); CHECK(dobby != nullptr); if (!dobby) return 2;
        const auto prepare=reinterpret_cast<PrepareFn>(GetProcAddress(dobby,"DobbyPrepare"));
        commit=reinterpret_cast<CommitFn>(GetProcAddress(dobby,"DobbyCommit"));
        CHECK(prepare && commit); if (!prepare || !commit) return 2;
        std::memcpy(targetBefore,reinterpret_cast<const void*>(&Original),sizeof(targetBefore));
        void* trampoline=nullptr;
        CHECK(prepare(reinterpret_cast<void*>(&Original),bridgeEntry,&trampoline) == 0);
        CHECK(trampoline && trampoline != bridgeEntry && trampoline != reinterpret_cast<void*>(&Original));
        CHECK(std::memcmp(targetBefore,reinterpret_cast<const void*>(&Original),sizeof(targetBefore)) == 0);
        if (!trampoline) return 2;
        config.original=reinterpret_cast<RizJudgeOriginalV1>(trampoline);
    }
    config.abiVersion=2; CHECK(configure(&config) == 1); config.abiVersion=1;
    CHECK(configure(&config) == 0 && configure(&config) == 2);
    CheckConfiguration(config, 1); // includes the actual Dobby trampoline when prepared above
    CHECK(setEnabled(2) == 1 && getStats(nullptr) == 1);
    if (commit) {
        // Exercise the configured entry before the target has any native patch.
        Paired(Normal);
        CHECK(std::memcmp(targetBefore,reinterpret_cast<const void*>(&Original),sizeof(targetBefore)) == 0);
        // These workers call only the bridge/trampoline. None executes machine
        // bytes being copied at the target prologue: this is a publication and
        // lifetime test, not an assertion that Dobby's code patch is atomic.
        HANDLE workers[4]{};
        for (auto& worker:workers) { worker=CreateThread(nullptr,0,ThreadWorker,nullptr,0,nullptr); CHECK(worker != nullptr); }
        CHECK(commit(reinterpret_cast<void*>(&Original)) == 0);
        CHECK(std::memcmp(targetBefore,reinterpret_cast<const void*>(&Original),sizeof(targetBefore)) != 0);
        CHECK(WaitForMultipleObjects(4,workers,TRUE,30000) == WAIT_OBJECT_0);
        for (auto worker:workers) CHECK(CloseHandle(worker));
        CHECK(Stats().inFlight == 0);
        entry=reinterpret_cast<void*>(&Original); // all remaining tests enter the actual Dobby patch
    }
    DWORD64 base=0;
    CHECK(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(entry), &base, nullptr) != nullptr);
    CHECK(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(&RizFixture_Invoke), &base, nullptr) != nullptr);
    Paired(Normal);
    Paired(Normal, 0x7fc12345u); // qNaN payload must remain bit-exact
    Paired(Normal, 0x80000000u); // negative zero
    Paired(Unknown);
    Paired(Reentry);
    const auto callsBeforeSeh=originalCalls;
    ExecuteSeh(Normal);
    CHECK(originalCalls == callsBeforeSeh+1);
    const auto callsBeforeCpp=originalCalls;
    ExecuteCpp();
    CHECK(originalCalls == callsBeforeCpp+1);
    Paired(Disable);
    CheckConfiguration(config, 0); // live enabled changes; the original copied config does not
    const auto disabledBefore=Stats(); const auto disabledCalls=originalCalls;
    Execute(Normal, 0x3e000000u);
    const auto disabledAfter=Stats();
    CHECK(originalCalls == disabledCalls+1);
    CHECK(disabledAfter.entered == disabledBefore.entered && disabledAfter.exited == disabledBefore.exited);
    CHECK(disabledAfter.inFlight == 0);
    CHECK(setEnabled(1) == 0);
    CheckConfiguration(config, 1);
    Paired(EnterSeh);
    Paired(ExitSeh);
    ExecuteSeh(ExitSeh); // Exit failure must not replace the original SEH exception
    const auto parallelBefore=Stats(); const auto parallelOriginal=originalCalls;
    HANDLE workers[4]{};
    for (auto& worker : workers) { worker=CreateThread(nullptr,0,ThreadWorker,nullptr,0,nullptr); CHECK(worker != nullptr); }
    CHECK(WaitForMultipleObjects(4,workers,TRUE,30000) == WAIT_OBJECT_0);
    for (auto worker : workers) CHECK(CloseHandle(worker));
    const auto final=Stats();
    CHECK(originalCalls == parallelOriginal+256);
    CHECK(final.entered == parallelBefore.entered+256 && final.exited == parallelBefore.exited+256);
    CHECK(final.entered == final.exited && final.inFlight == 0);
    CHECK(final.enterFaults == 1 && final.exitFaults == 2);
    CHECK(abnormalExits == 3 && zeroTokenExits == 2);
    std::printf("{\"schemaVersion\":1,\"kind\":\"real-windows-x64-native-fixture\",\"passed\":%s,"
        "\"failures\":%lld,\"checks\":%lld,\"originalCalls\":%lld,\"observerEnters\":%lld,\"observerExits\":%lld,"
        "\"enterFaults\":%llu,\"exitFaults\":%llu,\"abnormalExits\":%lld,\"inFlight\":%llu,\"gameRuntimeVerified\":false,\"realDobbyPreparedCommit\":%s,"
        "\"cases\":[\"configuration-contract\",\"configuration-readback-unconfigured-and-invalid-rejection\","
        "\"configuration-readback-exact-original-observers-user-and-config\",\"configuration-readback-enabled-switch-preserves-config\","
        "\"full-gpr-xmm-return-address-stack-and-arguments\",\"nan-payload\",\"negative-zero\","
        "\"unknown-source-forward\",\"synchronous-reentry\",\"original-seh-virtual-unwind-finally\",\"original-cpp-exception-unchanged\",\"disable-pairs-inflight-exit\","
        "\"disabled-forward\",\"enter-fault-forward\",\"exit-fault-cleanup\",\"exit-fault-does-not-mask-original-seh\","
        "\"four-thread-isolation-256-calls\",\"unwind-metadata-present\"]}\n",
        failures ? "false":"true",failures,checks,originalCalls,enterCalls,exitCalls,
        static_cast<unsigned long long>(final.enterFaults),static_cast<unsigned long long>(final.exitFaults),abnormalExits,
        static_cast<unsigned long long>(final.inFlight),commit ? "true":"false");
    // Deliberately no FreeLibrary: host integration must also retain this DLL for process life.
    return failures ? 1:0;
}
