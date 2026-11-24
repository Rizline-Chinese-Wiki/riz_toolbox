#pragma once
#include <cstddef>
#include <cstdint>
#include "EngineInlineProbe.h"
#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZEC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZEC_EXPORT extern "C"
#endif

// Fixed Windows x64 native UnityPlayer ABIs, without an IL2CPP MethodInfo.
// Start:   uint8(owner, managedEnumerator, nameArgument, nativeNodeOutputSlot)
// Execute: uint32(node, executionExceptionByteOutputSlot)
// Release: uint8(node)
// Detach:  void(node)
// Start/Release preserve the raw AL byte. Execute preserves the complete raw
// EAX word, required by its actual scheduler caller's EAX==1 comparison. No
// return is bool-normalized; complete RAX preservation is not the contract.
enum RizEngineNodeKindV1 : std::uint32_t {
    RizEngineNodeStart = 0, RizEngineNodeExecute = 1,
    RizEngineNodeRelease = 2, RizEngineNodeDetach = 3
};
enum RizEngineNodeSnapshotFlagsV1 : std::uint32_t {
    RizEngineNodeSnapshotRequested = 1, RizEngineNodeSnapshotValid = 2,
    RizEngineNodeSnapshotFaulted = 4
};
enum RizEngineNodeExitFlagsV1 : std::uint32_t {
    RizEngineNodeReturned = 1, RizEngineNodeAbnormal = 2,
    // Historical flag name/layout retained. Bit 8 validates returnByte with
    // kind-specific width: uint32 Execute, uint8 Start/Release; never Detach.
    RizEngineNodeEnterFaulted = 4, RizEngineNodeReturnByteValid = 8
};
struct RizEngineNodeFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, snapshotFlags;
    void* arg0;
    void* arg1;
    void* arg2;
    void* arg3;
    const void* caller;
    std::uint64_t sequence;
    void* node;
    void* owner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType;
    std::int32_t referenceCount;
    std::uint32_t reserved0, reserved1;
};
struct RizEngineNodeCompletionV1 {
    // Historical field name; raw uint32 EAX for Execute, raw AL for Start/Release.
    std::uint32_t abiVersion, byteSize, flags, returnByte;
    void* startOutputNode;
    std::uint32_t outputFlags, reserved;
};
using RizEngineNodeEnterV1 = std::uintptr_t(__cdecl*)(const RizEngineNodeFrameV1*, void* user);
using RizEngineNodeExitV1 = void(__cdecl*)(const RizEngineNodeFrameV1*, std::uintptr_t token,
    const RizEngineNodeCompletionV1*, void* user);
struct RizEngineNodeConfigV1 {
    std::uint32_t abiVersion, byteSize;
    void* target;
    void* original;
};
struct RizEngineNodeObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizEngineNodeEnterV1 enter;
    RizEngineNodeExitV1 exit;
    void* user;
};
struct RizEngineNodeInfoV1 {
    std::uint32_t abiVersion, byteSize, kind, slot, attached, unknown;
    void* target;
    void* original;
    void* entry;
    std::uint64_t epoch, entered, exited, inFlight, enterFaults, exitFaults;
    std::uint64_t snapshotFaults, outputFaults;
};

// Four independent tables of 32 permanent slots. Configure only copies one
// immutable target/original descriptor: it never patches target instructions.
// A separately established publication contract must publish the typed entry.
// Target/original, helper, attached callbacks and user roots live until exit.
// No detach, replacement, observer removal, cancellation receipt or root release.
// Transport ABI 2 corrects Execute's complete uint32 EAX return. Scalar record
// headers/layouts remain version 1. Old byte-Execute helpers lack this export
// and must not be adopted by a transport ABI 2 managed bridge.
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetAbiVersion();
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetLimit();
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetFrameSize();
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetCompletionSize();
RIZEC_EXPORT void* __cdecl RizEngineNodeContext_GetEntry(std::uint32_t kind, std::uint32_t slot);
// Configure: 0 ready; 1 invalid; 2 already permanently claimed; 3 epoch
// exhaustion, permanently Unknown and claimed. No slot is ever reusable.
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_Configure(std::uint32_t kind, std::uint32_t slot,
    const RizEngineNodeConfigV1* config);
