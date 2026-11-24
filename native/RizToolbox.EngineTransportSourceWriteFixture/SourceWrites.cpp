#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>
#include <sstream>
#include <iomanip>
#include <stdexcept>
#include "../RizToolbox.EngineBootstrap/EngineTransportSourceWrites.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
#pragma comment(lib,"bcrypt.lib")
#ifndef RIZ_ENGINE_TRANSPORT_COMMIT_FIXTURE
#error This owned fixture requires its explicit nonproduction fault-injection build.
#endif
namespace {
namespace R=RizEngineTransportRelays;namespace S=RizEngineTransportSourceWrites;namespace F=RizEngineTransportGameProfile;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
constexpr U64 AllWindows=(U64{1}<<36)-1;constexpr DWORD LastErrorMarker=0x7945b123;
U32 checks{},failures{},registeredCount{},deletedCount{};U64 totalWritten{};
bool sourceDiskUnchanged=true,registeredTablesDeleted=true,allPrivate=true;
R::Plan plan{};S::Receipt receipt{};
void Check(bool value,const char* reason){++checks;if(!value){++failures;std::fprintf(stderr,"SourceWritesFixture failed: %s\n",reason);}}
void Must(bool value,const char* reason){Check(value,reason);if(!value)throw std::runtime_error(reason);}
U32 CountBits(U64 value){U32 count{};while(value){count+=static_cast<U32>(value&1);value>>=1;}return count;}
U64 BeforeMask(U32 count){return count?(U64{1}<<count)-1:0;}
std::string Hex(const U8* bytes,U32 count){std::ostringstream out;for(U32 n=0;n<count;++n)out<<std::hex<<std::setw(2)<<std::setfill('0')<<static_cast<unsigned>(bytes[n]);return out.str();}
std::string Hash(const std::vector<U8>& input){
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};DWORD objectBytes{},copied{};std::vector<U8> object;U8 output[32]{};
    bool okay=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
    if(okay)okay=BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&objectBytes),sizeof(objectBytes),&copied,0)>=0&&copied==sizeof(objectBytes);
    if(okay){object.resize(objectBytes);okay=BCryptCreateHash(algorithm,&hash,object.data(),objectBytes,nullptr,0,0)>=0;}
    if(okay)okay=BCryptHashData(hash,const_cast<PUCHAR>(input.data()),static_cast<ULONG>(input.size()),0)>=0;
    if(okay)okay=BCryptFinishHash(hash,output,sizeof(output),0)>=0;
    if(hash)okay=(BCryptDestroyHash(hash)>=0)&&okay;if(algorithm)okay=(BCryptCloseAlgorithmProvider(algorithm,0)>=0)&&okay;
    Must(okay,"Actual SHA256 of the locked fixed disk input");std::ostringstream value;
    for(const auto byte:output)value<<std::hex<<std::setw(2)<<std::setfill('0')<<static_cast<unsigned>(byte);return value.str();
}
std::vector<U8> Read(const wchar_t* path){
    const HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    Must(file!=INVALID_HANDLE_VALUE,"Open fixed disk input for read only");LARGE_INTEGER size{};
    const bool sized=GetFileSizeEx(file,&size)!=0&&size.QuadPart==static_cast<LONGLONG>(F::UnityFileBytes);
    if(!sized){CloseHandle(file);Must(false,"Exact pinned disk byte size");}
    std::vector<U8> bytes(static_cast<std::size_t>(size.QuadPart));std::size_t total{};bool okay=true;
    while(total<bytes.size()){const auto remaining=bytes.size()-total;const DWORD wanted=static_cast<DWORD>(remaining>0x100000?0x100000:remaining);DWORD got{};
        if(!ReadFile(file,bytes.data()+total,wanted,&got,nullptr)||got!=wanted){okay=false;break;}total+=got;}
    const bool closed=CloseHandle(file)!=0;Must(okay&&closed&&total==bytes.size(),"Read and close exact fixed disk bytes");return bytes;
}
template<unsigned Index> __declspec(noinline) U64 __fastcall OwnedEntry(U64 a,U64 b,U64 c,U64 d){return a^b^c^d^(0x78340000u+Index);}
struct Mapping {
    U8* image{};U8* nearBytes{};bool registered{};std::vector<U8> original;
    void Protect(void* address,SIZE_T bytes,DWORD value){DWORD previous{};Must(VirtualProtect(address,bytes,value,&previous)!=0,"Actual owned protection change");}
    void Create(const std::vector<U8>& disk){
        image=static_cast<U8*>(VirtualAlloc(nullptr,static_cast<SIZE_T>(F::ImageSize)+0x10000,MEM_RESERVE,PAGE_NOACCESS));Must(image!=nullptr,"Reserve owned private image and near allocation");
        Must(VirtualAlloc(image,F::ImageSize,MEM_COMMIT,PAGE_READWRITE)==image,"Commit private non-executable image for mapping");
        nearBytes=reinterpret_cast<U8*>((reinterpret_cast<U64>(image)+F::ImageSize+0xffff)&~U64{0xffff});
        Must(VirtualAlloc(nearBytes,R::ArenaMinimumBytes,MEM_COMMIT,PAGE_READWRITE)==nearBytes,"Commit owned near RW block");
        const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(disk.data());const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(disk.data()+dos->e_lfanew);
        std::memcpy(image,disk.data(),nt->OptionalHeader.SizeOfHeaders);const auto* sections=IMAGE_FIRST_SECTION(nt);
        for(WORD i=0;i<nt->FileHeader.NumberOfSections;++i){const auto& section=sections[i];
            Must(static_cast<U64>(section.PointerToRawData)+section.SizeOfRawData<=disk.size()&&static_cast<U64>(section.VirtualAddress)+section.SizeOfRawData<=F::ImageSize,"Owned section mapping bounds");
            std::memcpy(image+section.VirtualAddress,disk.data()+section.PointerToRawData,section.SizeOfRawData);}
        original.assign(image,image+F::ImageSize);Protect(image,F::ImageSize,PAGE_READONLY);
    }
    R::Request RelayRequest(){R::Request request{};request.version=R::Version;request.byteSize=static_cast<U32>(sizeof(request));
        request.fixedImageBase=reinterpret_cast<U64>(image);request.fixedImageBytes=F::ImageSize;request.nearRw=reinterpret_cast<U64>(nearBytes);request.nearCapacity=R::ArenaMinimumBytes;
        const void* entries[]{reinterpret_cast<void*>(&OwnedEntry<0>),reinterpret_cast<void*>(&OwnedEntry<1>),reinterpret_cast<void*>(&OwnedEntry<2>),reinterpret_cast<void*>(&OwnedEntry<3>),
            reinterpret_cast<void*>(&OwnedEntry<4>),reinterpret_cast<void*>(&OwnedEntry<5>),reinterpret_cast<void*>(&OwnedEntry<6>),reinterpret_cast<void*>(&OwnedEntry<7>),
            reinterpret_cast<void*>(&OwnedEntry<8>),reinterpret_cast<void*>(&OwnedEntry<9>),reinterpret_cast<void*>(&OwnedEntry<10>),reinterpret_cast<void*>(&OwnedEntry<11>),reinterpret_cast<void*>(&OwnedEntry<12>)};
        for(U32 i=0;i<13;++i)request.typedEntries[i]=reinterpret_cast<U64>(entries[i]);
        request.inlineEntry=reinterpret_cast<U64>(&OwnedEntry<13>);request.rowEntry=reinterpret_cast<U64>(&OwnedEntry<14>);request.waitPhaseEntry=reinterpret_cast<U64>(&OwnedEntry<15>);return request;
    }
    void Prepare(bool addTable){const auto request=RelayRequest();Must(R::Prepare(&request,&plan)==R::Success,"Actual fixed-byte relay preparation before source commit");
        Must(plan.flags==3&&plan.nearBytesWritten==6400&&plan.siteCount==23&&plan.tableCount==23,"Exact complete actual prepared near block");
        if(addTable){Must(RtlAddFunctionTable(reinterpret_cast<PRUNTIME_FUNCTION>(nearBytes),23,reinterpret_cast<DWORD64>(image))!=FALSE,"Actual runtime function table registration");registered=true;++registeredCount;}
        Protect(image,F::ImageSize,PAGE_EXECUTE_READ);Protect(nearBytes,R::ArenaMinimumBytes,PAGE_EXECUTE_READ);
        Must(FlushInstructionCache(GetCurrentProcess(),nearBytes,R::ArenaMinimumBytes)!=FALSE,"Flush owned registered near bytes without execution");
    }
    bool Memory(U64 address,U32 bytes,DWORD protection,bool entry=false){U64 current=address;const U64 end=address+bytes;
        while(current<end){MEMORY_BASIC_INFORMATION info{};if(VirtualQuery(reinterpret_cast<const void*>(current),&info,sizeof(info))!=sizeof(info))return false;
            if(info.State!=MEM_COMMIT||info.Protect!=protection||info.Type!=static_cast<DWORD>(entry?MEM_IMAGE:MEM_PRIVATE)||(!entry&&info.AllocationBase!=image))return false;
            const auto next=reinterpret_cast<U64>(info.BaseAddress)+info.RegionSize;if(next<=current)return false;current=next<end?next:end;}
        return true;}
    void WriteOwned(U32 rva,U8 value){auto* pointer=image+rva;auto* page=reinterpret_cast<U8*>(reinterpret_cast<U64>(pointer)&~U64{0xfff});Protect(page,4096,PAGE_READWRITE);*pointer=value;Protect(page,4096,PAGE_EXECUTE_READ);}
    bool Delete(){if(!registered)return true;if(!RtlDeleteFunctionTable(reinterpret_cast<PRUNTIME_FUNCTION>(nearBytes))){registeredTablesDeleted=false;Check(false,"Actual function table removal failed; owned allocation retained to process exit");return false;}
        registered=false;++deletedCount;return true;}
    ~Mapping(){if(Delete()&&image)VirtualFree(image,0,MEM_RELEASE);}
};
struct Case {
    std::string name,windows;U32 result{},expected{},profile{},stage{},window{},checks{},committed{},attempts{},completions{},outstanding{},events{},state{},failureWindow{};
    U64 bytes{},committedMask{},attemptedMask{},preparedMask{};
    bool passed{},privateWrites{},terminal{},lastError{},registered{},deleted{},sourceVerified{},protectionVerified{},eventsVerified{},fixtureResetProtection{};
    bool coherentRelayTamper{},returnPcAtRfEnd{},actualNearMatchesPlan{};
};
std::vector<Case> cases;
bool Events(){bool okay=receipt.eventOverflow==0&&receipt.eventCount<=S::EventCapacity;
    for(U32 i=0;i<receipt.eventCount;++i){const auto& event=receipt.events[i];okay=okay&&event.sequence==i+1&&event.stage>=S::Preflight&&event.stage<=S::Failure
        &&(event.window==S::NoIndex||event.window<36)&&event.reserved[0]==0&&event.reserved[1]==0;}
    return okay;}
