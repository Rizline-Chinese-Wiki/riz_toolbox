#define RIZ_EARLY_PROXY_BUILD
#include "EarlyLoaderShared.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include <intrin.h>

extern "C" __declspec(dllimport) int __cdecl DobbyPrepare(void*, void*, void**);
extern "C" __declspec(dllimport) int __cdecl DobbyCommit(void*);

namespace {
HINSTANCE self;
void* originals[4];
struct Image { HMODULE module; std::uint32_t bytes; };
constexpr unsigned char PinnedDobbySha[32]{ 0x80,0x15,0xde,0x7d,0x86,0x72,0x45,0xa1,
    0x09,0x5d,0x13,0x94,0x7a,0x63,0x76,0x38,0x78,0xe4,0xcf,0x5f,0xd3,0xd3,0x08,0x9b,
    0x63,0xcc,0x39,0x20,0x0b,0x05,0x5d,0xed };
constexpr unsigned char PreparePrefix[32]{ 0x48,0x89,0x74,0x24,0x10,0x48,0x89,0x7c,
    0x24,0x18,0x41,0x56,0x48,0x83,0xec,0x30,0x4d,0x8b,0xf0,0x48,0x8b,0xf2,0x48,0x8b,
    0xf9,0x48,0x85,0xc9,0x75,0x4d,0x48,0x8d };
constexpr unsigned char CommitPrefix[32]{ 0x40,0x53,0x48,0x83,0xec,0x30,0x48,0x8b,
    0xd9,0x48,0x85,0xc9,0x75,0x42,0x48,0x8d,0x05,0x2b,0xd9,0x01,0x00,0x44,0x8d,0x49,
    0x40,0x48,0x89,0x44,0x24,0x20,0xb9,0xff };

void Copy(unsigned char* out, const unsigned char* in, std::size_t count)
{ for (std::size_t i = 0; i != count; ++i) out[i] = in[i]; }
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
        MEMORY_BASIC_INFORMATION memory{};
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
    wchar_t path[1024]{};const auto length=GetModuleFileNameW(module,path,1024);
    if(!length||length>=1024)Fail(RizEarlyBadModule);
    const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)Fail(RizEarlyBadHash);
    LARGE_INTEGER size{};
    if(!GetFileSizeEx(file,&size)||size.QuadPart<=0||static_cast<std::uint64_t>(size.QuadPart)!=expected.fileBytes
        ||expected.fileBytes>128u*1024u*1024u){CloseHandle(file);Fail(RizEarlyBadHash);}
    Sha256 hash;hash.Initialize();unsigned char buffer[4096];DWORD read=0;std::uint64_t total=0;bool okay=true;
    for(;;){if(!ReadFile(file,buffer,sizeof(buffer),&read,nullptr)){okay=false;break;}if(!read)break;total+=read;hash.Feed(buffer,read);}
    CloseHandle(file);hash.Finish(digest);
    unsigned char expectedHash[32];Copy(expectedHash,expected.sha256,32);
    // Negative fixture changes the expected identity, never the loaded image.
    if(index==0&&RizEarlyRecorder_GetShared()->header.config.mode==RizEarlyWrongHash)expectedHash[0]^=1;
    if(!okay||total!=expected.fileBytes||!Equal(digest,expectedHash,32))Fail(RizEarlyBadHash);
    Event(RizEarlyDependencyVerified,index,0,0,reinterpret_cast<std::uint64_t>(module),expected.fileBytes);
    return Image{module,nt->OptionalHeader.SizeOfImage};
}
void RequireNoExecution()
{
    const auto& h=RizEarlyRecorder_GetShared()->header;
    if(h.importerInitializers)Fail(RizEarlyPriorExecution);
    for(unsigned k=0;k!=4;++k)if(h.targetCalls[k])Fail(RizEarlyPriorExecution);
}
void RequireOriginalBytes()
{
    auto* shared=RizEarlyRecorder_GetShared();
    for(unsigned k=0;k!=4;++k){const auto& tuple=shared->tuples[k];
        if(!Accessible(reinterpret_cast<void*>(tuple.target),32,reinterpret_cast<HMODULE>(tuple.module),true)
            ||!Equal(reinterpret_cast<const unsigned char*>(tuple.target),tuple.originalBytes,32))Fail(RizEarlyPostWriteMismatch,true);}
}
void Inject(unsigned stage,unsigned index)
{
    auto* shared=RizEarlyRecorder_GetShared();
    if(shared->header.config.faultStage==stage&&shared->header.config.faultIndex==index){
        shared->tuples[index].failure=RizEarlyInjectedFailure;Fail(RizEarlyInjectedFailure);}
}
bool Reaches(void* source,void* destination)
{
    auto at=reinterpret_cast<std::uintptr_t>(source);
    for(unsigned step=0;step!=5;++step){
        if(at==reinterpret_cast<std::uintptr_t>(destination))return true;
        if(!Accessible(reinterpret_cast<void*>(at),16,nullptr,true))return false;
        const auto* p=reinterpret_cast<const unsigned char*>(at);
        if(p[0]==0xe9)at=at+5+*reinterpret_cast<const std::int32_t*>(p+1);
        else if(p[0]==0xff&&p[1]==0x25){
            const auto slot=at+6+*reinterpret_cast<const std::int32_t*>(p+2);
            if(!Accessible(reinterpret_cast<void*>(slot),8,nullptr,false))return false;
            at=*reinterpret_cast<const std::uintptr_t*>(slot);
        }else if(p[0]==0x48&&p[1]==0xb8&&p[10]==0xff&&p[11]==0xe0)at=*reinterpret_cast<const std::uintptr_t*>(p+2);
        else return false;
    }return false;
}
std::uint32_t Install()
{
    auto* shared=RizEarlyRecorder_GetShared();auto& h=shared->header;const auto& config=h.config;
    if(InterlockedCompareExchange(&h.producerClaim,1,0)!=0)Fail(RizEarlyAlreadyClaimed);
    h.producerThread=static_cast<LONG>(GetCurrentThreadId());
    InterlockedExchange(&h.state,RizEarlyInstalling);
    const auto begin=Event(RizEarlyProxyAttachBegin,0,0,0,config.mode,config.faultStage);
    InterlockedExchange64(&h.proxyEnterSequence,static_cast<LONG64>(begin));
    if(config.mode>RizEarlyDependencyNotReady||config.faultStage>RizEarlyAfterCommit
        ||config.faultIndex>3||config.flags||config.expectedHelperAbi!=2)Fail(RizEarlyBadConfig);
    if(config.mode==RizEarlyIncompleteLog){
        // Exhaust the actual fixed recorder, including its overflow branch.
        // No manufactured "overflow observed" flag stands in for a write.
        for(unsigned i=0;i<=config.eventLimit;++i)Event(RizEarlyOverflowProbe,0,0,1,i,0);
        Fail(RizEarlyEventOverflow,true);
    }
    if(config.mode==RizEarlyReentry)RizEarlyProxy_RunInstall();
    RequireNoExecution();
    const auto importer=GetModuleHandleW(L"RizOwnedImporter.dll");
    const auto helper=GetModuleHandleW(L"RizToolbox.JudgeContext.dll");
    const auto dobby=GetModuleHandleW(config.mode==RizEarlyMissingDependency?L"RizOwnedMissingDependency.dll":L"dobby.dll");
    if(!importer||!helper||!dobby||!h.recorderAttachSequence||config.mode==RizEarlyDependencyNotReady)Fail(RizEarlyBadDependency);
    h.importerModule=reinterpret_cast<std::uint64_t>(importer);h.helperModule=reinterpret_cast<std::uint64_t>(helper);h.dobbyModule=reinterpret_cast<std::uint64_t>(dobby);
    unsigned char hashes[3][32];
    const auto source=ValidateImage(importer,config.images[0],0,hashes[0]);
    ValidateImage(helper,config.images[1],1,hashes[1]);const auto patcher=ValidateImage(dobby,config.images[2],2,hashes[2]);
    if(!Equal(hashes[2],PinnedDobbySha,32)||patcher.bytes!=0x38000
        ||reinterpret_cast<void*>(&DobbyPrepare)!=GetProcAddress(dobby,"DobbyPrepare")
        ||reinterpret_cast<void*>(&DobbyCommit)!=GetProcAddress(dobby,"DobbyCommit"))Fail(RizEarlyBadDependency);
    const auto* prepare=reinterpret_cast<const unsigned char*>(dobby)+0x3060;
    const auto* commit=reinterpret_cast<const unsigned char*>(dobby)+0x2d30;
    if(!Accessible(prepare,32,dobby,true)||!Accessible(commit,32,dobby,true)
        ||!Equal(prepare,PreparePrefix,32)||!Equal(commit,CommitPrefix,32))Fail(RizEarlyBadDependency);
    if(RizEngineNodeContext_GetAbiVersion()!=(config.mode==RizEarlyWrongAbi?3u:2u)
        ||RizEngineNodeContext_GetLimit()!=32||RizEngineNodeContext_GetFrameSize()!=112
        ||RizEngineNodeContext_GetCompletionSize()!=32)Fail(RizEarlyBadAbi);
    const char* names[4]{"OwnedEarlyStart","OwnedEarlyExecute","OwnedEarlyRelease","OwnedEarlyDetach"};
    for(unsigned k=0;k!=4;++k){
        auto& tuple=shared->tuples[k];tuple.version=1;tuple.byteSize=sizeof(tuple);tuple.kind=k;tuple.slot=RizEarlySlot;
        auto* target=reinterpret_cast<void*>(GetProcAddress(importer,names[k]));
        if(config.mode==RizEarlyWrongModule&&k==0)target=reinterpret_cast<void*>(GetProcAddress(helper,"RizEngineNodeContext_GetLimit"));
        const auto base=reinterpret_cast<std::uintptr_t>(importer),address=reinterpret_cast<std::uintptr_t>(target);
        DWORD originalProtection=0;
        if(address<base||address-base>source.bytes||source.bytes-(address-base)<40
            ||!Accessible(target,40,importer,true,&originalProtection))Fail(RizEarlyBadTarget);
        tuple.originalProtect=originalProtection;
        tuple.module=base;tuple.target=address;tuple.rva=static_cast<std::uint32_t>(address-base);
        for(unsigned j=0;j!=32;++j)if(static_cast<unsigned char*>(target)[j]!=(config.mode==RizEarlyWrongPrefix&&k==0&&j==0?0xcc:0x90))Fail(RizEarlyBadPrefix);
        Copy(tuple.originalBytes,static_cast<unsigned char*>(target),32);Copy(tuple.moduleSha256,hashes[0],32);
        tuple.entry=reinterpret_cast<std::uint64_t>(RizEngineNodeContext_GetEntry(k,RizEarlySlot));
        if(!tuple.entry||!Accessible(reinterpret_cast<void*>(tuple.entry),16,helper,true))Fail(RizEarlyBadTarget);
        for(unsigned j=0;j!=k;++j){const auto other=shared->tuples[j].target;
            if((tuple.target<other+40&&other<tuple.target+40)||tuple.entry==shared->tuples[j].entry)Fail(RizEarlyBadTarget);}
        RizEngineNodeInfoV1 empty{1,sizeof(empty)};
        if(RizEngineNodeContext_GetInfo(k,RizEarlySlot,&empty)!=2||empty.kind!=k||empty.slot!=RizEarlySlot
            ||reinterpret_cast<std::uint64_t>(empty.entry)!=tuple.entry||empty.target||empty.original||empty.epoch
            ||empty.attached||empty.unknown||empty.entered||empty.exited||empty.inFlight
            ||empty.enterFaults||empty.exitFaults||empty.snapshotFaults||empty.outputFaults)Fail(RizEarlyConfigureFailed);
        Event(RizEarlyTargetValidated,k,0,0,tuple.rva,tuple.originalProtect);
    }
    for(unsigned k=0;k!=4;++k){
        auto& tuple=shared->tuples[k];RequireNoExecution();RequireOriginalBytes();
        tuple.prepareSequence=Event(RizEarlyPrepareBegin,k);Inject(RizEarlyBeforePrepare,k);
        const auto status=DobbyPrepare(reinterpret_cast<void*>(tuple.target),reinterpret_cast<void*>(tuple.entry),&originals[k]);
        tuple.original=reinterpret_cast<std::uint64_t>(originals[k]);
        Event(RizEarlyPrepareEnd,k,static_cast<unsigned>(status));
        if(status||!tuple.original||tuple.original==tuple.target||tuple.original==tuple.entry
            ||!Accessible(originals[k],16,nullptr,true))Fail(RizEarlyPrepareFailed);
        for(unsigned j=0;j!=4;++j)if(tuple.original==shared->tuples[j].target
            ||tuple.original==shared->tuples[j].entry||(j<k&&tuple.original==shared->tuples[j].original))Fail(RizEarlyPrepareFailed);
        InterlockedExchange(&tuple.prepared,1);RequireOriginalBytes();Inject(RizEarlyAfterPrepare,k);
    }
    // Complete all four preparations before permanently claiming any helper
    // configuration, and complete all configurations before the first write.
    for(unsigned k=0;k!=4;++k){
        auto& tuple=shared->tuples[k];RequireNoExecution();RequireOriginalBytes();
        tuple.configureSequence=Event(RizEarlyConfigureBegin,k);Inject(RizEarlyBeforeConfigure,k);
        RizEngineNodeConfigV1 description{1,sizeof(description),reinterpret_cast<void*>(tuple.target),originals[k]};
        const auto configured=RizEngineNodeContext_Configure(k,RizEarlySlot,&description);
        Event(RizEarlyConfigureEnd,k,configured);
        if(configured)Fail(RizEarlyConfigureFailed);
        RizEngineNodeInfoV1 info{1,sizeof(info)};
        if(RizEngineNodeContext_GetInfo(k,RizEarlySlot,&info)||info.target!=description.target
            ||info.original!=description.original||reinterpret_cast<std::uint64_t>(info.entry)!=tuple.entry
            ||!info.epoch||info.attached||info.unknown||info.entered||info.exited||info.inFlight)Fail(RizEarlyConfigureFailed);
        tuple.epoch=info.epoch;InterlockedExchange(&tuple.configured,1);RequireOriginalBytes();Inject(RizEarlyAfterConfigure,k);
    }
    RequireNoExecution();RequireOriginalBytes();
    for(unsigned k=0;k!=4;++k){
        auto& tuple=shared->tuples[k];RequireNoExecution();
        const auto before=Event(RizEarlyCommitBegin,k);Inject(RizEarlyBeforeCommit,k);
        tuple.writeAttemptSequence=before;InterlockedIncrement(&h.writeAttempts);
        const auto result=DobbyCommit(reinterpret_cast<void*>(tuple.target));
        const auto after=Event(RizEarlyCommitEnd,k,static_cast<unsigned>(result));
        tuple.writeCompleteSequence=after;
        if(result)Fail(RizEarlyCommitFailed);
        InterlockedIncrement(&h.writeCompletions);InterlockedExchange(&tuple.committed,1);
        DWORD publishedProtection=0;
        if(!Accessible(reinterpret_cast<void*>(tuple.target),32,importer,true,&publishedProtection))Fail(RizEarlyBadMemory);
        tuple.publishedProtect=publishedProtection;
        Copy(tuple.publishedBytes,reinterpret_cast<const unsigned char*>(tuple.target),32);
        if(Equal(tuple.originalBytes,tuple.publishedBytes,32)||tuple.originalProtect!=tuple.publishedProtect
            ||!Reaches(reinterpret_cast<void*>(tuple.target),reinterpret_cast<void*>(tuple.entry)))Fail(RizEarlyPostWriteMismatch);
        Inject(RizEarlyAfterCommit,k);
    }
    RequireNoExecution();
    for(unsigned k=0;k!=4;++k){
        const auto& tuple=shared->tuples[k];DWORD protection=0;
        if(!Accessible(reinterpret_cast<void*>(tuple.target),32,importer,true,&protection)
            ||protection!=tuple.publishedProtect||!Equal(reinterpret_cast<const unsigned char*>(tuple.target),tuple.publishedBytes,32)
            ||!Reaches(reinterpret_cast<void*>(tuple.target),reinterpret_cast<void*>(tuple.entry)))Fail(RizEarlyPostWriteMismatch);
        RizEngineNodeInfoV1 info{1,sizeof(info)};
        if(RizEngineNodeContext_GetInfo(k,RizEarlySlot,&info)||info.kind!=k||info.slot!=RizEarlySlot
            ||reinterpret_cast<std::uint64_t>(info.target)!=tuple.target||reinterpret_cast<std::uint64_t>(info.original)!=tuple.original
            ||reinterpret_cast<std::uint64_t>(info.entry)!=tuple.entry||info.epoch!=tuple.epoch||info.attached||info.unknown
            ||info.entered||info.exited||info.inFlight||info.enterFaults||info.exitFaults||info.snapshotFaults||info.outputFaults)Fail(RizEarlyConfigureFailed);
    }
    const auto ready=Event(RizEarlyReady,0,0,0,h.writeAttempts,h.writeCompletions);
    InterlockedExchange64(&h.readySequence,static_cast<LONG64>(ready));
    InterlockedExchange(&h.state,RizEarlyFixtureReady);
    return 0;
}
}
RIZ_EARLY_PROXY_API std::uint32_t __cdecl RizEarlyProxy_Anchor()
{
    auto* shared=RizEarlyRecorder_GetShared();
    Event(RizEarlyProxyAnchor,0,0,0,static_cast<std::uint64_t>(shared->header.state));return RizEarlyVersion;
}
RIZ_EARLY_PROXY_API const RizEarlyTupleV1* __cdecl RizEarlyProxy_Inspect(std::uint32_t kind)
{ return kind<4?&RizEarlyRecorder_GetShared()->tuples[kind]:nullptr; }
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
    if(mode==RizEarlyPriorCall||mode==RizEarlyLate){Event(RizEarlyInstallDeferred,0,0,0,mode);return TRUE;}
    RizEarlyProxy_RunInstall();
    if(mode==RizEarlyDuplicate)RizEarlyProxy_RunInstall();
    return TRUE;
}
