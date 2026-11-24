#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include "../BootstrapVoidContext.h"

// Offline targets are patched only before their first invocation. This fixture
// verifies immutable observer attachment over an already installed transport;
// it makes no claim about concurrent first installation into a game process.
extern "C" void RtBootstrapTarget0(void*, const void*);
extern "C" void RtBootstrapTarget1(void*, const void*);
extern "C" unsigned char RtBootstrapMidSite0, RtBootstrapMidReturn0;
extern "C" unsigned char RtBootstrapMidSite1, RtBootstrapMidReturn1;
extern "C" unsigned char RtBootstrapCallerReturn;
extern "C" void RtBootstrapCall(void*, void*, const void*);

namespace {
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using ConfigureFn = decltype(&RizVoidBootstrap_Configure);
using AttachFn = decltype(&RizVoidBootstrap_Attach);
using InfoFn = decltype(&RizVoidBootstrap_GetInfo);
using StatsFn = decltype(&RizVoidContext_GetStats);
using MidStatsFn = decltype(&RizEventMidContext_GetStats);
using GateStatsFn = decltype(&RizVoidContext_GetGateStats);
ConfigureFn configure;
AttachFn attach;
InfoFn getInfo;
StatsFn getStats;
MidStatsFn getMidStats;
GateStatsFn getGateStats;
void* targets[2]{ reinterpret_cast<void*>(&RtBootstrapTarget0), reinterpret_cast<void*>(&RtBootstrapTarget1) };
void* midSites[2]{ &RtBootstrapMidSite0, &RtBootstrapMidSite1 };
void* midReturns[2]{ &RtBootstrapMidReturn0, &RtBootstrapMidReturn1 };
void* originals[2]{};
std::uint64_t epochs[2]{};
volatile LONG64 checks{}, failures{}, oldCalls{}, observedCalls{}, recursions{}, originalExceptions{}, observerExceptions{};
constexpr DWORD OriginalSeh = 0xE042534F, ObserverSeh = 0xE0425343;
constexpr std::uint64_t Payload = 0xABCDEF9876543210ULL;
constexpr std::uintptr_t TokenMask = 0xABCDEF0123456789ULL;
struct CppOriginal { std::uint64_t payload; void* self; const void* method; };
struct CppObserver { std::uint64_t payload; };
struct Work {
    std::uint32_t slot{}, mode{}, faultStage{}, faultKind{};
    bool observed{}, hold{}, recurse{};
    HANDLE reached{}, proceed{};
    const void* method{};
    void* actualList{};
    Work* previous{};
    DWORD thread{};
    const void* caller{};
    volatile LONG enter{}, before{}, mid{}, body{}, originalExit{}, exit{};
    bool returned{}, caught{};
    DWORD exceptionCode{};
    std::uint64_t exceptionPayload{};
};
thread_local Work* current = nullptr;

void Check(bool condition, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!condition) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED bootstrap void line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto pointer = GetProcAddress(module, name); CHECK(pointer != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(pointer)); std::memcpy(&result, &pointer, sizeof(result)); return result;
}
HANDLE Event()
{ const auto handle = CreateEventW(nullptr, TRUE, FALSE, nullptr); CHECK(handle); return handle; }
void Init(Work* work, std::uint32_t slot, bool observed)
{
    *work = {}; work->slot = slot; work->observed = observed;
    work->method = reinterpret_cast<const void*>(0x123456789ABCD000ULL + slot * 0x100);
    // The list address is opaque but captured by actual RBP at the native CALL.
    work->actualList = &work->method; work->reached = Event(); work->proceed = Event();
}
void CloseEvents(Work* work) { CloseHandle(work->reached); CloseHandle(work->proceed); }
void Join(HANDLE thread)
{
    CHECK(thread != nullptr); if (!thread) return;
    const auto result = WaitForSingleObject(thread, 10000); CHECK(result == WAIT_OBJECT_0);
    if (result != WAIT_OBJECT_0) ExitProcess(3); CloseHandle(thread);
}
RizVoidBootstrapInfoV1 Info(std::uint32_t slot)
{ RizVoidBootstrapInfoV1 result{1, sizeof(result)}; CHECK(getInfo(slot, &result) == 0); return result; }
void Drained(std::uint32_t slot)
{
    RizVoidStatsV1 value{1, sizeof(value)}; CHECK(getStats(slot, &value) == 0);
    CHECK(value.entered == value.exited && value.inFlight == 0 && value.enterFaults == 0 && value.exitFaults == 0);
    RizEventMidStatsV1 mid{1, sizeof(mid)}; CHECK(getMidStats(slot, &mid) == 0);
    CHECK(mid.entered == mid.exited && mid.inFlight == 0 && mid.faults == 0);
    RizVoidGateStatsV1 gate{1, sizeof(gate)}; CHECK(getGateStats(slot, &gate) == 0);
    CHECK(gate.active == 0 && gate.parked == 0 && gate.phase == RizVoidGateOpen);
    CHECK(current == nullptr);
}
void Fault(Work* work, std::uint32_t stage)
{
    if (work->faultStage != stage) return;
    InterlockedIncrement64(&observerExceptions);
    if (work->faultKind == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
    else throw CppObserver{Payload};
}
std::uintptr_t __cdecl Enter(const RizVoidFrameV1* frame, void* user)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(work->observed && user == &targets[work->slot]);
    CHECK(frame->abiVersion == 1 && frame->byteSize == sizeof(*frame) && frame->reserved0 == 0 && frame->reserved1 == 0);
    CHECK(frame->method == work->method && frame->caller == &RtBootstrapCallerReturn);
    CHECK(InterlockedIncrement(&work->enter) == 1 && work->before == 0);
    work->thread = GetCurrentThreadId(); work->caller = frame->caller; work->previous = current; current = work;
    Fault(work, 1); return reinterpret_cast<std::uintptr_t>(work) ^ TokenMask;
}
void __cdecl Mid(const RizEventMidFrameV1* frame, void* user)
{
    auto* work = current;
    CHECK(work != nullptr); if (!work) return;
    CHECK(work->observed && user == &targets[work->slot] && GetCurrentThreadId() == work->thread);
    CHECK(frame->abiVersion == 1 && frame->byteSize == sizeof(*frame) && frame->reserved0 == 0 && frame->reserved1 == 0);
    CHECK(frame->returnPc == reinterpret_cast<std::uint64_t>(midReturns[work->slot]));
    CHECK(frame->gpr[RizEventMidRsi] == reinterpret_cast<std::uint64_t>(work));
    CHECK(frame->gpr[RizEventMidRbp] == reinterpret_cast<std::uint64_t>(work->actualList));
    CHECK(frame->gpr[RizEventMidRsp] == frame->entryRsp + 8 && frame->entryRsp != 0);
    CHECK(work->enter == 1 && work->before == 1 && work->body == 0 && InterlockedIncrement(&work->mid) == 1);
    Fault(work, 2);
}
void __cdecl Exit(const RizVoidFrameV1* frame, std::uintptr_t token, std::uint32_t flags, void* user)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(work->observed && user == &targets[work->slot] && GetCurrentThreadId() == work->thread && current == work);
    CHECK(frame->method == work->method && frame->caller == work->caller && work->caller == &RtBootstrapCallerReturn);
    CHECK(token == (work->faultStage == 1 ? 0 : reinterpret_cast<std::uintptr_t>(work) ^ TokenMask));
    const auto expectedFlags = (work->mode ? RizVoidAbnormal : RizVoidReturned) | (work->faultStage == 1 ? RizVoidEnterFaulted : 0u);
    CHECK(flags == expectedFlags && work->before == 1 && work->body == 1 && work->originalExit == 1);
    CHECK(InterlockedIncrement(&work->exit) == 1); current = work->previous; Fault(work, 3);
}
LONG CatchSeh(EXCEPTION_POINTERS* exception, Work* work)
{
    const auto record = exception->ExceptionRecord;
    if (record->ExceptionCode == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;
    CHECK(record->ExceptionCode == OriginalSeh && record->NumberParameters == 3
        && record->ExceptionInformation[0] == Payload && record->ExceptionInformation[1] == reinterpret_cast<ULONG_PTR>(work)
        && record->ExceptionInformation[2] == reinterpret_cast<ULONG_PTR>(work->method));
    work->caught = true; work->exceptionCode = record->ExceptionCode;
    work->exceptionPayload = record->NumberParameters ? record->ExceptionInformation[0] : 0;
    return EXCEPTION_EXECUTE_HANDLER;
}
void ExecuteSeh(Work* work)
{
    __try { RtBootstrapCall(targets[work->slot], work, work->method); work->returned = true; }
    __except (CatchSeh(GetExceptionInformation(), work)) { }
}
void Execute(Work* work)
{
    const auto* prior = current;
    try { ExecuteSeh(work); }
    catch (const CppOriginal& exception) {
        CHECK(exception.payload == Payload && exception.self == work && exception.method == work->method);
        work->caught = true; work->exceptionCode = 0xE06D7363; work->exceptionPayload = exception.payload;
    }
    CHECK(current == prior && work->before == 1 && work->body == 1 && work->originalExit == 1);
    CHECK(work->returned == (work->mode == 0) && work->caught == (work->mode != 0));
    if (work->mode) { CHECK(work->exceptionPayload == Payload); InterlockedIncrement64(&originalExceptions); }
    if (work->observed) {
        CHECK(work->enter == 1 && work->exit == 1 && work->mid == (work->faultStage == 1 ? 0 : 1));
        InterlockedIncrement64(&observedCalls);
    } else { CHECK(work->enter == 0 && work->mid == 0 && work->exit == 0); InterlockedIncrement64(&oldCalls); }
}
DWORD WINAPI Worker(void* data) { Execute(static_cast<Work*>(data)); return 0; }

void* RelayNear(void* site, void* helper)
{
    SYSTEM_INFO info{}; GetSystemInfo(&info); const auto granularity = static_cast<std::uintptr_t>(info.dwAllocationGranularity);
    const auto center = reinterpret_cast<std::uintptr_t>(site) & ~(granularity - 1);
    for (std::uintptr_t distance = granularity; distance < 0x70000000ULL; distance += granularity) {
        for (int direction = 0; direction != 2; ++direction) {
            if (direction && center < distance) continue;
            const auto candidate = direction ? center - distance : center + distance;
            auto* relay = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(candidate), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
            if (!relay) continue;
            const unsigned char jump[]{0xff, 0x25, 0, 0, 0, 0}; std::memcpy(relay, jump, sizeof(jump)); std::memcpy(relay + 6, &helper, sizeof(helper));
            DWORD previous{}; CHECK(VirtualProtect(relay, 4096, PAGE_EXECUTE_READ, &previous));
            CHECK(FlushInstructionCache(GetCurrentProcess(), relay, 14)); return relay;
        }
    }
    CHECK(false); return nullptr;
}
void PatchMid(std::uint32_t slot, void* helper)
{
    auto* site = static_cast<unsigned char*>(midSites[slot]);
    const unsigned char expected[]{0x0f, 0x1f, 0x44, 0, 0}; CHECK(std::memcmp(site, expected, 5) == 0);
    CHECK(site + 5 == midReturns[slot]); const auto* relay = static_cast<unsigned char*>(RelayNear(site, helper)); if (!relay) ExitProcess(2);
    const auto difference = reinterpret_cast<std::intptr_t>(relay) - reinterpret_cast<std::intptr_t>(site + 5);
    CHECK(difference >= (std::numeric_limits<std::int32_t>::min)() && difference <= (std::numeric_limits<std::int32_t>::max)());
    const auto displacement = static_cast<std::int32_t>(difference); unsigned char call[5]{0xe8}; std::memcpy(call + 1, &displacement, 4);
    DWORD old{}; CHECK(VirtualProtect(site, 5, PAGE_EXECUTE_READWRITE, &old)); std::memcpy(site, call, 5);
    DWORD ignored{}; CHECK(VirtualProtect(site, 5, old, &ignored)); CHECK(FlushInstructionCache(GetCurrentProcess(), site, 5));
    CHECK(std::memcmp(site, call, 5) == 0); MEMORY_BASIC_INFORMATION protection{};
    CHECK(VirtualQuery(site, &protection, sizeof(protection)) == sizeof(protection) && protection.Protect == old);
}
void LateAttach(std::uint32_t slot)
{
    Work old{}; Init(&old, slot, false); old.hold = true;
    const auto thread = CreateThread(nullptr, 0, Worker, &old, 0, nullptr); CHECK(thread);
    CHECK(WaitForSingleObject(old.reached, 5000) == WAIT_OBJECT_0);
    CHECK(old.before == 1 && old.mid == 0 && old.body == 0 && old.enter == 0 && old.exit == 0);
    const auto before = Info(slot); CHECK(before.attached == 0 && before.unknown == 0 && before.epoch == epochs[slot]);
    const RizVoidBootstrapObserverV1 observer{1, sizeof(observer), Enter, Exit, Mid, &targets[slot]};
    CHECK(attach(slot, epochs[slot] + 1, &observer) == 3);
    CHECK(Info(slot).attached == 0 && attach(slot, epochs[slot], &observer) == 0);
    const auto after = Info(slot); CHECK(after.attached == 1 && after.unknown == 0 && after.epoch == before.epoch);
    CHECK(attach(slot, epochs[slot], &observer) == 4);
    CHECK(SetEvent(old.proceed)); Join(thread); CloseEvents(&old); Drained(slot);
    CHECK(old.returned && !old.caught && old.enter == 0 && old.mid == 0 && old.exit == 0);
}
}

