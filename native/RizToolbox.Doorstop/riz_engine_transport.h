#pragma once
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstddef>
#include <cstdint>
#include "../RizToolbox.EngineBootstrap/EngineTransportPublication.h"

// Independent V5 build of the internal Doorstop glue. Link this implementation
// instead of riz_engine_bootstrap.c; the external 82 WinHTTP exports are unchanged.
// The C entry-wrapper signatures intentionally match the existing proxy sources.
enum RizTransportDoorstopStage : std::uint32_t {
    RizTransportProxyFresh, RizTransportProxyEntered, RizTransportProxySnapshot,
    RizTransportProxyVerified, RizTransportProxyDisabled, RizTransportProxyBody,
    RizTransportProxyComplete, RizTransportProxyRejected, RizTransportProxyAborting,
    RizTransportProxyAborted, RizTransportProxyDetaching, RizTransportProxyFatal
};
enum RizTransportDoorstopFailure : std::uint32_t {
    RizTransportProxyNoFailure, RizTransportProxyRepeatedAttach,
    RizTransportProxyBadVersion, RizTransportProxyUnreadable,
    RizTransportProxyBadPublication, RizTransportProxyVerifyFailed,
    RizTransportProxyChangedPublication, RizTransportProxyLastErrorChanged,
    RizTransportProxyPinFailed, RizTransportProxyBodyFailed,
    RizTransportProxyNativeException, RizTransportProxyDynamicDetach,
    RizTransportProxyUnavailable, RizTransportProxyIatFailed
};
struct RizEngineTransportDoorstopRecordV5 {
    std::uint32_t version,byteSize,stage,attempts;
    std::uint32_t threadId,processId,attachReason,startupStatic;
    std::uint32_t entryLastError,versionLastError,infoLastError,verifyLastError;
    std::uint32_t afterInfoLastError,exitLastError,nativeVersion,infoResult;
    std::uint32_t verifyResult,afterInfoResult,admission,result;
    std::uint32_t failure,abortAttempts,abortLastError,abortInfoResult;
    std::uint32_t abortInfoLastError,bodyLastError,exceptionCode,detachAttempts;
    std::uint32_t retainedResponsibility,selfPinned,rejected,processTermination;
    std::uint32_t verifyAttempts,infoReads,proxyIatAttempts,proxyIatResponsibility;
    std::uint64_t selfModule,reservedPointer;
    RizEngineTransportPublicationV5 before,after,terminal;
};
extern "C" {
extern RizEngineTransportDoorstopRecordV5 RizEngineTransportDoorstopRecord;
BOOL __cdecl RizEngineDoorstopBeforeAttach(HINSTANCE,DWORD,LPVOID);
BOOL __cdecl RizEngineDoorstopBeforeIat();
BOOL __cdecl RizEngineDoorstopAfterAttach(BOOL,DWORD);
BOOL __cdecl RizEngineDoorstopOnFailure(std::uint32_t,DWORD);
void __cdecl RizEngineDoorstopOnDetach(LPVOID);
}
static_assert(offsetof(RizEngineTransportDoorstopRecordV5,before)==160);
static_assert(sizeof(RizEngineTransportDoorstopRecordV5)==274288);
