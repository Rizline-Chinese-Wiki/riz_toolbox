#include "riz_engine_bootstrap.h"

// Kept in the MAP, not the proxy export table. The three snapshots retain
// initial, verified/final and failure-terminal evidence without a heap/CRT.
RizEngineDoorstopRecordV1 RizEngineDoorstopRecord;
static volatile LONG attachClaimed,abortState,retainedResponsibility;
static const uint32_t prefixBytes[4]={15,18,16,15};
static const uint32_t targetRvas[4]={0x792240,0x78f840,0x78f680,0x793840};
static const uint32_t runtimeFunctions[4]={1,1,3,1};
// Profile2: static/game/proxy dependency facts 0x7f00, plus the separately
// authorized execution window 0x8000. Owned profile1 uses 31 and cannot pass.
static const uint32_t productionReadyAdmission=0xff00;

static void Zero(void* destination,size_t count)
{
    volatile unsigned char* out=(volatile unsigned char*)destination;
    size_t index;for(index=0;index<count;++index)out[index]=0;
}
static BOOL Equal(const void* left,const void* right,size_t count)
{
    const volatile unsigned char* a=(const volatile unsigned char*)left;
    const volatile unsigned char* b=(const volatile unsigned char*)right;
    unsigned difference=0;size_t index;
    for(index=0;index<count;++index)difference|=a[index]^b[index];
    return difference==0;
}
static BOOL IsZero(const void* data,size_t count)
{
    const volatile unsigned char* value=(const volatile unsigned char*)data;
    unsigned bits=0;size_t index;for(index=0;index<count;++index)bits|=value[index];
    return bits==0;
}
static void Stage(uint32_t stage)
{InterlockedExchange((volatile LONG*)&RizEngineDoorstopRecord.stage,(LONG)stage);}
static void Fatal(void)
{
    Stage(RizEngineDoorstopFatal);
    RaiseFailFastException(NULL,NULL,0);
    TerminateProcess(GetCurrentProcess(),0xe052495c);
    for(;;) { /* No owner is permitted to unload and continue execution. */ }
}
static BOOL HasResponsibility(const RizDoorstopEnginePublicationV4* value)
{
    unsigned kind;
    if(value->configuredMask||value->boundMask||value->committedMask||value->sourceWriteAttempts
        ||value->sourceWriteCompletions||value->protectionOutstanding)return TRUE;
    for(kind=0;kind<4;++kind){const RizDoorstopEngineRowV4* row=&value->rows[kind];
        if((row->flags&~1u)||row->entry||row->helperEpoch||row->helperSlot
            ||row->protectAttempts||row->protectSucceeded||row->sourceWriteAttempts||row->sourceBytesWritten
            ||row->readbackAttempts||row->readbackVerified||row->restoreAttempts||row->restoreSucceeded
            ||row->flushAttempts||row->flushSucceeded||row->protectionOutstanding)return TRUE;
    }return FALSE;
}
static void RememberResponsibility(const RizDoorstopEnginePublicationV4* value)
{
    if(HasResponsibility(value))InterlockedExchange(&retainedResponsibility,1);
    RizEngineDoorstopRecord.retainedResponsibility=(uint32_t)InterlockedCompareExchange(&retainedResponsibility,0,0);
}
static uint32_t Read(RizDoorstopEnginePublicationV4* value,DWORD incoming)
{
    uint32_t result;Zero(value,sizeof(*value));value->version=4;value->byteSize=sizeof(*value);
    InterlockedIncrement((volatile LONG*)&RizEngineDoorstopRecord.infoReads);
    SetLastError(incoming);result=RizEngineFirstInstall_GetInfoV4(value);
    // Do not allow even a malformed/partial response to erase a seen claim.
    RememberResponsibility(value);return result;
}
static BOOL Envelope(const RizDoorstopEnginePublicationV4* value)
{
    unsigned index;
    if(value->version!=4||value->byteSize!=4096||value->profile!=2||value->claimed!=1
        ||value->processId!=GetCurrentProcessId()||!value->ownerThread||value->attachReason!=DLL_PROCESS_ATTACH
        ||value->startupStatic>1||value->startupStatic!=(value->reservedPointer?1u:0u)
        ||!value->eventCount||value->eventCount>64||value->overflow||value->verifyFailures
        ||!IsZero(value->reserved,sizeof(value->reserved)))return FALSE;
    for(index=0;index<value->eventCount;++index){const RizDoorstopEngineEventV4* event=&value->events[index];
        if(event->sequence!=(uint64_t)index+1||!event->stage||event->stage>18||event->kind>4
            ||event->state>6||event->reserved)return FALSE;
    }
    return IsZero(value->events+value->eventCount,(64-value->eventCount)*sizeof(value->events[0]));
}
static BOOL NoWriteDisabled(const RizDoorstopEnginePublicationV4* value)
{
    return Envelope(value)&&value->state==5&&value->failure>0&&value->failure<=19
        &&!HasResponsibility(value)&&!InterlockedCompareExchange(&retainedResponsibility,0,0);
}
static BOOL Within(uint64_t address,uint64_t bytes,const RizDoorstopEngineModuleV4* module)
{
    return address>=module->module&&address-module->module<module->imageSize
        &&bytes<=module->imageSize-(address-module->module);
}
static BOOL Ready(const RizDoorstopEnginePublicationV4* value)
{
    const RizDoorstopEngineModuleV4* target=&value->modules[2];unsigned index,other;
    const uint64_t addresses[4]={value->bootstrapModule,value->kernelModule,value->targetModule,value->executableModule};
    if(!Envelope(value)||value->state!=4||value->failure||value->epoch!=1||value->startupStatic!=1
        ||value->ownerThread!=GetCurrentThreadId()||value->configuredMask!=15||value->boundMask!=15
        ||value->committedMask!=15||value->sourceWriteAttempts!=4||value->sourceWriteCompletions!=4
        ||value->protectionOutstanding||value->admissionFlags!=productionReadyAdmission)return FALSE;
    if(addresses[0]!=(uint64_t)(uintptr_t)GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll")
        ||addresses[1]!=(uint64_t)(uintptr_t)GetModuleHandleW(L"RizToolbox.EngineNodeKernel.dll")
        ||addresses[2]!=(uint64_t)(uintptr_t)GetModuleHandleW(L"UnityPlayer.dll")
        ||addresses[3]!=(uint64_t)(uintptr_t)GetModuleHandleW(NULL))return FALSE;
    for(index=0;index<4;++index){const RizDoorstopEngineModuleV4* module=&value->modules[index];
        if(!addresses[index])return FALSE;
        for(other=0;other<index;++other)if(addresses[index]==addresses[other])return FALSE;
        if(module->kind!=index||module->machine!=0x8664||module->imageSize<4096||!module->timeDateStamp
            ||module->module!=addresses[index]||!module->fileBytes||!module->fileIndex||!module->volumeSerial
            ||!module->heldFile||module->flags!=(index==0?7u:15u)||IsZero(module->sha256,32)
            ||!module->exceptionRva||module->exceptionRva>=module->imageSize
            ||(index==1?module->entryRva!=0:(!module->entryRva||module->entryRva>=module->imageSize)))return FALSE;
    }
    for(index=0;index<4;++index){const RizDoorstopEngineRowV4* row=&value->rows[index];unsigned at;
        if(row->version!=4||row->byteSize!=320||row->kind!=index||row->prefixBytes!=prefixBytes[index]
            ||row->module!=target->module||row->rva!=targetRvas[index]||!Within(row->target,32,target)
            ||row->target-target->module!=row->rva||row->continuation!=row->target+row->prefixBytes
            ||!Within(row->original,row->prefixBytes+10,&value->modules[0])||!Within(row->entry,1,&value->modules[1])
            ||row->epoch!=value->epoch||row->originalProtection!=PAGE_EXECUTE_READ||row->observedProtection!=PAGE_EXECUTE_READ
            ||row->flags!=7||row->protectAttempts!=1||row->protectSucceeded!=1||row->sourceWriteAttempts!=1
            ||row->sourceBytesWritten!=row->prefixBytes||row->readbackAttempts!=1||row->readbackVerified!=1
            ||row->restoreAttempts!=1||row->restoreSucceeded!=1||row->flushAttempts!=1||row->flushSucceeded!=1
            ||row->protectionOutstanding||row->lastWin32Error||!row->helperEpoch||row->helperSlot!=28||row->reserved
            ||!IsZero(row->reservedTail,sizeof(row->reservedTail))||row->runtimeFunctionCount!=runtimeFunctions[index]
            ||!Equal(row->moduleSha256,target->sha256,32)||IsZero(row->bodySha256,32)
            ||row->patchBytes[0]!=0xff||row->patchBytes[1]!=0x25||!IsZero(row->patchBytes+2,4)
            ||!Equal(row->patchBytes,row->observedBytes,32))return FALSE;
        for(at=0;at<8;++at)if(row->patchBytes[6+at]!=(unsigned char)(row->entry>>(8*at)))return FALSE;
        for(at=14;at<row->prefixBytes;++at)if(row->patchBytes[at]!=0x90)return FALSE;
        if(!Equal(row->patchBytes+row->prefixBytes,row->originalBytes+row->prefixBytes,32-row->prefixBytes))return FALSE;
    }
    for(index=0;index<value->eventCount;++index)if(value->events[index].state>4||value->events[index].failure||value->events[index].win32Error)return FALSE;
    return TRUE;
}

