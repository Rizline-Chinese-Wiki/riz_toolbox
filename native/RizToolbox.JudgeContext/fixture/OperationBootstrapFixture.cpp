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
using BootstrapConfigureFn = decltype(&RizOperationBootstrap_Configure);
using AttachFn = decltype(&RizOperationBootstrap_Attach);
using InfoFn = decltype(&RizOperationBootstrap_GetInfo);
struct Owner { unsigned kind{}; volatile LONG active = 1; };
constexpr unsigned KindCount = 4;
Owner owners[KindCount]{};
EntryFn getEntry{}; ConfigureFn configure{}; StatsFn getStats{};
BootstrapConfigureFn bootstrapConfigure{}; AttachFn attach{}; InfoFn getInfo{};
std::uint64_t epochs[KindCount]{};
volatile LONG64 snapshotNullCalls{}, lateAttachCalls{}, aliasRejections{}, wrongEpochRejections{};
HMODULE helper{}; void* originals[KindCount]{};
struct Caller { unsigned char* code{}; const void* returnPc{}; PRUNTIME_FUNCTION function{}; };
Caller callers[KindCount]{};
volatile LONG64 checks{}, failures{}, originalCalls{}, nativeEnters{}, nativeExits{}, uniqueSequences{};
volatile LONG64 originalExceptions{}, observerExceptions{}, workerCalls{}, nestedCalls{}, detachedCases{}, zeroTokens{};
volatile LONG64 prepared{}, committed{}, commitRejections{}, configureRejections{}, retainedPrepared{};
std::uint64_t transportEntered{}, transportExited{}, transportInFlight{};
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
    bool lateWait{}; HANDLE reached{}, resume{};
    Work* previous{}; Work* nested{};
};
thread_local Work* current{};
struct OriginalCpp { std::uint64_t payload; Work* work; unsigned kind; };
struct ObserverCpp { std::uint64_t payload; };
void Check(bool value, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!value) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED OperationBootstrap line %d: %s\n", line, expression); }
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
    if (work->kind == RizOperationSingle1Void)
        return reinterpret_cast<void*>(static_cast<std::uintptr_t>(work->scalarBits));
    return work->kind > 0 ? &work->argument0 : nullptr;
}
void* Arg1(Work* work) { return work->kind == RizOperationRef2Void && !work->nullArguments ? &work->argument1 : nullptr; }
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
    CHECK(frame->self == work && frame->arg0 == Arg0(work) && frame->arg1 == Arg1(work) && frame->method == Method(work));
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
    CHECK(work->kind == kind && arg0 == Arg0(work) && arg1 == Arg1(work) && method == Method(work));
    CHECK(work->unobserved ? current == nullptr : current == work && work->entered == 1 && work->exited == 0);
    work->originalCaller = caller;
    {
        HMODULE module{};
        CHECK(caller && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(caller), &module) && module == helper);
        InterlockedIncrement64(&originalCalls);
    }
    ++work->invoked;
    if (work->lateWait) {
        CHECK(work->unobserved && current == nullptr && work->reached && work->resume);
        CHECK(SetEvent(work->reached));
        const auto waited = WaitForSingleObject(work->resume, 10000);
        CHECK(waited == WAIT_OBJECT_0); if (waited != WAIT_OBJECT_0) ExitProcess(3);
        CHECK(work->entered == 0 && work->exited == 0 && current == nullptr);
    }
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
    Body(static_cast<Work*>(self), RizOperationSingle1Void,
        reinterpret_cast<void*>(static_cast<std::uintptr_t>(bits)), nullptr, method, _ReturnAddress());
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
        else if (work->kind == RizOperationRef2Void)
            reinterpret_cast<void(__cdecl*)(void*, void*, void*, const void*)>(caller)(work, Arg0(work), Arg1(work), Method(work));
        else {
            // Passing a float through its real ABI is essential: an integer in
            // RDX would make a scalar-only frame check pass without testing XMM1.
            const auto bits = work->nullArguments ? 0u : work->scalarBits;
            float value{}; std::memcpy(&value, &bits, sizeof(value));
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
    CHECK(current == previous && work->entered == (work->unobserved ? 0u : 1u)
        && work->exited == (work->unobserved ? 0u : 1u) && work->invoked == 1);
    if (work->unobserved) InterlockedIncrement64(&snapshotNullCalls);
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
RizOperationBootstrapInfoV1 Info(unsigned kind, unsigned slot, std::uint32_t expected = 0)
{
    RizOperationBootstrapInfoV1 value{}; value.abiVersion = 1; value.byteSize = sizeof(value);
    // Caller-supplied identity is never a receipt. The native helper overwrites it.
    value.kind = 99; value.slot = 99; value.target = reinterpret_cast<void*>(1); value.epoch = 999;
    CHECK(getInfo(kind, slot, &value) == expected);
    CHECK(value.abiVersion == 1 && value.byteSize == 96 && value.kind == kind && value.slot == slot
        && value.entry == getEntry(kind, slot));
    return value;
}
void InvalidProtocols()
{
    CHECK(sizeof(RizOperationBootstrapConfigV1) == 24 && sizeof(RizOperationBootstrapObserverV1) == 32
        && sizeof(RizOperationBootstrapInfoV1) == 96);
    const RizOperationBootstrapObserverV1 observer{ 1, sizeof(observer), Enter, Exit, &owners[0] };
    for (unsigned kind = 0; kind != KindCount; ++kind) {
        auto empty = Info(kind, 30, 2);
        CHECK(!empty.target && !empty.original && !empty.epoch && !empty.attached && !empty.unknown
            && !empty.entered && !empty.exited && !empty.inFlight);
        CHECK(attach(kind, 30, 1, &observer) == 2);
        RizOperationBootstrapConfigV1 config{ 1, sizeof(config), Target(kind), reinterpret_cast<void*>(&RejectedTarget) };
        auto invalid = config; invalid.abiVersion = 2; CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        invalid = config; invalid.byteSize = 25; CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        invalid = config; invalid.target = nullptr; CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        invalid = config; invalid.original = nullptr; CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        invalid = config; invalid.original = invalid.target; CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        invalid = config; invalid.target = getEntry((kind+1)%KindCount, 0); CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        invalid = config; invalid.original = getEntry((kind+1)%KindCount, 0); CHECK(bootstrapConfigure(kind, 30, &invalid) == 1);
        CHECK(bootstrapConfigure(kind, 30, nullptr) == 1 && attach(kind, 30, 0, &observer) == 1);
        CHECK(Info(kind, 30, 2).unknown == 0);
    }
    RizOperationBootstrapInfoV1 info{}; info.abiVersion = 1; info.byteSize = sizeof(info);
    CHECK(getInfo(4, 0, &info) == 1 && getInfo(0, 32, &info) == 1);
    info.abiVersion = 2; CHECK(getInfo(0, 30, &info) == 1);
    info.abiVersion = 1; info.byteSize = 95; CHECK(getInfo(0, 30, &info) == 1);
    CHECK(attach(4, 0, 1, &observer) == 1 && attach(0, 32, 1, &observer) == 1);
}
void LegacyAlias(PrepareFn prepare)
{
    const auto target = reinterpret_cast<void*>(&RejectedTarget);
    unsigned char before[32]{}; std::memcpy(before, target, sizeof(before)); void* original{};
    CHECK(prepare(target, getEntry(0, 31), &original) == 0 && original); InterlockedIncrement64(&prepared);
    CHECK(std::memcmp(before, target, sizeof(before)) == 0);
    const RizOperationConfigV1 legacy{ 1, sizeof(legacy), original, Enter, Exit, &owners[0] };
    CHECK(configure(0, 31, &legacy) == 0);
    const RizOperationBootstrapConfigV1 bootstrap{ 1, sizeof(bootstrap), target, original };
    CHECK(bootstrapConfigure(0, 31, &bootstrap) == 2); InterlockedIncrement64(&aliasRejections);
    const auto info = Info(0, 31, 3);
    CHECK(!info.target && !info.original && !info.epoch && !info.attached && !info.unknown
        && !info.entered && !info.exited && !info.inFlight);
    const RizOperationBootstrapObserverV1 observer{ 1, sizeof(observer), Enter, Exit, &owners[0] };
    CHECK(attach(0, 31, 1, &observer) == 2);
    // A legacy permanently claimed slot cannot become a bootstrapped slot;
    // retain its prepared trampoline without Commit or destroy/rollback/retry.
    CHECK(std::memcmp(before, target, sizeof(before)) == 0); InterlockedIncrement64(&retainedPrepared);
}
void AttachAcrossOldCall(unsigned kind)
{
    // Includes actual original SEH/C++ through the null descriptor before Attach.
    for (unsigned fault = 0; fault != 3; ++fault) {
        Work initial{}; initial.kind = kind; initial.unobserved = true; initial.originalFault = fault; Run(&initial);
    }
    Work old{}; old.kind = kind; old.unobserved = true; old.lateWait = true;
    old.reached = CreateEventW(nullptr, TRUE, FALSE, nullptr); old.resume = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    CHECK(old.reached && old.resume); if (!old.reached || !old.resume) ExitProcess(3);
    const auto thread = CreateThread(nullptr, 0, Worker, &old, 0, nullptr); CHECK(thread != nullptr); if (!thread) ExitProcess(3);
    const auto reached = WaitForSingleObject(old.reached, 10000); CHECK(reached == WAIT_OBJECT_0);
    if (reached != WAIT_OBJECT_0) { SetEvent(old.resume); Join(thread); return; }
    auto info = Info(kind, 0);
    CHECK(info.target == Target(kind) && info.original == originals[kind] && info.epoch == epochs[kind]
        && !info.attached && !info.unknown && info.entered == 4 && info.exited == 3 && info.inFlight == 1);
    CHECK(old.invoked == 1 && old.entered == 0 && old.exited == 0);
    const RizOperationBootstrapObserverV1 observer{ 1, sizeof(observer), Enter, Exit, &owners[kind] };
    CHECK(attach(kind, 0, info.epoch+1, &observer) == 3); InterlockedIncrement64(&wrongEpochRejections);
    auto invalid = observer; invalid.enter = nullptr; CHECK(attach(kind, 0, info.epoch, &invalid) == 1);
    invalid = observer; invalid.exit = nullptr; CHECK(attach(kind, 0, info.epoch, &invalid) == 1);
    invalid = observer; invalid.byteSize = 31; CHECK(attach(kind, 0, info.epoch, &invalid) == 1);
    CHECK(attach(kind, 0, info.epoch, &observer) == 0);
    CHECK(attach(kind, 0, info.epoch, &observer) == 4);
    info = Info(kind, 0); CHECK(info.attached == 1 && !info.unknown && info.inFlight == 1 && info.exited == 3);
    CHECK(SetEvent(old.resume)); Join(thread); CloseHandle(old.reached); CloseHandle(old.resume);
    CHECK(old.invoked == 1 && old.entered == 0 && old.exited == 0 && old.returned && !old.caught);
    info = Info(kind, 0); CHECK(info.entered == 4 && info.exited == 4 && info.inFlight == 0 && !info.unknown);
    Work fresh{}; fresh.kind = kind; Run(&fresh);
    CHECK(fresh.entered == 1 && fresh.exited == 1 && fresh.invoked == 1);
    InterlockedIncrement64(&lateAttachCalls);
}
int Result()
{
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-operation-bootstrap-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,"
        "\"originalCalls\":%lld,\"observerEnters\":%lld,\"observerExits\":%lld,\"uniqueSequenceChecks\":%lld,"
        "\"nullSnapshotOriginalCalls\":%lld,\"lateAttachCases\":%lld,\"originalExceptionCases\":%lld,\"observerExceptionCases\":%lld,"
        "\"nativeWorkerCalls\":%lld,\"nestedCalls\":%lld,\"logicalDetachCases\":%lld,\"legitimateZeroTokens\":%lld,"
        "\"preparedTargets\":%lld,\"committedTargets\":%lld,\"commitRejections\":%lld,\"aliasConfigureRejections\":%lld,"
        "\"wrongEpochRejections\":%lld,\"retainedUnpublishedPreparedTargets\":%lld,\"kinds\":[0,1,2,3],\"floatPayloadCases\":8,\"frameSize\":64,\"infoSize\":96,"
        "\"realDobbyPreparedCommit\":true,\"oldCallsDoNotBorrowAttachedObserver\":true,\"exactSourceCallerVerified\":true,"
        "\"pinnedDobbyVerified\":true,\"transportEntered\":%llu,\"transportExited\":%llu,\"statsInFlight\":%llu,"
        "\"bootstrapConfigureRejections\":%lld,\"stickyUnknownAfterObserverFaultVerified\":true,"
        "\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false,\"installed\":false,\"firstInstallSafetyVerified\":false,"
        "\"concurrentFirstPublicationVerified\":false,\"partialPublicationFailureInjected\":false,\"exceptionFlagsTransparencyVerified\":false}"
        "\n", failures ? "false" : "true", static_cast<long long>(checks), static_cast<long long>(failures),
        static_cast<long long>(originalCalls), static_cast<long long>(nativeEnters), static_cast<long long>(nativeExits),
        static_cast<long long>(uniqueSequences), static_cast<long long>(snapshotNullCalls), static_cast<long long>(lateAttachCalls),
        static_cast<long long>(originalExceptions), static_cast<long long>(observerExceptions), static_cast<long long>(workerCalls),
        static_cast<long long>(nestedCalls), static_cast<long long>(detachedCases), static_cast<long long>(zeroTokens),
        static_cast<long long>(prepared), static_cast<long long>(committed), static_cast<long long>(commitRejections),
        static_cast<long long>(aliasRejections), static_cast<long long>(wrongEpochRejections), static_cast<long long>(retainedPrepared),
        static_cast<unsigned long long>(transportEntered), static_cast<unsigned long long>(transportExited),
        static_cast<unsigned long long>(transportInFlight), static_cast<long long>(configureRejections));
    return failures ? 1 : 0;
}
}
int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    CHECK(argc == 3); if (argc != 3) return Result();
    HANDLE pinned{}; CHECK(PinnedDobby(argv[2], &pinned)); if (failures) return Result();
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr && GetModuleHandleW(L"UnityPlayer.dll") == nullptr);
    helper = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    const auto dobby = LoadLibraryExW(argv[2], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    CHECK(helper && dobby); if (!helper || !dobby) return Result();
    getEntry = Symbol<EntryFn>(helper, "RizOperationContext_GetEntry"); configure = Symbol<ConfigureFn>(helper, "RizOperationContext_Configure");
    getStats = Symbol<StatsFn>(helper, "RizOperationContext_GetStats");
    bootstrapConfigure = Symbol<BootstrapConfigureFn>(helper, "RizOperationBootstrap_Configure");
    attach = Symbol<AttachFn>(helper, "RizOperationBootstrap_Attach"); getInfo = Symbol<InfoFn>(helper, "RizOperationBootstrap_GetInfo");
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    if (failures) return Result(); InvalidProtocols();
    void* rejectedOriginal{}; CHECK(prepare(nullptr, getEntry(0, 0), &rejectedOriginal) != 0 && !rejectedOriginal);
    for (unsigned kind = 0; kind != KindCount; ++kind) {
        owners[kind].kind = kind; const auto target = Target(kind); const auto entry = getEntry(kind, 0); CHECK(target && entry);
        callers[kind] = MakeCaller(target);
        unsigned char before[32]{}, published[32]{}; std::memcpy(before, target, sizeof(before));
        MEMORY_BASIC_INFORMATION prior{}; CHECK(VirtualQuery(target, &prior, sizeof(prior)) == sizeof(prior));
        CHECK(prepare(target, entry, &originals[kind]) == 0 && originals[kind]); InterlockedIncrement64(&prepared);
        CHECK(originals[kind] != target && originals[kind] != entry && std::memcmp(before, target, sizeof(before)) == 0);
        const RizOperationBootstrapConfigV1 config{ 1, sizeof(config), target, originals[kind] };
        CHECK(bootstrapConfigure(kind, 0, &config) == 0 && std::memcmp(before, target, sizeof(before)) == 0);
        const auto info = Info(kind, 0); epochs[kind] = info.epoch;
        CHECK(info.target == target && info.original == originals[kind] && info.epoch && !info.attached && !info.unknown);
        if (kind) CHECK(epochs[kind] > epochs[kind-1]);
        CHECK(bootstrapConfigure(kind, 0, &config) == 2); InterlockedIncrement64(&configureRejections);
        auto replacement = config; replacement.target = reinterpret_cast<void*>(&RejectedTarget);
        replacement.original = Target((kind+1)%KindCount); CHECK(bootstrapConfigure(kind, 0, &replacement) == 2);
        const auto immutable = Info(kind, 0); CHECK(immutable.target == target && immutable.original == originals[kind] && immutable.epoch == info.epoch);
        const RizOperationConfigV1 alias{ 1, sizeof(alias), originals[kind], Enter, Exit, &owners[kind] };
        CHECK(configure(kind, 0, &alias) == 2); InterlockedIncrement64(&aliasRejections);
        CHECK(commit(target) == 0); InterlockedIncrement64(&committed);
        CHECK(std::memcmp(before, target, sizeof(before)) != 0); std::memcpy(published, target, sizeof(published));
        MEMORY_BASIC_INFORMATION after{}; CHECK(VirtualQuery(target, &after, sizeof(after)) == sizeof(after) && after.Protect == prior.Protect);
        CHECK(commit(target) != 0 && std::memcmp(published, target, sizeof(published)) == 0); InterlockedIncrement64(&commitRejections);
    }
    LegacyAlias(prepare);
    for (unsigned kind = 0; kind != KindCount; ++kind) AttachAcrossOldCall(kind);
    // Normal scalar arguments, signed zero, infinity, both NaN encodings and a
    // subnormal must survive real XMM1 -> frame -> XMM1 round trips bit for bit.
    constexpr std::uint32_t scalarPayloads[]{0u,0x80000000u,0x3f800000u,0x7f800000u,
        0xff800000u,0x7fc12345u,0x7f812345u,1u};
    for (const auto bits : scalarPayloads) {
        Work scalar{}; scalar.kind = RizOperationSingle1Void; scalar.scalarBits = bits;
        Run(&scalar); CHECK(scalar.frame.arg0 == reinterpret_cast<void*>(static_cast<std::uintptr_t>(bits)));
    }
    for (unsigned kind = 0; kind != KindCount; ++kind) {
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
        Work cross{}; cross.kind = (kind+1)%KindCount; Work outer{}; outer.kind = kind; outer.nested = &cross; Run(&outer);
        Work detached{}; detached.kind = kind; detached.detach = true; Run(&detached);
        Work inactive{}; inactive.kind = kind; Run(&inactive);
        CHECK(detached.deliveries == 1 && detached.completions == 1 && inactive.deliveries == 0 && inactive.completions == 0);
        InterlockedExchange(&owners[kind].active, 1); InterlockedIncrement64(&detachedCases);
    }
    Work workers[16]{}; HANDLE threads[16]{};
    for (unsigned index = 0; index != 16; ++index) {
        workers[index].kind = index%KindCount; threads[index] = CreateThread(nullptr, 0, Worker, &workers[index], 0, nullptr);
    }
    for (const auto thread : threads) Join(thread);
    for (unsigned kind = 0; kind != KindCount; ++kind) {
        const auto info = Info(kind, 0);
        CHECK(info.attached && info.unknown && info.target == Target(kind) && info.original == originals[kind]
            && info.epoch == epochs[kind] && info.entered == (kind == RizOperationSingle1Void ? 36u : 28u) && info.exited == info.entered && !info.inFlight
            && info.enterFaults == 4 && info.exitFaults == 4);
        transportEntered += info.entered; transportExited += info.exited; transportInFlight += info.inFlight;
        RizOperationStatsV1 stats{ 1, sizeof(stats), 0,0,0,0,0 };
        CHECK(getStats(kind, 0, &stats) == 0 && stats.entered == info.entered && stats.exited == info.exited && stats.inFlight == 0
            && stats.enterFaults == 4 && stats.exitFaults == 4);
        const RizOperationBootstrapObserverV1 observer{ 1, sizeof(observer), Enter, Exit, &owners[kind] };
        CHECK(attach(kind, 0, info.epoch, &observer) == 5);
        const RizOperationBootstrapConfigV1 config{ 1, sizeof(config), Target(kind), originals[kind] };
        CHECK(bootstrapConfigure(kind, 0, &config) == 2);
    }
    CHECK(current == nullptr && originalCalls == 120 && snapshotNullCalls == 16 && lateAttachCalls == 4
        && nativeEnters == 104 && nativeExits == 104 && uniqueSequences == 104 && originalExceptions == 24
        && observerExceptions == 32 && workerCalls == 20 && nestedCalls == 8 && detachedCases == 4 && zeroTokens == 4
        && prepared == 5 && committed == 4 && commitRejections == 4 && configureRejections == 4
        && aliasRejections == 5 && wrongEpochRejections == 4 && retainedPrepared == 1);
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr && GetModuleHandleW(L"UnityPlayer.dll") == nullptr);
    // All helper slots, RX caller tables, modules, prepared trampolines and pinned
    // Dobby file handle intentionally survive until this isolated process exits.
    return Result();
}
