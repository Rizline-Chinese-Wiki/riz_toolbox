#define RIZ_WINHTTP_FORWARD_BUILD
#include "riz_winhttp.h"
#include <intrin.h>

#if !defined(RIZ_WINHTTP_EMBEDDED_DOORSTOP)
#pragma comment(linker, "/EXPORT:WinHttpOpen")
#pragma comment(linker, "/EXPORT:WinHttpGetProxyForUrl")
#pragma comment(linker, "/EXPORT:WinHttpGetIEProxyConfigForCurrentUser")
#pragma comment(linker, "/EXPORT:WinHttpCloseHandle")
#endif

extern IMAGE_DOS_HEADER __ImageBase;
typedef HINTERNET (WINAPI *RizOpen)(LPCWSTR,DWORD,LPCWSTR,LPCWSTR,DWORD);
typedef BOOL (WINAPI *RizProxy)(HINTERNET,LPCWSTR,WINHTTP_AUTOPROXY_OPTIONS*,WINHTTP_PROXY_INFO*);
typedef BOOL (WINAPI *RizIe)(WINHTTP_CURRENT_USER_IE_PROXY_CONFIG*);
typedef BOOL (WINAPI *RizClose)(HINTERNET);
typedef struct RizTable { RizOpen open; RizProxy proxy; RizIe ie; RizClose close; } RizTable;
typedef union RizAddress { FARPROC procedure; uintptr_t bits; RizOpen open;
    RizProxy proxy; RizIe ie; RizClose close; } RizAddress;
typedef char RizTableSize[(sizeof(RizTable)==32) ? 1 : -1];

static HINTERNET WINAPI RefuseOpen(LPCWSTR agent,DWORD access,LPCWSTR proxy,LPCWSTR bypass,DWORD flags)
{ (void)agent;(void)access;(void)proxy;(void)bypass;(void)flags;SetLastError(ERROR_DLL_INIT_FAILED);return NULL; }
static BOOL WINAPI RefuseProxy(HINTERNET session,LPCWSTR url,WINHTTP_AUTOPROXY_OPTIONS* options,WINHTTP_PROXY_INFO* output)
{ (void)session;(void)url;(void)options;(void)output;SetLastError(ERROR_DLL_INIT_FAILED);return FALSE; }
static BOOL WINAPI RefuseIe(WINHTTP_CURRENT_USER_IE_PROXY_CONFIG* output)
{ (void)output;SetLastError(ERROR_DLL_INIT_FAILED);return FALSE; }
static BOOL WINAPI RefuseClose(HINTERNET handle)
{ (void)handle;SetLastError(ERROR_DLL_INIT_FAILED);return FALSE; }

// Distinct address-significant objects. Link with /OPT:NOICF; the independent
// disk/runtime verifier also rejects a merged initial/poison table.
static const RizTable initialTable={RefuseOpen,RefuseProxy,RefuseIe,RefuseClose};
static const RizTable poisonTable={RefuseOpen,RefuseProxy,RefuseIe,RefuseClose};
static RizTable resolvedTable;
// The four leaf ASM exports read this aligned pointer exactly once, then tail
// jump. No API, lock or LastError manipulation occurs along the ready path.
const RizTable* volatile RizWinHttpActiveSlots=&initialTable;
static RizWinHttpInfoV1 information;
static volatile LONG gate,attempted,attempts,lifecycle,failureCode,errorCode;
static volatile LONG retainedFlags,unknown,publishCount;
#if defined(RIZ_WINHTTP_FIXTURE)
static LONG faultSelected;
#endif

static LONG ReadLong(volatile LONG* value)
{ return InterlockedCompareExchange(value,0,0); }
static const RizTable* ReadActive(void)
{ return (const RizTable*)InterlockedCompareExchangePointer((void* volatile*)&RizWinHttpActiveSlots,NULL,NULL); }
static void CopyBytes(void* output,const void* input,size_t bytes)
{ volatile unsigned char* out=(volatile unsigned char*)output;const volatile unsigned char* in=(const volatile unsigned char*)input;
  size_t i;for(i=0;i<bytes;++i)out[i]=in[i]; }
