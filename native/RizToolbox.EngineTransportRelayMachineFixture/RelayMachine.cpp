#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <bcrypt.h>
#include <intrin.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include "../RizToolbox.EngineBootstrap/EngineTransportRelays.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
#include "../RizToolbox.JudgeContext/MidContext.h"
#pragma comment(lib,"bcrypt.lib")

extern "C" {
void RizEngineInlineProbe_Entry(); void RizEngineSchedulerRow_Entry(); void RizEngineWaitPhase_Entry();
void __cdecl RizEngineInlineProbe_Dispatch(const RizEventMidFrameV1*);
void __cdecl RizEngineSchedulerRow_Dispatch(const RizEventMidFrameV1*);
void __cdecl RizEngineWaitPhase_Dispatch(const RizEventMidFrameV1*);
void RtRelayMachineInvoke(void(*)()); void RtRelayMachineDisturb();
extern unsigned char RtRelayMachineInvokeReturn,RtRelayMachineTargetsBegin,RtRelayMachineTargetsEnd;
#define MACHINE_SHAPE(Name) void RtRelayMachineShape##Name(); void RtRelayMachineCleanup##Name();
MACHINE_SHAPE(Start) MACHINE_SHAPE(Drain) MACHINE_SHAPE(Yield) MACHINE_SHAPE(Detach)
MACHINE_SHAPE(Release) MACHINE_SHAPE(Dispatch) MACHINE_SHAPE(Batch)
#undef MACHINE_SHAPE
const RizEventMidFrameV1* RtRelayMachineInput{};
std::uint64_t RtRelayMachineTarget{},RtRelayMachineSourceRsp{},RtRelayMachineCallerRsp{},RtRelayMachineAfterNv[8]{};
__declspec(align(16)) RizEventMidXmmV1 RtRelayMachineCallerXmm[16]{};
}

