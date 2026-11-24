// Included only by the actual FirstInstall translation unit. None of these
// producer operations is exported. In particular, the readers cannot capture a
// baseline, register an original, or advance the write journal.
namespace PlaybackAttestation {
constexpr unsigned OriginalCount = 28, WriteCount = 64;
struct MemoryFact {
    std::uintptr_t start{}, end{};
    const void* allocation{};
    DWORD allocationProtect{}, protect{}, type{}, state{};
};
bool Facts(const void* address, std::size_t length, std::vector<MemoryFact>& output)
{
    auto next = reinterpret_cast<std::uintptr_t>(address);
    if (!next || !length || length > (std::numeric_limits<std::uintptr_t>::max)() - next) return false;
    const auto end = next + length;
    while (next < end) {
        MEMORY_BASIC_INFORMATION value{};
        if (VirtualQuery(reinterpret_cast<const void*>(next), &value, sizeof(value)) != sizeof(value)
            || value.State != MEM_COMMIT || (value.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return false;
        const auto stop = reinterpret_cast<std::uintptr_t>(value.BaseAddress) + value.RegionSize;
        if (stop <= next) return false;
        const auto covered = (std::min)(end, stop);
        output.push_back({next, covered, value.AllocationBase, value.AllocationProtect, value.Protect, value.Type, value.State});
        next = covered;
    }
    return true;
}
bool SameFacts(const std::vector<MemoryFact>& facts)
{
    for (const auto& expected : facts) {
        auto next = expected.start;
        while (next < expected.end) {
            MEMORY_BASIC_INFORMATION actual{};
            if (VirtualQuery(reinterpret_cast<const void*>(next), &actual, sizeof(actual)) != sizeof(actual)
                || actual.AllocationBase != expected.allocation || actual.AllocationProtect != expected.allocationProtect
                || actual.Protect != expected.protect || actual.Type != expected.type || actual.State != expected.state) return false;
            const auto stop = reinterpret_cast<std::uintptr_t>(actual.BaseAddress) + actual.RegionSize;
            if (reinterpret_cast<std::uintptr_t>(actual.BaseAddress) > next || stop <= next) return false;
            next = (std::min)(expected.end, stop);
        }
    }
    return true;
}
struct ImageRange {
    std::uint32_t rva{}, size{}, flags{};
    std::vector<unsigned char> before, expected;
    std::vector<MemoryFact> facts;
};
// This is a closed list of the actual supported fixed prefixes, not an x64
// decoder. Each instruction is independently compared with its source bytes.
// Save=1, Push=2, Allocate=3, MovRbpRsp=4, volatile/no-stack move=0.
struct Step { unsigned end, kind, reg, value; };
struct Shape { unsigned moved, count; Step steps[6]; };
constexpr Shape PushRsiAlloc20{6,2,{{2,2,6,0},{6,3,0,32}}};
constexpr Shape TwoHomeSaves{10,2,{{5,1,3,8},{10,1,6,16}}};
constexpr Shape HomeRbxPushRdi{6,2,{{5,1,3,8},{6,2,7,0}}};
constexpr Shape Alloc28Move{8,2,{{4,3,0,40},{8,0,0,0}}};
constexpr Shape HomeRbpPushRsi{6,2,{{5,1,5,24},{6,2,6,0}}};
constexpr Shape PushRbxAlloc20{6,2,{{2,2,3,0},{6,3,0,32}}};
constexpr Shape TwoOtherHomeSaves{10,2,{{5,1,3,16},{10,1,5,24}}};
constexpr Shape FourPushes{6,4,{{2,2,3,0},{3,2,6,0},{4,2,7,0},{6,2,14,0}}};
constexpr Shape FourPushesAlloc28{9,5,{{2,2,3,0},{3,2,5,0},{4,2,6,0},{5,2,7,0},{9,3,0,40}}};
constexpr Shape ThreePushesFrame{7,4,{{2,2,5,0},{3,2,6,0},{4,2,7,0},{7,4,5,0}}};
constexpr Shape FourPushesR15{6,4,{{2,2,5,0},{3,2,6,0},{4,2,7,0},{6,2,15,0}}};
constexpr Shape FourPushesR12R13{7,4,{{2,2,5,0},{3,2,6,0},{5,2,12,0},{7,2,13,0}}};
constexpr Shape PushRdiAlloc20{6,2,{{2,2,7,0},{6,3,0,32}}};
constexpr Shape TwoPushesAlloc48{8,3,{{2,2,5,0},{4,2,13,0},{8,3,0,72}}};
constexpr Shape Shapes[OriginalCount]{
    PushRsiAlloc20, TwoHomeSaves,
    TwoHomeSaves, HomeRbxPushRdi, Alloc28Move, PushRsiAlloc20,
    HomeRbpPushRsi, PushRbxAlloc20, HomeRbxPushRdi, HomeRbxPushRdi,
    TwoOtherHomeSaves, PushRsiAlloc20, PushRsiAlloc20, FourPushes,
    FourPushesAlloc28, FourPushesAlloc28, HomeRbxPushRdi, HomeRbxPushRdi,
    PushRbxAlloc20, HomeRbxPushRdi, HomeRbxPushRdi, HomeRbxPushRdi,
    ThreePushesFrame, HomeRbxPushRdi, FourPushesR15, FourPushesR12R13,
    PushRdiAlloc20, TwoPushesAlloc48
};
struct Original {
    const unsigned char* target{}; unsigned char* code{}; const unsigned char* rawCode{}; const void* entry{};
    std::uint64_t epoch{};
    unsigned moved{}, length{}, functions{}, unwindBytes{}, lookups{}, boundaries{};
    unsigned char raw[48]{}, expected[48]{};
    unsigned char* block{};
    std::vector<unsigned char> blockBytes;
    std::vector<MemoryFact> codeFacts, rawFacts, blockFacts;
    bool supported{};
};
struct Owner {
    HANDLE file = INVALID_HANDLE_VALUE, dobbyFile = INVALID_HANDLE_VALUE;
    BY_HANDLE_FILE_INFORMATION fileInfo{}, dobbyInfo{};
    HMODULE module{}, helper{}, dobby{};
    std::uint32_t imageSize{}, supported{}, prepared{}, count{};
    volatile LONG state{}, unknown{};
    std::uint64_t epoch = 1;
    std::vector<ImageRange> image;
    Original originals[OriginalCount]{};
    RizFirstInstallPlaybackRangeV1 writes[WriteCount]{};
    const unsigned char* pending{}; std::size_t pendingLength{};
    unsigned char pendingBefore[32]{};
    bool preparedSeal{}, published{};
};
Owner owner;
void Fault() { InterlockedExchange(&owner.unknown, 1); InterlockedExchange(&owner.state, 4); }
bool SameFile(HANDLE file, const BY_HANDLE_FILE_INFORMATION& expected)
{
    BY_HANDLE_FILE_INFORMATION value{};
    return file != INVALID_HANDLE_VALUE && GetFileInformationByHandle(file, &value)
        && value.dwVolumeSerialNumber == expected.dwVolumeSerialNumber && value.nFileIndexHigh == expected.nFileIndexHigh
        && value.nFileIndexLow == expected.nFileIndexLow && value.nFileSizeHigh == expected.nFileSizeHigh
        && value.nFileSizeLow == expected.nFileSizeLow
        && CompareFileTime(&value.ftLastWriteTime, &expected.ftLastWriteTime) == 0;
}
bool ReadFileBytes(HANDLE file, std::vector<unsigned char>& bytes)
{
    LARGE_INTEGER size{}, zero{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart < 512 || size.QuadPart > 128 * 1024 * 1024
        || !SetFilePointerEx(file, zero, nullptr, FILE_BEGIN)) return false;
    bytes.resize(static_cast<std::size_t>(size.QuadPart)); DWORD count{};
    return ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr) && count == bytes.size();
}
bool ImageCurrent()
{
    if (owner.unknown || !owner.module || !SameFile(owner.file, owner.fileInfo) || !SameFile(owner.dobbyFile, owner.dobbyInfo)) return false;
    const auto* base = reinterpret_cast<const unsigned char*>(owner.module);
    for (const auto& range : owner.image)
        if (!SameFacts(range.facts) || std::memcmp(base + range.rva, range.expected.data(), range.size) != 0) return false;
    return true;
}
bool Begin(HMODULE module, HMODULE helper, HMODULE dobby, HANDLE file, HANDLE dobbyFile)
{
    if (owner.state || !module || !helper || !dobby || !StillPreparing()) return false;
    owner.state = 1; owner.module = module; owner.helper = helper; owner.dobby = dobby;
    if (!DuplicateHandle(GetCurrentProcess(), file, GetCurrentProcess(), &owner.file, 0, FALSE, DUPLICATE_SAME_ACCESS)
        || !DuplicateHandle(GetCurrentProcess(), dobbyFile, GetCurrentProcess(), &owner.dobbyFile, 0, FALSE, DUPLICATE_SAME_ACCESS)
        || !GetFileInformationByHandle(owner.file, &owner.fileInfo) || !GetFileInformationByHandle(owner.dobbyFile, &owner.dobbyInfo)) return false;
    std::vector<unsigned char> raw;
    if (!ReadFileBytes(owner.file, raw)) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(raw.data());
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 64
        || static_cast<std::size_t>(dos->e_lfanew) > raw.size() - sizeof(IMAGE_NT_HEADERS64)) return false;
    const auto* pe = reinterpret_cast<const IMAGE_NT_HEADERS64*>(raw.data() + dos->e_lfanew);
    if (pe->Signature != IMAGE_NT_SIGNATURE || pe->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || pe->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC || pe->OptionalHeader.SizeOfImage != Profile::ImageSize
        || pe->FileHeader.TimeDateStamp != Profile::TimeDateStamp || !pe->FileHeader.NumberOfSections
        || pe->FileHeader.NumberOfSections > 96) return false;
    const auto* sections = IMAGE_FIRST_SECTION(pe);
    if (reinterpret_cast<const unsigned char*>(sections + pe->FileHeader.NumberOfSections) > raw.data() + raw.size()) return false;
    owner.imageSize = pe->OptionalHeader.SizeOfImage;
    const auto* base = reinterpret_cast<const unsigned char*>(module);
    auto fileAt = [&](DWORD rva, DWORD count) -> const unsigned char* {
        for (unsigned i = 0; i < pe->FileHeader.NumberOfSections; ++i) {
            const auto& section = sections[i];
            if (rva >= section.VirtualAddress && rva - section.VirtualAddress <= section.SizeOfRawData
                && count <= section.SizeOfRawData - (rva - section.VirtualAddress)
                && section.PointerToRawData <= raw.size() && section.SizeOfRawData <= raw.size() - section.PointerToRawData)
                return raw.data() + section.PointerToRawData + rva - section.VirtualAddress;
        }
        return nullptr;
    };
    const auto delta = reinterpret_cast<std::uint64_t>(module) - pe->OptionalHeader.ImageBase;
    std::vector<DWORD> relocations;
    const auto directory = pe->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC];
    if (directory.Size) {
        const auto* bytes = fileAt(directory.VirtualAddress, directory.Size);
        if (!bytes) return false;
        for (DWORD cursor = 0; cursor < directory.Size;) {
            if (directory.Size - cursor < 8) return false;
            const auto* block = reinterpret_cast<const IMAGE_BASE_RELOCATION*>(bytes + cursor);
            if (block->SizeOfBlock < 8 || (block->SizeOfBlock & 1) || block->SizeOfBlock > directory.Size - cursor) return false;
            for (DWORD at = 8; at < block->SizeOfBlock; at += 2) {
                WORD item{}; std::memcpy(&item, bytes + cursor + at, 2); if (!(item >> 12)) continue;
                const auto target = static_cast<std::uint64_t>(block->VirtualAddress) + (item & 4095);
                if ((item >> 12) != IMAGE_REL_BASED_DIR64 || target > owner.imageSize - 8
                    || std::find(relocations.begin(), relocations.end(), static_cast<DWORD>(target)) != relocations.end()) return false;
                relocations.push_back(static_cast<DWORD>(target));
            }
            cursor += block->SizeOfBlock;
        }
    }
    ImageRange headers{}; headers.size = pe->OptionalHeader.SizeOfHeaders;
    if (headers.size > raw.size() || headers.size > owner.imageSize) return false;
    headers.before.assign(raw.begin(), raw.begin() + headers.size);
    const auto baseOffset = static_cast<std::size_t>(dos->e_lfanew) + offsetof(IMAGE_NT_HEADERS64, OptionalHeader) + offsetof(IMAGE_OPTIONAL_HEADER64, ImageBase);
    std::uint64_t mappedBase{}; std::memcpy(&mappedBase, base + baseOffset, 8);
    if (mappedBase != pe->OptionalHeader.ImageBase && mappedBase != reinterpret_cast<std::uint64_t>(module)) return false;
    std::memcpy(headers.before.data() + baseOffset, &mappedBase, 8); headers.expected = headers.before;
    if (!Facts(base, headers.size, headers.facts) || std::memcmp(base, headers.expected.data(), headers.size) != 0) return false;
    owner.image.push_back(std::move(headers));
    const auto iat = pe->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IAT];
    if (iat.VirtualAddress > owner.imageSize || iat.Size > owner.imageSize - iat.VirtualAddress) return false;
    for (unsigned i = 0; i < pe->FileHeader.NumberOfSections; ++i) {
        const auto& section = sections[i]; const auto size = section.Misc.VirtualSize;
        if (!size) continue;
        if (section.VirtualAddress > owner.imageSize || size > owner.imageSize - section.VirtualAddress
            || section.PointerToRawData > raw.size() || section.SizeOfRawData > raw.size() - section.PointerToRawData) return false;
        if (!(section.Characteristics & IMAGE_SCN_MEM_EXECUTE)
            && (!(section.Characteristics & IMAGE_SCN_MEM_READ) || (section.Characteristics & IMAGE_SCN_MEM_WRITE))) continue;
        ImageRange range{}; range.rva = section.VirtualAddress; range.size = size; range.flags = section.Characteristics;
        range.before.resize(size);
        std::memcpy(range.before.data(), raw.data() + section.PointerToRawData, (std::min)(size, section.SizeOfRawData));
        for (const auto rva : relocations) if (rva >= range.rva && rva - range.rva < size) {
            if (size - (rva - range.rva) < 8) return false;
            std::uint64_t value{}; std::memcpy(&value, range.before.data() + rva - range.rva, 8); value += delta;
            std::memcpy(range.before.data() + rva - range.rva, &value, 8);
        }
        // The loader alone binds the PE-declared IAT. It is excluded from the
        // file comparison only in non-executable memory, then immediately sealed
        // before Prepare. No caller supplies exclusions and no source/UW can be
        // in this range. Executable bytes never receive a loader exception.
        for (DWORD at = 0; at < size; ++at) {
            const auto rva = static_cast<std::uint64_t>(range.rva) + at;
            if (!(range.flags & IMAGE_SCN_MEM_EXECUTE) && rva >= iat.VirtualAddress && rva - iat.VirtualAddress < iat.Size)
                range.before[at] = base[range.rva + at];
        }
        range.expected = range.before;
        if (!Facts(base + range.rva, size, range.facts) || std::memcmp(base + range.rva, range.expected.data(), size) != 0) return false;
        owner.image.push_back(std::move(range));
    }
    return ImageCurrent();
}
bool StepMatches(const unsigned char* source, const Shape& shape)
{
    unsigned start = 0;
    for (unsigned i = 0; i < shape.count; ++i) {
        const auto& step = shape.steps[i]; const auto width = step.end - start; const auto* code = source + start;
        if (step.kind == 1) {
            if (width != 5 || code[0] != 0x48 || code[1] != 0x89 || code[2] != (0x44 | (step.reg << 3))
                || code[3] != 0x24 || code[4] != step.value) return false;
        } else if (step.kind == 2) {
            if (width == 1) { if (step.reg >= 8 || code[0] != 0x50 + step.reg) return false; }
            else if (width != 2 || code[0] != (step.reg < 8 ? 0x40 : 0x41) || code[1] != 0x50 + (step.reg & 7)) return false;
        } else if (step.kind == 3) {
            if (width != 4 || code[0] != 0x48 || code[1] != 0x83 || code[2] != 0xec || code[3] != step.value) return false;
        } else if (step.kind == 4) {
            if (width != 3 || code[0] != 0x48 || code[1] != 0x8b || code[2] != 0xec) return false;
        } else if (width != 4 || code[0] != 0x48 || code[1] != 0x8b || code[2] != 0x49 || code[3] != 0x10) return false;
        start = step.end;
    }
    return start == shape.moved;
}
DWORD64* Integer(CONTEXT& value, unsigned reg)
{
    switch (reg) {
    case 3: return &value.Rbx; case 5: return &value.Rbp; case 6: return &value.Rsi; case 7: return &value.Rdi;
    case 12: return &value.R12; case 13: return &value.R13; case 14: return &value.R14; case 15: return &value.R15;
    default: return nullptr;
    }
}
std::uint64_t RegisterValue(unsigned reg) { return 0x14567890abc00000ULL + reg * 0x10101ULL; }
bool Boundary(const Original& original, const Shape& shape, unsigned completed, unsigned pcOffset, PRUNTIME_FUNCTION function)
{
    alignas(16) CONTEXT context{}; context.ContextFlags = CONTEXT_FULL;
    std::uint64_t stack[256]{}; const auto entryRsp = reinterpret_cast<std::uint64_t>(stack + 128);
    constexpr std::uint64_t returnPc = 0x1234567812345678ULL;
    stack[128] = returnPc;
    constexpr unsigned registers[]{3,5,6,7,12,13,14,15};
    for (const auto reg : registers) *Integer(context, reg) = RegisterValue(reg);
    std::memset(&context.Xmm6, 0x5a, 10 * sizeof(M128A));
    unsigned delta = 0;
    for (unsigned i = 0; i < completed; ++i) {
        const auto& step = shape.steps[i];
        if (step.kind == 1) {
            *reinterpret_cast<std::uint64_t*>(entryRsp - delta + step.value) = RegisterValue(step.reg);
            *Integer(context, step.reg) = 0xbad0000000000000ULL + step.reg;
        }
        else if (step.kind == 2) {
            delta += 8; *reinterpret_cast<std::uint64_t*>(entryRsp - delta) = RegisterValue(step.reg);
            *Integer(context, step.reg) = 0xbad0000000000000ULL + step.reg;
        }
        else if (step.kind == 3) delta += step.value;
        else if (step.kind == 4) context.Rbp = entryRsp - delta;
    }
    context.Rip = reinterpret_cast<DWORD64>(original.code) + pcOffset; context.Rsp = entryRsp - delta;
    PVOID data{}; DWORD64 establisher{};
    RtlVirtualUnwind(0, reinterpret_cast<DWORD64>(original.code), context.Rip, function, &context, &data, &establisher, nullptr);
    if (context.Rip != returnPc || context.Rsp != entryRsp + 8) return false;
    for (const auto reg : registers) if (*Integer(context, reg) != RegisterValue(reg)) return false;
    const auto* xmm = reinterpret_cast<const unsigned char*>(&context.Xmm6);
    for (unsigned i = 0; i < 10 * sizeof(M128A); ++i) if (xmm[i] != 0x5a) return false;
    return true;
}
bool VerifyOriginal(Original& original, const Shape& shape, bool facts)
{
    if (!original.supported || !original.epoch || !ReadableRange(original.code, original.length, nullptr, true)
        || std::memcmp(original.code, original.expected, original.length) != 0
        || !ReadableRange(original.rawCode, original.length, nullptr, true)
        || std::memcmp(original.rawCode, original.raw, original.length) != 0
        || !ReadableRange(original.block, original.blockBytes.size())
        || std::memcmp(original.block, original.blockBytes.data(), original.blockBytes.size()) != 0
        || (facts && (!SameFacts(original.codeFacts) || !SameFacts(original.rawFacts) || !SameFacts(original.blockFacts)))) return false;
    const auto* table = reinterpret_cast<PRUNTIME_FUNCTION>(original.block);
    for (unsigned i = 0; i < original.functions; ++i) {
        const auto pc = i ? shape.steps[i - 1].end : 0;
        DWORD64 imageBase{};
        auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(original.code) + pc, &imageBase, nullptr);
        if (actual != table + i || imageBase != reinterpret_cast<DWORD64>(original.code)
            || !Boundary(original, shape, i, pc, actual)) return false;
    }
    for (const auto offset : {10u,13u}) {
        DWORD64 imageBase{}; const auto pc = shape.moved + offset;
        auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(original.code) + pc, &imageBase, nullptr);
        if (actual != table + original.functions - 1 || imageBase != reinterpret_cast<DWORD64>(original.code)
            || !Boundary(original, shape, shape.count, pc, actual)) return false;
    }
    return true;
}
bool Prove(unsigned index, Original& original, const void* target, const void* entry, void* code)
{
    if (index >= OriginalCount || original.code || original.rawCode) return false;
    const auto& shape = Shapes[index];
    original.target = static_cast<const unsigned char*>(target); original.rawCode = static_cast<unsigned char*>(code); original.entry = entry;
    original.moved = shape.moved; original.length = shape.moved + 14;
    if (!target || !entry || !code || !StepMatches(original.target, shape)
        || !ReadableRange(code, original.length, nullptr, true) || std::memcmp(code, target, shape.moved) != 0) return false;
    std::memcpy(original.raw, code, original.length);
    const auto* tail = original.rawCode + shape.moved;
    const void* continuation = original.target + shape.moved;
    if (tail[0] != 0xff || tail[1] != 0x25 || tail[2] || tail[3] || tail[4] || tail[5]
        || std::memcmp(tail + 6, &continuation, 8) != 0) return false;
    // A RIP-indirect JMP is a Windows epilogue form. Replace only that exact
    // verified Dobby tail in an independent private RX allocation. Never write
    // or protect Dobby's shared arena: later legitimate Prepare calls may use
    // adjacent slots. Retain and continuously verify both actual raw Dobby bytes
    // and the exact normalized relocation configured into the native lane.
    std::memcpy(original.expected, original.raw, original.length);
    auto* rewritten = original.expected + shape.moved;
    rewritten[0] = 0x49; rewritten[1] = 0xbb; std::memcpy(rewritten + 2, &continuation, 8);
    rewritten[10] = 0x41; rewritten[11] = 0xff; rewritten[12] = 0xe3; rewritten[13] = 0x90;
    // Keep the code and its unwind RVAs in one reservation. The fixed Windows
    // x64 profile uses 4 KiB pages; refuse another page size so RX code and
    // read-only metadata always have separate protection domains.
    constexpr std::size_t pageBytes = 4096;
    SYSTEM_INFO systemInfo{}; GetSystemInfo(&systemInfo);
    if (systemInfo.dwPageSize != pageBytes) return false;
    DWORD ignored{};
    original.code = static_cast<unsigned char*>(VirtualAlloc(nullptr, 2 * pageBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!original.code) return false;
    std::memcpy(original.code, original.expected, original.length);
    if (!VirtualProtect(original.code, pageBytes, PAGE_EXECUTE_READ, &ignored)
        || !FlushInstructionCache(GetCurrentProcess(), original.code, original.length)) return false;
    original.functions = shape.count + 1;
    // Both pages were committed atomically. Every unwind RVA is positive and
    // bounded by this 8 KiB allocation, independent of neighboring address space.
    original.block = original.code + pageBytes;
    if (!original.block) return false;
    auto* table = reinterpret_cast<PRUNTIME_FUNCTION>(original.block);
    unsigned cursor = 128;
    for (unsigned completed = 0; completed <= shape.count; ++completed) {
        const auto begin = completed ? shape.steps[completed - 1].end : 0;
        const auto end = completed == shape.count ? original.length : shape.steps[completed].end;
        table[completed] = {begin, end, static_cast<DWORD>(reinterpret_cast<std::uintptr_t>(original.block + cursor) - reinterpret_cast<std::uintptr_t>(original.code))};
        auto* unwind = original.block + cursor; unwind[0] = 1;
        unsigned stackDelta = 0;
        for (unsigned i = 0; i < completed; ++i) {
            if (shape.steps[i].kind == 2) stackDelta += 8;
            else if (shape.steps[i].kind == 3) stackDelta += shape.steps[i].value;
        }
        unsigned at = 4, runningDelta = 0;
        for (unsigned i = 0; i < completed; ++i) {
            const auto& step = shape.steps[i]; unsigned offset{};
            if (step.kind == 1) offset = step.value + stackDelta - runningDelta;
            else if (step.kind == 2) { runningDelta += 8; offset = stackDelta - runningDelta; }
            else { if (step.kind == 3) runningDelta += step.value; continue; }
            unwind[at++] = 0; unwind[at++] = static_cast<unsigned char>((step.reg << 4) | 4);
            const auto scaled = static_cast<WORD>(offset / 8); std::memcpy(unwind + at, &scaled, 2); at += 2;
        }
        if (stackDelta) { unwind[at++] = 0; unwind[at++] = static_cast<unsigned char>(((stackDelta - 8) / 8) << 4 | 2); }
        unwind[2] = static_cast<unsigned char>((at - 4) / 2);
        const auto size = (at + 3) & ~3u; cursor += size;
    }
    original.unwindBytes = cursor - 128;
    original.blockBytes.assign(original.block, original.block + cursor);
    if (!VirtualProtect(original.block, pageBytes, PAGE_READONLY, &ignored)
        || !RtlAddFunctionTable(table, original.functions, reinterpret_cast<DWORD64>(original.code))
        || !Facts(original.block, cursor, original.blockFacts)) return false;
    original.lookups = original.boundaries = original.functions + 2;
    original.supported = true;
    return true;
}
bool Prepared(unsigned index, const void* target, const void* entry, void*& code)
{
    if (index >= OriginalCount || owner.state != 1 || owner.unknown || owner.prepared != index || !ImageCurrent()) return false;
    ++owner.prepared;
    if (!Prove(index, owner.originals[index], target, entry, code)) return false;
    code = owner.originals[index].code; ++owner.supported; return true;
}
bool SetEpoch(unsigned index, std::uint64_t epoch)
{
    if (index >= OriginalCount || !epoch || owner.originals[index].epoch || !owner.originals[index].supported) return false;
    owner.originals[index].epoch = epoch;
    return VerifyOriginal(owner.originals[index], Shapes[index], false);
}
bool SealPrepared()
{
    if (owner.state != 1 || owner.prepared != OriginalCount || owner.supported != OriginalCount || owner.unknown || !ImageCurrent()) return false;
    // Capture exact covered facts after all initial preparations. The raw arena
    // remains Dobby-owned and writable; the configured original is independent RX.
    for (unsigned i = 0; i < OriginalCount; ++i)
        if (!Facts(owner.originals[i].code, owner.originals[i].length, owner.originals[i].codeFacts)
            || !Facts(owner.originals[i].rawCode, owner.originals[i].length, owner.originals[i].rawFacts)
            || !VerifyOriginal(owner.originals[i], Shapes[i], true)) return false;
    owner.preparedSeal = true; owner.state = 2; return true;
}
bool OriginalsCurrent()
{
    if (!owner.preparedSeal || owner.supported != OriginalCount) return false;
    for (unsigned i = 0; i < OriginalCount; ++i) if (!VerifyOriginal(owner.originals[i], Shapes[i], true)) return false;
    return true;
}
bool BeforeWrite(const void* address, std::size_t length)
{
    if (owner.unknown || owner.state != 2 || owner.pending || owner.count >= WriteCount || !length || length > 32
        || !ImageCurrent() || !OriginalsCurrent()) return false;
    const auto* source = static_cast<const unsigned char*>(address);
    const auto rva = reinterpret_cast<std::uintptr_t>(source) - reinterpret_cast<std::uintptr_t>(owner.module);
    bool contained = false;
    for (const auto& range : owner.image)
        if ((range.flags & IMAGE_SCN_MEM_EXECUTE) && rva >= range.rva && rva - range.rva <= range.size && length <= range.size - (rva - range.rva)) contained = true;
    for (unsigned i = 0; i < owner.count; ++i) {
        const auto* prior = static_cast<const unsigned char*>(owner.writes[i].address);
        if (source < prior + owner.writes[i].length && prior < source + length) return false;
    }
    if (!contained) return false;
    owner.pending = source; owner.pendingLength = length; std::memcpy(owner.pendingBefore, source, length); return true;
}
bool AfterWrite(const void* address, const unsigned char* exactBytes, std::size_t length)
{
    if (owner.unknown || owner.state != 2 || owner.pending != address || owner.pendingLength != length || !exactBytes
        || std::memcmp(address, exactBytes, length) != 0) return false;
    auto& row = owner.writes[owner.count]; row.abiVersion = 1; row.byteSize = sizeof(row); row.id = owner.count;
    row.address = address; row.length = static_cast<std::uint32_t>(length); row.kind = 2;
    std::memcpy(row.beforeBytes, owner.pendingBefore, length); std::memcpy(row.afterBytes, exactBytes, length);
    MEMORY_BASIC_INFORMATION memory{};
    if (VirtualQuery(address, &memory, sizeof(memory)) != sizeof(memory)) return false;
    row.protection = memory.Protect;
    for (const auto& original : owner.originals) if (original.target == address) {
        row.kind = 1; row.original = original.code; row.entry = original.entry; row.epoch = original.epoch;
        row.functionTable = original.block; row.functionTableBase = reinterpret_cast<std::uint64_t>(original.code);
        row.movedByteCount = original.moved; row.originalByteCount = original.length;
        row.functionCount = original.functions; row.unwindByteCount = original.unwindBytes;
        row.lookupChecks = original.lookups; row.boundaryChecks = original.boundaries;
        row.continuation = original.target + original.moved;
    }
    const auto rva = reinterpret_cast<std::uintptr_t>(address) - reinterpret_cast<std::uintptr_t>(owner.module);
    bool applied = false;
    for (auto& range : owner.image) if ((range.flags & IMAGE_SCN_MEM_EXECUTE) && rva >= range.rva && rva - range.rva < range.size
        && length <= range.size - (rva - range.rva)) {
        std::memcpy(range.expected.data() + rva - range.rva, exactBytes, length); applied = true; break;
    }
    owner.pending = nullptr; owner.pendingLength = 0; ++owner.count;
    return applied && ImageCurrent() && OriginalsCurrent();
}
bool Publish()
{
    if (owner.state != 2 || owner.unknown || owner.pending || owner.count != WriteCount || !ImageCurrent() || !OriginalsCurrent()) return false;
    unsigned originals = 0;
    for (const auto& row : owner.writes) if (row.kind == 1) ++originals;
    if (originals != OriginalCount) return false;
    owner.published = true; owner.state = 3; return true;
}
bool OwnedImageCurrent()
{
    if (owner.unknown || !SameFile(owner.file, owner.fileInfo) || !SameFile(owner.dobbyFile, owner.dobbyInfo)) return false;
    const auto* base = reinterpret_cast<const unsigned char*>(owner.module);
    for (const auto& range : owner.image) {
        if (!SameFacts(range.facts)) return false;
        if (!(range.flags & IMAGE_SCN_MEM_EXECUTE) && std::memcmp(base + range.rva, range.expected.data(), range.size) != 0) return false;
    }
    auto body = [&](std::uint32_t rva, std::uint32_t size) {
        for (const auto& range : owner.image)
            if ((range.flags & IMAGE_SCN_MEM_EXECUTE) && rva >= range.rva && rva - range.rva <= range.size && size <= range.size - (rva - range.rva))
                return std::memcmp(base + rva, range.expected.data() + rva - range.rva, size) == 0;
        return false;
    };
    // Publication compared the entire executable file before every Prepare/write.
    // A published member owns these source functions. The common broker must
    // compare all other executable bytes with its pinned file and actual members.
    // No reader accepts exclusions or authorizes another source write.
    if (!body(Profile::UnityRva, Profile::UnityEndRva - Profile::UnityRva) || !body(Profile::RuntimeRva, 0x2af14e - Profile::RuntimeRva)) return false;
    for (const auto& row : Profile::Operations) if (!body(row.rva, row.endRva - row.rva)) return false;
    for (const auto& row : Profile::OperationContinuations) if (!body(row.rva, row.endRva - row.rva)) return false;
    for (const auto& row : owner.writes) if (std::memcmp(row.address, row.afterBytes, row.length) != 0) return false;
    return true;
}
bool Verify()
{ return owner.state == 3 && owner.published && !owner.unknown && owner.count == WriteCount && OwnedImageCurrent() && OriginalsCurrent(); }
void Information(RizFirstInstallPlaybackAttestationV1& info)
{
    info = {}; info.abiVersion = 1; info.byteSize = sizeof(info); info.state = owner.state; info.unknown = owner.unknown ? 1u : 0u;
    info.token = owner.published ? &owner : nullptr; info.epoch = owner.published ? owner.epoch : 0;
    info.module = owner.module; info.helper = owner.helper; info.dobby = owner.dobby; info.imageSize = owner.imageSize;
    info.originalCount = owner.prepared; info.rangeCount = owner.count; info.completedWrites = owner.count; info.supportedOriginals = owner.supported;
    for (const auto& original : owner.originals) { info.lookupChecks += original.lookups; info.boundaryChecks += original.boundaries; }
    std::memcpy(info.fileSha256, Profile::GameSha256, sizeof(info.fileSha256));
}
} // namespace PlaybackAttestation