static void FirstFailure(uint32_t failure,DWORD error)
{ InterlockedCompareExchange(&failureCode,(LONG)failure,0);InterlockedCompareExchange(&errorCode,(LONG)(error?error:ERROR_DLL_INIT_FAILED),0); }
static void Poison(uint32_t failure,DWORD error)
{
    // Revocation linearizes at this exchange. There is no path back from the
    // poison address to initial, so the owner's publication CAS cannot revive it.
    InterlockedExchangePointer((void* volatile*)&RizWinHttpActiveSlots,(void*)&poisonTable);
    InterlockedExchange(&unknown,1);InterlockedOr(&retainedFlags,RizWinHttpPoisoned);
    FirstFailure(failure,error);InterlockedExchange(&lifecycle,RizWinHttpUnknown);
}
static int Fail(uint32_t failure,DWORD error)
{ FirstFailure(failure,error);return 0; }
static int Fault(uint32_t stage,uint32_t index)
{ return information.faultStage==stage&&information.faultIndex==index; }
static int Inject(uint32_t stage,uint32_t index)
{ return Fault(stage,index)?Fail(RizWinHttpInjectedFailure,ERROR_DLL_INIT_FAILED):1; }
static int InImage(uint32_t rva,size_t bytes)
{ return rva&&bytes&&rva<information.peImageSize&&bytes<=information.peImageSize-rva; }
static int Memory(const void* address,size_t bytes,HMODULE module,int executable,DWORD* protection)
{
    uintptr_t at=(uintptr_t)address,end;MEMORY_BASIC_INFORMATION memory;
    if(!at||!bytes||at>UINTPTR_MAX-bytes)return 0;
    end=at+bytes;
    while(at<end){uintptr_t next;DWORD kind;
        if(VirtualQuery((const void*)at,&memory,sizeof(memory))!=sizeof(memory)
            ||memory.State!=MEM_COMMIT||memory.Type!=MEM_IMAGE||memory.AllocationBase!=module
            ||(memory.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return 0;
        kind=memory.Protect&0xff;
        if(executable&&kind!=PAGE_EXECUTE&&kind!=PAGE_EXECUTE_READ
            &&kind!=PAGE_EXECUTE_READWRITE&&kind!=PAGE_EXECUTE_WRITECOPY)return 0;
        if(!executable&&kind!=PAGE_READONLY&&kind!=PAGE_READWRITE&&kind!=PAGE_WRITECOPY
            &&kind!=PAGE_EXECUTE_READ&&kind!=PAGE_EXECUTE_READWRITE&&kind!=PAGE_EXECUTE_WRITECOPY)return 0;
        if(at==(uintptr_t)address&&protection)*protection=memory.Protect;
        next=(uintptr_t)memory.BaseAddress+memory.RegionSize;if(next<=at)return 0;
        at=next<end?next:end;
    }return 1;
}
static int ReadImage(HMODULE module)
{
    const unsigned char* base=(const unsigned char*)module;const IMAGE_DOS_HEADER* dos;
    const IMAGE_NT_HEADERS64* nt;IMAGE_DATA_DIRECTORY exports;
    if(module==(HMODULE)&__ImageBase||!Memory(module,sizeof(IMAGE_DOS_HEADER),module,0,NULL))return 0;
    dos=(const IMAGE_DOS_HEADER*)module;
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x100000)return 0;
    nt=(const IMAGE_NT_HEADERS64*)(base+(uint32_t)dos->e_lfanew);
    if(!Memory(nt,sizeof(*nt),module,0,NULL)||nt->Signature!=IMAGE_NT_SIGNATURE
        ||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64
        ||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC
        ||nt->FileHeader.SizeOfOptionalHeader<sizeof(IMAGE_OPTIONAL_HEADER64)
        ||!(nt->FileHeader.Characteristics&IMAGE_FILE_DLL)
        ||nt->OptionalHeader.NumberOfRvaAndSizes<=IMAGE_DIRECTORY_ENTRY_EXPORT
        ||nt->OptionalHeader.SizeOfImage<4096||nt->OptionalHeader.SizeOfImage>256u*1024u*1024u)return 0;
    information.peMachine=nt->FileHeader.Machine;information.peImageSize=nt->OptionalHeader.SizeOfImage;
    information.peTimeDateStamp=nt->FileHeader.TimeDateStamp;
    if((uint32_t)dos->e_lfanew>information.peImageSize-sizeof(*nt))return 0;
    exports=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];
    information.peExportRva=exports.VirtualAddress;information.peExportBytes=exports.Size;
    return exports.Size>=sizeof(IMAGE_EXPORT_DIRECTORY)&&InImage(exports.VirtualAddress,exports.Size)
        &&Memory(base+exports.VirtualAddress,exports.Size,module,0,NULL);
}
static int EqualName(HMODULE module,uint32_t rva,const char* expected)
{
    const unsigned char* base=(const unsigned char*)module;unsigned i;
    for(i=0;i<128;++i){unsigned char actual;
        if(!InImage(rva+i,1)||!Memory(base+rva+i,1,module,0,NULL))return 0;
        actual=base[rva+i];if(actual!=(unsigned char)expected[i])return 0;
        if(!actual)return 1;
    }return 0;
}
static const char* const names[4]={"WinHttpOpen","WinHttpGetProxyForUrl",
    "WinHttpGetIEProxyConfigForCurrentUser","WinHttpCloseHandle"};
