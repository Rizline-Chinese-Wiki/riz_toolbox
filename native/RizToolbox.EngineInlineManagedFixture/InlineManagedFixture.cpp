#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <cstdint>
#include <cstring>
#include <limits>
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/MidContext.h"

// The existing independently checked ASM is compiled into THIS DLL. Only its
// three labelled windows are patched; no executable or game image is a target.
extern "C" {
std::uint8_t __cdecl RtEngineInlineFixture_Start(void*, void*, void*, void*);
extern unsigned char RtEngineInlineFixture_BeginSite, RtEngineInlineFixture_BeginContinuation;
extern unsigned char RtEngineInlineFixture_EndFreeSite, RtEngineInlineFixture_EndRetainedSite;
extern unsigned char RtEngineInlineFixture_CommonContinuation, RtEngineInlineFixture_StartEnd;
__declspec(align(16)) unsigned char RtEngineInlineFixture_XmmInput[16][16]{};
std::uint32_t RtEngineInlineFixture_InputMxcsr = 0x3f80, RtEngineInlineFixture_ClobberMxcsr = 0x1f80;
std::uint16_t RtEngineInlineFixture_InputFpcsr = 0x077f, RtEngineInlineFixture_ClobberFpcsr = 0x037f;
}
#define FIXTURE_EXPORT extern "C" __declspec(dllexport)
namespace {
using Machine = RizEventMidFrameV1;
using StartFn = std::uint8_t(__cdecl*)(void*, void*, void*, void*);
using ExecuteFn = std::uint32_t(__cdecl*)(void*, void*);
constexpr unsigned Slot = 26;
constexpr std::uint64_t Canary = 0xaabbccddeeff1122ULL, Link = 0x76543210abcdef09ULL;
struct WorkDescriptor { std::uint32_t id, retained; void* owner; void* enumerator; std::uint64_t handle; };
struct Descriptor {
    std::uint32_t version, byteSize, scenario, slot, workCount, configured, mainThreadId, failures;
    void* imageBase; void* kernel; void* inlineEntry; void* relayBase;
    RizEngineInlineSiteV1 sites[3]; WorkDescriptor works[2];
    std::uint32_t nativeChecks, reserved;
};
struct WorkReceipt {
    std::uint32_t id, retained, starts, executes, beginReplay, ends, freed, retainedCleanup;
    void* owner; void* enumerator; void* node; void* output;
    std::uint64_t handle; std::uint32_t rawReturn, reserved;
};
struct Receipt {
    std::uint32_t version, byteSize, scenario, workCount, checks, failures, freeCount, retainedCount;
    std::uint32_t addressReuse, originalStarts, originalExecutes, reserved;
    void* imageBase; void* relayBase; WorkReceipt works[2];
};
static_assert(sizeof(WorkDescriptor) == 32 && sizeof(Descriptor) == 184);
static_assert(sizeof(WorkReceipt) == 80 && sizeof(Receipt) == 224);
struct Work {
    unsigned id{}, retained{}, nested{}, starts{}, executes{}, begins{}, ends{}, freed{}, retainedCleanup{}, rawReturn{};
    void* node{}; void* output{}; void* preferred{};
};
struct Site { unsigned char* source{}; void* continuation{}; unsigned bytes{}; unsigned char original[8]{}, replacement[8]{}; };
HMODULE fixtureModule{}, kernelModule{};
HANDLE kernelFile = INVALID_HANDLE_VALUE;
Work works[2]{};
thread_local Work* current{};
Descriptor descriptor{};
unsigned checks{}, failures{}, scenario{}, workCount{};
bool configured{}, ran{};
unsigned char* relays{};
RUNTIME_FUNCTION* relayFunctions{};
Site sites[3]{};
StartFn startEntry{}; ExecuteFn executeEntry{}; void* inlineEntry{};
void Check(bool value) { ++checks; if (!value) ++failures; }
template<class T> T Symbol(const char* name)
{
    const auto address = GetProcAddress(kernelModule, name); Check(address != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(address)); std::memcpy(&result, &address, sizeof(result)); return result;
}
template<class T> void Write(Work* work, unsigned offset, T value)
{ *reinterpret_cast<T*>(static_cast<unsigned char*>(work->node) + offset) = value; }
template<class T> T Read(Work* work, unsigned offset)
{ return *reinterpret_cast<T*>(static_cast<unsigned char*>(work->node) + offset); }
bool RelativeJump(unsigned char* output, const void* target)
{
    const auto difference = reinterpret_cast<std::intptr_t>(target) - reinterpret_cast<std::intptr_t>(output + 5);
    if (difference < INT32_MIN || difference > INT32_MAX) return false;
    output[0] = 0xe9; const auto value = static_cast<std::int32_t>(difference); std::memcpy(output + 1, &value, 4); return true;
}
void* AllocateRelays()
{
    const auto base = reinterpret_cast<std::uintptr_t>(fixtureModule);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* image = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    auto candidate = (base + image->OptionalHeader.SizeOfImage + 0xffff) & ~std::uintptr_t(0xffff);
    const auto maximum = base + 0x70000000;
    for (; candidate < maximum; candidate += 0x10000) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(candidate), &memory, sizeof(memory))) break;
        if (memory.State == MEM_FREE && memory.RegionSize >= 4096) {
            if (auto* result = VirtualAlloc(reinterpret_cast<void*>(candidate), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE)) return result;
        } else if (memory.RegionSize > 0x10000) {
            const auto end = reinterpret_cast<std::uintptr_t>(memory.BaseAddress) + memory.RegionSize;
            if (end > candidate) candidate = (end - 1) & ~std::uintptr_t(0xffff);
        }
    }
    return nullptr;
}
bool PrepareOwnedRelays()
{
    const auto base = reinterpret_cast<DWORD64>(fixtureModule);
    DWORD64 actualImage{};
    auto* sourceFunction = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(&RtEngineInlineFixture_Start), &actualImage, nullptr);
    Check(sourceFunction && actualImage == base);
    if (!sourceFunction || actualImage != base) return false;
    Check(base + sourceFunction->BeginAddress == reinterpret_cast<DWORD64>(&RtEngineInlineFixture_Start)
        && base + sourceFunction->EndAddress == reinterpret_cast<DWORD64>(&RtEngineInlineFixture_StartEnd));
    const auto* sourceUnwind = reinterpret_cast<const unsigned char*>(base + sourceFunction->UnwindData);
    Check((sourceUnwind[0] & 7) == 1 && (sourceUnwind[0] >> 3) == 0);
    relays = static_cast<unsigned char*>(AllocateRelays()); Check(relays != nullptr); if (!relays) return false;
    relayFunctions = reinterpret_cast<RUNTIME_FUNCTION*>(relays + 0x300);
    sites[0] = { &RtEngineInlineFixture_BeginSite, &RtEngineInlineFixture_BeginContinuation, 7 };
    sites[1] = { &RtEngineInlineFixture_EndFreeSite, &RtEngineInlineFixture_CommonContinuation, 5 };
    sites[2] = { &RtEngineInlineFixture_EndRetainedSite, &RtEngineInlineFixture_CommonContinuation, 5 };
    const unsigned char begin[]{ 0xff,0x4b,0x60,0x48,0x8b,0x4b,0x68 };
    for (unsigned index = 0; index != 3; ++index) {
        auto& site = sites[index]; std::memcpy(site.original, site.source, site.bytes);
        MEMORY_BASIC_INFORMATION sourceMemory{};
        Check(VirtualQuery(site.source, &sourceMemory, sizeof(sourceMemory)) == sizeof(sourceMemory)
            && sourceMemory.AllocationBase == fixtureModule && sourceMemory.Type == MEM_IMAGE);
        if (index == 0) Check(std::memcmp(site.original, begin, sizeof(begin)) == 0);
        else {
            std::int32_t offset{}; std::memcpy(&offset, site.original + 1, 4);
            Check(site.original[0] == 0xe9 && site.source + 5 + offset == site.continuation);
        }
        auto* relay = relays + index * 64;
        const unsigned char call[]{ 0xff,0x15,0x2a,0,0,0 }; std::memcpy(relay, call, sizeof(call));
        std::memcpy(relay + 48, &inlineEntry, sizeof(inlineEntry));
        unsigned end = 6;
        if (index == 0) { std::memcpy(relay + end, begin, sizeof(begin)); end += static_cast<unsigned>(sizeof(begin)); }
        Check(RelativeJump(relay + end, site.continuation)); end += 5;
        auto* unwind = relays + 0x200 + index * 32;
        unwind[0] = 1 | (UNW_FLAG_CHAININFO << 3); unwind[1] = unwind[2] = unwind[3] = 0;
        std::memcpy(unwind + 4, sourceFunction, sizeof(*sourceFunction));
        relayFunctions[index] = { static_cast<DWORD>(reinterpret_cast<DWORD64>(relay) - base),
            static_cast<DWORD>(reinterpret_cast<DWORD64>(relay + end) - base), static_cast<DWORD>(reinterpret_cast<DWORD64>(unwind) - base) };
        descriptor.sites[index] = { site.source, relay + 6 };
        std::memset(site.replacement, 0x90, site.bytes);
        const auto delta = reinterpret_cast<std::intptr_t>(relay) - reinterpret_cast<std::intptr_t>(site.source + 5);
        Check(delta >= INT32_MIN && delta <= INT32_MAX);
        const auto relative = static_cast<std::int32_t>(delta); site.replacement[0] = 0xe9;
        std::memcpy(site.replacement + 1, &relative, 4);
    }
    if (failures) return false;
    DWORD prior{}; Check(VirtualProtect(relays, 4096, PAGE_EXECUTE_READ, &prior) != 0);
    Check(FlushInstructionCache(GetCurrentProcess(), relays, 4096) != 0);
    Check(RtlAddFunctionTable(relayFunctions, 3, base) != 0);
    for (unsigned index = 0; index != 3; ++index) {
        DWORD64 actualBase{};
        Check(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(descriptor.sites[index].relayReturnPc), &actualBase, nullptr)
            == &relayFunctions[index] && actualBase == base);
    }
    return failures == 0;
}
bool PatchOwnedSites()
{
    for (auto& site : sites) {
        DWORD old{}; Check(std::memcmp(site.source, site.original, site.bytes) == 0);
        if (!VirtualProtect(site.source, site.bytes, PAGE_EXECUTE_READWRITE, &old)) { Check(false); return false; }
        std::memcpy(site.source, site.replacement, site.bytes);
        Check(FlushInstructionCache(GetCurrentProcess(), site.source, site.bytes) != 0);
        DWORD unused{}; Check(VirtualProtect(site.source, site.bytes, old, &unused) != 0);
        Check(std::memcmp(site.source, site.replacement, site.bytes) == 0);
    }
    return failures == 0;
}
void Invoke(Work* work)
{
    Work* previous = current; current = work;
    work->rawReturn = startEntry(work, &work->id, reinterpret_cast<void*>(0x1234), &work->output);
    Check(work->rawReturn == 0 && work->output == nullptr); current = previous;
}
__declspec(noinline) std::uint8_t __cdecl StartOriginal(void* owner, void* enumerator, void* name, void* output)
{ Check(current == owner); ++current->starts; const auto result = RtEngineInlineFixture_Start(owner, enumerator, name, output); _ReadWriteBarrier(); return result; }
__declspec(noinline) std::uint32_t __cdecl ExecuteTarget(void*, void*) { return 0xaabb0001; }
__declspec(noinline) std::uint32_t __cdecl ExecuteOriginal(void* node, void* output)
{ Check(current && current->node == node); ++current->executes; *static_cast<unsigned char*>(output) = 0xa5; return 0x12340001; }
__declspec(noinline) std::uint8_t __cdecl ReleaseTarget(void*) { return 0xfe; }
__declspec(noinline) std::uint8_t __cdecl ReleaseOriginal(void*) { return 0x7f; }
__declspec(noinline) void __cdecl DetachTarget(void*) { _ReadWriteBarrier(); }
__declspec(noinline) void __cdecl DetachOriginal(void*) { _ReadWriteBarrier(); }
}