// Attach: 0 attached; 1 invalid; 2 not configured; 3 epoch mismatch;
// 4 already attempted; 5 sticky Unknown. Before Attach entries transparently
// forward without callbacks. Entry snapshots the nullable observer exactly once
// before sequence/frame creation, so an earlier call never borrows a later one.
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_Attach(std::uint32_t kind, std::uint32_t slot,
    std::uint64_t epoch, const RizEngineNodeObserverV1* observer);
// GetInfo: 0 configured; 1 invalid header/kind/slot; 2 unconfigured;
// 3 incomplete/permanently failed claim. Input identity is never trusted;
// real kind, slot, entry and counters are copied also for 2/3.
RIZEC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInfo(std::uint32_t kind, std::uint32_t slot,
    RizEngineNodeInfoV1* information);

// Start has no Before node and all snapshot scalars are zero. Its four actual
// input pointers are preserved even if null; only its original validates them.
// Execute/Release/Detach copy native node +58/+10/+18/+20/+60 before callbacks,
// with Requested|Valid on success. On read fault node retains actual arg0,
// other snapshot fields stay zero, flags Requested|Faulted, Unknown is sticky,
// and original/Exit still execute. referenceCount is not an execution count;
// enumHandle is a script handle, not an IEnumerator pointer.
// Only a normal Start reads its actual R9 output slot, once under SEH, and
// records Requested|Valid or Requested|Faulted in completion.outputFlags.
// It never dereferences that output node and the slot value alone proves no
// publication or lifetime. No kind reads its node after the original: Release,
// Detach and Execute can free it. Original exceptions unwind natively through
// __finally, never through a CLR callback; Exit receives Abnormal, no result.
// Original abnormal completion also retains sticky Unknown, including before
// observer attachment. Observer exceptions are contained and retain Unknown.
// Native Before
// read faults are reported even if no observer has yet been attached.

static_assert(sizeof(void*) == 8);
static_assert(sizeof(RizEngineNodeFrameV1) == 112);
static_assert(offsetof(RizEngineNodeFrameV1, arg0) == 16);
static_assert(offsetof(RizEngineNodeFrameV1, arg1) == 24);
static_assert(offsetof(RizEngineNodeFrameV1, arg2) == 32);
static_assert(offsetof(RizEngineNodeFrameV1, arg3) == 40);
static_assert(offsetof(RizEngineNodeFrameV1, caller) == 48);
static_assert(offsetof(RizEngineNodeFrameV1, sequence) == 56);
static_assert(offsetof(RizEngineNodeFrameV1, node) == 64);
static_assert(offsetof(RizEngineNodeFrameV1, owner) == 72);
static_assert(offsetof(RizEngineNodeFrameV1, enumHandle) == 80);
static_assert(offsetof(RizEngineNodeFrameV1, enumCache) == 88);
static_assert(offsetof(RizEngineNodeFrameV1, enumType) == 96);
static_assert(offsetof(RizEngineNodeFrameV1, referenceCount) == 100);
static_assert(sizeof(RizEngineNodeCompletionV1) == 32);
static_assert(offsetof(RizEngineNodeCompletionV1, startOutputNode) == 16);
static_assert(offsetof(RizEngineNodeCompletionV1, outputFlags) == 24);
static_assert(sizeof(RizEngineNodeConfigV1) == 24);
static_assert(sizeof(RizEngineNodeObserverV1) == 32);
static_assert(sizeof(RizEngineNodeInfoV1) == 112);
static_assert(offsetof(RizEngineNodeInfoV1, target) == 24);
static_assert(offsetof(RizEngineNodeInfoV1, original) == 32);
static_assert(offsetof(RizEngineNodeInfoV1, entry) == 40);
static_assert(offsetof(RizEngineNodeInfoV1, epoch) == 48);
static_assert(offsetof(RizEngineNodeInfoV1, snapshotFaults) == 96);
static_assert(offsetof(RizEngineNodeInfoV1, outputFaults) == 104);