static int ResolveName(HMODULE module,unsigned kind)
{
    const unsigned char* base=(const unsigned char*)module;
    const IMAGE_EXPORT_DIRECTORY* exports=(const IMAGE_EXPORT_DIRECTORY*)(base+information.peExportRva);
    const DWORD* nameRvas;const DWORD* functionRvas;const WORD* ordinals;
    uint32_t i,found=0,functionRva=0;DWORD protection;RizAddress actual,expected;
    if(!exports->NumberOfNames||exports->NumberOfNames>65536||!exports->NumberOfFunctions
        ||exports->NumberOfFunctions>65536)return Fail(RizWinHttpBadExport,ERROR_INVALID_DATA);
    if(!InImage(exports->AddressOfNames,(size_t)exports->NumberOfNames*sizeof(DWORD))
        ||!InImage(exports->AddressOfNameOrdinals,(size_t)exports->NumberOfNames*sizeof(WORD))
        ||!InImage(exports->AddressOfFunctions,(size_t)exports->NumberOfFunctions*sizeof(DWORD)))return Fail(RizWinHttpBadExport,ERROR_INVALID_DATA);
    nameRvas=(const DWORD*)(base+exports->AddressOfNames);ordinals=(const WORD*)(base+exports->AddressOfNameOrdinals);
    functionRvas=(const DWORD*)(base+exports->AddressOfFunctions);
    if(!Memory(nameRvas,(size_t)exports->NumberOfNames*sizeof(DWORD),module,0,NULL)
        ||!Memory(ordinals,(size_t)exports->NumberOfNames*sizeof(WORD),module,0,NULL)
        ||!Memory(functionRvas,(size_t)exports->NumberOfFunctions*sizeof(DWORD),module,0,NULL))return Fail(RizWinHttpBadExport,ERROR_INVALID_DATA);
    for(i=0;i<exports->NumberOfNames;++i)if(EqualName(module,nameRvas[i],names[kind])){
        if(++found!=1||ordinals[i]>=exports->NumberOfFunctions)return Fail(RizWinHttpBadExport,ERROR_INVALID_DATA);
        functionRva=functionRvas[ordinals[i]];
    }
    if(found!=1||!InImage(functionRva,1)
        ||(functionRva>=information.peExportRva&&functionRva-information.peExportRva<information.peExportBytes))return Fail(RizWinHttpBadExport,ERROR_PROC_NOT_FOUND);
    if(!Inject(RizWinHttpFaultBeforeName,kind))return 0;
    actual.procedure=GetProcAddress(module,Fault(RizWinHttpFaultWrongName,kind)?"RizFixedMissingWinHttpExport":names[kind]);
    if(!actual.procedure)return Fail(RizWinHttpBadExport,GetLastError());
    expected.bits=(uintptr_t)module+functionRva;
    if(Fault(RizWinHttpFaultSelfAddress,kind))actual.open=RefuseOpen;
    if(actual.bits!=expected.bits||!Memory((const void*)actual.bits,1,module,1,&protection))return Fail(RizWinHttpBadAddress,ERROR_INVALID_ADDRESS);
    switch(kind){case 0:resolvedTable.open=actual.open;break;case 1:resolvedTable.proxy=actual.proxy;break;
        case 2:resolvedTable.ie=actual.ie;break;default:resolvedTable.close=actual.close;break;}
    information.addresses[kind]=(uint64_t)actual.bits;information.protections[kind]=protection;
    ++information.resolvedCount;return 1;
}
static int SameFile(const BY_HANDLE_FILE_INFORMATION* left,const BY_HANDLE_FILE_INFORMATION* right)
{ return left->dwVolumeSerialNumber==right->dwVolumeSerialNumber
    &&left->nFileIndexHigh==right->nFileIndexHigh&&left->nFileIndexLow==right->nFileIndexLow
    &&left->nFileSizeHigh==right->nFileSizeHigh&&left->nFileSizeLow==right->nFileSizeLow; }