BOOL __cdecl RizEngineDoorstopOnFailure(uint32_t failure,DWORD incoming)
{
    RizEngineDoorstopRecordV1* record=&RizEngineDoorstopRecord;BOOL safeBefore=FALSE;LONG previous;
    InterlockedExchange((volatile LONG*)&record->rejected,1);
    InterlockedCompareExchange((volatile LONG*)&record->failure,(LONG)failure,0);
    record->result=FALSE;record->bodyLastError=incoming;Stage(RizEngineDoorstopRejected);
    __try {
        record->abortInfoResult=Read(&record->terminal,incoming);record->abortInfoLastError=GetLastError();
        safeBefore=record->nativeVersion==4&&record->abortInfoResult==0&&record->abortInfoLastError==incoming
            &&NoWriteDisabled(&record->terminal);
        previous=InterlockedCompareExchange(&abortState,1,0);
        if(previous==1)Fatal(); // Recursive failure must never wait on itself.
        if(previous==0){
            Stage(RizEngineDoorstopAborting);++record->abortAttempts;
            SetLastError(incoming);RizEngineFirstInstall_AbortAfterPublication();record->abortLastError=GetLastError();
            record->abortInfoResult=Read(&record->terminal,incoming);record->abortInfoLastError=GetLastError();
            InterlockedExchange(&abortState,2);
        }
        // Abort is not allowed to clear a prior responsibility or turn an
        // unreadable/Unknown/busy publication into a safe no-write rejection.
        if(!safeBefore||record->proxyIatResponsibility||(previous==0&&record->abortLastError!=incoming)||record->abortInfoResult||record->abortInfoLastError!=incoming
            ||!NoWriteDisabled(&record->terminal))Fatal();
    } __except(EXCEPTION_EXECUTE_HANDLER){record->exceptionCode=GetExceptionCode();Fatal();}
    Stage(RizEngineDoorstopAborted);record->exitLastError=incoming;SetLastError(incoming);return FALSE;
}

