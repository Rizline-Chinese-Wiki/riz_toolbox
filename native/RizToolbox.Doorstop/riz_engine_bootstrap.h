#ifndef RIZ_ENGINE_DOORSTOP_BOOTSTRAP_H
#define RIZ_ENGINE_DOORSTOP_BOOTSTRAP_H
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

// This file is the C view of the existing V4 wire layout. It deliberately
// does not include C++ publisher headers or expose configuration/patch APIs.
typedef struct RizDoorstopEngineModuleV4 {
    uint32_t kind,machine,imageSize,timeDateStamp;
    uint64_t module,fileBytes,fileIndex;
    uint32_t volumeSerial,flags;
    unsigned char sha256[32];
    uint64_t heldFile;
    uint32_t entryRva,exceptionRva;
} RizDoorstopEngineModuleV4;
typedef struct RizDoorstopEngineRowV4 {
    uint32_t version,byteSize,kind,prefixBytes;
    uint64_t module,target,original,continuation,entry,epoch;
    uint32_t rva,originalProtection,observedProtection,flags;
    uint32_t protectAttempts,protectSucceeded,sourceWriteAttempts,sourceBytesWritten;
    uint32_t readbackAttempts,readbackVerified,restoreAttempts,restoreSucceeded;
    uint32_t flushAttempts,flushSucceeded,protectionOutstanding,lastWin32Error;
    uint64_t helperEpoch;
    uint32_t helperSlot,reserved;
    unsigned char originalBytes[32],patchBytes[32],observedBytes[32],moduleSha256[32];
    unsigned char bodySha256[32];
    uint32_t runtimeFunctionCount,reservedTail[3];
} RizDoorstopEngineRowV4;
typedef struct RizDoorstopEngineEventV4 {
    uint64_t sequence;
    uint32_t stage,kind,state,failure,win32Error,reserved;
} RizDoorstopEngineEventV4;
typedef struct RizDoorstopEnginePublicationV4 {
    uint32_t version,byteSize,state,failure;
    uint64_t epoch;
    uint32_t ownerThread,processId,attachReason,startupStatic,claimed,profile;
    uint32_t configuredMask,boundMask,committedMask,sourceWriteAttempts;
    uint32_t sourceWriteCompletions,protectionOutstanding,eventCount,overflow;
    uint64_t reservedPointer,bootstrapModule,kernelModule,targetModule,executableModule;
    uint32_t admissionFlags,verifyFailures;
    RizDoorstopEngineModuleV4 modules[4];
    RizDoorstopEngineRowV4 rows[4];
    RizDoorstopEngineEventV4 events[64];
    unsigned char reserved[256];
} RizDoorstopEnginePublicationV4;

// Exactly the four existing Bootstrap ABI names. Direct dllimport calls below
// create ordinary IAT entries; there is no dynamic or delayed dependency path.
__declspec(dllimport) uint32_t __cdecl RizEngineFirstInstall_GetVersion(void);
__declspec(dllimport) uint32_t __cdecl RizEngineFirstInstall_GetInfoV4(RizDoorstopEnginePublicationV4* output);
__declspec(dllimport) uint32_t __cdecl RizEngineFirstInstall_VerifyV4(void);
__declspec(dllimport) void __cdecl RizEngineFirstInstall_AbortAfterPublication(void);

enum RizEngineDoorstopStageV1 {
    RizEngineDoorstopFresh, RizEngineDoorstopEntered, RizEngineDoorstopSnapshot,
    RizEngineDoorstopVerified, RizEngineDoorstopDisabled, RizEngineDoorstopBody,
    RizEngineDoorstopComplete, RizEngineDoorstopRejected, RizEngineDoorstopAborting,
    RizEngineDoorstopAborted, RizEngineDoorstopDetaching, RizEngineDoorstopFatal
};
enum RizEngineDoorstopFailureV1 {
    RizEngineDoorstopNoFailure, RizEngineDoorstopRepeatedAttach,
    RizEngineDoorstopBadVersion, RizEngineDoorstopUnreadable,
    RizEngineDoorstopBadPublication, RizEngineDoorstopVerifyFailed,
    RizEngineDoorstopChangedPublication, RizEngineDoorstopLastErrorChanged,
    RizEngineDoorstopPinFailed, RizEngineDoorstopBodyFailed,
    RizEngineDoorstopNativeException, RizEngineDoorstopDynamicDetach,
    RizEngineDoorstopProxyUnavailable, RizEngineDoorstopIatFailed
};
enum RizEngineDoorstopAdmissionV1 {
    RizEngineDoorstopNotAdmitted, RizEngineDoorstopReadyAdmission,
    RizEngineDoorstopNoWriteAdmission
};
typedef struct RizEngineDoorstopRecordV1 {
    uint32_t version,byteSize,stage,attempts;
    uint32_t threadId,processId,attachReason,startupStatic;
    uint32_t entryLastError,versionLastError,infoLastError,verifyLastError;
    uint32_t afterInfoLastError,exitLastError,nativeVersion,infoResult;
    uint32_t verifyResult,afterInfoResult,admission,result;
    uint32_t failure,abortAttempts,abortLastError,abortInfoResult;
    uint32_t abortInfoLastError,bodyLastError,exceptionCode,detachAttempts;
    uint32_t retainedResponsibility,selfPinned,rejected,processTermination;
    uint32_t verifyAttempts,infoReads;
    uint32_t proxyIatAttempts,proxyIatResponsibility;
    uint64_t selfModule,reservedPointer;
    RizDoorstopEnginePublicationV4 before,after,terminal;
} RizEngineDoorstopRecordV1;

// Internal public-link/MAP symbols, never additional DLL exports. Readers
// require the exact held proxy identity and its generated MAP before reading.
extern RizEngineDoorstopRecordV1 RizEngineDoorstopRecord;
BOOL __cdecl RizEngineDoorstopBeforeAttach(HINSTANCE module,DWORD reason,LPVOID reserved);
BOOL __cdecl RizEngineDoorstopBeforeIat(void);
BOOL __cdecl RizEngineDoorstopAfterAttach(BOOL bodyResult,DWORD incomingLastError);
BOOL __cdecl RizEngineDoorstopOnFailure(uint32_t failure,DWORD incomingLastError);
void __cdecl RizEngineDoorstopOnDetach(LPVOID reserved);

typedef char RizDoorstopModuleV4Size[(sizeof(RizDoorstopEngineModuleV4)==96)?1:-1];
typedef char RizDoorstopRowV4Size[(sizeof(RizDoorstopEngineRowV4)==320)?1:-1];
typedef char RizDoorstopV4ModuleOffset[(offsetof(RizDoorstopEnginePublicationV4,modules)==128)?1:-1];
typedef char RizDoorstopV4RowOffset[(offsetof(RizDoorstopEnginePublicationV4,rows)==512)?1:-1];
typedef char RizDoorstopV4EventOffset[(offsetof(RizDoorstopEnginePublicationV4,events)==1792)?1:-1];
typedef char RizDoorstopV4Size[(sizeof(RizDoorstopEnginePublicationV4)==4096)?1:-1];
typedef char RizDoorstopRecordBeforeOffset[(offsetof(RizEngineDoorstopRecordV1,before)==160)?1:-1];
typedef char RizDoorstopRecordSize[(sizeof(RizEngineDoorstopRecordV1)==12448)?1:-1];
#ifdef __cplusplus
}
#endif
#endif