static int Resolve(void)
{
    static const WCHAR suffix[]=L"\\winhttp.dll";
    unsigned length,i;HANDLE held,loadedFile;HMODULE module;BY_HANDLE_FILE_INFORMATION heldId,loadedId;
    information.phase=RizWinHttpPhaseSystemPath;
    if(!Inject(RizWinHttpFaultBeforePath,0))return 0;
    length=GetSystemDirectoryW(information.systemPath,260);
    if(length<3||length>=260||information.systemPath[1]!=L':'||information.systemPath[2]!=L'\\'
        ||length+sizeof(suffix)/sizeof(suffix[0])>260)return Fail(RizWinHttpBadSystemPath,GetLastError());
    if(information.systemPath[length-1]==L'\\')--length;
    for(i=0;i<sizeof(suffix)/sizeof(suffix[0]);++i)information.systemPath[length+i]=suffix[i];
    information.systemPathChars=length+(uint32_t)(sizeof(suffix)/sizeof(suffix[0]))-1;
    information.phase=RizWinHttpPhaseHeldFile;
    if(!Inject(RizWinHttpFaultBeforeOpen,0))return 0;
    held=CreateFileW(information.systemPath,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);
    if(held==INVALID_HANDLE_VALUE)return Fail(RizWinHttpOpenFailed,GetLastError());
    information.heldSystemFile=(uint64_t)(uintptr_t)held;InterlockedOr(&retainedFlags,RizWinHttpFileHeld);
    if(!GetFileInformationByHandle(held,&heldId))return Fail(RizWinHttpFileIdentityFailed,GetLastError());
    information.fileIndex=((uint64_t)heldId.nFileIndexHigh<<32)|heldId.nFileIndexLow;
    information.fileBytes=((uint64_t)heldId.nFileSizeHigh<<32)|heldId.nFileSizeLow;
    information.fileVolumeSerial=heldId.dwVolumeSerialNumber;
    if(!information.fileBytes||(heldId.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY))return Fail(RizWinHttpFileIdentityFailed,ERROR_INVALID_DATA);
    information.phase=RizWinHttpPhaseLoadedModule;
    if(!Inject(RizWinHttpFaultBeforeLoad,0))return 0;
    information.loadFlags=LOAD_LIBRARY_SEARCH_SYSTEM32;
    module=LoadLibraryExW(information.systemPath,NULL,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!module)return Fail(RizWinHttpLoadFailed,GetLastError());
    information.systemModule=(uint64_t)(uintptr_t)module;InterlockedOr(&retainedFlags,RizWinHttpModuleRetained);
    if(!Inject(RizWinHttpFaultAfterLoad,0))return 0;
    information.phase=RizWinHttpPhaseImageIdentity;
    length=GetModuleFileNameW(module,information.loadedPath,260);
    information.loadedPathChars=length;
    if(!length||length>=260||length!=information.systemPathChars
        ||CompareStringOrdinal(information.systemPath,(int)information.systemPathChars,information.loadedPath,(int)length,TRUE)!=CSTR_EQUAL)return Fail(RizWinHttpBadLoadedPath,ERROR_BAD_PATHNAME);
    loadedFile=CreateFileW(information.loadedPath,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);
    if(loadedFile==INVALID_HANDLE_VALUE)return Fail(RizWinHttpFileIdentityFailed,GetLastError());
    if(!GetFileInformationByHandle(loadedFile,&loadedId)){
        DWORD error=GetLastError();CloseHandle(loadedFile);return Fail(RizWinHttpFileIdentityFailed,error);
    }
    if(!CloseHandle(loadedFile))return Fail(RizWinHttpFileIdentityFailed,GetLastError());
    if(!SameFile(&heldId,&loadedId)||Fault(RizWinHttpFaultFileIdentity,0))return Fail(RizWinHttpWrongFile,ERROR_FILE_INVALID);
    InterlockedOr(&retainedFlags,RizWinHttpSameFile);
    if(!ReadImage(module))return Fail(RizWinHttpBadImage,ERROR_BAD_EXE_FORMAT);
    InterlockedOr(&retainedFlags,RizWinHttpImageVerified);information.phase=RizWinHttpPhaseNames;
    for(i=0;i<4;++i)if(!ResolveName(module,i))return 0;
    InterlockedOr(&retainedFlags,RizWinHttpNamesVerified);information.phase=RizWinHttpPhasePublish;
    if(Fault(RizWinHttpFaultReenter,0)){
        // Controlled actual recursion, not a simulated result or a callback.
        (void)RizWinHttpForward_Initialize();
    }
    if(!Inject(RizWinHttpFaultBeforePublish,0))return 0;
    if(information.resolvedCount!=4||ReadLong(&unknown)
        ||InterlockedCompareExchangePointer((void* volatile*)&RizWinHttpActiveSlots,&resolvedTable,(void*)&initialTable)!=&initialTable){
        Poison(RizWinHttpPublishRejected,ERROR_DLL_INIT_FAILED);return 0;
    }
    InterlockedIncrement(&publishCount);InterlockedOr(&retainedFlags,RizWinHttpPublished);
    information.phase=RizWinHttpPhaseComplete;
    if(InterlockedCompareExchange(&lifecycle,RizWinHttpReady,RizWinHttpResolving)!=RizWinHttpResolving
        ||ReadLong(&unknown)||ReadActive()!=&resolvedTable){Poison(RizWinHttpPublishRejected,ERROR_DLL_INIT_FAILED);return 0;}
    return 1;
}

