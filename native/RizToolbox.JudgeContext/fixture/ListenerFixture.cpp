#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <initializer_list>
#include "ListenerFixture.h"
#include "../BootstrapVoidContext.h"
#include "../ListenerContext.h"

// Finite offline executable. Only this EXE's own targets are patched, before
// their first invocation. Dynamic post stubs retain the actual parent unwind
// chain; no game image, IL2CPP API, managed callback or resource owner is used.
namespace {
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using ParentInfoFn = decltype(&RizVoidBootstrap_GetInfo);
using ParentStatsFn = decltype(&RizVoidContext_GetStats);
using ParentAttachFn = decltype(&RizVoidBootstrap_Attach);
using ListenerInfoFn = decltype(&RizListenerContext_GetInfo);
using ListenerAttachFn = decltype(&RizListenerContext_Attach);
constexpr std::uint32_t Slots[2]{ 29, 28 };
constexpr DWORD OriginalSeh = 0xE04C534F, ObserverSeh = 0xE04C5343;
constexpr std::uint64_t Payload = 0xFEDCBA9876543210ULL, StackSeal = 0xAABBCCDDEEFF1122ULL;
constexpr std::uintptr_t ParentMask = 0x12345678ABCDEF00ULL;
constexpr std::uintptr_t ChildMask = 0xF1234567ABCDEF00ULL;
void* targets[2]{ reinterpret_cast<void*>(&RtLfTarget0), reinterpret_cast<void*>(&RtLfTarget1) };
void* sites[2][3]{ { &RtLfSite00, &RtLfSite01, &RtLfSite02 },
    { &RtLfSite10, &RtLfSite11, &RtLfSite12 } };
void* posts[2][3]{ { &RtLfPost00, &RtLfPost01, &RtLfPost02 },
    { &RtLfPost10, &RtLfPost11, &RtLfPost12 } };
void* loops[2]{ &RtLfLoop0, &RtLfLoop1 };
void* mids[2]{ &RtLfMid0, &RtLfMid1 };
void* midReturns[2]{ &RtLfMidReturn0, &RtLfMidReturn1 };
void* originals[2]{};
std::uint64_t epochs[2]{};
std::uint64_t laneEpochs[2]{};
HMODULE helper{};
ParentInfoFn parentInfo{};
ParentStatsFn parentStats{};
ParentAttachFn parentAttach{};
ListenerInfoFn listenerInfo{};
ListenerAttachFn listenerAttach{};
volatile LONG64 checks{}, failures{}, walks{}, partialUnwinds{}, originalCalls{}, actualReturnChecks{}, replayChecks{}, observerFaults{}, oldCalls{};
volatile LONG64 codeWrites{}, lateCalls{}, normalCases{}, originalSehCases{}, originalCppCases{}, faultCases{}, nestedCases{}, workerCases{};
volatile LONG64 preStateChecks{}, postStateChecks{};
struct OriginalCpp { std::uint64_t payload; const void* work; std::uint32_t index; };
struct ObserverCpp { std::uint64_t payload; };
struct DynamicBlock {
    unsigned char* page{};
    RUNTIME_FUNCTION* table{};
    RUNTIME_FUNCTION parent{};
    DWORD64 imageBase{};
    unsigned char* preRelay{};
    unsigned char* midRelay{};
    unsigned char* postStub[3]{};
    const void* postReturn[3]{};
    std::size_t sourcePostSize[3]{ 6, 9, 9 };
    unsigned char originalPre[3][6]{}, originalPost[3][9]{};
};
DynamicBlock blocks[2];
struct VTable { unsigned char padding[0x178]; void* target; const void* method; };
static_assert(offsetof(VTable, target) == 0x178 && offsetof(VTable, method) == 0x180);
VTable vtable{};
void Check(bool condition, const char* expression, int line)
{
    InterlockedIncrement64(&checks);
    if (!condition) { InterlockedIncrement64(&failures); std::fprintf(stderr, "FAILED listener fixture line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto symbol = GetProcAddress(module, name); CHECK(symbol);
    T result{}; static_assert(sizeof(result) == sizeof(symbol)); std::memcpy(&result, &symbol, sizeof(result)); return result;
}
HANDLE Event() { const auto result = CreateEventW(nullptr, TRUE, FALSE, nullptr); CHECK(result); return result; }
}

struct ListenerFixtureWork {
    ListenerFixturePrefix prefix{};
    std::uint64_t args[2]{ 0x1111222233334444ULL, 0x5555666677778888ULL };
    ListenerFixtureList list{};
    ListenerFixtureInvokable* items[3]{};
    ListenerFixtureInvokable rows[3]{};
    RizEventMidFrameV1 preFrames[3]{}, postFrames[3]{};
    std::uint64_t childTokens[3]{}, childSequences[3]{}, parentSequence{};
    const void* parentMethod{};
    ListenerFixtureWork* previous{};
    HANDLE reached{}, resume{};
    unsigned target{}, originalMode{}, originalFaultIndex{}, observerFaultStage{}, observerFaultKind{}, recurseTarget{};
    bool expectedObserver{true}, expectedListener{true}, hold{}, recurse{}, returned{}, caught{};
    DWORD thread{}, exceptionCode{};
    std::uint64_t exceptionPayload{};
    unsigned parentEnter{}, parentMid{}, parentExit{}, pre[3]{}, post[3]{}, invoked[3]{}, childAbnormal[3]{}, replayed{}, after{}, abnormal{};
};
static_assert(offsetof(ListenerFixtureWork, prefix) == 0 && offsetof(ListenerFixtureWork, args) == 24);

namespace {
thread_local ListenerFixtureWork* current{};
void Init(ListenerFixtureWork* work, unsigned target, unsigned count = 3, bool observer = true)
{
    *work = {}; work->target = target; work->recurseTarget = target;
    work->expectedObserver = observer; work->expectedListener = observer;
    work->parentMethod = reinterpret_cast<const void*>(0x123456789ABCD000ULL + target * 0x100);
    work->prefix.list = &work->list;
    work->list.items = work->items; work->list.count = static_cast<std::int32_t>(count); work->list.version = 17;
    for (unsigned i = 0; i != 3; ++i) {
        work->items[i] = &work->rows[i]; work->rows[i].klass = &vtable;
        work->rows[i].owner = work; work->rows[i].index = i; work->rows[i].path = i;
    }
    work->reached = Event(); work->resume = Event();
}
void CloseEvents(ListenerFixtureWork* work) { CloseHandle(work->reached); CloseHandle(work->resume); }
void Join(HANDLE worker)
{
    CHECK(worker); if (!worker) return;
    const auto joined = WaitForSingleObject(worker, 10000); CHECK(joined == WAIT_OBJECT_0);
    if (joined != WAIT_OBJECT_0) ExitProcess(3); CloseHandle(worker);
}
void Fault(ListenerFixtureWork* work, unsigned stage)
{
    if (work->observerFaultStage != stage) return;
    InterlockedIncrement64(&observerFaults);
    if (work->observerFaultKind == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
    else throw ObserverCpp{Payload};
}
std::uintptr_t __cdecl ParentEnter(const RizVoidFrameV1* frame, void* user)
{
    auto* work = static_cast<ListenerFixtureWork*>(frame->self);
    CHECK(user == &targets[work->target] && work->expectedObserver && ++work->parentEnter == 1);
    CHECK(frame->abiVersion == 1 && frame->byteSize == sizeof(*frame) && frame->method == work->parentMethod
        && frame->caller == &RtLfCallerReturn);
    work->previous = current; current = work; work->thread = GetCurrentThreadId();
    return reinterpret_cast<std::uintptr_t>(work) ^ ParentMask;
}
void __cdecl ParentMid(const RizEventMidFrameV1* frame, void* user)
{
    auto* work = current; CHECK(work); if (!work) return;
    CHECK(user == &targets[work->target] && work->expectedObserver && ++work->parentMid == 1);
    CHECK(frame->returnPc == reinterpret_cast<std::uint64_t>(midReturns[work->target])
        && frame->gpr[RizEventMidRsi] == reinterpret_cast<std::uint64_t>(work)
        && frame->gpr[RizEventMidRbp] == reinterpret_cast<std::uint64_t>(&work->list));
}
void __cdecl ParentExit(const RizVoidFrameV1* frame, std::uintptr_t token, std::uint32_t flags, void* user)
{
    auto* work = static_cast<ListenerFixtureWork*>(frame->self);
    CHECK(current == work && user == &targets[work->target] && ++work->parentExit == 1);
    CHECK(token == (reinterpret_cast<std::uintptr_t>(work) ^ ParentMask)
        && flags == (work->originalMode ? RizVoidAbnormal : RizVoidReturned));
    if (work->originalMode) ++work->abnormal;
    current = work->previous;
}
void AttachParent(unsigned target)
{
    const RizVoidBootstrapObserverV1 observer{ 1, sizeof(observer), ParentEnter, ParentExit, ParentMid, &targets[target] };
    CHECK(parentAttach(Slots[target], epochs[target], &observer) == 0);
}
void* AllocateNearAbove(DWORD64 imageBase, const void* site)
{
    SYSTEM_INFO info{}; GetSystemInfo(&info); const auto granularity = static_cast<std::uintptr_t>(info.dwAllocationGranularity);
    for (std::uintptr_t offset = granularity; offset < 0x70000000ULL; offset += granularity) {
        const auto candidate = static_cast<std::uintptr_t>(imageBase) + offset;
        const auto difference = static_cast<std::intptr_t>(candidate) - reinterpret_cast<std::intptr_t>(site);
        if (difference > 0x70000000LL || difference < -0x70000000LL) continue;
        const auto allocated = VirtualAlloc(reinterpret_cast<void*>(candidate), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (allocated) return allocated;
    }
    return nullptr;
}
void Relative(unsigned char* instruction, unsigned char opcode, const void* destination)
{
    const auto difference = reinterpret_cast<std::intptr_t>(destination) - reinterpret_cast<std::intptr_t>(instruction + 5);
    CHECK(difference >= (std::numeric_limits<std::int32_t>::min)() && difference <= (std::numeric_limits<std::int32_t>::max)());
    const auto displacement = static_cast<std::int32_t>(difference);
    instruction[0] = opcode; std::memcpy(instruction + 1, &displacement, sizeof(displacement));
}
void Tail(unsigned char* destination, const void* entry)
{
    const unsigned char code[]{ 0xff, 0x25, 0, 0, 0, 0 };
    std::memcpy(destination, code, sizeof(code)); std::memcpy(destination + 6, &entry, sizeof(entry));
}
void MakeDynamic(unsigned target, const void* preEntry, const void* postEntry, const void* midEntry)
{
    auto& block = blocks[target]; DWORD64 imageBase{};
    const auto* parent = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(targets[target]), &imageBase, nullptr);
    CHECK(parent && imageBase == reinterpret_cast<DWORD64>(GetModuleHandleW(nullptr))); if (!parent) ExitProcess(2);
    block.parent = *parent; block.imageBase = imageBase;
    block.page = static_cast<unsigned char*>(AllocateNearAbove(imageBase, targets[target])); CHECK(block.page); if (!block.page) ExitProcess(2);
    std::memset(block.page, 0xcc, 4096);
    block.table = reinterpret_cast<RUNTIME_FUNCTION*>(block.page + 0x380);
    block.preRelay = block.page + 0x500; Tail(block.preRelay, preEntry);
    block.midRelay = block.page + 0x520; Tail(block.midRelay, midEntry);
    auto* postRelay = block.page + 0x540; Tail(postRelay, postEntry);
    for (unsigned site = 0; site != 3; ++site) {
        auto* stub = block.page + site * 0x100; block.postStub[site] = stub;
        Relative(stub, 0xe8, postRelay); block.postReturn[site] = stub + 5;
        const unsigned char replay[]{ 0xff, 0xc3, 0x8b, 0xc3 }; std::memcpy(stub + 5, replay, sizeof(replay));
        Relative(stub + 9, 0xe9, loops[target]);
        auto* unwind = block.page + site * 0x100 + 0x40;
        // Version1, UNW_FLAG_CHAININFO, zero prolog/codes/frame. The CALL's
        // own return slot belongs to the helper's native frame. At stub RIP,
        // RSP already names the original parent's body, so chain its real RF.
        unwind[0] = 0x21; unwind[1] = 0; unwind[2] = 0; unwind[3] = 0;
        std::memcpy(unwind + 4, parent, sizeof(*parent));
        const auto start = reinterpret_cast<DWORD64>(stub) - imageBase;
        const auto data = reinterpret_cast<DWORD64>(unwind) - imageBase;
        CHECK(start <= MAXDWORD && data <= MAXDWORD);
        block.table[site] = { static_cast<DWORD>(start), static_cast<DWORD>(start + 14), static_cast<DWORD>(data) };
        std::memcpy(block.originalPre[site], sites[target][site], site == 2 ? 6 : 5);
        std::memcpy(block.originalPost[site], posts[target][site], block.sourcePostSize[site]);
    }
    DWORD old{}; CHECK(VirtualProtect(block.page, 4096, PAGE_EXECUTE_READ, &old));
    CHECK(FlushInstructionCache(GetCurrentProcess(), block.page, 4096));
    CHECK(RtlAddFunctionTable(block.table, 3, imageBase));
    for (unsigned site = 0; site != 3; ++site) {
        DWORD64 registeredBase{};
        const auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(block.postStub[site]), &registeredBase, nullptr);
        CHECK(actual && registeredBase == imageBase && std::memcmp(actual, &block.table[site], sizeof(*actual)) == 0);
        if (actual) CHECK(std::memcmp(reinterpret_cast<const void*>(registeredBase + actual->UnwindData + 4), parent, sizeof(*parent)) == 0);
    }
}
void WriteCode(void* destination, const unsigned char* bytes, std::size_t count)
{
    DWORD old{}; CHECK(VirtualProtect(destination, count, PAGE_EXECUTE_READWRITE, &old));
    std::memcpy(destination, bytes, count); DWORD ignored{};
    CHECK(VirtualProtect(destination, count, old, &ignored));
    CHECK(FlushInstructionCache(GetCurrentProcess(), destination, count));
    MEMORY_BASIC_INFORMATION memory{};
    CHECK(VirtualQuery(destination, &memory, sizeof(memory)) == sizeof(memory) && memory.Protect == old);
    CHECK(std::memcmp(destination, bytes, count) == 0);
    InterlockedIncrement64(&codeWrites);
}
void PublishSites(unsigned target)
{
    auto& block = blocks[target];
    for (unsigned site = 0; site != 3; ++site) {
        const auto* source = static_cast<const unsigned char*>(sites[target][site]);
        CHECK(source + (site == 2 ? 6 : 5) == posts[target][site]);
        CHECK(site == 2 ? (source[0] == 0xff && source[1] == 0x90 && source[2] == 0x78 && source[3] == 1 && source[4] == 0 && source[5] == 0) : source[0] == 0xe8);
        const auto* continuation = static_cast<const unsigned char*>(posts[target][site]);
        CHECK(continuation[0] == 0xff && continuation[1] == 0xc3 && continuation[2] == 0x8b && continuation[3] == 0xc3
            && continuation[4] == (site == 0 ? 0xeb : 0xe9));
        unsigned char call[6]{0x90}; call[site == 2 ? 1 : 0] = 0xe8;
        const auto* callPc = source + (site == 2 ? 1 : 0);
        const auto delta = reinterpret_cast<std::intptr_t>(block.preRelay) - reinterpret_cast<std::intptr_t>(callPc + 5);
        CHECK(delta >= (std::numeric_limits<std::int32_t>::min)() && delta <= (std::numeric_limits<std::int32_t>::max)());
        const auto displacement = static_cast<std::int32_t>(delta); std::memcpy(call + (site == 2 ? 2 : 1), &displacement, 4);
        WriteCode(sites[target][site], call, site == 2 ? 6 : 5);
        unsigned char jump[9]{}; std::memset(jump, 0x90, sizeof(jump)); jump[0] = 0xe9;
        const auto postDelta = reinterpret_cast<std::intptr_t>(block.postStub[site]) - reinterpret_cast<std::intptr_t>(continuation + 5);
        CHECK(postDelta >= (std::numeric_limits<std::int32_t>::min)() && postDelta <= (std::numeric_limits<std::int32_t>::max)());
        const auto postDisplacement = static_cast<std::int32_t>(postDelta); std::memcpy(jump + 1, &postDisplacement, 4);
        WriteCode(posts[target][site], jump, block.sourcePostSize[site]);
    }
    unsigned char midCall[5]{0xe8};
    const auto midDelta = reinterpret_cast<std::intptr_t>(block.midRelay) - reinterpret_cast<std::intptr_t>(midReturns[target]);
    CHECK(midDelta >= (std::numeric_limits<std::int32_t>::min)() && midDelta <= (std::numeric_limits<std::int32_t>::max)());
    const auto midDisplacement = static_cast<std::int32_t>(midDelta); std::memcpy(midCall + 1, &midDisplacement, 4);
    const unsigned char nop[]{0x0f,0x1f,0x44,0,0}; CHECK(std::memcmp(mids[target], nop, sizeof(nop)) == 0);
    WriteCode(mids[target], midCall, sizeof(midCall));
}
void FrameContext(CONTEXT& context, const RizEventMidFrameV1& frame)
{
    context = {}; context.ContextFlags = CONTEXT_ALL;
    context.Rax = frame.gpr[0]; context.Rcx = frame.gpr[1]; context.Rdx = frame.gpr[2]; context.Rbx = frame.gpr[3];
    context.Rsp = frame.gpr[4]; context.Rbp = frame.gpr[5]; context.Rsi = frame.gpr[6]; context.Rdi = frame.gpr[7];
    context.R8 = frame.gpr[8]; context.R9 = frame.gpr[9]; context.R10 = frame.gpr[10]; context.R11 = frame.gpr[11];
    context.R12 = frame.gpr[12]; context.R13 = frame.gpr[13]; context.R14 = frame.gpr[14]; context.R15 = frame.gpr[15];
    context.EFlags = static_cast<DWORD>(frame.rflags); context.MxCsr = frame.mxcsr;
    std::memcpy(&context.Xmm0, frame.xmm, sizeof(frame.xmm));
}
void Walk(CONTEXT context, unsigned target, unsigned site, bool post)
{
    bool sawHelper = false, sawStub = false, sawCaller = false;
    for (unsigned step = 0; step != 64 && context.Rip && context.Rsp; ++step) {
        MEMORY_BASIC_INFORMATION memory{}; CHECK(VirtualQuery(reinterpret_cast<const void*>(context.Rip), &memory, sizeof(memory)) == sizeof(memory));
        if (memory.AllocationBase == helper) sawHelper = true;
        if (context.Rip >= reinterpret_cast<DWORD64>(blocks[target].postStub[site])
            && context.Rip < reinterpret_cast<DWORD64>(blocks[target].postStub[site]) + 14) sawStub = true;
        if (context.Rip == reinterpret_cast<DWORD64>(&RtLfCallerReturn)) { sawCaller = true; break; }
        DWORD64 imageBase{}; auto* function = RtlLookupFunctionEntry(context.Rip, &imageBase, nullptr);
        const auto previous = context.Rsp;
        if (function) { PVOID data{}; DWORD64 establisher{}; RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, context.Rip, function, &context, &data, &establisher, nullptr); }
        else { context.Rip = *reinterpret_cast<const DWORD64*>(context.Rsp); context.Rsp += 8; }
        CHECK(context.Rsp >= previous && context.Rsp - previous < 0x100000);
    }
    CHECK(sawHelper && sawCaller && (!post || sawStub)); InterlockedIncrement64(&walks);
}
void PostVirtualUnwind(const RizEventMidFrameV1& frame, unsigned target, unsigned site)
{
    const auto& block = blocks[target];
    const auto* body = reinterpret_cast<const std::uint64_t*>(frame.gpr[RizEventMidRsp]);
    // These are the FOUR actual instruction boundaries. +13 is inside E9.
    // The original is called by the permanent native transport, so one unwind
    // reaches its real saved caller (body[13]), rather than skipping directly
    // to the EXE's outer caller. Walk() separately proves the complete chain.
    for (const unsigned offset : { 0u, 5u, 7u, 9u }) {
        CONTEXT context{}; FrameContext(context, frame);
        context.Rip = reinterpret_cast<DWORD64>(block.postStub[site] + offset);
        context.Rsp = frame.gpr[RizEventMidRsp];
        if (offset >= 7) context.Rbx = static_cast<std::uint32_t>(context.Rbx + 1);
        if (offset >= 9) context.Rax = static_cast<std::uint32_t>(context.Rbx);
        PVOID data{}; DWORD64 establisher{};
        RtlVirtualUnwind(UNW_FLAG_NHANDLER, block.imageBase, context.Rip, &block.table[site], &context, &data, &establisher, nullptr);
        if (context.Rip != body[13] || context.Rsp != frame.gpr[RizEventMidRsp] + 0x70)
            std::fprintf(stderr, "post unwind target=%u site=%u boundary=%u RIP=%llx expected=%llx RSP=%llx expected=%llx\n",
                target, site, offset, static_cast<unsigned long long>(context.Rip), static_cast<unsigned long long>(body[13]),
                static_cast<unsigned long long>(context.Rsp), static_cast<unsigned long long>(frame.gpr[RizEventMidRsp] + 0x70));
        CHECK(context.Rip == body[13] && context.Rsp == frame.gpr[RizEventMidRsp] + 0x70);
        CHECK(context.R14 == body[8] && context.Rdi == body[9] && context.Rsi == body[10]
            && context.Rbp == body[11] && context.Rbx == body[12]);
        CHECK(context.R12 == frame.gpr[RizEventMidR12] && context.R13 == frame.gpr[RizEventMidR13]
            && context.R15 == frame.gpr[RizEventMidR15]);
        CHECK(std::memcmp(&context.Xmm6, frame.xmm + 6, 10 * sizeof(frame.xmm[0])) == 0);
        InterlockedIncrement64(&partialUnwinds);
    }
}
void Execute(ListenerFixtureWork* work);
LONG CatchSeh(EXCEPTION_POINTERS* pointers, ListenerFixtureWork* work)
{
    const auto* record = pointers->ExceptionRecord;
    if (record->ExceptionCode == 0xE06D7363) return EXCEPTION_CONTINUE_SEARCH;
    CHECK(record->ExceptionCode == OriginalSeh && record->NumberParameters == 3
        && record->ExceptionInformation[0] == Payload && record->ExceptionInformation[1] == reinterpret_cast<ULONG_PTR>(work)
        && record->ExceptionInformation[2] == work->originalFaultIndex);
    work->caught = true; work->exceptionCode = record->ExceptionCode; work->exceptionPayload = record->ExceptionInformation[0];
    return EXCEPTION_EXECUTE_HANDLER;
}
void ExecuteSeh(ListenerFixtureWork* work)
{
    __try { RtLfCall(targets[work->target], work, work->parentMethod); work->returned = true; }
    __except (CatchSeh(GetExceptionInformation(), work)) { }
}
void Execute(ListenerFixtureWork* work)
{
    const auto* previous = current;
    try { ExecuteSeh(work); }
    catch (const OriginalCpp& error) {
        CHECK(error.payload == Payload && error.work == work && error.index == work->originalFaultIndex);
        work->caught = true; work->exceptionCode = 0xE06D7363; work->exceptionPayload = error.payload;
    }
    CHECK(current == previous && work->returned == (work->originalMode == 0) && work->caught == (work->originalMode != 0));
    if (work->expectedObserver) CHECK(work->parentEnter == 1 && work->parentMid == 1 && work->parentExit == 1);
    else { CHECK(work->parentEnter == 0 && work->parentMid == 0 && work->parentExit == 0); InterlockedIncrement64(&oldCalls); }
}
DWORD WINAPI Worker(void* data) { Execute(static_cast<ListenerFixtureWork*>(data)); return 0; }
void CalleeBody(ListenerFixtureInvokable* row, const void* argument, const void* method, const void* caller)
{
    auto* work = row->owner; const auto index = row->index;
    CHECK(index < 3 && caller == posts[work->target][index] && row->actualReturn == caller);
    InterlockedIncrement64(&actualReturnChecks); InterlockedIncrement64(&originalCalls);
    CHECK(++work->invoked[index] == 1);
    CHECK(row->actualGpr[RizEventMidRsi] == reinterpret_cast<std::uint64_t>(work)
        && row->actualGpr[RizEventMidRbp] == reinterpret_cast<std::uint64_t>(&work->list)
        && row->actualGpr[RizEventMidRdi] == reinterpret_cast<std::uint64_t>(row)
        && row->actualGpr[RizEventMidRbx] == index);
    CHECK(index == 2 ? (argument == work->args && method == vtable.method)
        : (argument == nullptr && method == reinterpret_cast<const void*>(0x88888888789ABCDEULL)));
    CHECK(row->actualGpr[RizEventMidR9] == 0x9999999989ABCDEFULL);
    if (work->expectedListener) {
        CHECK(work->pre[index] == 1);
        const auto& frame = work->preFrames[index];
        CHECK(frame.returnPc == reinterpret_cast<std::uint64_t>(caller) && frame.rflags == row->actualFlags
            && frame.mxcsr == row->actualMxcsr && frame.fpcsr == row->actualFpcsr);
        for (unsigned reg = 0; reg != 16; ++reg) CHECK(frame.gpr[reg] == row->actualGpr[reg] + (reg == RizEventMidRsp ? 8 : 0));
        CHECK(std::memcmp(frame.xmm, row->actualXmm, sizeof(frame.xmm)) == 0);
        InterlockedIncrement64(&preStateChecks);
    }
    if (work->recurse && index == 1) {
        ListenerFixtureWork nested{}; Init(&nested, work->recurseTarget, 1); Execute(&nested); CloseEvents(&nested); CHECK(current == work);
        InterlockedIncrement64(&nestedCases);
    }
    if (work->originalMode && index == work->originalFaultIndex) {
        if (work->originalMode == 1) {
            const ULONG_PTR values[]{static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(work), index};
            RaiseException(OriginalSeh, 0, 3, values);
        } else throw OriginalCpp{Payload, work, index};
    }
}
}

extern "C" void __cdecl RtLfBefore(void* self, const void* method)
{
    auto* work = static_cast<ListenerFixtureWork*>(self); CHECK(method == work->parentMethod);
    if (work->hold) { CHECK(SetEvent(work->reached)); CHECK(WaitForSingleObject(work->resume, 10000) == WAIT_OBJECT_0); }
}
extern "C" void __cdecl RtLfLoopCheck(void* self, std::uint32_t index, std::uint64_t value, std::uint64_t flags)
{
    auto* work = static_cast<ListenerFixtureWork*>(self);
    if (index) {
        CHECK(index <= 3 && value == index && work->invoked[index - 1] == 1);
        if (work->expectedListener) {
            const auto& row = work->rows[index - 1]; const auto& frame = work->postFrames[index - 1];
            CHECK(work->post[index - 1] == 1 && (flags & 1) == (frame.rflags & 1));
            for (unsigned reg = 0; reg != 16; ++reg) {
                const auto expected = reg == RizEventMidRax || reg == RizEventMidRbx ? index : frame.gpr[reg];
                CHECK(row.loopGpr[reg] == expected);
            }
            CHECK(std::memcmp(row.loopXmm, frame.xmm, sizeof(frame.xmm)) == 0
                && row.loopMxcsr == frame.mxcsr && row.loopFpcsr == frame.fpcsr);
            // INC 0/1/2 -> 1/2/3 changes OF/SF/ZF/AF/PF; CF and all other
            // flags are preserved. Only 3 has even parity in these three cases.
            const auto expectedFlags = (frame.rflags & ~0x8d4ULL) | (index == 3 ? 4ULL : 0ULL);
            CHECK(flags == expectedFlags);
            InterlockedIncrement64(&postStateChecks);
        }
        ++work->replayed; InterlockedIncrement64(&replayChecks);
    }
}
extern "C" void __cdecl RtLfAfter(void* self, const void* method, std::uint64_t first, std::uint64_t second)
{
    auto* work = static_cast<ListenerFixtureWork*>(self);
    CHECK(method == work->parentMethod && first == StackSeal && second == StackSeal
        && work->replayed == static_cast<unsigned>(work->list.count) && ++work->after == 1);
}
extern "C" __declspec(noinline) void __cdecl RtLfDirectBody(ListenerFixtureInvokable* row, const void* argument, const void* method)
{ const auto* actualCaller = _ReturnAddress(); CalleeBody(row, argument, method, actualCaller); }
extern "C" __declspec(noinline) void __cdecl RtLfVirtualBody(ListenerFixtureInvokable* row, const void* argument, const void* method)
{ const auto* actualCaller = _ReturnAddress(); CalleeBody(row, argument, method, actualCaller); }

namespace {
void ValidateChild(const RizListenerFrameV1* frame, ListenerFixtureWork* work, void* user)
{
    CHECK(frame && work); if (!frame || !work) return;
    const auto site = frame->site;
    CHECK(site < 3 && current == work && user == &targets[work->target]); if (site >= 3) return;
    CHECK(work->expectedListener && frame->abiVersion == 1 && frame->byteSize == sizeof(*frame)
        && frame->slot == Slots[work->target] && frame->epoch == laneEpochs[work->target]
        && frame->osThreadId == GetCurrentThreadId() && frame->osThreadId == work->thread
        && frame->reserved0 == 0 && frame->reserved1 == 0);
    CHECK(frame->event == work && frame->list == &work->list && frame->index == site
        && frame->invokable == &work->rows[site] && frame->argRcx == frame->invokable
        && frame->sourceReturnPc == posts[work->target][site]
        && frame->actualTarget == (site == 2 ? reinterpret_cast<const void*>(&RtLfVirtual)
            : reinterpret_cast<const void*>(&RtLfDirect)));
    CHECK(frame->parentManagedToken == (reinterpret_cast<std::uintptr_t>(work) ^ ParentMask)
        && frame->parentSequence != 0 && frame->childSequence != 0);
    CHECK(frame->argRdx == (site == 2 ? work->args : nullptr)
        && frame->argR8 == (site == 2 ? vtable.method : reinterpret_cast<const void*>(0x88888888789ABCDEULL))
        && frame->argR9 == reinterpret_cast<const void*>(0x9999999989ABCDEFULL));
}
std::uintptr_t __cdecl BeforeChild(const RizListenerFrameV1* frame, void* user)
{
    auto* work = current; ValidateChild(frame, work, user); if (!work || frame->site >= 3) return 0;
    const auto site = frame->site;
    CHECK(frame->stage == RizListenerBefore && frame->flags == 0 && ++work->pre[site] == 1
        && frame->machineFrame && frame->transportReturnPc == frame->sourceReturnPc);
    if (!frame->machineFrame) return 0;
    const auto& machine = *frame->machineFrame;
    CHECK(machine.abiVersion == 1 && machine.byteSize == sizeof(machine) && machine.reserved0 == 0 && machine.reserved1 == 0
        && machine.returnPc == reinterpret_cast<std::uint64_t>(posts[work->target][site])
        && machine.entryRsp + 8 == machine.gpr[RizEventMidRsp] && frame->sourceRsp == machine.gpr[RizEventMidRsp]);
    if (work->parentSequence == 0) work->parentSequence = frame->parentSequence;
    CHECK(work->parentSequence == frame->parentSequence);
    for (unsigned previous = 0; previous != site; ++previous) CHECK(work->childSequences[previous] != frame->childSequence);
    work->childSequences[site] = frame->childSequence;
    work->preFrames[site] = machine;
    work->childTokens[site] = reinterpret_cast<std::uintptr_t>(work) ^ ChildMask ^ frame->childSequence;
    CONTEXT captured{}; RtlCaptureContext(&captured); Walk(captured, work->target, site, false);
    Fault(work, 1);
    RtLfClobber();
    return work->childTokens[site];
}
void __cdecl AfterChild(const RizListenerFrameV1* frame, std::uintptr_t token, void* user)
{
    auto* work = current; ValidateChild(frame, work, user); if (!work || frame->site >= 3) return;
    const auto site = frame->site;
    CHECK(frame->parentSequence == work->parentSequence && frame->childSequence == work->childSequences[site]
        && frame->sourceRsp == work->preFrames[site].gpr[RizEventMidRsp]);
    const bool beforeFaulted = work->observerFaultStage == 1;
    CHECK(token == (beforeFaulted ? 0 : work->childTokens[site]));
    if (frame->stage == RizListenerAbnormal) {
        CHECK(work->originalMode != 0 && site == work->originalFaultIndex && ++work->childAbnormal[site] == 1
            && frame->flags == (RizListenerParentAbnormal | (beforeFaulted ? RizListenerBeforeFaulted : 0u))
            && frame->machineFrame == nullptr && frame->transportReturnPc == nullptr);
    } else {
        CHECK(frame->stage == RizListenerReturned && ++work->post[site] == 1
            && frame->flags == (beforeFaulted ? RizListenerBeforeFaulted : 0u)
            && frame->machineFrame && frame->transportReturnPc == blocks[work->target].postReturn[site]);
        if (!frame->machineFrame) return;
        work->postFrames[site] = *frame->machineFrame;
        CHECK(frame->machineFrame->returnPc == reinterpret_cast<std::uint64_t>(frame->transportReturnPc)
            && frame->machineFrame->entryRsp + 8 == frame->sourceRsp);
        CONTEXT captured{}; RtlCaptureContext(&captured); Walk(captured, work->target, site, true);
        PostVirtualUnwind(*frame->machineFrame, work->target, site);
    }
    Fault(work, 2);
    RtLfClobber();
}
void AttachListener(unsigned target)
{
    const RizListenerObserverV1 observer{1, sizeof(observer), BeforeChild, AfterChild, &targets[target]};
    CHECK(listenerAttach(Slots[target], laneEpochs[target] + 99, &observer) == 3);
    CHECK(listenerAttach(Slots[target], laneEpochs[target], &observer) == 0);
    CHECK(listenerAttach(Slots[target], laneEpochs[target], &observer) == 4);
}
RizListenerInfoV1 Lane(unsigned target)
{
    RizListenerInfoV1 info{}; info.abiVersion = 1; info.byteSize = sizeof(info);
    CHECK(listenerInfo(Slots[target], &info) == 0);
    CHECK(info.slot == Slots[target] && info.epoch == laneEpochs[target] && info.parentEpoch == epochs[target]);
    return info;
}
void Drained(unsigned target, bool unknown)
{
    const auto lane = Lane(target);
    CHECK(lane.entered == lane.exited && lane.inFlight == 0 && lane.unknown == (unknown ? 1u : 0u)
        && lane.overflow == 0 && lane.missingPost == 0);
    RizVoidStatsV1 parent{}; parent.abiVersion = 1; parent.byteSize = sizeof(parent);
    CHECK(parentStats(Slots[target], &parent) == 0 && parent.entered == parent.exited && parent.inFlight == 0
        && parent.enterFaults == 0 && parent.exitFaults == 0);
    RizVoidBootstrapInfoV1 identity{}; identity.abiVersion = 1; identity.byteSize = sizeof(identity);
    CHECK(parentInfo(Slots[target], &identity) == 0 && identity.unknown == (unknown ? 1u : 0u)
        && identity.target == targets[target] && reinterpret_cast<const void*>(identity.original) == originals[target]
        && identity.epoch == epochs[target] && identity.attached == 1);
}
void VerifyWork(const ListenerFixtureWork& work)
{
    const auto count = static_cast<unsigned>(work.list.count);
    for (unsigned site = 0; site != 3; ++site) {
        const bool called = site < count && (!work.originalMode || site <= work.originalFaultIndex);
        const bool abnormal = called && work.originalMode && site == work.originalFaultIndex;
        CHECK(work.invoked[site] == (called ? 1u : 0u));
        CHECK(work.pre[site] == (called && work.expectedListener ? 1u : 0u));
        CHECK(work.post[site] == (called && !abnormal && work.expectedListener ? 1u : 0u));
        CHECK(work.childAbnormal[site] == (abnormal && work.expectedListener ? 1u : 0u));
    }
    CHECK(work.replayed == (work.originalMode ? work.originalFaultIndex : count)
        && work.after == (work.originalMode ? 0u : 1u));
}
void Run(unsigned target, unsigned count = 3, unsigned originalMode = 0, unsigned index = 0,
    unsigned faultStage = 0, unsigned faultKind = 0, bool worker = false, int recurseTarget = -1)
{
    ListenerFixtureWork work{}; Init(&work, target, count);
    work.originalMode = originalMode; work.originalFaultIndex = index;
    work.observerFaultStage = faultStage; work.observerFaultKind = faultKind;
    if (recurseTarget >= 0) { work.recurse = true; work.recurseTarget = static_cast<unsigned>(recurseTarget); }
    if (worker) { Join(CreateThread(nullptr, 0, Worker, &work, 0, nullptr)); InterlockedIncrement64(&workerCases); }
    else Execute(&work);
    VerifyWork(work); CloseEvents(&work);
    if (originalMode == 1) InterlockedIncrement64(&originalSehCases);
    else if (originalMode == 2) InterlockedIncrement64(&originalCppCases);
    else if (faultStage) InterlockedIncrement64(&faultCases);
    else InterlockedIncrement64(&normalCases);
}
void LateAttach(unsigned target, bool parentAlreadyAttached)
{
    ListenerFixtureWork work{}; Init(&work, target, 3, parentAlreadyAttached);
    work.expectedListener = false; work.hold = true;
    const auto worker = CreateThread(nullptr, 0, Worker, &work, 0, nullptr); CHECK(worker);
    if (!worker) ExitProcess(2);
    CHECK(WaitForSingleObject(work.reached, 10000) == WAIT_OBJECT_0);
    CHECK(work.parentEnter == (parentAlreadyAttached ? 1u : 0u));
    if (!parentAlreadyAttached) AttachParent(target);
    AttachListener(target);
    CHECK(SetEvent(work.resume)); Join(worker);
    VerifyWork(work); CloseEvents(&work); InterlockedIncrement64(&lateCalls);
    // The very next invocation must select the now-published descriptors.
    Run(target);
}
void Hex(const unsigned char* bytes, std::size_t count)
{
    std::putchar('"'); for (std::size_t i = 0; i != count; ++i) std::printf("%02x", bytes[i]); std::putchar('"');
}
int Result()
{
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-listener-six-region-fixture\",\"passed\":%s,\"checks\":%lld,\"failures\":%lld,"
        "\"gameRuntimeVerified\":false,\"gameCodeExecuted\":false,\"firstInstallSafetyVerified\":false,\"allFutureListenersClosed\":false,"
        "\"actualCalleeReturnChecks\":%lld,\"originalCalls\":%lld,\"replayChecks\":%lld,\"actualCallbackStackWalks\":%lld,"
        "\"postInstructionBoundaryUnwinds\":%lld,\"postBoundaryOffsets\":[0,5,7,9],\"patchedSourceWidths\":[5,5,6],\"patchedPostWidths\":[6,9,9],"
        "\"preFullStateChecks\":%lld,\"postFullStateChecks\":%lld,\"fixtureCodeWrites\":%lld,\"lateAttachOldCalls\":%lld,\"nullParentObserverOldCalls\":%lld,\"normalCases\":%lld,"
        "\"originalSehCases\":%lld,\"originalCppCases\":%lld,\"observerFaultCases\":%lld,\"observerFaults\":%lld,\"workerCases\":%lld,\"nestedCases\":%lld,"
        "\"unimplemented\":[\"dynamic-vtable-mutation\",\"target-read-av\",\"foreign-parent-mask-and-overflow\",\"missing-post-corruption\",\"full-exception-record\",\"prehelper-every-instruction-unwind\",\"all-delegate-leaves\",\"mutation-stopcoroutine-retirement\"],\"regions\":[",
        failures == 0 ? "true" : "false", static_cast<long long>(checks), static_cast<long long>(failures),
        static_cast<long long>(actualReturnChecks), static_cast<long long>(originalCalls), static_cast<long long>(replayChecks),
        static_cast<long long>(walks), static_cast<long long>(partialUnwinds), static_cast<long long>(preStateChecks), static_cast<long long>(postStateChecks),
        static_cast<long long>(codeWrites), static_cast<long long>(lateCalls),
        static_cast<long long>(oldCalls), static_cast<long long>(normalCases), static_cast<long long>(originalSehCases), static_cast<long long>(originalCppCases),
        static_cast<long long>(faultCases), static_cast<long long>(observerFaults), static_cast<long long>(workerCases), static_cast<long long>(nestedCases));
    for (unsigned target = 0; target != 2; ++target) {
        if (target) std::putchar(',');
        std::printf("{\"slot\":%u,\"originalPre\":[", Slots[target]);
        for (unsigned site = 0; site != 3; ++site) { if (site) std::putchar(','); Hex(blocks[target].originalPre[site], site == 2 ? 6 : 5); }
        std::printf("],\"originalPost\":[");
        for (unsigned site = 0; site != 3; ++site) { if (site) std::putchar(','); Hex(blocks[target].originalPost[site], blocks[target].sourcePostSize[site]); }
        std::printf("]}");
    }
    std::puts("]}"); return failures == 0 ? 0 : 1;
}
void DisableErrorUi()
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    const auto wer = LoadLibraryW(L"wer.dll");
    if (wer) {
        using WerFlags = HRESULT(WINAPI*)(DWORD);
        const auto symbol = GetProcAddress(wer, "WerSetFlags");
        WerFlags set{}; static_assert(sizeof(set) == sizeof(symbol)); std::memcpy(&set, &symbol, sizeof(set));
        if (set) set(0x20); // WER_FAULT_REPORTING_NO_UI; dynamically linked.
    }
}
}

