#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>
#include "../FirstInstallPlaybackAttestation.h"
#include "../FirstInstallProfile.h"
extern "C" bool __cdecl RizFirstInstallFixture_ProvePlaybackOriginal(unsigned,const void*,const void*,void*,RizFirstInstallPlaybackRangeV1*);
extern "C" bool __cdecl RizFirstInstallFixture_VerifyPlaybackOriginal(unsigned);
extern "C" int __cdecl RizFirstInstallPlayback_TraceOriginal(const void*,void*,unsigned);
extern "C" const unsigned char RizFirstInstallPlayback_TraceReturned;
namespace {
using Fn=int(__cdecl*)(void*);
unsigned checks{},failures{},bodyCalls{},entryCalls{},originalSeh{},machineContexts{},prepared{},committed{};
constexpr DWORD FaultCode=0xe0464950;
bool bodyFault{};
Fn currentOriginal{};
const RizFirstInstallPlaybackRangeV1* tracing{};
CONTEXT initial{};
bool tracedEntry{},tracedExit{};
unsigned traceContexts{};
void Check(bool value,const char* text,int line){++checks;if(!value){++failures;std::fprintf(stderr,"FAILED machine %d: %s\n",line,text);}}
#define CHECK(x) Check(!!(x),#x,__LINE__)
int __cdecl Body(){++bodyCalls;if(bodyFault)RaiseException(FaultCode,0,0,nullptr);return 37;}
int __cdecl Entry(void* self){++entryCalls;return currentOriginal(self);}
void Append(std::vector<unsigned char>& bytes,std::uint64_t value,unsigned count){for(unsigned i=0;i<count;++i)bytes.push_back(static_cast<unsigned char>(value>>(i*8)));}
DWORD64 Register(const CONTEXT& value,unsigned reg){switch(reg){case 3:return value.Rbx;case 5:return value.Rbp;case 6:return value.Rsi;case 7:return value.Rdi;case 12:return value.R12;case 13:return value.R13;case 14:return value.R14;case 15:return value.R15;default:return 0;}}
LONG CALLBACK Trace(EXCEPTION_POINTERS* pointers)
{
    if(!tracing||pointers->ExceptionRecord->ExceptionCode!=EXCEPTION_SINGLE_STEP)return EXCEPTION_CONTINUE_SEARCH;
    auto& actual=*pointers->ContextRecord;
    const auto begin=reinterpret_cast<DWORD64>(tracing->original);
    if(actual.Rip==begin){initial=actual;tracedEntry=true;CHECK(*reinterpret_cast<const void* const*>(actual.Rsp)==&RizFirstInstallPlayback_TraceReturned);}
    if(actual.Rip>=begin && actual.Rip<begin+tracing->originalByteCount){
        CHECK(tracedEntry);CONTEXT unwound=actual;DWORD64 imageBase{};
        auto* function=RtlLookupFunctionEntry(actual.Rip,&imageBase,nullptr);
        const auto* table=static_cast<const RUNTIME_FUNCTION*>(tracing->functionTable);
        CHECK(function && imageBase==begin && function>=table && function<table+tracing->functionCount);
        if(function){PVOID data{};DWORD64 establisher{};RtlVirtualUnwind(0,imageBase,actual.Rip,function,&unwound,&data,&establisher,nullptr);
            CHECK(unwound.Rip==reinterpret_cast<DWORD64>(&RizFirstInstallPlayback_TraceReturned) && unwound.Rsp==initial.Rsp+8);
            for(const auto reg:{3u,5u,6u,7u,12u,13u,14u,15u})CHECK(Register(unwound,reg)==Register(initial,reg));
            CHECK(std::memcmp(&unwound.Xmm6,&initial.Xmm6,10*sizeof(M128A))==0);}
        ++machineContexts;++traceContexts;actual.EFlags|=0x100u;return EXCEPTION_CONTINUE_EXECUTION;
    }
    if(actual.Rip==reinterpret_cast<DWORD64>(tracing->continuation)){
        actual.EFlags&=~0x100u;tracedExit=true;return EXCEPTION_CONTINUE_EXECUTION;
    }
    if(!tracedEntry){actual.EFlags|=0x100u;return EXCEPTION_CONTINUE_EXECUTION;}
    CHECK(false);actual.EFlags&=~0x100u;return EXCEPTION_CONTINUE_EXECUTION;
}
struct Target{unsigned char* code{};unsigned length{},moved{},extra{};};
constexpr unsigned Moved[28]{6,10,10,6,8,6,6,6,6,6,10,6,6,6,9,9,6,6,6,6,6,6,7,6,6,7,6,8};
Target MakeTarget(unsigned index)
{
    const auto* prefix=index==0?RizFirstInstallProfile::UnityPrefix:index==1?RizFirstInstallProfile::RuntimePrefix:RizFirstInstallProfile::Operations[index-2].original;
    const auto moved=Moved[index];
    unsigned delta=0;
    for(unsigned at=0;at<moved;){
        if(prefix[at]==0x48 && prefix[at+1]==0x89){at+=5;}
        else if(prefix[at]==0x48 && prefix[at+1]==0x83){delta+=prefix[at+3];at+=4;}
        else if(prefix[at]==0x48 && prefix[at+1]==0x8b){at+=prefix[at+2]==0xec?3:4;}
        else {at+=prefix[at]==0x40||prefix[at]==0x41?2:1;delta+=8;}
    }
    const auto extra=32u+((8u-(delta&15u))&15u);
    std::vector<unsigned char> bytes(prefix,prefix+moved);bytes.insert(bytes.end(),{0x48,0x83,0xec,static_cast<unsigned char>(extra)});
    bytes.insert(bytes.end(),{0x48,0xb8});Append(bytes,reinterpret_cast<std::uint64_t>(&Body),8);bytes.insert(bytes.end(),{0xff,0xd0});
    auto* code=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));CHECK(code);
    if(!code)ExitProcess(3);std::memcpy(code,bytes.data(),bytes.size());
    DWORD ignored{};CHECK(VirtualProtect(code,4096,PAGE_EXECUTE_READ,&ignored));
    return {code,static_cast<unsigned>(bytes.size()),moved,extra};
}
void FinishTarget(Target& target,const RizFirstInstallPlaybackRangeV1& proof)
{
    const auto* originalTable=static_cast<const RUNTIME_FUNCTION*>(proof.functionTable);
    const auto* last=reinterpret_cast<const unsigned char*>(proof.original)+originalTable[proof.functionCount-1].UnwindData;
    const auto rawSize=(4u+last[2]*2u+3u)&~3u;
    std::vector<unsigned char> full(last,last+rawSize);
    unsigned total=target.extra;
    bool allocated=false;
    for(unsigned at=4;at<4u+full[2]*2u;){
        const auto op=full[at+1]&15;
        if(op==4){WORD offset{};std::memcpy(&offset,full.data()+at+2,2);offset=static_cast<WORD>(offset+target.extra/8);std::memcpy(full.data()+at+2,&offset,2);at+=4;}
        else{CHECK(op==2);total+=((full[at+1]>>4)*8)+8;full[at+1]=static_cast<unsigned char>(((total-8)/8)<<4|2);allocated=true;at+=2;}
    }
    if(!allocated){const auto at=4u+full[2]*2u;full.resize((at+2+3)&~3u);full[at]=0;full[at+1]=static_cast<unsigned char>(((total-8)/8)<<4|2);++full[2];}
    std::vector<unsigned char> epilogue;
    for(unsigned at=4;at<4u+full[2]*2u;){
        const auto op=full[at+1]&15;
        if(op==4){const auto reg=full[at+1]>>4;WORD offset{};std::memcpy(&offset,full.data()+at+2,2);
            epilogue.push_back(reg>=8?0x4c:0x48);epilogue.push_back(0x8b);epilogue.push_back(static_cast<unsigned char>(0x84|((reg&7)<<3)));epilogue.push_back(0x24);Append(epilogue,offset*8u,4);at+=4;
        }else at+=2;
    }
    epilogue.insert(epilogue.end(),{0x48,0x83,0xc4,static_cast<unsigned char>(total),0xc3});
    DWORD old{},ignored{};CHECK(VirtualProtect(target.code,4096,PAGE_READWRITE,&old));
    std::memcpy(target.code+target.length,epilogue.data(),epilogue.size());target.length+=static_cast<unsigned>(epilogue.size());
    auto* table=reinterpret_cast<PRUNTIME_FUNCTION>(target.code+512);unsigned cursor=768;
    for(unsigned i=0;i<proof.functionCount;++i){
        const auto& source=originalTable[i];const auto* unwind=reinterpret_cast<const unsigned char*>(proof.original)+source.UnwindData;
        const auto size=(4u+unwind[2]*2u+3u)&~3u;
        table[i]={source.BeginAddress,i+1==proof.functionCount?target.moved+4:source.EndAddress,cursor};
        std::memcpy(target.code+cursor,unwind,size);cursor+=size;
    }
    table[proof.functionCount]={target.moved+4,target.length,cursor};std::memcpy(target.code+cursor,full.data(),full.size());
    CHECK(VirtualProtect(target.code,4096,PAGE_EXECUTE_READ,&ignored));CHECK(FlushInstructionCache(GetCurrentProcess(),target.code,4096));
    CHECK(RtlAddFunctionTable(table,proof.functionCount+1,reinterpret_cast<DWORD64>(target.code)));
}
__declspec(noinline) bool CatchOriginal(Fn target,void* self)
{
    __try{target(self);return false;}
    __except(GetExceptionCode()==FaultCode?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH){++originalSeh;return true;}
}
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=2)return 2;
    auto dobby=LoadLibraryW(argv[1]);CHECK(dobby);if(!dobby)return 3;
    const auto prepare=reinterpret_cast<int(__cdecl*)(void*,void*,void**)>(GetProcAddress(dobby,"DobbyPrepare"));
    const auto commit=reinterpret_cast<int(__cdecl*)(void*)>(GetProcAddress(dobby,"DobbyCommit"));CHECK(prepare&&commit);
    auto* handler=AddVectoredExceptionHandler(1,Trace);CHECK(handler);
    std::uint64_t input[8]{};input[2]=reinterpret_cast<std::uint64_t>(input);
    RizFirstInstallPlaybackRangeV1 proofs[28]{};
    for(unsigned index=0;index<28;++index){
        auto target=MakeTarget(index);void* raw{};
        CHECK(prepare(target.code,reinterpret_cast<void*>(&Entry),&raw)==0);++prepared;
        auto& proof=proofs[index];CHECK(RizFirstInstallFixture_ProvePlaybackOriginal(index,target.code,reinterpret_cast<void*>(&Entry),raw,&proof));
        if(!proof.original)continue;
        CHECK(proof.original!=raw && proof.movedByteCount==Moved[index]);CHECK(std::memcmp(proof.beforeBytes,target.code,proof.movedByteCount)==0);
        CHECK(proof.beforeBytes[proof.movedByteCount]==0xff && proof.beforeBytes[proof.movedByteCount+1]==0x25);
        CHECK(std::memcmp(proof.beforeBytes+proof.movedByteCount+6,&proof.continuation,8)==0);
        CHECK(std::memcmp(proof.original,proof.afterBytes,proof.originalByteCount)==0);
        CHECK(std::memcmp(raw,proof.beforeBytes,proof.originalByteCount)==0);
        FinishTarget(target,proof);currentOriginal=reinterpret_cast<Fn>(const_cast<void*>(proof.original));
        tracing=&proof;tracedEntry=tracedExit=false;traceContexts=0;
        CHECK(RizFirstInstallPlayback_TraceOriginal(proof.original,input,1)==37);tracing=nullptr;
        CHECK(tracedEntry&&tracedExit&&traceContexts==proof.functionCount+1);
        CHECK(commit(target.code)==0);++committed;DWORD ignored{};CHECK(VirtualProtect(target.code,4096,PAGE_EXECUTE_READ,&ignored));
        CHECK(RizFirstInstallPlayback_TraceOriginal(target.code,input,0)==37);
        bodyFault=true;CHECK(CatchOriginal(reinterpret_cast<Fn>(target.code),input));bodyFault=false;
        CHECK(RizFirstInstallPlayback_TraceOriginal(target.code,input,0)==37);CHECK(RizFirstInstallFixture_VerifyPlaybackOriginal(index));
        std::printf("shape=%u moved=%u machineContexts=%u raw=",index,proof.movedByteCount,traceContexts);
        for(unsigned at=0;at<proof.originalByteCount;++at)std::printf("%02x",proof.beforeBytes[at]);std::printf(" normalized=");
        for(unsigned at=0;at<proof.originalByteCount;++at)std::printf("%02x",proof.afterBytes[at]);std::printf("\n");
    }
    for(unsigned index=0;index<28;++index)CHECK(RizFirstInstallFixture_VerifyPlaybackOriginal(index));
    CHECK(RemoveVectoredExceptionHandler(handler));CHECK(prepared==28&&committed==28&&originalSeh==28&&bodyCalls==112&&entryCalls==84&&machineContexts==127);
    std::printf("{\"kind\":\"first-install-private-original-machine\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"actualPrepareCount\":%u,\"actualCommitCount\":%u,\"actualMachineUnwindContexts\":%u,\"actualOriginalSeh\":%u,\"originalBodyCalls\":%u,\"entryCalls\":%u,\"gameCodeExecuted\":false,\"productionOwnerIssued\":false}\n",failures?"false":"true",checks,failures,prepared,committed,machineContexts,originalSeh,bodyCalls,entryCalls);
    return failures?1:0;
}
