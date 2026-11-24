#include "riz_winhttp_doorstop.h"

RizWinHttpDoorstopRecordV1 RizWinHttpDoorstopRecord;
static volatile LONG attachClaimed,attachRejected;

static BOOL Rejected(uint32_t failure,uint32_t stage)
{
    InterlockedExchange(&attachRejected,1);
    InterlockedCompareExchange((volatile LONG*)&RizWinHttpDoorstopRecord.failure,(LONG)failure,0);
    InterlockedExchange((volatile LONG*)&RizWinHttpDoorstopRecord.stage,(LONG)stage);
    RizWinHttpDoorstopRecord.exitLastError=ERROR_DLL_INIT_FAILED;
    SetLastError(ERROR_DLL_INIT_FAILED);return FALSE;
}
BOOL __cdecl RizWinHttpDoorstopAttach(HINSTANCE module)
{
    DWORD incoming=GetLastError();RizWinHttpDoorstopRecordV1* record=&RizWinHttpDoorstopRecord;
    InterlockedIncrement((volatile LONG*)&record->attempts);
    if(InterlockedCompareExchange(&attachClaimed,1,0)!=0){
        // Actual canonical repetition poisons the table before refusing the
        // repeated wrapper. Never let the outer owner revive a Ready record.
        (void)RizWinHttpForward_Initialize();
        return Rejected(RizWinHttpDoorstopRepeatedAttach,RizWinHttpDoorstopReentered);
    }
    record->version=1;record->byteSize=sizeof(*record);
    record->selfModule=(uint64_t)(uintptr_t)module;record->threadId=GetCurrentThreadId();
    record->entryLastError=incoming;
    InterlockedCompareExchange((volatile LONG*)&record->stage,RizWinHttpDoorstopEntered,RizWinHttpDoorstopFresh);
    SetLastError(incoming);
    record->initializeResult=RizWinHttpForward_Initialize();
    record->initializeLastError=GetLastError();
    InterlockedCompareExchange((volatile LONG*)&record->stage,RizWinHttpDoorstopResolverReturned,RizWinHttpDoorstopEntered);
    record->info.version=1;record->info.byteSize=sizeof(record->info);
    record->infoResult=RizWinHttpForward_GetInfo(&record->info);
    InterlockedCompareExchange((volatile LONG*)&record->stage,RizWinHttpDoorstopSnapshotCaptured,RizWinHttpDoorstopResolverReturned);
    if(InterlockedCompareExchange(&attachRejected,0,0))return Rejected(RizWinHttpDoorstopRepeatedAttach,RizWinHttpDoorstopReentered);
    if(record->initializeResult)return Rejected(RizWinHttpDoorstopInitializeFailed,RizWinHttpDoorstopFailed);
    if(record->infoResult)return Rejected(RizWinHttpDoorstopSnapshotFailed,RizWinHttpDoorstopFailed);
    if(record->initializeLastError!=incoming||record->info.state!=RizWinHttpReady
        ||record->info.failure||record->info.unknown||record->info.initializeAttempts!=1
        ||record->info.publishCount!=1||record->info.resolvedCount!=4
        ||record->info.flags!=(RizWinHttpFileHeld|RizWinHttpModuleRetained|RizWinHttpSameFile
            |RizWinHttpImageVerified|RizWinHttpNamesVerified|RizWinHttpPublished)
        ||record->info.selfModule!=record->selfModule||!record->info.systemModule||!record->info.heldSystemFile
        ||!record->info.initialTable||!record->info.poisonTable||!record->info.resolvedTable
        ||record->info.initialTable==record->info.poisonTable
        ||record->info.activeTable!=record->info.resolvedTable
        ||!record->info.addresses[0]||!record->info.addresses[1]||!record->info.addresses[2]||!record->info.addresses[3])
        return Rejected(RizWinHttpDoorstopInvalidReady,RizWinHttpDoorstopFailed);
    record->readyChecksPassed=1;
    if(InterlockedCompareExchange((volatile LONG*)&record->stage,RizWinHttpDoorstopReady,
            RizWinHttpDoorstopSnapshotCaptured)!=RizWinHttpDoorstopSnapshotCaptured
        ||InterlockedCompareExchange(&attachRejected,0,0))
        return Rejected(RizWinHttpDoorstopRepeatedAttach,RizWinHttpDoorstopReentered);
    record->exitLastError=incoming;SetLastError(incoming);return TRUE;
}