extern "C" void __cdecl RtBootstrapBeforeMid(void* self, const void* method)
{
    auto* work = static_cast<Work*>(self); CHECK(method == work->method && InterlockedIncrement(&work->before) == 1);
    CHECK(work->enter == (work->observed ? 1 : 0));
    if (work->hold) { CHECK(SetEvent(work->reached)); CHECK(WaitForSingleObject(work->proceed, 10000) == WAIT_OBJECT_0); }
    if (work->recurse) {
        Work child{}; Init(&child, work->slot, true); child.method = reinterpret_cast<const void*>(reinterpret_cast<std::uintptr_t>(work->method) + 8);
        Execute(&child); CHECK(child.returned && current == work && child.method != work->method);
        CloseEvents(&child); InterlockedIncrement64(&recursions);
    }
}
extern "C" void* __cdecl RtBootstrapGetList(void* self) { return static_cast<Work*>(self)->actualList; }
extern "C" void __cdecl RtBootstrapBody(void* self, const void* method)
{
    auto* work = static_cast<Work*>(self); CHECK(method == work->method && InterlockedIncrement(&work->body) == 1);
    struct Leave { Work* work; ~Leave() { InterlockedIncrement(&work->originalExit); } } leave{work};
    if (work->mode == 1) {
        const ULONG_PTR payload[]{static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(self), reinterpret_cast<ULONG_PTR>(method)};
        RaiseException(OriginalSeh, 0, 3, payload);
    } else if (work->mode == 2) throw CppOriginal{Payload, self, method};
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) { std::fprintf(stderr, "Usage: BootstrapVoid.Fixture.exe existing-helper.dll inspected-dobby.dll\n"); return 2; }
    const auto helper = LoadLibraryW(argv[1]), dobby = LoadLibraryW(argv[2]); CHECK(helper && dobby); if (!helper || !dobby) return 2;
    configure = Symbol<ConfigureFn>(helper, "RizVoidBootstrap_Configure"); attach = Symbol<AttachFn>(helper, "RizVoidBootstrap_Attach");
    getInfo = Symbol<InfoFn>(helper, "RizVoidBootstrap_GetInfo"); getStats = Symbol<StatsFn>(helper, "RizVoidContext_GetStats");
    getMidStats = Symbol<MidStatsFn>(helper, "RizEventMidContext_GetStats"); getGateStats = Symbol<GateStatsFn>(helper, "RizVoidContext_GetGateStats");
    const auto getEntry = Symbol<decltype(&RizVoidContext_GetEntry)>(helper, "RizVoidContext_GetEntry");
    const auto getMidEntry = Symbol<decltype(&RizEventMidContext_GetEntry)>(helper, "RizEventMidContext_GetEntry");
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    if (!configure || !attach || !getInfo || !getStats || !getMidStats || !getGateStats || !getEntry || !getMidEntry || !prepare || !commit) return 2;
    for (std::uint32_t slot = 0; slot != 2; ++slot) {
        unsigned char prefix[32]{}; std::memcpy(prefix, targets[slot], sizeof(prefix));
        CHECK(prepare(targets[slot], getEntry(slot), &originals[slot]) == 0 && originals[slot]);
        CHECK(std::memcmp(prefix, targets[slot], sizeof(prefix)) == 0);
        const RizVoidBootstrapConfigV1 config{1, sizeof(config), targets[slot], reinterpret_cast<RizVoidOriginalV1>(originals[slot]), midReturns[slot]};
        CHECK(configure(slot, &config) == 0 && configure(slot, &config) == 2);
        const auto info = Info(slot); epochs[slot] = info.epoch;
        CHECK(info.slot == slot && info.attached == 0 && info.target == targets[slot] && reinterpret_cast<void*>(info.original) == originals[slot]
            && info.entry == getEntry(slot) && info.epoch != 0 && info.unknown == 0 && info.reserved == 0);
        PatchMid(slot, getMidEntry()); CHECK(commit(targets[slot]) == 0);
        CHECK(std::memcmp(prefix, targets[slot], sizeof(prefix)) != 0); Drained(slot);
    }
    CHECK(epochs[0] != epochs[1]); LateAttach(0); LateAttach(1);
    for (std::uint32_t mode = 0; mode != 3; ++mode) {
        Work work{}; Init(&work, 0, true); work.mode = mode; work.recurse = true;
        const auto thread = CreateThread(nullptr, 0, Worker, &work, 0, nullptr); Join(thread); CloseEvents(&work); Drained(0);
        CHECK(Info(0).unknown == 0);
    }
    for (std::uint32_t stage = 1; stage != 4; ++stage) for (std::uint32_t kind = 1; kind != 3; ++kind) {
        Work work{}; Init(&work, 1, true); work.faultStage = stage; work.faultKind = kind;
        Execute(&work); CloseEvents(&work); Drained(1); CHECK(Info(1).unknown == 1);
        // A subsequent clean invocation must see no stale TLS record or token.
        Work clean{}; Init(&clean, 1, true); Execute(&clean); CloseEvents(&clean); Drained(1);
    }
    CHECK(oldCalls == 2 && observedCalls == 18 && recursions == 3 && originalExceptions == 2 && observerExceptions == 6);
    CHECK(Info(0).unknown == 0 && Info(1).unknown == 1);
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-bootstrap-void-late-attach\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,\"oldUnobservedCalls\":%lld,\"observedCalls\":%lld,\"foreignMethodRecursions\":%lld,\"originalExceptions\":%lld,\"observerExceptions\":%lld,\"realNativeMidFrame\":true,\"firstInstallSafetyVerified\":false,\"gameRuntimeVerified\":false}\n",
        failures ? "false" : "true", checks, failures, oldCalls, observedCalls, recursions, originalExceptions, observerExceptions);
    return failures ? 1 : 0;
}
