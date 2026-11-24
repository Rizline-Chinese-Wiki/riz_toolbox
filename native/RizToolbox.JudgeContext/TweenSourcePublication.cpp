#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <cstring>
#include <limits>
#include "TweenSourcePublication.h"
#include "TweenSourceProfile.h"
#include "TweenSourceContext.h"
#include "FirstInstallTweenProfile.h"

namespace {
namespace P = RizTweenSourceProfile;
namespace T = RizFirstInstallTweenProfile;
constexpr DWORD imageSize = 0x243c000, imageTimestamp = 0x6ad0a11b;
constexpr DWORD pdataRva = 0x225b000, pdataSize = 0x14d75c;
constexpr std::size_t allocationSize = 0x2000, siteStride = 0x100, tailOffset = 0xc0;
constexpr std::size_t tableOffset = 0x1800, unwindOffset = 0x1c00, leafUnwindOffset = 0x1cc0;
constexpr std::size_t flagsUnwindOffset = 0x1c90;
constexpr std::size_t patchOffset = 0x1d00, patchStride = 0x20;
constexpr unsigned bindingCount = 43, functionCount = 56, maximumBoundaries = 256;
constexpr std::uint64_t completeMask = (std::uint64_t{1} << P::SiteCount) - 1;
// High 32 bits of a V7 PublishedTweenSource failure. SourceSite + i
// identifies the existing verification of physical site i.
enum SourceFailureDetail : std::uint32_t {
    SourceIncomplete = 1, SourceUnknown = 2, SourceImmutable = 3,
    SourceContextIdentity = 4, SourceContextUnknown = 5, SourcePacket = 6,
    SourceMask = 7, SourceSite = 8
};

struct Parent {
    RUNTIME_FUNCTION function;
    const unsigned char* unwind;
    std::size_t unwindSize;
    const unsigned char* body;
    std::size_t bodySize;
};
constexpr Parent parents[]{
    {{P::UpdateRva, P::UpdateEndRva, P::UpdateUnwindRva}, T::Operation23Unwind,
        sizeof(T::Operation23Unwind), T::Operation23Bytes, sizeof(T::Operation23Bytes)},
    {{0x3956f0, 0x3964e8, 0x1ce5f64}, T::Operation20Unwind,
        sizeof(T::Operation20Unwind), T::Operation20Bytes, sizeof(T::Operation20Bytes)},
    {{0x396e40, 0x398554, 0x1ce5fe0}, T::Operation22Unwind,
        sizeof(T::Operation22Unwind), T::Operation22Bytes, sizeof(T::Operation22Bytes)}
};
struct GeneratedFlagsUnwind {
    std::array<unsigned char, 48> bytes{};
    std::size_t used = 0;
    bool valid = false;
};
constexpr GeneratedFlagsUnwind CreateUpdateFlagsUnwind()
{
    GeneratedFlagsUnwind result{};
    const auto& original = T::Operation23Unwind;
    if (original[0] != 1 || original[3] != 0 || sizeof(original) != 4u + 2u * original[2]) return result;
    auto& output = result.bytes;
    output[0] = 1; // Standalone, no handler/chain; all code offsets/prolog/frame zero.
    std::size_t read = 4, written = 4;
    output[written++] = 0; output[written++] = 2; // UWOP_ALLOC_SMALL: discard PUSHFQ's eight bytes.
    while (read < sizeof(original)) {
        if (read + 2 > sizeof(original)) return result;
        const auto opcode = original[read + 1];
        const auto operation = static_cast<unsigned>(opcode & 0x0f);
        std::size_t width = 0;
        if (operation == 0 || operation == 2) width = 2; // Original PUSH_NONVOL / ALLOC_SMALL.
        else if (operation == 1 && (opcode >> 4) == 0) width = 4; // Original ALLOC_LARGE, scaled WORD.
        else if (operation == 4 || operation == 8) width = 4; // Original SAVE_NONVOL / SAVE_XMM128.
        else return result; // This publisher accepts only the independently fixed Update primary.
        if (read + width > sizeof(original)) return result;
        if (operation == 4 || operation == 8) {
            if (written + 6 > output.size()) return result;
            const auto scaled = static_cast<std::uint32_t>(original[read + 2])
                | (static_cast<std::uint32_t>(original[read + 3]) << 8);
            const auto offset = scaled * (operation == 4 ? 8u : 16u) + 8u;
            output[written++] = 0;
            output[written++] = static_cast<unsigned char>((opcode & 0xf0) | (operation + 1));
            // FAR saves use an unscaled DWORD offset. XMM saves at +8 cannot
            // be represented by their original 16-byte-scaled encoding.
            for (unsigned byte = 0; byte < 4; ++byte)
                output[written++] = static_cast<unsigned char>((offset >> (8u * byte)) & 0xffu);
        } else {
            if (written + width > output.size()) return result;
            output[written++] = 0; output[written++] = opcode;
            for (std::size_t byte = 2; byte < width; ++byte) output[written++] = original[read + byte];
        }
        read += width;
    }
    if (written != 46) return result;
    output[2] = static_cast<unsigned char>((written - 4) / 2);
    result.used = (written + 3) & ~std::size_t{3}; // Two zero padding bytes; no language-specific data.
    result.valid = result.used == output.size();
    return result;
}
constexpr auto updateFlagsUnwind = CreateUpdateFlagsUnwind();
static_assert(updateFlagsUnwind.valid && updateFlagsUnwind.used == 48 && updateFlagsUnwind.bytes[2] == 21);
enum class StackKind : unsigned { Source, Flags, ObserverCall };
struct Range { std::size_t begin, end; unsigned site; StackKind kind; };
struct Boundary { std::size_t offset; unsigned site; StackKind kind; const void* returnPc; };
struct Publication {
    const unsigned char* module = nullptr;
    unsigned char* block = nullptr;
    const void* entry = nullptr;
    std::array<DWORD, P::SiteCount> protection{};
    std::array<Range, functionCount> ranges{};
    std::array<Boundary, maximumBoundaries> boundaries{};
    std::array<unsigned char, allocationSize> expected{};
    unsigned rangeCount = 0, boundaryCount = 0;
    bool sealed = false;
};
Publication state;
SRWLOCK infoLock = SRWLOCK_INIT;
RizTweenSourcePublicationV1 identity{1, sizeof(identity)};
volatile LONG attemptClaimed = 0, commitClaimed = 0, prepared = 0, published = 0, unknown = 0;
volatile LONG firstFailureDetail = 0;
volatile LONG tableRegistered = 0, contextConfigured = 0;
alignas(8) volatile LONG64 publishedMask = 0;

static_assert(sizeof(void*) == 8 && sizeof(DWORD) == 4 && sizeof(RUNTIME_FUNCTION) == 12);
static_assert(P::SiteCount == RizTweenPublishedSourceCount && P::SiteCount == 22);
static_assert(sizeof(RizTweenPublishedSourceSiteV1) == 88 && alignof(RizTweenPublishedSourceSiteV1) == 8);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, sourcePc) == 16);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, preRelay) == 24);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, postRelay) == 32);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, preReturnPc) == 40);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, postReturnPc) == 48);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, continuation) == 56);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, deadContinuation) == 64);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, unwindInfo) == 72);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, width) == 80);
static_assert(offsetof(RizTweenPublishedSourceSiteV1, protection) == 84);
static_assert(sizeof(RizTweenSourcePublicationV1) == 2032 && alignof(RizTweenSourcePublicationV1) == 8);
static_assert(offsetof(RizTweenSourcePublicationV1, epoch) == 32);
static_assert(offsetof(RizTweenSourcePublicationV1, publishedMask) == 40);
static_assert(offsetof(RizTweenSourcePublicationV1, moduleBase) == 48);
static_assert(offsetof(RizTweenSourcePublicationV1, allocation) == 56);
static_assert(offsetof(RizTweenSourcePublicationV1, functionTable) == 64);
static_assert(offsetof(RizTweenSourcePublicationV1, entry) == 72);
static_assert(offsetof(RizTweenSourcePublicationV1, allocationSize) == 80);
static_assert(offsetof(RizTweenSourcePublicationV1, reserved) == 88);
static_assert(offsetof(RizTweenSourcePublicationV1, sites) == 96);
static_assert(P::SiteCount * siteStride <= tableOffset);
static_assert(tableOffset + functionCount * sizeof(RUNTIME_FUNCTION) <= unwindOffset);
static_assert(unwindOffset + 2 * 0x40 + 16 <= flagsUnwindOffset);
static_assert(flagsUnwindOffset + updateFlagsUnwind.bytes.size() <= leafUnwindOffset);
static_assert(leafUnwindOffset + 4 <= patchOffset);
static_assert(patchOffset + P::SiteCount * patchStride <= allocationSize);