BOOL __cdecl RizEngineDoorstopBeforeAttach(HINSTANCE module,DWORD reason,LPVOID reserved)
{
    const DWORD incoming=GetLastError();RizEngineDoorstopRecordV1* record=&RizEngineDoorstopRecord;
    InterlockedIncrement((volatile LONG*)&record->attempts);
    if(InterlockedCompareExchange(&attachClaimed,1,0)!=0)
        return RizEngineDoorstopOnFailure(RizEngineDoorstopRepeatedAttach,incoming);
    record->version=1;record->byteSize=sizeof(*record);record->selfModule=(uint64_t)(uintptr_t)module;
    record->reservedPointer=(uint64_t)(uintptr_t)reserved;record->attachReason=reason;record->startupStatic=reserved?1u:0u;
    record->threadId=GetCurrentThreadId();record->processId=GetCurrentProcessId();record->entryLastError=incoming;
    record->verifyResult=0xffffffffu;Stage(RizEngineDoorstopEntered);
    __try {
        SetLastError(incoming);record->nativeVersion=RizEngineFirstInstall_GetVersion();record->versionLastError=GetLastError();
        if(record->nativeVersion!=4)return RizEngineDoorstopOnFailure(RizEngineDoorstopBadVersion,incoming);
        if(record->versionLastError!=incoming)return RizEngineDoorstopOnFailure(RizEngineDoorstopLastErrorChanged,incoming);
        record->infoResult=Read(&record->before,incoming);record->infoLastError=GetLastError();Stage(RizEngineDoorstopSnapshot);
        if(record->infoResult)return RizEngineDoorstopOnFailure(RizEngineDoorstopUnreadable,incoming);
        if(record->infoLastError!=incoming)return RizEngineDoorstopOnFailure(RizEngineDoorstopLastErrorChanged,incoming);
        if(reason!=DLL_PROCESS_ATTACH||record->before.ownerThread!=record->threadId)
            return RizEngineDoorstopOnFailure(RizEngineDoorstopBadPublication,incoming);
        if(Ready(&record->before)){
            HMODULE pinned=NULL;
            if(!reserved)return RizEngineDoorstopOnFailure(RizEngineDoorstopBadPublication,incoming);
            ++record->verifyAttempts;SetLastError(incoming);record->verifyResult=RizEngineFirstInstall_VerifyV4();
            record->verifyLastError=GetLastError();
            if(record->verifyResult)return RizEngineDoorstopOnFailure(RizEngineDoorstopVerifyFailed,incoming);
            if(record->verifyLastError!=incoming)return RizEngineDoorstopOnFailure(RizEngineDoorstopLastErrorChanged,incoming);
            if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN|GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(LPCWSTR)module,&pinned)
                ||pinned!=module)return RizEngineDoorstopOnFailure(RizEngineDoorstopPinFailed,incoming);
            record->selfPinned=1;record->admission=RizEngineDoorstopReadyAdmission;Stage(RizEngineDoorstopVerified);
        }else if(NoWriteDisabled(&record->before)){
            record->admission=RizEngineDoorstopNoWriteAdmission;Stage(RizEngineDoorstopDisabled);
        }else return RizEngineDoorstopOnFailure(RizEngineDoorstopBadPublication,incoming);
        record->afterInfoResult=Read(&record->after,incoming);record->afterInfoLastError=GetLastError();
        if(record->afterInfoResult||record->afterInfoLastError!=incoming
            ||!Equal(&record->before,&record->after,sizeof(record->before)))
            return RizEngineDoorstopOnFailure(RizEngineDoorstopChangedPublication,incoming);
    } __except(EXCEPTION_EXECUTE_HANDLER){record->exceptionCode=GetExceptionCode();
        return RizEngineDoorstopOnFailure(RizEngineDoorstopNativeException,incoming);}
    record->result=TRUE;record->exitLastError=incoming;Stage(RizEngineDoorstopBody);SetLastError(incoming);return TRUE;
}

