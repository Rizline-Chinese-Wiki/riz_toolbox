#pragma once
#include <Windows.h>
#include <cstdint>
#include <cstring>
#include <initializer_list>
#include "../FirstInstall.h"
#include "../OperationContext.h"

// Independent expected declarations. Only the isolated PE mapping is inspected;
// these tests never invoke an operation entry, original, or game initializer.
namespace RizOperationChecks {
using CheckFn = void(*)(bool, const char*, int);
#define RIZ_OPERATION_CHECK(value) check(!!(value), #value, __LINE__)
inline constexpr std::uint32_t Rvas[26]{
    0x15640a0, 0x15630e0, 0x1563c50, 0x1563b80, 0x15631a0, 0x1563b40,
    0x154ded0, 0x154df90, 0x154e280, 0x154e110, 0x154e050, 0x1550ee0,
    0x154e890, 0x154e930, 0x153d900, 0x153d7f0, 0x153d6c0, 0x153d750, 0x153d700, 0x153d7a0,
    0x3956f0, 0x396be0, 0x396e40, 0x398d60, 0x396cf0, 0xbc0ff0 };
inline constexpr std::uint32_t Kinds[26]{1,1,2,0,0,0,1,1,2,0,0,2,1,1,1,1,0,1,1,1,0,1,1,0,0,3};
inline constexpr std::uint32_t Slots[26]{31,30,31,31,30,29,29,28,30,28,27,29,27,26,25,24,26,23,22,21,25,20,19,24,23,31};
struct Snapshot {
    unsigned char bytes[26][32]{};
    DWORD protection[26]{};
    RUNTIME_FUNCTION functions[26]{};
};
inline bool LeadsTo(const unsigned char* code, const void* target)
{
    for (unsigned hop = 0; hop != 8; ++hop) {
        if (code == target) return true;
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(code, &memory, sizeof(memory)) != sizeof(memory) || memory.State != MEM_COMMIT
            || (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0 || (memory.Protect & 0xf0) == 0
            || reinterpret_cast<std::uintptr_t>(code) + 14 > reinterpret_cast<std::uintptr_t>(memory.BaseAddress) + memory.RegionSize) return false;
        if (code[0] == 0xe9) {
            std::int32_t displacement{}; std::memcpy(&displacement,code + 1,4); code += 5 + displacement;
        } else if (code[0] == 0xff && code[1] == 0x25) {
            std::int32_t displacement{}; std::memcpy(&displacement,code + 2,4);
            const auto* cell = code + 6 + displacement;
            MEMORY_BASIC_INFORMATION data{};
            if (VirtualQuery(cell,&data,sizeof(data)) != sizeof(data) || data.State != MEM_COMMIT
                || (data.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0
                || reinterpret_cast<std::uintptr_t>(cell) + 8 > reinterpret_cast<std::uintptr_t>(data.BaseAddress) + data.RegionSize) return false;
            std::memcpy(&code,cell,8);
        } else if (code[0] == 0x48 && code[1] == 0xb8 && code[10] == 0xff && code[11] == 0xe0) {
            std::memcpy(&code,code + 2,8);
        } else return false;
    }
    return false;
}
inline Snapshot Capture(const unsigned char* base, CheckFn check)
{
    Snapshot result{};
    for (unsigned i = 0; i != 26; ++i) {
        const auto* target = base + Rvas[i]; MEMORY_BASIC_INFORMATION memory{};
        RIZ_OPERATION_CHECK(VirtualQuery(target, &memory, sizeof(memory)) == sizeof(memory));
        RIZ_OPERATION_CHECK(memory.State == MEM_COMMIT && memory.Type == MEM_IMAGE && memory.AllocationBase == base
            && (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS)) == 0 && (memory.Protect & 0xf0) != 0);
        result.protection[i] = memory.Protect; std::memcpy(result.bytes[i], target, 32);
        DWORD64 actualBase{}; const auto* function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(target), &actualBase, nullptr);
        RIZ_OPERATION_CHECK(function && actualBase == reinterpret_cast<DWORD64>(base));
        if (function) {
            result.functions[i] = *function;
            RIZ_OPERATION_CHECK(function->BeginAddress == Rvas[i] && function->EndAddress > Rvas[i] && function->UnwindData != 0);
        }
    }
    return result;
}
inline RizFirstInstallInfoV7 Header()
{
    RizFirstInstallInfoV7 result{}; result.publication.common.abiVersion = 7;
    result.publication.common.byteSize = sizeof(result); return result;
}
inline bool SameUnknown(const RizFirstInstallInfoV7& value, const RizFirstInstallInfoV7& ready)
{
    auto expected = ready; expected.publication.common.phase = RizFirstInstallUnknown;
    expected.publication.common.unknown = 1;
    // Compare the retained identity independently of the first-cause diagnostic.
    expected.publication.common.reserved1 = value.publication.common.reserved1;
    expected.singleEvent.phase = RizFirstInstallUnknown; expected.singleEvent.unknown = 1;
    return std::memcmp(&value, &expected, sizeof(value)) == 0;
}
template<class ReadInfo, class GetEntry> inline unsigned Validate(const unsigned char* base, HMODULE helper,
    const RizFirstInstallInfoV7& value, const Snapshot& before, ReadInfo info, GetEntry entry, CheckFn check)
{
    RIZ_OPERATION_CHECK(value.publication.common.abiVersion == 7 && value.publication.common.byteSize == sizeof(value)
        && value.publication.common.phase == RizFirstInstallReady && value.publication.common.unknown == 0
        && value.publication.common.writesStarted == 64);
    RIZ_OPERATION_CHECK(value.operationCapabilityVersion == 4 && value.operationCount == 26 && value.expectedWrites == 64
        && value.reserved == 0 && value.configuredMask == 0x3ffffff && value.publishedMask == 0x3ffffff);
    unsigned verified = 0;
    for (unsigned i = 0; i != 26; ++i) {
        const auto& row = value.operations[i]; const auto* target = base + Rvas[i];
        RIZ_OPERATION_CHECK(row.id == i && row.kind == Kinds[i] && row.slot == Slots[i] && row.reserved == 0
            && row.rva == Rvas[i] && row.target == target && row.epoch != 0 && row.original && row.original != target
            && row.entry == entry(row.kind, row.slot));
        for (unsigned j = 0; j != i; ++j) {
            const auto& peer = value.operations[j];
            RIZ_OPERATION_CHECK(row.target != peer.target && row.original != peer.original && row.entry != peer.entry
                && row.epoch != peer.epoch && (row.kind != peer.kind || row.slot != peer.slot));
        }
        RizOperationBootstrapInfoV1 lane{1, sizeof(lane)};
        RIZ_OPERATION_CHECK(info(row.kind, row.slot, &lane) == 0 && lane.kind == row.kind && lane.slot == row.slot
            && lane.attached == 0 && lane.unknown == 0 && lane.target == row.target && lane.original == row.original
            && lane.entry == row.entry && lane.epoch == row.epoch && lane.entered == 0 && lane.exited == 0
            && lane.inFlight == 0 && lane.enterFaults == 0 && lane.exitFaults == 0);
        MEMORY_BASIC_INFORMATION source{}, trampoline{}, replacement{};
        RIZ_OPERATION_CHECK(VirtualQuery(target, &source, sizeof(source)) == sizeof(source)
            && source.Protect == before.protection[i] && row.protection == before.protection[i]);
        RIZ_OPERATION_CHECK(VirtualQuery(row.original, &trampoline, sizeof(trampoline)) == sizeof(trampoline)
            && trampoline.Type == MEM_PRIVATE && (trampoline.Protect & 0xf0) != 0);
        RIZ_OPERATION_CHECK(VirtualQuery(row.entry, &replacement, sizeof(replacement)) == sizeof(replacement)
            && replacement.Type == MEM_IMAGE && replacement.AllocationBase == helper && (replacement.Protect & 0xf0) != 0);
        // Validate the actual bounded branch/relay chain to the exact entry,
        // not merely "some prefix changed" or an address range in the helper.
        RIZ_OPERATION_CHECK(LeadsTo(target,row.entry)
            && std::memcmp(target, before.bytes[i], 14) != 0);
        DWORD64 actualBase{}; const auto* function = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(target), &actualBase, nullptr);
        RIZ_OPERATION_CHECK(function && actualBase == reinterpret_cast<DWORD64>(base)
            && std::memcmp(function, &before.functions[i], sizeof(*function)) == 0
            && row.bodySize == before.functions[i].EndAddress - before.functions[i].BeginAddress
            && row.unwindRva == before.functions[i].UnwindData);
        ++verified;
    }
    return verified;
}
inline bool Unchanged(const unsigned char* base, const Snapshot& published)
{
    for (unsigned i = 0; i != 26; ++i)
        if (std::memcmp(base + Rvas[i], published.bytes[i], 32) != 0) return false;
    return true;
}
template<class Reader> inline unsigned InvalidHeaders(Reader read, const RizFirstInstallInfoV7& valid, CheckFn check)
{
    unsigned count = 0;
    RIZ_OPERATION_CHECK(read(nullptr) == 1); ++count;
    for (const auto version : {1u,2u,3u,4u,5u,6u}) {
        auto bad = valid; bad.publication.common.abiVersion = version; const auto saved = bad;
        RIZ_OPERATION_CHECK(read(&bad) == 1 && std::memcmp(&bad, &saved, sizeof(bad)) == 0); ++count;
    }
    for (const auto size : {144u,416u,2047u,2049u,2112u}) {
        auto bad = valid; bad.publication.common.byteSize = size; const auto saved = bad;
        RIZ_OPERATION_CHECK(read(&bad) == 1 && std::memcmp(&bad, &saved, sizeof(bad)) == 0); ++count;
    }
    for (unsigned field = 0; field != 5; ++field) {
        auto bad = valid;
        if (field == 0) bad.publication.common.reserved0 = 1;
        else if (field == 1) bad.publication.common.reserved1 = 1;
        else if (field == 2) bad.publication.reserved2 = 1;
        else if(field==3)bad.reserved = 1;
        else bad.singleEvent.reserved=1;
        const auto saved = bad;
        RIZ_OPERATION_CHECK(read(&bad) == 1 && std::memcmp(&bad, &saved, sizeof(bad)) == 0); ++count;
    }
    // Output identity is canonical; a caller cannot submit a synthesized row.
    auto poisoned = valid; poisoned.operationCount = 999; poisoned.configuredMask = 0;
    poisoned.operations[0].target = reinterpret_cast<void*>(1); poisoned.operations[24].epoch = 1;
    RIZ_OPERATION_CHECK(read(&poisoned) == 0 && std::memcmp(&poisoned, &valid, sizeof(valid)) == 0);
    return count;
}
#undef RIZ_OPERATION_CHECK
}
