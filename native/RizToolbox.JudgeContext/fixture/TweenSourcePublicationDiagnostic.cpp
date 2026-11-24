// Standalone diagnostic TU: compile this file (not Publication.cpp separately)
// with TweenSourceContext.cpp + TweenSourceEntry.asm and RIZ_TWEEN_SOURCE_FIXTURE.
// It invokes the real publisher once, then reads its permanent failed state.
// No production helper, source Commit, game entry or game instruction executes.
#include "../TweenSourcePublication.cpp"
#include <bcrypt.h>
#include <cstdio>
#include <cwchar>
#pragma comment(lib, "bcrypt.lib")

#if !defined(RIZ_TWEEN_SOURCE_FIXTURE)
#error This diagnostic must be built only with RIZ_TWEEN_SOURCE_FIXTURE.
#endif

extern "C" bool RizTweenSourceFixture_Metadata(const RizTweenSourceMetadataV1*) { return false; }

namespace {
struct DiagPinnedImage {
    HANDLE file = INVALID_HANDLE_VALUE;
    ~DiagPinnedImage() { if (file != INVALID_HANDLE_VALUE) CloseHandle(file); }
};
struct DiagHash {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    PUCHAR object = nullptr;
    ~DiagHash() {
        if (hash) BCryptDestroyHash(hash);
        if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
        if (object) HeapFree(GetProcessHeap(), 0, object);
    }
};
bool DiagHashImage(DiagPinnedImage& pin)
{
    const unsigned char expected[]{0x85,0x3d,0x65,0x30,0xd6,0x94,0x07,0x9c,0xf1,0xcd,0x59,0x6b,0x33,0x62,0xc5,0xbc,
        0xb6,0x47,0xda,0x21,0x15,0x41,0xa0,0x08,0xd7,0x46,0x39,0x99,0xbc,0x5b,0x1b,0x79};
    LARGE_INTEGER zero{};
    if (!SetFilePointerEx(pin.file, zero, nullptr, FILE_BEGIN)) return false;
    DiagHash handles;
    ULONG objectLength = 0, written = 0;
    if (BCryptOpenAlgorithmProvider(&handles.algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0
        || BCryptGetProperty(handles.algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectLength),
            sizeof(objectLength), &written, 0) < 0 || written != sizeof(objectLength)
        || !objectLength || objectLength > 1024 * 1024) return false;
    handles.object = static_cast<PUCHAR>(HeapAlloc(GetProcessHeap(), 0, objectLength));
    if (!handles.object || BCryptCreateHash(handles.algorithm, &handles.hash, handles.object, objectLength, nullptr, 0, 0) < 0) return false;
    unsigned char buffer[65536]{};
    for (;;) {
        DWORD count = 0;
        if (!ReadFile(pin.file, buffer, sizeof(buffer), &count, nullptr)) return false;
        if (!count) break;
        if (BCryptHashData(handles.hash, buffer, count, 0) < 0) return false;
    }
    unsigned char actual[32]{};
    return BCryptFinishHash(handles.hash, actual, sizeof(actual), 0) >= 0
        && std::memcmp(actual, expected, sizeof(actual)) == 0;
}
unsigned diagChecks = 0, diagFailures = 0, diagBoundaryFailures = 0;
DWORD diagException = 0;
void DiagCheck(const char* stage, bool result)
{
    ++diagChecks; if (!result) ++diagFailures;
    std::printf("stage=%s passed=%u\n", stage, result ? 1u : 0u);
}
bool DiagPrepare(void* module)
{
    __try { return RizTweenSourcePreparePublication(module); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        diagException = GetExceptionCode();
        std::printf("stage=real-prepare exception=0x%08lx\n", diagException); return false;
    }
}
void DiagDifferences(const CONTEXT& expected, const CONTEXT& actual)
{
    std::printf("  result expectedRip=0x%llx actualRip=0x%llx expectedRsp=0x%llx actualRsp=0x%llx\n",
        static_cast<unsigned long long>(expected.Rip), static_cast<unsigned long long>(actual.Rip),
        static_cast<unsigned long long>(expected.Rsp), static_cast<unsigned long long>(actual.Rsp));
    const char* names[]{"rax","rbx","rcx","rdx","rbp","rsi","rdi","r8","r9","r10","r11","r12","r13","r14","r15"};
    const DWORD64 expectedGpr[]{expected.Rax,expected.Rbx,expected.Rcx,expected.Rdx,expected.Rbp,expected.Rsi,expected.Rdi,
        expected.R8,expected.R9,expected.R10,expected.R11,expected.R12,expected.R13,expected.R14,expected.R15};
    const DWORD64 actualGpr[]{actual.Rax,actual.Rbx,actual.Rcx,actual.Rdx,actual.Rbp,actual.Rsi,actual.Rdi,
        actual.R8,actual.R9,actual.R10,actual.R11,actual.R12,actual.R13,actual.R14,actual.R15};
    for (unsigned i = 0; i < 15; ++i) if (expectedGpr[i] != actualGpr[i])
        std::printf("  gpr=%s expected=0x%llx actual=0x%llx\n", names[i],
            static_cast<unsigned long long>(expectedGpr[i]), static_cast<unsigned long long>(actualGpr[i]));
    if (expected.EFlags != actual.EFlags || expected.MxCsr != actual.MxCsr)
        std::printf("  flags expected=0x%lx actual=0x%lx mxcsrExpected=0x%lx mxcsrActual=0x%lx\n",
            expected.EFlags, actual.EFlags, expected.MxCsr, actual.MxCsr);
    const auto* expectedFpu = reinterpret_cast<const unsigned char*>(&expected.FltSave);
    const auto* actualFpu = reinterpret_cast<const unsigned char*>(&actual.FltSave);
    for (std::size_t i = 0; i < sizeof(expected.FltSave); ++i) if (expectedFpu[i] != actualFpu[i]) {
        std::printf("  firstFpuDifference offset=0x%zx expected=0x%02x actual=0x%02x\n",
            i, static_cast<unsigned>(expectedFpu[i]), static_cast<unsigned>(actualFpu[i])); break;
    }
}
void DiagDescribeBoundary(const Boundary& boundary)
{
    // Deliberately reproduce the production comparison, including the exact
    // original parent's control PC and the same physical synthetic stack.
    alignas(16) DWORD64 stack[256]{};
    for (unsigned i = 0; i < 256; ++i) stack[i] = 0x6100000000000000ULL + static_cast<DWORD64>(i) * 0x10001;
    constexpr unsigned sourceIndex = 16;
    const auto sourceRsp = reinterpret_cast<DWORD64>(stack + sourceIndex);
    const auto& parent = parents[ParentIndex(boundary.site)];
    CONTEXT expected{}; SeedContext(expected, sourceRsp);
    expected.Rip = reinterpret_cast<DWORD64>(state.module + parent.function.BeginAddress + parent.unwind[1]);
    auto actual = expected; auto parentFunction = parent.function;
    PVOID expectedHandler = nullptr, actualHandler = nullptr; DWORD64 expectedFrame = 0, actualFrame = 0;
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, reinterpret_cast<DWORD64>(state.module), expected.Rip,
        &parentFunction, &expected, &expectedHandler, &expectedFrame, nullptr);
    actual.Rip = reinterpret_cast<DWORD64>(state.block + boundary.offset);
    if (boundary.kind == StackKind::Flags) { stack[sourceIndex - 1] = actual.EFlags; actual.Rsp -= 8; }
    if (boundary.kind == StackKind::ObserverCall) { stack[sourceIndex - 1] = reinterpret_cast<DWORD64>(boundary.returnPc); actual.Rsp -= 8; }
    RUNTIME_FUNCTION* function = nullptr;
    if (!Lookup(boundary.offset, function)) { std::printf("  dynamic-lookup-failed\n"); return; }
    std::printf("  function begin=0x%lx end=0x%lx unwind=0x%lx inputRsp=0x%llx opcode=0x%02x\n",
        function->BeginAddress, function->EndAddress, function->UnwindData,
        static_cast<unsigned long long>(actual.Rsp), static_cast<unsigned>(state.block[boundary.offset]));
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, reinterpret_cast<DWORD64>(state.module), actual.Rip,
        function, &actual, &actualHandler, &actualFrame, nullptr);
    if (boundary.kind == StackKind::ObserverCall) {
        std::printf("  tail-step expectedRip=%p actualRip=0x%llx expectedRsp=0x%llx actualRsp=0x%llx\n",
            boundary.returnPc, static_cast<unsigned long long>(actual.Rip),
            static_cast<unsigned long long>(sourceRsp), static_cast<unsigned long long>(actual.Rsp));
        if (actual.Rip != reinterpret_cast<DWORD64>(boundary.returnPc) || actual.Rsp != sourceRsp) {
            DiagDifferences(expected, actual); return;
        }
        const auto offset = reinterpret_cast<const unsigned char*>(boundary.returnPc) - state.block;
        if (offset < 0 || !Lookup(static_cast<std::size_t>(offset), function)) { std::printf("  relay-return-lookup-failed\n"); return; }
        RtlVirtualUnwind(UNW_FLAG_NHANDLER, reinterpret_cast<DWORD64>(state.module), actual.Rip,
            function, &actual, &actualHandler, &actualFrame, nullptr);
    }
    DiagDifferences(expected, actual);
    std::printf("  handlers expected=%p actual=%p frames expected=0x%llx actual=0x%llx\n",
        expectedHandler, actualHandler, static_cast<unsigned long long>(expectedFrame), static_cast<unsigned long long>(actualFrame));
}
bool DiagBoundary(unsigned index)
{
    const auto& boundary = state.boundaries[index];
    bool result = false;
    __try {
        result = BoundaryUnwind(boundary);
        if (!result) {
            std::printf("FAILED boundary index=%u site=%u offset=0x%zx kind=%u returnPc=%p\n",
                index, boundary.site, boundary.offset, static_cast<unsigned>(boundary.kind), boundary.returnPc);
            DiagDescribeBoundary(boundary);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        diagException = GetExceptionCode();
        std::printf("FAILED boundary index=%u site=%u offset=0x%zx kind=%u exception=0x%08lx\n",
            index, boundary.site, boundary.offset, static_cast<unsigned>(boundary.kind), diagException);
    }
    return result;
}
bool DiagPublication()
{
    std::printf("state claimed=%ld prepared=%ld published=%ld unknown=%ld mask=0x%llx allocation=%p sealed=%u registered=%ld configured=%ld ranges=%u boundaries=%u\n",
        AtomicRead(&attemptClaimed), AtomicRead(&prepared), AtomicRead(&published), AtomicRead(&unknown),
        static_cast<unsigned long long>(Mask()), static_cast<void*>(state.block), state.sealed ? 1u : 0u,
        AtomicRead(&tableRegistered), AtomicRead(&contextConfigured), state.rangeCount, state.boundaryCount);
    DiagCheck("image-identity", ImageIdentity()); DiagCheck("profile-identity", ProfileIdentity());
    DiagCheck("destinations-identity", DestinationsIdentity());
    DiagCheck("preserving-entry-memory", Memory(state.entry, 1, nullptr, true));
    bool parentsValid = true;
    for (unsigned i = 0; i < 3; ++i) {
        const auto result = ParentIdentity(i); parentsValid = parentsValid && result;
        std::printf("parent=%u begin=0x%lx end=0x%lx unwind=0x%lx passed=%u\n", i,
            parents[i].function.BeginAddress, parents[i].function.EndAddress, parents[i].function.UnwindData, result ? 1u : 0u);
        ++diagChecks; if (!result) ++diagFailures;
    }
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto result = SourceIdentity(i, false); ++diagChecks; if (!result) ++diagFailures;
        if (!result) std::printf("FAILED original-source site=%u begin=0x%x\n", i, P::Sites[i].beginRva);
    }
    const auto blockValid = BlockIdentity(); DiagCheck("complete-sealed-block", blockValid);
    if (!blockValid) {
        if (state.block) {
            MEMORY_BASIC_INFORMATION memory{};
            const auto queried = VirtualQuery(state.block, &memory, sizeof(memory));
            std::printf("block query=%zu protection=0x%lx state=0x%lx type=0x%lx region=%zu expectedBytesMatch=%u\n",
                queried, memory.Protect, memory.State, memory.Type, memory.RegionSize,
                state.sealed && Memory(state.block, allocationSize) && std::memcmp(state.block, state.expected.data(), allocationSize) == 0 ? 1u : 0u);
        }
        return false;
    }
    if (!parentsValid) return false;
    std::printf("flags-unwind offset=0x%zx size=%zu bytes=", flagsUnwindOffset, updateFlagsUnwind.used);
    for (std::size_t i = 0; i < updateFlagsUnwind.used; ++i)
        std::printf("%02x", static_cast<unsigned>(state.block[flagsUnwindOffset + i]));
    std::printf("\n");
    for (unsigned i = 0; i < state.rangeCount; ++i) {
        const auto& range = state.ranges[i]; RUNTIME_FUNCTION* entry = nullptr;
        const auto result = Lookup(range.begin, entry) && Lookup(range.end - 1, entry);
        ++diagChecks; if (!result) { ++diagFailures; std::printf("FAILED range index=%u site=%u begin=0x%zx end=0x%zx kind=%u\n",
            i, range.site, range.begin, range.end, static_cast<unsigned>(range.kind)); }
    }
    for (unsigned i = 0; i < state.boundaryCount; ++i) {
        ++diagChecks;
        if (!DiagBoundary(i)) { ++diagFailures; ++diagBoundaryFailures; }
    }
    std::printf("boundary-summary total=%u failures=%u\n", state.boundaryCount, diagBoundaryFailures);
    if (AtomicRead(&contextConfigured) == 1) DiagCheck("source-context-identity", ContextIdentity());
    return diagFailures == 0;
}
bool DiagPublicationGuarded()
{
    __try { return DiagPublication(); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        diagException = GetExceptionCode(); ++diagFailures;
        std::printf("stage=inspect-publication exception=0x%08lx\n", diagException); return false;
    }
}
}

