#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "../EngineNodeContext.h"
#include "EngineNodeManagedFixture.h"

// Executes only targets owned by EngineNodeManagedFixture.dll. The same DLL is
// used by the CLR joint runner; this executable adds native observer faults,
// deterministic late Attach, exact outer call PCs, nesting and worker calls.
namespace {
constexpr DWORD ObserverSeh = 0xe04e4f53;
constexpr std::uint64_t Payload = 0xfedcba9876543210ULL;
constexpr std::uintptr_t TokenMask = 0xa123456789abcdefULL;
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using EntryFn = decltype(&RizEngineNodeContext_GetEntry);
using ConfigureFn = decltype(&RizEngineNodeContext_Configure);
using AttachFn = decltype(&RizEngineNodeContext_Attach);
using InfoFn = decltype(&RizEngineNodeContext_GetInfo);
using TargetFn = decltype(&RizEngineNodeManaged_GetTarget);
using InvokeFn = decltype(&RizEngineNodeManaged_Invoke);
using CurrentFn = decltype(&RizEngineNodeManaged_GetCurrent);
struct Owner { unsigned kind{}; volatile LONG active = 1; } owners[4];
struct Caller { unsigned char* code{}; const void* returnPc{}; PRUNTIME_FUNCTION unwind{}; } callers[4];
HMODULE helper{}, fixtureModule{};
EntryFn getEntry{}; ConfigureFn configure{}; AttachFn attach{}; InfoFn getInfo{};
TargetFn getTarget{}; InvokeFn invoke{}; CurrentFn getCurrent{};
void* originals[4]{}; std::uint64_t epochs[4]{};
volatile LONG64 checks{}, failures{}, bodyCalls{}, entered{}, exited{}, uniqueSequences{}, unobservedCalls{};
volatile LONG64 originalExceptions{}, observerExceptions{}, workerCalls{}, nestedCalls{}, detachedCases{}, zeroTokens{};
volatile LONG64 snapshotFaults{}, outputFaults{}, freeNodeCases{}, rawHighBytes{}, lateAttachCases{};
volatile LONG64 executeFullWidthCases{}, executeFullWidthCompletions{};
SRWLOCK sequenceLock = SRWLOCK_INIT;
std::uint64_t sequences[512]{}; std::size_t sequenceCount{};
struct Observation {
    const RizEngineNodeManagedResultV1* live{};
    RizEngineNodeFrameV1 frame{};
    std::uintptr_t token{};
    DWORD thread{};
    bool captured{};
    Observation* previous{};
};
struct TestCase {
    unsigned kind{}, mode{}, scenario{}, requestedByte = 0x81, enterFault{}, exitFault{};
    bool unobserved{}, zeroToken{}, logicalDetach{}, block{};
    HANDLE reached{}, resume{};
    TestCase* nested{};
    unsigned enters{}, exits{}, captures{}, completions{}, bodies{}, observedCount{};
    Observation observations[8]{};
    RizEngineNodeManagedResultV1 result{};
};
thread_local TestCase* currentCase{};
thread_local Observation* currentObservation{};
struct ObserverCpp { std::uint64_t payload; };
void Check(bool value, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!value) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED EngineNode line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto address = GetProcAddress(module, name); CHECK(address != nullptr);
    T value{}; static_assert(sizeof(value) == sizeof(address)); std::memcpy(&value, &address, sizeof(value)); return value;
}
bool PinnedDobby(const wchar_t* path, HANDLE* retained)
{
    *retained = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (*retained == INVALID_HANDLE_VALUE) return false;
    BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
    unsigned char object[1024]{}, digest[32]{}, buffer[4096]{}; DWORD objectSize{}, resultSize{}, read{};
    bool okay = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0;
    if (okay) okay = BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize),
        sizeof(objectSize), &resultSize, 0) >= 0 && objectSize <= sizeof(object);
    if (okay) okay = BCryptCreateHash(algorithm, &hash, object, objectSize, nullptr, 0, 0) >= 0;
    while (okay) {
        okay = ReadFile(*retained, buffer, sizeof(buffer), &read, nullptr) != FALSE;
        if (!okay || !read) break;
        okay = BCryptHashData(hash, buffer, read, 0) >= 0;
    }
    if (okay) okay = BCryptFinishHash(hash, digest, sizeof(digest), 0) >= 0;
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    constexpr unsigned char expected[32]{ 0x80,0x15,0xde,0x7d,0x86,0x72,0x45,0xa1,0x09,0x5d,0x13,0x94,0x7a,0x63,0x76,0x38,
        0x78,0xe4,0xcf,0x5f,0xd3,0xd3,0x08,0x9b,0x63,0xcc,0x39,0x20,0x0b,0x05,0x5d,0xed };
    return okay && std::memcmp(digest, expected, sizeof(digest)) == 0;
}
Caller MakeCaller(void* target)
{
    auto* block = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    CHECK(block); if (!block) ExitProcess(2);
    const unsigned char code[]{ 0x48,0x83,0xec,0x28,0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xd0,0x48,0x83,0xc4,0x28,0xc3 };
    const unsigned char unwind[]{ 1,4,1,0,4,0x42,0,0 };
    std::memcpy(block, code, sizeof(code)); std::memcpy(block + 6, &target, sizeof(target));
    std::memcpy(block + 32, unwind, sizeof(unwind));
    auto* table = reinterpret_cast<PRUNTIME_FUNCTION>(block + 64); *table = { 0, static_cast<DWORD>(sizeof(code)), 32 };
    DWORD old{}; CHECK(VirtualProtect(block, 4096, PAGE_EXECUTE_READ, &old));
    CHECK(FlushInstructionCache(GetCurrentProcess(), block, 4096));
    CHECK(RtlAddFunctionTable(table, 1, reinterpret_cast<DWORD64>(block)));
    DWORD64 imageBase{};
    CHECK(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(block + 16), &imageBase, nullptr) == table
        && imageBase == reinterpret_cast<DWORD64>(block));
    return { block, block + 16, table };
}
HMODULE ModuleOf(const void* address)
{
    HMODULE result{};
    CHECK(address && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(address), &result));
    return result;
}
void ObserverFault(unsigned fault)
{
    if (!fault) return;
    InterlockedIncrement64(&observerExceptions);
    if (fault == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
    else throw ObserverCpp{ Payload };
}
std::uintptr_t __cdecl Enter(const RizEngineNodeFrameV1* frame, void* user)
{
    auto* test = currentCase; const auto* live = getCurrent();
    CHECK(test && live && !test->unobserved); if (!test || !live) return 0;
    CHECK(test->observedCount < 8); if (test->observedCount >= 8) return 0;
    auto* observation = &test->observations[test->observedCount++];
    observation->live = live; observation->frame = *frame; observation->thread = GetCurrentThreadId();
    observation->previous = currentObservation; currentObservation = observation;
    CHECK(user == &owners[live->kind] && frame->kind == live->kind && frame->abiVersion == 1 && frame->byteSize == 112);
    CHECK(frame->arg0 == live->arg0 && frame->arg1 == live->arg1 && frame->arg2 == live->arg2 && frame->arg3 == live->arg3);
    CHECK(frame->reserved0 == 0 && frame->reserved1 == 0 && frame->sequence != 0);
    // An outer call has the exact dynamic CALL return PC. Synchronous nested
    // calls made by the DLL use its compiled call site, checked by module only.
    if (test->enters == 0) CHECK(frame->caller == callers[live->kind].returnPc);
    else CHECK(ModuleOf(frame->caller) == fixtureModule);
    if (live->kind == 0) {
        CHECK(frame->snapshotFlags == 0 && !frame->node && !frame->owner && !frame->enumHandle && !frame->enumCache
            && !frame->enumType && !frame->referenceCount);
    } else if (live->scenario & RizEngineNodeManagedUnreadableNode) {
        CHECK(frame->snapshotFlags == 5 && frame->node == live->node && !frame->owner && !frame->enumHandle
            && !frame->enumCache && !frame->enumType && !frame->referenceCount);
        InterlockedIncrement64(&snapshotFaults);
    } else {
        CHECK(frame->snapshotFlags == 3 && frame->node == live->node && frame->owner == live->owner
            && frame->enumHandle == live->enumHandle && frame->enumCache == live->enumCache
            && frame->enumType == live->enumType && frame->referenceCount == live->referenceCount);
    }
    AcquireSRWLockExclusive(&sequenceLock);
    bool unique = sequenceCount < sizeof(sequences) / sizeof(sequences[0]);
    for (std::size_t i = 0; i != sequenceCount; ++i) if (sequences[i] == frame->sequence) unique = false;
    if (unique) sequences[sequenceCount++] = frame->sequence;
    ReleaseSRWLockExclusive(&sequenceLock);
    CHECK(unique); InterlockedIncrement64(&uniqueSequences);
    observation->captured = InterlockedCompareExchange(&owners[live->kind].active, 0, 0) != 0;
    observation->token = test->zeroToken || !observation->captured ? 0 : reinterpret_cast<std::uintptr_t>(live) ^ TokenMask;
    ++test->enters; if (observation->captured) ++test->captures;
    InterlockedIncrement64(&entered); ObserverFault(test->enterFault); return observation->token;
}
void __cdecl Exit(const RizEngineNodeFrameV1* frame, std::uintptr_t token, const RizEngineNodeCompletionV1* completion, void* user)
{
    auto* test = currentCase; const auto* live = getCurrent(); auto* observation = currentObservation;
    CHECK(test && live && observation); if (!test || !live || !observation) return;
    CHECK(observation->live == live && user == &owners[live->kind] && GetCurrentThreadId() == observation->thread);
    CHECK(std::memcmp(&observation->frame, frame, sizeof(*frame)) == 0);
    CHECK(token == (test->enterFault ? 0 : observation->token));
    const auto expectedFlags = (live->mode ? RizEngineNodeAbnormal : RizEngineNodeReturned)
        + (test->enterFault ? RizEngineNodeEnterFaulted : 0u) + (!live->mode && live->kind != 3 ? RizEngineNodeReturnByteValid : 0u);
    CHECK(completion->abiVersion == 1 && completion->byteSize == 32 && !completion->reserved && completion->flags == expectedFlags);
    const auto expectedByte = test->observedCount > 1 && live != test->observations[0].live
        ? live->kind == 2 ? 0u : 0xa5c3fe81u : test->requestedByte;
    CHECK(completion->returnByte == (!live->mode && live->kind != 3 ? expectedByte : 0u));
    if (!live->mode && live->kind == 1 && expectedByte > 255) InterlockedIncrement64(&executeFullWidthCompletions);
    if (live->kind == 0 && !live->mode) {
        if (live->scenario & (RizEngineNodeManagedProtectOutput | RizEngineNodeManagedNullOutputSlot)) {
            CHECK(completion->outputFlags == 5 && !completion->startOutputNode); InterlockedIncrement64(&outputFaults);
        } else CHECK(completion->outputFlags == 3 && completion->startOutputNode == live->outputNode);
    } else CHECK(!completion->outputFlags && !completion->startOutputNode);
    // Only immutable Before scalars are checked above. There is deliberately
    // no node read here, including after original VirtualFree or nested release.
    ++test->exits; if (observation->captured) ++test->completions;
    currentObservation = observation->previous; InterlockedIncrement64(&exited); ObserverFault(test->exitFault);
}
void Run(TestCase* test);
void __cdecl Body(const RizEngineNodeManagedResultV1* result, void* user)
{
    CHECK(user == owners && currentCase && result == getCurrent());
    if (!currentCase) return;
    auto* test = currentCase; ++test->bodies; InterlockedIncrement64(&bodyCalls);
    CHECK(ModuleOf(result->originalCaller) == helper);
    CHECK(test->unobserved ? currentObservation == nullptr : currentObservation && currentObservation->live == result);
    if (test->logicalDetach) InterlockedExchange(&owners[result->kind].active, 0);
    if (test->nested) {
        auto* nested = test->nested; test->nested = nullptr;
        auto* parentObservation = currentObservation; Run(nested);
        CHECK(currentCase == test && currentObservation == parentObservation); InterlockedIncrement64(&nestedCalls);
    }
    if (test->block) {
        CHECK(test->unobserved && !currentObservation && test->reached && test->resume);
        CHECK(SetEvent(test->reached)); const auto waited = WaitForSingleObject(test->resume, 10000);
        CHECK(waited == WAIT_OBJECT_0); if (waited != WAIT_OBJECT_0) ExitProcess(3);
        CHECK(!test->enters && !test->exits && !currentObservation);
    }
}
void Run(TestCase* test)
{
    auto* parent = currentCase; auto* previous = currentObservation; currentCase = test;
    test->result.abiVersion = 1; test->result.byteSize = sizeof(test->result);
    CHECK(invoke(callers[test->kind].code, test->kind, test->mode, test->scenario, test->requestedByte, &test->result) == 0);
    CHECK(test->result.nativeChecks > 0 && test->result.nativeFailures == 0 && test->result.originalCalls == 1);
    CHECK(test->result.returned == (test->mode ? 0u : 1u) && test->result.caught == (test->mode ? 1u : 0u));
    CHECK(currentObservation == previous && test->enters == test->exits && test->captures == test->completions);
    CHECK(test->bodies == 1 + test->result.nestedCalls);
    CHECK(test->unobserved ? test->enters == 0 : test->enters == test->bodies);
    if (test->unobserved) InterlockedAdd64(&unobservedCalls, test->bodies);
    if (test->mode) { CHECK(test->result.exceptionAddress && test->result.exceptionParameterCount); InterlockedIncrement64(&originalExceptions); }
    if (test->zeroToken) InterlockedIncrement64(&zeroTokens);
    if (test->result.nodeFreed) InterlockedIncrement64(&freeNodeCases);
    if (!test->mode && test->kind != 3 && test->requestedByte >= 0x80) InterlockedIncrement64(&rawHighBytes);
    if (!test->mode && test->kind == 1 && test->requestedByte > 255) {
        CHECK(test->result.returnByte == test->requestedByte);
        InterlockedIncrement64(&executeFullWidthCases);
    }
    currentCase = parent;
}
DWORD WINAPI Worker(void* data) { Run(static_cast<TestCase*>(data)); InterlockedIncrement64(&workerCalls); return 0; }
void Join(HANDLE thread)
{
    CHECK(thread); if (!thread) ExitProcess(2);
    const auto waited = WaitForSingleObject(thread, 10000); CHECK(waited == WAIT_OBJECT_0);
    if (waited != WAIT_OBJECT_0) ExitProcess(3);
    CloseHandle(thread);
}
void AttachAcrossOldCall(unsigned kind)
{
    TestCase old{}; old.kind = kind; old.unobserved = true; old.block = true;
    old.reached = CreateEventW(nullptr, TRUE, FALSE, nullptr); old.resume = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    CHECK(old.reached && old.resume);
    const auto thread = CreateThread(nullptr, 0, Worker, &old, 0, nullptr); CHECK(thread);
    const auto waited = WaitForSingleObject(old.reached, 10000); CHECK(waited == WAIT_OBJECT_0);
    if (waited != WAIT_OBJECT_0) ExitProcess(3);
    RizEngineNodeObserverV1 observer{ 1, sizeof(observer), Enter, Exit, &owners[kind] };
    CHECK(attach(kind, 0, epochs[kind] + 1, &observer) == 3);
    CHECK(attach(kind, 0, epochs[kind], &observer) == 0);
    CHECK(attach(kind, 0, epochs[kind], &observer) == 4);
    RizEngineNodeInfoV1 information{}; information.abiVersion = 1; information.byteSize = sizeof(information);
    CHECK(getInfo(kind, 0, &information) == 0 && information.attached == 1 && information.inFlight == 1 && !information.unknown);
    CHECK(SetEvent(old.resume)); Join(thread);
    CHECK(!old.enters && !old.exits && old.bodies == 1); InterlockedIncrement64(&lateAttachCases);
    CloseHandle(old.reached); CloseHandle(old.resume);
}
int Result()
{
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-engine-node-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,"
        "\"actualOriginalCalls\":%lld,\"entered\":%lld,\"exited\":%lld,\"uniqueSequences\":%lld,\"unobservedCalls\":%lld,"
        "\"originalExceptionCases\":%lld,\"observerExceptionCases\":%lld,\"nativeWorkerCalls\":%lld,\"explicitNestedCalls\":%lld,"
        "\"lateAttachCases\":%lld,\"logicalDetachCases\":%lld,\"legitimateZeroTokens\":%lld,\"snapshotFaultCases\":%lld,"
        "\"outputFaultCases\":%lld,\"originalFreeNodeCases\":%lld,\"rawHighByteCases\":%lld,"
        "\"executeFullWidthCases\":%lld,\"executeFullWidthCompletions\":%lld,"
        "\"kinds\":[0,1,2,3],\"frameSize\":112,\"completionSize\":32,\"transportAbiVersion\":2,\"realDobbyPreparedCommit\":true,\"preparedTargets\":4,\"committedTargets\":4,"
        "\"pinnedDobbyVerified\":true,\"exactOuterSourceCallerVerified\":true,\"nestedSourceCallerModuleVerified\":true,\"oldCallsNeverBorrowObserver\":true,"
        "\"deadNodeAfterReadAvoided\":true,\"rawAlByteVerified\":true,\"executeReturnBits\":32,\"rawExecuteEaxVerified\":true,\"fullRaxPreservationVerified\":false,\"exceptionFlagsTransparencyVerified\":false,"
        "\"engineCancellationCompleted\":false,\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,\"installed\":false,"
        "\"firstInstallSafetyVerified\":false,\"concurrentFirstPublicationVerified\":false,\"partialPublicationFailureInjected\":false}\n",
        failures ? "false" : "true", static_cast<long long>(checks), static_cast<long long>(failures),
        static_cast<long long>(bodyCalls), static_cast<long long>(entered), static_cast<long long>(exited), static_cast<long long>(uniqueSequences),
        static_cast<long long>(unobservedCalls), static_cast<long long>(originalExceptions), static_cast<long long>(observerExceptions),
        static_cast<long long>(workerCalls), static_cast<long long>(nestedCalls), static_cast<long long>(lateAttachCases),
        static_cast<long long>(detachedCases), static_cast<long long>(zeroTokens), static_cast<long long>(snapshotFaults), static_cast<long long>(outputFaults),
        static_cast<long long>(freeNodeCases), static_cast<long long>(rawHighBytes),
        static_cast<long long>(executeFullWidthCases), static_cast<long long>(executeFullWidthCompletions));
    return failures ? 1 : 0;
}
}
int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    CHECK(argc == 4); if (argc != 4) return Result();
    HANDLE retained{}; CHECK(PinnedDobby(argv[2], &retained)); if (failures) return Result();
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr && GetModuleHandleW(L"UnityPlayer.dll") == nullptr);
    helper = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    const auto dobby = LoadLibraryExW(argv[2], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    fixtureModule = LoadLibraryExW(argv[3], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    CHECK(helper && dobby && fixtureModule); if (!helper || !dobby || !fixtureModule) return Result();
    getEntry = Symbol<EntryFn>(helper, "RizEngineNodeContext_GetEntry");
    configure = Symbol<ConfigureFn>(helper, "RizEngineNodeContext_Configure");
    attach = Symbol<AttachFn>(helper, "RizEngineNodeContext_Attach");
    getInfo = Symbol<InfoFn>(helper, "RizEngineNodeContext_GetInfo");
    getTarget = Symbol<TargetFn>(fixtureModule, "RizEngineNodeManaged_GetTarget");
    invoke = Symbol<InvokeFn>(fixtureModule, "RizEngineNodeManaged_Invoke");
    getCurrent = Symbol<CurrentFn>(fixtureModule, "RizEngineNodeManaged_GetCurrent");
    const auto setBody = Symbol<decltype(&RizEngineNodeManaged_SetBodyObserver)>(fixtureModule, "RizEngineNodeManaged_SetBodyObserver");
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    const auto limit = Symbol<decltype(&RizEngineNodeContext_GetLimit)>(helper, "RizEngineNodeContext_GetLimit");
    const auto abiVersion = Symbol<decltype(&RizEngineNodeContext_GetAbiVersion)>(helper, "RizEngineNodeContext_GetAbiVersion");
    const auto frameSize = Symbol<decltype(&RizEngineNodeContext_GetFrameSize)>(helper, "RizEngineNodeContext_GetFrameSize");
    const auto completionSize = Symbol<decltype(&RizEngineNodeContext_GetCompletionSize)>(helper, "RizEngineNodeContext_GetCompletionSize");
    if (failures) return Result();
    CHECK(abiVersion() == 2 && limit() == 32 && frameSize() == 112 && completionSize() == 32 && !getEntry(4, 0) && !getEntry(0, 32) && !getTarget(4));
    CHECK(setBody(Body, owners) == 0 && setBody(Body, owners) == 2);
    void* rejected{}; CHECK(prepare(nullptr, getEntry(0, 0), &rejected) != 0 && !rejected);
    for (unsigned kind = 0; kind != 4; ++kind) {
        owners[kind].kind = kind; auto* target = getTarget(kind); auto* entry = getEntry(kind, 0); CHECK(target && entry);
        callers[kind] = MakeCaller(target);
        unsigned char before[32]{}, published[32]{}; std::memcpy(before, target, sizeof(before));
        CHECK(prepare(target, entry, &originals[kind]) == 0 && originals[kind] && originals[kind] != target && originals[kind] != entry);
        CHECK(std::memcmp(before, target, sizeof(before)) == 0);
        RizEngineNodeConfigV1 config{ 1, sizeof(config), target, originals[kind] };
        CHECK(configure(kind, 0, &config) == 0 && configure(kind, 0, &config) == 2);
        CHECK(std::memcmp(before, target, sizeof(before)) == 0 && commit(target) == 0);
        CHECK(std::memcmp(before, target, sizeof(before)) != 0); std::memcpy(published, target, sizeof(published));
        CHECK(commit(target) != 0 && std::memcmp(published, target, sizeof(published)) == 0);
        RizEngineNodeInfoV1 information{}; information.abiVersion = 1; information.byteSize = sizeof(information);
        CHECK(getInfo(kind, 0, &information) == 0 && information.kind == kind && !information.slot && !information.attached && !information.unknown
            && information.target == target && information.original == originals[kind] && information.entry == entry && information.epoch != 0);
        epochs[kind] = information.epoch;
        for (unsigned index = 0; index != 2; ++index) { TestCase beforeAttach{}; beforeAttach.kind = kind; beforeAttach.unobserved = true; Run(&beforeAttach); }
        AttachAcrossOldCall(kind);
    }
    for (unsigned kind = 0; kind != 4; ++kind) {
        for (const auto byte : { 0u, 1u, 0x7fu, 0x80u, 0xffu }) { TestCase test{}; test.kind = kind; test.requestedByte = byte; Run(&test); }
        for (unsigned fault = 1; fault != 3; ++fault) {
            TestCase original{}; original.kind = kind; original.mode = fault; Run(&original);
            TestCase before{}; before.kind = kind; before.enterFault = fault; Run(&before);
            TestCase after{}; after.kind = kind; after.exitFault = fault; Run(&after);
            TestCase combined{}; combined.kind = kind; combined.mode = fault; combined.enterFault = 3-fault; combined.exitFault = fault; Run(&combined);
            TestCase recovery{}; recovery.kind = kind; Run(&recovery);
        }
        TestCase same{}; same.kind = kind; TestCase outer{}; outer.kind = kind; outer.nested = &same; Run(&outer);
        TestCase cross{}; cross.kind = (kind+1)%4; TestCase parent{}; parent.kind = kind; parent.nested = &cross; Run(&parent);
        TestCase detach{}; detach.kind = kind; detach.logicalDetach = true; Run(&detach);
        TestCase inactive{}; inactive.kind = kind; Run(&inactive);
        CHECK(detach.captures == 1 && detach.completions == 1 && inactive.captures == 0 && inactive.completions == 0);
        InterlockedExchange(&owners[kind].active, 1); InterlockedIncrement64(&detachedCases);
        TestCase zero{}; zero.kind = kind; zero.zeroToken = true; Run(&zero);
    }
    TestCase workers[12]{}; HANDLE threads[12]{};
    for (unsigned index = 0; index != 12; ++index) { workers[index].kind = index%4; threads[index] = CreateThread(nullptr, 0, Worker, &workers[index], 0, nullptr); }
    for (const auto thread : threads) Join(thread);
    // High 24 bits are deliberate poison, including AL=0 and AL=1. Returning
    // uint8 or normalizing bool would pass old low-byte tests but fail these
    // actual Dobby -> helper -> original -> caller EAX comparisons.
    for (const auto value : { 0x12345680u, 0xabcdef01u, 0xffffff00u, 0xffffffffu }) {
        TestCase test{}; test.kind = 1; test.requestedByte = value; Run(&test);
    }
    TestCase eaxBefore{}; eaxBefore.kind = 1; eaxBefore.requestedByte = 0x34567801u; eaxBefore.enterFault = 1; Run(&eaxBefore);
    TestCase eaxAfter{}; eaxAfter.kind = 1; eaxAfter.requestedByte = 0x45678900u; eaxAfter.exitFault = 2; Run(&eaxAfter);
    TestCase eaxFree{}; eaxFree.kind = 1; eaxFree.requestedByte = 0x56789affu; eaxFree.scenario = RizEngineNodeManagedFreeNode; Run(&eaxFree);
    TestCase eaxWorker{}; eaxWorker.kind = 1; eaxWorker.requestedByte = 0x6789ab80u;
    Join(CreateThread(nullptr, 0, Worker, &eaxWorker, 0, nullptr));
    for (const auto flags : { 32u, 2u, 64u, 4u, 8u, 12u }) { TestCase test{}; test.scenario = flags; Run(&test); }
    for (unsigned kind = 1; kind != 4; ++kind) { TestCase test{}; test.kind = kind; test.scenario = RizEngineNodeManagedFreeNode; Run(&test); }
    for (unsigned kind = 1; kind != 4; ++kind) { TestCase test{}; test.kind = kind; test.scenario = RizEngineNodeManagedUnreadableNode; Run(&test); }
    std::uint64_t nativeEntered{}, nativeExited{};
    for (unsigned kind = 0; kind != 4; ++kind) {
        RizEngineNodeInfoV1 information{}; information.abiVersion = 1; information.byteSize = sizeof(information);
        CHECK(getInfo(kind, 0, &information) == 0 && information.attached && information.unknown && !information.inFlight
            && information.entered == information.exited && information.enterFaults == (kind == 1 ? 5u : 4u)
            && information.exitFaults == (kind == 1 ? 5u : 4u)
            && information.snapshotFaults == (kind ? 1u : 0u) && information.outputFaults == (kind ? 0u : 2u));
        nativeEntered += information.entered; nativeExited += information.exited;
    }
    CHECK(nativeEntered == static_cast<std::uint64_t>(bodyCalls) && nativeExited == nativeEntered);
    CHECK(bodyCalls == 136 && entered == 124 && exited == entered && uniqueSequences == entered && unobservedCalls == 12
        && originalExceptions == 16 && observerExceptions == 34 && workerCalls == 17 && nestedCalls == 8
        && lateAttachCases == 4 && detachedCases == 4 && zeroTokens == 4 && snapshotFaults == 3 && outputFaults == 2 && freeNodeCases == 6
        && executeFullWidthCases == 8 && executeFullWidthCompletions == 10);
    CHECK(!currentCase && !currentObservation && GetModuleHandleW(L"GameAssembly.dll") == nullptr && GetModuleHandleW(L"UnityPlayer.dll") == nullptr);
    // Modules, prepared trampolines, callback descriptors, dynamic unwind tables
    // and the pinned Dobby file handle intentionally survive until process exit.
    return Result();
}