namespace {
namespace R=RizEngineTransportRelays;namespace F=RizEngineTransportGameProfile;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;using Machine=RizEventMidFrameV1;
constexpr DWORD InputError=0x23456789;constexpr U64 Poison=0xbad0bad1bad2bad3ULL;
constexpr U64 Nv[]{0x1122334455667788ULL,0x2233445566778899ULL,0x33445566778899aaULL,0x445566778899aabbULL,
    0x5566778899aabbccULL,0x66778899aabbccddULL,0x778899aabbccddeeULL,0x8899aabbccddeeffULL};
constexpr unsigned NvGpr[]{3,5,6,7,12,13,14,15};
constexpr U32 StackBytes[]{0x168,0x38,0xb8,0x28,0x28,0x158,0x58};
constexpr U32 ControlOffset=6656,ControlBytes=256,PointLimit=2048;
constexpr U32 SourceRfCount=27;
U32 checks{},failures{},registeredTables{},deletedTables{},sourceTablesRegistered{},sourceTablesDeleted{},walks{},naturalTfResumptions{},sourcePatchContexts{};
DWORD mainThread{};bool diskUnchanged{},allPrivate=true,allSourceContinuations=true,allSourcePatches=true,allSyntheticPrimaries=true;
R::Plan plan{};alignas(16) Machine input{},outputs[2]{},lastCallState{};
alignas(16) U8 node[256]{},waitObject[256]{},auxiliary[512]{};
struct Data { U8 node[256],waitObject[256],auxiliary[512]; } dataAfter[2]{};
void Check(bool okay,const char* reason){++checks;if(!okay){++failures;std::fprintf(stderr,"RelayMachine failed: %s\n",reason);}}
void Must(bool okay,const char* reason){Check(okay,reason);if(!okay)throw std::runtime_error(reason);}
U32 Read32(const U8* p){U32 v{};for(U32 i=0;i<4;++i)v|=static_cast<U32>(p[i])<<(8*i);return v;}
void Put32(U8* p,U32 v){for(U32 i=0;i<4;++i)p[i]=static_cast<U8>(v>>(8*i));}
void Put64(U8* p,U64 v){for(U32 i=0;i<8;++i)p[i]=static_cast<U8>(v>>(8*i));}
U64 Address(const void* p){return reinterpret_cast<U64>(p);}
U64 Arithmetic(U64 flags){return flags&~U64{0x100};} // Only instrumented TF differs.
bool SameMachine(const Machine& a,const Machine& b){return Arithmetic(a.rflags)==Arithmetic(b.rflags)
    &&std::memcmp(a.gpr,b.gpr,sizeof(a.gpr))==0&&std::memcmp(a.xmm,b.xmm,sizeof(a.xmm))==0&&a.mxcsr==b.mxcsr&&a.fpcsr==b.fpcsr;}
void NvFrom(const CONTEXT& c,U64* values){values[0]=c.Rbx;values[1]=c.Rbp;values[2]=c.Rsi;values[3]=c.Rdi;
    values[4]=c.R12;values[5]=c.R13;values[6]=c.R14;values[7]=c.R15;}
Machine FromContext(const CONTEXT& c){Machine m{};m.rflags=c.EFlags;
    const U64 values[]{c.Rax,c.Rcx,c.Rdx,c.Rbx,c.Rsp,c.Rbp,c.Rsi,c.Rdi,c.R8,c.R9,c.R10,c.R11,c.R12,c.R13,c.R14,c.R15};
    std::memcpy(m.gpr,values,sizeof(values));std::memcpy(m.xmm,&c.Xmm0,sizeof(m.xmm));m.mxcsr=c.MxCsr;m.fpcsr=c.FltSave.ControlWord;return m;}
void (*const shapes[])(){RtRelayMachineShapeStart,RtRelayMachineShapeDrain,RtRelayMachineShapeYield,RtRelayMachineShapeDetach,
    RtRelayMachineShapeRelease,RtRelayMachineShapeDispatch,RtRelayMachineShapeBatch};
void (*const cleanup[])(){RtRelayMachineCleanupStart,RtRelayMachineCleanupDrain,RtRelayMachineCleanupYield,RtRelayMachineCleanupDetach,
    RtRelayMachineCleanupRelease,RtRelayMachineCleanupDispatch,RtRelayMachineCleanupBatch};
void (*const entries[])(){RizEngineInlineProbe_Entry,RizEngineSchedulerRow_Entry,RizEngineWaitPhase_Entry};
void (__cdecl*const dispatches[])(const Machine*){RizEngineInlineProbe_Dispatch,RizEngineSchedulerRow_Dispatch,RizEngineWaitPhase_Dispatch};
struct Range {U64 begin{},end{};};Range entryRanges[3]{};
struct Point {U32 site{},branch{},variant{},offset{},source{};bool passed{},nv{},xmm{},sourceRfMatched{};U64 pc{},rsp{},expectedRip{},expectedRsp{},afterRip{},afterRsp{},imageBase{},functionAddress{};U32 functionBegin{},functionEnd{},functionUnwind{};U64 restoredNv[8]{};};
Point points[PointLimit]{};U32 pointCount{};
struct Route {
    U32 site{},branch{},shape{},family{},variant{},beforeFailures{},pointFirst{},pointCount{};
    U64 code{},control{},controlSize{},landing{},landingReturn{},landingRva{},sourceRsp[2]{},callerRsp[2]{},sourcePatchPc{},sourcePatchTargets[2]{};
    std::vector<U32> required[2];U32 observed[2]{},probeCount[2]{},landingCount[2]{};
    U32 sourceRfLookups{},sourcePatchCount[2]{};bool sourceContinuationsVerified{},sourcePatchUnwind[2]{};
    bool trace{},stopped{},returned{},callStateValid{},returnUnwind{},poison{},stateEqual{},memoryEqual{},lastError{},passed{};
};
std::vector<Route> routes;Route* current{};U32 steps{};
bool IsBranch(U32 site){return site==3||site==6||site==9||site==11||site==14||site==17||site==19||site==21;}
U32 Shape(U32 site){if(site<=2||(site>=14&&site<=16))return 0;if(site<=4)return 1;if(site<=8)return 2;
    if(site<=10)return 3;if(site<=13)return 4;if(site<=20)return 5;return 6;}
bool Unwind(CONTEXT* c,PRUNTIME_FUNCTION function,U64 base){bool okay=false;
    __try{PVOID data{};DWORD64 establisher{};RtlVirtualUnwind(UNW_FLAG_NHANDLER,base,c->Rip,function,c,&data,&establisher,nullptr);okay=true;}
    __except(EXCEPTION_EXECUTE_HANDLER){okay=false;}return okay;}
void RecordPoint(const CONTEXT& actual,U32 offset,U32 source){
    if(pointCount==PointLimit){Check(false,"Actual CONTEXT receipt capacity");return;}
    auto& p=points[pointCount++];p.site=current->site;p.branch=current->branch;p.variant=current->variant;p.offset=offset;p.source=source;
    p.pc=actual.Rip;p.rsp=actual.Rsp;p.expectedRip=Address(&RtRelayMachineInvokeReturn);p.expectedRsp=RtRelayMachineCallerRsp;
    CONTEXT copy=actual;DWORD64 base{};const auto* function=RtlLookupFunctionEntry(actual.Rip,&base,nullptr);
    bool okay=function&&base==plan.fixedImageBase&&Unwind(&copy,const_cast<PRUNTIME_FUNCTION>(function),base);
    p.imageBase=base;p.functionAddress=Address(function);
    if(function){p.functionBegin=function->BeginAddress;p.functionEnd=function->EndAddress;p.functionUnwind=function->UnwindData;}
    if(source==3){const auto& expected=F::PhysicalWindows[F::Sites[current->site].physicalWindowIndex].runtimeFunction;
        p.sourceRfMatched=function&&base==plan.fixedImageBase&&function->BeginAddress==expected.beginRva
            &&function->EndAddress==expected.endRva&&function->UnwindData==expected.unwindRva;
        okay=okay&&p.sourceRfMatched&&actual.Rip==current->sourcePatchPc&&actual.Rsp==RtRelayMachineSourceRsp;
        Check(p.sourceRfMatched,"Actual source-patch CONTEXT resolves the original registered source RF, not a near RF");}
    p.afterRip=copy.Rip;p.afterRsp=copy.Rsp;NvFrom(copy,p.restoredNv);p.nv=std::memcmp(p.restoredNv,Nv,sizeof(Nv))==0;
    p.xmm=std::memcmp(&copy.Xmm6,RtRelayMachineCallerXmm+6,10*sizeof(RizEventMidXmmV1))==0;
    p.passed=okay&&p.afterRip==p.expectedRip&&p.afterRsp==p.expectedRsp&&p.nv&&p.xmm;
    Check(p.passed,"Actual relay/control machine CONTEXT unwinds to exact caller and nonvolatiles");
    if(current->shape==5){const auto* home=reinterpret_cast<const U64*>(RtRelayMachineSourceRsp+0x168);Check(*home==Poison,"Dispatch late RBX home is actually poisoned and was never saved");current->poison=p.nv&&*home==Poison;}
    if(source==1)++current->observed[current->variant];
    if(source==3){++current->sourcePatchCount[current->variant];++sourcePatchContexts;
        current->sourcePatchUnwind[current->variant]=p.passed;allSourcePatches=allSourcePatches&&p.passed;}
    if(current->variant==1&&actual.Rip==plan.sites[current->site].relayReturnPc){current->returnUnwind=p.passed;}
}
LONG CALLBACK Trace(EXCEPTION_POINTERS* exception){
    const DWORD incoming=GetLastError();
    if(!exception||exception->ExceptionRecord->ExceptionCode!=EXCEPTION_SINGLE_STEP||!current||!current->trace||GetCurrentThreadId()!=mainThread){SetLastError(incoming);return EXCEPTION_CONTINUE_SEARCH;}
    auto* c=exception->ContextRecord;const U64 pc=c->Rip;++steps;
    if(pc==current->landing){current->trace=false;current->stopped=true;c->EFlags&=~0x100u;SetLastError(incoming);return EXCEPTION_CONTINUE_EXECUTION;}
    if(pc==current->sourcePatchPc){
        const auto* instruction=reinterpret_cast<const U8*>(pc);const auto displacement=static_cast<std::int32_t>(Read32(instruction+1));
        const U64 target=displacement<0?pc+5-static_cast<U64>(-static_cast<std::int64_t>(displacement)):pc+5+static_cast<U32>(displacement);
        const bool exact=instruction[0]==0xe9&&target==current->sourcePatchTargets[current->variant]
            &&current->sourcePatchCount[current->variant]==0;
        Check(exact,"TF permits exactly the one owned source E9 and its exact variant destination");
        if(!exact){SetLastError(incoming);return EXCEPTION_CONTINUE_SEARCH;}
        RecordPoint(*c,0,3);c->EFlags|=0x100u;SetLastError(incoming);return EXCEPTION_CONTINUE_EXECUTION;
    }
    // A real Entry PUSHFQ has already saved TF=1 before its CALL gets here.
    // Suppress stepping only in fixture C++; the unchanged actual Entry POPFQ
    // naturally re-arms TF on return. No return slot/debug register is edited.
    if(pc==Address(reinterpret_cast<const void*>(dispatches[current->family]))){c->EFlags&=~0x100u;SetLastError(incoming);return EXCEPTION_CONTINUE_EXECUTION;}
    const U64 begin=current->variant?current->code:current->control;
    const U64 bytes=current->variant?plan.sites[current->site].codeBytes:current->controlSize;
    bool allowed=pc>=begin&&pc<begin+bytes;
    if(allowed){const U32 offset=static_cast<U32>(pc-begin);const auto& required=current->required[current->variant];
        const U32 observed=current->observed[current->variant];
        Check(observed<required.size()&&required[observed]==offset,"Actual TF order equals every required instruction on this exact branch");
        RecordPoint(*c,offset,1);
        if(current->variant&&pc==plan.sites[current->site].relayReturnPc-6){lastCallState=FromContext(*c);current->callStateValid=true;}
    }
    if(pc>=Address(&RtRelayMachineTargetsBegin)&&pc<Address(&RtRelayMachineTargetsEnd))allowed=true;
    for(const auto& r:entryRanges)if(pc>=r.begin&&pc<r.end){allowed=true;if(pc==r.begin)Check((c->Rsp&15)==8,"Real probe CALL has Windows x64 entry alignment");}
    if(!allowed||steps>2048){Check(false,"TF refuses every original game byte and every unexpected code path");SetLastError(incoming);return EXCEPTION_CONTINUE_SEARCH;}
    c->EFlags|=0x100u;SetLastError(incoming);return EXCEPTION_CONTINUE_EXECUTION;
}

void WalkFromProbe(){
    CONTEXT c{};RtlCaptureContext(&c);bool reached=false;
    for(U32 depth=0;depth<24&&c.Rip;++depth){
        if(c.Rip==plan.sites[current->site].relayReturnPc){Check(c.Rsp==RtRelayMachineSourceRsp,"Actual Entry unwind reaches actual relay return PC/source RSP");RecordPoint(c,static_cast<U32>(c.Rip-current->code),2);reached=true;break;}
        DWORD64 base{};auto* function=RtlLookupFunctionEntry(c.Rip,&base,nullptr);
        if(function){if(!Unwind(&c,function,base))break;}else{c.Rip=*reinterpret_cast<const U64*>(c.Rsp);c.Rsp+=8;}
    }
    Check(reached,"Actual C++ callback stack walks through real Entry to its relay CALL return PC");if(reached)++walks;
}
void Observe(const Machine* frame,U32 family){
    const DWORD incoming=GetLastError();
    Check(current&&frame&&frame->abiVersion==1&&frame->byteSize==sizeof(Machine)&&family==current->family,"Actual fixed MidEntry delivered its immutable machine ABI");
    if(!current||!frame){SetLastError(incoming);return;}
    Check(frame->entryRsp+8==frame->gpr[RizEventMidRsp]&&frame->gpr[RizEventMidRsp]==RtRelayMachineSourceRsp,
        "Actual saved CALL slot and source RSP are exact");
    if(frame->returnPc==current->landingReturn){outputs[current->variant]=*frame;++current->landingCount[current->variant];}
    else{
        Check(current->variant==1&&frame->returnPc==plan.sites[current->site].relayReturnPc,"Only actual relay CALLs may be probes");
        ++current->probeCount[current->variant];Check(current->callStateValid&&SameMachine(*frame,lastCallState),"Actual MidEntry frame exactly preserves pre-CALL registers/XMM/flags/controls");
        Check((frame->rflags&0x100)!=0,"Actual Entry saved TF before C++ stepping was suspended");++naturalTfResumptions;WalkFromProbe();
    }
    RtRelayMachineDisturb();SetLastError(incoming);
}
}

