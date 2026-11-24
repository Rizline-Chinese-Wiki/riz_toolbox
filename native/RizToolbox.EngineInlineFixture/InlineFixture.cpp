#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/MidContext.h"

// A bounded, self-owned executable fixture. The three instruction windows in
// InlineCaller.asm are its only patch targets. The kernel remains loaded and
// every relay/function table stays allocated until this child process exits.
extern "C" {
std::uint8_t __cdecl RtEngineInlineFixture_Start(void*, void*, void*, void*);
void RtEngineInlineFixture_ClobberVolatile();
extern unsigned char RtEngineInlineFixture_BeginSite, RtEngineInlineFixture_BeginContinuation;
extern unsigned char RtEngineInlineFixture_EndFreeSite, RtEngineInlineFixture_EndRetainedSite;
extern unsigned char RtEngineInlineFixture_CommonContinuation, RtEngineInlineFixture_StartEnd;
__declspec(align(16)) unsigned char RtEngineInlineFixture_XmmInput[16][16]{};
std::uint32_t RtEngineInlineFixture_InputMxcsr = 0x3f80, RtEngineInlineFixture_ClobberMxcsr = 0x1f80;
std::uint16_t RtEngineInlineFixture_InputFpcsr = 0x077f, RtEngineInlineFixture_ClobberFpcsr = 0x037f;
}
namespace {
using Machine = RizEventMidFrameV1;
using StartFn = std::uint8_t(__cdecl*)(void*, void*, void*, void*);
using ExecuteFn = std::uint32_t(__cdecl*)(void*, void*);
constexpr DWORD OriginalSeh = 0xe04c4f52, ObserverSeh = 0xe04c4f42;
constexpr DWORD IncomingLastError = 0x23456789, BeginLastError = 0x1a2b3c4d, EndLastError = 0x5e6f7081;
constexpr std::uint64_t NodeLink = 0x76543210abcdef09ULL, Canary = 0xaabbccddeeff1122ULL;
constexpr unsigned WorkLimit = 8, EventLimit = 64, TypedLimit = 64;
struct CppFault { std::uint64_t seal; };
struct Work {
    unsigned id{}, retained{}, nested{}, originalFault{}, observerFault{}, duplicateBegin{}, duplicateEnd{}, lateAttach{};
    unsigned betweenCalls{}, completeCalls{}, enters{}, exits{}, sideEvents{}, begins{}, ends{}, executes{};
    unsigned caught{}, freed{}, retainedCleanup{}, sameAddress{}, expectUnknown{}, expectedReferences = 1;
    void* node{}; void* output{}; void* preferred{}; Work* parent{};
    std::uint64_t actualSequence{}, sourceRsp{}, sourceReturnPc{};
    Machine* beginBeforeLive{}; Machine* beginAfterLive{};
    Machine beginBefore{}, beginAfter{}, endBefore{}, endAfter{};
    RizEngineNodeFrameV1 typedStart{}; RizEngineNodeCompletionV1 completion{};
    RizEngineInlineFrameV1 sideExit{};
    unsigned sideExited{};
};
struct SiteReceipt {
    std::uint64_t site{}, relay{}, returnPc{}, continuation{};
    std::uint32_t patchSize{}, patched{};
    unsigned char original[8]{}, replacement[8]{}, code[64]{}, unwind[16]{};
    RUNTIME_FUNCTION function{};
};
struct RawReceipt {
    std::uint32_t version = 1, byteSize = sizeof(RawReceipt), machineSize = sizeof(Machine), workCount{};
    std::uint32_t eventCount{}, typedCount{}, checks{}, failures{};
    std::uint32_t helperWalks{}, relayWalks{}, virtualUnwinds{}, realFreeCount{}, retainedCount{}, addressReuseCount{};
    std::uint32_t allStatePairs{}, beginReplayPairs{}, canaryChecks{}, sehCaught{}, cppCaught{}, observerFaults{};
    std::uint32_t workerCalls{}, lateAttachCalls{}, helperCetCompat{}, processCetEnabled{};
    std::uint32_t fixtureCetCompat{}, processCetPolicyKnown{}, savedLastErrorChecks{}, reserved{};
    std::uint64_t imageBase{}, relayBase{}, sourceBegin{}, sourceEnd{}, helperEntry{}, firstSequence{}, secondSequence{};
    RUNTIME_FUNCTION sourceFunction{};
    unsigned char sourceUnwind[96]{};
    SiteReceipt sites[3]{};
    RizEngineNodeInfoV1 startInfo{}, executeInfo{};
    RizEngineInlineInfoV1 inlineInfo{};
    RizEngineInlineFrameV1 events[EventLimit]{};
    RizEngineNodeFrameV1 typed[TypedLimit]{};
    RizEngineNodeCompletionV1 typedCompletions[TypedLimit]{};
    Work works[WorkLimit]{};
} raw;
HMODULE kernel{};
HANDLE kernelFile = INVALID_HANDLE_VALUE;
DWORD mainThread{};
const wchar_t* scenario{};
thread_local Work* current{};
StartFn startEntry{};
ExecuteFn executeEntry{};
void* inlineEntry{};
decltype(&RizEngineNodeContext_GetInfo) getInfo{};
decltype(&RizEngineNodeContext_Configure) configure{};
decltype(&RizEngineNodeContext_Attach) attach{};
decltype(&RizEngineNodeContext_GetInlineInfo) getInlineInfo{};
decltype(&RizEngineNodeContext_AttachInline) attachInline{};
std::uint64_t inlineEpoch{};
unsigned char* relayBlock{};
PRUNTIME_FUNCTION relayFunctions{};
RizEngineInlineObserverV1 inlineObserver{};
bool workerScenario{}, wrongSite{}, missingBegin{}, missingEnd{}, lateScenario{};

void Check(bool value, const char* expression, int line)
{
    ++raw.checks;
    if (!value) { ++raw.failures; std::fprintf(stderr, "FAILED InlineFixture line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(const char* name)
{
    const auto address = GetProcAddress(kernel, name); CHECK(address != nullptr);
    T value{}; static_assert(sizeof(value) == sizeof(address)); std::memcpy(&value, &address, sizeof(value)); return value;
}
template<class T> T ReadNode(Work* work, unsigned offset)
{ return *reinterpret_cast<T*>(static_cast<unsigned char*>(work->node) + offset); }
template<class T> void WriteNode(Work* work, unsigned offset, T value)
{ *reinterpret_cast<T*>(static_cast<unsigned char*>(work->node) + offset) = value; }
bool Named(const wchar_t* name) { return std::wcscmp(scenario, name) == 0; }
bool CetCompatible(HMODULE module)
{
    const auto base = reinterpret_cast<std::uintptr_t>(module);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) return false;
    const auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    if (!directory.VirtualAddress || directory.Size % sizeof(IMAGE_DEBUG_DIRECTORY) != 0) return false;
    const auto* entries = reinterpret_cast<const IMAGE_DEBUG_DIRECTORY*>(base + directory.VirtualAddress);
    for (unsigned i = 0; i != directory.Size / sizeof(*entries); ++i)
        if (entries[i].Type == 20 && entries[i].AddressOfRawData && entries[i].SizeOfData >= 2)
            return (*reinterpret_cast<const std::uint16_t*>(base + entries[i].AddressOfRawData) & 1) != 0;
    return false;
}
bool IsRelay(DWORD64 address, unsigned* found = nullptr)
{
    for (unsigned i = 0; i != 3; ++i)
        if (address >= raw.sites[i].relay && address < raw.sites[i].relay + 32) {
            if (found) *found = i; return true;
        }
    return false;
}
void FillContext(CONTEXT* context, const Machine& before)
{
    *context = {}; context->ContextFlags = CONTEXT_ALL;
    context->Rax = before.gpr[0]; context->Rcx = before.gpr[1]; context->Rdx = before.gpr[2]; context->Rbx = before.gpr[3];
    context->Rsp = before.gpr[4]; context->Rbp = before.gpr[5]; context->Rsi = before.gpr[6]; context->Rdi = before.gpr[7];
    context->R8 = before.gpr[8]; context->R9 = before.gpr[9]; context->R10 = before.gpr[10]; context->R11 = before.gpr[11];
    context->R12 = before.gpr[12]; context->R13 = before.gpr[13]; context->R14 = before.gpr[14]; context->R15 = before.gpr[15];
    context->EFlags = static_cast<DWORD>(before.rflags); context->MxCsr = before.mxcsr;
    std::memcpy(&context->Xmm0, before.xmm, sizeof(before.xmm));
}
void UnwindOne(const Machine& before, DWORD64 image, PRUNTIME_FUNCTION function,
    DWORD64 pc, DWORD64 rsp, DWORD64 r13, DWORD64 returnPc)
{
    CONTEXT context{}; FillContext(&context, before); context.Rip = pc; context.Rsp = rsp; context.R13 = r13;
    PVOID data{}; DWORD64 establisher{};
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, image, pc, function, &context, &data, &establisher, nullptr);
    CHECK(context.Rip == returnPc && context.Rsp == before.gpr[4]);
    CHECK(context.Rbx == before.gpr[3] && context.Rbp == before.gpr[5] && context.Rsi == before.gpr[6]
        && context.Rdi == before.gpr[7] && context.R12 == before.gpr[12] && context.R13 == before.gpr[13]
        && context.R14 == before.gpr[14] && context.R15 == before.gpr[15]);
    for (unsigned i = 6; i != 16; ++i)
        CHECK(std::memcmp(reinterpret_cast<const unsigned char*>(&context.Xmm0) + i * 16, &before.xmm[i], 16) == 0);
    ++raw.virtualUnwinds;
}
void PartialUnwind(Work* work, unsigned site)
{
    const auto* before = site == 0 ? work->beginBeforeLive
        : reinterpret_cast<const Machine*>(work->sourceRsp + 0x3a0);
    DWORD64 image{}; const auto function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(inlineEntry), &image, nullptr);
    CHECK(function && image == reinterpret_cast<DWORD64>(kernel)); if (!function) return;
    const auto begin = image + function->BeginAddress, end = image + function->EndAddress;
    const unsigned char prefix[]{ 0x41,0x55,0x9c,0x48,0x81,0xec,0xe8,0x01,0x00,0x00,0x4c,0x8d,0xac,0x24,0x80,0,0,0 };
    const unsigned char epilog[]{ 0x49,0x8d,0xa5,0x70,0x01,0,0,0x41,0x5d,0xc3 };
    CHECK(begin == reinterpret_cast<DWORD64>(inlineEntry));
    CHECK(std::memcmp(reinterpret_cast<const void*>(begin), prefix, sizeof(prefix)) == 0);
    CHECK(end > begin + 32 && std::memcmp(reinterpret_cast<const void*>(end - 10), epilog, sizeof(epilog)) == 0);
    const auto entry = work->sourceRsp - 8, base = entry - 0x1f8, ret = raw.sites[site].returnPc;
    UnwindOne(*before, image, function, begin, entry, before->gpr[13], ret);
    UnwindOne(*before, image, function, begin + 2, entry - 8, before->gpr[13], ret);
    UnwindOne(*before, image, function, begin + 3, entry - 16, before->gpr[13], ret);
    UnwindOne(*before, image, function, begin + 10, base, before->gpr[13], ret);
    UnwindOne(*before, image, function, begin + 18, base, base + 0x80, ret);
    UnwindOne(*before, image, function, end - 10, base, base + 0x80, ret);
    UnwindOne(*before, image, function, end - 3, base + 0x1f0, base + 0x80, ret);
    UnwindOne(*before, image, function, end - 1, entry, before->gpr[13], ret);
}
void WalkUnwind(CONTEXT context, Work* work, bool requireRelay)
{
    bool sawHelper = false, sawRelay = false, sawSourceReturn = false;
    DWORD64 helperImage{}; const auto helperFunction = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(inlineEntry), &helperImage, nullptr);
    CHECK(helperFunction != nullptr); if (!helperFunction) return;
    for (unsigned n = 0; n != 64 && context.Rip && context.Rsp; ++n) {
        if (context.Rip >= helperImage + helperFunction->BeginAddress && context.Rip < helperImage + helperFunction->EndAddress) sawHelper = true;
        if (context.Rip == work->sourceReturnPc) { sawSourceReturn = true; break; }
        unsigned site{}; const bool inRelay = IsRelay(context.Rip, &site);
        DWORD64 image{}; const auto function = RtlLookupFunctionEntry(context.Rip, &image, nullptr);
        const auto oldRsp = context.Rsp;
        if (inRelay) {
            sawRelay = true; CHECK(function == &relayFunctions[site] && image == raw.imageBase);
            CHECK(context.Rsp == work->sourceRsp && context.Rip == raw.sites[site].returnPc);
        }
        if (function) {
            PVOID data{}; DWORD64 establisher{};
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, image, context.Rip, function, &context, &data, &establisher, nullptr);
        } else { context.Rip = *reinterpret_cast<const DWORD64*>(context.Rsp); context.Rsp += 8; }
        CHECK(context.Rsp >= oldRsp && context.Rsp - oldRsp < 0x100000);
        if (inRelay) {
            CHECK(context.Rip == work->sourceReturnPc && context.Rsp == work->sourceRsp + 0x820);
            const auto* saved = reinterpret_cast<const DWORD64*>(work->sourceRsp + 0x7d8);
            CHECK(context.R15 == saved[0] && context.R14 == saved[1] && context.R13 == saved[2] && context.R12 == saved[3]
                && context.Rdi == saved[4] && context.Rsi == saved[5] && context.Rbp == saved[6] && context.Rbx == saved[7]);
            for (unsigned i = 6; i != 16; ++i)
                CHECK(std::memcmp(reinterpret_cast<const unsigned char*>(&context.Xmm0) + i * 16,
                    reinterpret_cast<const void*>(work->sourceRsp + 0x700 + (i - 6) * 16), 16) == 0);
            ++raw.relayWalks;
        }
    }
    CHECK(sawSourceReturn); if (requireRelay) CHECK(sawHelper && sawRelay);
    ++raw.helperWalks;
}
LONG ObserverFilter(EXCEPTION_POINTERS* exception)
{
    CHECK(exception->ExceptionRecord->ExceptionCode == ObserverSeh || exception->ExceptionRecord->ExceptionCode == 0xe06d7363);
    WalkUnwind(*exception->ContextRecord, current, true); return EXCEPTION_CONTINUE_SEARCH;
}
void ThrowObserver(unsigned kind)
{
    if (kind == 1) RaiseException(ObserverSeh, 0, 0, nullptr);
    else if (kind == 2) throw CppFault{ 0xcafef00d12345678ULL };
}
void FaultObserverWithWalk(unsigned kind)
{
    __try { ThrowObserver(kind); }
    __except (ObserverFilter(GetExceptionInformation())) { }
}
std::uintptr_t __cdecl TypedEnter(const RizEngineNodeFrameV1* frame, void* user)
{
    CHECK(user == &raw && current && frame->abiVersion == 1 && frame->byteSize == 112 && frame->sequence);
    CHECK(frame->kind <= RizEngineNodeExecute && raw.typedCount < TypedLimit);
    if (!current || raw.typedCount >= TypedLimit) return 0;
    const auto index = raw.typedCount++; raw.typed[index] = *frame;
    if (frame->kind == RizEngineNodeStart) {
        auto* work = static_cast<Work*>(frame->arg0); CHECK(work == current);
        work->actualSequence = frame->sequence; work->typedStart = *frame; ++work->enters;
        CHECK(frame->arg1 == &work->id && frame->arg3 == &work->output && frame->snapshotFlags == 0);
        for (unsigned i = 0; i != index; ++i) CHECK(raw.typed[i].sequence != frame->sequence);
    } else {
        CHECK(frame->node == current->node && frame->owner == current && frame->enumCache == &current->id
            && frame->enumType == 2 && frame->referenceCount == 1 && frame->snapshotFlags == 3);
        ++current->executes;
    }
    SetLastError(0xfedcba97);
    return static_cast<std::uintptr_t>(index + 1);
}
void __cdecl TypedExit(const RizEngineNodeFrameV1* frame, std::uintptr_t token,
    const RizEngineNodeCompletionV1* completion, void* user)
{
    CHECK(user == &raw && token != 0 && token <= raw.typedCount);
    if (!token || token > raw.typedCount) return;
    CHECK(std::memcmp(frame, &raw.typed[token - 1], sizeof(*frame)) == 0);
    raw.typedCompletions[token - 1] = *completion;
    CHECK(completion->abiVersion == 1 && completion->byteSize == 32 && completion->reserved == 0);
    if (frame->kind == RizEngineNodeStart) {
        auto* work = static_cast<Work*>(frame->arg0); CHECK(work == current); work->completion = *completion; ++work->exits;
        if (work->sideExited) {
            CHECK(work->sideExited == 1 && work->sideExit.startExitFlags == completion->flags
                && work->sideExit.rawStartReturn == completion->returnByte
                && work->sideExit.startOutputNode == completion->startOutputNode);
        }
        if (work->originalFault) CHECK(completion->flags == RizEngineNodeAbnormal && completion->outputFlags == 0);
        else CHECK(completion->flags == (RizEngineNodeReturned | RizEngineNodeReturnByteValid)
            && completion->returnByte == 0 && completion->outputFlags == 3 && completion->startOutputNode == nullptr);
    } else CHECK(completion->flags == 9 && completion->returnByte == 0x12340001);
    SetLastError(0xfedcba96);
}
void __cdecl SideObserve(const RizEngineInlineFrameV1* frame, void* user)
{
    auto* work = current;
    CHECK(user == &raw && work && frame->abiVersion == 1 && frame->byteSize == 144 && raw.eventCount < EventLimit);
    if (!work || raw.eventCount >= EventLimit) return;
    const auto index = raw.eventCount++; raw.events[index] = *frame; ++work->sideEvents;
    CHECK(frame->startSequence == work->actualSequence && frame->startSequence == work->typedStart.sequence);
    CHECK(frame->epoch == inlineEpoch && frame->startSlot == 0 && frame->probeSlot == 0 && frame->threadId == GetCurrentThreadId());
    CHECK(frame->parentStartSequence == (work->parent ? work->parent->actualSequence : 0));
    CHECK(frame->depth == (work->parent ? 2u : 1u) && frame->outputSlot == &work->output);
    CHECK(frame->eventSequence && (index == 0 || frame->eventSequence > raw.events[index - 1].eventSequence));
    if (!workerScenario) CHECK((frame->flags & RizEngineInlineParentMatched) != 0);
    if (frame->kind == RizEngineInlineStartEntered) {
        CHECK(frame->node == nullptr && frame->owner == work && frame->enumCache == &work->id
            && !frame->enumHandle && !frame->referenceCount && !frame->relayReturnPc);
    } else if (frame->kind == RizEngineInlineBegin || frame->kind == RizEngineInlineEndFree || frame->kind == RizEngineInlineEndRetained) {
        const unsigned site = frame->kind == RizEngineInlineBegin ? 0u : frame->kind == RizEngineInlineEndFree ? 1u : 2u;
        CHECK(frame->sitePc == reinterpret_cast<void*>(raw.sites[site].site)
            && frame->relayReturnPc == reinterpret_cast<void*>(raw.sites[site].returnPc));
        if (!missingBegin && !wrongSite && !workerScenario)
            CHECK(frame->node == work->node && frame->owner == work && frame->enumCache == &work->id
                && frame->enumType == 2 && frame->referenceCount == 1
                && frame->enumHandle == 0x100000000ULL + work->id && (frame->flags & 7) == 3);
        if (frame->kind == RizEngineInlineBegin) ++work->begins;
        else {
            ++work->ends; MEMORY_BASIC_INFORMATION state{};
            CHECK(VirtualQuery(work->node, &state, sizeof(state)) == sizeof(state));
            CHECK(state.State == static_cast<DWORD>(work->retained ? MEM_COMMIT : MEM_FREE));
            CHECK(work->output == nullptr && (work->freed || work->retainedCleanup));
            CHECK(frame->rawStartReturn == 0);
        }
        CONTEXT context{}; RtlCaptureContext(&context); WalkUnwind(context, work, true); PartialUnwind(work, site);
        if (frame->kind == RizEngineInlineBegin && work->observerFault) {
            ++raw.observerFaults; FaultObserverWithWalk(work->observerFault);
        }
    } else {
        CHECK(frame->kind == RizEngineInlineStartExited && work->exits == 0);
        work->sideExit = *frame; ++work->sideExited;
        CHECK(frame->startExitFlags == (work->originalFault ? RizEngineNodeAbnormal
            : RizEngineNodeReturned | RizEngineNodeReturnByteValid));
        if (!work->originalFault) CHECK(frame->rawStartReturn == 0 && frame->startOutputNode == nullptr
            && (frame->flags & (RizEngineInlineOutputRequested | RizEngineInlineOutputRead)) == 0x300);
        else CHECK((frame->flags & RizEngineInlineAbnormal) && (frame->flags & RizEngineInlineMissingEnd));
    }
    SetLastError(0xfedcba98); RtEngineInlineFixture_ClobberVolatile();
}
__declspec(noinline) std::uint8_t __cdecl StartOriginal(void* owner, void* enumerator, void* name, void* output)
{
    const auto incoming = GetLastError();
    CHECK(incoming == IncomingLastError); ++raw.savedLastErrorChecks;
    const auto value = RtEngineInlineFixture_Start(owner, enumerator, name, output);
    _ReadWriteBarrier(); return value;
}
__declspec(noinline) std::uint32_t __cdecl ExecuteTarget(void*, void*) { return 0xa5a50001; }
__declspec(noinline) std::uint32_t __cdecl ExecuteOriginal(void* node, void* output)
{ CHECK(current && current->node == node); *static_cast<unsigned char*>(output) = 0xa5; return 0x12340001; }