bool MatrixEvents(U32 stage,U32 faultWindow){U32 cursor{};bool okay=true;
    const auto take=[&](U32 eventStage,U32 window){if(cursor>=receipt.eventCount){okay=false;return;}const auto& event=receipt.events[cursor++];okay=okay&&event.stage==eventStage&&event.window==window;};
    take(S::Preflight,S::NoIndex);const U32 completed=stage?faultWindow:36;
    constexpr U32 normalStages[]{S::ProtectBegin,S::Protected,S::WriteBegin,S::Written,S::Readback,S::Restored,S::Flushed,S::Committed};
    for(U32 index=0;index<completed;++index)for(const U32 eventStage:normalStages)take(eventStage,index);
    if(stage){take(S::ProtectBegin,faultWindow);if(stage!=S::ProtectBegin){take(S::Protected,faultWindow);
        if(stage!=S::Protected){take(S::WriteBegin,faultWindow);take(S::Written,faultWindow);
            if(stage==S::Restored||stage==S::Flushed)take(S::Readback,faultWindow);}
        take(S::Restored,faultWindow);}take(S::Failure,faultWindow);}
    return okay&&cursor==receipt.eventCount;
}
S::Result StageResult(U32 stage){return stage==S::ProtectBegin||stage==S::Protected?S::ProtectFailed:stage==S::WriteBegin?S::WriteFailed:
    stage==S::Readback?S::ReadbackFailed:stage==S::Restored?S::RestoreFailed:S::FlushFailed;}
