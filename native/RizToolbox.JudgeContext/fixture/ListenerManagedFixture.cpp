#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <cstdint>
#include <cstring>
#include <initializer_list>
#include <limits>
#include "ListenerManagedFixture.h"
#include "ListenerFixture.h"

// The existing ASM is compiled into this independent DLL, with these C++
// bodies, not linked to the previous executable. Only this DLL's two targets
// are ever published. Modules, dynamic RFs, slots, work and events are permanent.
namespace {
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
using ParentInfoFn = decltype(&RizVoidBootstrap_GetInfo);
using LaneInfoFn = decltype(&RizListenerContext_GetInfo);
constexpr DWORD OriginalSeh = 0xe04c534f, HoldTimeout = 0xe04c544f;
constexpr std::uint64_t Payload = 0xfedcba9876543210ULL, StackSeal = 0xaabbccddeeff1122ULL;
constexpr std::size_t WorkLimit = 128;
void* const targets[2]{ reinterpret_cast<void*>(&RtLfTarget0), reinterpret_cast<void*>(&RtLfTarget1) };
void* const sites[2][3]{ { &RtLfSite00, &RtLfSite01, &RtLfSite02 }, { &RtLfSite10, &RtLfSite11, &RtLfSite12 } };
void* const posts[2][3]{ { &RtLfPost00, &RtLfPost01, &RtLfPost02 }, { &RtLfPost10, &RtLfPost11, &RtLfPost12 } };
void* const loops[2]{ &RtLfLoop0, &RtLfLoop1 };
void* const mids[2]{ &RtLfMid0, &RtLfMid1 };
void* const midReturns[2]{ &RtLfMidReturn0, &RtLfMidReturn1 };
unsigned char opaqueMethods[2][16]{};
struct VTable { unsigned char padding[0x178]; void* target; const void* method; };
static_assert(offsetof(VTable, target) == 0x178 && offsetof(VTable, method) == 0x180);
VTable vtable{};
struct DynamicBlock {
    unsigned char* page{};
    RUNTIME_FUNCTION* table{};
    RUNTIME_FUNCTION primary{};
    DWORD64 imageBase{};
    unsigned char* preRelay{};
    unsigned char* midRelay{};
    unsigned char* postStub[3]{};
    const void* postReturn[3]{};
    unsigned char sealed[4096]{};
    std::uint32_t writes{}, lookups{}, boundaries{}, functions{};
};
DynamicBlock blocks[2]{};
void* originals[2]{};
std::uint32_t slots[2]{};
std::uint64_t parentEpochs[2]{}, laneEpochs[2]{};
HMODULE helper{}, dobby{};
ParentInfoFn readParent{};
LaneInfoFn readLane{};
volatile LONG setupState{}, workCount{};
SRWLOCK workLock = SRWLOCK_INIT;
const void* badKlass{};
const void* badReadAddress{};
struct FixtureFailure {};
struct OriginalCpp { std::uint64_t payload; const void* work; std::uint32_t site; };
void Require(bool value) { if (!value) throw FixtureFailure{}; }
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto symbol = GetProcAddress(module, name); Require(symbol != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(symbol));
    std::memcpy(&result, &symbol, sizeof(result)); return result;
}
}

// The three first fields/rows are exactly the immutable existing ASM contract.
struct ListenerFixtureWork {
    ListenerFixturePrefix prefix{};
    std::uint64_t args[2]{ 0x1111222233334444ULL, 0x5555666677778888ULL };
    ListenerFixtureList list{};
    ListenerFixtureInvokable* items[3]{};
    ListenerFixtureInvokable rows[3]{};
    RizListenerManagedConfigV1 config{};
    RizListenerManagedResultV1 result{};
    HANDLE reached{}, resume{};
    volatile LONG state{}, held{};
};
static_assert(offsetof(ListenerFixtureWork, prefix) == 0 && offsetof(ListenerFixtureWork, args) == 24);

