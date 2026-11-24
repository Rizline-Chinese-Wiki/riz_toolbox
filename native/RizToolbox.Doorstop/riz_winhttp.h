#ifndef RIZ_WINHTTP_FORWARD_H
#define RIZ_WINHTTP_FORWARD_H
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winhttp.h>
#include <stddef.h>
#include <stdint.h>

#if defined(RIZ_WINHTTP_FIXTURE) && RIZ_WINHTTP_FIXTURE != 1
#error RIZ_WINHTTP_FIXTURE must be omitted or exactly 1.
#endif
#if defined(RIZ_WINHTTP_EMBEDDED_DOORSTOP) && RIZ_WINHTTP_EMBEDDED_DOORSTOP != 1
#error RIZ_WINHTTP_EMBEDDED_DOORSTOP must be omitted or exactly 1.
#endif
#if defined(RIZ_WINHTTP_EMBEDDED_DOORSTOP) && defined(RIZ_WINHTTP_FIXTURE)
#error The production Doorstop embedded profile cannot expose fixture selectors.
#endif
#ifdef __cplusplus
extern "C" {
#endif
#if defined(RIZ_WINHTTP_EMBEDDED_DOORSTOP)
#define RIZ_WINHTTP_API
#elif defined(RIZ_WINHTTP_FORWARD_BUILD)
#define RIZ_WINHTTP_API __declspec(dllexport)
#else
#define RIZ_WINHTTP_API __declspec(dllimport)
#endif

enum RizWinHttpStateV1 { RizWinHttpUninitialized, RizWinHttpResolving,
    RizWinHttpReady, RizWinHttpDisabledNoPublish, RizWinHttpUnknown };
enum RizWinHttpPhaseV1 { RizWinHttpPhaseNone, RizWinHttpPhaseSystemPath,
    RizWinHttpPhaseHeldFile, RizWinHttpPhaseLoadedModule, RizWinHttpPhaseImageIdentity,
    RizWinHttpPhaseNames, RizWinHttpPhasePublish, RizWinHttpPhaseComplete };
enum RizWinHttpFailureV1 { RizWinHttpNoFailure, RizWinHttpInvalidInput,
    RizWinHttpRepeatedInitialize, RizWinHttpBusyInitialize, RizWinHttpBadSystemPath,
    RizWinHttpOpenFailed, RizWinHttpFileIdentityFailed, RizWinHttpLoadFailed,
    RizWinHttpBadLoadedPath, RizWinHttpWrongFile, RizWinHttpBadImage,
    RizWinHttpBadExport, RizWinHttpBadAddress, RizWinHttpInjectedFailure,
    RizWinHttpNativeException, RizWinHttpPublishRejected };
enum RizWinHttpFlagsV1 { RizWinHttpFileHeld = 1, RizWinHttpModuleRetained = 2,
    RizWinHttpSameFile = 4, RizWinHttpImageVerified = 8, RizWinHttpNamesVerified = 16,
    RizWinHttpPublished = 32, RizWinHttpPoisoned = 64 };
// Fixed selectors only; never a path, module, function pointer or callback.
enum RizWinHttpFaultV1 { RizWinHttpFaultNone, RizWinHttpFaultBeforePath,
    RizWinHttpFaultBeforeOpen, RizWinHttpFaultBeforeLoad, RizWinHttpFaultAfterLoad,
    RizWinHttpFaultWrongName, RizWinHttpFaultSelfAddress, RizWinHttpFaultBeforePublish,
    RizWinHttpFaultReenter, RizWinHttpFaultFileIdentity, RizWinHttpFaultBeforeName };

typedef struct RizWinHttpInfoV1 {
    uint32_t version, byteSize, state, failure;
    uint32_t phase, flags, initializeAttempts, publishCount;
    uint64_t systemModule, heldSystemFile, selfModule, activeTable;
    uint64_t initialTable, poisonTable, resolvedTable;
    uint64_t fileIndex, fileBytes;
    uint32_t fileVolumeSerial, systemPathChars, loadedPathChars, peMachine;
    uint32_t peImageSize, peTimeDateStamp, peExportRva, peExportBytes;
    uint32_t loadFlags, resolvedCount, lastWin32Error, unknown;
    uint64_t addresses[4];
    uint32_t protections[4];
    uint32_t faultStage, faultIndex;
    WCHAR systemPath[260], loadedPath[260];
} RizWinHttpInfoV1;

RIZ_WINHTTP_API uint32_t __cdecl RizWinHttpForward_GetVersion(void);
// Caller supplies version=1/byteSize=1248. 0 snapshot, 1 invalid, 2 busy.
// Nonwaiting; busy GetInfo does not poison. Preserves caller LastError.
RIZ_WINHTTP_API uint32_t __cdecl RizWinHttpForward_GetInfo(RizWinHttpInfoV1* output);
// Standalone: explicit loader-OUTSIDE host stage only. Embedded Doorstop profile:
// only the selected real DllEntry PROCESS_ATTACH wrapper may initialize. That
// path is loader-stage code pending its own evidence, not an outside-loader call
// or a general claim that arbitrary WinHTTP/loader APIs are safe in DllMain.
// Exactly one attempt; 0 success,
// 1 failed/permanently rejected, 2 busy. Success restores incoming LastError.
// No caller-supplied module/path/slot. All four resolved slots publish at once.
// Repeat/reentry permanently poisons: future new forward calls return typed
// NULL/FALSE with ERROR_DLL_INIT_FAILED. A call that already read the resolved
// table may finish. Module, held file and all tables remain retained until exit.
RIZ_WINHTTP_API uint32_t __cdecl RizWinHttpForward_Initialize(void);
#if defined(RIZ_WINHTTP_FIXTURE)
// One selection before Initialize. 0 selected, 1 invalid, 2 already selected or
// attempted Initialize. Rejected setters cannot reset a previous selection.
RIZ_WINHTTP_API uint32_t __cdecl RizWinHttpForward_SetFault(uint32_t stage, uint32_t index);
#endif

// The four exported WinHttp* names use exactly the SDK declarations above.
// Standalone production exports these plus three Riz APIs (seven total).
// Embedded profile keeps Riz APIs internal and its explicit DEF owns exactly
// the prior 82 Doorstop names/ordinals, including these four ASM symbols.
#ifdef __cplusplus
}
static_assert(sizeof(RizWinHttpInfoV1) == 1248);
static_assert(offsetof(RizWinHttpInfoV1, addresses) == 152);
static_assert(offsetof(RizWinHttpInfoV1, systemPath) == 208);
static_assert(offsetof(RizWinHttpInfoV1, loadedPath) == 728);
#else
typedef char RizWinHttpInfoSizeCheck[(sizeof(RizWinHttpInfoV1) == 1248) ? 1 : -1];
typedef char RizWinHttpInfoPathCheck[(offsetof(RizWinHttpInfoV1, systemPath) == 208) ? 1 : -1];
#endif
#endif
