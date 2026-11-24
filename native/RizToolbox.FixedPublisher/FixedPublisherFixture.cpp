#include "FixedPublisher.h"
#include "FixedFixtureApi.h"
#include "FixedUnwindProbe.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include <bcrypt.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>

namespace {
unsigned checks{}, failures{}, originalCalls{}, observationEnters{}, observationExits{}, exceptions{}, unwindCases{};
__declspec(thread) RizFixedCase* live;
struct Observation { std::uint64_t sequence; unsigned kind; };
__declspec(thread) Observation active[8];
__declspec(thread) unsigned depth;
FILE* callLog{};
bool collectingBaseline{};
DWORD baselineSehFlags[4]{};
void Check(bool value, unsigned line)
{
    ++checks;
    if(!value){++failures;std::fprintf(stderr,"Fixed fixture failure line %u\n",line);}
}
#define CHECK(value) Check(!!(value),__LINE__)
RizFixedInfoV1 Snapshot()
{
    RizFixedInfoV1 info{1,sizeof(info)};
    CHECK(RizFixedPublisher_GetInfo(&info)==0);
    return info;
}
void Save(const std::wstring& path,const void* data,std::size_t size)
{
    FILE* output{};CHECK(_wfopen_s(&output,path.c_str(),L"wb")==0&&output);
    if(output){CHECK(std::fwrite(data,1,size,output)==size);CHECK(std::fclose(output)==0);}
}
bool HashFile(const wchar_t* path,unsigned char* hash,std::uint64_t* bytes)
{
    const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE state{};
    bool okay=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)==0
        &&BCryptCreateHash(algorithm,&state,nullptr,0,nullptr,0,0)==0;
    unsigned char buffer[4096];DWORD read{};*bytes=0;
    while(okay){if(!ReadFile(file,buffer,sizeof(buffer),&read,nullptr)){okay=false;break;}if(!read)break;
        *bytes+=read;okay=BCryptHashData(state,buffer,read,0)==0;}
    if(okay)okay=BCryptFinishHash(state,hash,32,0)==0;
    if(state)BCryptDestroyHash(state);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);
    CloseHandle(file);return okay;
}
std::uintptr_t __cdecl Enter(const RizEngineNodeFrameV1* frame,void*)
{
    CHECK(live&&frame&&depth<8&&frame->kind<4);
    if(!live||!frame||depth>=8)return 0;
    const auto kind=frame->kind;
    CHECK(frame->arg0==(kind==0?static_cast<void*>(live->owner):live->node));
    CHECK(frame->arg1==(kind==0?static_cast<void*>(live->enumerator):kind==1?&live->outputByte:nullptr));
    CHECK(frame->arg2==(kind==0?static_cast<void*>(live->name):nullptr));
    CHECK(frame->arg3==(kind==0?&live->outputNode:nullptr));
    if(kind){CHECK(frame->snapshotFlags==3&&frame->owner==live->owner&&frame->enumCache==live->enumerator);
        CHECK(frame->referenceCount==*reinterpret_cast<std::int32_t*>(live->node+0x60));}
    else CHECK(frame->snapshotFlags==0);
    active[depth++]={frame->sequence,kind};++observationEnters;
    return static_cast<std::uintptr_t>(frame->sequence^0x9a123456789abcdeull);
}
void __cdecl Exit(const RizEngineNodeFrameV1* frame,std::uintptr_t token,const RizEngineNodeCompletionV1* completion,void*)
{
    CHECK(live&&frame&&completion&&depth);
    if(!frame||!completion||!depth)return;
    const auto captured=active[--depth];
    CHECK(captured.sequence==frame->sequence&&captured.kind==frame->kind
        &&token==(frame->sequence^0x9a123456789abcdeull));
    const bool abnormal=live->throwMode&&live->throwKind==frame->kind&&!live->nested;
    CHECK(completion->flags==(abnormal?2u:(frame->kind==3?1u:9u)));
    if(!abnormal){const unsigned returns[]{0x81,0xa5c3fe81u,0xe7,0};CHECK(completion->returnByte==returns[frame->kind]);}
    ++observationExits;
}
void Call(unsigned kind,RizFixedCase* test)
{
    switch(kind){case 0:test->rawReturn=RizFixedOwnedStart(test->owner,test->enumerator,test->name,&test->outputNode);CHECK(test->rawReturn==0x81);break;
        case 1:test->rawReturn=RizFixedOwnedExecute(test->node,&test->outputByte);CHECK(test->rawReturn==0xa5c3fe81u);break;
        case 2:test->rawReturn=RizFixedOwnedRelease(test->node);CHECK(test->rawReturn==0xe7);break;
        default:RizFixedOwnedDetach(test->node);break;}
    test->returned=1;
}
int Filter(EXCEPTION_POINTERS* pointers,unsigned kind)
{
    const auto* record=pointers->ExceptionRecord;
    if(record->ExceptionCode!=RizFixedSehCode)return EXCEPTION_CONTINUE_SEARCH;
    if(collectingBaseline)baselineSehFlags[kind]=record->ExceptionFlags;
    CHECK(record->ExceptionFlags==baselineSehFlags[kind]&&record->NumberParameters==2
        &&record->ExceptionInformation[0]==RizFixedPayload&&record->ExceptionInformation[1]==kind);
    live->exceptionCode=record->ExceptionCode;live->exceptionFlags=record->ExceptionFlags;
    live->exceptionParameters=record->NumberParameters;live->exceptionPayload=record->ExceptionInformation[0];
    live->exceptionKind=record->ExceptionInformation[1];
    ++exceptions;return EXCEPTION_EXECUTE_HANDLER;
}
void CallSeh(unsigned kind,RizFixedCase* test)
{__try{Call(kind,test);CHECK(false);}__except(Filter(GetExceptionInformation(),kind)){} }
void One(unsigned kind,unsigned faultMode,std::uint32_t references)
{
    RizFixedCase test{};test.throwKind=kind;test.throwMode=faultMode;
    *reinterpret_cast<void**>(test.node+0x58)=test.owner;
    *reinterpret_cast<void**>(test.node+0x20)=test.enumerator;
    *reinterpret_cast<std::uint64_t*>(test.node+0x10)=0xf123456789abcdefull;
    *reinterpret_cast<std::uint32_t*>(test.node+0x18)=2;
    *reinterpret_cast<std::uint32_t*>(test.node+0x60)=references;
    live=&test;auto* prior=RizFixedOwnedSetCase(&test);
    const auto beforeEntered=observationEnters,beforeExited=observationExits;
    if(faultMode==1)CallSeh(kind,&test);
    else if(faultMode==2){try{Call(kind,&test);CHECK(false);}catch(const RizFixedOriginalException& error){
        CHECK(error.payload==RizFixedPayload&&error.kind==kind);test.exceptionPayload=error.payload;test.exceptionKind=error.kind;++exceptions;}}
    else Call(kind,&test);
    CHECK(RizFixedOwnedSetCase(prior)==&test);live=nullptr;
    CHECK(test.diagnosticsFailed==0&&depth==0);
    for(unsigned i=0;i!=4;++i){const auto expected=(i==kind?1u:0u)+(!faultMode&&kind==0&&(i==1||i==2)?1u:0u);
        CHECK(test.calls[i]==expected);originalCalls+=test.calls[i];}
    if(kind==2||(!faultMode&&kind==0)){
        CHECK(*reinterpret_cast<std::uint32_t*>(test.node+0x60)==references-1);
        if(!faultMode)CHECK(test.releaseCount==references-1&&test.releaseZeroFlag==((references-1)==0?1u:0u));}
    if(callLog)std::fprintf(callLog,"{\"kind\":%u,\"faultMode\":%u,\"references\":%u,\"baseline\":%s,\"threadId\":%lu,\"returned\":%u,\"rawReturn\":%u,\"calls\":[%u,%u,%u,%u],\"finalNodeRef\":%u,\"releaseCount\":%u,\"releaseZeroFlag\":%u,\"detachZero\":%u,\"diagnosticsFailed\":%u,\"observerEnters\":%u,\"observerExits\":%u,\"exceptionCode\":%u,\"exceptionFlags\":%u,\"exceptionParameters\":%u,\"exceptionPayload\":%llu,\"exceptionKind\":%llu,\"outputByte\":%u,\"outputNodeMatches\":%s}\n",
        kind,faultMode,references,collectingBaseline?"true":"false",GetCurrentThreadId(),test.returned,test.rawReturn,test.calls[0],test.calls[1],test.calls[2],test.calls[3],
        *reinterpret_cast<std::uint32_t*>(test.node+0x60),test.releaseCount,test.releaseZeroFlag,test.detachZero,test.diagnosticsFailed,
        observationEnters-beforeEntered,observationExits-beforeExited,test.exceptionCode,test.exceptionFlags,test.exceptionParameters,
        test.exceptionPayload,test.exceptionKind,static_cast<unsigned>(test.outputByte),test.outputNode==test.node?"true":"false");
}
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=4){std::fprintf(stderr,"Usage: FixedPublisher.Fixture.exe <output-prefix> <stage> <kind>\n");return 2;}
    const std::wstring prefix=argv[1];
    const auto stage=static_cast<unsigned>(_wtoi(argv[2])),selectedKind=static_cast<unsigned>(_wtoi(argv[3]));
    const auto module=GetModuleHandleW(L"RizFixedOwnedTargets.dll");CHECK(module);
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const unsigned char*>(module)+dos->e_lfanew);
    RizFixedConfigV1 config{1,sizeof(config)};config.module=module;
    config.imageSize=nt->OptionalHeader.SizeOfImage;config.timeDateStamp=nt->FileHeader.TimeDateStamp;
    wchar_t path[32768];CHECK(GetModuleFileNameW(module,path,32768)>0);CHECK(HashFile(path,config.fileSha256,&config.fileBytes));
    const char* names[]{"RizFixedOwnedStart","RizFixedOwnedExecute","RizFixedOwnedRelease","RizFixedOwnedDetach"};
    for(unsigned i=0;i!=4;++i)config.targets[i]=reinterpret_cast<void*>(GetProcAddress(module,names[i]));
    config.fault={stage,selectedKind,stage==RizFixedPartialWrite?3u:0u,0};
    CHECK(RizFixedPublisher_Prepare(&config)==0);
    auto prepared=Snapshot();CHECK(prepared.state==RizFixedPrepared&&prepared.preparedCount==4&&prepared.sourceWriteAttempts==0);
    Save(prefix+L".prepared.bin",&prepared,sizeof(prepared));
    FILE* unwind{};CHECK(_wfopen_s(&unwind,(prefix+L".unwind.jsonl").c_str(),L"wb")==0&&unwind);
    if(unwind){unwindCases=RizFixedProbeUnwind(prepared,unwind,&failures);CHECK(std::fclose(unwind)==0);}
    // Do not execute any target if static replay/unwind validation failed.
    if(failures){std::fprintf(stderr,"Unwind/Prepare failed before source publication\n");return 1;}
    if(!stage){
        CHECK(_wfopen_s(&callLog,(prefix+L".calls.jsonl").c_str(),L"wb")==0&&callLog);
        collectingBaseline=true;
        for(unsigned mode=1;mode!=3;++mode)for(unsigned kind=0;kind!=4;++kind)One(kind,mode,3);
        collectingBaseline=false;
    }
    RizFixedBindingV1 binding{1,sizeof(binding)};binding.epoch=prepared.epoch;
    binding.helperModule=GetModuleHandleW(L"RizToolbox.JudgeContext.dll");
    for(unsigned kind=0;kind!=4;++kind){
        binding.entries[kind]=RizEngineNodeContext_GetEntry(kind,RizFixedHelperSlot);binding.helperSlots[kind]=RizFixedHelperSlot;
        RizEngineNodeConfigV1 transport{1,sizeof(transport),config.targets[kind],reinterpret_cast<void*>(prepared.receipts[kind].original)};
        CHECK(RizEngineNodeContext_Configure(kind,RizFixedHelperSlot,&transport)==0);
        RizEngineNodeInfoV1 info{1,sizeof(info)};CHECK(RizEngineNodeContext_GetInfo(kind,RizFixedHelperSlot,&info)==0);
        binding.helperEpochs[kind]=info.epoch;
    }
    CHECK(RizFixedPublisher_Bind(&binding)==0);
    bool ready=true;
    for(unsigned kind=0;kind!=4;++kind){const auto result=RizFixedPublisher_Commit(kind);
        if(stage&&kind==selectedKind){CHECK(result!=0);ready=false;break;}CHECK(result==0);}
    auto info=Snapshot();Save(prefix+L".receipt.bin",&info,sizeof(info));
    struct ActualMemoryRow { std::uint32_t kind,protection;std::uint64_t target,allocationBase;unsigned char bytes[32]; };
    struct ActualMemory { std::uint32_t version,byteSize,systemPageSize,reserved;ActualMemoryRow rows[4]; };
    static_assert(sizeof(ActualMemoryRow)==56&&sizeof(ActualMemory)==240);
    ActualMemory memory{1,sizeof(memory)};
    SYSTEM_INFO system{};GetSystemInfo(&system);memory.systemPageSize=system.dwPageSize;
    for(unsigned kind=0;kind!=4;++kind){
        auto& row=memory.rows[kind];row.kind=kind;row.target=info.receipts[kind].target;
        MEMORY_BASIC_INFORMATION page{};
        CHECK(VirtualQuery(reinterpret_cast<void*>(row.target),&page,sizeof(page))==sizeof(page)&&page.State==MEM_COMMIT);
        row.protection=page.Protect;row.allocationBase=reinterpret_cast<std::uint64_t>(page.AllocationBase);
        std::memcpy(row.bytes,reinterpret_cast<void*>(row.target),sizeof(row.bytes));
    }
    Save(prefix+L".memory.bin",&memory,sizeof(memory));
    if(ready){
        CHECK(info.state==RizFixedFixtureReady&&info.nextCommit==4&&info.sourceWriteCompletions==4);
        for(unsigned kind=0;kind!=4;++kind)One(kind,0,3);
        for(unsigned kind=0;kind!=4;++kind){RizEngineNodeObserverV1 observer{1,sizeof(observer),Enter,Exit,nullptr};
            CHECK(RizEngineNodeContext_Attach(kind,RizFixedHelperSlot,binding.helperEpochs[kind],&observer)==0);}
        for(unsigned kind=0;kind!=4;++kind)One(kind,0,1);
        std::thread worker([]{for(unsigned kind=0;kind!=4;++kind)One(kind,0,3);});worker.join();
        for(unsigned mode=1;mode!=3;++mode)for(unsigned kind=0;kind!=4;++kind)One(kind,mode,3);
        CHECK(exceptions==16&&observationEnters==observationExits);
        if(callLog){CHECK(std::fclose(callLog)==0);callLog=nullptr;}
    }else{
        const auto before=info.sourceWriteAttempts;
        CHECK(info.state==RizFixedUnknown||info.state==RizFixedDisabledNoSourceWrite);
        CHECK(RizFixedPublisher_Commit(selectedKind)!=0);
        CHECK(Snapshot().sourceWriteAttempts==before);
    }
    std::printf("{\"kind\":\"fixed-publisher-fixture\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"faultStage\":%u,\"faultKind\":%u,\"actualOriginalCalls\":%u,\"observerEnters\":%u,\"observerExits\":%u,\"originalExceptionsCaught\":%u,\"controlledUnwindCases\":%u,\"actualExceptionsAtEveryPc\":false,\"gameExecuted\":false,\"firstInstallVerified\":false}\n",
        failures?"false":"true",checks,failures,stage,selectedKind,originalCalls,observationEnters,observationExits,exceptions,unwindCases);
    return failures?1:0;
}
