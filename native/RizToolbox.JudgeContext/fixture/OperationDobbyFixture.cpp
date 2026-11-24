#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <bcrypt.h>
#include <intrin.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../OperationContext.h"

// This executable owns every patched target. It deliberately does not expose a
// production Ready receipt, load GameAssembly, or model concurrent first writes.
namespace {
constexpr DWORD OriginalSeh = 0xe04f4453, ObserverSeh = 0xe04f4443;
constexpr std::uint64_t Payload = 0xfedcba9876543210ULL;
constexpr std::uintptr_t TokenMask = 0x9123456789abcdefULL;
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using EntryFn = decltype(&RizOperationContext_GetEntry);
using ConfigureFn = decltype(&RizOperationContext_Configure);
using StatsFn = decltype(&RizOperationContext_GetStats);
struct Owner { unsigned kind{}; volatile LONG active = 1; };
Owner owners[4]{};
EntryFn getEntry{}; ConfigureFn configure{}; StatsFn getStats{};
HMODULE helper{}; void* originals[4]{};
struct Caller { unsigned char* code{}; const void* returnPc{}; PRUNTIME_FUNCTION function{}; };
Caller callers[4]{};
volatile LONG64 checks{}, failures{}, originalCalls{}, nativeEnters{}, nativeExits{}, uniqueSequences{};
volatile LONG64 originalExceptions{}, observerExceptions{}, workerCalls{}, nestedCalls{}, detachedCases{}, zeroTokens{};
volatile LONG64 prepared{}, committed{}, commitRejections{}, configureRejections{}, retainedPrepared{};
SRWLOCK sequenceLock = SRWLOCK_INIT;
std::uint64_t seen[512]{}; std::size_t seenCount{};
struct Work {
    unsigned kind{}, originalFault{}, enterFault{}, exitFault{};
    bool nullArguments{}, zeroToken{}, detach{}, returned{}, caught{}, captured{}, unobserved{};
    unsigned invoked{}, entered{}, exited{}, deliveries{}, completions{};
    DWORD thread{}, exceptionCode{}, exceptionFlags{}, parameterCount{};
    ULONG_PTR information[4]{};
    const void* exceptionAddress{}; const void* originalCaller{};
    std::uint64_t argument0 = 0x1111222233334444ULL, argument1 = 0x5555666677778888ULL;
    std::uint32_t scalarBits = 0x7fc12345u;
    std::uintptr_t token{}; std::uint32_t flags{};
    RizOperationFrameV1 frame{};
    Work* previous{}; Work* nested{};
};
thread_local Work* current{};
struct OriginalCpp { std::uint64_t payload; Work* work; unsigned kind; };
struct ObserverCpp { std::uint64_t payload; };
void Check(bool value, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!value) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED OperationDobby line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto symbol = GetProcAddress(module, name); CHECK(symbol != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(symbol)); std::memcpy(&result, &symbol, sizeof(result)); return result;
}
bool PinnedDobby(const wchar_t* path, HANDLE* retainedFile)
{
    *retainedFile = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (*retainedFile == INVALID_HANDLE_VALUE) return false;
    BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
    unsigned char object[1024]{}, digest[32]{}, buffer[4096]{};
    DWORD objectSize{}, resultSize{}, read{};
    bool okay = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0;
    if (okay) okay = BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize),
        sizeof(objectSize), &resultSize, 0) >= 0 && objectSize <= sizeof(object);
    if (okay) okay = BCryptCreateHash(algorithm, &hash, object, objectSize, nullptr, 0, 0) >= 0;
    while (okay) {
        okay = ReadFile(*retainedFile, buffer, sizeof(buffer), &read, nullptr) != FALSE;
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
void* Arg0(Work* work)
{
    if (work->nullArguments) return nullptr;
    if (work->kind == 3) return reinterpret_cast<void*>(static_cast<std::uintptr_t>(work->scalarBits));
    return work->kind > 0 ? &work->argument0 : nullptr;
}
void* Arg1(Work* work) { return work->kind == 2 && !work->nullArguments ? &work->argument1 : nullptr; }
const void* Method(Work* work) { return &work->originalFault; }
void ObserverFault(unsigned fault)
{
    if (!fault) return;
    InterlockedIncrement64(&observerExceptions);
    if (fault == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
    else throw ObserverCpp{ Payload };
}
std::uintptr_t __cdecl Enter(const RizOperationFrameV1* frame, void* user)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(user == &owners[work->kind]);
    CHECK(frame->abiVersion == 1 && frame->byteSize == 64 && frame->kind == work->kind && frame->reserved == 0);
    const bool exactArguments = frame->self == work && frame->arg0 == Arg0(work)
        && frame->arg1 == Arg1(work) && frame->method == Method(work);
    if (!exactArguments) std::fprintf(stderr,
        "enter argument mismatch kind=%u self=%p/%p arg0=%p/%p arg1=%p/%p method=%p/%p null=%u\n",
        work->kind, frame->self, work, frame->arg0, Arg0(work), frame->arg1, Arg1(work),
        frame->method, Method(work), work->nullArguments ? 1u : 0u);
    CHECK(exactArguments);
    CHECK(frame->caller == callers[work->kind].returnPc && frame->sequence != 0);
    AcquireSRWLockExclusive(&sequenceLock);
    bool unique = seenCount < sizeof(seen) / sizeof(seen[0]);
    for (std::size_t i = 0; i != seenCount; ++i) if (seen[i] == frame->sequence) unique = false;
    if (unique) seen[seenCount++] = frame->sequence;
    ReleaseSRWLockExclusive(&sequenceLock);
    CHECK(unique); InterlockedIncrement64(&uniqueSequences);
    work->frame = *frame; work->thread = GetCurrentThreadId(); ++work->entered;
    work->previous = current; current = work;
    work->captured = InterlockedCompareExchange(&owners[work->kind].active, 0, 0) != 0;
    if (work->captured) ++work->deliveries;
    work->token = work->zeroToken || !work->captured ? 0 : reinterpret_cast<std::uintptr_t>(work) ^ TokenMask;
    InterlockedIncrement64(&nativeEnters); ObserverFault(work->enterFault); return work->token;
}
void __cdecl Exit(const RizOperationFrameV1* frame, std::uintptr_t token, std::uint32_t flags, void* user)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(current == work && user == &owners[work->kind] && GetCurrentThreadId() == work->thread);
    CHECK(std::memcmp(&work->frame, frame, sizeof(*frame)) == 0);
    CHECK(token == (work->enterFault ? 0 : work->token));
    CHECK(flags == (work->originalFault ? RizOperationAbnormal : RizOperationReturned)
        + (work->enterFault ? RizOperationEnterFaulted : 0u));
    ++work->exited; work->flags = flags;
    if (work->captured) ++work->completions;
    current = work->previous; InterlockedIncrement64(&nativeExits); ObserverFault(work->exitFault);
}
void Run(Work* work);
void Body(Work* work, unsigned kind, void* arg0, void* arg1, const void* method, const void* caller)
{
    const bool exactArguments = work->kind == kind && arg0 == Arg0(work) && arg1 == Arg1(work) && method == Method(work);
    if (!exactArguments) std::fprintf(stderr,
        "original argument mismatch kind=%u/%u arg0=%p/%p arg1=%p/%p method=%p/%p null=%u\n",
        work->kind, kind, arg0, Arg0(work), arg1, Arg1(work), method, Method(work), work->nullArguments ? 1u : 0u);
    CHECK(exactArguments);
    CHECK(work->unobserved ? current == nullptr : current == work && work->entered == 1 && work->exited == 0);
    work->originalCaller = caller;
    if (!work->unobserved) {
        HMODULE module{};
        CHECK(caller && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(caller), &module) && module == helper);
        InterlockedIncrement64(&originalCalls);
    }
    ++work->invoked;
    if (work->detach) InterlockedExchange(&owners[kind].active, 0);
    if (work->nested) { Run(work->nested); CHECK(current == work); InterlockedIncrement64(&nestedCalls); }
    if (work->originalFault == 1) {
        const ULONG_PTR payload[]{ static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(work), work->kind };
        RaiseException(OriginalSeh, 0, 3, payload);
    } else if (work->originalFault == 2) throw OriginalCpp{ Payload, work, work->kind };
}
// Keep substantial, separate, real compiler-generated prologues for Dobby.
// Call sites use dynamic caller stubs so the optimizer cannot bypass detours.
#pragma optimize("", off)
__declspec(noinline) void __cdecl Target0(void* self, const void* method)
{ Body(static_cast<Work*>(self), 0, nullptr, nullptr, method, _ReturnAddress()); }
__declspec(noinline) void __cdecl Target1(void* self, void* arg0, const void* method)
{ Body(static_cast<Work*>(self), 1, arg0, nullptr, method, _ReturnAddress()); }
__declspec(noinline) void __cdecl Target2(void* self, void* arg0, void* arg1, const void* method)
{ Body(static_cast<Work*>(self), 2, arg0, arg1, method, _ReturnAddress()); }
__declspec(noinline) void __cdecl Target3(void* self, float value, const void* method)
{
    std::uint32_t bits{};
    std::memcpy(&bits, &value, sizeof(bits));
    Body(static_cast<Work*>(self), 3, reinterpret_cast<void*>(static_cast<std::uintptr_t>(bits)),
        nullptr, method, _ReturnAddress());
}
__declspec(noinline) void __cdecl RejectedTarget(void* self, const void* method)
{ Body(static_cast<Work*>(self), 0, nullptr, nullptr, method, _ReturnAddress()); }
#pragma optimize("", on)
void* Target(unsigned kind)
{ return kind == 0 ? reinterpret_cast<void*>(&Target0) : kind == 1 ? reinterpret_cast<void*>(&Target1)
    : kind == 2 ? reinterpret_cast<void*>(&Target2) : reinterpret_cast<void*>(&Target3); }