extern "C" __declspec(noinline) void __cdecl RizEngineInlineProbe_Dispatch(const Machine* frame){Observe(frame,0);}
extern "C" __declspec(noinline) void __cdecl RizEngineSchedulerRow_Dispatch(const Machine* frame){Observe(frame,1);}
extern "C" __declspec(noinline) void __cdecl RizEngineWaitPhase_Dispatch(const Machine* frame){Observe(frame,2);}

namespace {
std::vector<U8> Read(const wchar_t* path){const HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    Must(file!=INVALID_HANDLE_VALUE,"Read-only actual fixed disk open");LARGE_INTEGER size{};const bool sized=GetFileSizeEx(file,&size)!=0&&size.QuadPart==static_cast<LONGLONG>(F::UnityFileBytes);
    if(!sized){CloseHandle(file);Must(false,"Exact pinned disk size");}std::vector<U8> bytes(static_cast<std::size_t>(size.QuadPart));DWORD total{};
    const bool okay=ReadFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&total,nullptr)!=0&&total==bytes.size();const bool closed=CloseHandle(file)!=0;Must(okay&&closed,"Read and close full pinned disk input");return bytes;}
std::string Hash(const std::vector<U8>& bytes){BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};DWORD length{},got{};std::vector<U8> object;U8 result[32]{};
    bool okay=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
    if(okay)okay=BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&length),sizeof(length),&got,0)>=0&&got==sizeof(length);
    if(okay){object.resize(length);okay=BCryptCreateHash(algorithm,&hash,object.data(),length,nullptr,0,0)>=0;}
    if(okay)okay=BCryptHashData(hash,const_cast<PUCHAR>(bytes.data()),static_cast<ULONG>(bytes.size()),0)>=0;
    if(okay)okay=BCryptFinishHash(hash,result,sizeof(result),0)>=0;
    if(hash)okay=BCryptDestroyHash(hash)>=0&&okay;if(algorithm)okay=BCryptCloseAlgorithmProvider(algorithm,0)>=0&&okay;
    Must(okay,"Actual pinned file SHA256");std::ostringstream out;for(U8 byte:result)out<<std::hex<<std::setw(2)<<std::setfill('0')<<static_cast<unsigned>(byte);return out.str();}