extern "C" void* RtEngineInlineFixture_Prepare(void* opaque, std::uint64_t rsp, Machine*, Machine*)
{
    auto* work = static_cast<Work*>(opaque); Check(work == current);
    for (unsigned index = 0; index != 4; ++index) {
        auto* frame = reinterpret_cast<Machine*>(rsp + 0x40 + index * 0x1b0);
        std::memset(frame, 0, sizeof(*frame)); frame->abiVersion = 1; frame->byteSize = sizeof(*frame);
    }
    work->node = VirtualAlloc(work->preferred, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    Check(work->node && (!work->preferred || work->node == work->preferred));
    if (!work->node) RaiseFailFastException(nullptr, nullptr, 0);
    Write(work, 0x58, work); Write(work, 0x10, 0x100000000ULL + work->id);
    Write(work, 0x18, std::uint32_t(2)); Write(work, 0x20, static_cast<void*>(&work->id));
    Write(work, 0x60, std::int32_t(1)); Write(work, 0x68, Link); work->output = work->node;
    unsigned char error{}; Check(executeEntry(work->node, &error) == 0x12340001 && error == 0xa5);
    return work->node;
}
extern "C" std::uint32_t RtEngineInlineFixture_Between(void* opaque, Machine* before, Machine* after)
{
    auto* work = static_cast<Work*>(opaque); Check(current == work); ++work->begins;
    Check(before->gpr[RizEventMidRbx] == reinterpret_cast<std::uint64_t>(work->node)
        && after->gpr[RizEventMidRcx] == Link && Read<std::int32_t>(work, 0x60) == 0);
    if (work->nested) Invoke(&works[1]);
    if (work->retained) {
        Write(work, 0x58, static_cast<void*>(nullptr)); Write(work, 0x10, std::uint64_t(0));
        Write(work, 0x18, std::uint32_t(0)); Write(work, 0x20, static_cast<void*>(nullptr));
        Write(work, 0x28, std::uint64_t(0)); Write(work, 0x30, std::uint64_t(0)); work->retainedCleanup = 1;
    } else {
        Check(VirtualFree(work->node, 0, MEM_RELEASE) != 0); work->freed = 1;
        MEMORY_BASIC_INFORMATION memory{}; Check(VirtualQuery(work->node, &memory, sizeof(memory)) == sizeof(memory) && memory.State == MEM_FREE);
    }
    return work->retained ? 1u : 0u;
}
extern "C" std::uint32_t RtEngineInlineFixture_Complete(void* opaque, Machine* before, Machine* after, const std::uint64_t* canaries)
{
    auto* work = static_cast<Work*>(opaque); Check(current == work); ++work->ends;
    Check(work->output == nullptr && (before->gpr[RizEventMidRax] & 0xff) == 0
        && before->gpr[RizEventMidRax] == after->gpr[RizEventMidRax]
        && canaries[0] == Canary && canaries[1] == Canary && canaries[2] == Canary);
    return 0;
}

FIXTURE_EXPORT std::uint32_t __cdecl RizEngineInlineManaged_Configure(const wchar_t* kernelPath, std::uint32_t mode, Descriptor* output)
{
    if (!kernelPath || !output || output->version != 1 || output->byteSize != sizeof(Descriptor) || configured || mode > 3) return 1;
    scenario = mode; workCount = mode >= 2 ? 2u : 1u;
    for (unsigned index = 0; index != 2; ++index) { works[index].id = index + 1; works[index].retained = mode == 1 ? 1u : 0u; }
    works[0].nested = mode == 2 ? 1u : 0u;
    Check(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&RtEngineInlineFixture_Start), &fixtureModule) != 0);
    kernelFile = CreateFileW(kernelPath, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    Check(kernelFile != INVALID_HANDLE_VALUE);
    kernelModule = LoadLibraryExW(kernelPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32); Check(kernelModule != nullptr);
    if (!kernelModule || !fixtureModule || failures) return 2;
    auto configure = Symbol<decltype(&RizEngineNodeContext_Configure)>("RizEngineNodeContext_Configure");
    auto info = Symbol<decltype(&RizEngineNodeContext_GetInfo)>("RizEngineNodeContext_GetInfo");
    auto getEntry = Symbol<decltype(&RizEngineNodeContext_GetEntry)>("RizEngineNodeContext_GetEntry");
    auto configureInline = Symbol<decltype(&RizEngineNodeContext_ConfigureInline)>("RizEngineNodeContext_ConfigureInline");
    auto inlineInfo = Symbol<decltype(&RizEngineNodeContext_GetInlineInfo)>("RizEngineNodeContext_GetInlineInfo");
    auto getInlineEntry = Symbol<decltype(&RizEngineNodeContext_GetInlineEntry)>("RizEngineNodeContext_GetInlineEntry");
    if (failures) return 3;
    const RizEngineNodeConfigV1 configurations[]{
        { 1, sizeof(RizEngineNodeConfigV1), reinterpret_cast<void*>(&RtEngineInlineFixture_Start), reinterpret_cast<void*>(&StartOriginal) },
        { 1, sizeof(RizEngineNodeConfigV1), reinterpret_cast<void*>(&ExecuteTarget), reinterpret_cast<void*>(&ExecuteOriginal) },
        { 1, sizeof(RizEngineNodeConfigV1), reinterpret_cast<void*>(&ReleaseTarget), reinterpret_cast<void*>(&ReleaseOriginal) },
        { 1, sizeof(RizEngineNodeConfigV1), reinterpret_cast<void*>(&DetachTarget), reinterpret_cast<void*>(&DetachOriginal) } };
    RizEngineNodeInfoV1 startInfo{};
    for (unsigned kind = 0; kind != 4; ++kind) {
        Check(configure(kind, Slot, &configurations[kind]) == 0);
        RizEngineNodeInfoV1 value{ 1, sizeof(value) }; Check(info(kind, Slot, &value) == 0 && value.attached == 0 && value.unknown == 0);
        if (kind == 0) startInfo = value;
    }
    inlineEntry = getInlineEntry(); startEntry = reinterpret_cast<StartFn>(getEntry(0, Slot)); executeEntry = reinterpret_cast<ExecuteFn>(getEntry(1, Slot));
    Check(inlineEntry && startEntry && executeEntry);
    for (unsigned i = 0; i != 16; ++i) for (unsigned j = 0; j != 16; ++j) RtEngineInlineFixture_XmmInput[i][j] = static_cast<unsigned char>(0x10 + i * 7 + j);
    if (failures || !PrepareOwnedRelays()) return 4;
    RizEngineInlineConfigV1 side{ 1, sizeof(side), Slot, GetCurrentThreadId(), startInfo.epoch, startInfo.target, 0 };
    std::memcpy(side.sites, descriptor.sites, sizeof(side.sites)); Check(configureInline(Slot, &side) == 0);
    RizEngineInlineInfoV1 sideInfo{ 1, sizeof(sideInfo) }; Check(inlineInfo(Slot, &sideInfo) == 0 && sideInfo.attached == 0 && sideInfo.unknown == 0);
    if (failures || !PatchOwnedSites()) return 5;
    descriptor.version = 1; descriptor.byteSize = sizeof(descriptor); descriptor.scenario = scenario; descriptor.slot = Slot;
    descriptor.workCount = workCount; descriptor.configured = 1; descriptor.mainThreadId = GetCurrentThreadId(); descriptor.failures = failures;
    descriptor.imageBase = fixtureModule; descriptor.kernel = kernelModule; descriptor.inlineEntry = inlineEntry; descriptor.relayBase = relays;
    for (unsigned index = 0; index != 2; ++index) descriptor.works[index] = { works[index].id, works[index].retained, &works[index], &works[index].id, 0x100000000ULL + works[index].id };
    descriptor.nativeChecks = checks; configured = true; *output = descriptor; return 0;
}
FIXTURE_EXPORT std::uint32_t __cdecl RizEngineInlineManaged_Run(Receipt* output)
{
    if (!output || output->version != 1 || output->byteSize != sizeof(Receipt) || !configured || ran) return 1;
    ran = true; Invoke(&works[0]);
    if (scenario == 3) { works[1].preferred = works[0].node; Invoke(&works[1]); Check(works[1].node == works[0].node); }
    Receipt result{ 1, sizeof(result), scenario, workCount };
    result.imageBase = fixtureModule; result.relayBase = relays;
    for (unsigned index = 0; index != workCount; ++index) {
        const auto& work = works[index]; Check(work.starts == 1 && work.executes == 1 && work.begins == 1 && work.ends == 1 && work.output == nullptr);
        result.works[index] = { work.id, work.retained, work.starts, work.executes, work.begins, work.ends, work.freed, work.retainedCleanup,
            const_cast<Work*>(&work), const_cast<unsigned*>(&work.id), work.node, work.output, 0x100000000ULL + work.id, work.rawReturn, 0 };
        result.freeCount += work.freed; result.retainedCount += work.retainedCleanup;
        result.originalStarts += work.starts; result.originalExecutes += work.executes;
    }
    result.addressReuse = scenario == 3 && works[0].node == works[1].node ? 1u : 0u;
    result.checks = checks; result.failures = failures; *output = result; return failures ? 2u : 0u;
}