Caller MakeCaller(void* target)
{
    // sub rsp,28; mov rax,target; call rax; add rsp,28; ret.
    // The CALL supplies the exact source return PC while unwind is ordinary
    // UWOP_ALLOC_SMALL(0x28), registered before any invocation.
    auto* block = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    CHECK(block != nullptr); if (!block) ExitProcess(2);
    const unsigned char code[]{ 0x48,0x83,0xec,0x28,0x48,0xb8,0,0,0,0,0,0,0,0,0xff,0xd0,0x48,0x83,0xc4,0x28,0xc3 };
    const unsigned char unwind[]{ 1,4,1,0,4,0x42,0,0 };
    std::memcpy(block, code, sizeof(code)); std::memcpy(block + 6, &target, sizeof(target));
    std::memcpy(block + 32, unwind, sizeof(unwind));
    auto* table = reinterpret_cast<PRUNTIME_FUNCTION>(block + 64);
    *table = { 0, static_cast<DWORD>(sizeof(code)), 32 };
    DWORD old{}; CHECK(VirtualProtect(block, 4096, PAGE_EXECUTE_READ, &old));
    CHECK(FlushInstructionCache(GetCurrentProcess(), block, 4096));
    CHECK(RtlAddFunctionTable(table, 1, reinterpret_cast<DWORD64>(block)));
    DWORD64 imageBase{};
    CHECK(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(block + 16), &imageBase, nullptr) == table
        && imageBase == reinterpret_cast<DWORD64>(block));
    return { block, block + 16, table };
}
LONG Filter(EXCEPTION_POINTERS* pointers, Work* work)
{
    const auto* record = pointers->ExceptionRecord;
    work->exceptionCode = record->ExceptionCode; work->exceptionFlags = record->ExceptionFlags;
    work->exceptionAddress = record->ExceptionAddress; work->parameterCount = record->NumberParameters;
    for (unsigned i = 0; i != 4; ++i) work->information[i] = i < record->NumberParameters ? record->ExceptionInformation[i] : 0;
    return record->ExceptionCode == 0xe06d7363 ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER;
}
__declspec(noinline) void InvokeSeh(Work* work)
{
    __try {
        const auto caller = callers[work->kind].code;
        if (!work->kind) reinterpret_cast<void(__cdecl*)(void*, const void*)>(caller)(work, Method(work));
        else if (work->kind == 1) reinterpret_cast<void(__cdecl*)(void*, void*, const void*)>(caller)(work, Arg0(work), Method(work));
        else if (work->kind == 2) reinterpret_cast<void(__cdecl*)(void*, void*, void*, const void*)>(caller)(work, Arg0(work), Arg1(work), Method(work));
        else {
            std::uint32_t bits = work->nullArguments ? 0u : work->scalarBits;
            float value{};
            std::memcpy(&value, &bits, sizeof(value));
            reinterpret_cast<void(__cdecl*)(void*, float, const void*)>(caller)(work, value, Method(work));
        }
        work->returned = true;
    } __except (Filter(GetExceptionInformation(), work)) { work->caught = true; }
}
void Run(Work* work)
{
    auto* previous = current;
    try { InvokeSeh(work); }
    catch (const OriginalCpp& error) {
        CHECK(error.payload == Payload && error.work == work && error.kind == work->kind);
        CHECK(work->exceptionCode == 0xe06d7363 && work->parameterCount == 4 && work->information[0] == 0x19930520
            && work->information[1] && work->information[2] && work->information[3] == reinterpret_cast<ULONG_PTR>(GetModuleHandleW(nullptr)));
        work->caught = true;
    } catch (...) { CHECK(false); work->caught = true; }
    CHECK(current == previous && work->entered == 1 && work->exited == 1 && work->invoked == 1);
    CHECK(work->returned == (work->originalFault == 0) && work->caught == (work->originalFault != 0));
    CHECK(work->deliveries == work->completions);
    if (work->originalFault) {
        CHECK(work->exceptionAddress != nullptr);
        if (work->originalFault == 1) CHECK(work->exceptionCode == OriginalSeh && work->parameterCount == 3
            && work->information[0] == Payload && work->information[1] == reinterpret_cast<ULONG_PTR>(work)
            && work->information[2] == work->kind);
        InterlockedIncrement64(&originalExceptions);
    }
    if (work->zeroToken) InterlockedIncrement64(&zeroTokens);
}
DWORD WINAPI Worker(void* data) { Run(static_cast<Work*>(data)); InterlockedIncrement64(&workerCalls); return 0; }
void Join(HANDLE thread)
{
    CHECK(thread != nullptr); if (!thread) ExitProcess(2);
    const auto result = WaitForSingleObject(thread, 10000); CHECK(result == WAIT_OBJECT_0);
    if (result != WAIT_OBJECT_0) ExitProcess(3); CloseHandle(thread);
}
void RejectedConfigure(PrepareFn prepare)
{
    const auto target = reinterpret_cast<void*>(&RejectedTarget); unsigned char before[32]{};
    std::memcpy(before, target, sizeof(before)); void* original{};
    CHECK(prepare(target, getEntry(0, 1), &original) == 0 && original); InterlockedIncrement64(&prepared);
    CHECK(std::memcmp(before, target, sizeof(before)) == 0);
    RizOperationConfigV1 config{ 1, sizeof(config), original, nullptr, Exit, &owners[0] };
    CHECK(configure(0, 1, &config) == 1);
    config.enter = Enter; CHECK(configure(0, 1, &config) == 0);
    CHECK(configure(0, 1, &config) == 2); InterlockedIncrement64(&configureRejections);
    // Retain the prepared trampoline and claimed helper slot forever. After the
    // claimed rejection there is no Commit, rollback, destroy or publication retry.
    CHECK(std::memcmp(before, target, sizeof(before)) == 0);
    Work work{}; work.unobserved = true;
    reinterpret_cast<void(__cdecl*)(void*, const void*)>(target)(&work, Method(&work));
    CHECK(work.invoked == 1 && work.entered == 0 && work.exited == 0 && current == nullptr);
    RizOperationStatsV1 stats{ 1, sizeof(stats), 0,0,0,0,0 };
    CHECK(getStats(0, 1, &stats) == 0 && stats.entered == 0 && stats.exited == 0 && stats.inFlight == 0);
    InterlockedIncrement64(&retainedPrepared);
}
int Result()
{
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-operation-dobby-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,"
        "\"originalCalls\":%lld,\"unpublishedOriginalCalls\":1,\"totalActualOriginalCalls\":93,\"entered\":%lld,\"exited\":%lld,\"uniqueSequenceChecks\":%lld,\"originalExceptionCases\":%lld,"
        "\"observerExceptionCases\":%lld,\"nativeWorkerCalls\":%lld,\"nestedCalls\":%lld,\"logicalDetachCases\":%lld,\"legitimateZeroTokens\":%lld,"
        "\"preparedTargets\":%lld,\"committedTargets\":%lld,\"commitRejections\":%lld,\"configureRejectedTargets\":%lld,\"retainedUnpublishedPreparedTargets\":%lld,"
        "\"kinds\":[0,1,2,3],\"frameSize\":64,\"realDobbyPreparedCommit\":true,\"exactSourceCallerVerified\":true,"
        "\"prepareRejected\":true,\"pinnedDobbyVerified\":true,\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,\"installed\":false,"
        "\"firstInstallSafetyVerified\":false,\"concurrentFirstPublicationVerified\":false,\"partialPublicationFailureInjected\":false,"
        "\"exceptionFlagsTransparencyVerified\":false,\"nativeAdmissionGateVerified\":false}\n",
        failures ? "false" : "true", static_cast<long long>(checks), static_cast<long long>(failures),
        static_cast<long long>(originalCalls), static_cast<long long>(nativeEnters), static_cast<long long>(nativeExits),
        static_cast<long long>(uniqueSequences), static_cast<long long>(originalExceptions), static_cast<long long>(observerExceptions),
        static_cast<long long>(workerCalls), static_cast<long long>(nestedCalls), static_cast<long long>(detachedCases), static_cast<long long>(zeroTokens),
        static_cast<long long>(prepared), static_cast<long long>(committed), static_cast<long long>(commitRejections),
        static_cast<long long>(configureRejections), static_cast<long long>(retainedPrepared));
    return failures ? 1 : 0;
}
}
int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    CHECK(argc == 3); if (argc != 3) return Result();
    HANDLE pinned{}; CHECK(PinnedDobby(argv[2], &pinned)); if (failures) return Result();
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr);
    helper = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    const auto dobby = LoadLibraryExW(argv[2], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    CHECK(helper && dobby); if (!helper || !dobby) return Result();
    getEntry = Symbol<EntryFn>(helper, "RizOperationContext_GetEntry");
    configure = Symbol<ConfigureFn>(helper, "RizOperationContext_Configure");
    getStats = Symbol<StatsFn>(helper, "RizOperationContext_GetStats");
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    if (failures) return Result();
    void* rejectedOriginal{}; CHECK(prepare(nullptr, getEntry(0, 0), &rejectedOriginal) != 0 && rejectedOriginal == nullptr);
    for (unsigned kind = 0; kind != 4; ++kind) {
        owners[kind].kind = kind; const auto target = Target(kind); const auto entry = getEntry(kind, 0);
        CHECK(target && entry); callers[kind] = MakeCaller(target);
        unsigned char before[32]{}, published[32]{}; std::memcpy(before, target, sizeof(before));
        CHECK(prepare(target, entry, &originals[kind]) == 0 && originals[kind]); InterlockedIncrement64(&prepared);
        CHECK(originals[kind] != target && originals[kind] != entry && std::memcmp(before, target, sizeof(before)) == 0);
        const RizOperationConfigV1 config{ 1, sizeof(config), originals[kind], Enter, Exit, &owners[kind] };
        CHECK(configure(kind, 0, &config) == 0);
        CHECK(std::memcmp(before, target, sizeof(before)) == 0);
        CHECK(commit(target) == 0); InterlockedIncrement64(&committed);
        CHECK(std::memcmp(before, target, sizeof(before)) != 0); std::memcpy(published, target, sizeof(published));
        CHECK(commit(target) != 0 && std::memcmp(published, target, sizeof(published)) == 0); InterlockedIncrement64(&commitRejections);
    }
    RejectedConfigure(prepare);
    for (unsigned kind = 0; kind != 4; ++kind) {
        for (unsigned variant = 0; variant != 3; ++variant) {
            Work work{}; work.kind = kind; work.zeroToken = variant == 1; work.nullArguments = variant == 2; Run(&work);
        }
        for (unsigned fault = 1; fault != 3; ++fault) {
            Work original{}; original.kind = kind; original.originalFault = fault; Run(&original);
            Work enter{}; enter.kind = kind; enter.enterFault = fault; Run(&enter);
            Work exit{}; exit.kind = kind; exit.exitFault = fault; Run(&exit);
            Work combined{}; combined.kind = kind; combined.originalFault = fault; combined.enterFault = 3-fault; combined.exitFault = fault; Run(&combined);
            Work recovery{}; recovery.kind = kind; Run(&recovery);
        }
        Work child{}; child.kind = kind; Work parent{}; parent.kind = kind; parent.nested = &child; Run(&parent);
        Work foreign{}; foreign.kind = (kind+1)%4; Work outer{}; outer.kind = kind; outer.nested = &foreign; Run(&outer);
        Work detach{}; detach.kind = kind; detach.detach = true; Run(&detach);
        Work inactive{}; inactive.kind = kind; Run(&inactive);
        CHECK(detach.deliveries == 1 && detach.completions == 1 && inactive.deliveries == 0 && inactive.completions == 0);
        InterlockedExchange(&owners[kind].active, 1); InterlockedIncrement64(&detachedCases);
    }
    Work workers[16]{}; HANDLE threads[16]{};
    for (unsigned index = 0; index != 16; ++index) {
        workers[index].kind = index%4; threads[index] = CreateThread(nullptr, 0, Worker, &workers[index], 0, nullptr);
    }
    for (const auto thread : threads) Join(thread);
    for (unsigned kind = 0; kind != 4; ++kind) {
        RizOperationStatsV1 stats{ 1, sizeof(stats), 0,0,0,0,0 };
        CHECK(getStats(kind, 0, &stats) == 0 && stats.entered == stats.exited && stats.inFlight == 0);
        CHECK(stats.enterFaults == 4 && stats.exitFaults == 4);
    }
    CHECK(current == nullptr && originalCalls == 92 && originalCalls == nativeEnters && nativeEnters == nativeExits
        && uniqueSequences == originalCalls && originalExceptions == 16 && observerExceptions == 32 && workerCalls == 16
        && nestedCalls == 8 && detachedCases == 4 && zeroTokens == 4 && prepared == 5 && committed == 4
        && commitRejections == 4 && configureRejections == 1 && retainedPrepared == 1);
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr);
    // All descriptors, Dobby trampolines, function tables, file/module handles
    // intentionally survive until process exit, including rejected publication.
    return Result();
}