template<unsigned Index> __declspec(noinline) U64 OwnedTyped(U64 a,U64 b,U64 c,U64 d){return a^b^c^d^(0x41420000u+Index);}
struct Mapping {
    U8* image{};U8* arena{};PRUNTIME_FUNCTION sourceFunctions{};bool registered{},sourceRegistered{};U8 ownedLanding[48]{};std::vector<U8> original;
    void Protect(void* p,SIZE_T n,DWORD value){DWORD old{};Must(VirtualProtect(p,n,value,&old)!=0,"Actual owned page protection");}
    void VerifyPages(U64 begin,U64 bytes,DWORD normalProtection,U64 executableBegin=0,U64 executableBytes=0,U64 secondExecutableBegin=0,U64 secondExecutableBytes=0){
        const U64 end=begin+bytes;bool okay=end>begin;
        if(executableBytes&&secondExecutableBytes&&secondExecutableBegin<=executableBegin+executableBytes
            &&executableBegin<=secondExecutableBegin+secondExecutableBytes){
            const U64 combinedBegin=executableBegin<secondExecutableBegin?executableBegin:secondExecutableBegin;
            const U64 combinedEnd=executableBegin+executableBytes>secondExecutableBegin+secondExecutableBytes
                ?executableBegin+executableBytes:secondExecutableBegin+secondExecutableBytes;
            executableBegin=combinedBegin;executableBytes=combinedEnd-combinedBegin;secondExecutableBytes=0;}
        for(U64 cursor=begin;okay&&cursor<end;){MEMORY_BASIC_INFORMATION info{};
            if(VirtualQuery(reinterpret_cast<const void*>(cursor),&info,sizeof(info))!=sizeof(info)){okay=false;break;}
            const U64 regionEnd=Address(info.BaseAddress)+info.RegionSize;
            okay=info.State==MEM_COMMIT&&info.Type==MEM_PRIVATE&&info.AllocationBase==image&&regionEnd>cursor;
            const U64 partEnd=regionEnd<end?regionEnd:end;
            const bool executable=(executableBytes&&cursor>=executableBegin&&partEnd<=executableBegin+executableBytes)
                ||(secondExecutableBytes&&cursor>=secondExecutableBegin&&partEnd<=secondExecutableBegin+secondExecutableBytes);
            okay=okay&&info.Protect==(executable?PAGE_EXECUTE_READ:normalProtection);
            cursor=partEnd;
        }
        allPrivate=allPrivate&&okay;Must(okay,"Actual VirtualQuery proves private allocation identity and exact page protections");
    }
    void Create(const std::vector<U8>& disk){image=static_cast<U8*>(VirtualAlloc(nullptr,F::ImageSize+0x10000ull,MEM_RESERVE,PAGE_NOACCESS));Must(image!=nullptr,"Reserve owned image and near arena");
        Must(VirtualAlloc(image,F::ImageSize,MEM_COMMIT,PAGE_READWRITE)==image,"Commit data-only fixed image copy");
        arena=reinterpret_cast<U8*>((Address(image)+F::ImageSize+0xffff)&~U64{0xffff});Must(VirtualAlloc(arena,8192,MEM_COMMIT,PAGE_READWRITE)==arena,"Commit near arena as RW");
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(disk.data());const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(disk.data()+dos->e_lfanew);
        std::memcpy(image,disk.data(),nt->OptionalHeader.SizeOfHeaders);const auto* sections=IMAGE_FIRST_SECTION(nt);
        for(WORD i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& s=sections[i];Must(static_cast<U64>(s.PointerToRawData)+s.SizeOfRawData<=disk.size()&&static_cast<U64>(s.VirtualAddress)+s.SizeOfRawData<=F::ImageSize,"Owned section copy bounds");std::memcpy(image+s.VirtualAddress,disk.data()+s.PointerToRawData,s.SizeOfRawData);}
        original.assign(image,image+F::ImageSize);Protect(image,F::ImageSize,PAGE_READONLY);
        VerifyPages(Address(image),F::ImageSize,PAGE_READONLY);VerifyPages(Address(arena),8192,PAGE_READWRITE);
        R::Request request{};request.version=1;request.byteSize=static_cast<U32>(sizeof(request));request.fixedImageBase=Address(image);request.fixedImageBytes=F::ImageSize;request.nearRw=Address(arena);request.nearCapacity=8192;
        const void* typed[]{reinterpret_cast<void*>(&OwnedTyped<0>),reinterpret_cast<void*>(&OwnedTyped<1>),reinterpret_cast<void*>(&OwnedTyped<2>),reinterpret_cast<void*>(&OwnedTyped<3>),reinterpret_cast<void*>(&OwnedTyped<4>),reinterpret_cast<void*>(&OwnedTyped<5>),reinterpret_cast<void*>(&OwnedTyped<6>),reinterpret_cast<void*>(&OwnedTyped<7>),reinterpret_cast<void*>(&OwnedTyped<8>),reinterpret_cast<void*>(&OwnedTyped<9>),reinterpret_cast<void*>(&OwnedTyped<10>),reinterpret_cast<void*>(&OwnedTyped<11>),reinterpret_cast<void*>(&OwnedTyped<12>)};
        for(U32 i=0;i<13;++i)request.typedEntries[i]=Address(typed[i]);request.inlineEntry=Address(reinterpret_cast<void*>(entries[0]));request.rowEntry=Address(reinterpret_cast<void*>(entries[1]));request.waitPhaseEntry=Address(reinterpret_cast<void*>(entries[2]));
        Must(R::Prepare(&request,&plan)==R::Success,"Actual canonical 23-relay preparation from fixed source facts");
        U32 partialCount{};bool partialOkay=true;constexpr U8 partial[12]{0x01,0x00,0x03,0x00,0x00,0x01,0x2a,0x00,0x00,0x70,0x00,0x00};
        for(U32 i=0;i<R::SiteCount;++i){const auto& site=plan.sites[i];const auto& source=F::PhysicalWindows[site.windowIndex].runtimeFunction;
            const auto* outer=reinterpret_cast<const U8*>(site.unwind);bool okay=site.unwindBytes==16&&outer[0]==0x21&&outer[1]==0&&outer[2]==0&&outer[3]==0
                &&Read32(outer+4)==source.beginRva&&Read32(outer+8)==source.endRva;
            if(i>=17&&i<=20){++partialCount;okay=okay&&Read32(outer+12)==site.toImageStart+R::DispatchPrimaryUnwindOffset
                &&std::memcmp(reinterpret_cast<const void*>(site.relay+R::DispatchPrimaryUnwindOffset),partial,sizeof(partial))==0;
                Check(okay,"Dispatch outer CHAININFO points to its explicit source-identity synthetic partial primary");}
            else{okay=okay&&Read32(outer+12)==source.unwindRva;Check(okay,"Other relay outer CHAININFO retains its actual source unwind parent");}
            partialOkay=partialOkay&&okay;}
        Check(partialCount==4&&partialOkay,"Exactly four explicit synthetic partial primaries preserve source identity without late RBX save");
        allSyntheticPrimaries=allSyntheticPrimaries&&partialCount==4&&partialOkay;
    }
    bool LookupSource(U32 rva){
        PRUNTIME_FUNCTION expected{};
        for(U32 i=0;i<SourceRfCount;++i)if(rva>=sourceFunctions[i].BeginAddress&&rva<sourceFunctions[i].EndAddress){
            if(expected)return false;expected=sourceFunctions+i;}
        DWORD64 base{};const auto* actual=RtlLookupFunctionEntry(Address(image)+rva,&base,nullptr);
        return expected&&actual==expected&&base==Address(image)
            &&actual->BeginAddress==expected->BeginAddress&&actual->EndAddress==expected->EndAddress&&actual->UnwindData==expected->UnwindData;
    }
    void RegisterSources(Route& route){
        // A private PE copy is not an OS-loaded MEM_IMAGE. Register its actual
        // source RFs too: the Windows E9 epilogue test follows both functions'
        // primary CHAININFO identities through RtlLookupFunctionEntry.
        sourceFunctions=static_cast<PRUNTIME_FUNCTION>(VirtualAlloc(nullptr,SourceRfCount*sizeof(RUNTIME_FUNCTION),MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        Must(sourceFunctions!=nullptr,"Allocate stable owned source-RF table storage");
        U32 count{};for(U32 f=0;f<F::TypedCount;++f)for(U32 n=0;n<F::Functions[f].runtimeFunctionCount;++n){
            Must(count<SourceRfCount,"Fixed source-RF flattening stays within exact capacity");
            const auto& rf=F::Functions[f].runtimeFunctions[n];sourceFunctions[count++]={rf.beginRva,rf.endRva,rf.unwindRva};}
        Must(count==SourceRfCount,"Exactly 27 actual source RFs are present");
        for(U32 i=1;i<count;++i){const RUNTIME_FUNCTION value=sourceFunctions[i];U32 at=i;
            while(at&&sourceFunctions[at-1].BeginAddress>value.BeginAddress){sourceFunctions[at]=sourceFunctions[at-1];--at;}sourceFunctions[at]=value;}
        for(U32 i=0;i<count;++i)Must(sourceFunctions[i].BeginAddress<sourceFunctions[i].EndAddress
            &&(i==0||sourceFunctions[i-1].EndAddress<=sourceFunctions[i].BeginAddress),"Owned source table is sorted with nonoverlapping exact RF ranges");
        Protect(sourceFunctions,count*sizeof(RUNTIME_FUNCTION),PAGE_READONLY);MEMORY_BASIC_INFORMATION memory{};
        Must(VirtualQuery(sourceFunctions,&memory,sizeof(memory))==sizeof(memory)&&memory.AllocationBase==sourceFunctions
            &&memory.Type==MEM_PRIVATE&&memory.State==MEM_COMMIT&&memory.Protect==PAGE_READONLY,"Actual source table has stable private read-only storage");
        Must(RtlAddFunctionTable(sourceFunctions,count,Address(image))!=FALSE,"Register actual source function metadata for the private copied image");
        sourceRegistered=true;++sourceTablesRegistered;bool okay=true;
        for(U32 i=0;i<count;++i){const bool found=LookupSource(sourceFunctions[i].BeginAddress)&&LookupSource(sourceFunctions[i].EndAddress-1);
            Check(found,"Actual source RF start/end lookups resolve the registered source table and image base");okay=okay&&found;route.sourceRfLookups+=2;}
        for(U32 i=0;i<R::SiteCount;++i){const auto& site=F::Sites[i];const bool continuation=LookupSource(site.physicalContinuationRva);
            Check(continuation,"Every original source continuation has an actual registered RF");okay=okay&&continuation;++route.sourceRfLookups;
            if(site.branchTargetRva){const bool branch=LookupSource(site.branchTargetRva);Check(branch,"Every original source branch target has an actual registered RF");okay=okay&&branch;++route.sourceRfLookups;}}
        for(const auto& bypass:F::ConditionalBypasses){const bool zero=LookupSource(bypass.zeroContinuationRva);
            Check(zero,"Every expanded zero-path continuation has an actual registered RF");okay=okay&&zero;++route.sourceRfLookups;}
        route.sourceContinuationsVerified=okay;allSourceContinuations=allSourceContinuations&&okay;
    }
    void Finish(Route& route,const U8* control,U32 controlSize){
        std::memcpy(arena+ControlOffset,control,controlSize);route.control=Address(arena)+ControlOffset;route.controlSize=controlSize;
        auto* table=reinterpret_cast<RUNTIME_FUNCTION*>(arena);table[23]={static_cast<DWORD>(route.control-Address(image)),static_cast<DWORD>(route.control+controlSize-Address(image)),plan.sites[route.site].runtimeFunction.unwindRva};
        U8 landing[48]{};std::memset(landing,0x90,sizeof(landing));landing[0]=0xff;landing[1]=0x15;Put32(landing+2,26);landing[6]=0xff;landing[7]=0x25;Put32(landing+8,28);
        Put64(landing+32,Address(reinterpret_cast<void*>(entries[route.family])));Put64(landing+40,Address(reinterpret_cast<void*>(cleanup[route.shape])));
        std::memcpy(ownedLanding,landing,sizeof(landing));
        auto* destination=image+route.landingRva;Protect(destination,sizeof(landing),PAGE_READWRITE);std::memcpy(destination,landing,sizeof(landing));Protect(destination,sizeof(landing),PAGE_EXECUTE_READ);
        route.landing=Address(destination);route.landingReturn=route.landing+6;
        auto expected=original;std::memcpy(expected.data()+route.landingRva,landing,sizeof(landing));Must(std::memcmp(image,expected.data(),F::ImageSize)==0,"Only explicitly owned landing bytes modify the private source copy");
        Protect(arena,8192,PAGE_EXECUTE_READ);Must(FlushInstructionCache(GetCurrentProcess(),arena,8192)!=FALSE&&FlushInstructionCache(GetCurrentProcess(),destination,sizeof(landing))!=FALSE,"Flush only owned relay/control/landing bytes");
        SYSTEM_INFO system{};GetSystemInfo(&system);const U64 pageBytes=system.dwPageSize;
        Must(pageBytes&&(pageBytes&(pageBytes-1))==0,"Actual system page size is a power of two");
        const U64 firstPage=Address(destination)&~(pageBytes-1),lastPage=(Address(destination)+sizeof(landing)+pageBytes-1)&~(pageBytes-1);
        VerifyPages(Address(image),F::ImageSize,PAGE_READONLY,firstPage,lastPage-firstPage);VerifyPages(Address(arena),8192,PAGE_EXECUTE_READ);
        RegisterSources(route);
        Must(RtlAddFunctionTable(table,24,Address(image))!=FALSE,"Register actual nineteen chain/four partial relay RFs and owned control RF");registered=true;++registeredTables;
        // Both tables must remain discoverable after the near table is added.
        const bool sourceStillFound=route.landingRva<=0xffffffffull&&LookupSource(static_cast<U32>(route.landingRva));Check(sourceStillFound,"Actual source continuation lookup survives registration of the near table");
        route.sourceContinuationsVerified=route.sourceContinuationsVerified&&sourceStillFound;
        allSourceContinuations=allSourceContinuations&&sourceStillFound;++route.sourceRfLookups;
    }
    void SourcePatch(Route& route,U32 variant){
        const U64 source=plan.sites[route.site].physicalPatchPc,target=variant?route.code:route.control;
        Must(source==Address(image)+F::Sites[route.site].patchRva&&source+5<=Address(image)+F::ImageSize
            &&(source+5<=route.landing||source>=route.landing+sizeof(ownedLanding)),"Owned source-patch and landing ranges are exact, bounded and disjoint");
        const auto delta=static_cast<std::int64_t>(target)-static_cast<std::int64_t>(source+5);
        Must(delta>=INT32_MIN&&delta<=INT32_MAX,"Actual source E9 reaches the selected control/relay within rel32");
        U8 patch[5]{0xe9};Put32(patch+1,static_cast<U32>(static_cast<std::int32_t>(delta)));
        Protect(reinterpret_cast<void*>(source),sizeof(patch),PAGE_READWRITE);std::memcpy(reinterpret_cast<void*>(source),patch,sizeof(patch));
        Protect(reinterpret_cast<void*>(source),sizeof(patch),PAGE_EXECUTE_READ);
        Must(FlushInstructionCache(GetCurrentProcess(),reinterpret_cast<void*>(source),sizeof(patch))!=FALSE,"Flush only the actual owned five-byte source E9 patch");
        auto expected=original;std::memcpy(expected.data()+route.landingRva,ownedLanding,sizeof(ownedLanding));
        std::memcpy(expected.data()+F::Sites[route.site].patchRva,patch,sizeof(patch));
        Must(std::memcmp(image,expected.data(),F::ImageSize)==0,"Private source differs from pinned data only at the owned landing and source E9");
        SYSTEM_INFO system{};GetSystemInfo(&system);const U64 page=system.dwPageSize;
        const U64 landingPage=route.landing&~(page-1),landingEnd=(route.landing+sizeof(ownedLanding)+page-1)&~(page-1);
        const U64 patchPage=source&~(page-1),patchEnd=(source+sizeof(patch)+page-1)&~(page-1);
        VerifyPages(Address(image),F::ImageSize,PAGE_READONLY,landingPage,landingEnd-landingPage,patchPage,patchEnd-patchPage);
        VerifyPages(Address(arena),8192,PAGE_EXECUTE_READ);route.sourcePatchPc=source;route.sourcePatchTargets[variant]=target;
    }
    bool Remove(){if(registered){if(!RtlDeleteFunctionTable(reinterpret_cast<PRUNTIME_FUNCTION>(arena))){Check(false,"Retain allocation if owned near function-table removal fails");return false;}registered=false;++deletedTables;}
        if(sourceRegistered){if(!RtlDeleteFunctionTable(sourceFunctions)){Check(false,"Retain stable source-table storage and image when source-table removal fails");return false;}sourceRegistered=false;++sourceTablesDeleted;}return true;}
    ~Mapping(){if(Remove()){if(image)VirtualFree(image,0,MEM_RELEASE);if(sourceFunctions)VirtualFree(sourceFunctions,0,MEM_RELEASE);}}
};

// Independent executable control: the same fixed physical window, actual
// relocated RIP operands and branch destinations, with the probe CALL omitted.
// The same emitter also records every required live instruction offset, then
// compares that expected relay code with the independently prepared arena.
struct Emitter {
    U8 code[160]{};U64 base{};U32 cursor{};bool selected=true;std::vector<U32> required;
    void Mark(){if(selected)required.push_back(cursor);}
    void Byte(U8 b){Must(cursor<sizeof(code),"Owned control emission bound");code[cursor++]=b;}
    void Data(const U8* p,U32 n){for(U32 i=0;i<n;++i)Byte(p[i]);}
    U32 Relative(U64 target,U64 after){const auto delta=static_cast<std::int64_t>(target)-static_cast<std::int64_t>(after);Must(delta>=INT32_MIN&&delta<=INT32_MAX,"Actual near rel32 bound");return static_cast<U32>(static_cast<std::int32_t>(delta));}
    void Dword(U32 value){for(U32 i=0;i<4;++i)Byte(static_cast<U8>(value>>(8*i)));}
    void Ins(const U8* p,U32 n){Mark();Data(p,n);}
    void Jump(U64 target){Mark();const U32 d=Relative(target,base+cursor+5);Byte(0xe9);Dword(d);}
    void Zero(U64 target){Mark();const U32 d=Relative(target,base+cursor+6);Byte(0x0f);Byte(0x84);Dword(d);}
    void Probe(U64 literal,bool live){if(!live)return;Mark();const U32 d=Relative(literal,base+cursor+6);Byte(0xff);Byte(0x15);Dword(d);}
    void Rip(const U8* p,U64 source){Mark();const auto old=static_cast<std::int32_t>(Read32(p+3));const U64 target=old<0?source+7-static_cast<U64>(-static_cast<std::int64_t>(old)):source+7+static_cast<U32>(old);
        const U32 d=Relative(target,base+cursor+7);Data(p,3);Dword(d);}
};
Emitter Emit(Route& route,bool live){Emitter out;out.base=live?plan.sites[route.site].relay:plan.nearRw+ControlOffset;
    const auto& site=F::Sites[route.site];const auto& window=F::PhysicalWindows[site.physicalWindowIndex];const U8* bytes=window.bytes;
    const U64 continuation=plan.fixedImageBase+site.physicalContinuationRva,branch=plan.fixedImageBase+site.branchTargetRva;
    const bool zero=route.branch==0;
    if(site.expandedPhysicalBlock){const auto& bypass=F::ConditionalBypasses[route.site==9?0:route.site==11?1:2];
        if(route.site==9){out.Ins(bytes,4);out.Ins(bytes+4,3);}else out.Ins(bytes,3);
        out.Zero(out.base+112);out.selected=!zero;out.Probe(plan.sites[route.site].literal,live);
        if(route.site==9){out.Ins(bytes+9,3);out.Rip(bytes+12,plan.fixedImageBase+site.patchRva+12);}else{out.Ins(bytes+5,3);out.Ins(bytes+8,4);}
        out.Jump(continuation);while(out.cursor<112)out.Byte(0x90);out.selected=zero;
        if(bypass.commonRipMov)out.Rip(bypass.zeroReplay,plan.fixedImageBase+bypass.zeroReplayRva);else out.Ins(bypass.zeroReplay,bypass.zeroReplayBytes);
        out.Jump(plan.fixedImageBase+bypass.zeroContinuationRva);
    }else{
        out.Probe(plan.sites[route.site].literal,live);
        switch(route.site){
        case 1:case 2:case 8:out.Jump(branch);break;
        case 3:out.Ins(bytes,4);out.Zero(branch);out.selected=!zero;out.Jump(continuation);break;
        case 5:case 10:out.Ins(bytes,3);out.Rip(bytes+3,plan.fixedImageBase+site.patchRva+3);out.Jump(continuation);break;
        case 6:out.Zero(branch);out.selected=!zero;out.Ins(bytes+2,4);out.Jump(continuation);break;
        case 17:case 19:out.Ins(bytes,3);out.Zero(branch);out.selected=!zero;out.Jump(continuation);break;
        case 21:out.Ins(bytes,4);out.Zero(branch);out.selected=!zero;out.Jump(continuation);break;
        default:{const U32* lengths=nullptr;U32 count{};
            static constexpr U32 a[]{3,4},b[]{5},c[]{4,4},d[]{4,5},e[]{4,3},f[]{8},g[]{3,7};
            switch(route.site){case 0:lengths=a;count=2;break;case 4:lengths=b;count=1;break;case 7:case 12:case 13:case 15:lengths=c;count=2;break;
                case 16:lengths=d;count=2;break;case 18:lengths=e;count=2;break;case 20:lengths=f;count=1;break;case 22:lengths=g;count=2;break;default:Must(false,"Known exact replay instruction split");}
            U32 used{};for(U32 i=0;i<count;++i){out.Ins(bytes+used,lengths[i]);used+=lengths[i];}Must(used==window.byteCount,"Control consumes the entire exact physical source window");out.Jump(continuation);break;}
        }
    }
    return out;
}

void Inputs(U32 site,U32 branch){
    std::memset(node,0,sizeof(node));std::memset(waitObject,0,sizeof(waitObject));std::memset(auxiliary,0,sizeof(auxiliary));input=Machine{};
    input.abiVersion=1;input.byteSize=static_cast<U32>(sizeof(input));input.rflags=0xed7u|0x100u;input.mxcsr=0x3f80;input.fpcsr=0x077f;
    for(U32 i=0;i<16;++i){input.gpr[i]=0x93456789abcdef00ULL+i;input.xmm[i]=RtRelayMachineCallerXmm[i];}
    for(U32 i=0;i<8;++i)input.gpr[NvGpr[i]]=Nv[i];
    input.gpr[RizEventMidRax]=Address(waitObject);input.gpr[RizEventMidRcx]=Address(waitObject);input.gpr[RizEventMidRdx]=Address(node);
    const U32 shape=Shape(site);
    if(shape==0||shape==1||shape==2||shape==3||shape==4||shape==6)input.gpr[RizEventMidRbx]=Address(node);
    if(shape==0||shape==1||shape==2||shape==3||shape==6)input.gpr[RizEventMidRbp]=Address(auxiliary);
    if(shape==0||shape==1||shape==2||shape==3||shape==6)input.gpr[RizEventMidRsi]=Address(auxiliary);
    if(shape==0||shape==1||shape==2||shape==3||shape==4||shape==5||shape==6)input.gpr[RizEventMidRdi]=Address(waitObject);
    if(shape==0||shape==1||shape==2||shape==3||shape==6)input.gpr[RizEventMidR14]=Address(node);
    Put32(node+0x60,3);Put64(node+0x68,Address(waitObject));Put64(node+0x78,Address(waitObject));Put64(node+0x10,9);
    Put64(waitObject+0x20,Address(node));Put64(waitObject+0x10,0x738456129abcdef0ULL);Put32(waitObject+0xc,4);Put64(auxiliary+0x38,7);Put64(auxiliary+0x1a0,5);
    if(site==3)node[0x54]=branch?0x40:0;
    if(site==6){if(branch)input.rflags&=~U64{0x40};else input.rflags|=0x40;}
    if(site==9){input.gpr[RizEventMidRdi]=Address(node);Put64(node+0x78,branch?Address(waitObject):0);}
    if(site>=11&&site<=16){input.gpr[RizEventMidRdi]=0;if(site==11||site==14)input.gpr[RizEventMidRax]=branch?Address(waitObject):0;}
    if(site==17||site==19)input.gpr[RizEventMidRax]=branch?Address(node):0;
    if(site==18)input.gpr[RizEventMidRax]=Address(node);
    if(site==21)Put64(auxiliary+0x10,input.gpr[RizEventMidR14]+(branch?8:0));
    if(site==22)input.gpr[RizEventMidRdi]=3;
}
U32 ExpectedLanding(U32 site,U32 branch){const auto& fact=F::Sites[site];
    if(fact.expandedPhysicalBlock)return branch?fact.physicalContinuationRva:F::ConditionalBypasses[site==9?0:site==11?1:2].zeroContinuationRva;
    if(site==1||site==2||site==8)return fact.branchTargetRva;
    if(IsBranch(site)&&branch==0)return fact.branchTargetRva;return fact.physicalContinuationRva;}
void RunRoute(const std::vector<U8>& disk,U32 site,U32 branch){
    Route route;route.site=site;route.branch=branch;route.shape=Shape(site);route.family=F::Sites[site].family;route.beforeFailures=failures;route.pointFirst=pointCount;
    Mapping mapping;mapping.Create(disk);route.code=plan.sites[site].relay;route.landingRva=ExpectedLanding(site,branch);
    const auto live=Emit(route,true),control=Emit(route,false);route.required[0]=control.required;route.required[1]=live.required;
    Must(live.cursor==plan.sites[site].codeBytes&&std::memcmp(live.code,mapping.arena+plan.sites[site].relativeOffset,live.cursor)==0,"Independent exact instruction/control emitter matches actual prepared relay code");
    mapping.Finish(route,control.code,control.cursor);current=&route;
    for(U32 variant=0;variant<2;++variant){route.variant=variant;Inputs(site,branch);mapping.SourcePatch(route,variant);
        RtRelayMachineInput=&input;RtRelayMachineTarget=route.sourcePatchPc;
        route.trace=true;route.stopped=false;route.callStateValid=false;steps=0;SetLastError(InputError);RtRelayMachineInvoke(shapes[route.shape]);const DWORD last=GetLastError();
        route.sourceRsp[variant]=RtRelayMachineSourceRsp;route.callerRsp[variant]=RtRelayMachineCallerRsp;
        Check(route.stopped&&!route.trace&&route.landingCount[variant]==1,"Actual route completes exactly one owned landing");
        Check(route.observed[variant]==route.required[variant].size(),"Every required actual instruction CONTEXT was captured once");
        Check(route.sourcePatchCount[variant]==1&&route.sourcePatchUnwind[variant],"Each actual source E9 executes once and its real source-RF CONTEXT unwinds to the exact caller");
        Check(RtRelayMachineSourceRsp==RtRelayMachineCallerRsp-8-StackBytes[route.shape]&&(RtRelayMachineSourceRsp&15)==0,"Actual ASM source shape has its exact independent stack delta/alignment");
        Check(std::memcmp(RtRelayMachineAfterNv,Nv,sizeof(Nv))==0,"Real source cleanup restores independently seeded caller nonvolatiles");
        route.lastError=(variant?route.lastError:true)&&last==InputError;Check(last==InputError,"TF/actual MidEntry/landing preserve actual caller LastError");
        std::memcpy(dataAfter[variant].node,node,sizeof(node));std::memcpy(dataAfter[variant].waitObject,waitObject,sizeof(waitObject));std::memcpy(dataAfter[variant].auxiliary,auxiliary,sizeof(auxiliary));
    }
    const U32 expectedProbe=F::Sites[site].expandedPhysicalBlock&&branch==0?0u:1u;
    Check(route.probeCount[0]==0&&route.probeCount[1]==expectedProbe,"Expanded zero bypass skips actual probe while every other relay probes once");
    Check((expectedProbe?route.returnUnwind:!route.returnUnwind),"Only real probe routes prove actual CALL return-PC unwind");
    route.stateEqual=SameMachine(outputs[0],outputs[1]);route.memoryEqual=std::memcmp(&dataAfter[0],&dataAfter[1],sizeof(Data))==0;
    Check(route.stateEqual&&route.memoryEqual,"Actual control/relay GPR16 XMM16 all non-TF flags FP controls and replay writes agree");
    if(route.shape==5)Check(route.poison,"Four Dispatch source sites retain poisoned late RBX home and unwind live RBX");
    route.pointCount=pointCount-route.pointFirst;Check(mapping.Remove(),"Actual owned function tables are removed before allocations may be freed");
    route.passed=failures==route.beforeFailures;current=nullptr;routes.push_back(route);
}
std::string Escape(const std::string& value){std::string out;for(unsigned char c:value){if(c=='"'||c=='\\'){out+='\\';out+=static_cast<char>(c);}else out+=c<' '?'?':static_cast<char>(c);}return out;}
const char* Bool(bool value){return value?"true":"false";}
std::string Json(const std::string& diskHash,const std::string& fatal){
    const bool passed=failures==0&&routes.size()==31&&registeredTables==31&&deletedTables==registeredTables
        &&sourceTablesRegistered==31&&sourceTablesDeleted==sourceTablesRegistered&&allSourceContinuations
        &&sourcePatchContexts==62&&allSourcePatches&&allSyntheticPrimaries;
    std::ostringstream out;out<<"{\"schemaVersion\":1,\"kind\":\"engine-transport-relay-machine\",\"pid\":"<<GetCurrentProcessId()<<",\"passed\":"<<Bool(passed)<<",\"checks\":"<<checks<<",\"failures\":"<<failures
        <<",\"fixedDiskSha256\":\""<<diskHash<<"\",\"fatal\":\""<<Escape(fatal)<<"\",\"routeCount\":"<<routes.size()<<",\"controlPairs\":"<<routes.size()<<",\"siteCount\":23,\"chainSiteCount\":19,\"partialSiteCount\":4"
        <<",\"fixtureExecuted\":true,\"relayCodeExecuted\":true,\"actualMachineContexts\":true,\"actualReturnPcUnwind\":"<<Bool(passed&&walks==28)
        <<",\"dispatchPoisonVerified\":"<<Bool(passed)<<",\"expandedBypassVerified\":"<<Bool(passed)<<",\"allStateControlPairs\":"<<Bool(passed)<<",\"sourceDiskUnchanged\":"<<Bool(diskUnchanged)
        <<",\"privateMappingOnly\":"<<Bool(allPrivate)<<",\"mappingOwnedLandingModified\":true,\"planArenaExtendedForControl\":true,\"gameExecuted\":false,\"originalGameCodeExecuted\":false,\"installerExecuted\":false,\"productionAdmissionVerified\":false,\"kernelLedgerVerified\":false,\"exceptionPropagationVerified\":false"
        <<",\"sourcePatchExecuted\":"<<Bool(sourcePatchContexts==62&&allSourcePatches)<<",\"actualSourcePatchContextCount\":"<<sourcePatchContexts
        <<",\"mappingOwnedSourcePatchModified\":true,\"syntheticPartialPrimaryVerified\":"<<Bool(allSyntheticPrimaries)
        <<",\"cppDispatcherSingleStepped\":false,\"actualEntrySavedTfNaturallyRestored\":true,\"naturalTfResumptions\":"<<naturalTfResumptions<<",\"actualProbeWalks\":"<<walks<<",\"actualContextCount\":"<<pointCount
        <<",\"registeredTableCount\":"<<registeredTables<<",\"deletedTableCount\":"<<deletedTables
        <<",\"sourceTableRegistered\":"<<sourceTablesRegistered<<",\"sourceTableDeleted\":"<<sourceTablesDeleted
        <<",\"sourceRFCount\":"<<SourceRfCount<<",\"sourceContinuationsVerified\":"<<Bool(allSourceContinuations)<<",\"routes\":[";
    for(std::size_t n=0;n<routes.size();++n){const auto& r=routes[n];if(n)out<<',';const U32 expectedProbe=F::Sites[r.site].expandedPhysicalBlock&&r.branch==0?0u:1u;
        out<<"{\"site\":"<<r.site<<",\"branch\":"<<r.branch<<",\"name\":\""<<F::Sites[r.site].name<<"/"<<r.branch<<"\",\"passed\":"<<Bool(r.passed)
            <<",\"controlProbeCount\":"<<r.probeCount[0]<<",\"relayProbeCount\":"<<r.probeCount[1]<<",\"expectedProbeCount\":"<<expectedProbe<<",\"actualContextCount\":"<<r.pointCount
            <<",\"returnPcUnwind\":"<<Bool(r.returnUnwind)<<",\"poisonVerified\":"<<Bool(r.poison)<<",\"stateEqual\":"<<Bool(r.stateEqual)<<",\"memoryEqual\":"<<Bool(r.memoryEqual)
            <<",\"lastErrorPreserved\":"<<Bool(r.lastError)<<",\"controlComplete\":"<<Bool(r.landingCount[0]==1)<<",\"relayComplete\":"<<Bool(r.landingCount[1]==1)
            <<",\"sourceRfLookups\":"<<r.sourceRfLookups<<",\"sourceContinuationsVerified\":"<<Bool(r.sourceContinuationsVerified)
            <<",\"sourcePatchPc\":"<<r.sourcePatchPc<<",\"sourcePatchContextCount\":"<<r.sourcePatchCount[0]+r.sourcePatchCount[1]
            <<",\"sourcePatchTargets\":["<<r.sourcePatchTargets[0]<<','<<r.sourcePatchTargets[1]<<"],\"sourcePatchCounts\":["<<r.sourcePatchCount[0]<<','<<r.sourcePatchCount[1]
            <<"],\"sourcePatchUnwind\":["<<Bool(r.sourcePatchUnwind[0])<<','<<Bool(r.sourcePatchUnwind[1])<<']'
            <<",\"requiredContextCount\":"<<r.required[0].size()+r.required[1].size()<<",\"observedContextCount\":"<<r.observed[0]+r.observed[1]<<",\"requiredPoints\":[";
        bool first=true;for(U32 v=0;v<2;++v)for(U32 offset:r.required[v]){if(!first)out<<',';first=false;out<<"{\"variant\":"<<v<<",\"offset\":"<<offset<<",\"pc\":"<<(v?r.code:r.control)+offset<<'}';}
        out<<"],\"points\":[";for(U32 k=0;k<r.pointCount;++k){const auto& p=points[r.pointFirst+k];if(k)out<<',';
            out<<"{\"variant\":"<<p.variant<<",\"offset\":"<<p.offset<<",\"source\":\""<<(p.source==1?"single-step":p.source==2?"actual-probe-stackwalk":"actual-source-patch")<<"\",\"actualContext\":true,\"passed\":"<<Bool(p.passed)<<",\"pc\":"<<p.pc<<",\"beforeRsp\":"<<p.rsp
                <<",\"expectedRip\":"<<p.expectedRip<<",\"expectedRsp\":"<<p.expectedRsp<<",\"restoredRip\":"<<p.afterRip<<",\"restoredRsp\":"<<p.afterRsp<<",\"nonvolatilesRestored\":"<<Bool(p.nv)<<",\"xmmNonvolatilesRestored\":"<<Bool(p.xmm)<<",\"restoredNv\":[";
            for(U32 j=0;j<8;++j){if(j)out<<',';out<<p.restoredNv[j];}out<<"],\"imageBase\":"<<p.imageBase<<",\"functionAddress\":"<<p.functionAddress
                <<",\"functionBegin\":"<<p.functionBegin<<",\"functionEnd\":"<<p.functionEnd<<",\"functionUnwind\":"<<p.functionUnwind
                <<",\"sourceRfMatched\":"<<Bool(p.sourceRfMatched)<<'}';}
        out<<"]}";}
    out<<"]}\n";return out.str();
}
bool Absolute(const wchar_t* p){return p&&((std::wcslen(p)>3&&p[1]==L':'&&(p[2]==L'/'||p[2]==L'\\'))||(p[0]==L'\\'&&p[1]==L'\\'));}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=4||std::wcscmp(argv[1],L"--all")||!Absolute(argv[2])||!Absolute(argv[3])||_wcsicmp(argv[2],argv[3])==0){std::fputs("Usage: --all <absolute-fixed-Unity-disk-path> <absolute-child-json-path>\n",stderr);return 2;}
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);std::string diskHash,fatal;PVOID handler{};
    try{Must(GetModuleHandleW(L"UnityPlayer.dll")==nullptr&&GetModuleHandleW(L"GameAssembly.dll")==nullptr,"No game module is loaded");mainThread=GetCurrentThreadId();
        for(U32 i=0;i<16;++i)RtRelayMachineCallerXmm[i]={0x213456789abcdef0ULL+i,0x73456789abcdef01ULL+i};
        for(U32 i=0;i<3;++i){DWORD64 base{};const auto* rf=RtlLookupFunctionEntry(Address(reinterpret_cast<void*>(entries[i])),&base,nullptr);Must(rf!=nullptr,"Actual linked MidEntry has real Windows unwind metadata");entryRanges[i]={base+rf->BeginAddress,base+rf->EndAddress};}
        const auto disk=Read(argv[2]);diskHash=Hash(disk);Must(diskHash==F::UnitySha256,"Actual pinned disk SHA matches fixed profile");handler=AddVectoredExceptionHandler(1,Trace);Must(handler!=nullptr,"Actual single-step VEH installed");
        for(U32 site=0;site<23;++site){RunRoute(disk,site,0);if(IsBranch(site))RunRoute(disk,site,1);}
        Check(routes.size()==31&&walks==28&&naturalTfResumptions==28,"Exact 31 routes and 28 real probe CALL/Entry walks");
        Check(sourceTablesRegistered==31&&sourceTablesDeleted==31&&allSourceContinuations,
            "All 31 owned source tables were actually registered, found at original continuations, and deleted");
        Check(sourcePatchContexts==62&&allSourcePatches&&allSyntheticPrimaries,
            "All 62 actual source-patch CONTEXTs and all four synthetic partial-primary metadata contracts were verified");
        diskUnchanged=Hash(Read(argv[2]))==diskHash;Check(diskUnchanged,"Original game disk remains read-only and unchanged");
    }catch(const std::exception& error){fatal=error.what();Check(false,"Unexpected owned machine fixture setup/runtime exception");}
    catch(...){fatal="Unexpected native failure";Check(false,"Unexpected owned native machine fixture exception");}
    if(handler)Check(RemoveVectoredExceptionHandler(handler)!=0,"Actual single-step VEH removed");
    const auto json=Json(diskHash,fatal);FILE* file{};if(_wfopen_s(&file,argv[3],L"wb")!=0||!file){std::fputs("Cannot persist machine receipt.\n",stderr);return 1;}
    const bool wrote=std::fwrite(json.data(),1,json.size(),file)==json.size();const bool flushed=std::fflush(file)==0;const bool closed=std::fclose(file)==0;
    if(!wrote||!flushed||!closed)return 1;if(std::fwrite(json.data(),1,json.size(),stdout)!=json.size()||std::fflush(stdout)!=0)return 1;
    return failures||routes.size()!=31?1:0;
}
