#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../../native/RizToolbox.JudgeContext/FirstInstallPlaybackAttestation.h"
#include "../../native/RizToolbox.JudgeContext/FirstInstallProfile.h"
extern "C" bool __cdecl RizFirstInstallFixture_ProvePlaybackOriginal(unsigned,const void*,const void*,void*,RizFirstInstallPlaybackRangeV1*);
extern "C" bool __cdecl RizFirstInstallFixture_VerifyPlaybackOriginal(unsigned);
extern "C" int __cdecl RizFirstInstallPlayback_TraceOriginal(const void*,void*,unsigned);
extern "C" const unsigned char RizFirstInstallPlayback_TraceReturned;
namespace {
unsigned checks{},failures{},prepared{},returned{},faults{};
const void* expectedOriginal{};
void Check(bool value,const char* text,int line){++checks;if(!value){++failures;std::fprintf(stderr,"FAILED relocated fault %d: %s\n",line,text);}}
#define CHECK(x) Check(!!(x),#x,__LINE__)
int __cdecl Entry(void*){return -1;}
LONG Filter(EXCEPTION_POINTERS* pointers)
{
    const auto& record=*pointers->ExceptionRecord;
    CHECK(record.ExceptionCode==EXCEPTION_ACCESS_VIOLATION);
    CHECK(record.ExceptionAddress==static_cast<const unsigned char*>(expectedOriginal)+4);
    CHECK(record.NumberParameters==2 && record.ExceptionInformation[0]==0 && record.ExceptionInformation[1]==0x10);
    CONTEXT unwound=*pointers->ContextRecord;const auto before=unwound;
    DWORD64 base{};auto* function=RtlLookupFunctionEntry(unwound.Rip,&base,nullptr);
    CHECK(function && base==reinterpret_cast<DWORD64>(expectedOriginal) && function->BeginAddress==4 && function->EndAddress==8);
    if(function){PVOID data{};DWORD64 frame{};RtlVirtualUnwind(0,base,unwound.Rip,function,&unwound,&data,&frame,nullptr);
        CHECK(unwound.Rip==reinterpret_cast<DWORD64>(&RizFirstInstallPlayback_TraceReturned) && unwound.Rsp==before.Rsp+48);
        CHECK(unwound.Rbx==before.Rbx && unwound.Rbp==before.Rbp && unwound.Rsi==before.Rsi && unwound.Rdi==before.Rdi
            && unwound.R12==before.R12 && unwound.R13==before.R13 && unwound.R14==before.R14 && unwound.R15==before.R15);
        CHECK(std::memcmp(&unwound.Xmm6,&before.Xmm6,10*sizeof(M128A))==0);}
    return record.ExceptionCode==EXCEPTION_ACCESS_VIOLATION?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH;
}
__declspec(noinline) void FaultInsideNormalized()
{
    __try{RizFirstInstallPlayback_TraceOriginal(expectedOriginal,nullptr,0);CHECK(false);}
    __except(Filter(GetExceptionInformation())){++faults;}
}
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=2)return 2;
    auto dobby=LoadLibraryW(argv[1]);CHECK(dobby);if(!dobby)return 3;
    auto prepare=reinterpret_cast<int(__cdecl*)(void*,void*,void**)>(GetProcAddress(dobby,"DobbyPrepare"));CHECK(prepare);
    auto* source=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));CHECK(source);if(!source)return 3;
    std::memcpy(source,RizFirstInstallProfile::Operation2Bytes,8);
    const unsigned char body[]{0xb8,37,0,0,0,0x48,0x83,0xc4,0x28,0xc3};std::memcpy(source+8,body,sizeof(body));
    DWORD ignored{};CHECK(VirtualProtect(source,4096,PAGE_EXECUTE_READ,&ignored));
    void* raw{};CHECK(prepare(source,reinterpret_cast<void*>(&Entry),&raw)==0);++prepared;
    RizFirstInstallPlaybackRangeV1 proof{};CHECK(RizFirstInstallFixture_ProvePlaybackOriginal(4,source,reinterpret_cast<void*>(&Entry),raw,&proof));
    CHECK(proof.original && proof.original!=raw && proof.movedByteCount==8);if(!proof.original)return 4;
    expectedOriginal=proof.original;std::uint64_t input[4]{};
    CHECK(RizFirstInstallPlayback_TraceOriginal(expectedOriginal,input,0)==37);++returned;
    FaultInsideNormalized();CHECK(faults==1);CHECK(RizFirstInstallFixture_VerifyPlaybackOriginal(4));
    std::printf("{\"kind\":\"first-install-private-normalized-read-fault\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"actualPrepareCount\":%u,\"normalOriginalReturns\":%u,\"actualFaultsInsideNormalized\":%u,\"faultOffset\":4,\"gameCodeExecuted\":false,\"productionOwnerIssued\":false}\n",failures?"false":"true",checks,failures,prepared,returned,faults);
    return failures?1:0;
}
