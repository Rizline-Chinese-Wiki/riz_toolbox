#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include "EngineTransportSourceWrites.h"
#include "EngineTransportGameProfile.h"

#ifdef RIZ_ENGINE_TRANSPORT_OWNED_IMAGE
extern "C" bool __cdecl RizEngineTransportValidateOwnedImage(std::uint64_t target);
#endif
#ifdef RIZ_ENGINE_TRANSPORT_PRODUCTION
#include "EngineTransportProfile.h"
extern "C" bool __cdecl RizEngineTransportValidateProductionAdmission(std::uint64_t target);
#endif

namespace RizEngineTransportSourceWrites {
namespace {
namespace Facts = RizEngineTransportGameProfile;
namespace Relays = RizEngineTransportRelays;
using U8 = std::uint8_t; using U32 = std::uint32_t; using U64 = std::uint64_t;
DWORD SourceType(U32 profile){
#ifdef RIZ_ENGINE_TRANSPORT_OWNED_IMAGE
    (void)profile;return MEM_IMAGE;
#else
    return profile==RizEngineTransportOwnedProfileV5?MEM_PRIVATE:MEM_IMAGE;
#endif
}
bool Fits(U64 at, U64 bytes, U64 total) { return at <= total && bytes <= total - at; }
bool Equal(const void* left, const void* right, U32 bytes) {
    const auto* a = static_cast<const volatile U8*>(left);
    const auto* b = static_cast<const volatile U8*>(right);
    for (U32 i = 0; i < bytes; ++i) if (a[i] != b[i]) return false;
    return true;
}
bool Overlap(U64 a, U64 bytesA, U64 b, U64 bytesB) {
    if (a > ~U64{0} - bytesA || b > ~U64{0} - bytesB) return true;
    return a < b + bytesB && b < a + bytesA;
}
U32 Read32(const U8* bytes) {
    U32 value{}; for (U32 i = 0; i < 4; ++i) value |= static_cast<U32>(bytes[i]) << (8 * i); return value;
}
U64 Read64(const U8* bytes) {
    U64 value{}; for (U32 i = 0; i < 8; ++i) value |= static_cast<U64>(bytes[i]) << (8 * i); return value;
}
U64 Branch(U64 next, U32 encoded) {
    const auto delta = static_cast<std::int32_t>(encoded);
    return delta < 0 ? next - static_cast<U64>(-static_cast<std::int64_t>(delta)) : next + static_cast<U32>(delta);
}
bool Memory(U64 address, U64 bytes, U64 allocation, DWORD type, DWORD protection) {
    if (!address || !bytes || address > ~U64{0} - bytes) return false;
    U64 at = address; const U64 end = address + bytes;
    while (at < end) {
        MEMORY_BASIC_INFORMATION information{};
        if (VirtualQuery(reinterpret_cast<const void*>(at), &information, sizeof(information)) != sizeof(information)
            || information.State != MEM_COMMIT || information.Protect != protection
            || (type && information.Type != type)
            || (allocation && reinterpret_cast<U64>(information.AllocationBase) != allocation)) return false;
        const U64 next = reinterpret_cast<U64>(information.BaseAddress) + information.RegionSize;
        if (next <= at) return false;
        at = next < end ? next : end;
    }
    return true;
}
bool Record(Receipt* output, U32 stage, U32 index, U32 error = 0) {
    if (output->eventCount >= EventCapacity) { output->eventOverflow = 1; return false; }
    auto& event = output->events[output->eventCount++]; event.sequence = output->eventCount;
    event.stage = stage; event.window = index; event.result = output->result; event.lastWin32Error = error;
    if (index < Relays::WindowCount) event.bytesWritten = output->windows[index].sourceBytesWritten;
    return true;
}
Result Fail(Receipt* output, Result result, U32 index, U32 error = 0) {
    if (output->result == Success) { output->result = result; output->failureWindow = index; }
    output->state = Failed;
    if (index < Relays::WindowCount) output->windows[index].lastWin32Error = error;
    (void)Record(output, Failure, index, error); return static_cast<Result>(output->result);
}
bool Fault(const Request& request, U32 stage, U32 index) {
#ifdef RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE
    return request.faultStage == stage && request.faultWindow == index;
#else
    (void)request; (void)stage; (void)index; return false;
#endif
}
U32 WriteBytes(const Request& request, U32 index, U32 total) {
#ifdef RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE
    if (Fault(request, WriteBegin, index)) return request.partialBytes < total ? request.partialBytes : total - 1;
#else
    (void)request; (void)index;
#endif
    return total;
}
U32 Family(U32 typed) { return typed < 4 ? typed : typed < 10 ? typed + 1 : 11; }
bool FullBodies(const Relays::Plan& plan, const Receipt& receipt) {
    const auto* image = reinterpret_cast<const volatile U8*>(plan.fixedImageBase);
    for (U32 f = 0; f < Facts::TypedCount; ++f) {
        const auto& function = Facts::Functions[f];
        for (U32 i = 0; i < function.bodyBytes; ++i) {
            U8 expected = function.body[i]; const U32 rva = function.beginRva + i;
            for (U32 w = 0; w < Relays::WindowCount; ++w) {
                if (!(receipt.committedWindows & (U64{1} << w))) continue;
                const auto& window = Facts::PhysicalWindows[w];
                if (rva >= window.beginRva && rva < window.endRva) { expected = plan.windows[w].patch[rva - window.beginRva]; break; }
            }
            if (image[rva] != expected) return false;
        }
    }
    return true;
}
bool RegisteredRelay(const Relays::Plan& plan, U32 index) {
    const auto& site = plan.sites[index]; DWORD64 actualBase{};
    const auto* actual = RtlLookupFunctionEntry(site.relay, &actualBase, nullptr);
    if (!actual || actualBase != plan.fixedImageBase || reinterpret_cast<U64>(actual) != plan.tableAddress + index * sizeof(RUNTIME_FUNCTION)
        || !Equal(actual, &site.runtimeFunction, sizeof(RUNTIME_FUNCTION))) return false;
    for (U32 which = 0; which < (site.expandedPhysicalBlock ? 3u : 2u); ++which) {
        const U64 pc = which == 0 ? site.relay : which == 1 ? site.relayReturnPc : site.relay + 112;
        actualBase = 0; const auto* found = RtlLookupFunctionEntry(pc, &actualBase, nullptr);
        if (found != actual || actualBase != plan.fixedImageBase) return false;
    }
    const auto& source = Facts::PhysicalWindows[site.windowIndex].runtimeFunction;
    const auto* unwind = reinterpret_cast<const U8*>(site.unwind);
    if (index >= 17 && index <= 20) {
        // An explicit synthetic primary preserves source function identity
        // without importing Dispatch's still-unexecuted late RBX save.
        constexpr U8 partial[12]{0x01,0x00,0x03,0x00,0x00,0x01,0x2a,0x00,0x00,0x70,0x00,0x00};
        return site.unwindBytes == 16 && unwind[0] == 0x21 && unwind[1] == 0 && unwind[2] == 0 && unwind[3] == 0
            && Read32(unwind + 4) == source.beginRva && Read32(unwind + 8) == source.endRva
            && Read32(unwind + 12) == site.toImageStart + Relays::DispatchPrimaryUnwindOffset
            && Equal(reinterpret_cast<const U8*>(site.relay + Relays::DispatchPrimaryUnwindOffset), partial, sizeof(partial));
    }
    return site.unwindBytes == 16 && unwind[0] == 0x21 && unwind[1] == 0 && unwind[2] == 0 && unwind[3] == 0
        && Read32(unwind + 4) == source.beginRva && Read32(unwind + 8) == source.endRva
        && Read32(unwind + 12) == source.unwindRva;
}
Result PreflightAll(const Request& request, Receipt* output) {
    const auto& plan = *request.prepared;
    if (plan.version != Relays::Version || plan.byteSize != sizeof(plan) || plan.result != Relays::Success
        || plan.flags != (Relays::FactsVerified | Relays::NearPrepared) || plan.siteCount != Relays::SiteCount
        || plan.windowCount != Relays::WindowCount || plan.tableCount != Relays::SiteCount
        || plan.usedBytes != Relays::ArenaUsedBytes || plan.nearBytesWritten != Relays::ArenaUsedBytes
        || plan.nearWriteAttempted != 1 || plan.nearCapacity < Relays::ArenaMinimumBytes
        || plan.fixedImageBase > ~U64{0} - Facts::ImageSize || plan.nearRw < plan.fixedImageBase + Facts::ImageSize
        || plan.tableBase != plan.fixedImageBase || plan.tableAddress != plan.nearRw
        || plan.nearRw - plan.fixedImageBase != plan.arenaToImageStart
        || plan.nearRw > ~U64{0} - plan.nearCapacity
        || plan.reserved || plan.nearRw % 16) return Fail(output, BadPlan, NoIndex);
    output->fixedImageBase = plan.fixedImageBase; output->nearBlock = plan.nearRw;
    const DWORD type = SourceType(request.profile);
#ifdef RIZ_ENGINE_TRANSPORT_OWNED_IMAGE
    if(request.profile==RizEngineTransportOwnedProfileV5&&!RizEngineTransportValidateOwnedImage(plan.fixedImageBase))return Fail(output,BadMemory,NoIndex);
#endif
    SYSTEM_INFO system{}; GetSystemInfo(&system); const U64 pageBytes = system.dwPageSize;
    if (!pageBytes || (pageBytes & (pageBytes - 1)) || pageBytes > 0x10000) return Fail(output, BadMemory, NoIndex);
    if (!Memory(plan.nearRw, Relays::ArenaMinimumBytes, 0, MEM_PRIVATE, PAGE_EXECUTE_READ)
        || !Equal(reinterpret_cast<const void*>(plan.nearRw), plan.preparedBytes, Relays::ArenaUsedBytes)) return Fail(output, BadMemory, NoIndex);
    // The Plan and near bytes may agree with each other and still both contain
    // a changed replay. Compare them against a new canonical emission from the
    // pinned facts before the first source write. Preserve source-drift as its
    // own rejection class rather than hiding it behind a plan mismatch.
    if (!FullBodies(plan, *output)) return Fail(output, BadSource, NoIndex);
    if (Relays::VerifyPrepared(&plan) != Relays::Success) return Fail(output, BadPlan, NoIndex);
    for (U32 index = 0; index < Relays::WindowCount; ++index) {
        const auto& source = plan.windows[index]; const auto& fact = Facts::PhysicalWindows[index]; auto& row = output->windows[index];
        if (source.index != index || fact.index != index || source.role != fact.role || source.roleIndex != fact.roleIndex
            || source.byteCount != fact.byteCount || !Fits(fact.beginRva, fact.byteCount, Facts::ImageSize)
            || source.source != plan.fixedImageBase + fact.beginRva || fact.byteCount < 5 || fact.byteCount > 32
            || !Equal(source.expected, fact.bytes, fact.byteCount)) return Fail(output, BadPlan, index);
        for (U32 prior = 0; prior < index; ++prior)
            if (Overlap(source.source, source.byteCount, plan.windows[prior].source, plan.windows[prior].byteCount)) return Fail(output, BadPlan, index);
        if (!Memory(source.source, source.byteCount, plan.fixedImageBase, type, PAGE_EXECUTE_READ)) return Fail(output, BadMemory, index);
        row.protectionBase = source.source & ~(pageBytes - 1);
        row.protectionBytes = ((source.source + source.byteCount - 1) & ~(pageBytes - 1)) + pageBytes - row.protectionBase;
        if (!Memory(row.protectionBase, row.protectionBytes, plan.fixedImageBase, type, PAGE_EXECUTE_READ)) return Fail(output, BadMemory, index);
        if (index < Relays::TypedCount) {
            if (source.patch[0] != 0xff || source.patch[1] != 0x25 || Read32(source.patch + 2) != 0
                || !source.entry || Read64(source.patch + 6) != source.entry || source.byteCount < 14
                || !Memory(source.entry, 1, 0, MEM_IMAGE, PAGE_EXECUTE_READ)) return Fail(output, BadPlan, index);
            for (U32 i = 14; i < source.byteCount; ++i) if (source.patch[i] != 0x90) return Fail(output, BadPlan, index);
        } else {
            const U32 s = index - Relays::TypedCount; const auto& site = plan.sites[s]; const auto& logical = Facts::Sites[s];
            if (site.index != s || site.windowIndex != index || site.relay != plan.nearRw + Relays::SlotOffset + s * Relays::SlotBytes
                || site.family != logical.family || site.phase != logical.familyIndex || site.functionIndex != fact.functionIndex
                || site.logicalSitePc != plan.fixedImageBase + logical.observationRva || site.physicalPatchPc != source.source
                || site.relativeOffset != Relays::SlotOffset + s * Relays::SlotBytes
                || site.toImageStart != plan.arenaToImageStart + site.relativeOffset
                || site.continuation != plan.fixedImageBase + logical.physicalContinuationRva
                || site.expandedPhysicalBlock != (logical.expandedPhysicalBlock ? 1u : 0u)
                || site.codeBytes < 6 || site.codeBytes > Relays::LiteralOffset
                || site.literal != site.relay + Relays::LiteralOffset || site.unwind != site.relay + Relays::UnwindOffset
                || site.relayReturnPc < site.relay + 6 || site.relayReturnPc >= site.relay + site.codeBytes
                || site.runtimeFunction.beginRva != site.toImageStart || site.runtimeFunction.endRva != site.toImageStart + site.codeBytes
                || site.runtimeFunction.unwindRva != site.toImageStart + Relays::UnwindOffset
                || source.relay != site.relay || source.entry != site.probeEntry || source.patch[0] != 0xe9
                || Branch(source.source + 5, Read32(source.patch + 1)) != site.relay
                || !Memory(site.probeEntry, 1, 0, MEM_IMAGE, PAGE_EXECUTE_READ)) return Fail(output, BadPlan, index);
            const auto* call = reinterpret_cast<const U8*>(site.relayReturnPc - 6);
            if (call[0] != 0xff || call[1] != 0x15 || Branch(site.relayReturnPc, Read32(call + 2)) != site.literal
                || Read64(reinterpret_cast<const U8*>(site.literal)) != site.probeEntry) return Fail(output, BadPlan, index);
            if (logical.expandedPhysicalBlock) {
                const auto& bypass = Facts::ConditionalBypasses[s == 9 ? 0 : s == 11 ? 1 : 2];
                if (site.alternateContinuation != plan.fixedImageBase + bypass.zeroContinuationRva) return Fail(output, BadPlan, index);
            } else if (site.alternateContinuation) return Fail(output, BadPlan, index);
            for (U32 i = 5; i < source.byteCount; ++i) if (source.patch[i] != 0x90) return Fail(output, BadPlan, index);
            if (!RegisteredRelay(plan, s)) return Fail(output, BadRuntimeTable, index);
            row.siteIndex = s; row.relay = site.relay; row.relayReturnPc = site.relayReturnPc;
            row.continuation = site.continuation; row.alternateContinuation = site.alternateContinuation;
            row.probeEntry = site.probeEntry; row.literalAddress = site.literal; row.literalValue = Read64(reinterpret_cast<const U8*>(site.literal));
            row.literalBytes = 8; row.relayBytes = site.codeBytes; row.relayRfCount = 1;
            row.logicalRva = Facts::Sites[s].observationRva; row.logicalBytes = Facts::Sites[s].logicalSpanBytes;
            if (site.expandedPhysicalBlock) row.flags |= RizEngineTransportWindowExpandedV5 | RizEngineTransportWindowConditionalBypassV5;
        }
        row.version = 5; row.byteSize = sizeof(row); row.index = index; row.role = source.role;
        row.typedIndex = fact.functionIndex; row.familyIndex = index < 13 ? Family(index) : index < 16 ? 4 : index < 18 ? 9 : 11;
        if (index < 13) { row.siteIndex = NoIndex; row.logicalRva = fact.beginRva; row.logicalBytes = fact.byteCount; }
        row.moduleIndex = 2; row.sourceRfIndex = NoIndex; row.resourceIndex = index < 13 ? NoIndex : 0;
        row.physicalRva = fact.beginRva; row.physicalBytes = fact.byteCount; row.source = source.source; row.patchTarget = index < 13 ? source.entry : source.relay;
        row.originalProtection = PAGE_EXECUTE_READ; row.observedProtection = PAGE_EXECUTE_READ;
        __movsb(row.expectedBytes, source.expected, source.byteCount); __movsb(row.patchBytes, source.patch, source.byteCount);
        row.flags |= RizEngineTransportWindowPreparedV5; output->preparedWindows |= U64{1} << index;
    }
    if (!FullBodies(plan, *output)) return Fail(output, BadSource, NoIndex);
    if (!Record(output, Preflight, NoIndex)) return Fail(output, EventOverflow, NoIndex);
    return Success;
}
bool Restore(Receipt* output, U32 index) {
    auto& row = output->windows[index]; if (!row.protectionOutstanding) return true;
    ++row.restoreAttempts; DWORD prior{}; SetLastError(ERROR_SUCCESS);
    if (!VirtualProtect(reinterpret_cast<void*>(row.source), row.physicalBytes, row.originalProtection, &prior)) {
        row.lastWin32Error = GetLastError(); return false;
    }
    if (!Memory(row.source, row.physicalBytes, output->fixedImageBase,
        SourceType(output->profile), row.originalProtection)) return false;
    ++row.restoreSucceeded; row.protectionOutstanding = 0; --output->protectionOutstanding;
    output->outstandingWindows &= ~(U64{1} << index); row.observedProtection = row.originalProtection;
    row.flags |= RizEngineTransportWindowProtectionRestoredV5; return Record(output, Restored, index);
}
Result Abort(Receipt* output, Result result, U32 index, DWORD error = 0) {
    // Restoration is attempted even when a later check or injected failure
    // rejected the write. Committed bytes are never restored to old code here.
    if (index < Relays::WindowCount && output->windows[index].protectionOutstanding && !Restore(output, index))
        return Fail(output, RestoreFailed, index, output->windows[index].lastWin32Error);
    return Fail(output, result, index, error);
}
Result One(const Request& request, Receipt* output, U32 index) {
    const auto& plan = *request.prepared; auto& row = output->windows[index];
    if (!FullBodies(plan, *output)) return Fail(output, BadSource, index);
    if (!Record(output, ProtectBegin, index)) return Fail(output, EventOverflow, index);
    ++row.protectAttempts; DWORD prior{}; SetLastError(ERROR_SUCCESS);
    if (Fault(request, ProtectBegin, index) || !VirtualProtect(reinterpret_cast<void*>(row.source), row.physicalBytes, PAGE_EXECUTE_READWRITE, &prior))
        return Fail(output, ProtectFailed, index, GetLastError());
    ++row.protectSucceeded; row.protectionOutstanding = 1; ++output->protectionOutstanding;
    output->outstandingWindows |= U64{1} << index; row.observedProtection = PAGE_EXECUTE_READWRITE;
    if (!Record(output, Protected, index)) return Abort(output, EventOverflow, index);
    if (prior != row.originalProtection || Fault(request, Protected, index)) return Abort(output, ProtectFailed, index);
    if (!Record(output, WriteBegin, index)) return Abort(output, EventOverflow, index);
    ++row.sourceWriteAttempts; ++output->sourceWriteAttempts; row.flags |= RizEngineTransportWindowWriteAttemptedV5;
    output->attemptedWindows |= U64{1} << index; row.writeSequence = output->eventCount;
    const U32 bytes = WriteBytes(request, index, row.physicalBytes); auto* address = reinterpret_cast<volatile U8*>(row.source);
    for (U32 b = 0; b < bytes; ++b) { address[b] = row.patchBytes[b]; ++row.sourceBytesWritten; }
    if (!Record(output, Written, index)) return Abort(output, EventOverflow, index);
    if (bytes != row.physicalBytes) return Abort(output, WriteFailed, index);
    row.flags |= RizEngineTransportWindowWrittenV5; ++row.readbackAttempts; row.readbackBytes = row.physicalBytes;
#ifdef RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE
    if (Fault(request, Readback, index)) address[0] ^= 1;
#endif
    __movsb(row.observedBytes, reinterpret_cast<const U8*>(row.source), row.physicalBytes);
    if (!Equal(row.patchBytes, row.observedBytes, row.physicalBytes)) return Abort(output, ReadbackFailed, index);
    ++row.readbackVerified; row.flags |= RizEngineTransportWindowReadbackV5;
    if (!Record(output, Readback, index)) return Abort(output, EventOverflow, index);
    row.readbackSequence = output->eventCount;
    if (Fault(request, Restored, index)) return Abort(output, RestoreFailed, index, ERROR_ACCESS_DENIED);
    if (!Restore(output, index)) return Fail(output, RestoreFailed, index, row.lastWin32Error);
    ++row.flushAttempts; SetLastError(ERROR_SUCCESS);
    if (Fault(request, Flushed, index) || !FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(row.source), row.physicalBytes))
        return Fail(output, FlushFailed, index, GetLastError());
    ++row.flushSucceeded; row.flags |= RizEngineTransportWindowFlushedV5;
    if (!Record(output, Flushed, index)) return Fail(output, EventOverflow, index);
    output->committedWindows |= U64{1} << index; ++output->sourceWriteCompletions;
    if (!Record(output, Committed, index)) return Fail(output, EventOverflow, index);
    row.commitSequence = output->eventCount; return Success;
}
Result Core(const Request& request, Receipt* output) {
    output->version = Version; output->byteSize = sizeof(*output); output->state = Validating;
    output->profile = request.profile; output->windowCount = Relays::WindowCount; output->failureWindow = NoIndex;
    if (request.version != Version || request.byteSize != sizeof(Request) || !request.prepared || request.reserved
        || (request.profile != 1 && request.profile != 2)) return Fail(output, InvalidInput, NoIndex);
#ifdef RIZ_ENGINE_TRANSPORT_PRODUCTION
    if constexpr (!RizEngineTransportProfile::ProductionFirstInstallAuthorized) {
        return Fail(output, AdmissionUnproven, NoIndex);
    } else if (request.profile != 2 || !RizEngineTransportValidateProductionAdmission(request.prepared->fixedImageBase)) {
        return Fail(output, AdmissionUnproven, NoIndex);
    }
#else
    if (request.profile == 2) return Fail(output, AdmissionUnproven, NoIndex);
#endif
    const auto result = PreflightAll(request, output); if (result != Success) return result;
    output->state = Publishing;
    for (U32 index = 0; index < Relays::WindowCount; ++index) {
        const auto current = One(request, output, index); if (current != Success) return current;
    }
    if (output->committedWindows != RizEngineTransportPublicationLayout::AllWindows || !FullBodies(*request.prepared, *output))
        return Fail(output, BadSource, NoIndex);
    output->state = Complete; return Success;
}
}
Result Commit(const Request* request, Receipt* output) {
    const DWORD incoming = GetLastError(); Result result = InvalidInput; bool started = false;
    if (!request || !output) return result;
    __try {
        Request copied; __movsb(reinterpret_cast<U8*>(&copied), reinterpret_cast<const U8*>(request), sizeof(copied));
        if (Overlap(reinterpret_cast<U64>(request), sizeof(*request), reinterpret_cast<U64>(output), sizeof(*output))) __leave;
        if (copied.prepared) {
            const auto& plan = *copied.prepared; const U64 destination = reinterpret_cast<U64>(output);
            if (Overlap(destination, sizeof(*output), reinterpret_cast<U64>(copied.prepared), sizeof(plan))
                || Overlap(destination, sizeof(*output), plan.fixedImageBase, Facts::ImageSize)
                || Overlap(destination, sizeof(*output), plan.nearRw, plan.nearCapacity)) __leave;
        }
        // A completed/failed receipt is terminal. A second call cannot erase its
        // retained source/protection history or reuse it to retry publication.
        const auto* original = reinterpret_cast<const U8*>(output);
        for (U32 i = 0; i < sizeof(*output); ++i) if (original[i]) __leave;
        started = true; result = Core(copied, output);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        if (started) {
            __try {
                for (U32 i = 0; i < Relays::WindowCount; ++i)
                    if (output->windows[i].protectionOutstanding) (void)Restore(output, i);
                result = Fail(output, NativeException, NoIndex);
            } __except (EXCEPTION_EXECUTE_HANDLER) { result = NativeException; }
        } else result = NativeException;
    }
    SetLastError(incoming); return result;
}
}