int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    if (argc != 2) { std::fprintf(stderr, "Usage: TweenSourcePublicationDiagnostic.exe exact-fixed-GameAssembly.dll\n"); return 2; }
    if (GetModuleHandleW(L"GameAssembly.dll")) { std::fprintf(stderr, "GameAssembly was already mapped before this isolated diagnostic.\n"); return 2; }
    DiagPinnedImage pin;
    pin.file = CreateFileW(argv[1], GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (pin.file == INVALID_HANDLE_VALUE || !DiagHashImage(pin)) { std::fprintf(stderr, "Fixed image could not be pinned and SHA256-verified.\n"); return 2; }
    const auto module = LoadLibraryExW(argv[1], nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (!module) { std::fprintf(stderr, "Read-only fixed PE mapping failed: %lu\n", GetLastError()); return 2; }
    std::printf("mapping=%p mode=DONT_RESOLVE_DLL_REFERENCES sourceWrites=0 gameCodeExecuted=0\n", static_cast<void*>(module));
    std::array<std::array<unsigned char, patchStride>, P::SiteCount> before{};
    std::array<DWORD, P::SiteCount> beforeProtection{};
    const auto* base = reinterpret_cast<const unsigned char*>(module);
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i];
        if (!Memory(base + site.beginRva, site.size, base, true, &beforeProtection[i])) return 2;
        std::memcpy(before[i].data(), base + site.beginRva, site.size);
    }
    const auto prepareSucceeded = DiagPrepare(module);
    DiagCheck("real-prepare-return", prepareSucceeded);
    const auto diagnosticSucceeded = DiagPublicationGuarded();
    bool sourceUnchanged = true;
    for (unsigned i = 0; i < P::SiteCount; ++i) {
        const auto& site = P::Sites[i]; DWORD protection = 0;
        if (!Memory(base + site.beginRva, site.size, base, true, &protection) || protection != beforeProtection[i]
            || std::memcmp(before[i].data(), base + site.beginRva, site.size) != 0) sourceUnchanged = false;
    }
    DiagCheck("source-spans-and-protection-unchanged", sourceUnchanged);
    const auto fileUnchanged = DiagHashImage(pin); DiagCheck("fixed-file-sha256-unchanged", fileUnchanged);
    const auto passed = prepareSucceeded && diagnosticSucceeded && sourceUnchanged && fileUnchanged && diagFailures == 0 && !diagException;
    std::printf("{\"schemaVersion\":1,\"kind\":\"tween-source-publication-prepare-diagnostic\",\"passed\":%s,\"prepareSucceeded\":%s,\"checks\":%u,\"failures\":%u,\"boundaryFailures\":%u,\"exception\":%lu,\"publishedMask\":%llu,\"sourceUnchanged\":%s,\"fixedFileUnchanged\":%s,\"sourceWrites\":0,\"publicationCommitted\":false,\"gameCodeExecuted\":false,\"gameRuntimeVerified\":false}\n",
        passed ? "true" : "false", prepareSucceeded ? "true" : "false", diagChecks, diagFailures, diagBoundaryFailures, diagException,
        static_cast<unsigned long long>(Mask()), sourceUnchanged ? "true" : "false", fileUnchanged ? "true" : "false");
    // Preserve the mapped image, every relay allocation and registered table
    // through process exit, including every failed Prepare result.
    return passed ? 0 : 1;
}