BOOL __cdecl RizEngineDoorstopAfterAttach(BOOL bodyResult,DWORD incoming)
{
    RizEngineDoorstopRecordV1* record=&RizEngineDoorstopRecord;record->bodyLastError=incoming;
    if(!bodyResult||InterlockedCompareExchange((volatile LONG*)&record->rejected,0,0))
        return RizEngineDoorstopOnFailure(RizEngineDoorstopBodyFailed,incoming);
    __try {
        record->afterInfoResult=Read(&record->after,incoming);record->afterInfoLastError=GetLastError();
        if(record->afterInfoResult||record->afterInfoLastError!=incoming
            ||!Equal(&record->before,&record->after,sizeof(record->before)))
            return RizEngineDoorstopOnFailure(RizEngineDoorstopChangedPublication,incoming);
        if(record->admission==RizEngineDoorstopReadyAdmission){
            if(!Ready(&record->after))return RizEngineDoorstopOnFailure(RizEngineDoorstopBadPublication,incoming);
            ++record->verifyAttempts;SetLastError(incoming);record->verifyResult=RizEngineFirstInstall_VerifyV4();
            record->verifyLastError=GetLastError();
            if(record->verifyResult||record->verifyLastError!=incoming)
                return RizEngineDoorstopOnFailure(RizEngineDoorstopVerifyFailed,incoming);
            record->afterInfoResult=Read(&record->terminal,incoming);record->afterInfoLastError=GetLastError();
            if(record->afterInfoResult||record->afterInfoLastError!=incoming
                ||!Equal(&record->before,&record->terminal,sizeof(record->before)))
                return RizEngineDoorstopOnFailure(RizEngineDoorstopChangedPublication,incoming);
        }else if(record->admission!=RizEngineDoorstopNoWriteAdmission||!NoWriteDisabled(&record->after))
            return RizEngineDoorstopOnFailure(RizEngineDoorstopBadPublication,incoming);
    } __except(EXCEPTION_EXECUTE_HANDLER){record->exceptionCode=GetExceptionCode();
        return RizEngineDoorstopOnFailure(RizEngineDoorstopNativeException,incoming);}
    record->result=TRUE;record->exitLastError=incoming;Stage(RizEngineDoorstopComplete);SetLastError(incoming);return TRUE;
}

