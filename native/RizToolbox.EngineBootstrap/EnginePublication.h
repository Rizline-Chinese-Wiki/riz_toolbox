#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <cstddef>
#include <cstdint>

// A production publication record is distinct from Kernel transport ABI2,
// scalar record1, and the old owned publisher2/early wire3. No caller supplies
// a target, module, path, profile, entry or executable buffer to this owner.
enum RizEnginePublicationStateV4 : std::uint32_t {
    RizEngineNever, RizEngineValidating, RizEngineConfiguring, RizEnginePublishing,
    RizEnginePublicationReady, RizEngineDisabledNoSourceWrite, RizEnginePublicationUnknown
};
enum RizEnginePublicationProfileV4 : std::uint32_t {
    RizEngineOwnedProfile=1, RizEngineFixedGameCandidate=2
};
enum RizEnginePublicationFailureV4 : std::uint32_t {
    RizEngineNoFailure, RizEngineDynamicLoad, RizEngineRepeatedAttach,
    RizEngineBadImage, RizEngineBadHash, RizEngineBadDependency,
    RizEngineBadSource, RizEngineBadUnwind, RizEngineBadKernel,
    RizEngineConfigureFailed, RizEngineProtectFailed, RizEngineWriteFailed,
    RizEngineRestoreFailed, RizEngineFlushFailed, RizEngineBadReadback,
    RizEngineNativeException, RizEngineAdmissionUnproven, RizEngineEventOverflow,
    RizEngineRepeatedVerify, RizEngineInvalidCaller
};
struct RizEngineModuleV4 {
    std::uint32_t kind,machine,imageSize,timeDateStamp;
    std::uint64_t module,fileBytes,fileIndex;
    std::uint32_t volumeSerial,flags;
    unsigned char sha256[32];
    std::uint64_t heldFile;
    std::uint32_t entryRva,exceptionRva;
};
struct RizEngineRowV4 {
    std::uint32_t version,byteSize,kind,prefixBytes;
    std::uint64_t module,target,original,continuation,entry,epoch;
    std::uint32_t rva,originalProtection,observedProtection,flags;
    std::uint32_t protectAttempts,protectSucceeded,sourceWriteAttempts,sourceBytesWritten;
    std::uint32_t readbackAttempts,readbackVerified,restoreAttempts,restoreSucceeded;
    std::uint32_t flushAttempts,flushSucceeded,protectionOutstanding,lastWin32Error;
    std::uint64_t helperEpoch;
    std::uint32_t helperSlot,reserved;
    unsigned char originalBytes[32],patchBytes[32],observedBytes[32],moduleSha256[32];
    unsigned char bodySha256[32];
    std::uint32_t runtimeFunctionCount,reservedTail[3];
};
struct RizEngineEventV4 {
    std::uint64_t sequence;
    std::uint32_t stage,kind,state,failure,win32Error,reserved;
};
struct RizEnginePublicationV4 {
    std::uint32_t version,byteSize,state,failure;
    std::uint64_t epoch;
    std::uint32_t ownerThread,processId,attachReason,startupStatic,claimed,profile;
    std::uint32_t configuredMask,boundMask,committedMask,sourceWriteAttempts;
    std::uint32_t sourceWriteCompletions,protectionOutstanding,eventCount,overflow;
    std::uint64_t reservedPointer,bootstrapModule,kernelModule,targetModule,executableModule;
    std::uint32_t admissionFlags,verifyFailures;
    RizEngineModuleV4 modules[4]; // bootstrap, Kernel, fixed target, executable
    RizEngineRowV4 rows[4];
    RizEngineEventV4 events[64];
    unsigned char reserved[256];
};
#ifdef RIZ_ENGINE_BOOTSTRAP_BUILD
#define RIZ_ENGINE_PUBLICATION_API extern "C" __declspec(dllexport)
#else
#define RIZ_ENGINE_PUBLICATION_API extern "C" __declspec(dllimport)
#endif
RIZ_ENGINE_PUBLICATION_API std::uint32_t __cdecl RizEngineFirstInstall_GetVersion();
// Caller version4/size4096. Nonwaiting copy: 0 success, 1 invalid, 2 busy.
RIZ_ENGINE_PUBLICATION_API std::uint32_t __cdecl RizEngineFirstInstall_GetInfoV4(RizEnginePublicationV4* output);
// Revalidate current fixed source, continuation and Kernel identity. Read-only
// with respect to code/configuration; a failed check permanently records Unknown.
RIZ_ENGINE_PUBLICATION_API std::uint32_t __cdecl RizEngineFirstInstall_VerifyV4();
// A dependent proxy calls this on a later failed initialization. Publication
// responsibility cannot be abandoned by returning FALSE and unloading owners.
RIZ_ENGINE_PUBLICATION_API void __cdecl RizEngineFirstInstall_AbortAfterPublication();
static_assert(sizeof(RizEngineModuleV4)==96);
static_assert(sizeof(RizEngineRowV4)==320);
static_assert(offsetof(RizEngineRowV4,bodySha256)==272);
static_assert(sizeof(RizEngineEventV4)==32);
static_assert(offsetof(RizEnginePublicationV4,modules)==128);
static_assert(offsetof(RizEnginePublicationV4,rows)==512);
static_assert(offsetof(RizEnginePublicationV4,events)==1792);
static_assert(sizeof(RizEnginePublicationV4)==4096);