LONG AtomicRead(volatile LONG* value) { return InterlockedCompareExchange(value, 0, 0); }
std::uint64_t Mask() { return static_cast<std::uint64_t>(InterlockedCompareExchange64(&publishedMask, 0, 0)); }
std::uint32_t MarkUnknown(std::uint32_t detail = SourceUnknown)
{
    // A source publication read can discover the failure before V7 verifies.
    // Preserve the first result for later readers.
    const auto previous = InterlockedCompareExchange(&firstFailureDetail, static_cast<LONG>(detail), 0);
    InterlockedExchange(&unknown, 1);
    return previous != 0 ? static_cast<std::uint32_t>(previous) : detail;
}
unsigned ParentIndex(unsigned site) { return site == 21 ? 2u : site >= 19 ? 1u : 0u; }
const unsigned char* Patch(unsigned site) { return state.block + patchOffset + site * patchStride; }

bool Executable(DWORD protection)
{
    if (protection & (PAGE_GUARD | PAGE_NOACCESS)) return false;
    const auto value = protection & 0xff;
    return value == PAGE_EXECUTE || value == PAGE_EXECUTE_READ
        || value == PAGE_EXECUTE_READWRITE || value == PAGE_EXECUTE_WRITECOPY;
}
bool Readable(DWORD protection)
{
    if (protection & (PAGE_GUARD | PAGE_NOACCESS)) return false;
    const auto value = protection & 0xff;
    return value == PAGE_READONLY || value == PAGE_READWRITE || value == PAGE_WRITECOPY
        || value == PAGE_EXECUTE_READ || value == PAGE_EXECUTE_READWRITE || value == PAGE_EXECUTE_WRITECOPY;
}
bool Memory(const void* address, std::size_t size, const void* image = nullptr,
    bool executable = false, DWORD* protection = nullptr)
{
    auto cursor = reinterpret_cast<std::uintptr_t>(address);
    if (!cursor || !size || size > (std::numeric_limits<std::uintptr_t>::max)() - cursor) return false;
    const auto end = cursor + size;
    DWORD first = 0;
    while (cursor < end) {
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(reinterpret_cast<const void*>(cursor), &memory, sizeof(memory)) != sizeof(memory)
            || memory.State != MEM_COMMIT || !Readable(memory.Protect)
            || (executable && !Executable(memory.Protect))
            || (image && (memory.AllocationBase != image || memory.Type != MEM_IMAGE))) return false;
        if (!first) first = memory.Protect;
        else if (protection && first != memory.Protect) return false;
        const auto begin = reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
        if (memory.RegionSize > (std::numeric_limits<std::uintptr_t>::max)() - begin) return false;
        const auto next = begin + memory.RegionSize;
        if (next <= cursor) return false;
        cursor = next < end ? next : end;
    }
    if (protection) *protection = first;
    return true;
}
bool ImageIdentity()
{
    const auto* base = state.module;
    if (!Memory(base, sizeof(IMAGE_DOS_HEADER), base)) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0
        || static_cast<unsigned>(dos->e_lfanew) > 0x1000
        || !Memory(base + dos->e_lfanew, sizeof(IMAGE_NT_HEADERS64), base)) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || nt->FileHeader.TimeDateStamp != imageTimestamp || nt->FileHeader.NumberOfSections != 7
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
        || nt->OptionalHeader.SizeOfImage != imageSize || nt->OptionalHeader.SizeOfHeaders != 0x400
        || nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_EXCEPTION) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    return directory.VirtualAddress == pdataRva && directory.Size == pdataSize;
}
bool ParentIdentity(unsigned index)
{
    const auto& parent = parents[index];
    const auto* base = state.module;
    if (!Memory(base + parent.function.UnwindData, parent.unwindSize, base)
        || std::memcmp(base + parent.function.UnwindData, parent.unwind, parent.unwindSize) != 0
        || !Memory(base + pdataRva, pdataSize, base)) return false;
    const auto* table = reinterpret_cast<const RUNTIME_FUNCTION*>(base + pdataRva);
    unsigned found = 0;
    for (std::size_t i = 0; i < pdataSize / sizeof(*table); ++i) {
        if (table[i].BeginAddress < parent.function.EndAddress && table[i].EndAddress > parent.function.BeginAddress) {
            if (std::memcmp(table + i, &parent.function, sizeof(parent.function)) != 0) return false;
            ++found;
        }
    }
    if (found != 1) return false;
    DWORD64 lookupBase = 0;
    const auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(base + parent.function.BeginAddress), &lookupBase, nullptr);
    return actual && lookupBase == reinterpret_cast<DWORD64>(base)
        && std::memcmp(actual, &parent.function, sizeof(parent.function)) == 0;
}
bool ProfileIdentity()
{
    for (unsigned i = 0; i < 3; ++i) {
        const auto& parent = parents[i];
        // A frame register would make the extra pushfq frame require a different
        // chain contract. These three independently fixed primaries have none.
        if (parent.unwind[0] != 1 || parent.unwind[3] != 0
            || parent.unwindSize != 4u + 2u * parent.unwind[2]
            || parent.bodySize != parent.function.EndAddress - parent.function.BeginAddress) return false;
    }
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i]; const auto& parent = parents[ParentIndex(i)];
        if (site.id != i || site.size < 5 || site.size > patchStride
            || site.endRva - site.beginRva != site.size
            || site.beginRva < parent.function.BeginAddress + parent.unwind[1]
            || site.endRva > parent.function.EndAddress
            || std::memcmp(site.bytes, parent.body + site.beginRva - parent.function.BeginAddress, site.size) != 0
            || (i != RizTweenTailDeadDecision && site.bytes[site.size - 5] != 0xe8)) return false;
        if (site.deadContinuationRva && (site.deadContinuationRva < parent.function.BeginAddress
            || site.deadContinuationRva >= parent.function.EndAddress)) return false;
        for (unsigned j = 0; j < i; ++j) {
            if (P::Sites[j].beginRva < site.endRva && P::Sites[j].endRva > site.beginRva) return false;
        }
    }
    return P::Sites[RizTweenTailDeadDecision].size == 13;
}
bool Relative(unsigned char* output, const void* instruction, unsigned char opcode, const void* destination)
{
    const auto start = reinterpret_cast<std::uintptr_t>(instruction), target = reinterpret_cast<std::uintptr_t>(destination);
    if (start > (std::numeric_limits<std::uintptr_t>::max)() - 5) return false;
    const auto next = start + 5;
    std::int32_t displacement = 0;
    if (target >= next) {
        const auto distance = target - next;
        if (distance > static_cast<std::uintptr_t>((std::numeric_limits<std::int32_t>::max)())) return false;
        displacement = static_cast<std::int32_t>(distance);
    } else {
        const auto distance = next - target;
        if (distance > 0x80000000ULL) return false;
        displacement = distance == 0x80000000ULL ? (std::numeric_limits<std::int32_t>::min)()
            : -static_cast<std::int32_t>(distance);
    }
    output[0] = opcode; std::memcpy(output + 1, &displacement, sizeof(displacement)); return true;
}
bool Relative(unsigned char* instruction, unsigned char opcode, const void* destination)
{ return Relative(instruction, instruction, opcode, destination); }
bool Conditional(unsigned char* instruction, unsigned char condition, const void* destination)
{
    // The rel32 begins after the two-byte 0F 8x opcode, so use a virtual E9
    // one byte later solely for computing its actual next-instruction address.
    if (!Relative(instruction + 1, instruction + 1, condition, destination)) return false;
    instruction[0] = 0x0f; return true;
}
bool AddRange(std::size_t begin, std::size_t end, unsigned site, StackKind kind)
{
    if (begin >= end || state.rangeCount >= functionCount || end > tableOffset
        || (state.rangeCount && state.ranges[state.rangeCount - 1].end > begin)) return false;
    state.ranges[state.rangeCount++] = {begin, end, site, kind}; return true;
}
bool AddBoundary(std::size_t offset, unsigned site, StackKind kind, const void* returnPc = nullptr)
{
    if (state.boundaryCount >= maximumBoundaries) return false;
    state.boundaries[state.boundaryCount++] = {offset, site, kind, returnPc}; return true;
}
bool AllocateNear()
{
    SYSTEM_INFO system{}; GetSystemInfo(&system);
    const auto step = static_cast<std::uintptr_t>(system.dwAllocationGranularity);
    const auto base = reinterpret_cast<std::uintptr_t>(state.module);
    if (!step || (step & (step - 1)) != 0) return false;
    for (std::uintptr_t distance = (imageSize + step - 1) & ~(step - 1); distance < 0x70000000ULL; distance += step) {
        if (base > (std::numeric_limits<std::uintptr_t>::max)() - distance - allocationSize) return false;
        state.block = static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(base + distance), allocationSize,
            MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
        if (state.block) break;
    }
    // A successful reserve is permanent even if any later step fails. Its code
    // or function-table pointer may already have escaped into the OS.
    return state.block != nullptr;
}
void CreateUnwind()
{
    for (unsigned i = 0; i < 3; ++i) {
        auto* plain = state.block + unwindOffset + i * 0x40;
        plain[0] = 0x21; plain[1] = 0; plain[2] = 0; plain[3] = 0;
        std::memcpy(plain + 4, &parents[i].function, sizeof(RUNTIME_FUNCTION));
    }
    // Only Update has PUSHFQ ranges. RtlVirtualUnwind keeps the original
    // FrameBase while processing a chain, so ALLOC_SMALL(8)+CHAIN would restore
    // the parent's SAVE registers eight bytes too low. Fuse the fixed primary
    // instead: discard flags first, then restore every SAVE at FrameBase+8+
    // its original offset, and retain the original ALLOC/PUSH order.
    std::memcpy(state.block + flagsUnwindOffset, updateFlagsUnwind.bytes.data(), updateFlagsUnwind.used);
    // The FF25 tail has a real observer CALL return slot. It is a leaf frame,
    // then the registered return PC chains to the original source primary.
    const unsigned char leaf[]{1, 0, 0, 0};
    std::memcpy(state.block + leafUnwindOffset, leaf, sizeof(leaf));
}
bool Selector(unsigned site, std::size_t offset, const void* normal, std::size_t neutralEnd)
{
    const auto begin = site * siteStride;
    auto* selector = state.block + begin + offset;
    const unsigned char bytes[]{0x9c, 0x48, 0x83, 0x7c, 0x24, 0x08, 0x00, 0x75, 0x06, 0x9d,
        0xe9, 0, 0, 0, 0, 0x9d, 0xe9, 0, 0, 0, 0};
    std::memcpy(selector, bytes, sizeof(bytes));
    if (!Relative(selector + 10, 0xe9, normal)
        || !Relative(selector + 16, 0xe9, state.module + P::Sites[site].deadContinuationRva)
        || !AddRange(begin, begin + offset + 1, site, StackKind::Source)
        || !AddRange(begin + offset + 1, begin + offset + 10, site, StackKind::Flags)
        || !AddRange(begin + offset + 10, begin + offset + 15, site, StackKind::Source)
        || !AddRange(begin + offset + 15, begin + offset + 16, site, StackKind::Flags)
        || !AddRange(begin + offset + 16, neutralEnd, site, StackKind::Source)) return false;
    const std::size_t sourceOffsets[]{0, 10, 16};
    const std::size_t flagsOffsets[]{1, 7, 9, 15};
    for (const auto value : sourceOffsets) if (!AddBoundary(begin + offset + value, site, StackKind::Source)) return false;
    for (const auto value : flagsOffsets) if (!AddBoundary(begin + offset + value, site, StackKind::Flags)) return false;
    return true;
}
bool ReplayBoundaries(unsigned site, std::size_t replayOffset)
{
    if (site >= RizTweenPlayingInsertFront && site <= RizTweenPlayingTailClear) {
        const std::size_t normal[]{0, 5, 8, 12, 16}, clear[]{0, 5, 9, 13, 15};
        const auto* offsets = site == RizTweenPlayingMoveClear ? clear : normal;
        for (unsigned i = 0; i < 5; ++i) if (!AddBoundary(replayOffset + offsets[i], site, StackKind::Source)) return false;
        return true;
    }
    return AddBoundary(replayOffset, site, StackKind::Source);
}
bool CreateSite(unsigned site, RizTweenPublishedSourceSiteV1& info,
    RizTweenSourceSiteBindingV1* bindings, unsigned& bindingIndex)
{
    const auto& profile = P::Sites[site];
    const auto begin = site * siteStride; auto* relay = state.block + begin;
    auto* tail = relay + tailOffset;
    const unsigned char tailBytes[]{0xff, 0x25, 0, 0, 0, 0};
    std::memcpy(tail, tailBytes, sizeof(tailBytes)); std::memcpy(tail + 6, &state.entry, sizeof(state.entry));
    if (!Relative(relay, 0xe8, tail) || !AddBoundary(begin, site, StackKind::Source)) return false;
    info = {site, profile.beginRva, profile.endRva, parents[ParentIndex(site)].function.BeginAddress,
        state.module + profile.beginRva, relay, nullptr, relay + 5, nullptr, state.module + profile.endRva,
        profile.deadContinuationRva ? state.module + profile.deadContinuationRva : nullptr,
        state.block + unwindOffset + ParentIndex(site) * 0x40, profile.size, state.protection[site]};
    bindings[bindingIndex++] = {site, site == RizTweenTailDeadDecision ? RizTweenSourceDecision : RizTweenSourceBefore,
        info.sourcePc, info.preReturnPc, site == RizTweenTailDeadDecision ? info.deadContinuation : nullptr};
    std::size_t end = 0;
    if (site == RizTweenTailDeadDecision) {
        // Relocate both conditional destinations independently. The selector's
        // normal branch replays TEST AL / JNE / TEST RBX / JE exactly, so a null
        // or exceptional path retains its original game destination.
        auto* replay = relay + 26;
        replay[0] = 0x84; replay[1] = 0xc0;
        replay[8] = 0x48; replay[9] = 0x85; replay[10] = 0xdb;
        std::int32_t secondDisplacement = 0;
        std::memcpy(&secondDisplacement, profile.bytes + 9, sizeof(secondDisplacement));
        const auto firstRva = static_cast<std::int64_t>(profile.beginRva) + 4 + static_cast<std::int8_t>(profile.bytes[3]);
        const auto secondRva = static_cast<std::int64_t>(profile.endRva) + secondDisplacement;
        if (firstRva <= 0 || firstRva >= imageSize || secondRva <= 0 || secondRva >= imageSize
            || !Conditional(replay + 2, 0x85, state.module + firstRva)
            || !Conditional(replay + 11, 0x84, state.module + secondRva)
            || !Relative(replay + 17, 0xe9, info.continuation)
            || !Selector(site, 5, replay, begin + 48)) return false;
        const std::size_t offsets[]{26, 28, 34, 37, 43};
        for (const auto value : offsets) if (!AddBoundary(begin + value, site, StackKind::Source)) return false;
        end = 48;
    } else {
        auto* replay = relay + 5;
        std::memcpy(replay, profile.bytes, profile.size);
        std::int32_t displacement = 0; std::memcpy(&displacement, profile.bytes + profile.size - 4, sizeof(displacement));
        const auto targetRva = static_cast<std::int64_t>(profile.endRva) + displacement;
        if (targetRva <= 0 || targetRva >= imageSize
            || !Memory(state.module + targetRva, 1, state.module, true)
            || !Relative(replay + profile.size - 5, 0xe8, state.module + targetRva)
            || !ReplayBoundaries(site, begin + 5)) return false;
        auto* after = replay + profile.size;
        if (!Relative(after, 0xe8, tail) || !AddBoundary(begin + 5 + profile.size, site, StackKind::Source)) return false;
        info.postRelay = after; info.postReturnPc = after + 5;
        bindings[bindingIndex++] = {site, RizTweenSourceAfter, info.sourcePc, info.postReturnPc,
            site <= RizTweenReadyPopAppend ? info.deadContinuation : nullptr};
        const auto finish = 10u + profile.size;
        if (site <= RizTweenReadyPopAppend) {
            if (!Selector(site, finish, info.continuation, begin + finish + 21)) return false;
            end = finish + 21;
        } else {
            if (!Relative(relay + finish, 0xe9, info.continuation)
                || !AddBoundary(begin + finish, site, StackKind::Source)
                || !AddRange(begin, begin + finish + 5, site, StackKind::Source)) return false;
            end = finish + 5;
        }
    }
    if (end > tailOffset || !AddRange(begin + tailOffset, begin + tailOffset + 6, site, StackKind::ObserverCall)
        || !AddBoundary(begin + tailOffset, site, StackKind::ObserverCall, info.preReturnPc)
        || (info.postReturnPc && !AddBoundary(begin + tailOffset, site, StackKind::ObserverCall, info.postReturnPc))) return false;
    auto* replacement = state.block + patchOffset + site * patchStride;
    std::memset(replacement, 0x90, patchStride);
    // Replacement is stored in immutable helper data, but its displacement is
    // relative to the actual GameAssembly source instruction, never this buffer.
    return Relative(replacement, info.sourcePc, 0xe9, relay);
}
bool CreateTable()
{
    if (state.rangeCount != functionCount) return false;
    const auto base = reinterpret_cast<std::uintptr_t>(state.module), block = reinterpret_cast<std::uintptr_t>(state.block);
    if (block < base || block - base > MAXDWORD - allocationSize) return false;
    const auto blockRva = block - base;
    auto* table = reinterpret_cast<RUNTIME_FUNCTION*>(state.block + tableOffset);
    for (unsigned i = 0; i < functionCount; ++i) {
        const auto& range = state.ranges[i];
        if (range.kind == StackKind::Flags && ParentIndex(range.site) != 0) return false;
        const auto unwind = range.kind == StackKind::ObserverCall ? leafUnwindOffset
            : range.kind == StackKind::Flags ? flagsUnwindOffset : unwindOffset + ParentIndex(range.site) * 0x40;
        table[i] = {static_cast<DWORD>(blockRva + range.begin), static_cast<DWORD>(blockRva + range.end),
            static_cast<DWORD>(blockRva + unwind)};
    }
    return true;
}
bool BlockIdentity()
{
    MEMORY_BASIC_INFORMATION memory{};
    // Full code, RF table, chained xdata, leaf xdata and source patch bytes must
    // agree before OS lookup or unwind ever consumes this dynamic registration.
    return state.block && state.sealed && AtomicRead(&tableRegistered) == 1
        && state.entry == RizTweenSourceContext_GetEntry()
        && VirtualQuery(state.block, &memory, sizeof(memory)) == sizeof(memory)
        && memory.AllocationBase == state.block && memory.State == MEM_COMMIT && memory.Type == MEM_PRIVATE
        && memory.Protect == PAGE_EXECUTE_READ && memory.RegionSize >= allocationSize
        && Memory(state.block, allocationSize, nullptr, true)
        && std::memcmp(state.block, state.expected.data(), allocationSize) == 0;
}
bool SourceIdentity(unsigned site, bool patched)
{
    const auto& profile = P::Sites[site]; DWORD protection = 0;
    return Memory(state.module + profile.beginRva, profile.size, state.module, true, &protection)
        && protection == state.protection[site]
        && std::memcmp(state.module + profile.beginRva, patched ? Patch(site) : profile.bytes, profile.size) == 0;
}
bool DestinationsIdentity()
{
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i];
        if (!Memory(state.module + site.endRva, 1, state.module, true)
            || (site.deadContinuationRva && !Memory(state.module + site.deadContinuationRva, 1, state.module, true))) return false;
        std::int32_t displacement = 0;
        std::memcpy(&displacement, site.bytes + site.size - 4, sizeof(displacement));
        const auto target = static_cast<std::int64_t>(site.endRva) + displacement;
        if (target <= 0 || target >= imageSize || !Memory(state.module + target, 1, state.module, true)) return false;
        if (i == RizTweenTailDeadDecision) {
            const auto first = static_cast<std::int64_t>(site.beginRva) + 4 + static_cast<std::int8_t>(site.bytes[3]);
            if (first <= 0 || first >= imageSize || !Memory(state.module + first, 1, state.module, true)) return false;
        }
    }
    return true;
}
bool SameContext(const CONTEXT& left, const CONTEXT& right)
{
    return left.Rip == right.Rip && left.Rsp == right.Rsp && left.Rax == right.Rax && left.Rbx == right.Rbx
        && left.Rcx == right.Rcx && left.Rdx == right.Rdx && left.Rbp == right.Rbp && left.Rsi == right.Rsi
        && left.Rdi == right.Rdi && left.R8 == right.R8 && left.R9 == right.R9 && left.R10 == right.R10
        && left.R11 == right.R11 && left.R12 == right.R12 && left.R13 == right.R13 && left.R14 == right.R14
        && left.R15 == right.R15 && left.EFlags == right.EFlags && left.MxCsr == right.MxCsr
        && std::memcmp(&left.FltSave, &right.FltSave, sizeof(left.FltSave)) == 0;
}
void SeedContext(CONTEXT& context, DWORD64 rsp)
{
    context = {}; context.ContextFlags = CONTEXT_ALL; context.Rsp = rsp; context.EFlags = 0x202; context.MxCsr = 0x1f80;
    context.Rax = 0x10101010; context.Rcx = 0x20202020; context.Rdx = 0x30303030; context.Rbx = 0x40404040;
    context.Rbp = 0x50505050; context.Rsi = 0x60606060; context.Rdi = 0x70707070;
    context.R8 = 0x80808080; context.R9 = 0x90909090; context.R10 = 0xa0a0a0a0; context.R11 = 0xb0b0b0b0;
    context.R12 = 0xc0c0c0c0; context.R13 = 0xd0d0d0d0; context.R14 = 0xe0e0e0e0; context.R15 = 0xf0f0f0f0;
    std::memset(&context.FltSave, 0x5a, sizeof(context.FltSave));
}
bool Lookup(std::size_t offset, RUNTIME_FUNCTION*& entry)
{
    const auto* table = reinterpret_cast<const RUNTIME_FUNCTION*>(state.block + tableOffset);
    const auto pc = reinterpret_cast<DWORD64>(state.block + offset);
    DWORD64 lookupBase = 0; entry = RtlLookupFunctionEntry(pc, &lookupBase, nullptr);
    if (!entry || lookupBase != reinterpret_cast<DWORD64>(state.module)) return false;
    for (unsigned i = 0; i < functionCount; ++i) {
        if (state.ranges[i].begin <= offset && offset < state.ranges[i].end)
            return std::memcmp(entry, table + i, sizeof(*entry)) == 0;
    }
    return false;
}
bool BoundaryUnwind(const Boundary& boundary)
{
    // Both comparisons use one controlled stack. Only OS metadata is executed;
    // no original game instruction, replay, observer or synthetic entry runs.
    alignas(16) DWORD64 stack[256]{};
    for (unsigned i = 0; i < 256; ++i) stack[i] = 0x6100000000000000ULL + static_cast<DWORD64>(i) * 0x10001;
    constexpr unsigned sourceIndex = 16;
    const auto sourceRsp = reinterpret_cast<DWORD64>(stack + sourceIndex);
    const auto& parent = parents[ParentIndex(boundary.site)];
    CONTEXT expected{}; SeedContext(expected, sourceRsp);
    expected.Rip = reinterpret_cast<DWORD64>(state.module + parent.function.BeginAddress + parent.unwind[1]);
    auto actual = expected;
    PVOID expectedHandler = nullptr, actualHandler = nullptr; DWORD64 expectedFrame = 0, actualFrame = 0;
    auto parentFunction = parent.function;
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, reinterpret_cast<DWORD64>(state.module), expected.Rip,
        &parentFunction, &expected, &expectedHandler, &expectedFrame, nullptr);
    actual.Rip = reinterpret_cast<DWORD64>(state.block + boundary.offset);
    if (boundary.kind == StackKind::Flags) { stack[sourceIndex - 1] = actual.EFlags; actual.Rsp -= 8; }
    if (boundary.kind == StackKind::ObserverCall) {
        stack[sourceIndex - 1] = reinterpret_cast<DWORD64>(boundary.returnPc); actual.Rsp -= 8;
    }
    RUNTIME_FUNCTION* function = nullptr;
    if (!Lookup(boundary.offset, function)) return false;
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, reinterpret_cast<DWORD64>(state.module), actual.Rip,
        function, &actual, &actualHandler, &actualFrame, nullptr);
    if (boundary.kind == StackKind::ObserverCall) {
        if (actual.Rip != reinterpret_cast<DWORD64>(boundary.returnPc) || actual.Rsp != sourceRsp) return false;
        const auto returnOffset = reinterpret_cast<const unsigned char*>(boundary.returnPc) - state.block;
        if (returnOffset < 0 || !Lookup(static_cast<std::size_t>(returnOffset), function)) return false;
        RtlVirtualUnwind(UNW_FLAG_NHANDLER, reinterpret_cast<DWORD64>(state.module), actual.Rip,
            function, &actual, &actualHandler, &actualFrame, nullptr);
    }
    return SameContext(expected, actual) && expectedHandler == actualHandler
        && expected.Rsp > sourceRsp && expected.Rsp <= reinterpret_cast<DWORD64>(stack + 256);
}
bool VerifyImmutable()
{
    if (!BlockIdentity() || !ImageIdentity() || !ProfileIdentity() || !DestinationsIdentity()) return false;
    for (unsigned i = 0; i < 3; ++i) if (!ParentIdentity(i)) return false;
    // Check each non-overlapping RF's final byte too, without asking the OS to
    // decode an interior instruction byte as an unwind control PC.
    for (const auto& range : state.ranges) {
        RUNTIME_FUNCTION* entry = nullptr;
        if (!Lookup(range.begin, entry) || !Lookup(range.end - 1, entry)) return false;
    }
    for (unsigned i = 0; i < state.boundaryCount; ++i) if (!BoundaryUnwind(state.boundaries[i])) return false;
    return true;
}
std::uint32_t ContextFailure()
{
    RizTweenSourceInfoV1 info{1, sizeof(info)};
    if (AtomicRead(&contextConfigured) != 1 || RizTweenSourceContext_GetInfo(&info) != 0
        || info.abiVersion != 1 || info.byteSize != sizeof(info) || info.configured != 1) return SourceContextIdentity;
    if (info.unknown != 0) {
        const auto detail = RizTweenSourceFirstFaultDetail();
        return detail != 0 ? detail : SourceContextUnknown;
    }
    if (info.bindingCount != bindingCount || info.epoch != 1 || info.metadataBound > 1) return SourceContextIdentity;
    return 0;
}
bool PacketIdentity()
{
    RizTweenSourcePublicationV1 packet{};
    AcquireSRWLockShared(&infoLock); packet = identity; ReleaseSRWLockShared(&infoLock);
    if (packet.abiVersion != 1 || packet.byteSize != sizeof(packet) || packet.prepared || packet.published
        || packet.unknown || packet.siteCount != P::SiteCount || packet.bindingCount != bindingCount
        || packet.functionCount != functionCount || packet.epoch != 1 || packet.publishedMask
        || packet.moduleBase != state.module || packet.allocation != state.block
        || packet.functionTable != state.block + tableOffset || packet.entry != state.entry
        || packet.allocationSize != allocationSize || packet.reserved) return false;
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i]; const auto& value = packet.sites[i];
        const auto* relay = state.block + i * siteStride;
        const bool decision = i == RizTweenTailDeadDecision;
        if (value.site != i || value.beginRva != site.beginRva || value.endRva != site.endRva
            || value.parentRva != parents[ParentIndex(i)].function.BeginAddress
            || value.sourcePc != state.module + site.beginRva || value.preRelay != relay
            || value.preReturnPc != relay + 5
            || value.postRelay != (decision ? nullptr : relay + 5 + site.size)
            || value.postReturnPc != (decision ? nullptr : relay + 10 + site.size)
            || value.continuation != state.module + site.endRva
            || value.deadContinuation != (site.deadContinuationRva ? state.module + site.deadContinuationRva : nullptr)
            || value.unwindInfo != state.block + unwindOffset + ParentIndex(i) * 0x40
            || value.width != site.size || value.protection != state.protection[i]) return false;
    }
    return true;
}
std::uint32_t VerifyFailure(bool requirePublished)
{
    if (AtomicRead(&prepared) != 1) return SourceIncomplete;
    if (AtomicRead(&unknown) != 0) return SourceUnknown;
    if (requirePublished && AtomicRead(&published) != 1) return SourceIncomplete;
    if (!VerifyImmutable()) return SourceImmutable;
    if (const auto failure = ContextFailure()) return failure;
    if (!PacketIdentity()) return SourcePacket;
    const auto mask = Mask();
    if ((mask & ~completeMask) || (AtomicRead(&published) == 1 && mask != completeMask)) return SourceMask;
    for (unsigned i = 0; i < P::SiteCount; ++i)
        if (!SourceIdentity(i, (mask & (std::uint64_t{1} << i)) != 0)) return SourceSite + i;
    if (AtomicRead(&unknown) != 0) return SourceUnknown;
    return requirePublished && mask != completeMask ? SourceMask : 0u;
}
bool Prepare(void* module)
{
    // The enclosing actual pre-init producer already pinned and hashed the
    // 853d6530...b79 image. This private primitive additionally proves its loaded
    // PE/source/RF identities and has no caller-supplied profile or Ready bit.
    if (InterlockedCompareExchange(&attemptClaimed, 1, 0) != 0 || !module) return false;
    state.module = static_cast<const unsigned char*>(module); state.entry = RizTweenSourceContext_GetEntry();
    if (!ImageIdentity() || !ProfileIdentity() || !DestinationsIdentity() || !Memory(state.entry, 1, nullptr, true)) return false;
    for (unsigned i = 0; i < 3; ++i) if (!ParentIdentity(i)) return false;
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i];
        if (!Memory(state.module + site.beginRva, site.size, state.module, true, &state.protection[i])
            || std::memcmp(state.module + site.beginRva, site.bytes, site.size) != 0) return false;
    }
    RizTweenSourcePublicationV1 packet{1, sizeof(packet)};
    packet.moduleBase = module; packet.entry = state.entry; packet.epoch = 1; packet.siteCount = P::SiteCount;
    if (!AllocateNear()) return false;
    packet.allocation = state.block; packet.allocationSize = allocationSize; packet.functionTable = state.block + tableOffset;
    AcquireSRWLockExclusive(&infoLock); identity = packet; ReleaseSRWLockExclusive(&infoLock);
    std::memset(state.block, 0xcc, allocationSize); CreateUnwind();
    std::array<RizTweenSourceSiteBindingV1, bindingCount> bindings{}; unsigned count = 0;
    for (unsigned i = 0; i < P::SiteCount; ++i) if (!CreateSite(i, packet.sites[i], bindings.data(), count)) return false;
    if (count != bindingCount || !CreateTable()) return false;
    std::memcpy(state.expected.data(), state.block, allocationSize);
    DWORD old = 0;
    if (!VirtualProtect(state.block, allocationSize, PAGE_EXECUTE_READ, &old)
        || old != PAGE_READWRITE || !FlushInstructionCache(GetCurrentProcess(), state.block, allocationSize)) return false;
    state.sealed = true;
    auto* table = reinterpret_cast<RUNTIME_FUNCTION*>(state.block + tableOffset);
    if (!RtlAddFunctionTable(table, functionCount, reinterpret_cast<DWORD64>(module))) return false;
    InterlockedExchange(&tableRegistered, 1); packet.functionCount = functionCount;
    AcquireSRWLockExclusive(&infoLock); identity = packet; ReleaseSRWLockExclusive(&infoLock);
    if (!VerifyImmutable()) return false;
    const RizTweenSourceConfigV1 config{1, sizeof(config), module, nullptr, nullptr, bindingCount, 0, bindings.data()};
    if (RizTweenSourceConfigure(&config) != 0) return false;
    InterlockedExchange(&contextConfigured, 1); packet.bindingCount = bindingCount;
    if (ContextFailure() || AtomicRead(&unknown) != 0) return false;
    for (unsigned i = 0; i < P::SiteCount; ++i) if (!SourceIdentity(i, false)) return false;
    AcquireSRWLockExclusive(&infoLock); identity = packet; ReleaseSRWLockExclusive(&infoLock);
    InterlockedExchange(&prepared, 1); return true;
}
bool Commit(RizTweenSourceWriteV1 write)
{
    if (!write || InterlockedCompareExchange(&commitClaimed, 1, 0) != 0 || VerifyFailure(false) || Mask() != 0) return false;
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i];
        if (AtomicRead(&unknown) != 0 || !SourceIdentity(i, false)
            || !write(const_cast<unsigned char*>(state.module + site.beginRva), site.bytes, Patch(i), site.size, state.protection[i])
            || !SourceIdentity(i, true)) return false;
        InterlockedOr64(&publishedMask, static_cast<LONG64>(std::uint64_t{1} << i));
    }
    if (Mask() != completeMask || VerifyFailure(false)) return false;
    InterlockedExchange(&published, 1); return true;
}
}