void VerifyMatrix(U32 stage,U32 failureWindow,bool normal){
    const U32 completed=normal?36:failureWindow;const bool targetWrites=!normal&&stage!=S::ProtectBegin&&stage!=S::Protected;
    const U64 attempted=BeforeMask(completed)|(targetWrites?(U64{1}<<failureWindow):0);
    Check(receipt.state==(normal?S::Complete:S::Failed)&&receipt.sourceWriteCompletions==completed&&receipt.committedWindows==BeforeMask(completed),"Exact preceding committed windows and terminal state");
    Check(receipt.sourceWriteAttempts==completed+(targetWrites?1u:0u)&&receipt.attemptedWindows==attempted&&receipt.preparedWindows==AllWindows,"Exact attempted/prepared bitmap and physical source attempts");
    Check(receipt.protectionOutstanding==0&&receipt.outstandingWindows==0,"All actual source protection duties are discharged");
    if(!normal)Check(receipt.failureWindow==failureWindow,"Exact injected window failure retained");
    for(U32 index=0;index<36;++index){const auto& row=receipt.windows[index];const bool previous=index<completed;const bool target=!normal&&index==failureWindow;
        SYSTEM_INFO system{};GetSystemInfo(&system);const U64 pageBytes=system.dwPageSize;
        const auto& physical=F::PhysicalWindows[index];const U64 source=plan.fixedImageBase+physical.beginRva;
        const U64 first=source&~(pageBytes-1),last=(source+physical.byteCount-1)&~(pageBytes-1);
        Check(row.protectionBase==first&&row.protectionBytes==last+pageBytes-first,"Recorded protection obligation covers the exact OS-rounded source pages");
        const bool protectedTarget=target&&stage!=S::ProtectBegin;const bool writesTarget=target&&targetWrites;
        const U32 written=previous?F::PhysicalWindows[index].byteCount:writesTarget?(stage==S::WriteBegin?1u:F::PhysicalWindows[index].byteCount):0;
        Check(row.sourceBytesWritten==written&&row.sourceWriteAttempts==(previous||writesTarget?1u:0u),"Exact real per-window successful write byte progress");
        Check(row.protectAttempts==(previous||target?1u:0u)&&row.protectSucceeded==(previous||protectedTarget?1u:0u)
            &&row.restoreSucceeded==(previous||protectedTarget?1u:0u)&&row.protectionOutstanding==0,"Exact real protection/restore operation progress");
        const bool readback=previous||(writesTarget&&stage!=S::WriteBegin);const bool validReadback=readback&&!(target&&stage==S::Readback);
        Check(row.readbackAttempts==(readback?1u:0u)&&row.readbackVerified==(validReadback?1u:0u),"Actual readback verification count");
        Check(row.flushAttempts==(previous||(target&&stage==S::Flushed)?1u:0u)&&row.flushSucceeded==(previous?1u:0u),"Actual flush attempts distinguish simulated stage failure from successful OS flush");
        if(previous)Check((row.flags&(RizEngineTransportWindowWrittenV5|RizEngineTransportWindowReadbackV5|RizEngineTransportWindowProtectionRestoredV5|RizEngineTransportWindowFlushedV5))
            ==(RizEngineTransportWindowWrittenV5|RizEngineTransportWindowReadbackV5|RizEngineTransportWindowProtectionRestoredV5|RizEngineTransportWindowFlushedV5),"Committed window has all actual completion evidence");
    }
}
void Run(const std::vector<U8>& disk,U32 kind,U32 stage=0,U32 faultWindow=S::NoIndex){
    const U32 beforeChecks=checks,beforeFailures=failures;Mapping mapping;mapping.Create(disk);mapping.Prepare(kind!=5);
    Case output;output.profile=kind==4?2u:1u;output.stage=stage;output.window=faultWindow;output.registered=mapping.registered;
    const char* preflight[]{"normal","bad-request","bad-plan","bad-source","production-gate","table-missing","source-not-RX","nonfresh-receipt","coherent-relay-tamper","returnpc-at-rf-end"};
    output.name=stage?"stage-"+std::to_string(stage)+"-window-"+std::to_string(faultWindow):preflight[kind];
    S::Request request{};request.version=S::Version;request.byteSize=static_cast<U32>(sizeof(request));request.profile=output.profile;request.prepared=&plan;
    request.faultStage=stage;request.faultWindow=faultWindow;request.partialBytes=1;std::memset(&receipt,0,sizeof(receipt));
    if(kind==1)request.version=0;if(kind==2)plan.flags&=~R::NearPrepared;
    U32 badRva{};U8 badByte{};if(kind==3){badRva=F::Functions[0].beginRva+0x20;badByte=static_cast<U8>(mapping.image[badRva]^1);mapping.WriteOwned(badRva,badByte);}
    if(kind==6){auto* page=reinterpret_cast<U8*>(plan.windows[0].source&~U64{0xfff});mapping.Protect(page,4096,PAGE_READONLY);}
    if(kind==7)receipt.version=0xa5;
    if(kind==8){const auto& site=plan.sites[0];const U32 at=site.relativeOffset+6;
        Check(site.relayReturnPc==site.relay+6&&plan.preparedBytes[at]==0xff&&plan.preparedBytes[at+1]==0x4b&&plan.preparedBytes[at+2]==0x60
            &&std::memcmp(mapping.nearBytes+at,plan.preparedBytes+at,3)==0,"Actual coherent-tamper input is the generated InlineBegin DEC instruction");
        mapping.Protect(mapping.nearBytes,R::ArenaMinimumBytes,PAGE_READWRITE);
        for(U32 byte=0;byte<3;++byte){mapping.nearBytes[at+byte]=0x90;plan.preparedBytes[at+byte]=0x90;}
        mapping.Protect(mapping.nearBytes,R::ArenaMinimumBytes,PAGE_EXECUTE_READ);
        output.coherentRelayTamper=std::memcmp(mapping.nearBytes+at,"\x90\x90\x90",3)==0
            &&std::memcmp(plan.preparedBytes+at,"\x90\x90\x90",3)==0;
        Check(output.coherentRelayTamper,"Actual near bytes and caller plan both carry the same three NOPs while CALL and metadata remain unchanged");}
    if(kind==9){auto& site=plan.sites[0];site.relayReturnPc=site.relay+site.codeBytes;
        output.returnPcAtRfEnd=site.relayReturnPc==plan.fixedImageBase+site.runtimeFunction.endRva;
        Check(output.returnPcAtRfEnd,"Actual candidate return PC is exactly at the exclusive registered RF end");}
    output.actualNearMatchesPlan=std::memcmp(mapping.nearBytes,plan.preparedBytes,R::ArenaUsedBytes)==0;
    Check(output.actualNearMatchesPlan,"Actual candidate near bytes agree with caller plan before Commit");
    output.expected=stage?StageResult(stage):kind==0?S::Success:kind==1||kind==7?S::InvalidInput:kind==2||kind==8||kind==9?S::BadPlan:kind==3?S::BadSource:
        kind==4?S::AdmissionUnproven:kind==5?S::BadRuntimeTable:S::BadMemory;
    std::vector<U8> initialReceipt(reinterpret_cast<U8*>(&receipt),reinterpret_cast<U8*>(&receipt)+sizeof(receipt));
    SetLastError(LastErrorMarker);const auto actual=S::Commit(&request,&receipt);const DWORD errorAfter=GetLastError();
    output.result=actual;output.lastError=errorAfter==LastErrorMarker;Check(output.lastError,"Actual Commit preserves caller LastError");Check(actual==output.expected,"Specific actual result for this case");
    if(kind==7)Check(std::memcmp(&receipt,initialReceipt.data(),sizeof(receipt))==0,"Nonfresh receipt remains completely immutable");
    else Check(receipt.result==actual,"Returned result matches actual terminal receipt");
    const bool matrix=kind==0;if(matrix)VerifyMatrix(stage,faultWindow,stage==0);
    else Check(receipt.sourceWriteAttempts==0&&receipt.sourceWriteCompletions==0&&receipt.committedWindows==0,"Preflight rejection performs no source writes");
    // Compare the entire private image, including every byte outside all source
    // windows. Only actual reported write bytes and explicit owned mutation may
    // differ; there is no game-image execution or body emulation here.
    std::vector<U8> expectedImage=mapping.original;if(kind==3)expectedImage[badRva]=badByte;
    U64 writtenBytes{};std::ostringstream windowsJson;windowsJson<<'[';
    for(U32 index=0;index<36;++index){const auto& fact=F::PhysicalWindows[index];const auto& row=receipt.windows[index];const auto& prepared=plan.windows[index];
        U32 written=kind==7?0:row.sourceBytesWritten;Check(written<=fact.byteCount,"Actual written count is bounded by its physical window");
        if(written>fact.byteCount)written=0;for(U32 b=0;b<written;++b)expectedImage[fact.beginRva+b]=prepared.patch[b];
        if(matrix&&stage==S::Readback&&index==faultWindow)expectedImage[fact.beginRva]^=1;writtenBytes+=written;
        if(matrix)Check(std::memcmp(mapping.image+fact.beginRva,expectedImage.data()+fact.beginRva,fact.byteCount)==0,"Actual image window equals full/partial/damaged expected write and untouched later bytes");
        if(index)windowsJson<<',';windowsJson<<"{\"index\":"<<index<<",\"written\":"<<written<<",\"protectAttempts\":"<<row.protectAttempts
            <<",\"protectSucceeded\":"<<row.protectSucceeded<<",\"restoreSucceeded\":"<<row.restoreSucceeded<<",\"flushAttempts\":"<<row.flushAttempts<<",\"flushSucceeded\":"<<row.flushSucceeded
            <<",\"actualBytes\":\""<<Hex(mapping.image+fact.beginRva,fact.byteCount)<<"\"}";
        const DWORD wanted=kind==6&&index==0?PAGE_READONLY:PAGE_EXECUTE_READ;
        // Several source windows share a page. This exceptional owned preflight
        // setup is checked once below, then reset by the fixture, never credited
        // as a writer restoration.
        if(kind!=6)Check(mapping.Memory(plan.windows[index].source,fact.byteCount,wanted),"Actual private source window has restored RX protection");
    }windowsJson<<']';output.windows=windowsJson.str();output.bytes=writtenBytes;totalWritten+=writtenBytes;
    output.sourceVerified=std::memcmp(mapping.image,expectedImage.data(),F::ImageSize)==0;Check(output.sourceVerified,"All private mapped-image bytes match exact owned write progress");
    if(kind==6){auto* page=reinterpret_cast<U8*>(plan.windows[0].source&~U64{0xfff});Check(mapping.Memory(plan.windows[0].source,plan.windows[0].byteCount,PAGE_READONLY),"Preflight leaves deliberately non-RX source unchanged");
        mapping.Protect(page,4096,PAGE_EXECUTE_READ);output.fixtureResetProtection=true;}
    bool protections=true;for(U32 index=0;index<36;++index)protections=mapping.Memory(plan.windows[index].source,F::PhysicalWindows[index].byteCount,PAGE_EXECUTE_READ)&&protections;
    protections=mapping.Memory(reinterpret_cast<U64>(mapping.nearBytes),8192,PAGE_EXECUTE_READ)&&protections;output.protectionVerified=protections;allPrivate=allPrivate&&protections;
    Check(protections,"Every source/near window is MEM_PRIVATE RX after actual writer or explicit fixture reset");
    output.actualNearMatchesPlan=output.actualNearMatchesPlan&&std::memcmp(mapping.nearBytes,plan.preparedBytes,R::ArenaUsedBytes)==0;
    Check(output.actualNearMatchesPlan,"Source commit preserves registered relay/table/literal bytes, including coherent negative candidate");
    output.eventsVerified=Events()&&(!matrix||MatrixEvents(stage,faultWindow));Check(output.eventsVerified,"Actual event sequence exactly matches real operations and simulated failure boundary");
    if(matrix){bool failedSeen=false;for(U32 e=0;e<receipt.eventCount;++e)if(receipt.events[e].stage==S::Failure){failedSeen=true;Check(receipt.events[e].window==faultWindow,"Failure event identifies exact window");}
        Check(failedSeen==(stage!=0),"Actual normal/failure event terminal evidence");}
    const std::vector<U8> terminal(reinterpret_cast<U8*>(&receipt),reinterpret_cast<U8*>(&receipt)+sizeof(receipt));
    SetLastError(LastErrorMarker);const auto repeated=S::Commit(&request,&receipt);output.terminal=repeated==S::InvalidInput&&std::memcmp(&receipt,terminal.data(),sizeof(receipt))==0;
    output.lastError=output.lastError&&GetLastError()==LastErrorMarker;Check(output.terminal&&output.lastError,"Actual second Commit rejects terminal receipt without changing one byte or LastError");
    output.privateWrites=writtenBytes!=0;output.committed=CountBits(receipt.committedWindows);output.attempts=receipt.sourceWriteAttempts;output.completions=receipt.sourceWriteCompletions;output.outstanding=receipt.protectionOutstanding;
    output.events=receipt.eventCount;output.state=receipt.state;output.failureWindow=receipt.failureWindow;
    output.committedMask=receipt.committedWindows;output.attemptedMask=receipt.attemptedWindows;output.preparedMask=receipt.preparedWindows;
    output.deleted=mapping.Delete();Check(output.deleted,"Actual table deletion succeeds before owned allocation may be freed");
    if(output.registered){for(U32 site=0;site<23;++site){DWORD64 base{};const auto* after=RtlLookupFunctionEntry(plan.sites[site].relay,&base,nullptr);Check(after==nullptr,"No removed relay RF remains registered after actual deletion");}}
    output.checks=checks-beforeChecks;output.passed=failures==beforeFailures;cases.push_back(output);
}
std::string Escape(const std::string& value){std::string result;for(const unsigned char byte:value){if(byte=='"'||byte=='\\'){result+='\\';result+=static_cast<char>(byte);}else result+=byte<' '?'?':static_cast<char>(byte);}return result;}
std::string Json(const std::string& diskHash,const std::string& fatal){const bool passed=failures==0&&cases.size()==226&&registeredTablesDeleted;
    U32 stages{},preflight{},normalCount{};for(const auto& value:cases){if(value.stage)++stages;else if(value.name=="normal")++normalCount;else ++preflight;}
    std::ostringstream out;out<<"{\"schemaVersion\":1,\"kind\":\"engine-transport-source-writes\",\"pid\":"<<GetCurrentProcessId()<<",\"passed\":"<<(passed?"true":"false")
        <<",\"checks\":"<<checks<<",\"failures\":"<<failures<<",\"caseCount\":"<<cases.size()<<",\"normalCases\":"<<normalCount<<",\"stageFailureCases\":"<<stages<<",\"preflightCases\":"<<preflight
        <<",\"fixtureExecuted\":true,\"actualSourceWrites\":"<<(totalWritten?"true":"false")<<",\"privateMappingOnly\":"<<(allPrivate?"true":"false")
        <<",\"mappedImageNeverExecuted\":true,\"registeredTablesDeleted\":"<<(registeredTablesDeleted&&registeredCount==deletedCount?"true":"false")
        <<",\"sourceDiskUnchanged\":"<<(sourceDiskUnchanged?"true":"false")<<",\"simulatedStageFailures\":true,\"actualOsFailureClaims\":false"
        <<",\"relayCodeExecuted\":false,\"gameExecuted\":false,\"mappedImageExecuted\":false,\"installerExecuted\":false,\"productionAdmissionVerified\":false,\"productionInstallerExecuted\":false,\"sourceDiskPatched\":false"
        <<",\"registeredTableCount\":"<<registeredCount<<",\"deletedTableCount\":"<<deletedCount<<",\"sourceBytesWritten\":"<<totalWritten
        <<",\"fixedDiskSha256\":\""<<diskHash<<"\",\"fatal\":\""<<Escape(fatal)<<"\",\"normal\":{\"passed\":"<<(!cases.empty()&&cases[0].passed?"true":"false")
        <<",\"sourceWriteCompletions\":"<<(cases.empty()?0:cases[0].completions)<<",\"committedWindows\":"<<(cases.empty()?0:cases[0].committedMask)
        <<",\"outstandingProtection\":"<<(cases.empty()?0:cases[0].outstanding)<<"},\"cases\":[";
    for(std::size_t index=0;index<cases.size();++index){const auto& value=cases[index];if(index)out<<',';
        out<<"{\"name\":\""<<value.name<<"\",\"passed\":"<<(value.passed?"true":"false")<<",\"checks\":"<<value.checks<<",\"result\":"<<value.result<<",\"expected\":"<<value.expected
            <<",\"profile\":"<<value.profile<<",\"faultStage\":"<<value.stage<<",\"faultWindow\":"<<value.window<<",\"committedCount\":"<<value.committed
            <<",\"sourceWriteAttempts\":"<<value.attempts<<",\"sourceWriteCompletions\":"<<value.completions<<",\"sourceBytesWritten\":"<<value.bytes<<",\"outstandingProtection\":"<<value.outstanding
            <<",\"committedWindows\":"<<value.committedMask<<",\"attemptedWindows\":"<<value.attemptedMask<<",\"preparedWindows\":"<<value.preparedMask
            <<",\"eventCount\":"<<value.events<<",\"state\":"<<value.state<<",\"failureWindow\":"<<value.failureWindow
            <<",\"actualPrivateSourceWrites\":"<<(value.privateWrites?"true":"false")<<",\"terminalReceiptUnchanged\":"<<(value.terminal?"true":"false")
            <<",\"lastErrorPreserved\":"<<(value.lastError?"true":"false")<<",\"tableRegistered\":"<<(value.registered?"true":"false")<<",\"tableDeleted\":"<<(value.deleted?"true":"false")
            <<",\"sourceBytesVerified\":"<<(value.sourceVerified?"true":"false")<<",\"protectionsVerified\":"<<(value.protectionVerified?"true":"false")
            <<",\"eventsVerified\":"<<(value.eventsVerified?"true":"false")<<",\"simulatedStageFailure\":"<<(value.stage?"true":"false")
            <<",\"coherentRelayTamper\":"<<(value.coherentRelayTamper?"true":"false")<<",\"returnPcAtRfEnd\":"<<(value.returnPcAtRfEnd?"true":"false")
            <<",\"actualNearMatchesPlan\":"<<(value.actualNearMatchesPlan?"true":"false")
            <<",\"fixtureResetPreflightProtection\":"<<(value.fixtureResetProtection?"true":"false")<<",\"windows\":"<<value.windows<<'}';}
    out<<"]}\n";return out.str();}
