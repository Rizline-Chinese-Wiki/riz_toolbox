#pragma once
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "../FirstInstall.h"
#include "../FirstInstallProfile.h"
#include "../ListenerContext.h"

// Shared only by independent offline fixtures. No function in this header
// calls a game address. VirtualUnwind uses a bounded, self-owned stack with
// the actual producer's registered RF and the fixed mapped parent's xdata.
namespace RizMappedChecks {
using CheckFn = void(*)(bool, const char*, int);
inline constexpr std::uint32_t SourceRva[3]{0x1564470, 0x15644c2, 0x156451f};
inline constexpr std::uint32_t ReturnRva[3]{0x1564475, 0x15644c7, 0x1564525};
inline constexpr std::uint32_t SourceWidth[3]{5, 5, 6}, PostWidth[3]{6, 9, 9};
inline constexpr unsigned char OriginalSource[3][6]{
    {0xe8, 0x3b, 0xa1, 0xfe, 0xff, 0}, {0xe8, 0xe9, 0xa0, 0xfe, 0xff, 0}, {0xff, 0x90, 0x78, 1, 0, 0}};
inline constexpr unsigned char OriginalPost[3][9]{
    {0xff, 0xc3, 0x8b, 0xc3, 0xeb, 0xa5, 0, 0, 0},
    {0xff, 0xc3, 0x8b, 0xc3, 0xe9, 0x50, 0xff, 0xff, 0xff},
    {0xff, 0xc3, 0x8b, 0xc3, 0xe9, 0xf2, 0xfe, 0xff, 0xff}};
struct BeforeSnapshot {
    DWORD sourceProtection[3]{}, postProtection[3]{};
};
struct PublishedSnapshot {
    unsigned char source[3][6]{}, post[3][9]{}, allocation[4096]{};
    const void* allocationBase{};
};
struct Evidence {
    std::uint32_t sources{}, posts{}, relays{}, lookups{}, boundaries{}, restoredProtections{};
};
#define RIZ_MAPPED_CHECK(x) check(!!(x), #x, __LINE__)
inline MEMORY_BASIC_INFORMATION Memory(const void* address, CheckFn check)
{
    MEMORY_BASIC_INFORMATION info{};
    RIZ_MAPPED_CHECK(VirtualQuery(address, &info, sizeof(info)) == sizeof(info));
    return info;
}
inline void PrivateRx(const void* address, CheckFn check)
{
    const auto memory = Memory(address, check);
    RIZ_MAPPED_CHECK(memory.State == MEM_COMMIT && memory.Type == MEM_PRIVATE
        && memory.Protect == PAGE_EXECUTE_READ && memory.RegionSize >= 14);
}
inline const unsigned char* RelativeTarget(const unsigned char* instruction)
{
    std::int32_t displacement{}; std::memcpy(&displacement, instruction + 1, sizeof(displacement));
    return instruction + 5 + displacement;
}
inline void Tail(const void* address, const void* target, CheckFn check)
{
    PrivateRx(address, check);
    const unsigned char prefix[]{0xff, 0x25, 0, 0, 0, 0};
    RIZ_MAPPED_CHECK(std::memcmp(address, prefix, sizeof(prefix)) == 0);
    const void* actual{}; std::memcpy(&actual, static_cast<const unsigned char*>(address) + 6, sizeof(actual));
    RIZ_MAPPED_CHECK(actual == target);
}
inline BeforeSnapshot CaptureBefore(const unsigned char* base, CheckFn check)
{
    BeforeSnapshot before{};
    for (unsigned site = 0; site != 3; ++site) {
        RIZ_MAPPED_CHECK(std::memcmp(base + SourceRva[site], OriginalSource[site], SourceWidth[site]) == 0);
        RIZ_MAPPED_CHECK(std::memcmp(base + ReturnRva[site], OriginalPost[site], PostWidth[site]) == 0);
        RIZ_MAPPED_CHECK(SourceRva[site] + SourceWidth[site] == ReturnRva[site]);
        if (site != 2) RIZ_MAPPED_CHECK(RelativeTarget(base + SourceRva[site]) == base + 0x154e5b0);
        const auto* end = base + ReturnRva[site];
        const auto* loop = site == 0 ? end + 6 + static_cast<signed char>(end[5]) : RelativeTarget(end + 4);
        RIZ_MAPPED_CHECK(loop == base + RizFirstInstallProfile::MidReturnRva);
        const auto source = Memory(base + SourceRva[site], check), post = Memory(base + ReturnRva[site], check);
        RIZ_MAPPED_CHECK(source.State == MEM_COMMIT && source.Type == MEM_IMAGE && source.AllocationBase == base);
        RIZ_MAPPED_CHECK(post.State == MEM_COMMIT && post.Type == MEM_IMAGE && post.AllocationBase == base);
        before.sourceProtection[site] = source.Protect; before.postProtection[site] = post.Protect;
    }
    return before;
}
inline void CommonAgrees(const RizFirstInstallInfoV1& v1, const RizFirstInstallInfoV2& v2, CheckFn check)
{
    auto expected = v1; expected.abiVersion = 2; expected.byteSize = sizeof(v2);
    RIZ_MAPPED_CHECK(std::memcmp(&expected, &v2.common, sizeof(expected)) == 0);
}
inline void CallerMarker() { }
inline void UnwindBoundary(const unsigned char* base, const RizFirstInstallInfoV2& info,
    unsigned site, unsigned offset, PRUNTIME_FUNCTION function, DWORD64 tableBase, CheckFn check)
{
    alignas(16) std::uint64_t body[48]{};
    for (unsigned index = 0; index != 48; ++index) body[index] = 0xF000000000000000ULL + index;
    // Fixed real Unity parent: push RSI; sub RSP,20h; late SAVE_NONVOL
    // RBX/RBP/RDI/R14 at +30/+38/+40/+48. Return is at +28, not body[0].
    body[5] = reinterpret_cast<std::uint64_t>(&CallerMarker);
    CONTEXT context{}; context.ContextFlags = CONTEXT_ALL;
    context.Rsp = reinterpret_cast<DWORD64>(body); context.Rip = reinterpret_cast<DWORD64>(info.postStub[site]) + offset;
    context.Rbx = offset >= 7 ? 2 : 1; context.Rax = offset >= 9 ? context.Rbx : 0xABCDEF0123456789ULL;
    context.Rbp = 0xB0B0B0B0B0B0B0B0ULL; context.Rsi = 0x5151515151515151ULL; context.Rdi = 0xD1D1D1D1D1D1D1D1ULL;
    context.R12 = 0x1212121212121212ULL; context.R13 = 0x1313131313131313ULL;
    context.R14 = 0x1414141414141414ULL; context.R15 = 0x1515151515151515ULL;
    auto* xmm = &context.Xmm0;
    for (unsigned index = 0; index != 16; ++index) { xmm[index].Low = 0x1111111100000000ULL + index; xmm[index].High = 0x2222222200000000LL + index; }
    const auto original = context;
    PVOID data{}; DWORD64 establisher{};
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, tableBase, context.Rip, function, &context, &data, &establisher, nullptr);
    if (context.Rip != body[5] || context.Rsp != reinterpret_cast<DWORD64>(body) + 0x30)
        std::fprintf(stderr, "mapped unwind site=%u boundary=%u rip=%llx expected=%llx rsp=%llx expected=%llx\n",
            site, offset, context.Rip, body[5], context.Rsp, reinterpret_cast<DWORD64>(body) + 0x30);
    RIZ_MAPPED_CHECK(context.Rip == body[5] && context.Rsp == reinterpret_cast<DWORD64>(body) + 0x30);
    RIZ_MAPPED_CHECK(context.Rsi == body[4] && context.Rbx == body[6] && context.Rbp == body[7]
        && context.Rdi == body[8] && context.R14 == body[9]);
    RIZ_MAPPED_CHECK(context.R12 == original.R12 && context.R13 == original.R13 && context.R15 == original.R15);
    RIZ_MAPPED_CHECK(std::memcmp(&context.Xmm6, &original.Xmm6, 10 * sizeof(M128A)) == 0);
    RIZ_MAPPED_CHECK(tableBase == reinterpret_cast<DWORD64>(base));
}
inline Evidence ValidatePublished(const unsigned char* base, const RizFirstInstallInfoV2& info,
    const BeforeSnapshot& before, CheckFn check)
{
    Evidence evidence{};
    RIZ_MAPPED_CHECK(info.common.abiVersion == 2 && info.common.byteSize == sizeof(info)
        && info.common.phase == RizFirstInstallReady && info.common.unknown == 0 && info.common.writesStarted == 64);
    RIZ_MAPPED_CHECK(info.capabilityVersion == 1 && info.listenerSlot == 31 && info.listenerEpoch != 0
        && info.preEntry && info.postEntry && info.reserved2 == 0 && info.allocationSize == 4096);
    RIZ_MAPPED_CHECK(info.registeredFunctionCount == 3 && info.prewriteLookupChecks == 12 && info.prewriteBoundaryChecks == 12);
    RIZ_MAPPED_CHECK(info.directTarget[0] == base + 0x154e5b0 && info.directTarget[1] == base + 0x154e5b0);
    RIZ_MAPPED_CHECK(info.functionTableBase == reinterpret_cast<std::uint64_t>(base));
    const auto allocation = Memory(info.preRelay, check).AllocationBase;
    const auto* block = static_cast<const unsigned char*>(allocation);
    RIZ_MAPPED_CHECK(info.preRelay == block + 0x500 && info.postRelay == block + 0x540
        && info.common.relay == block + 0x520 && info.functionTable == block + 0x380);
    Tail(info.preRelay, info.preEntry, check); Tail(info.postRelay, info.postEntry, check); evidence.relays = 2;
    DWORD64 parentBase{}; auto* parent = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(base + RizFirstInstallProfile::UnityRva), &parentBase, nullptr);
    RIZ_MAPPED_CHECK(parent && parentBase == reinterpret_cast<DWORD64>(base));
    if (!parent) return evidence;
    RIZ_MAPPED_CHECK(parent->BeginAddress == RizFirstInstallProfile::UnityRva && parent->EndAddress == RizFirstInstallProfile::UnityEndRva
        && parent->UnwindData == 0x1db3fb4);
    const auto* table = static_cast<const RUNTIME_FUNCTION*>(info.functionTable);
    for (unsigned site = 0; site != 3; ++site) {
        const auto* source = base + SourceRva[site]; const auto* post = base + ReturnRva[site];
        const auto* stub = static_cast<const unsigned char*>(info.postStub[site]);
        RIZ_MAPPED_CHECK(info.sourcePc[site] == source && info.sourceReturnPc[site] == post
            && info.sourceWidth[site] == SourceWidth[site] && info.postWidth[site] == PostWidth[site]);
        RIZ_MAPPED_CHECK(site == 2 ? (source[0] == 0x90 && source[1] == 0xe8) : source[0] == 0xe8);
        const auto* actualCall = source + (site == 2 ? 1 : 0);
        RIZ_MAPPED_CHECK(actualCall + 5 == post && RelativeTarget(actualCall) == info.preRelay); ++evidence.sources;
        RIZ_MAPPED_CHECK(post[0] == 0xe9 && RelativeTarget(post) == stub);
        for (unsigned byte = 5; byte != PostWidth[site]; ++byte) RIZ_MAPPED_CHECK(post[byte] == 0x90);
        ++evidence.posts;
        PrivateRx(stub, check); RIZ_MAPPED_CHECK(stub == block + site * 0x100);
        RIZ_MAPPED_CHECK(stub[0] == 0xe8 && RelativeTarget(stub) == info.postRelay
            && info.postTransportReturnPc[site] == stub + 5);
        const unsigned char replay[]{0xff, 0xc3, 0x8b, 0xc3};
        RIZ_MAPPED_CHECK(std::memcmp(stub + 5, replay, sizeof(replay)) == 0 && stub[9] == 0xe9
            && RelativeTarget(stub + 9) == base + RizFirstInstallProfile::MidReturnRva);
        const auto* unwind = static_cast<const unsigned char*>(info.unwindInfo[site]);
        const unsigned char xdata[]{0x21, 0, 0, 0};
        RIZ_MAPPED_CHECK(unwind == block + site * 0x100 + 0x40 && std::memcmp(unwind, xdata, sizeof(xdata)) == 0
            && std::memcmp(unwind + 4, parent, sizeof(*parent)) == 0);
        RIZ_MAPPED_CHECK(table[site].BeginAddress == reinterpret_cast<DWORD64>(stub) - info.functionTableBase
            && table[site].EndAddress == table[site].BeginAddress + 14
            && table[site].UnwindData == reinterpret_cast<DWORD64>(unwind) - info.functionTableBase);
        for (const unsigned offset : {0u, 5u, 7u, 9u}) {
            DWORD64 actualBase{}; auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(stub + offset), &actualBase, nullptr);
            RIZ_MAPPED_CHECK(actual && actualBase == info.functionTableBase && std::memcmp(actual, &table[site], sizeof(*actual)) == 0);
            ++evidence.lookups;
            if (actual) { UnwindBoundary(base, info, site, offset, actual, actualBase, check); ++evidence.boundaries; }
        }
        const auto sourceMemory = Memory(source, check), postMemory = Memory(post, check);
        RIZ_MAPPED_CHECK(sourceMemory.Protect == before.sourceProtection[site] && info.sourceProtection[site] == before.sourceProtection[site]
            && postMemory.Protect == before.postProtection[site] && info.postProtection[site] == before.postProtection[site]);
        evidence.restoredProtections += 2;
    }
    return evidence;
}
inline PublishedSnapshot CapturePublished(const unsigned char* base, const RizFirstInstallInfoV2& info, CheckFn check)
{
    PublishedSnapshot snapshot{}; snapshot.allocationBase = Memory(info.preRelay, check).AllocationBase;
    for (unsigned site = 0; site != 3; ++site) {
        std::memcpy(snapshot.source[site], base + SourceRva[site], SourceWidth[site]);
        std::memcpy(snapshot.post[site], base + ReturnRva[site], PostWidth[site]);
    }
    std::memcpy(snapshot.allocation, snapshot.allocationBase, sizeof(snapshot.allocation)); return snapshot;
}
inline bool Unchanged(const unsigned char* base, const PublishedSnapshot& snapshot)
{
    for (unsigned site = 0; site != 3; ++site)
        if (std::memcmp(snapshot.source[site], base + SourceRva[site], SourceWidth[site]) != 0
            || std::memcmp(snapshot.post[site], base + ReturnRva[site], PostWidth[site]) != 0) return false;
    return std::memcmp(snapshot.allocation, snapshot.allocationBase, sizeof(snapshot.allocation)) == 0;
}
inline bool SameUnknown(const RizFirstInstallInfoV2& value, const RizFirstInstallInfoV2& ready)
{
    auto expected = ready; expected.common.phase = RizFirstInstallUnknown; expected.common.unknown = 1;
    return std::memcmp(&value, &expected, sizeof(value)) == 0;
}
inline RizFirstInstallInfoV2 Header()
{
    RizFirstInstallInfoV2 value{}; value.common.abiVersion = 2; value.common.byteSize = sizeof(value); return value;
}
template<class Reader> inline unsigned InvalidHeaders(Reader read, const RizFirstInstallInfoV2& valid, CheckFn check)
{
    unsigned cases = 0;
    RIZ_MAPPED_CHECK(read(nullptr) == 1); ++cases;
    for (const unsigned version : {1u, 3u}) {
        auto invalid = valid; invalid.common.abiVersion = version; const auto saved = invalid;
        RIZ_MAPPED_CHECK(read(&invalid) == 1 && std::memcmp(&saved, &invalid, sizeof(saved)) == 0); ++cases;
    }
    for (const unsigned size : {144u, 415u, 417u}) {
        auto invalid = valid; invalid.common.byteSize = size; const auto saved = invalid;
        RIZ_MAPPED_CHECK(read(&invalid) == 1 && std::memcmp(&saved, &invalid, sizeof(saved)) == 0); ++cases;
    }
    for (unsigned field = 0; field != 3; ++field) {
        auto invalid = valid;
        if (field == 0) invalid.common.reserved0 = 1;
        else if (field == 1) invalid.common.reserved1 = 1;
        else invalid.reserved2 = 1;
        const auto saved = invalid;
        RIZ_MAPPED_CHECK(read(&invalid) == 1 && std::memcmp(&saved, &invalid, sizeof(saved)) == 0); ++cases;
    }
    return cases;
}
template<class Reader> inline bool Canonicalized(Reader read, const RizFirstInstallInfoV2& actual, CheckFn check)
{
    auto poisoned = actual;
    // Caller-owned output fields cannot forge, suppress or replace the native
    // authority. All non-header identity is overwritten by the real snapshot.
    poisoned.common.phase = RizFirstInstallReady; poisoned.common.unknown = 0;
    poisoned.common.moduleBase = 1; poisoned.common.writesStarted = 999;
    poisoned.common.runtimeEpoch = 999; poisoned.common.unityEpoch = 999;
    poisoned.listenerSlot = 0; poisoned.listenerEpoch = 999; poisoned.capabilityVersion = 999;
    poisoned.preEntry = poisoned.postEntry = poisoned.preRelay = poisoned.postRelay = reinterpret_cast<const void*>(1);
    poisoned.functionTable = reinterpret_cast<const void*>(1); poisoned.functionTableBase = 1;
    for (unsigned site = 0; site != 3; ++site)
        poisoned.sourcePc[site] = poisoned.sourceReturnPc[site] = poisoned.postStub[site]
            = poisoned.postTransportReturnPc[site] = poisoned.unwindInfo[site] = reinterpret_cast<const void*>(1);
    const auto okay = read(&poisoned) == 0 && std::memcmp(&poisoned, &actual, sizeof(actual)) == 0;
    RIZ_MAPPED_CHECK(okay); return okay;
}
inline void DisableErrorUi()
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    const auto wer = LoadLibraryW(L"wer.dll");
    if (wer) {
        using WerFlags = HRESULT(WINAPI*)(DWORD);
        const auto symbol = GetProcAddress(wer, "WerSetFlags");
        WerFlags set{}; static_assert(sizeof(set) == sizeof(symbol)); std::memcpy(&set, &symbol, sizeof(set));
        if (set) set(0x20);
    }
}
#undef RIZ_MAPPED_CHECK
}