// Propagate any SEH failure to the enclosing producer's guard, while retaining
// its permanent resources and sticky Unknown. No exception becomes success.
bool RizTweenSourcePreparePublication(void* actualModule)
{
    bool result = false;
    __try { result = Prepare(actualModule); }
    __finally { if (!result) MarkUnknown(); }
    return result;
}
bool RizTweenSourceCommitPublication(RizTweenSourceWriteV1 write)
{
    bool result = false;
    __try { result = Commit(write); }
    __finally { if (!result) MarkUnknown(); }
    return result;
}
bool RizTweenSourceVerifyPublication(bool requirePublished, std::uint32_t* failureDetail)
{
    std::uint32_t failure = SourceUnknown;
    bool finished = false;
    __try {
        failure = VerifyFailure(requirePublished);
        const auto first = failure != 0 ? MarkUnknown(failure) : 0u;
        if (failureDetail) *failureDetail = first;
        finished = true;
    }
    __finally { if (!finished) MarkUnknown(); }
    return failure == 0;
}
bool RizTweenSourceCopyPublication(RizTweenSourcePublicationV1* output)
{
    if (!output || output->abiVersion != 1 || output->byteSize != sizeof(*output)) return false;
    const auto valid = RizTweenSourceVerifyPublication(AtomicRead(&published) == 1);
    RizTweenSourcePublicationV1 copy{};
    AcquireSRWLockShared(&infoLock); copy = identity; ReleaseSRWLockShared(&infoLock);
    copy.prepared = static_cast<std::uint32_t>(AtomicRead(&prepared));
    copy.published = static_cast<std::uint32_t>(AtomicRead(&published));
    copy.unknown = static_cast<std::uint32_t>(AtomicRead(&unknown)); copy.publishedMask = Mask();
    *output = copy; return valid;
}
bool RizTweenSourceGetPublishedPatch(std::uint32_t site, const void** source,
    const unsigned char** bytes, std::size_t* size)
{
    if (site >= P::SiteCount || !source || !bytes || !size || AtomicRead(&prepared) != 1
        || AtomicRead(&published) != 1 || AtomicRead(&unknown) != 0 || Mask() != completeMask) return false;
    if (!BlockIdentity() || !SourceIdentity(site, true)) { MarkUnknown(); return false; }
    *source = state.module + P::Sites[site].beginRva; *bytes = Patch(site); *size = P::Sites[site].size; return true;
}