namespace {
ListenerFixtureWork works[WorkLimit]{};
ListenerFixtureWork* Work(void* pointer)
{
    const auto address = reinterpret_cast<std::uintptr_t>(pointer);
    const auto first = reinterpret_cast<std::uintptr_t>(&works[0]);
    if (address < first || address - first >= sizeof(works) || (address - first) % sizeof(works[0]) != 0) return nullptr;
    const auto index = (address - first) / sizeof(works[0]);
    return index < static_cast<std::size_t>(InterlockedCompareExchange(&workCount, 0, 0)) ? &works[index] : nullptr;
}
void Check(ListenerFixtureWork* work, bool value)
{
    ++work->result.nativeChecks;
    if (!value) ++work->result.nativeFailures;
}
void* AllocateNearAbove(DWORD64 imageBase, const void* source)
{
    SYSTEM_INFO info{}; GetSystemInfo(&info);
    const auto granularity = static_cast<std::uintptr_t>(info.dwAllocationGranularity);
    for (std::uintptr_t offset = granularity; offset < 0x70000000ULL; offset += granularity) {
        const auto candidate = static_cast<std::uintptr_t>(imageBase) + offset;
        const auto delta = static_cast<std::intptr_t>(candidate) - reinterpret_cast<std::intptr_t>(source);
        if (delta < -0x70000000LL || delta > 0x70000000LL) continue;
        const auto allocation = VirtualAlloc(reinterpret_cast<void*>(candidate), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (allocation) return allocation;
    }
    return nullptr;
}
void Relative(unsigned char* instruction, unsigned char opcode, const void* destination)
{
    const auto difference = reinterpret_cast<std::intptr_t>(destination) - reinterpret_cast<std::intptr_t>(instruction + 5);
    Require(difference >= (std::numeric_limits<std::int32_t>::min)() && difference <= (std::numeric_limits<std::int32_t>::max)());
    const auto displacement = static_cast<std::int32_t>(difference);
    instruction[0] = opcode; std::memcpy(instruction + 1, &displacement, 4);
}
void Tail(unsigned char* code, const void* destination)
{
    const unsigned char bytes[]{ 0xff, 0x25, 0, 0, 0, 0 };
    std::memcpy(code, bytes, sizeof(bytes)); std::memcpy(code + 6, &destination, sizeof(destination));
}
void VerifyBoundary(DynamicBlock& block, unsigned site, unsigned offset)
{
    alignas(16) std::uint64_t stack[64]{};
    stack[8] = 0x1414141414141414ULL; stack[9] = 0x7777777777777777ULL;
    stack[10] = 0x6666666666666666ULL; stack[11] = 0x5555555555555555ULL;
    stack[12] = 0x3333333333333333ULL; stack[13] = reinterpret_cast<std::uint64_t>(&RtLfCallerReturn);
    CONTEXT context{}; context.ContextFlags = CONTEXT_ALL;
    context.Rip = reinterpret_cast<DWORD64>(block.postStub[site] + offset);
    context.Rsp = reinterpret_cast<DWORD64>(stack); context.Rbx = offset >= 7 ? 1 : 0;
    context.Rax = offset >= 9 ? 1 : 0; context.R12 = 12; context.R13 = 13; context.R15 = 15;
    DWORD64 base{}; auto* actual = RtlLookupFunctionEntry(context.Rip, &base, nullptr);
    Require(actual && base == block.imageBase && std::memcmp(actual, &block.table[site], sizeof(*actual)) == 0);
    ++block.lookups;
    PVOID handler{}; DWORD64 establisher{};
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, base, context.Rip, actual, &context, &handler, &establisher, nullptr);
    Require(context.Rip == stack[13] && context.Rsp == reinterpret_cast<DWORD64>(stack) + 0x70
        && context.R14 == stack[8] && context.Rdi == stack[9] && context.Rsi == stack[10]
        && context.Rbp == stack[11] && context.Rbx == stack[12]
        && context.R12 == 12 && context.R13 == 13 && context.R15 == 15);
    ++block.boundaries;
}
void PrepareDynamic(unsigned target, const void* pre, const void* post, const void* mid)
{
    auto& block = blocks[target]; DWORD64 base{};
    const auto* primary = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(targets[target]), &base, nullptr);
    HMODULE own{};
    Require(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(targets[target]), &own) != 0);
    Require(primary && base == reinterpret_cast<DWORD64>(own));
    // This own ASM parent has no frame register. CHAININFO must inherit the
    // actual primary frame byte, not assume a game profile or another module.
    const auto* primaryUnwind = reinterpret_cast<const unsigned char*>(base + primary->UnwindData);
    Require(primaryUnwind[3] == 0);
    block.primary = *primary; block.imageBase = base;
    block.page = static_cast<unsigned char*>(AllocateNearAbove(base, targets[target])); Require(block.page != nullptr);
    std::memset(block.page, 0xcc, 4096);
    block.table = reinterpret_cast<RUNTIME_FUNCTION*>(block.page + 0x380);
    block.preRelay = block.page + 0x500; block.midRelay = block.page + 0x520;
    Tail(block.preRelay, pre); Tail(block.midRelay, mid); Tail(block.page + 0x540, post);
    for (unsigned site = 0; site != 3; ++site) {
        auto* stub = block.page + site * 0x100; block.postStub[site] = stub; block.postReturn[site] = stub + 5;
        Relative(stub, 0xe8, block.page + 0x540);
        const unsigned char replay[]{ 0xff, 0xc3, 0x8b, 0xc3 }; std::memcpy(stub + 5, replay, sizeof(replay));
        Relative(stub + 9, 0xe9, loops[target]);
        auto* unwind = stub + 0x40; unwind[0] = 0x21; unwind[1] = 0; unwind[2] = 0; unwind[3] = primaryUnwind[3];
        std::memcpy(unwind + 4, primary, sizeof(*primary));
        const auto begin = reinterpret_cast<DWORD64>(stub) - base, data = reinterpret_cast<DWORD64>(unwind) - base;
        Require(begin + 14 <= MAXDWORD && data <= MAXDWORD);
        block.table[site] = { static_cast<DWORD>(begin), static_cast<DWORD>(begin + 14), static_cast<DWORD>(data) };
    }
    std::memcpy(block.sealed, block.page, sizeof(block.sealed));
    DWORD old{}; Require(VirtualProtect(block.page, 4096, PAGE_EXECUTE_READ, &old) != 0);
    Require(FlushInstructionCache(GetCurrentProcess(), block.page, 4096) != 0);
    Require(RtlAddFunctionTable(block.table, 3, base) != 0); block.functions = 3;
    for (unsigned site = 0; site != 3; ++site)
        for (const unsigned offset : { 0u, 5u, 7u, 9u }) VerifyBoundary(block, site, offset);
    Require(block.lookups == 12 && block.boundaries == 12 && block.writes == 0);
}
void WriteCode(unsigned target, void* destination, const unsigned char* bytes, std::size_t length)
{
    ++blocks[target].writes; // Attempt count precedes VirtualProtect or mutation.
    DWORD old{}; Require(VirtualProtect(destination, length, PAGE_EXECUTE_READWRITE, &old) != 0);
    std::memcpy(destination, bytes, length); DWORD ignored{};
    Require(VirtualProtect(destination, length, old, &ignored) != 0);
    Require(FlushInstructionCache(GetCurrentProcess(), destination, length) != 0);
    MEMORY_BASIC_INFORMATION memory{};
    Require(VirtualQuery(destination, &memory, sizeof(memory)) == sizeof(memory) && memory.Protect == old
        && std::memcmp(destination, bytes, length) == 0);
}
void Publish(unsigned target, CommitFn commit)
{
    auto& block = blocks[target];
    for (unsigned site = 0; site != 3; ++site) {
        const auto* source = static_cast<const unsigned char*>(sites[target][site]);
        const auto* post = static_cast<const unsigned char*>(posts[target][site]);
        Require(source + (site == 2 ? 6 : 5) == post);
        Require(site == 2 ? std::memcmp(source, "\xff\x90\x78\x01\x00\x00", 6) == 0 : source[0] == 0xe8);
        Require(std::memcmp(post, "\xff\xc3\x8b\xc3", 4) == 0 && post[4] == (site == 0 ? 0xeb : 0xe9));
        unsigned char call[6]{ 0x90 }; const auto prefix = site == 2 ? 1u : 0u;
        const auto delta = reinterpret_cast<std::intptr_t>(block.preRelay) - reinterpret_cast<std::intptr_t>(source + prefix + 5);
        Require(delta >= (std::numeric_limits<std::int32_t>::min)() && delta <= (std::numeric_limits<std::int32_t>::max)());
        const auto relative = static_cast<std::int32_t>(delta); call[prefix] = 0xe8; std::memcpy(call + prefix + 1, &relative, 4);
        WriteCode(target, sites[target][site], call, site == 2 ? 6 : 5);
        unsigned char jump[9]{}; std::memset(jump, 0x90, sizeof(jump));
        const auto postDelta = reinterpret_cast<std::intptr_t>(block.postStub[site]) - reinterpret_cast<std::intptr_t>(post + 5);
        Require(postDelta >= (std::numeric_limits<std::int32_t>::min)() && postDelta <= (std::numeric_limits<std::int32_t>::max)());
        const auto postRelative = static_cast<std::int32_t>(postDelta); jump[0] = 0xe9; std::memcpy(jump + 1, &postRelative, 4);
        WriteCode(target, posts[target][site], jump, site == 0 ? 6 : 9);
    }
    Require(std::memcmp(mids[target], "\x0f\x1f\x44\x00\x00", 5) == 0);
    unsigned char call[5]{}; const auto delta = reinterpret_cast<std::intptr_t>(block.midRelay) - reinterpret_cast<std::intptr_t>(midReturns[target]);
    Require(delta >= (std::numeric_limits<std::int32_t>::min)() && delta <= (std::numeric_limits<std::int32_t>::max)());
    const auto relative = static_cast<std::int32_t>(delta); call[0] = 0xe8; std::memcpy(call + 1, &relative, 4);
    WriteCode(target, mids[target], call, sizeof(call));
    ++block.writes; Require(commit(targets[target]) == 0); Require(block.writes == 8);
}
void PrepareAv()
{
    SYSTEM_INFO info{}; GetSystemInfo(&info); const auto pageSize = static_cast<std::size_t>(info.dwPageSize);
    auto* memory = static_cast<unsigned char*>(VirtualAlloc(nullptr, pageSize * 2, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    Require(memory != nullptr);
    // The ASM's [RAX+180h] metadata read is readable, but the helper's single
    // [RAX+178h] target read is in the immediately preceding PAGE_NOACCESS page.
    auto* boundary = memory + pageSize;
    const void* method = &opaqueMethods[0][0]; std::memcpy(boundary, &method, sizeof(method));
    badKlass = boundary - 0x180; badReadAddress = boundary - 8;
    DWORD old{}; Require(VirtualProtect(memory, pageSize, PAGE_NOACCESS, &old) != 0);
}
void Hold(ListenerFixtureWork* work)
{
    InterlockedExchange(&work->held, 1);
    if (!SetEvent(work->reached) || WaitForSingleObject(work->resume, 15000) != WAIT_OBJECT_0)
        RaiseException(HoldTimeout, 0, 0, nullptr);
}
void InvokeBody(ListenerFixtureWork* work);
LONG Filter(EXCEPTION_POINTERS* pointers, ListenerFixtureWork* work)
{
    const auto* record = pointers->ExceptionRecord; auto& result = work->result;
    result.exceptionCode = record->ExceptionCode; result.exceptionFlags = record->ExceptionFlags;
    result.exceptionParameterCount = record->NumberParameters; result.exceptionAddress = record->ExceptionAddress;
    for (unsigned i = 0; i != 3; ++i) result.exceptionInformation[i] = i < record->NumberParameters ? record->ExceptionInformation[i] : 0;
    result.exceptionInformation3 = record->NumberParameters > 3 ? record->ExceptionInformation[3] : 0;
    return record->ExceptionCode == 0xe06d7363 ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER;
}
// This leaf deliberately has no C++ unwinding objects; C++ exceptions continue
// to the typed outer catch while SEH/AV finish natively inside the DLL.
void InvokeSeh(ListenerFixtureWork* work)
{
    __try {
        const auto target = work->config.mode == RizLmForeignOriginal ? originals[work->config.targetIndex] : targets[work->config.targetIndex];
        RtLfCall(target, work, &opaqueMethods[work->config.targetIndex][0]);
        work->result.returned = 1;
    } __except (Filter(GetExceptionInformation(), work)) { work->result.caught = 1; }
}
void InvokeBody(ListenerFixtureWork* work)
{
    work->result.osThreadId = GetCurrentThreadId();
    try { InvokeSeh(work); }
    catch (const OriginalCpp& error) {
        Check(work, error.payload == Payload && error.work == work && error.site == work->config.site);
        Check(work, work->result.exceptionCode == 0xe06d7363 && work->result.exceptionParameterCount == 4);
        work->result.caught = 1;
    } catch (...) { Check(work, false); work->result.caught = 1; }
    if (work->config.mode == RizLmOriginalSeh) {
        Check(work, work->result.caught == 1 && work->result.exceptionCode == OriginalSeh
            && work->result.exceptionParameterCount == 3 && work->result.exceptionInformation[0] == Payload
            && work->result.exceptionInformation[1] == reinterpret_cast<std::uint64_t>(work)
            && work->result.exceptionInformation[2] == work->config.site);
    } else if (work->config.mode == RizLmTargetReadAv) {
        HMODULE faultModule{};
        Check(work, work->result.caught == 1 && work->result.exceptionCode == EXCEPTION_ACCESS_VIOLATION
            && work->result.exceptionParameterCount == 2 && work->result.exceptionInformation[0] == 0
            && work->result.exceptionInformation[1] == reinterpret_cast<std::uint64_t>(badReadAddress));
        const auto mapped = GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(work->result.exceptionAddress), &faultModule);
        Check(work, mapped != 0 && faultModule == helper);
    } else if (work->config.mode != RizLmOriginalCpp) Check(work, work->result.returned == 1 && work->result.caught == 0);
    Check(work, work->result.returnPcChecks == work->result.originalCalls);
    InterlockedExchange(&work->state, 2);
}
DWORD WINAPI Worker(void* data) { InvokeBody(static_cast<ListenerFixtureWork*>(data)); return 0; }
void Callee(ListenerFixtureInvokable* row, const void* argument, const void* method, const void* actualCaller)
{
    auto* work = row->owner; const auto site = row->index; const auto target = work->config.targetIndex;
    Check(work, site < 3); if (site >= 3) return;
    ++work->result.originalCalls; ++work->result.invoked[site]; work->result.actualReturn[site] = actualCaller;
    Check(work, actualCaller == posts[target][site] && row->actualReturn == actualCaller);
    if (actualCaller == posts[target][site] && row->actualReturn == actualCaller) ++work->result.returnPcChecks;
    Check(work, row->actualGpr[RizEventMidRsi] == reinterpret_cast<std::uint64_t>(work)
        && row->actualGpr[RizEventMidRbp] == reinterpret_cast<std::uint64_t>(&work->list)
        && row->actualGpr[RizEventMidRdi] == reinterpret_cast<std::uint64_t>(row)
        && row->actualGpr[RizEventMidRbx] == site && row->actualGpr[RizEventMidR9] == 0x9999999989abcdefULL);
    Check(work, site == 2 ? argument == work->args && method == vtable.method
        : argument == nullptr && method == reinterpret_cast<const void*>(0x88888888789abcdeULL));
    if ((work->config.flags & RizLmHoldInCallee) && site == work->config.site) Hold(work);
    if (work->config.nestedWork && site == work->config.site) {
        auto* nested = Work(work->config.nestedWork); Check(work, nested != nullptr);
        if (nested && InterlockedCompareExchange(&nested->state, 1, 0) == 0) {
            InvokeBody(nested); ++work->result.nestedInvocations; Check(work, nested->result.nativeFailures == 0);
        } else Check(work, false);
    }
    if (site != work->config.site) return;
    if (work->config.mode == RizLmOriginalSeh) {
        const ULONG_PTR values[]{ static_cast<ULONG_PTR>(Payload), reinterpret_cast<ULONG_PTR>(work), site };
        RaiseException(OriginalSeh, 0, 3, values);
    } else if (work->config.mode == RizLmOriginalCpp) throw OriginalCpp{ Payload, work, site };
}
}