void* AllocateRelayAboveImage()
{
    const auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    auto next = (base + nt->OptionalHeader.SizeOfImage + 0xffff) & ~std::uintptr_t(0xffff);
    const auto maximum = base + 0x70000000;
    for (; next < maximum; next += 0x10000) {
        MEMORY_BASIC_INFORMATION state{};
        if (!VirtualQuery(reinterpret_cast<void*>(next), &state, sizeof(state))) break;
        if (state.State == MEM_FREE && state.RegionSize >= 4096) {
            if (void* block = VirtualAlloc(reinterpret_cast<void*>(next), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE)) return block;
        } else if (state.RegionSize > 0x10000) {
            const auto end = reinterpret_cast<std::uintptr_t>(state.BaseAddress) + state.RegionSize;
            if (end > next) next = (end - 1) & ~std::uintptr_t(0xffff);
        }
    }
    return nullptr;
}
bool Rel32(unsigned char* at, const void* target)
{
    const auto delta = reinterpret_cast<std::intptr_t>(target) - reinterpret_cast<std::intptr_t>(at + 5);
    if (delta < INT32_MIN || delta > INT32_MAX) return false;
    at[0] = 0xe9; const auto relative = static_cast<std::int32_t>(delta); std::memcpy(at + 1, &relative, 4); return true;
}
bool PublishRelays()
{
    raw.imageBase = reinterpret_cast<DWORD64>(GetModuleHandleW(nullptr));
    raw.sourceBegin = reinterpret_cast<DWORD64>(&RtEngineInlineFixture_Start);
    raw.sourceEnd = reinterpret_cast<DWORD64>(&RtEngineInlineFixture_StartEnd);
    raw.helperEntry = reinterpret_cast<DWORD64>(inlineEntry);
    DWORD64 image{}; const auto source = RtlLookupFunctionEntry(raw.sourceBegin, &image, nullptr);
    CHECK(source && image == raw.imageBase); if (!source) return false;
    CHECK(image + source->BeginAddress == raw.sourceBegin && image + source->EndAddress == raw.sourceEnd);
    raw.sourceFunction = *source;
    const auto* sourceUnwind = reinterpret_cast<const unsigned char*>(image + source->UnwindData);
    const auto codeBytes = static_cast<unsigned>(4 + ((sourceUnwind[2] + 1) & ~1u) * 2);
    CHECK(codeBytes <= sizeof(raw.sourceUnwind) && (sourceUnwind[0] & 7) == 1 && (sourceUnwind[0] >> 3) == 0);
    if (codeBytes > sizeof(raw.sourceUnwind)) return false;
    std::memcpy(raw.sourceUnwind, sourceUnwind, codeBytes);
    relayBlock = static_cast<unsigned char*>(AllocateRelayAboveImage()); CHECK(relayBlock); if (!relayBlock) return false;
    raw.relayBase = reinterpret_cast<DWORD64>(relayBlock);
    const void* sites[]{ &RtEngineInlineFixture_BeginSite, &RtEngineInlineFixture_EndFreeSite, &RtEngineInlineFixture_EndRetainedSite };
    const void* continuations[]{ &RtEngineInlineFixture_BeginContinuation, &RtEngineInlineFixture_CommonContinuation, &RtEngineInlineFixture_CommonContinuation };
    const unsigned char beginBytes[]{ 0xff,0x4b,0x60,0x48,0x8b,0x4b,0x68 };
    relayFunctions = reinterpret_cast<PRUNTIME_FUNCTION>(relayBlock + 0x300);
    for (unsigned i = 0; i != 3; ++i) {
        auto& receipt = raw.sites[i]; receipt.site = reinterpret_cast<DWORD64>(sites[i]);
        receipt.continuation = reinterpret_cast<DWORD64>(continuations[i]); receipt.patchSize = i == 0 ? 7u : 5u;
        std::memcpy(receipt.original, sites[i], receipt.patchSize);
        if (i == 0) CHECK(std::memcmp(sites[i], beginBytes, sizeof(beginBytes)) == 0);
        else {
            CHECK(receipt.original[0] == 0xe9); std::int32_t delta{}; std::memcpy(&delta, receipt.original + 1, 4);
            CHECK(receipt.site + 5 + delta == receipt.continuation);
        }
        auto* relay = relayBlock + i * 64; receipt.relay = reinterpret_cast<DWORD64>(relay); receipt.returnPc = receipt.relay + 6;
        // FF15 indirect CALL owns a genuine return address, including CET shadow
        // stack. The following E9 replays/transfers without rewriting that PC.
        const unsigned char call[]{ 0xff,0x15,0x2a,0,0,0 }; std::memcpy(relay, call, sizeof(call));
        std::memcpy(relay + 48, &inlineEntry, sizeof(inlineEntry));
        unsigned end = 6;
        if (i == 0) { std::memcpy(relay + 6, beginBytes, sizeof(beginBytes)); end += sizeof(beginBytes); }
        CHECK(Rel32(relay + end, continuations[i])); end += 5;
        auto* unwind = relayBlock + 0x200 + i * 32;
        unwind[0] = 1 | (UNW_FLAG_CHAININFO << 3); unwind[1] = 0; unwind[2] = 0; unwind[3] = 0;
        std::memcpy(unwind + 4, source, sizeof(*source));
        relayFunctions[i] = { static_cast<DWORD>(receipt.relay - image),
            static_cast<DWORD>(receipt.relay + end - image), static_cast<DWORD>(reinterpret_cast<DWORD64>(unwind) - image) };
        receipt.function = relayFunctions[i]; std::memcpy(receipt.unwind, unwind, sizeof(receipt.unwind));
        std::memcpy(receipt.code, relay, sizeof(receipt.code));
        receipt.patched = (i == 0 ? !missingBegin : !missingEnd) ? 1u : 0u;
        if (receipt.patched) {
            std::memset(receipt.replacement, 0x90, receipt.patchSize);
            const auto delta = static_cast<std::int64_t>(receipt.relay) - static_cast<std::int64_t>(receipt.site + 5);
            CHECK(delta >= INT32_MIN && delta <= INT32_MAX); const auto relative = static_cast<std::int32_t>(delta);
            receipt.replacement[0] = 0xe9; std::memcpy(receipt.replacement + 1, &relative, 4);
        }
    }
    DWORD old{}; CHECK(VirtualProtect(relayBlock, 4096, PAGE_EXECUTE_READ, &old));
    CHECK(FlushInstructionCache(GetCurrentProcess(), relayBlock, 4096));
    CHECK(RtlAddFunctionTable(relayFunctions, 3, raw.imageBase));
    for (unsigned i = 0; i != 3; ++i) {
        DWORD64 actualBase{}; CHECK(RtlLookupFunctionEntry(raw.sites[i].returnPc, &actualBase, nullptr) == &relayFunctions[i]
            && actualBase == raw.imageBase);
    }
    return raw.failures == 0;
}
bool PatchOwnedSites()
{
    for (auto& receipt : raw.sites) if (receipt.patched) {
        auto* site = reinterpret_cast<void*>(receipt.site); DWORD previous{};
        CHECK(std::memcmp(site, receipt.original, receipt.patchSize) == 0);
        CHECK(VirtualProtect(site, receipt.patchSize, PAGE_EXECUTE_READWRITE, &previous));
        std::memcpy(site, receipt.replacement, receipt.patchSize);
        CHECK(FlushInstructionCache(GetCurrentProcess(), site, receipt.patchSize));
        DWORD unused{}; CHECK(VirtualProtect(site, receipt.patchSize, previous, &unused));
        CHECK(std::memcmp(site, receipt.replacement, receipt.patchSize) == 0);
    }
    return raw.failures == 0;
}
void CompareMachine(const Machine& before, const Machine& after, bool begin, Work* work)
{
    CHECK(before.abiVersion == 1 && after.abiVersion == 1 && before.byteSize == 432 && after.byteSize == 432);
    CHECK(before.mxcsr == after.mxcsr && before.fpcsr == after.fpcsr && before.mxcsr == 0x3f80 && before.fpcsr == 0x077f);
    CHECK((before.rflags & 0x400) && (after.rflags & 0x400));
    const std::uint64_t arithmeticExceptCarry = 0x8d4;
    const auto refs = static_cast<std::int32_t>(work->expectedReferences) - 1;
    const auto bits = static_cast<std::uint32_t>(refs);
    const auto low = bits & 0xff; unsigned parity = low;
    parity ^= parity >> 4; parity ^= parity >> 2; parity ^= parity >> 1;
    const auto resultFlags = ((bits == 0) ? 0x40ULL : 0ULL) | ((bits & 0x80000000) ? 0x80ULL : 0ULL)
        | (((parity & 1) == 0) ? 4ULL : 0ULL) | (((work->expectedReferences & 15) == 0) ? 0x10ULL : 0ULL)
        | ((work->expectedReferences == 0x80000000u) ? 0x800ULL : 0ULL);
    CHECK(after.rflags == (begin ? ((before.rflags & ~arithmeticExceptCarry) | resultFlags) : before.rflags));
    for (unsigned i = 0; i != 16; ++i) {
        CHECK(after.gpr[i] == (begin && i == RizEventMidRcx ? NodeLink : before.gpr[i]));
        CHECK(std::memcmp(&before.xmm[i], &after.xmm[i], 16) == 0);
    }
    ++raw.allStatePairs; if (begin) ++raw.beginReplayPairs;
}
void Invoke(Work* work)
{
    Work* previous = current; current = work;
    SetLastError(IncomingLastError);
    const auto result = startEntry(work, &work->id, reinterpret_cast<void*>(0x1234), &work->output);
    const auto outgoing = GetLastError();
    CHECK(outgoing == EndLastError); ++raw.savedLastErrorChecks;
    CHECK(result == 0 && work->output == nullptr);
    current = previous;
}
void InvokeSeh(Work* work)
{
    __try { Invoke(work); }
    __except (GetExceptionCode() == OriginalSeh ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        ++raw.sehCaught; work->caught = 1;
    }
}
DWORD WINAPI Worker(void* arg) { Invoke(static_cast<Work*>(arg)); return 0; }
void SetupWork(Work* work, unsigned id)
{ work->id = id; work->retained = Named(L"retained") ? 1u : 0u; }
bool Initialize(const wchar_t* path)
{
    kernelFile = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    CHECK(kernelFile != INVALID_HANDLE_VALUE);
    kernel = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    CHECK(kernel); if (!kernel) return false;
    raw.helperCetCompat = CetCompatible(kernel) ? 1u : 0u;
    raw.fixtureCetCompat = CetCompatible(GetModuleHandleW(nullptr)) ? 1u : 0u;
    CHECK(raw.helperCetCompat && raw.fixtureCetCompat);
    DWORD cetPolicy{};
    raw.processCetPolicyKnown = GetProcessMitigationPolicy(GetCurrentProcess(),
        static_cast<PROCESS_MITIGATION_POLICY>(16), &cetPolicy, sizeof(cetPolicy)) ? 1u : 0u;
    raw.processCetEnabled = cetPolicy & 1;
    auto version = Symbol<decltype(&RizEngineNodeContext_GetAbiVersion)>("RizEngineNodeContext_GetAbiVersion");
    auto sideVersion = Symbol<decltype(&RizEngineNodeContext_GetInlineAbiVersion)>("RizEngineNodeContext_GetInlineAbiVersion");
    auto frameSize = Symbol<decltype(&RizEngineNodeContext_GetInlineFrameSize)>("RizEngineNodeContext_GetInlineFrameSize");
    auto getEntry = Symbol<decltype(&RizEngineNodeContext_GetEntry)>("RizEngineNodeContext_GetEntry");
    auto getInlineEntry = Symbol<decltype(&RizEngineNodeContext_GetInlineEntry)>("RizEngineNodeContext_GetInlineEntry");
    configure = Symbol<decltype(configure)>("RizEngineNodeContext_Configure"); attach = Symbol<decltype(attach)>("RizEngineNodeContext_Attach");
    getInfo = Symbol<decltype(getInfo)>("RizEngineNodeContext_GetInfo");
    getInlineInfo = Symbol<decltype(getInlineInfo)>("RizEngineNodeContext_GetInlineInfo");
    attachInline = Symbol<decltype(attachInline)>("RizEngineNodeContext_AttachInline");
    auto configureInline = Symbol<decltype(&RizEngineNodeContext_ConfigureInline)>("RizEngineNodeContext_ConfigureInline");
    if (raw.failures) return false;
    CHECK(version() == 2 && sideVersion() == 1 && frameSize() == 144);
    inlineEntry = getInlineEntry(); startEntry = reinterpret_cast<StartFn>(getEntry(RizEngineNodeStart, 0));
    executeEntry = reinterpret_cast<ExecuteFn>(getEntry(RizEngineNodeExecute, 0)); CHECK(inlineEntry && startEntry && executeEntry);
    for (unsigned i = 0; i != 16; ++i) for (unsigned j = 0; j != 16; ++j)
        RtEngineInlineFixture_XmmInput[i][j] = static_cast<unsigned char>(0x10 + i * 7 + j);
    RizEngineNodeConfigV1 startConfig{ 1, sizeof(startConfig), reinterpret_cast<void*>(&RtEngineInlineFixture_Start), reinterpret_cast<void*>(&StartOriginal) };
    RizEngineNodeConfigV1 executeConfig{ 1, sizeof(executeConfig), reinterpret_cast<void*>(&ExecuteTarget), reinterpret_cast<void*>(&ExecuteOriginal) };
    CHECK(configure(RizEngineNodeStart, 0, &startConfig) == 0 && configure(RizEngineNodeExecute, 0, &executeConfig) == 0);
    raw.startInfo = { 1, sizeof(raw.startInfo) }; raw.executeInfo = { 1, sizeof(raw.executeInfo) };
    CHECK(getInfo(0, 0, &raw.startInfo) == 0 && getInfo(1, 0, &raw.executeInfo) == 0);
    if (!PublishRelays()) return false;
    RizEngineInlineConfigV1 config{}; config.abiVersion = 1; config.byteSize = sizeof(config); config.startSlot = 0;
    config.mainThreadId = mainThread; config.expectedStartEpoch = raw.startInfo.epoch; config.expectedStartTarget = startConfig.target;
    for (unsigned i = 0; i != 3; ++i) config.sites[i] = { reinterpret_cast<void*>(raw.sites[i].site), reinterpret_cast<void*>(raw.sites[i].returnPc) };
    if (wrongSite) config.sites[0].relayReturnPc = reinterpret_cast<void*>(raw.sites[0].returnPc + 1);
    CHECK(configureInline(0, &config) == 0); raw.inlineInfo = { 1, sizeof(raw.inlineInfo) };
    CHECK(getInlineInfo(0, &raw.inlineInfo) == 0); inlineEpoch = raw.inlineInfo.epoch;
    RizEngineNodeObserverV1 observer{ 1, sizeof(observer), &TypedEnter, &TypedExit, &raw };
    CHECK(attach(0, 0, raw.startInfo.epoch, &observer) == 0 && attach(1, 0, raw.executeInfo.epoch, &observer) == 0);
    inlineObserver = { 1, sizeof(inlineObserver), &SideObserve, &raw };
    if (!lateScenario) CHECK(attachInline(0, inlineEpoch, &inlineObserver) == 0);
    return PatchOwnedSites();
}
}
extern "C" void* RtEngineInlineFixture_Prepare(void* opaque, std::uint64_t rsp, Machine* before, Machine* after)
{
    auto* work = static_cast<Work*>(opaque); CHECK(work == current); work->sourceRsp = rsp;
    work->sourceReturnPc = *reinterpret_cast<const std::uint64_t*>(rsp + 0x818);
    work->beginBeforeLive = before; work->beginAfterLive = after;
    for (unsigned i = 0; i != 4; ++i) {
        auto* frame = reinterpret_cast<Machine*>(rsp + 0x40 + i * 0x1b0);
        std::memset(frame, 0, sizeof(*frame)); frame->abiVersion = 1; frame->byteSize = sizeof(*frame);
    }
    work->node = VirtualAlloc(work->preferred, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    CHECK(work->node && (!work->preferred || work->node == work->preferred)); if (!work->node) RaiseFailFastException(nullptr, nullptr, 0);
    WriteNode(work, 0x58, work); WriteNode(work, 0x10, 0x100000000ULL + work->id);
    WriteNode(work, 0x18, std::uint32_t(2)); WriteNode(work, 0x20, static_cast<void*>(&work->id));
    WriteNode(work, 0x60, std::int32_t(1)); WriteNode(work, 0x68, NodeLink); work->output = work->node;
    // The real synchronous Execute precedes the inline release branch. The
    // native typed transport must observe that generation before Begin.
    unsigned char exceptionByte{};
    CHECK(executeEntry(work->node, &exceptionByte) == 0x12340001 && exceptionByte == 0xa5);
    if (work->lateAttach) { CHECK(attachInline(0, inlineEpoch, &inlineObserver) == 0); ++raw.lateAttachCalls; }
    SetLastError(BeginLastError);
    return work->node;
}
extern "C" std::uint32_t RtEngineInlineFixture_Between(void* opaque, Machine* before, Machine* after)
{
    auto* work = static_cast<Work*>(opaque); CHECK(current == work); ++work->betweenCalls;
    CHECK(GetLastError() == BeginLastError); ++raw.savedLastErrorChecks;
    work->expectedReferences = work->betweenCalls == 1 ? 1u : 0u;
    CompareMachine(*before, *after, true, work); work->beginBefore = *before; work->beginAfter = *after;
    if (work->betweenCalls == 1) {
        if (work->nested) { auto* child = &raw.works[1]; SetupWork(child, 2); child->parent = work; raw.workCount = 2; Invoke(child); }
    }
    if (work->duplicateBegin && work->betweenCalls == 1) { SetLastError(BeginLastError); return 4; }
    CHECK(ReadNode<std::int32_t>(work, 0x60) == (work->duplicateBegin ? -1 : 0));
    if (work->originalFault == 1) RaiseException(OriginalSeh, 0, 0, nullptr);
    else if (work->originalFault == 2) throw CppFault{ 0xdecafbad12345678ULL };
    if (work->retained) {
        // Destroy the live tuple after Begin. EndRetained must use its immutable
        // Begin cache, even though the allocation remains readable.
        WriteNode(work, 0x58, static_cast<void*>(nullptr)); WriteNode(work, 0x10, std::uint64_t(0));
        WriteNode(work, 0x18, std::uint32_t(0)); WriteNode(work, 0x20, static_cast<void*>(nullptr));
        work->retainedCleanup = 1; ++raw.retainedCount;
    } else {
        CHECK(VirtualFree(work->node, 0, MEM_RELEASE)); work->freed = 1; ++raw.realFreeCount;
        MEMORY_BASIC_INFORMATION state{}; CHECK(VirtualQuery(work->node, &state, sizeof(state)) == sizeof(state) && state.State == MEM_FREE);
    }
    SetLastError(EndLastError); return work->retained ? 1u : 0u;
}
extern "C" std::uint32_t RtEngineInlineFixture_Complete(void* opaque, Machine* before, Machine* after, const std::uint64_t* canaries)
{
    auto* work = static_cast<Work*>(opaque); CHECK(work == current); ++work->completeCalls;
    CHECK(GetLastError() == EndLastError); ++raw.savedLastErrorChecks;
    CompareMachine(*before, *after, false, work); work->endBefore = *before; work->endAfter = *after;
    CHECK(canaries[0] == Canary && canaries[1] == Canary && canaries[2] == Canary); ++raw.canaryChecks;
    CHECK(work->output == nullptr && (before->gpr[0] & 0xff) == 0);
    SetLastError(EndLastError);
    return work->duplicateEnd && work->completeCalls == 1 ? (0x100u | (work->retained ? 0x200u : 0u)) : 0u;
}
int wmain(int argc, wchar_t** argv)
{
    if (argc != 4) { std::fprintf(stderr, "usage: fixture.exe ABS_KERNEL_DLL CASE ABS_RAW_OUTPUT\n"); return 2; }
    mainThread = GetCurrentThreadId(); scenario = argv[2];
    workerScenario = Named(L"worker"); wrongSite = Named(L"wrongsite"); missingBegin = Named(L"missing-begin");
    missingEnd = Named(L"missing-end"); lateScenario = Named(L"late-attach");
    const bool known = Named(L"free") || Named(L"retained") || Named(L"nested") || Named(L"address-reuse")
        || Named(L"seh") || Named(L"cpp") || Named(L"observer-seh") || Named(L"observer-cpp")
        || Named(L"duplicate-begin") || Named(L"duplicate-end") || workerScenario || wrongSite || missingBegin || missingEnd || lateScenario;
    CHECK(known && std::wcslen(argv[1]) > 3 && std::wcslen(argv[3]) > 3);
    bool initialized = false;
    if (raw.failures == 0) initialized = Initialize(argv[1]);
    if (initialized) {
        raw.workCount = 1; auto* work = &raw.works[0]; SetupWork(work, 1);
        work->nested = Named(L"nested"); work->duplicateBegin = Named(L"duplicate-begin"); work->duplicateEnd = Named(L"duplicate-end");
        work->originalFault = Named(L"seh") ? 1u : Named(L"cpp") ? 2u : 0u;
        work->observerFault = Named(L"observer-seh") ? 1u : Named(L"observer-cpp") ? 2u : 0u;
        work->lateAttach = lateScenario ? 1u : 0u;
        if (workerScenario) {
            HANDLE thread = CreateThread(nullptr, 0, &Worker, work, 0, nullptr); CHECK(thread != nullptr);
            if (thread) { CHECK(WaitForSingleObject(thread, 15000) == WAIT_OBJECT_0); DWORD code{};
                CHECK(GetExitCodeThread(thread, &code) && code == 0); CHECK(CloseHandle(thread)); ++raw.workerCalls; }
        } else if (work->originalFault == 1) InvokeSeh(work);
        else if (work->originalFault == 2) {
            try { Invoke(work); } catch (const CppFault& fault) { CHECK(fault.seal == 0xdecafbad12345678ULL); ++raw.cppCaught; work->caught = 1; }
        } else Invoke(work);
        // Exceptions unwind native frames before control reaches here. The
        // fixture's own TLS is restored explicitly; it is no kernel evidence.
        current = nullptr;
        if (Named(L"address-reuse") || lateScenario) {
            auto* second = &raw.works[1]; SetupWork(second, 2); raw.workCount = 2;
            if (!lateScenario) second->preferred = work->node;
            const auto oldEvents = raw.eventCount;
            if (lateScenario) CHECK(oldEvents == 0 && work->sideEvents == 0);
            Invoke(second);
            CHECK(second->actualSequence && second->actualSequence != work->actualSequence);
            if (!lateScenario) { CHECK(second->node == work->node); ++raw.addressReuseCount; }
            else CHECK(second->sideEvents == 4 && raw.eventCount == 4);
        }
        CHECK(getInfo(0, 0, &raw.startInfo) == 0 && getInfo(1, 0, &raw.executeInfo) == 0 && getInlineInfo(0, &raw.inlineInfo) == 0);
        CHECK(raw.startInfo.entered == raw.workCount && raw.startInfo.exited == raw.workCount && raw.startInfo.inFlight == 0);
        CHECK(raw.executeInfo.entered == raw.workCount && raw.executeInfo.exited == raw.workCount && raw.executeInfo.inFlight == 0);
        CHECK(raw.inlineInfo.startsEntered == raw.workCount && raw.inlineInfo.startsExited == raw.workCount
            && raw.inlineInfo.startsInFlight == 0 && raw.inlineInfo.probesInFlight == 0);
        const bool expectedUnknown = workerScenario || wrongSite || missingBegin || missingEnd || work->originalFault || work->observerFault
            || work->duplicateBegin || work->duplicateEnd;
        CHECK(raw.inlineInfo.unknown == (expectedUnknown ? 1u : 0u));
        CHECK(raw.startInfo.unknown == (expectedUnknown ? 1u : 0u));
        if (!workerScenario && !wrongSite && !missingBegin && !missingEnd && !work->originalFault && !work->duplicateBegin && !work->duplicateEnd)
            CHECK(raw.eventCount == (lateScenario ? 4u : raw.workCount * 4));
        if (!workerScenario && !wrongSite && !missingBegin) CHECK(raw.inlineInfo.snapshotFaults == 0);
        if (!workerScenario && !wrongSite && !missingBegin && !missingEnd && !work->originalFault) {
            const auto observedWorks = lateScenario ? 1u : raw.workCount;
            CHECK(raw.helperWalks >= observedWorks * 2 && raw.relayWalks >= observedWorks * 2 && raw.virtualUnwinds >= observedWorks * 16);
        }
        CHECK(raw.executeInfo.unknown == (expectedUnknown ? 1u : 0u));
        for (unsigned i = 0; i != raw.workCount; ++i) {
            auto& item = raw.works[i]; CHECK(item.enters == 1 && item.exits == 1 && item.executes == 1);
            if (!item.freed) { CHECK(VirtualFree(item.node, 0, MEM_RELEASE)); item.node = nullptr; }
        }
        raw.firstSequence = work->actualSequence;
        if (raw.workCount > 1) raw.secondSequence = raw.works[1].actualSequence;
    }
    HANDLE output = CreateFileW(argv[3], GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    CHECK(output != INVALID_HANDLE_VALUE);
    if (output != INVALID_HANDLE_VALUE) {
        DWORD written{};
        const bool writtenOkay = WriteFile(output, &raw, sizeof(raw), &written, nullptr) && written == sizeof(raw);
        const bool flushed = FlushFileBuffers(output) != FALSE, closed = CloseHandle(output) != FALSE;
        if (!writtenOkay || !flushed || !closed) { std::fprintf(stderr, "Raw receipt could not be persisted.\n"); return 3; }
    }
    std::printf("{\"kind\":\"native-engine-inline-fixture\",\"schemaVersion\":1,\"passed\":%s,\"checks\":%u,\"failures\":%u,\"initialized\":%s,\"works\":%u,\"events\":%u,\"typedCalls\":%u,\"realFree\":%u,\"retainedCleanup\":%u,\"addressReuse\":%u,\"allStatePairs\":%u,\"beginReplayPairs\":%u,\"canaries\":%u,\"helperWalks\":%u,\"relayWalks\":%u,\"virtualUnwinds\":%u,\"sehCaught\":%u,\"cppCaught\":%u,\"observerFaults\":%u,\"workerCalls\":%u,\"lateAttachCalls\":%u,\"inlineUnknown\":%u,\"startsInFlight\":%llu,\"probesInFlight\":%llu,\"rawBytes\":%zu,\"typedEntryInvoked\":true,\"realSourceE9\":true,\"relayActualCall\":true,\"relayChainUnwind\":true,\"allGpr\":true,\"allXmm\":true,\"rflags\":true,\"mxcsr\":true,\"fpcsr\":true,\"directionFlag\":true,\"fixtureOnlyPatch\":true,\"gameRuntimeVerified\":false,\"productionAdmissionVerified\":false,\"engineCancellationCompleted\":false}\n",
        raw.failures ? "false" : "true", raw.checks, raw.failures, initialized ? "true" : "false", raw.workCount, raw.eventCount, raw.typedCount,
        raw.realFreeCount, raw.retainedCount, raw.addressReuseCount, raw.allStatePairs, raw.beginReplayPairs, raw.canaryChecks, raw.helperWalks,
        raw.relayWalks, raw.virtualUnwinds, raw.sehCaught, raw.cppCaught, raw.observerFaults, raw.workerCalls, raw.lateAttachCalls,
        raw.inlineInfo.unknown, raw.inlineInfo.startsInFlight, raw.inlineInfo.probesInFlight, sizeof(raw));
    // Permanent native configuration, helper, callback roots, relays, and RF
    // registrations are intentionally process-lifetime. Do not unload them.
    return raw.failures ? 1 : 0;
}