uint32_t __cdecl RizWinHttpForward_GetVersion(void){return 1;}
uint32_t __cdecl RizWinHttpForward_GetInfo(RizWinHttpInfoV1* output)
{
    DWORD saved=GetLastError();uint32_t result=0;
    if(InterlockedCompareExchange(&gate,1,0)!=0){SetLastError(saved);return 2;}
    __try {
        if(!output||output->version!=1||output->byteSize!=sizeof(*output))result=1;
        else {
            CopyBytes(output,&information,sizeof(*output));output->version=1;output->byteSize=sizeof(*output);
            output->state=(uint32_t)ReadLong(&lifecycle);output->failure=(uint32_t)ReadLong(&failureCode);
            output->flags=(uint32_t)ReadLong(&retainedFlags);output->initializeAttempts=(uint32_t)ReadLong(&attempts);
            output->publishCount=(uint32_t)ReadLong(&publishCount);output->lastWin32Error=(uint32_t)ReadLong(&errorCode);
            output->unknown=(uint32_t)ReadLong(&unknown);output->selfModule=(uint64_t)(uintptr_t)&__ImageBase;
            output->activeTable=(uint64_t)(uintptr_t)ReadActive();output->initialTable=(uint64_t)(uintptr_t)&initialTable;
            output->poisonTable=(uint64_t)(uintptr_t)&poisonTable;output->resolvedTable=(uint64_t)(uintptr_t)&resolvedTable;
            if(output->activeTable==output->poisonTable){output->unknown=1;output->state=RizWinHttpUnknown;output->flags|=RizWinHttpPoisoned;}
        }
    } __except(EXCEPTION_EXECUTE_HANDLER){result=1;}
    InterlockedExchange(&gate,0);SetLastError(saved);return result;
}
uint32_t __cdecl RizWinHttpForward_Initialize(void)
{
    DWORD saved=GetLastError(),error;int okay=0;
    InterlockedIncrement(&attempts);
    if(InterlockedCompareExchange(&attempted,1,0)!=0){Poison(RizWinHttpRepeatedInitialize,ERROR_DLL_INIT_FAILED);SetLastError(ERROR_DLL_INIT_FAILED);return 1;}
    if(InterlockedCompareExchange(&gate,1,0)!=0){Poison(RizWinHttpBusyInitialize,ERROR_DLL_INIT_FAILED);SetLastError(ERROR_DLL_INIT_FAILED);return 2;}
    InterlockedCompareExchange(&lifecycle,RizWinHttpResolving,RizWinHttpUninitialized);
    __try {if(!ReadLong(&unknown))okay=Resolve();}
    __except(EXCEPTION_EXECUTE_HANDLER){Poison(RizWinHttpNativeException,GetExceptionCode());okay=0;}
    if(!okay){
        InterlockedCompareExchange(&lifecycle,RizWinHttpDisabledNoPublish,RizWinHttpResolving);
        if(ReadLong(&unknown)||ReadActive()==&poisonTable)InterlockedExchange(&lifecycle,RizWinHttpUnknown);
    }
    error=(DWORD)ReadLong(&errorCode);InterlockedExchange(&gate,0);
    SetLastError(okay?saved:(error?error:ERROR_DLL_INIT_FAILED));return okay?0:1;
}
#if defined(RIZ_WINHTTP_FIXTURE)
uint32_t __cdecl RizWinHttpForward_SetFault(uint32_t stage,uint32_t index)
{
    DWORD saved=GetLastError();uint32_t result=0;
    if(stage>RizWinHttpFaultBeforeName||index>=4
        ||((stage!=RizWinHttpFaultWrongName&&stage!=RizWinHttpFaultSelfAddress&&stage!=RizWinHttpFaultBeforeName)&&index!=0))return 1;
    if(InterlockedCompareExchange(&gate,1,0)!=0)return 2;
    if(faultSelected||ReadLong(&attempted))result=2;
    else {faultSelected=1;information.faultStage=stage;information.faultIndex=index;}
    InterlockedExchange(&gate,0);SetLastError(saved);return result;
}
#endif