BOOL __cdecl RizEngineDoorstopBeforeIat(void)
{
    const DWORD incoming=GetLastError();HMODULE pinned=NULL;RizEngineDoorstopRecordV1* record=&RizEngineDoorstopRecord;
    ++record->proxyIatAttempts;
    if(InterlockedCompareExchange((volatile LONG*)&record->rejected,0,0)){SetLastError(incoming);return FALSE;}
    if(!record->selfPinned){
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN|GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                (LPCWSTR)(uintptr_t)record->selfModule,&pinned)||pinned!=(HMODULE)(uintptr_t)record->selfModule)
            return RizEngineDoorstopOnFailure(RizEngineDoorstopPinFailed,incoming);
        record->selfPinned=1;
    }
    // Legacy Doorstop IAT entries also point into this proxy. A partial hook
    // failure cannot return FALSE and abandon those writes, even when the
    // separate engine publisher was disabled without any source changes.
    InterlockedExchange((volatile LONG*)&record->proxyIatResponsibility,1);
    SetLastError(incoming);return TRUE;
}

void __cdecl RizEngineDoorstopOnDetach(LPVOID reserved)
{
    const DWORD incoming=GetLastError();RizEngineDoorstopRecordV1* record=&RizEngineDoorstopRecord;
    ++record->detachAttempts;record->processTermination=reserved?1u:0u;Stage(RizEngineDoorstopDetaching);
    // Process termination releases the entire address space. Dynamic unload
    // while the process continues must instead merge all permanent claims.
    if(!reserved)(void)RizEngineDoorstopOnFailure(RizEngineDoorstopDynamicDetach,incoming);
    SetLastError(incoming);
}
