#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <climits>
#include <initializer_list>
#include "../MidContext.h"

// Independent executable fixture: it patches only its own five-byte NOP while
// single-threaded. No game image, live process patcher or native parent TLS is
// implemented by this executable.
namespace {
struct alignas(16) Frame {
    std::uint32_t version, size;
    std::uintptr_t returnPc, entryRsp, flags;
    std::uintptr_t gpr[16];
    unsigned char xmm[16][16];
    std::uint32_t mxcsr;
    std::uint16_t fpcsr, reserved0;
    std::uint64_t reserved1;
};
using Callback = void(__cdecl*)(const Frame*, void*);
struct Config {
    std::uint32_t version, size;
    const void* expectedReturnPc;
    Callback callback;
    void* user;
    std::uint64_t reserved;
};
struct Stats {
    std::uint32_t version, size;
    std::uint64_t entered, exited, inFlight, faults;
};
static_assert(sizeof(Frame) == 0x1b0 && alignof(Frame) == 16);
static_assert(offsetof(Frame, gpr) == 0x20 && offsetof(Frame, xmm) == 0xa0 && offsetof(Frame, mxcsr) == 0x1a0);
static_assert(sizeof(Config) == 40 && sizeof(Stats) == 40);
static_assert(sizeof(Frame) == sizeof(RizEventMidFrameV1) && sizeof(Config) == sizeof(RizEventMidConfigV1)
    && sizeof(Stats) == sizeof(RizEventMidStatsV1));
struct List { void* klass; void* monitor; void** items; std::int32_t count, version; };
static_assert(offsetof(List, count) == 0x18 && offsetof(List, version) == 0x1c);
struct Work {
    unsigned callbackMode{}, originalMode{}, count{}, mutate{};
    bool recurse{}, block{}, expectCallback{true};
    std::uint64_t entered{}, continued{}, callbacks{}, rows{}, rowSeal{};
    unsigned char event[32]{};
    void* items[8]{};
    List list{};
    Frame before{}, after{};
    bool caught{};
    DWORD exceptionCode{}, thread{};
    std::uint64_t payload{};
};
constexpr DWORD callbackSehCode = 0xE04D4342, originalSehCode = 0xE04D4F52;
constexpr std::uint64_t payloadSeal = 0xF123456789ABCDEFULL, stackSeal = 0xAABBCCDDEEFF1122ULL;
struct CallbackCpp { std::uint64_t payload; };
struct OriginalCpp { std::uint64_t payload; };
volatile LONG64 checks{}, failures{}, callbacks{}, unwindWalks{}, virtualUnwinds{}, maxInFlight{};
volatile LONG observerEnabled = 1;
thread_local Work* workStack[16]{};
thread_local unsigned workDepth{};
HMODULE helper;
void* helperEntry;
HANDLE reached, resume;
using GetPointer = void*(__cdecl*)();
using GetScalar = std::uint32_t(__cdecl*)();
using Configure = std::uint32_t(__cdecl*)(std::uint32_t, const Config*);
using GetStats = std::uint32_t(__cdecl*)(std::uint32_t, Stats*);
Configure configure;
GetStats getStats;
constexpr std::uint32_t slot = 0;
}
extern "C" {
alignas(16) unsigned char RtEventMidFixture_XmmInput[16][16]{};
std::uint32_t RtEventMidFixture_InputMxcsr = 0x3f80, RtEventMidFixture_ClobberMxcsr = 0x1f80;
std::uint16_t RtEventMidFixture_InputFpcsr = 0x077f, RtEventMidFixture_ClobberFpcsr = 0x037f;
void __cdecl RtEventMidFixture_InvokeAsm(void*);
void __cdecl RtEventMidFixture_ClobberVolatile();
extern char RtEventMidFixture_Body, RtEventMidFixture_Site, RtEventMidFixture_SiteReturn, RtEventMidFixture_InvokeReturn;
}
namespace {
void Check(bool ok, const char* reason, int line)
{
    InterlockedIncrement64(&checks);
    if (!ok) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED mid fixture line %d: %s\n", line, reason); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(const char* name)
{
    const auto pointer = GetProcAddress(helper, name); CHECK(pointer != nullptr);
    T value{}; static_assert(sizeof(value) == sizeof(pointer)); std::memcpy(&value, &pointer, sizeof(value)); return value;
}
Stats ReadStats()
{
    Stats value{ 1, sizeof(value) }; CHECK(getStats(slot, &value) == 0);
    CHECK(value.version == 1 && value.size == sizeof(value));
    LONG64 previous = InterlockedCompareExchange64(&maxInFlight, 0, 0);
    while (value.inFlight > static_cast<std::uint64_t>(previous)) {
        const auto actual = InterlockedCompareExchange64(&maxInFlight, static_cast<LONG64>(value.inFlight), previous);
        if (actual == previous) break; previous = actual;
    }
    return value;
}
Work* Current()
{
    CHECK(workDepth > 0 && workDepth <= 16); return workStack[workDepth - 1];
}
void SameState(const Frame& left, const Frame& right)
{
    CHECK(left.version == 1 && right.version == 1 && left.size == sizeof(Frame) && right.size == sizeof(Frame));
    CHECK(left.flags == right.flags && left.mxcsr == right.mxcsr && left.fpcsr == right.fpcsr);
    for (unsigned index = 0; index != 16; ++index) {
        CHECK(left.gpr[index] == right.gpr[index]);
        CHECK(std::memcmp(left.xmm[index], right.xmm[index], 16) == 0);
    }
}
void FillContext(CONTEXT* context, const Frame& frame)
{
    *context = {};
    context->ContextFlags = CONTEXT_ALL;
    context->Rax = frame.gpr[0]; context->Rcx = frame.gpr[1]; context->Rdx = frame.gpr[2]; context->Rbx = frame.gpr[3];
    context->Rsp = frame.gpr[4]; context->Rbp = frame.gpr[5]; context->Rsi = frame.gpr[6]; context->Rdi = frame.gpr[7];
    context->R8 = frame.gpr[8]; context->R9 = frame.gpr[9]; context->R10 = frame.gpr[10]; context->R11 = frame.gpr[11];
    context->R12 = frame.gpr[12]; context->R13 = frame.gpr[13]; context->R14 = frame.gpr[14]; context->R15 = frame.gpr[15];
    context->EFlags = static_cast<DWORD>(frame.flags); context->MxCsr = frame.mxcsr;
    std::memcpy(&context->Xmm0, frame.xmm, sizeof(frame.xmm));
}
void OneVirtualUnwind(const Frame& before, DWORD64 imageBase, PRUNTIME_FUNCTION function,
    std::uintptr_t pc, std::uintptr_t rsp, std::uintptr_t r13)
{
    CONTEXT context{}; FillContext(&context, before); context.Rip = pc; context.Rsp = rsp; context.R13 = r13;
    PVOID data = nullptr; DWORD64 establisher = 0;
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, pc, function, &context, &data, &establisher, nullptr);
    CHECK(context.Rip == reinterpret_cast<std::uintptr_t>(&RtEventMidFixture_SiteReturn));
    CHECK(context.Rsp == before.gpr[4] && context.R13 == before.gpr[13]);
    CHECK(context.Rbx == before.gpr[3] && context.Rbp == before.gpr[5] && context.Rsi == before.gpr[6]
        && context.Rdi == before.gpr[7] && context.R12 == before.gpr[12] && context.R14 == before.gpr[14] && context.R15 == before.gpr[15]);
    for (unsigned index = 6; index != 16; ++index)
        CHECK(std::memcmp(reinterpret_cast<const unsigned char*>(&context.Xmm0) + index * 16, before.xmm[index], 16) == 0);
    InterlockedIncrement64(&virtualUnwinds);
}
void TestUnwindBoundaries(const Frame& frame)
{
    DWORD64 imageBase = 0; const auto function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(helperEntry), &imageBase, nullptr);
    CHECK(function != nullptr && imageBase == reinterpret_cast<DWORD64>(helper)); if (!function) return;
    const auto begin = imageBase + function->BeginAddress, end = imageBase + function->EndAddress;
    CHECK(begin == reinterpret_cast<DWORD64>(helperEntry));
    // Check the concrete assembly contract before simulating partial prolog and
    // epilog contexts. This is not arbitrary instruction decoding.
    const unsigned char prefix[]{ 0x41,0x55,0x9c,0x48,0x81,0xec,0xe8,0x01,0x00,0x00,0x4c,0x8d,0xac,0x24,0x80,0x00,0x00,0x00 };
    const unsigned char epilog[]{ 0x49,0x8d,0xa5,0x70,0x01,0x00,0x00,0x41,0x5d,0xc3 };
    CHECK(std::memcmp(reinterpret_cast<const void*>(begin), prefix, sizeof(prefix)) == 0);
    CHECK(end > begin + sizeof(prefix) + sizeof(epilog) && std::memcmp(reinterpret_cast<const void*>(end - sizeof(epilog)), epilog, sizeof(epilog)) == 0);
    const auto* unwind = reinterpret_cast<const unsigned char*>(imageBase + function->UnwindData);
    CHECK((unwind[0] & 7) == 1 && (unwind[3] & 15) == 13 && (unwind[3] >> 4) == 8);
    const auto entry = frame.entryRsp, base = entry - 0x1f8;
    const auto& before = *reinterpret_cast<const Frame*>(frame.gpr[4] + 0x40);
    OneVirtualUnwind(before, imageBase, function, begin, entry, before.gpr[13]);
    OneVirtualUnwind(before, imageBase, function, begin + 2, entry - 8, before.gpr[13]);
    OneVirtualUnwind(before, imageBase, function, begin + 3, entry - 16, before.gpr[13]);
    OneVirtualUnwind(before, imageBase, function, begin + 10, base, before.gpr[13]);
    OneVirtualUnwind(before, imageBase, function, begin + 18, base, base + 0x80);
    OneVirtualUnwind(before, imageBase, function, end - 10, base, base + 0x80);
    OneVirtualUnwind(before, imageBase, function, end - 3, base + 0x1f0, base + 0x80);
    OneVirtualUnwind(before, imageBase, function, end - 1, entry, before.gpr[13]);
}
void WalkUnwind(CONTEXT context)
{
    bool sawHelper = false, sawFixture = false, sawCaller = false;
    DWORD64 bridgeBase = 0; const auto bridgeFunction = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(helperEntry), &bridgeBase, nullptr);
    CHECK(bridgeFunction != nullptr); if (!bridgeFunction) return;
    for (unsigned index = 0; index != 64 && context.Rip && context.Rsp; ++index) {
        if (context.Rip >= bridgeBase + bridgeFunction->BeginAddress && context.Rip < bridgeBase + bridgeFunction->EndAddress) sawHelper = true;
        if (context.Rip >= reinterpret_cast<DWORD64>(&RtEventMidFixture_Body)
            && context.Rip <= reinterpret_cast<DWORD64>(&RtEventMidFixture_SiteReturn)) sawFixture = true;
        if (context.Rip == reinterpret_cast<DWORD64>(&RtEventMidFixture_InvokeReturn)) { sawCaller = true; break; }
        DWORD64 imageBase = 0; const auto function = RtlLookupFunctionEntry(context.Rip, &imageBase, nullptr);
        const auto oldRsp = context.Rsp;
        if (function) {
            PVOID data = nullptr; DWORD64 establisher = 0;
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, context.Rip, function, &context, &data, &establisher, nullptr);
        } else { context.Rip = *reinterpret_cast<const DWORD64*>(context.Rsp); context.Rsp += 8; }
        CHECK(context.Rsp >= oldRsp && context.Rsp - oldRsp < 0x100000);
    }
    CHECK(sawHelper && sawFixture && sawCaller); InterlockedIncrement64(&unwindWalks);
}
LONG ObserveFault(EXCEPTION_POINTERS* exception)
{
    CHECK(exception->ExceptionRecord->ExceptionCode == callbackSehCode || exception->ExceptionRecord->ExceptionCode == 0xE06D7363);
    WalkUnwind(*exception->ContextRecord); return EXCEPTION_CONTINUE_SEARCH;
}
void ThrowCallback(unsigned mode)
{
    if (mode == 1) RaiseException(callbackSehCode, 0, 0, nullptr);
    else if (mode == 2) throw CallbackCpp{ payloadSeal };
}
void FaultWithUnwind(unsigned mode)
{
    __try { ThrowCallback(mode); }
    __except (ObserveFault(GetExceptionInformation())) { }
}
void Invoke(Work* work);
void __cdecl Observe(const Frame* frame, void* user)
{
    auto* work = Current(); CHECK(user == reinterpret_cast<void*>(payloadSeal));
    ++work->callbacks; InterlockedIncrement64(&callbacks);
    CHECK(frame && frame->version == 1 && frame->size == sizeof(Frame) && frame->reserved0 == 0 && frame->reserved1 == 0);
    if (!frame) return;
    CHECK(frame->returnPc == reinterpret_cast<std::uintptr_t>(&RtEventMidFixture_SiteReturn));
    CHECK(frame->entryRsp + 8 == frame->gpr[4] && (frame->entryRsp & 15) == 8);
    CHECK(frame->gpr[5] == reinterpret_cast<std::uintptr_t>(&work->list) && frame->gpr[6] == reinterpret_cast<std::uintptr_t>(work->event));
    const auto* before = reinterpret_cast<const Frame*>(frame->gpr[4] + 0x40); SameState(*before, *frame);
    CHECK(frame->gpr[0] == 0 && frame->gpr[3] == 0 && work->entered == 1 && work->continued == 0);
    CHECK(ReadStats().inFlight >= 1);
    CONTEXT context{}; RtlCaptureContext(&context); WalkUnwind(context);
    TestUnwindBoundaries(*frame);
    if (work->block) { CHECK(SetEvent(reached)); CHECK(WaitForSingleObject(resume, 5000) == WAIT_OBJECT_0); }
    if (InterlockedCompareExchange(&observerEnabled, 1, 1) == 0) return;
    if (work->recurse) {
        Work child{}; child.count = 1; Invoke(&child);
        CHECK(child.callbacks == 1 && child.rows == 1 && !child.caught && Current() == work && ReadStats().inFlight == 1);
    }
    RtEventMidFixture_ClobberVolatile();
    FaultWithUnwind(work->callbackMode);
}
LONG CatchOriginal(EXCEPTION_POINTERS* exception, Work* work)
{
    const auto record = exception->ExceptionRecord;
    if (record->ExceptionCode != originalSehCode) return EXCEPTION_CONTINUE_SEARCH;
    CHECK(record->NumberParameters == 2 && record->ExceptionInformation[0] == payloadSeal
        && record->ExceptionInformation[1] == reinterpret_cast<ULONG_PTR>(work));
    work->payload = record->NumberParameters ? static_cast<std::uint64_t>(record->ExceptionInformation[0]) : 0;
    return EXCEPTION_EXECUTE_HANDLER;
}
void InvokeWithSeh(Work* work)
{
    __try { RtEventMidFixture_InvokeAsm(work); }
    __except (CatchOriginal(GetExceptionInformation(), work)) {
        work->caught = true; work->exceptionCode = originalSehCode;
    }
}
void Invoke(Work* work)
{
    CHECK(workDepth < 16); if (workDepth >= 16) return;
    workStack[workDepth++] = work;
    try { InvokeWithSeh(work); }
    catch (const OriginalCpp& error) { work->caught = true; work->exceptionCode = 0xE06D7363; work->payload = error.payload; }
    CHECK(Current() == work); workStack[--workDepth] = nullptr;
}
void* AllocateRelay(const void* source)
{
    SYSTEM_INFO info{}; GetSystemInfo(&info);
    const auto granularity = static_cast<std::uintptr_t>(info.dwAllocationGranularity);
    const auto origin = reinterpret_cast<std::uintptr_t>(source) & ~(granularity - 1);
    for (std::uintptr_t index = 1; index != 4096; ++index) {
        const auto distance = index * granularity;
        for (const auto candidate : { origin >= distance ? origin - distance : 0, origin + distance }) {
            if (!candidate || candidate < reinterpret_cast<std::uintptr_t>(info.lpMinimumApplicationAddress)
                || candidate > reinterpret_cast<std::uintptr_t>(info.lpMaximumApplicationAddress) - 4096) continue;
            auto* result = VirtualAlloc(reinterpret_cast<void*>(candidate), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            if (result) return result;
        }
    }
    return nullptr;
}
void PublishFixtureCall()
{
    auto* site = reinterpret_cast<unsigned char*>(&RtEventMidFixture_Site);
    CHECK(&RtEventMidFixture_SiteReturn == &RtEventMidFixture_Site + 5);
    const unsigned char expected[]{ 0x0f,0x1f,0x44,0x00,0x00 };
    CHECK(std::memcmp(site, expected, sizeof(expected)) == 0);
    unsigned char surrounding[21]{}; std::memcpy(surrounding, site - 8, sizeof(surrounding));
    auto* relay = static_cast<unsigned char*>(AllocateRelay(site)); CHECK(relay != nullptr); if (!relay) return;
    const unsigned char jump[]{ 0xff,0x25,0,0,0,0 }; std::memcpy(relay, jump, sizeof(jump));
    std::memcpy(relay + 6, &helperEntry, sizeof(helperEntry));
    DWORD previous = 0; CHECK(VirtualProtect(relay, 4096, PAGE_EXECUTE_READ, &previous));
    CHECK(FlushInstructionCache(GetCurrentProcess(), relay, 14));
    const auto delta = reinterpret_cast<std::intptr_t>(relay) - reinterpret_cast<std::intptr_t>(site + 5);
    CHECK(delta >= INT32_MIN && delta <= INT32_MAX); const auto relative = static_cast<std::int32_t>(delta);
    CHECK(VirtualProtect(site, 5, PAGE_EXECUTE_READWRITE, &previous));
    site[0] = 0xe8; std::memcpy(site + 1, &relative, sizeof(relative));
    DWORD ignored = 0; CHECK(VirtualProtect(site, 5, previous, &ignored));
    CHECK(FlushInstructionCache(GetCurrentProcess(), site, 5));
    CHECK(site[0] == 0xe8 && reinterpret_cast<std::intptr_t>(site + 5) + relative == reinterpret_cast<std::intptr_t>(relay));
    CHECK(std::memcmp(site - 8, surrounding, 8) == 0 && std::memcmp(site + 5, surrounding + 13, 8) == 0);
    CHECK(std::memcmp(relay, jump, 6) == 0 && *reinterpret_cast<void**>(relay + 6) == helperEntry);
    // Relay, helper and published code remain process-owned. No unpatch or
    // concurrent live-code publication is tested by this finite executable.
}
DWORD WINAPI Worker(void* data) { Invoke(static_cast<Work*>(data)); return 0; }
}
extern "C" void __cdecl RtEventMidFixture_Prepare(void* identity, std::uintptr_t siteRsp, Frame* before, Frame* after)
{
    auto* work = Current(); CHECK(identity == work); ++work->entered; work->thread = GetCurrentThreadId();
    CHECK(work->count <= 8); for (unsigned index = 0; index != 8; ++index) work->items[index] = reinterpret_cast<void*>(0x1111222233330000ULL + index * 16);
    work->list = { nullptr, nullptr, work->items, static_cast<std::int32_t>(work->count), 7 };
    *before = {}; *after = {};
    before->version = after->version = 1; before->size = after->size = sizeof(Frame);
    before->entryRsp = after->entryRsp = siteRsp - 8;
    before->returnPc = after->returnPc = reinterpret_cast<std::uintptr_t>(&RtEventMidFixture_SiteReturn);
    before->gpr[5] = reinterpret_cast<std::uintptr_t>(&work->list); before->gpr[6] = reinterpret_cast<std::uintptr_t>(work->event);
}
extern "C" void __cdecl RtEventMidFixture_Complete(void* identity, const Frame* before, const Frame* after, const std::uint64_t* canaries)
{
    auto* work = Current(); CHECK(identity == work); ++work->continued;
    SameState(*before, *after); work->before = *before; work->after = *after;
    CHECK(canaries[0] == stackSeal && canaries[1] == stackSeal && canaries[2] == stackSeal);
    CHECK(work->callbacks == (work->expectCallback ? 1u : 0u));
    for (std::int32_t index = 0; index < work->list.count; ++index) {
        CHECK(index < 8); if (index >= 8) break;
        ++work->rows; work->rowSeal ^= reinterpret_cast<std::uintptr_t>(work->list.items[index]);
        if (index == 0 && work->mutate) { work->list.count = 1; ++work->list.version; }
    }
    if (work->originalMode == 1) {
        work->payload = payloadSeal;
        const ULONG_PTR payload[]{ static_cast<ULONG_PTR>(payloadSeal), reinterpret_cast<ULONG_PTR>(work) };
        RaiseException(originalSehCode, 0, 2, payload);
    }
    else if (work->originalMode == 2) throw OriginalCpp{ payloadSeal };
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) { std::fprintf(stderr, "Usage: EventMidContext.Fixture.exe existing-helper.dll\n"); return 2; }
    helper = LoadLibraryW(argv[1]); CHECK(helper != nullptr); if (!helper) return 2;
    const auto entry = Symbol<GetPointer>("RizEventMidContext_GetEntry");
    const auto limit = Symbol<GetScalar>("RizEventMidContext_GetLimit"), frameSize = Symbol<GetScalar>("RizEventMidContext_GetFrameSize");
    configure = Symbol<Configure>("RizEventMidContext_Configure"); getStats = Symbol<GetStats>("RizEventMidContext_GetStats");
    if (!entry || !limit || !frameSize || !configure || !getStats) return 2;
    helperEntry = entry(); CHECK(helperEntry && limit() == 32 && frameSize() == sizeof(Frame));
    for (unsigned row = 0; row != 16; ++row) for (unsigned column = 0; column != 16; ++column)
        RtEventMidFixture_XmmInput[row][column] = static_cast<unsigned char>(row * 17 + column * 7 + 3);
    reached = CreateEvent(nullptr, TRUE, FALSE, nullptr); resume = CreateEvent(nullptr, TRUE, FALSE, nullptr); CHECK(reached && resume);
    Work baseline{}; baseline.count = 2; baseline.expectCallback = false; Invoke(&baseline);
    CHECK(baseline.entered == 1 && baseline.continued == 1 && baseline.callbacks == 0 && baseline.rows == 2);
    Config configuration{ 1, sizeof(configuration), &RtEventMidFixture_SiteReturn, Observe, reinterpret_cast<void*>(payloadSeal), 0 };
    auto invalid = configuration; invalid.version = 2; CHECK(configure(slot, &invalid) == 1);
    invalid = configuration; invalid.reserved = 1; CHECK(configure(slot, &invalid) == 1);
    CHECK(configure(slot, &configuration) == 0 && configure(slot, &configuration) == 2);
    CHECK(configure(1, &configuration) == 3); // Duplicate ReturnPc does not claim another slot.
    configuration = {};
    Work configured{}; configured.expectCallback = false; Invoke(&configured); CHECK(configured.callbacks == 0 && ReadStats().entered == 0);
    PublishFixtureCall();
    std::uint64_t expectedFaults = 0;
    for (const auto count : { 0u,1u,8u }) {
        Work work{}; work.count = count; Invoke(&work);
        CHECK(work.entered == 1 && work.continued == 1 && work.callbacks == 1 && work.rows == count && !work.caught);
    }
    Work mutableList{}; mutableList.count = 8; mutableList.mutate = 1; Invoke(&mutableList);
    CHECK(mutableList.rows == 1 && mutableList.list.count == 1 && mutableList.list.version == 8);
    Work recursive{}; recursive.count = 2; recursive.recurse = true; Invoke(&recursive);
    CHECK(recursive.callbacks == 1 && recursive.rows == 2 && maxInFlight >= 2);
    for (const auto callbackMode : { 0u,1u,2u }) for (const auto originalMode : { 0u,1u,2u }) {
        Work work{}; work.count = 2; work.callbackMode = callbackMode; work.originalMode = originalMode; Invoke(&work);
        CHECK(work.entered == 1 && work.continued == 1 && work.callbacks == 1 && work.rows == 2);
        CHECK(work.caught == (originalMode != 0));
        if (originalMode) CHECK(work.exceptionCode == (originalMode == 1 ? originalSehCode : 0xE06D7363) && work.payload == payloadSeal);
        if (callbackMode) ++expectedFaults;
        const auto activity = ReadStats(); CHECK(activity.entered == activity.exited && activity.inFlight == 0 && activity.faults == expectedFaults);
    }
    Work worker{}; worker.count = 1; worker.block = true;
    const auto thread = CreateThread(nullptr, 0, Worker, &worker, 0, nullptr); CHECK(thread != nullptr);
    if (thread) {
        CHECK(WaitForSingleObject(reached, 5000) == WAIT_OBJECT_0);
        CHECK(ReadStats().inFlight == 1); InterlockedExchange(&observerEnabled, 0);
        CHECK(ReadStats().inFlight == 1); CHECK(SetEvent(resume));
        CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
        CHECK(worker.thread != GetCurrentThreadId() && worker.continued == 1 && worker.callbacks == 1 && worker.rows == 1 && ReadStats().inFlight == 0);
    }
    const auto finalStats = ReadStats();
    CHECK(finalStats.entered == finalStats.exited && finalStats.inFlight == 0 && finalStats.faults == 6 && workDepth == 0);
    CHECK(unwindWalks > 0 && virtualUnwinds >= 8);
    CloseHandle(reached); CloseHandle(resume);
    std::printf("{\"kind\":\"native-event-mid-full-state-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,\"entered\":%llu,\"exited\":%llu,\"inFlight\":%llu,\"faults\":%llu,\"callbacks\":%lld,\"unwindWalks\":%lld,\"virtualUnwinds\":%lld,\"allGpr\":true,\"allXmm\":true,\"rflags\":true,\"mxcsr\":true,\"fpcsr\":true,\"stackCanaries\":true,\"exactFiveByteCall\":true,\"nearTailRelay\":true,\"fixtureOnlyPatch\":true,\"nativeParentVerified\":false,\"gameRuntimeVerified\":false}\n",
        failures ? "false" : "true", checks, failures, finalStats.entered, finalStats.exited, finalStats.inFlight, finalStats.faults, callbacks, unwindWalks, virtualUnwinds);
    return failures ? 1 : 0;
}