int wmain(int argc, wchar_t** argv)
{
    DisableErrorUi(); CHECK(argc == 3); if (argc != 3) return Result();
    helper = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    const auto dobby = LoadLibraryExW(argv[2], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    CHECK(helper && dobby); if (!helper || !dobby) return Result();
    const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare");
    const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
    const auto configureParent = Symbol<decltype(&RizVoidBootstrap_Configure)>(helper, "RizVoidBootstrap_Configure");
    parentInfo = Symbol<ParentInfoFn>(helper, "RizVoidBootstrap_GetInfo");
    parentAttach = Symbol<ParentAttachFn>(helper, "RizVoidBootstrap_Attach");
    parentStats = Symbol<ParentStatsFn>(helper, "RizVoidContext_GetStats");
    const auto configureListener = Symbol<decltype(&RizListenerContext_Configure)>(helper, "RizListenerContext_Configure");
    listenerAttach = Symbol<ListenerAttachFn>(helper, "RizListenerContext_Attach");
    listenerInfo = Symbol<ListenerInfoFn>(helper, "RizListenerContext_GetInfo");
    const auto pre = Symbol<decltype(&RizListenerContext_GetPreEntry)>(helper, "RizListenerContext_GetPreEntry");
    const auto post = Symbol<decltype(&RizListenerContext_GetPostEntry)>(helper, "RizListenerContext_GetPostEntry");
    const auto mid = Symbol<decltype(&RizEventMidContext_GetEntry)>(helper, "RizEventMidContext_GetEntry");
    const auto parentEntry = Symbol<decltype(&RizVoidContext_GetEntry)>(helper, "RizVoidContext_GetEntry");
    if (failures) return Result();
    vtable.target = reinterpret_cast<void*>(&RtLfVirtual);
    vtable.method = reinterpret_cast<const void*>(0xF123456789ABCDEFULL);
    const auto preEntry = pre(), postEntry = post(), midEntry = mid(); CHECK(preEntry && postEntry && midEntry);
    for (unsigned target = 0; target != 2; ++target) {
        unsigned char before[32]{}; std::memcpy(before, targets[target], sizeof(before));
        for (const auto value : before) CHECK(value == 0x90);
        CHECK(prepare(targets[target], parentEntry(Slots[target]), &originals[target]) == 0 && originals[target]);
        CHECK(std::memcmp(before, targets[target], sizeof(before)) == 0);
        const RizVoidBootstrapConfigV1 config{1, sizeof(config), targets[target], reinterpret_cast<RizVoidOriginalV1>(originals[target]), midReturns[target]};
        CHECK(configureParent(Slots[target], &config) == 0);
        RizVoidBootstrapInfoV1 info{}; info.abiVersion = 1; info.byteSize = sizeof(info);
        CHECK(parentInfo(Slots[target], &info) == 0 && info.unknown == 0 && info.attached == 0 && info.epoch != 0);
        epochs[target] = info.epoch;
        MakeDynamic(target, preEntry, postEntry, midEntry);
        RizListenerConfigV1 lane{}; lane.abiVersion = 1; lane.byteSize = sizeof(lane); lane.parentEpoch = epochs[target];
        for (unsigned site = 0; site != 3; ++site) { lane.sourceReturnPc[site] = posts[target][site]; lane.postTransportReturnPc[site] = blocks[target].postReturn[site]; }
        lane.directTarget[0] = lane.directTarget[1] = reinterpret_cast<const void*>(&RtLfDirect);
        auto invalid = lane; ++invalid.parentEpoch; CHECK(configureListener(Slots[target], &invalid) == 5);
        CHECK(configureListener(Slots[target], &lane) == 0);
        RizListenerInfoV1 laneInfo{}; laneInfo.abiVersion = 1; laneInfo.byteSize = sizeof(laneInfo);
        CHECK(listenerInfo(Slots[target], &laneInfo) == 0 && laneInfo.unknown == 0 && laneInfo.attached == 0 && laneInfo.epoch != 0);
        laneEpochs[target] = laneInfo.epoch;
        CHECK(configureListener(Slots[target], &lane) == 2);
        CHECK(std::memcmp(before, targets[target], sizeof(before)) == 0);
        PublishSites(target);
        CHECK(commit(targets[target]) == 0);
        CHECK(std::memcmp(before, targets[target], sizeof(before)) != 0);
        if (failures) return Result();
    }
    // Both independent cases begin after all own code publication, but before
    // Attach. The worker really passed Bootstrap Enter before its pre-mid gate.
    LateAttach(0, false);
    AttachParent(1); LateAttach(1, true);
    for (unsigned target = 0; target != 2; ++target) {
        Run(target, 0); Run(target, 3, 0, 0, 0, 0, true);
        Run(target, 3, 0, 0, 0, 0, false, static_cast<int>(target));
        Run(target, 3, 0, 0, 0, 0, false, static_cast<int>(1 - target));
        for (unsigned mode = 1; mode != 3; ++mode)
            for (unsigned site = 0; site != 3; ++site) Run(target, 3, mode, site);
        Drained(target, false);
    }
    // Fault contamination is confined to slot 28 after the clean cases.
    for (unsigned stage = 1; stage != 3; ++stage)
        for (unsigned kind = 1; kind != 3; ++kind) {
            const auto prior = Lane(1).faults;
            Run(1, 1, 0, 0, stage, kind); CHECK(Lane(1).faults == prior + 1);
            Drained(1, true); Run(1); Drained(1, true);
        }
    const RizListenerObserverV1 observer{1, sizeof(observer), BeforeChild, AfterChild, &targets[1]};
    CHECK(listenerAttach(Slots[1], laneEpochs[1], &observer) == 5);
    Drained(0, false); Drained(1, true);
    CHECK(current == nullptr && actualReturnChecks == originalCalls && codeWrites == 14
        && lateCalls == 2 && originalSehCases == 6 && originalCppCases == 6 && observerFaults == 4);
    return Result();
}
