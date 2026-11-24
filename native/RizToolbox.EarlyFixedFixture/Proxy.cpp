#define RIZ_EARLY_PROXY_BUILD
#include "EarlyFixedShared.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include <intrin.h>

namespace {
HINSTANCE self;
RizFixedConfigV1 fixedConfig;
RizFixedBindingV1 fixedBinding;
struct Image { HMODULE module; std::uint32_t bytes; };
// Every object has constant/zero initialization. No CRT, allocation, logger,
// dynamic instruction decoder or late DLL load runs in this producer.
void Copy(unsigned char* out, const unsigned char* in, std::size_t count)
{ auto* destination=static_cast<volatile unsigned char*>(out);const auto* source=static_cast<const volatile unsigned char*>(in);for (std::size_t i = 0; i != count; ++i) destination[i] = source[i]; }
bool Equal(const unsigned char* a, const unsigned char* b, std::size_t count)
{ unsigned diff = 0; for (std::size_t i = 0; i != count; ++i) diff |= a[i] ^ b[i]; return diff == 0; }
std::uint32_t Rotate(std::uint32_t value, unsigned count) { return (value >> count) | (value << (32 - count)); }
struct Sha256 {
    std::uint32_t state[8]; unsigned char pending[64]; std::uint64_t bytes; unsigned used;
    void Initialize()
    {
        constexpr std::uint32_t initial[8]{ 0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,
            0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19 };
        for (unsigned i = 0; i != 8; ++i) state[i] = initial[i]; bytes = 0; used = 0;
    }
    void Block()
    {
        constexpr std::uint32_t k[64]{ 0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,
            0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,
            0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,
            0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,
            0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,
            0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,
            0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,
            0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,
            0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2 };
        std::uint32_t w[64];
        for (unsigned i = 0; i != 16; ++i) w[i] = (std::uint32_t(pending[i*4]) << 24)
            | (std::uint32_t(pending[i*4+1]) << 16) | (std::uint32_t(pending[i*4+2]) << 8) | pending[i*4+3];
        for (unsigned i = 16; i != 64; ++i) w[i] = w[i-16] + (Rotate(w[i-15],7)^Rotate(w[i-15],18)^(w[i-15]>>3))
            + w[i-7] + (Rotate(w[i-2],17)^Rotate(w[i-2],19)^(w[i-2]>>10));
        auto a=state[0], b=state[1], c=state[2], d=state[3], e=state[4], f=state[5], g=state[6], h=state[7];
        for (unsigned i = 0; i != 64; ++i) {
            const auto t1=h+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+k[i]+w[i];
            const auto t2=(Rotate(a,2)^Rotate(a,13)^Rotate(a,22))+((a&b)^(a&c)^(b&c));
            h=g;g=f;f=e;e=d+t1;d=c;c=b;b=a;a=t1+t2;
        }
        state[0]+=a;state[1]+=b;state[2]+=c;state[3]+=d;state[4]+=e;state[5]+=f;state[6]+=g;state[7]+=h;
    }
    void Feed(const unsigned char* data, unsigned count)
    { bytes += count; for (unsigned i=0;i!=count;++i) { pending[used++]=data[i]; if(used==64){Block();used=0;} } }
    void Finish(unsigned char* out)
    {
        const auto bits=bytes*8;const unsigned char one=0x80,zero=0;Feed(&one,1);
        while(used!=56)Feed(&zero,1);
        unsigned char suffix[8];for(unsigned i=0;i!=8;++i)suffix[i]=static_cast<unsigned char>(bits>>(56-i*8));Feed(suffix,8);
        for(unsigned i=0;i!=8;++i)for(unsigned j=0;j!=4;++j)out[i*4+j]=static_cast<unsigned char>(state[i]>>(24-j*8));
    }
};
std::uint64_t Event(std::uint32_t type, unsigned kind, unsigned result=0, unsigned flags=0,
    std::uint64_t value0=0, std::uint64_t value1=0)
{
    auto* shared=RizEarlyRecorder_GetShared();const auto& tuple=shared->tuples[kind<4?kind:0];
    return RizEarlyRecorder_Event(type,0,kind,kind,result,flags,reinterpret_cast<std::uint64_t>(self),
        tuple.target,tuple.original,tuple.entry,tuple.epoch,value0,value1,0);
}
[[noreturn]] void Fail(unsigned failure, bool uncertain=false)
{ RizEarlyRecorder_Fail(failure,uncertain?1u:0u); }
bool Accessible(const void* pointer,std::size_t bytes,HMODULE image,bool executable,DWORD* protection=nullptr)
{
    const auto begin=reinterpret_cast<std::uintptr_t>(pointer);
    if(!begin||!bytes||begin>UINTPTR_MAX-bytes)return false;
    auto at=begin;const auto end=begin+bytes;
    while(at<end){
        MEMORY_BASIC_INFORMATION memory;
        if(!VirtualQuery(reinterpret_cast<const void*>(at),&memory,sizeof(memory))||memory.State!=MEM_COMMIT
            ||(image&&(memory.AllocationBase!=image||memory.Type!=MEM_IMAGE))||(memory.Protect&(PAGE_NOACCESS|PAGE_GUARD)))return false;
        const auto kind=memory.Protect&0xffu;
        if(executable&&kind!=PAGE_EXECUTE&&kind!=PAGE_EXECUTE_READ&&kind!=PAGE_EXECUTE_READWRITE&&kind!=PAGE_EXECUTE_WRITECOPY)return false;
        if(at==begin&&protection)*protection=memory.Protect;
        const auto next=reinterpret_cast<std::uintptr_t>(memory.BaseAddress)+memory.RegionSize;
        if(next<=at)return false;at=next<end?next:end;
    }return true;
}
Image ValidateImage(HMODULE module,const RizEarlyImageConfigV1& expected,unsigned index,unsigned char* digest)
{
    if(!module||!Accessible(module,sizeof(IMAGE_DOS_HEADER),module,false))Fail(RizEarlyBadModule);
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x100000)Fail(RizEarlyBadImage);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const unsigned char*>(module)+dos->e_lfanew);
    if(!Accessible(nt,sizeof(*nt),module,false)||nt->Signature!=IMAGE_NT_SIGNATURE
        ||nt->FileHeader.Machine!=(RizEarlyRecorder_GetShared()->header.config.mode==RizEarlyWrongArchitecture?IMAGE_FILE_MACHINE_I386:IMAGE_FILE_MACHINE_AMD64)
        ||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||!nt->OptionalHeader.SizeOfImage
        ||nt->FileHeader.TimeDateStamp!=expected.timeDateStamp||expected.reserved)Fail(RizEarlyBadImage);
    wchar_t path[1024];const auto length=GetModuleFileNameW(module,path,1024);
    if(!length||length>=1024)Fail(RizEarlyBadModule);
    const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)Fail(RizEarlyBadHash);
    LARGE_INTEGER size;
    if(!GetFileSizeEx(file,&size)||size.QuadPart<=0||static_cast<std::uint64_t>(size.QuadPart)!=expected.fileBytes
        ||expected.fileBytes>128u*1024u*1024u){CloseHandle(file);Fail(RizEarlyBadHash);}
    Sha256 hash;hash.Initialize();unsigned char buffer[256];DWORD read=0;std::uint64_t total=0;bool okay=true;
    for(;;){if(!ReadFile(file,buffer,sizeof(buffer),&read,nullptr)){okay=false;break;}if(!read)break;total+=read;hash.Feed(buffer,read);}
    CloseHandle(file);hash.Finish(digest);
    if(!okay||total!=expected.fileBytes||!Equal(digest,expected.sha256,32))Fail(RizEarlyBadHash);
    Event(RizEarlyDependencyVerified,index,0,0,reinterpret_cast<std::uint64_t>(module),expected.fileBytes);
    return Image{module,nt->OptionalHeader.SizeOfImage};
}
void Clear(void* object,std::size_t size)
{auto* destination=static_cast<volatile unsigned char*>(object);for(std::size_t j=0;j!=size;++j)destination[j]=0;}
void SyncTuples()
{
    auto* shared=RizEarlyRecorder_GetShared();
    if(!(shared->publisherSnapshot.flags&RizEarlyInfoAvailable))return;
    for(unsigned k=0;k!=4;++k){auto& tuple=shared->tuples[k];const auto& r=shared->publisherInfo.receipts[k];
        // Baseline and pre-Prepare rejection retain actual target identities
        // from InitTargets. Fresh publisher records do not invent bindings.
        if(r.target){tuple.module=r.module;tuple.target=r.target;tuple.original=r.original;tuple.entry=r.entry;
            tuple.rva=r.rva;tuple.originalProtect=r.originalProtection;tuple.publishedProtect=r.observedProtection;
            Copy(tuple.originalBytes,r.originalBytes,32);Copy(tuple.publishedBytes,r.observedBytes,32);Copy(tuple.moduleSha256,r.moduleSha256,32);}
        tuple.prepared=(r.flags&1)?1:0;tuple.committed=(r.flags&4)?1:0;
        if(r.helperEpoch)tuple.epoch=r.helperEpoch;
    }
}
bool Capture(bool event=true)
{
    const auto result=RizEarlyRecorder_Capture();SyncTuples();
    if(event){const auto* shared=RizEarlyRecorder_GetShared();
        Event(RizEarlySnapshotCaptured,0,result,shared->publisherSnapshot.flags,
            shared->publisherSnapshot.captureCalls,shared->publisherSnapshot.publisherState);}
    return result==0;
}
void RequireNoExecution()
{
    const auto& h=RizEarlyRecorder_GetShared()->header;
    if(h.importerInitializers)Fail(RizEarlyPriorExecution);
    for(unsigned k=0;k!=4;++k)if(h.targetCalls[k])Fail(RizEarlyPriorExecution);
}
void InitTargets(HMODULE module,const unsigned char* digest)
{
    auto* shared=RizEarlyRecorder_GetShared();
    const char* const names[4]{"RizFixedOwnedStart","RizFixedOwnedExecute","RizFixedOwnedRelease","RizFixedOwnedDetach"};
    for(unsigned k=0;k!=4;++k){auto& tuple=shared->tuples[k];tuple.version=RizEarlyVersion;tuple.byteSize=sizeof(tuple);tuple.kind=k;tuple.slot=RizEarlySlot;
        const auto* target=reinterpret_cast<const unsigned char*>(GetProcAddress(module,names[k]));DWORD protect=0;
        if(!Accessible(target,32,module,true,&protect))Fail(RizEarlyBadTarget);
        tuple.module=reinterpret_cast<std::uint64_t>(module);tuple.target=reinterpret_cast<std::uint64_t>(target);
        tuple.rva=static_cast<std::uint32_t>(target-reinterpret_cast<const unsigned char*>(module));
        tuple.originalProtect=protect;tuple.publishedProtect=protect;Copy(tuple.originalBytes,target,32);Copy(tuple.moduleSha256,digest,32);
        if(shared->header.config.mode==RizEarlyWrongPrefix&&k==0){
            // A controlled wrong expectation against the real source byte.
            const volatile unsigned char expected=0xcc;if(*target!=expected)Fail(RizEarlyBadPrefix);
        }
        Event(RizEarlyTargetValidated,k,0,0,tuple.rva,protect);
    }
}
bool KnownPublisher(HMODULE module)
{
    const char* const names[6]{"RizFixedPublisher_Prepare","RizFixedPublisher_Bind","RizFixedPublisher_Commit",
        "RizFixedPublisher_GetInfo","RizFixedPublisher_GetVersion","RizFixedPublisher_GetInfoSize"};
    const void* addresses[6]{reinterpret_cast<void*>(&RizFixedPublisher_Prepare),reinterpret_cast<void*>(&RizFixedPublisher_Bind),
        reinterpret_cast<void*>(&RizFixedPublisher_Commit),reinterpret_cast<void*>(&RizFixedPublisher_GetInfo),
        reinterpret_cast<void*>(&RizFixedPublisher_GetVersion),reinterpret_cast<void*>(&RizFixedPublisher_GetInfoSize)};
    for(unsigned k=0;k!=6;++k)if(addresses[k]!=reinterpret_cast<void*>(GetProcAddress(module,names[k]))
        ||!Accessible(addresses[k],16,module,true))return false;
    return RizFixedPublisher_GetVersion()==RizFixedVersion&&RizFixedPublisher_GetInfoSize()==sizeof(RizFixedInfoV1);
}
std::uint32_t Install()
{
    auto* shared=RizEarlyRecorder_GetShared();auto& h=shared->header;const auto& config=h.config;
    if(InterlockedCompareExchange(&h.producerClaim,1,0)!=0)Fail(RizEarlyAlreadyClaimed);
    h.producerThread=static_cast<LONG>(GetCurrentThreadId());InterlockedExchange(&h.state,RizEarlyInstalling);
    const auto begin=Event(RizEarlyProxyAttachBegin,0,0,0,config.mode,config.faultStage);
    InterlockedExchange64(&h.proxyEnterSequence,static_cast<LONG64>(begin));
    if(config.mode>RizEarlyBaselineOnly||config.faultStage>RizFixedFlushApiFailure||config.faultIndex>3||config.flags
        ||config.expectedHelperAbi!=2)Fail(RizEarlyBadConfig);
    if(config.mode==RizEarlyIncompleteLog){
        for(unsigned j=0;j<=config.eventLimit;++j)Event(RizEarlyOverflowProbe,0,0,1,j,0);
        Fail(RizEarlyEventOverflow,true);
    }
    if(config.mode==RizEarlyReentry)RizEarlyProxy_RunInstall();
    RequireNoExecution();
    const auto targets=GetModuleHandleW(RizEarlyTargetsModuleName);
    const auto helper=GetModuleHandleW(RizEarlyHelperModuleName);
    const auto publisher=GetModuleHandleW(config.mode==RizEarlyMissingDependency
        ?RizEarlyMissingPublisherModuleName:RizEarlyPublisherModuleName);
    if(!targets||!helper||!publisher||!h.recorderAttachSequence||config.mode==RizEarlyDependencyNotReady)Fail(RizEarlyBadDependency);
    h.importerModule=reinterpret_cast<std::uint64_t>(targets);h.helperModule=reinterpret_cast<std::uint64_t>(helper);
    h.publisherModule=reinterpret_cast<std::uint64_t>(publisher);
    unsigned char digests[3][32];
    const auto targetImage=ValidateImage(targets,config.images[0],0,digests[0]);
    ValidateImage(helper,config.images[1],1,digests[1]);ValidateImage(publisher,config.images[2],2,digests[2]);
    if(!KnownPublisher(publisher))Fail(RizEarlyBadDependency);
    if(RizEngineNodeContext_GetAbiVersion()!=(config.mode==RizEarlyWrongAbi?3u:2u)
        ||RizEngineNodeContext_GetLimit()!=32||RizEngineNodeContext_GetFrameSize()!=112
        ||RizEngineNodeContext_GetCompletionSize()!=32)Fail(RizEarlyBadAbi);
    InitTargets(targets,digests[0]);
    if(!Capture())Fail(RizEarlyBadMemory,true);
    // The fixed publisher owns one four-row Prepare, not four Dobby calls.
    Clear(&fixedConfig,sizeof(fixedConfig));fixedConfig.version=RizFixedVersion;fixedConfig.byteSize=sizeof(fixedConfig);
    fixedConfig.module=config.mode==RizEarlyWrongModule?helper:targets;
    fixedConfig.imageSize=targetImage.bytes;fixedConfig.timeDateStamp=config.images[0].timeDateStamp;
    fixedConfig.fileBytes=config.images[0].fileBytes;Copy(fixedConfig.fileSha256,digests[0],32);
    if(config.mode==RizEarlyWrongHash)fixedConfig.fileSha256[0]^=1;
    fixedConfig.fault.stage=config.faultStage;fixedConfig.fault.kind=config.faultIndex;
    if(config.faultStage==RizFixedPartialWrite)fixedConfig.fault.partialWriteBytes=1;
    for(unsigned k=0;k!=4;++k)fixedConfig.targets[k]=reinterpret_cast<void*>(shared->tuples[k].target);
    RequireNoExecution();
    const auto prepareSequence=Event(RizEarlyPrepareGroupBegin,0);
    const auto prepared=RizFixedPublisher_Prepare(&fixedConfig);
    Event(RizEarlyPrepareGroupEnd,0,prepared);
    if(!Capture())Fail(RizEarlyBadMemory,true);
    for(unsigned k=0;k!=4;++k)shared->tuples[k].prepareSequence=prepareSequence;
    if(prepared||shared->publisherInfo.state!=RizFixedPrepared||shared->publisherInfo.preparedCount!=4)Fail(RizEarlyPrepareFailed);
    Clear(&fixedBinding,sizeof(fixedBinding));fixedBinding.version=RizFixedVersion;fixedBinding.byteSize=sizeof(fixedBinding);
    fixedBinding.epoch=shared->publisherInfo.epoch;fixedBinding.helperModule=helper;
    for(unsigned k=0;k!=4;++k){
        RequireNoExecution();auto& tuple=shared->tuples[k];
        tuple.entry=reinterpret_cast<std::uint64_t>(RizEngineNodeContext_GetEntry(k,RizEarlySlot));
        if(!tuple.entry||!Accessible(reinterpret_cast<void*>(tuple.entry),16,helper,true))Fail(RizEarlyBadTarget);
        RizEngineNodeInfoV1 before;Clear(&before,sizeof(before));before.abiVersion=1;before.byteSize=sizeof(before);
        if(RizEngineNodeContext_GetInfo(k,RizEarlySlot,&before)!=2||before.kind!=k||before.slot!=RizEarlySlot
            ||reinterpret_cast<std::uint64_t>(before.entry)!=tuple.entry||before.target||before.original||before.epoch
            ||before.attached||before.unknown||before.entered||before.exited||before.inFlight)Fail(RizEarlyConfigureFailed);
        tuple.configureSequence=Event(RizEarlyConfigureBegin,k);
        RizEngineNodeConfigV1 description;Clear(&description,sizeof(description));description.abiVersion=1;description.byteSize=sizeof(description);
        description.target=reinterpret_cast<void*>(tuple.target);description.original=reinterpret_cast<void*>(tuple.original);
        const auto configured=RizEngineNodeContext_Configure(k,RizEarlySlot,&description);
        Event(RizEarlyConfigureEnd,k,configured);
        if(configured)Fail(RizEarlyConfigureFailed);
        RizEngineNodeInfoV1 actual;Clear(&actual,sizeof(actual));actual.abiVersion=1;actual.byteSize=sizeof(actual);
        if(RizEngineNodeContext_GetInfo(k,RizEarlySlot,&actual)||actual.kind!=k||actual.slot!=RizEarlySlot
            ||actual.target!=description.target||actual.original!=description.original||reinterpret_cast<std::uint64_t>(actual.entry)!=tuple.entry
            ||!actual.epoch||actual.attached||actual.unknown||actual.entered||actual.exited||actual.inFlight
            ||actual.enterFaults||actual.exitFaults||actual.snapshotFaults||actual.outputFaults)Fail(RizEarlyConfigureFailed);
        tuple.epoch=actual.epoch;tuple.configured=1;
        fixedBinding.entries[k]=actual.entry;fixedBinding.helperEpochs[k]=actual.epoch;fixedBinding.helperSlots[k]=RizEarlySlot;
    }
    RequireNoExecution();Event(RizEarlyBindGroupBegin,0);
    const auto bound=RizFixedPublisher_Bind(&fixedBinding);Event(RizEarlyBindGroupEnd,0,bound);
    if(!Capture())Fail(RizEarlyBadMemory,true);
    if(bound||shared->publisherInfo.state!=RizFixedBound||shared->publisherInfo.boundCount!=4)Fail(RizEarlyConfigureFailed);
    for(unsigned k=0;k!=4;++k){
        RequireNoExecution();auto& tuple=shared->tuples[k];
        const auto attempt=Event(RizEarlyCommitBegin,k);
        const auto committed=RizFixedPublisher_Commit(k);
        const auto completion=Event(RizEarlyCommitEnd,k,committed);
        if(!Capture())Fail(RizEarlyBadMemory,true);
        const auto& actual=shared->publisherInfo.receipts[k];
        if(actual.sourceWriteAttempts)tuple.writeAttemptSequence=attempt;
        if(actual.flags&4)tuple.writeCompleteSequence=completion;
        if(committed)Fail(RizEarlyCommitFailed);
    }
    RequireNoExecution();
    if(!Capture())Fail(RizEarlyBadMemory,true);
    if(shared->publisherInfo.state!=RizFixedFixtureReady||shared->publisherInfo.nextCommit!=4
        ||shared->publisherInfo.sourceWriteAttempts!=4||shared->publisherInfo.sourceWriteCompletions!=4
        ||shared->publisherInfo.protectionOutstanding)Fail(RizEarlyPostWriteMismatch,true);
    const auto ready=Event(RizEarlyReady,0,0,0,h.writeAttempts,h.writeCompletions);
    InterlockedExchange64(&h.readySequence,static_cast<LONG64>(ready));InterlockedExchange(&h.state,RizEarlyFixtureReady);return 0;
}
}
RIZ_EARLY_PROXY_API std::uint32_t __cdecl RizEarlyProxy_Anchor()
{const auto* shared=RizEarlyRecorder_GetShared();Event(RizEarlyProxyAnchor,0,0,0,shared->header.state);return RizEarlyVersion;}
RIZ_EARLY_PROXY_API const RizEarlyTupleV1* __cdecl RizEarlyProxy_Inspect(std::uint32_t kind)
{return kind<4?&RizEarlyRecorder_GetShared()->tuples[kind]:nullptr;}
RIZ_EARLY_PROXY_API std::uint32_t __cdecl RizEarlyProxy_RunInstall()
{
    __try{return Install();}
    __except(EXCEPTION_EXECUTE_HANDLER){Fail(RizEarlyUnexpectedException,true);}
}
extern "C" BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID)
{
    if(reason!=DLL_PROCESS_ATTACH)return TRUE;self=module;
    auto* shared=RizEarlyRecorder_GetShared();shared->header.proxyModule=reinterpret_cast<std::uint64_t>(module);
    const auto mode=shared->header.config.mode;
    if(mode==RizEarlyPriorCall||mode==RizEarlyLate||mode==RizEarlyBaselineOnly){
        Event(RizEarlyInstallDeferred,0,0,0,mode);if(!Capture())Fail(RizEarlyBadMemory,true);return TRUE;
    }
    RizEarlyProxy_RunInstall();if(mode==RizEarlyDuplicate)RizEarlyProxy_RunInstall();return TRUE;
}
