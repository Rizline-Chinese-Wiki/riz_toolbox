#pragma once
#include "EngineTransportRelays.h"
#include "EngineTransportPublication.h"

#if defined(RIZ_ENGINE_TRANSPORT_OWNED_IMAGE) && (RIZ_ENGINE_TRANSPORT_OWNED_IMAGE != 1 || !defined(RIZ_ENGINE_TRANSPORT_OWNED) || RIZ_ENGINE_TRANSPORT_OWNED != 1 || defined(RIZ_ENGINE_TRANSPORT_PRODUCTION))
#error The MEM_IMAGE owned path is restricted to the separately compiled V5 owned publisher.
#endif

// Private implementation used by the V5 publisher. There is no exported setter
// or permission here. Complete means all source-write obligations completed;
// it does not mean a publication or any Kernel family is Ready. The eventual
// owner must additionally establish all module, import, Configure and startup
// identities and retain the registered relay arena before calling this code.
namespace RizEngineTransportSourceWrites {
inline constexpr std::uint32_t Version = 1;
enum Result : std::uint32_t {
    Success = 0, InvalidInput, AdmissionUnproven, BadPlan, BadSource,
    BadMemory, BadRuntimeTable, ProtectFailed, WriteFailed, ReadbackFailed,
    RestoreFailed, FlushFailed, NativeException, EventOverflow
};
enum State : std::uint32_t { Never = 0, Validating, Publishing, Complete, Failed };
enum Stage : std::uint32_t {
    Preflight = 1, ProtectBegin, Protected, WriteBegin, Written, Readback,
    Restored, Flushed, Committed, Failure
};
inline constexpr std::uint32_t EventCapacity = 512, NoIndex = 0xffffffffu;
struct Request {
    std::uint32_t version, byteSize, profile, reserved;
    const RizEngineTransportRelays::Plan* prepared;
#ifdef RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE
    // Only compiled into the independent owned fixture, never the publisher.
    std::uint32_t faultStage, faultWindow, partialBytes, reservedFixture;
#endif
};
struct Event {
    std::uint32_t sequence, stage, window, result;
    std::uint32_t bytesWritten, lastWin32Error, reserved[2];
};
struct Receipt {
    std::uint32_t version, byteSize, state, result;
    std::uint32_t profile, windowCount, failureWindow, eventCount;
    std::uint32_t eventOverflow, sourceWriteAttempts, sourceWriteCompletions, protectionOutstanding;
    std::uint64_t preparedWindows, attemptedWindows, committedWindows, outstandingWindows;
    std::uint64_t fixedImageBase, nearBlock;
    RizEngineTransportWindowV5 windows[RizEngineTransportRelays::WindowCount];
    Event events[EventCapacity];
};
// Requires a fresh, zeroed receipt. All windows are prepared and checked before
// any source protection change. Source mappings are MEM_IMAGE for production,
// MEM_PRIVATE only for the default profile 1 fixture. A separate compile-owned
// image publisher verifies the target's actual held file/marker before its
// profile 1 MEM_IMAGE path; there is no runtime selector. Production stays shut.
// Relay code must already be RX and its exact function table actually registered.
// Failure retains every byte/protection obligation; no source rollback or arena
// removal occurs here. The caller applies its own termination/retention policy.
Result Commit(const Request* request, Receipt* receipt);
}
