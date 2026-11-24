#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../VoidContext.h"

// Finite offline executable. Its two RX targets consist of an inspectable NOP
// prefix and a native tail jump. No game image or external process is patched.
namespace {
struct Receipt {
    std::uint32_t version, size, slot, ownerThread;
    std::uint64_t epoch;
    std::uint64_t reserved;
};
struct GateStats {
    std::uint32_t version, size;
    std::uint64_t active, parked, epoch;
    std::uint32_t phase, unknown;
};
static_assert(sizeof(Receipt) == 32 && offsetof(Receipt, epoch) == 0x10 && offsetof(Receipt, reserved) == 0x18);
static_assert(sizeof(GateStats) == 40 && offsetof(GateStats, phase) == 0x20 && offsetof(GateStats, unknown) == 0x24);
static_assert(sizeof(Receipt) == sizeof(RizVoidGateReceiptV1) && sizeof(GateStats) == sizeof(RizVoidGateStatsV1)
    && offsetof(Receipt, ownerThread) == offsetof(RizVoidGateReceiptV1, ownerThreadId));
using CloseFn = std::uint32_t(__cdecl*)(std::uint32_t, std::uint32_t, Receipt*);
using VerifyFn = std::uint32_t(__cdecl*)(const Receipt*);
using ReleaseFn = std::uint32_t(__cdecl*)(const Receipt*);
using GateStatsFn = std::uint32_t(__cdecl*)(std::uint32_t, GateStats*);
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using GetEntryFn = decltype(&RizVoidContext_GetEntry);
using ConfigureFn = decltype(&RizVoidContext_Configure);
using StatsFn = decltype(&RizVoidContext_GetStats);
CloseFn closeGate;
VerifyFn verifyGate;
ReleaseFn releaseGate;
GateStatsFn gateStats;
StatsFn getStats;
void* targets[2]{};
void* originals[2]{};
constexpr std::uint32_t Open = 0, Closing = 1, Closed = 2;
constexpr DWORD OriginalSeh = 0xE056474F;
constexpr std::uint64_t Payload = 0xFEDCBA9876543210ULL;
constexpr std::uintptr_t Token = 0xABCDEF0123456789ULL;
volatile LONG64 checks{}, failures{}, successfulCloses{}, timedOutCloses{}, rejectedSelfCloses{}, recursiveCalls{}, parkedWitnesses{};
struct CppOriginal { std::uint64_t payload; void* self; const void* method; };
struct Work {
    std::uint32_t slot{}, holdStage{}, originalMode{}, selfCloseStage{};
    bool recurse{};
    HANDLE reached{}, proceed{}, recurseNow{}, recurseDone{};
    volatile LONG enterCalls{}, originalCalls{}, originalExits{}, exitCalls{}, exitFinished{};
    DWORD callerThread{};
    const void* method{};
    bool returned{}, caught{};
    DWORD exceptionCode{};
    std::uint64_t exceptionPayload{};
};
struct CloseWork {
    std::uint32_t slot{}, timeout{}, result{99};
    Receipt receipt{1, sizeof(Receipt)};
    HANDLE done{}, releaseNow{}, releaseDone{};
    bool releaseOnCommand{}, wrongChecks{};
    std::uint32_t releaseResult{99};
};
void Check(bool condition, const char* text, int line)
{
    InterlockedIncrement64(&checks);
    if (!condition) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED void gate line %d: %s\n", line, text); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto pointer = GetProcAddress(module, name); CHECK(pointer != nullptr);
    T value{}; static_assert(sizeof(value) == sizeof(pointer)); std::memcpy(&value, &pointer, sizeof(value)); return value;
}
GateStats ReadGate(std::uint32_t slot)
{
    GateStats value{1, sizeof(value)}; CHECK(gateStats(slot, &value) == 0);
    CHECK(value.version == 1 && value.size == sizeof(value) && value.unknown == 0);
    return value;
}
RizVoidStatsV1 ReadStats(std::uint32_t slot)
{
    RizVoidStatsV1 value{1, sizeof(value)}; CHECK(getStats(slot, &value) == 0); return value;
}
bool WaitPhase(std::uint32_t slot, std::uint32_t phase, std::uint64_t parked)
{
    const auto deadline = GetTickCount64() + 5000;
    do { const auto value = ReadGate(slot); if (value.phase == phase && value.parked >= parked) return true; Sleep(1); }
    while (GetTickCount64() < deadline);
    return false;
}
HANDLE Event(bool signaled = false)
{
    const auto result = CreateEventW(nullptr, TRUE, signaled ? TRUE : FALSE, nullptr); CHECK(result != nullptr); return result;
}
void InitWork(Work* work, std::uint32_t slot, std::uint32_t stage = 0)
{
    *work = {}; work->slot = slot; work->holdStage = stage;
    work->method = reinterpret_cast<const void*>(0x3456789ABCDEF010ULL + slot * 16);
    work->reached = Event(); work->proceed = Event(); work->recurseNow = Event(); work->recurseDone = Event();
}
void CloseWorkEvents(Work* work)
{
    CloseHandle(work->reached); CloseHandle(work->proceed); CloseHandle(work->recurseNow); CloseHandle(work->recurseDone);
}
void InitClose(CloseWork* work, std::uint32_t slot, std::uint32_t timeout, bool releaseOnCommand = false)
{
    *work = {}; work->slot = slot; work->timeout = timeout; work->result = 99; work->releaseResult = 99;
    work->receipt.version = 1; work->receipt.size = sizeof(Receipt); work->releaseOnCommand = releaseOnCommand;
    work->done = Event(); work->releaseNow = Event(); work->releaseDone = Event();
}
void CloseControllerEvents(CloseWork* work)
{ CloseHandle(work->done); CloseHandle(work->releaseNow); CloseHandle(work->releaseDone); }
void Join(HANDLE thread)
{
    CHECK(thread != nullptr); if (!thread) return;
    CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
}
void SelfClose(Work* work, std::uint32_t stage)
{
    if (work->selfCloseStage != stage) return;
    Receipt receipt{1, sizeof(receipt)};
    CHECK(closeGate(work->slot, 0, &receipt) == 2);
    CHECK(ReadGate(work->slot).phase == Open && receipt.epoch == 0);
    InterlockedIncrement64(&rejectedSelfCloses);
    // Any admitted void slot rejects self-close, including another target.
    receipt = {1, sizeof(receipt)};
    CHECK(closeGate(work->slot ^ 1u, 0, &receipt) == 2);
    CHECK(ReadGate(work->slot ^ 1u).phase == Open && receipt.epoch == 0);
}
void Hold(Work* work, std::uint32_t stage)
{
    if (work->holdStage != stage) return;
    CHECK(SetEvent(work->reached)); CHECK(WaitForSingleObject(work->proceed, 5000) == WAIT_OBJECT_0);
}
void Execute(Work* work);
std::uintptr_t __cdecl Enter(const RizVoidFrameV1* frame, void* owner)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(owner == &targets[work->slot] && frame->abiVersion == 1 && frame->byteSize == sizeof(*frame)
        && frame->reserved0 == 0 && frame->reserved1 == 0 && frame->method == work->method && frame->caller);
    CHECK(InterlockedIncrement(&work->enterCalls) == 1 && ReadGate(work->slot).active >= 1 && ReadStats(work->slot).inFlight >= 1);
    work->callerThread = GetCurrentThreadId(); SelfClose(work, 1); Hold(work, 1); return Token;
}
void __cdecl Exit(const RizVoidFrameV1* frame, std::uintptr_t token, std::uint32_t flags, void* owner)
{
    auto* work = static_cast<Work*>(frame->self);
    CHECK(owner == &targets[work->slot] && frame->method == work->method && token == Token);
    CHECK(InterlockedIncrement(&work->exitCalls) == 1 && work->originalCalls == 1 && work->originalExits == 1);
    CHECK(flags == (work->originalMode ? RizVoidAbnormal : RizVoidReturned));
    CHECK(ReadGate(work->slot).active >= 1 && ReadStats(work->slot).inFlight >= 1);
    SelfClose(work, 3); Hold(work, 3); InterlockedIncrement(&work->exitFinished);
}
struct OriginalExit {
    Work* work;
    ~OriginalExit() { InterlockedIncrement(&work->originalExits); }
};
void __cdecl Original(void* self, const void* method)
{
    auto* work = static_cast<Work*>(self); CHECK(method == work->method);
    CHECK(InterlockedIncrement(&work->originalCalls) == 1 && work->enterCalls == 1);
    OriginalExit exited{work}; SelfClose(work, 2);
    if (work->recurse) {
        CHECK(SetEvent(work->reached)); CHECK(WaitForSingleObject(work->recurseNow, 5000) == WAIT_OBJECT_0);
        const auto before = ReadGate(work->slot); CHECK(before.phase == Closing || before.phase == Closed);
        Work child{}; InitWork(&child, work->slot); Execute(&child);
        CHECK(child.returned && !child.caught && child.enterCalls == 1 && child.originalCalls == 1 && child.originalExits == 1
            && child.exitCalls == 1 && child.exitFinished == 1);
        CloseWorkEvents(&child); InterlockedIncrement64(&recursiveCalls);
        CHECK(SetEvent(work->recurseDone)); CHECK(WaitForSingleObject(work->proceed, 5000) == WAIT_OBJECT_0);
    } else Hold(work, 2);
    if (work->originalMode == 1) {
        const ULONG_PTR payload[]{static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(self), reinterpret_cast<ULONG_PTR>(method)};
        RaiseException(OriginalSeh, 0, 3, payload);
    } else if (work->originalMode == 2) throw CppOriginal{Payload, self, method};
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
    __try { reinterpret_cast<RizVoidOriginalV1>(targets[work->slot])(work, work->method); work->returned = true; }
    __except (CatchSeh(GetExceptionInformation(), work)) { }
}
void Execute(Work* work)
{
    try { ExecuteSeh(work); }
    catch (const CppOriginal& exception) {
        CHECK(exception.payload == Payload && exception.self == work && exception.method == work->method);
        work->caught = true; work->exceptionCode = 0xE06D7363; work->exceptionPayload = exception.payload;
    }
    CHECK(work->enterCalls == 1 && work->originalCalls == 1 && work->originalExits == 1 && work->exitCalls == 1 && work->exitFinished == 1);
    CHECK(work->returned == (work->originalMode == 0) && work->caught == (work->originalMode != 0));
}
DWORD WINAPI CallerThread(void* data) { Execute(static_cast<Work*>(data)); return 0; }
DWORD WINAPI CloserThread(void* data)
{
    auto* work = static_cast<CloseWork*>(data);
    work->result = closeGate(work->slot, work->timeout, &work->receipt);
    if (work->result == 0) {
        InterlockedIncrement64(&successfulCloses);
        CHECK(work->receipt.version == 1 && work->receipt.size == sizeof(Receipt) && work->receipt.slot == work->slot
            && work->receipt.epoch != 0 && work->receipt.ownerThread == GetCurrentThreadId()
            && work->receipt.reserved == 0);
        CHECK(verifyGate(&work->receipt) == 0 && ReadGate(work->slot).active == 0);
    } else if (work->result == 4) {
        InterlockedIncrement64(&timedOutCloses); CHECK(ReadGate(work->slot).phase == Open && work->receipt.epoch == 0);
    }
    CHECK(SetEvent(work->done));
    if (work->releaseOnCommand && work->result == 0) {
        CHECK(WaitForSingleObject(work->releaseNow, 10000) == WAIT_OBJECT_0);
        if (work->wrongChecks) {
            auto wrong = work->receipt; ++wrong.epoch;
            CHECK(verifyGate(&wrong) == 4 && releaseGate(&wrong) == 4 && ReadGate(work->slot).phase == Closed);
            Receipt other{1, sizeof(other)};
            CHECK(closeGate(work->slot ^ 1u, 1000, &other) == 0 && other.epoch != work->receipt.epoch);
            CHECK(verifyGate(&other) == 0 && ReadGate(work->slot ^ 1u).phase == Closed);
            InterlockedIncrement64(&successfulCloses);
            wrong = work->receipt; wrong.slot ^= 1u;
            CHECK(releaseGate(&wrong) == 4 && ReadGate(work->slot).phase == Closed && ReadGate(work->slot ^ 1u).phase == Closed);
            CHECK(releaseGate(&other) == 0 && ReadGate(work->slot ^ 1u).phase == Open);
            wrong = work->receipt; wrong.reserved = 1;
            CHECK(releaseGate(&wrong) == 1 && ReadGate(work->slot).phase == Closed);
        }
        work->releaseResult = releaseGate(&work->receipt); CHECK(work->releaseResult == 0);
        CHECK(ReadGate(work->slot).phase == Open && releaseGate(&work->receipt) != 0);
        CHECK(SetEvent(work->releaseDone));
    }
    return 0;
}
void* CreateTarget()
{
    auto* target = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    CHECK(target != nullptr); if (!target) return nullptr;
    std::memset(target, 0x90, 32);
    const unsigned char tail[]{0xff,0x25,0,0,0,0}; std::memcpy(target + 32, tail, sizeof(tail));
    const auto original = &Original; std::memcpy(target + 38, &original, sizeof(original));
    DWORD previous = 0; CHECK(VirtualProtect(target, 4096, PAGE_EXECUTE_READ, &previous));
    CHECK(FlushInstructionCache(GetCurrentProcess(), target, 46)); return target;
}
void CheckParked(const Work& work)
{
    CHECK(work.enterCalls == 0 && work.originalCalls == 0 && work.originalExits == 0 && work.exitCalls == 0 && work.exitFinished == 0);
    InterlockedIncrement64(&parkedWitnesses);
}
void CheckFinal(std::uint32_t slot)
{
    const auto gate = ReadGate(slot);
    const auto stats = ReadStats(slot);
    CHECK(gate.phase == Open && gate.active == 0 && gate.parked == 0);
    CHECK(stats.entered == stats.exited && stats.inFlight == 0 && stats.enterFaults == 0 && stats.exitFaults == 0);
}
void TestSelfCloseAndExceptions()
{
    for (std::uint32_t stage = 1; stage != 4; ++stage) {
        Work work{}; InitWork(&work, 0); work.selfCloseStage = stage; Execute(&work); CloseWorkEvents(&work); CheckFinal(0);
    }
    for (std::uint32_t mode = 1; mode != 3; ++mode) {
        Work work{}; InitWork(&work, 0); work.originalMode = mode; Execute(&work);
        CHECK(work.exceptionCode == (mode == 1 ? OriginalSeh : 0xE06D7363) && work.exceptionPayload == Payload);
        CloseWorkEvents(&work); CheckFinal(0);
        Receipt receipt{1, sizeof(receipt)}; CHECK(closeGate(0, 1000, &receipt) == 0);
        CHECK(verifyGate(&receipt) == 0 && releaseGate(&receipt) == 0); InterlockedIncrement64(&successfulCloses);
    }
}
void TestTimeout(std::uint32_t stage)
{
    Work active{}, parked{}; InitWork(&active, 0, stage); InitWork(&parked, 0);
    const auto activeThread = CreateThread(nullptr, 0, CallerThread, &active, 0, nullptr); CHECK(activeThread != nullptr);
    CHECK(WaitForSingleObject(active.reached, 5000) == WAIT_OBJECT_0);
    const auto before = ReadGate(0); CHECK(before.active == 1 && before.phase == Open);
    CloseWork closer{}; InitClose(&closer, 0, 1000);
    const auto closeThread = CreateThread(nullptr, 0, CloserThread, &closer, 0, nullptr); CHECK(closeThread != nullptr);
    CHECK(WaitPhase(0, Closing, 0));
    const auto parkedThread = CreateThread(nullptr, 0, CallerThread, &parked, 0, nullptr); CHECK(parkedThread != nullptr);
    CHECK(WaitPhase(0, Closing, 1)); CheckParked(parked); CHECK(ReadGate(0).active == 1);
    CHECK(WaitForSingleObject(closer.done, 5000) == WAIT_OBJECT_0); CHECK(closer.result == 4);
    // Timeout means no edit was admitted. Every waiter must be awakened even
    // though the old original/callback remains blocked and Active is still 1.
    Join(parkedThread); CHECK(parked.returned && active.exitFinished == 0 && ReadGate(0).phase == Open);
    CHECK(ReadGate(0).epoch > before.epoch);
    CHECK(SetEvent(active.proceed)); Join(activeThread); Join(closeThread);
    CloseWorkEvents(&active); CloseWorkEvents(&parked); CloseControllerEvents(&closer); CheckFinal(0);
}
void TestClosedAndRecursion()
{
    Work active{}, parked{}, late{}; InitWork(&active, 0); InitWork(&parked, 0); InitWork(&late, 0); active.recurse = true;
    const auto activeThread = CreateThread(nullptr, 0, CallerThread, &active, 0, nullptr); CHECK(activeThread != nullptr);
    CHECK(WaitForSingleObject(active.reached, 5000) == WAIT_OBJECT_0);
    CloseWork closer{}; InitClose(&closer, 0, 5000, true); closer.wrongChecks = true;
    const auto closeThread = CreateThread(nullptr, 0, CloserThread, &closer, 0, nullptr); CHECK(closeThread != nullptr);
    CHECK(WaitPhase(0, Closing, 0));
    const auto parkedThread = CreateThread(nullptr, 0, CallerThread, &parked, 0, nullptr); CHECK(parkedThread != nullptr);
    CHECK(WaitPhase(0, Closing, 1)); CheckParked(parked);
    CHECK(SetEvent(active.recurseNow)); CHECK(WaitForSingleObject(active.recurseDone, 5000) == WAIT_OBJECT_0);
    CHECK(WaitForSingleObject(closer.done, 0) == WAIT_TIMEOUT && ReadGate(0).active == 1);
    CHECK(SetEvent(active.proceed)); Join(activeThread);
    CHECK(WaitForSingleObject(closer.done, 5000) == WAIT_OBJECT_0 && closer.result == 0);
    const auto closed = ReadGate(0); CHECK(closed.phase == Closed && closed.active == 0 && closed.parked == 1);
    CHECK(closed.epoch == closer.receipt.epoch);
    CheckParked(parked); CHECK(ReadStats(0).inFlight >= 1);
    Receipt busy{1, sizeof(busy)}; CHECK(closeGate(0, 0, &busy) == 3 && ReadGate(0).phase == Closed);
    // Receipt belongs to the closer's OS thread, not the test controller.
    CHECK(verifyGate(&closer.receipt) == 3 && releaseGate(&closer.receipt) == 3 && ReadGate(0).phase == Closed);
    const auto lateThread = CreateThread(nullptr, 0, CallerThread, &late, 0, nullptr); CHECK(lateThread != nullptr);
    CHECK(WaitPhase(0, Closed, 2)); CheckParked(late); CheckParked(parked);
    CHECK(ReadGate(0).active == 0 && ReadStats(0).inFlight == 2);
    CHECK(SetEvent(closer.releaseNow)); CHECK(WaitForSingleObject(closer.releaseDone, 5000) == WAIT_OBJECT_0);
    Join(parkedThread); Join(lateThread); Join(closeThread);
    CHECK(parked.returned && late.returned && closer.releaseResult == 0);
    CloseWorkEvents(&active); CloseWorkEvents(&parked); CloseWorkEvents(&late); CloseControllerEvents(&closer); CheckFinal(0);
}
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 3) { std::fprintf(stderr, "Usage: VoidAdmission.Fixture.exe existing-helper.dll inspected-dobby.dll\n"); return 2; }
    const auto helper = LoadLibraryW(argv[1]), dobby = LoadLibraryW(argv[2]); CHECK(helper && dobby); if (!helper || !dobby) return 2;
    const auto getEntry = Symbol<GetEntryFn>(helper, "RizVoidContext_GetEntry");
    const auto configure = Symbol<ConfigureFn>(helper, "RizVoidContext_Configure");
    getStats = Symbol<StatsFn>(helper, "RizVoidContext_GetStats");
    closeGate = Symbol<CloseFn>(helper, "RizVoidContext_Close"); verifyGate = Symbol<VerifyFn>(helper, "RizVoidContext_Verify");
    releaseGate = Symbol<ReleaseFn>(helper, "RizVoidContext_Release"); gateStats = Symbol<GateStatsFn>(helper, "RizVoidContext_GetGateStats");
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare"); const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    if (!getEntry || !configure || !getStats || !closeGate || !verifyGate || !releaseGate || !gateStats || !prepare || !commit) return 2;
    for (std::uint32_t slot = 0; slot != 2; ++slot) {
        targets[slot] = CreateTarget(); if (!targets[slot]) return 2;
        unsigned char before[32]{}; std::memcpy(before, targets[slot], sizeof(before));
        CHECK(prepare(targets[slot], getEntry(slot), &originals[slot]) == 0 && originals[slot]);
        CHECK(std::memcmp(before, targets[slot], sizeof(before)) == 0);
        const RizVoidConfigV1 config{1, sizeof(config), reinterpret_cast<RizVoidOriginalV1>(originals[slot]), Enter, Exit, &targets[slot]};
        CHECK(configure(slot, &config) == 0 && commit(targets[slot]) == 0);
        CHECK(std::memcmp(before, targets[slot], sizeof(before)) != 0); CheckFinal(slot);
    }
    Receipt invalid{2, sizeof(invalid)}; CHECK(closeGate(0, 0, &invalid) == 1);
    invalid = {1, sizeof(invalid)}; CHECK(closeGate(0, 10001, &invalid) == 1 && closeGate(31, 0, &invalid) == 1);
    TestSelfCloseAndExceptions();
    TestTimeout(1); TestTimeout(2); TestTimeout(3);
    TestClosedAndRecursion(); CheckFinal(1);
    CHECK(rejectedSelfCloses == 3 && timedOutCloses == 3 && recursiveCalls == 1 && successfulCloses == 4 && parkedWitnesses >= 5);
    const auto final = ReadGate(0);
    const auto stats = ReadStats(0);
    CHECK(stats.entered == 15 && stats.exited == 15 && stats.inFlight == 0);
    std::printf("{\"kind\":\"native-void-admission-gate-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,\"successfulCloses\":%lld,\"timeouts\":%lld,\"selfCloseRejected\":%lld,\"recursiveCalls\":%lld,\"parkedWitnesses\":%lld,\"active\":%llu,\"parked\":%llu,\"epoch\":%llu,\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"realDobby\":true,\"prepareConfigureCommit\":true,\"nativeOriginalOnce\":true,\"closedReceipt\":true,\"wrongReleaseRejected\":true,\"timeoutReopens\":true,\"sehCppPreserved\":true,\"initialInstallSafetyVerified\":false,\"gameRuntimeVerified\":false}\n",
        failures ? "false" : "true", checks, failures, successfulCloses, timedOutCloses, rejectedSelfCloses, recursiveCalls, parkedWitnesses,
        final.active, final.parked, final.epoch, stats.entered, stats.exited, stats.inFlight);
    return failures ? 1 : 0;
}