bool Absolute(const wchar_t* path){return path&&((std::wcslen(path)>3&&path[1]==L':'&&(path[2]==L'/'||path[2]==L'\\'))||(path[0]==L'\\'&&path[1]==L'\\'));}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=4||std::wcscmp(argv[1],L"--all")||!Absolute(argv[2])||!Absolute(argv[3])||_wcsicmp(argv[2],argv[3])==0){std::fputs("Usage: --all <absolute-fixed-Unity-disk-path> <absolute-child-json-path>\n",stderr);return 2;}
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);std::string diskHash,fatal;
    try{Must(GetModuleHandleW(L"UnityPlayer.dll")==nullptr&&GetModuleHandleW(L"GameAssembly.dll")==nullptr,"No game DLL is loaded or executed by the fixture");
        const auto disk=Read(argv[2]);diskHash=Hash(disk);Must(diskHash==F::UnitySha256,"Actual fixed disk SHA matches generated facts");
        Run(disk,0);constexpr U32 stages[]{S::ProtectBegin,S::Protected,S::WriteBegin,S::Readback,S::Restored,S::Flushed};
        for(const U32 stage:stages)for(U32 window=0;window<36;++window)Run(disk,0,stage,window);
        for(U32 kind=1;kind<=9;++kind)Run(disk,kind);Check(cases.size()==226,"Exact 1 normal + 216 stage/window + 9 preflight cases completed");
        sourceDiskUnchanged=Hash(Read(argv[2]))==diskHash;Check(sourceDiskUnchanged,"Actual game disk remains read only and byte-identical");
        Check(registeredTablesDeleted&&registeredCount==225&&deletedCount==registeredCount,"Every actually registered table was deleted before freeing its owned allocation");
    }catch(const std::exception& error){fatal=error.what();Check(false,"Unexpected actual fixture setup/runtime exception");}
    catch(...){fatal="Unexpected native exception in owned fixture";Check(false,"Unexpected native fixture exception");}
    const auto json=Json(diskHash,fatal);FILE* file{};if(_wfopen_s(&file,argv[3],L"wb")!=0||!file){std::fputs("Cannot persist actual child JSON.\n",stderr);return 1;}
    const bool wrote=std::fwrite(json.data(),1,json.size(),file)==json.size();const bool flushed=std::fflush(file)==0;const bool closed=std::fclose(file)==0;
    if(!wrote||!flushed||!closed){std::fputs("Actual child JSON persistence failed.\n",stderr);return 1;}
    if(std::fwrite(json.data(),1,json.size(),stdout)!=json.size()||std::fflush(stdout)!=0)return 1;return failures||cases.size()!=226?1:0;
}
