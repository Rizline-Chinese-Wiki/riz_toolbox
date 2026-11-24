#ifndef RIZ_WINHTTP_DOORSTOP_H
#define RIZ_WINHTTP_DOORSTOP_H
#include "riz_winhttp.h"
#if !defined(RIZ_WINHTTP_EMBEDDED_DOORSTOP)
#error This wrapper is only for the explicit embedded Doorstop profile.
#endif
#ifdef __cplusplus
extern "C" {
#endif
enum RizWinHttpDoorstopStageV1 { RizWinHttpDoorstopFresh, RizWinHttpDoorstopEntered,
    RizWinHttpDoorstopResolverReturned, RizWinHttpDoorstopSnapshotCaptured,
    RizWinHttpDoorstopReady, RizWinHttpDoorstopFailed, RizWinHttpDoorstopReentered };
enum RizWinHttpDoorstopFailureV1 { RizWinHttpDoorstopNoFailure,
    RizWinHttpDoorstopRepeatedAttach, RizWinHttpDoorstopInitializeFailed,
    RizWinHttpDoorstopSnapshotFailed, RizWinHttpDoorstopInvalidReady };
typedef struct RizWinHttpDoorstopRecordV1 {
    uint32_t version,byteSize,stage,attempts;
    uint32_t threadId,initializeResult,infoResult,entryLastError;
    uint32_t initializeLastError,exitLastError,readyChecksPassed,failure;
    uint64_t selfModule;
    RizWinHttpInfoV1 info;
} RizWinHttpDoorstopRecordV1;
// Public link/MAP symbols only, deliberately absent from the 82-export DEF.
// A selected self-owned harness may resolve the exact held image's MAP RVA.
// Readers copy a completed record; this is not a live transactional snapshot.
extern RizWinHttpDoorstopRecordV1 RizWinHttpDoorstopRecord;
BOOL __cdecl RizWinHttpDoorstopAttach(HINSTANCE module);
#ifdef __cplusplus
}
static_assert(sizeof(RizWinHttpDoorstopRecordV1)==1304);
static_assert(offsetof(RizWinHttpDoorstopRecordV1,info)==56);
#else
typedef char RizDoorstopRecordSize[(sizeof(RizWinHttpDoorstopRecordV1)==1304)?1:-1];
typedef char RizDoorstopRecordInfoOffset[(offsetof(RizWinHttpDoorstopRecordV1,info)==56)?1:-1];
#endif
#endif
