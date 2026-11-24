#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <vector>
#include <string>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include "../RizToolbox.EngineBootstrap/EngineTransportFamilies.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportRelays.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportOriginals.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/EngineSchedulerContext.h"
#include "../RizToolbox.JudgeContext/EngineWaitContext.h"

extern "C" {
void* RizEngineContinuation[4]{};
uintptr_t RizEngineTransportContinuation[9]{};
void RizEngineOriginalStart();void RizEngineOriginalExecute();
void RizEngineOriginalRelease();void RizEngineOriginalDetach();
}
namespace {
namespace C=RizEngineTransportFamilies;namespace R=RizEngineTransportRelays;namespace F=RizEngineTransportGameProfile;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
constexpr DWORD ErrorMarker=0x7148a19b;
U32 checks{},failures{},observerCalls{},attachCalls{};
bool verifyUnchanged{},imageUnchanged{},nearUnchanged{},hashesVerified{},infoVerified{},retainedUntilExit{};
bool actualConfigured{},negativeRejected{},stickyRejected{},repeatPreserved{};
C::Request request{};C::Receipt receipt{},afterAttempt{};R::Plan plan{};
C::Result actualResult=C::InvalidInput,verifyResult=C::InvalidInput;
std::vector<U8> sourceBefore,nearBefore;std::string fixedDiskSha;
void Check(bool value,const char* reason){++checks;if(!value){++failures;std::fprintf(stderr,"FamiliesFixture: %s\n",reason);}}
void Must(bool value,const char* reason){Check(value,reason);if(!value)throw std::runtime_error(reason);}
U64 Address(const void* value){return reinterpret_cast<U64>(value);}
std::string Hex(const U8* bytes,std::size_t count){std::ostringstream out;
    for(std::size_t i=0;i<count;++i)out<<std::hex<<std::setw(2)<<std::setfill('0')<<static_cast<unsigned>(bytes[i]);return out.str();}
std::vector<U8> Hash(const void* bytes,std::size_t count){
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};DWORD objectBytes{},copied{};std::vector<U8> object,digest(32);
    bool okay=count<=static_cast<std::size_t>(0xffffffffu)&&BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
    if(okay)okay=BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&objectBytes),sizeof(objectBytes),&copied,0)>=0&&copied==sizeof(objectBytes);
    if(okay){object.resize(objectBytes);okay=BCryptCreateHash(algorithm,&hash,object.data(),objectBytes,nullptr,0,0)>=0;}
    if(okay)okay=BCryptHashData(hash,const_cast<PUCHAR>(static_cast<const U8*>(bytes)),static_cast<ULONG>(count),0)>=0;
    if(okay)okay=BCryptFinishHash(hash,digest.data(),static_cast<ULONG>(digest.size()),0)>=0;
    if(hash)okay=(BCryptDestroyHash(hash)>=0)&&okay;
    if(algorithm)okay=(BCryptCloseAlgorithmProvider(algorithm,0)>=0)&&okay;
    Must(okay,"Independent BCrypt SHA256");return digest;
}
std::vector<U8> Read(const wchar_t* path){
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    Must(file!=INVALID_HANDLE_VALUE,"Read-only fixed file open");LARGE_INTEGER size{};
    if(!GetFileSizeEx(file,&size)||size.QuadPart!=static_cast<LONGLONG>(F::UnityFileBytes)){CloseHandle(file);Must(false,"Fixed disk length");}
    std::vector<U8> disk(static_cast<std::size_t>(size.QuadPart));std::size_t used=0;bool okay=true;
    while(used<disk.size()){DWORD got{};const DWORD wanted=static_cast<DWORD>((disk.size()-used)>0x100000?0x100000:disk.size()-used);
        if(!ReadFile(file,disk.data()+used,wanted,&got,nullptr)||got!=wanted){okay=false;break;}used+=got;}
    okay=CloseHandle(file)!=FALSE&&okay;Must(okay&&used==disk.size(),"Read and close complete fixed disk image");return disk;
}
void Write(const std::wstring& path,const void* bytes,std::size_t count){FILE* stream{};
    Must(_wfopen_s(&stream,path.c_str(),L"wb")==0&&stream,"Create actual child evidence");
    const bool wrote=std::fwrite(bytes,1,count,stream)==count;const bool flushed=std::fflush(stream)==0;const bool closed=std::fclose(stream)==0;
    Must(wrote&&flushed&&closed,"Persist actual child bytes with flush and close");}