extern "C" void __cdecl RtLfBefore(void* self, const void* method)
{
    auto* work = static_cast<ListenerFixtureWork*>(self);
    Check(work, method == &opaqueMethods[work->config.targetIndex][0]);
    if (work->config.flags & RizLmHoldBeforeMid) Hold(work);
}
extern "C" void __cdecl RtLfLoopCheck(void* self, std::uint32_t index, std::uint64_t value, std::uint64_t)
{
    auto* work = static_cast<ListenerFixtureWork*>(self);
    if (index) {
        Check(work, index <= 3 && value == index && work->result.invoked[index - 1] == 1);
        ++work->result.replayChecks;
    }
}
extern "C" void __cdecl RtLfAfter(void* self, const void* method, std::uint64_t first, std::uint64_t second)
{
    auto* work = static_cast<ListenerFixtureWork*>(self);
    Check(work, method == &opaqueMethods[work->config.targetIndex][0] && first == StackSeal && second == StackSeal
        && work->result.replayChecks == static_cast<std::uint64_t>(work->config.count));
    ++work->result.afterCount;
}
extern "C" __declspec(noinline) void __cdecl RtLfDirectBody(ListenerFixtureInvokable* row, const void* argument, const void* method)
{ const auto* caller = _ReturnAddress(); Callee(row, argument, method, caller); }
extern "C" __declspec(noinline) void __cdecl RtLfVirtualBody(ListenerFixtureInvokable* row, const void* argument, const void* method)
{ const auto* caller = _ReturnAddress(); Callee(row, argument, method, caller); }

RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_Setup(const wchar_t* helperPath, const wchar_t* dobbyPath, std::uint32_t primarySlot)
{
    if (!helperPath || !*helperPath || !dobbyPath || !*dobbyPath || primarySlot == 0 || primarySlot > 31) return 1;
    if (InterlockedCompareExchange(&setupState, 1, 0) != 0) return 2;
    try {
        helper = LoadLibraryExW(helperPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        dobby = LoadLibraryExW(dobbyPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        Require(helper && dobby);
        const auto prepare = Symbol<PrepareFn>(dobby, "DobbyPrepare");
        const auto commit = Symbol<CommitFn>(dobby, "DobbyCommit");
        const auto configureParent = Symbol<decltype(&RizVoidBootstrap_Configure)>(helper, "RizVoidBootstrap_Configure");
        const auto configureLane = Symbol<decltype(&RizListenerContext_Configure)>(helper, "RizListenerContext_Configure");
        readParent = Symbol<ParentInfoFn>(helper, "RizVoidBootstrap_GetInfo");
        readLane = Symbol<LaneInfoFn>(helper, "RizListenerContext_GetInfo");
        const auto parentEntry = Symbol<decltype(&RizVoidContext_GetEntry)>(helper, "RizVoidContext_GetEntry");
        const auto mid = Symbol<decltype(&RizEventMidContext_GetEntry)>(helper, "RizEventMidContext_GetEntry")();
        const auto pre = Symbol<decltype(&RizListenerContext_GetPreEntry)>(helper, "RizListenerContext_GetPreEntry")();
        const auto post = Symbol<decltype(&RizListenerContext_GetPostEntry)>(helper, "RizListenerContext_GetPostEntry")();
        Require(pre && post && mid);
        vtable.target = reinterpret_cast<void*>(&RtLfVirtual); vtable.method = &opaqueMethods[0][0]; PrepareAv();
        for (unsigned target = 0; target != 2; ++target) {
            slots[target] = primarySlot - target;
            unsigned char before[32]{}; std::memcpy(before, targets[target], sizeof(before));
            for (const auto value : before) Require(value == 0x90);
            Require(prepare(targets[target], parentEntry(slots[target]), &originals[target]) == 0 && originals[target]);
            Require(std::memcmp(before, targets[target], sizeof(before)) == 0);
            const RizVoidBootstrapConfigV1 config{ 1, sizeof(config), targets[target], reinterpret_cast<RizVoidOriginalV1>(originals[target]), midReturns[target] };
            Require(configureParent(slots[target], &config) == 0);
            RizVoidBootstrapInfoV1 parent{}; parent.abiVersion = 1; parent.byteSize = sizeof(parent);
            Require(readParent(slots[target], &parent) == 0 && parent.unknown == 0 && parent.attached == 0 && parent.epoch != 0);
            parentEpochs[target] = parent.epoch;
            PrepareDynamic(target, pre, post, mid);
            RizListenerConfigV1 lane{}; lane.abiVersion = 1; lane.byteSize = sizeof(lane); lane.parentEpoch = parent.epoch;
            for (unsigned site = 0; site != 3; ++site) { lane.sourceReturnPc[site] = posts[target][site]; lane.postTransportReturnPc[site] = blocks[target].postReturn[site]; }
            lane.directTarget[0] = lane.directTarget[1] = reinterpret_cast<const void*>(&RtLfDirect);
            Require(configureLane(slots[target], &lane) == 0);
            RizListenerInfoV1 info{}; info.abiVersion = 1; info.byteSize = sizeof(info);
            Require(readLane(slots[target], &info) == 0 && info.unknown == 0 && info.attached == 0 && info.epoch != 0);
            laneEpochs[target] = info.epoch;
            Require(std::memcmp(before, targets[target], sizeof(before)) == 0);
        }
        // Both complete sets of stubs/registrations/identities are proven before
        // either target receives its first source write. Observer Attach is CLR.
        for (unsigned target = 0; target != 2; ++target) Publish(target, commit);
        InterlockedExchange(&setupState, 2); return 0;
    } catch (...) { InterlockedExchange(&setupState, -1); return 3; }
}
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_GetInfo(std::uint32_t target, RizListenerManagedInfoV1* info)
{
    if (target > 1 || !info || info->abiVersion != 1 || info->byteSize != sizeof(*info) || info->reserved != 0) return 1;
    if (InterlockedCompareExchange(&setupState, 0, 0) != 2) return 2;
    *info = {}; info->abiVersion = 1; info->byteSize = sizeof(*info); info->targetIndex = target;
    info->parent.abiVersion = 1; info->parent.byteSize = sizeof(info->parent);
    info->lane.abiVersion = 1; info->lane.byteSize = sizeof(info->lane);
    const auto parentStatus = readParent(slots[target], &info->parent), laneStatus = readLane(slots[target], &info->lane);
    const auto& block = blocks[target];
    info->midReturnPc = midReturns[target]; info->method = &opaqueMethods[target][0];
    for (unsigned site = 0; site != 3; ++site) { info->sourceWidth[site] = site == 2 ? 6u : 5u; info->postWidth[site] = site == 0 ? 6u : 9u; }
    info->codeWrites = block.writes; info->prewriteLookupChecks = block.lookups;
    info->prewriteBoundaryChecks = block.boundaries; info->registeredFunctionCount = block.functions;
    info->functionTable = block.table; info->allocation = block.page; info->allocationSize = 4096;
    info->unknown = parentStatus != 0 || laneStatus != 0 || info->parent.unknown || info->lane.unknown
        || info->parent.epoch != parentEpochs[target] || info->lane.epoch != laneEpochs[target]
        || std::memcmp(block.page, block.sealed, sizeof(block.sealed)) != 0 ? 1u : 0u;
    return 0;
}
RIZLM_EXPORT const void* __cdecl RizListenerManaged_GetMethod(std::uint32_t target)
{ return target < 2 && InterlockedCompareExchange(&setupState, 0, 0) == 2 ? &opaqueMethods[target][0] : nullptr; }
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_CreateWork(const RizListenerManagedConfigV1* config, void** output)
{
    if (!output) return 1; *output = nullptr;
    if (!config || config->abiVersion != 1 || config->byteSize != sizeof(*config) || config->targetIndex > 1
        || config->mode > RizLmForeignOriginal || config->site > 2 || (config->flags & ~7u) || config->count > 3
        || config->reserved || (config->mode == RizLmTargetReadAv && config->count != 3)
        || ((config->mode == RizLmOriginalSeh || config->mode == RizLmOriginalCpp) && config->site >= config->count)
        || ((config->flags & RizLmHoldInCallee) && config->site >= config->count)
        || ((config->flags & RizLmHoldBeforeMid) && (config->flags & RizLmHoldInCallee))
        || (config->nestedWork && (!Work(config->nestedWork) || config->site >= config->count))) return 1;
    if (InterlockedCompareExchange(&setupState, 0, 0) != 2) return 2;
    AcquireSRWLockExclusive(&workLock);
    const auto index = InterlockedCompareExchange(&workCount, 0, 0);
    if (index == static_cast<LONG>(WorkLimit)) { ReleaseSRWLockExclusive(&workLock); return 3; }
    auto* work = &works[index]; work->config = *config;
    work->reached = CreateEventW(nullptr, TRUE, FALSE, nullptr); work->resume = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!work->reached || !work->resume) { ReleaseSRWLockExclusive(&workLock); return 4; }
    work->prefix.list = &work->list; work->list.items = work->items;
    work->list.count = static_cast<std::int32_t>(config->count); work->list.version = 17;
    for (unsigned site = 0; site != 3; ++site) {
        work->items[site] = &work->rows[site]; auto& row = work->rows[site];
        row.klass = const_cast<void*>(config->mode == RizLmTargetReadAv && site == 2 ? badKlass : static_cast<const void*>(&vtable));
        row.owner = work; row.index = site; row.path = site;
    }
    auto& result = work->result; result.abiVersion = 1; result.byteSize = sizeof(result);
    result.mode = config->mode; result.targetIndex = config->targetIndex;
    result.event = work; result.list = &work->list; result.args = work->args;
    for (unsigned site = 0; site != 3; ++site) result.invokable[site] = &work->rows[site];
    InterlockedExchange(&workCount, index + 1); *output = work;
    ReleaseSRWLockExclusive(&workLock); return 0;
}
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_InvokeWork(void* pointer)
{
    auto* work = Work(pointer); if (!work) return 1;
    if (InterlockedCompareExchange(&work->state, 1, 0) != 0) return 2;
    if (work->config.flags & RizLmNativeWorker) {
        const auto thread = CreateThread(nullptr, 0, Worker, work, 0, nullptr);
        if (!thread) { Check(work, false); InterlockedExchange(&work->state, 2); return 3; }
        const auto waited = WaitForSingleObject(thread, 30000); CloseHandle(thread);
        // A timeout leaves state1 and the permanent work/resources rooted;
        // it never terminates a thread or pretends its physical call retired.
        return waited == WAIT_OBJECT_0 ? 0u : 3u;
    }
    InvokeBody(work); return 0;
}
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_ReadWork(void* pointer, RizListenerManagedResultV1* result)
{
    auto* work = Work(pointer);
    if (!work || !result || result->abiVersion != 1 || result->byteSize != sizeof(*result)) return 1;
    const auto state = InterlockedCompareExchange(&work->state, 0, 0);
    *result = {}; result->abiVersion = 1; result->byteSize = sizeof(*result);
    if (state == 2) *result = work->result;
    else {
        result->mode = work->config.mode; result->targetIndex = work->config.targetIndex;
        result->event = work; result->list = &work->list; result->args = work->args;
        for (unsigned site = 0; site != 3; ++site) result->invokable[site] = &work->rows[site];
    }
    result->state = static_cast<std::uint32_t>(state);
    result->holdReached = static_cast<std::uint32_t>(InterlockedCompareExchange(&work->held, 0, 0)); return 0;
}
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_WaitHeld(void* pointer, std::uint32_t timeoutMs)
{
    auto* work = Work(pointer); if (!work || timeoutMs > 30000) return 1;
    const auto status = WaitForSingleObject(work->reached, timeoutMs);
    return status == WAIT_OBJECT_0 ? 0u : status == WAIT_TIMEOUT ? 2u : 3u;
}
RIZLM_EXPORT std::uint32_t __cdecl RizListenerManaged_Resume(void* pointer)
{ auto* work = Work(pointer); if (!work) return 1; return SetEvent(work->resume) ? 0u : 2u; }