struct Mapping {
    U8* image{};U8* arena{};bool registered{};
    void Protect(void* at,std::size_t size,DWORD value){DWORD previous{};Must(VirtualProtect(at,size,value,&previous)!=FALSE,"Actual private protection change");}
    void Create(const std::vector<U8>& disk){
        image=static_cast<U8*>(VirtualAlloc(nullptr,F::ImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        Must(image!=nullptr,"Allocate separate MEM_PRIVATE source image");
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(disk.data());
        Must(dos->e_magic==IMAGE_DOS_SIGNATURE&&dos->e_lfanew>0&&static_cast<std::size_t>(dos->e_lfanew)+sizeof(IMAGE_NT_HEADERS64)<disk.size(),"Bounded fixed DOS header");
        const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(disk.data()+dos->e_lfanew);
        Must(nt->Signature==IMAGE_NT_SIGNATURE&&nt->OptionalHeader.SizeOfImage==F::ImageSize,"Bounded fixed PE mapping");
        std::memcpy(image,disk.data(),nt->OptionalHeader.SizeOfHeaders);
        const auto* sections=IMAGE_FIRST_SECTION(nt);
        for(WORD i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& section=sections[i];
            Must(U64(section.PointerToRawData)+section.SizeOfRawData<=disk.size()
                &&U64(section.VirtualAddress)+section.SizeOfRawData<=F::ImageSize,"Source section bounds");
            std::memcpy(image+section.VirtualAddress,disk.data()+section.PointerToRawData,section.SizeOfRawData);}
        sourceBefore.assign(image,image+F::ImageSize);
        // A separate reservation is mandatory: Families checks AllocationBase
        // against the actual arena, not an image+arena combined reservation.
        U64 at=(Address(image)+F::ImageSize+0xffff)&~U64{0xffff};const U64 limit=Address(image)+0x7fff0000ull;
        for(U32 tries=0;tries<65536&&at<limit;++tries){MEMORY_BASIC_INFORMATION info{};
            if(VirtualQuery(reinterpret_cast<void*>(at),&info,sizeof(info))!=sizeof(info))break;
            const U64 begin=(at+0xffff)&~U64{0xffff};const U64 end=Address(info.BaseAddress)+info.RegionSize;
            if(info.State==MEM_FREE&&begin<=end&&R::ArenaMinimumBytes<=end-begin){
                arena=static_cast<U8*>(VirtualAlloc(reinterpret_cast<void*>(begin),R::ArenaMinimumBytes,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
                if(arena)break;}
            if(end<=at)break;at=(end+0xffff)&~U64{0xffff};
        }
        Must(arena!=nullptr,"Reserve actual separate near arena above source");
        MEMORY_BASIC_INFORMATION info{};Must(VirtualQuery(arena,&info,sizeof(info))==sizeof(info)&&info.AllocationBase==arena
            &&info.Type==MEM_PRIVATE&&Address(arena)>Address(image)+F::ImageSize,"Arena has its own actual AllocationBase");
        Protect(image,F::ImageSize,PAGE_READONLY);
    }
    void Prepare(){
        R::Request value{};value.version=R::Version;value.byteSize=static_cast<U32>(sizeof(value));value.fixedImageBase=Address(image);
        value.fixedImageBytes=F::ImageSize;value.nearRw=Address(arena);value.nearCapacity=R::ArenaMinimumBytes;
        for(U32 i=0;i<4;++i)value.typedEntries[i]=Address(RizEngineNodeContext_GetEntry(i,28));
        for(U32 i=0;i<6;++i)value.typedEntries[i+4]=Address(RizEngineSchedulerContext_GetEntry(i,28));
        for(U32 i=0;i<3;++i)value.typedEntries[i+10]=Address(RizEngineWaitContext_GetEntry(i,28));
        value.inlineEntry=Address(RizEngineNodeContext_GetInlineEntry());value.rowEntry=Address(RizEngineSchedulerContext_GetDrainRowEntry());
        value.waitPhaseEntry=Address(RizEngineWaitContext_GetPhaseEntry());
        Must(R::Prepare(&value,&plan)==R::Success&&plan.flags==3&&plan.siteCount==23&&plan.windowCount==36
            &&plan.tableCount==23&&plan.nearBytesWritten==R::ArenaUsedBytes,"Actual Prepare with sixteen real Kernel entry identities");
        Protect(image,F::ImageSize,PAGE_EXECUTE_READ);Protect(arena,R::ArenaMinimumBytes,PAGE_EXECUTE_READ);
        Must(FlushInstructionCache(GetCurrentProcess(),arena,R::ArenaMinimumBytes)!=FALSE,"Flush actual RX near bytes without executing them");
        Must(RtlAddFunctionTable(reinterpret_cast<PRUNTIME_FUNCTION>(arena),23,Address(image))!=FALSE,"Register actual emitted relay runtime table");registered=true;
        for(U32 i=0;i<23;++i){DWORD64 base{};const auto* function=RtlLookupFunctionEntry(plan.sites[i].relay,&base,nullptr);
            Must(function&&base==Address(image)&&std::memcmp(function,&plan.sites[i].runtimeFunction,12)==0,"Actual registered RF tuple found by Windows");}
        nearBefore.assign(arena,arena+R::ArenaMinimumBytes);
    }
};
void Initialize(const Mapping& mapping){
    request.version=C::Version;request.byteSize=static_cast<U32>(sizeof(request));request.profile=C::Owned;request.slot=28;request.mainOsTid=GetCurrentThreadId();
    request.kernelModule=Address(GetModuleHandleW(L"RizToolbox.EngineNodeKernel.dll"));request.targetModule=Address(mapping.image);
    request.originalModule=Address(GetModuleHandleW(nullptr));request.relayAllocationBase=Address(mapping.arena);request.relayAllocationBytes=R::ArenaMinimumBytes;
    Must(request.kernelModule!=0,"Kernel is already loaded through ordinary EXE imports");
    const void* originals[]{reinterpret_cast<const void*>(&RizEngineOriginalStart),reinterpret_cast<const void*>(&RizEngineOriginalExecute),
        reinterpret_cast<const void*>(&RizEngineOriginalRelease),reinterpret_cast<const void*>(&RizEngineOriginalDetach),
        reinterpret_cast<const void*>(&RizEngineTransportOriginalRegister),reinterpret_cast<const void*>(&RizEngineTransportOriginalRemove),
        reinterpret_cast<const void*>(&RizEngineTransportOriginalConsume),reinterpret_cast<const void*>(&RizEngineTransportOriginalPump),
        reinterpret_cast<const void*>(&RizEngineTransportOriginalDrain),reinterpret_cast<const void*>(&RizEngineTransportOriginalCallback),
        reinterpret_cast<const void*>(&RizEngineTransportOriginalYield),reinterpret_cast<const void*>(&RizEngineTransportOriginalDispatch),
        reinterpret_cast<const void*>(&RizEngineTransportOriginalBatch)};
    for(U32 i=0;i<13;++i){request.targets[i]=Address(mapping.image)+F::Functions[i].beginRva;request.originals[i]=Address(originals[i]);
        if(i<4)RizEngineContinuation[i]=reinterpret_cast<void*>(request.targets[i]+F::Functions[i].prefixBytes);
        else RizEngineTransportContinuation[i-4]=static_cast<uintptr_t>(request.targets[i]+F::Functions[i].prefixBytes);}
    for(U32 i=0;i<23;++i){request.sites[i].logicalPc=plan.sites[i].logicalSitePc;request.sites[i].relayReturnPc=plan.sites[i].relayReturnPc;}
}

void NativeInfo(bool nodeAttached){
    RizEngineNodeInfoV1 start{};
    for(U32 kind=0;kind<4;++kind){RizEngineNodeInfoV1 value{};value.abiVersion=1;value.byteSize=sizeof(value);
        Must(RizEngineNodeContext_GetInfo(kind,28,&value)==0&&value.kind==kind&&value.slot==28&&!value.unknown
            &&value.attached==((nodeAttached&&kind==0)?1u:0u)&&Address(value.target)==request.targets[kind]
            &&Address(value.original)==request.originals[kind]&&Address(value.entry)==receipt.typedEntries[kind]
            &&value.epoch==receipt.families[kind].epoch&&!value.entered&&!value.exited&&!value.inFlight
            &&!value.enterFaults&&!value.exitFaults&&!value.snapshotFaults&&!value.outputFaults,"Actual exact Node Info and zero execution counters");
        if(kind==0)start=value;}
    RizEngineInlineInfoV1 side{};side.abiVersion=1;side.byteSize=sizeof(side);
    Must(RizEngineNodeContext_GetInlineInfo(28,&side)==0&&side.configured==1&&!side.attached&&!side.unknown
        &&side.mainThreadId==request.mainOsTid&&side.epoch==receipt.families[4].epoch&&side.startEpoch==start.epoch
        &&Address(side.startTarget)==request.targets[0]&&Address(side.entry)==receipt.inlineEntry
        &&!side.startsEntered&&!side.startsExited&&!side.startsInFlight&&!side.probesInFlight&&!side.events,"Inline uses the actually issued Start epoch and main OS TID");
    for(U32 kind=0;kind<6;++kind){RizEngineSchedulerInfoV1 value{};value.abiVersion=1;value.byteSize=sizeof(value);
        Must(RizEngineSchedulerContext_GetInfo(kind,28,&value)==0&&value.kind==kind&&value.slot==28&&value.configured==1
            &&!value.attached&&!value.unknown&&value.mainThreadId==request.mainOsTid&&value.epoch==receipt.families[kind+5].epoch
            &&Address(value.target)==request.targets[kind+4]&&Address(value.original)==request.originals[kind+4]
            &&Address(value.entry)==receipt.typedEntries[kind+4]&&Address(value.rowEntry)==receipt.drainRowEntry
            &&!value.entered&&!value.exited&&!value.inFlight&&!value.rowBegins&&!value.rowEnds&&!value.rowsInFlight
            &&!value.faults&&!value.snapshotFaults&&!value.unmatchedRows,"Actual six configured Scheduler identities with zero calls/rows");}
    RizEngineWaitInfoV1 wait{};wait.abiVersion=1;wait.byteSize=sizeof(wait);
    Must(RizEngineWaitContext_GetInfo(28,&wait)==0&&wait.slot==28&&wait.configured==1&&!wait.attached&&!wait.unknown
        &&wait.mainThreadId==request.mainOsTid&&wait.epoch==receipt.families[11].epoch
        &&Address(wait.phaseEntry)==receipt.waitPhaseEntry&&!wait.entered&&!wait.exited&&!wait.inFlight&&!wait.phases
        &&!wait.faults&&!wait.snapshotFaults&&!wait.unmatched,"Actual Wait identity and zero physical execution");
    for(U32 i=0;i<3;++i)Must(Address(wait.targets[i])==request.targets[i+10]&&Address(wait.originals[i])==request.originals[i+10]
        &&Address(wait.entries[i])==receipt.typedEntries[i+10],"Actual Wait three target/original/entry tuples");
    infoVerified=true;
}
void ConfigurationBytes(){
    U64 sites=0,windows=0;
    for(U32 family=0;family<12;++family){const auto& row=receipt.families[family];alignas(8) U8 expected[432]{};
        if(family<4){auto* value=reinterpret_cast<RizEngineNodeConfigV1*>(expected);value->abiVersion=1;value->byteSize=sizeof(*value);
            value->target=reinterpret_cast<void*>(request.targets[family]);value->original=reinterpret_cast<void*>(request.originals[family]);}
        else if(family==4){auto* value=reinterpret_cast<RizEngineInlineConfigV1*>(expected);value->abiVersion=1;value->byteSize=sizeof(*value);
            value->startSlot=28;value->mainThreadId=request.mainOsTid;value->expectedStartEpoch=receipt.families[0].epoch;
            value->expectedStartTarget=reinterpret_cast<void*>(request.targets[0]);
            for(U32 i=0;i<3;++i)value->sites[i]={reinterpret_cast<void*>(request.sites[i].logicalPc),reinterpret_cast<void*>(request.sites[i].relayReturnPc)};}
        else if(family<=10){auto* value=reinterpret_cast<RizEngineSchedulerConfigV1*>(expected);const U32 kind=family-5;
            value->abiVersion=1;value->byteSize=sizeof(*value);value->mainThreadId=request.mainOsTid;
            value->target=reinterpret_cast<void*>(request.targets[kind+4]);value->original=reinterpret_cast<void*>(request.originals[kind+4]);
            if(kind==RizEngineSchedulerDrain)for(U32 i=0;i<2;++i)value->sites[i]={reinterpret_cast<void*>(request.sites[i+3].logicalPc),reinterpret_cast<void*>(request.sites[i+3].relayReturnPc)};}
        else{auto* value=reinterpret_cast<RizEngineWaitConfigV1*>(expected);value->abiVersion=1;value->byteSize=sizeof(*value);value->mainThreadId=request.mainOsTid;
            for(U32 i=0;i<3;++i){value->targets[i]=reinterpret_cast<void*>(request.targets[i+10]);value->originals[i]=reinterpret_cast<void*>(request.originals[i+10]);}
            for(U32 i=0;i<4;++i)value->nodeTargets[i]=reinterpret_cast<void*>(request.targets[i]);
            for(U32 i=0;i<6;++i)value->schedulerTargets[i]=reinterpret_cast<void*>(request.targets[i+4]);
            for(U32 i=0;i<18;++i)value->sites[i]={reinterpret_cast<void*>(request.sites[i+5].logicalPc),reinterpret_cast<void*>(request.sites[i+5].relayReturnPc)};}
        constexpr U32 lengths[]{24,24,24,24,88,64,64,64,64,64,64,432};
        Must(row.index==family&&row.configBytes==lengths[family]&&row.slot==28&&row.configureAttempts==1&&row.configureSucceeded==1
            &&row.retainedClaim==1&&row.flags==7&&row.epoch>0&&row.epoch<=0x7fffffffffffffffull
            &&row.mainOsThread==(family<4?0u:request.mainOsTid),"Exact actual family claim and correct TID-bearing ABI");
        Must(std::memcmp(expected,receipt.configurations[family],sizeof(expected))==0,"Independent complete zero-filled Configure descriptor including all 23 PC pairs");
        const auto digest=Hash(expected,lengths[family]);Must(std::memcmp(digest.data(),row.configSha256,32)==0,"Independent BCrypt matches recorded actual config bytes SHA256");
        Must(receipt.configureResults[family]==0&&receipt.informationResults[family]==0&&receipt.informationBytes[family]==row.infoBytes,"Each real Configure/Info result is successful");
        Must((sites&row.siteMask)==0&&(windows&row.windowMask)==0,"No duplicate physical side/config responsibilities");sites|=row.siteMask;windows|=row.windowMask;
    }
    Must(sites==((1ull<<23)-1)&&windows==((1ull<<36)-1),"Exact 23 site and 36 window family coverage");hashesVerified=true;
}
std::uintptr_t __cdecl ObserverEnter(const RizEngineNodeFrameV1*,void*){++observerCalls;return 1;}
void __cdecl ObserverExit(const RizEngineNodeFrameV1*,std::uintptr_t,const RizEngineNodeCompletionV1*,void*){++observerCalls;}
void Run(const std::string& mode,const wchar_t* imagePath,Mapping& mapping){
    Must(GetModuleHandleW(L"UnityPlayer.dll")==nullptr&&GetModuleHandleW(L"GameAssembly.dll")==nullptr,"Self-owned process has no loaded game modules");
    const auto disk=Read(imagePath);const auto hash=Hash(disk.data(),disk.size());fixedDiskSha=Hex(hash.data(),hash.size());
    Must(fixedDiskSha==F::UnitySha256,"Independent fixed whole disk SHA256");mapping.Create(disk);mapping.Prepare();Initialize(mapping);
    if(mode=="bad-site")++request.sites[0].relayReturnPc;
    if(mode=="production-gate")request.profile=C::FixedGame;
    SetLastError(ErrorMarker);actualResult=C::PrepareAndConfigure(&request,&receipt);
    Must(GetLastError()==ErrorMarker,"Actual Configure preserves incoming LastError");
    if(mode=="bad-site"||mode=="production-gate"){
        const auto expected=mode=="bad-site"?C::BadSite:C::AdmissionUnproven;
        Must(actualResult==expected&&receipt.state==C::Failed&&receipt.result==expected,"Actual malformed descriptor/production gate refused");
        Must(receipt.configureAttempts==0&&receipt.configureSucceeded==0&&receipt.attemptedFamilies==0&&receipt.configuredFamilies==0
            &&receipt.retainedFamilies==0,"Refusal precedes every real Configure claim");negativeRejected=true;
        verifyResult=C::Verify(&request,&receipt);Must(verifyResult==expected,"Failed attempt remains permanently refused");stickyRejected=true;
    }else{
        Must(actualResult==C::Success&&receipt.state==C::Configured&&receipt.result==C::Success&&receipt.descriptorsPrepared==12
            &&receipt.configureAttempts==12&&receipt.configureSucceeded==12&&receipt.apiCount==35
            &&receipt.attemptedFamilies==4095&&receipt.configuredFamilies==4095&&receipt.verifiedFamilies==4095
            &&receipt.retainedFamilies==4095&&receipt.inlineEpochBound==1,"Twelve actual native Configure claims completed");
        actualConfigured=true;ConfigurationBytes();NativeInfo(false);
        const C::Receipt before=receipt;SetLastError(ErrorMarker);verifyResult=C::Verify(&request,&receipt);
        verifyUnchanged=std::memcmp(&before,&receipt,sizeof(receipt))==0;
        Must(verifyResult==C::Success&&verifyUnchanged&&GetLastError()==ErrorMarker,"Actual Verify succeeds without modifying one receipt byte or LastError");
        if(mode=="receipt-tamper"){
            C::Receipt changed=receipt;changed.families[11].configSha256[0]^=1;
            Must(C::Verify(&request,&changed)==C::ReceiptChanged,"Caller modified receipt cannot authorize actual configured channels");
            Must(C::Verify(&request,&receipt)==C::ReceiptChanged,"Receipt drift leaves private sticky failure after original restored");
            negativeRejected=true;stickyRejected=true;
            Must(C::PrepareAndConfigure(&request,&afterAttempt)==C::AlreadyAttempted&&afterAttempt.state==C::Failed
                &&afterAttempt.result==C::ReceiptChanged&&afterAttempt.retainedFamilies==4095&&afterAttempt.configureAttempts==12,
                "Failed owner still returns every actual permanent claim");
        }else if(mode=="repeat-attempt"){
            Must(C::PrepareAndConfigure(&request,&afterAttempt)==C::AlreadyAttempted,"Second permanent attempt rejected");
            repeatPreserved=std::memcmp(&receipt,&afterAttempt,sizeof(receipt))==0;
            Must(repeatPreserved&&C::Verify(&request,&receipt)==C::Success,"Second attempt neither reconfigures nor replaces first receipt");
        }else if(mode=="node-attach"){
            RizEngineNodeObserverV1 observer{1,sizeof(RizEngineNodeObserverV1),ObserverEnter,ObserverExit,nullptr};
            Must(RizEngineNodeContext_Attach(0,28,receipt.families[0].epoch,&observer)==0,"Actual single Node Start Attach on exact issued epoch");++attachCalls;
            NativeInfo(true);Must(C::Verify(&request,&receipt)==C::Success&&std::memcmp(&before,&receipt,sizeof(receipt))==0,
                "Verify accepts actual monotonic attached identity without modifying receipt");
        }
        Must(observerCalls==0,"No actual Kernel entry, mapped source, original or relay executed");
    }
    imageUnchanged=std::memcmp(mapping.image,sourceBefore.data(),sourceBefore.size())==0;
    nearUnchanged=std::memcmp(mapping.arena,nearBefore.data(),nearBefore.size())==0;
    Must(imageUnchanged&&nearUnchanged,"All source/near bytes unchanged across real Configure and Verify");
    MEMORY_BASIC_INFORMATION source{},arena{};
    Must(VirtualQuery(mapping.image,&source,sizeof(source))==sizeof(source)&&source.Type==MEM_PRIVATE&&source.AllocationBase==mapping.image
        &&source.Protect==PAGE_EXECUTE_READ,"Source remains exact owned RX private allocation");
    Must(VirtualQuery(mapping.arena,&arena,sizeof(arena))==sizeof(arena)&&arena.Type==MEM_PRIVATE&&arena.AllocationBase==mapping.arena
        &&arena.Protect==PAGE_EXECUTE_READ&&mapping.registered,"Near arena remains independently owned registered RX allocation");
    retainedUntilExit=true; // No destructor/free/unregister: permanent claims must never reference freed code.
    const auto diskAfter=Read(imagePath);const auto afterHash=Hash(diskAfter.data(),diskAfter.size());
    Must(Hex(afterHash.data(),afterHash.size())==fixedDiskSha,"Read-only source disk remains unchanged after actual calls");
}
std::string Json(const std::string& mode,const std::string& fatal){
    const auto requestHash=Hash(&request,sizeof(request)),receiptHash=Hash(&receipt,sizeof(receipt)),planHash=Hash(&plan,sizeof(plan));
    std::ostringstream out;out<<std::boolalpha;
    out<<"{\"schemaVersion\":1,\"kind\":\"engine-transport-families-owned-child\",\"mode\":\""<<mode
        <<"\",\"passed\":"<<(failures==0&&fatal.empty())<<",\"pid\":"<<GetCurrentProcessId()<<",\"checks\":"<<checks<<",\"failures\":"<<failures
        <<",\"result\":"<<static_cast<U32>(actualResult)<<",\"verifyResult\":"<<static_cast<U32>(verifyResult)
        <<",\"state\":"<<receipt.state<<",\"actualConfigured\":"<<actualConfigured<<",\"apiCount\":"<<receipt.apiCount
        <<",\"configureAttempts\":"<<receipt.configureAttempts<<",\"configureSucceeded\":"<<receipt.configureSucceeded
        <<",\"attemptedFamilies\":"<<receipt.attemptedFamilies<<",\"configuredFamilies\":"<<receipt.configuredFamilies
        <<",\"retainedFamilies\":"<<receipt.retainedFamilies<<",\"verifiedFamilies\":"<<receipt.verifiedFamilies
        <<",\"configurationShaVerified\":"<<hashesVerified<<",\"actualInformationVerified\":"<<infoVerified
        <<",\"verifyReceiptUnchanged\":"<<verifyUnchanged<<",\"negativeRejected\":"<<negativeRejected
        <<",\"stickyRejected\":"<<stickyRejected<<",\"repeatReceiptPreserved\":"<<repeatPreserved
        <<",\"actualAttachCalls\":"<<attachCalls<<",\"observerCalls\":"<<observerCalls
        <<",\"mappedSourceUnchanged\":"<<imageUnchanged<<",\"nearBytesUnchanged\":"<<nearUnchanged
        <<",\"retainedUntilProcessExit\":"<<retainedUntilExit<<",\"runtimeTableRegistered\":"<<retainedUntilExit
        <<",\"receiptBytes\":"<<sizeof(receipt)<<",\"requestBytes\":"<<sizeof(request)<<",\"planBytes\":"<<sizeof(plan)
        <<",\"fixedDiskSha256\":\""<<fixedDiskSha<<"\",\"requestSha256\":\"";
    out<<Hex(requestHash.data(),requestHash.size())<<"\",\"receiptSha256\":\""<<Hex(receiptHash.data(),receiptHash.size())
        <<"\",\"planSha256\":\""<<Hex(planHash.data(),planHash.size())<<"\",\"families\":[";
    for(U32 i=0;i<12;++i){if(i)out<<',';const auto& row=receipt.families[i];
        out<<"{\"index\":"<<i<<",\"epoch\":"<<row.epoch<<",\"slot\":"<<row.slot<<",\"mainOsTid\":"<<row.mainOsThread
            <<",\"configBytes\":"<<row.configBytes<<",\"configSha256\":\""<<Hex(row.configSha256,32)<<"\"}";}
    out<<"],\"selfOwnedOnly\":true,\"sourceWrites\":0,\"sourcePatched\":false,\"gameExecuted\":false,\"gameLoaded\":false,"
        "\"mappedImageExecuted\":false,\"originalsExecuted\":false,\"relayCodeExecuted\":false,\"fullFamilyAdoptionVerified\":false,"
        "\"runtimeTableDeleted\":false,\"ownedMappingsFreed\":false,\"productionAdmissionVerified\":false,\"installerExecuted\":false,\"fatal\":\"";
    // Fatal text is JSON-escaped; stdout remains one complete actual object.
    for(char c:fatal){if(c=='\\'||c=='"')out<<'\\';if(c=='\n')out<<"\\n";else if(c=='\r')out<<"\\r";else out<<c;}
    out<<"\"}";return out.str();
}
} // anonymous namespace
static_assert(sizeof(C::Request)==640&&sizeof(C::Receipt)==9792&&offsetof(C::Receipt,configurations)==2448);
int wmain(int argc,wchar_t** argv){
    std::string mode,fatal;std::wstring output;
    try{
        Must(argc==5&&std::wcscmp(argv[1],L"--case")==0,"Use --case MODE FIXED_DISK FRESH_CASE_DIRECTORY");
        for(const wchar_t* at=argv[2];*at;++at)mode.push_back(static_cast<char>(*at));output=argv[4];
        Must(mode=="normal"||mode=="bad-site"||mode=="production-gate"||mode=="receipt-tamper"||mode=="repeat-attempt"||mode=="node-attach","Exact owned scenario");
        Mapping mapping;Run(mode,argv[3],mapping);
        Write(output+L"/request.bin",&request,sizeof(request));Write(output+L"/receipt.bin",&receipt,sizeof(receipt));
        Write(output+L"/plan.bin",&plan,sizeof(plan));Write(output+L"/after-attempt.bin",&afterAttempt,sizeof(afterAttempt));
    }catch(const std::exception& error){fatal=error.what();if(!failures)++failures;}
    const auto json=Json(mode,fatal);bool persisted=false;
    if(!output.empty()){FILE* stream{};if(_wfopen_s(&stream,(output+L"/child-result.json").c_str(),L"wb")==0&&stream){
        const bool wrote=std::fwrite(json.data(),1,json.size(),stream)==json.size();const bool flushed=std::fflush(stream)==0;
        const bool closed=std::fclose(stream)==0;persisted=wrote&&flushed&&closed;}}
    std::printf("%s\n",json.c_str());const bool flushed=std::fflush(stdout)==0;
    return failures==0&&fatal.empty()&&persisted&&flushed?0:1;
}
