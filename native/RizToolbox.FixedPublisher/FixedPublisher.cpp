#define RIZ_FIXED_PUBLISHER_BUILD
#include "FixedPublisher.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include <intrin.h>

extern "C" { void* RizFixedContinuation[4]{}; }
namespace {
// Constant/zero initialized storage. No allocation, CRT, TLS object, dynamic
// instruction decoding, arbitrary callback, logger or late module load.
RizFixedInfoV1 information{};
RizFixedFaultV1 selectedFault{};
volatile LONG gate{};
void* const originalStubs[4]{reinterpret_cast<void*>(&RizFixedOriginalStart),
    reinterpret_cast<void*>(&RizFixedOriginalExecute),reinterpret_cast<void*>(&RizFixedOriginalRelease),
    reinterpret_cast<void*>(&RizFixedOriginalDetach)};
constexpr unsigned lengths[4]{15,18,16,15};
constexpr unsigned char prefixes[4][18]{
    {0x48,0x89,0x5c,0x24,0x08,0x48,0x89,0x74,0x24,0x18,0x48,0x89,0x7c,0x24,0x20},
    {0x40,0x55,0x53,0x57,0x41,0x56,0x48,0x8d,0x6c,0x24,0x88,0x48,0x81,0xec,0x78,0x01,0x00,0x00},
    {0x40,0x53,0x48,0x83,0xec,0x20,0xff,0x49,0x60,0x48,0x8b,0xd9,0x83,0x79,0x60,0x00},
    {0x48,0x89,0x6c,0x24,0x20,0x57,0x48,0x83,0xec,0x20,0x48,0x8b,0xf9,0x33,0xed}
};
constexpr unsigned char marker[16]{'R','I','Z','F','I','X','E','D','O','W','N','E','D','V','1',0};

void Copy(void* output,const void* input,std::size_t count)
{auto* out=static_cast<volatile unsigned char*>(output);const auto* in=static_cast<const volatile unsigned char*>(input);for(std::size_t i=0;i!=count;++i)out[i]=in[i];}
bool Equal(const void* left,const void* right,std::size_t count)
{const auto* a=static_cast<const volatile unsigned char*>(left);const auto* b=static_cast<const volatile unsigned char*>(right);unsigned diff=0;for(std::size_t i=0;i!=count;++i)diff|=a[i]^b[i];return diff==0;}
bool Zero(const std::uint32_t* values,unsigned count)
{for(unsigned i=0;i!=count;++i)if(values[i])return false;return true;}
std::uint32_t Rotate(std::uint32_t value,unsigned count){return(value>>count)|(value<<(32-count));}
struct Sha256 {
    std::uint32_t state[8];unsigned char pending[64];std::uint64_t bytes;unsigned used;
    void Initialize(){constexpr std::uint32_t initial[8]{0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};for(unsigned i=0;i!=8;++i)state[i]=initial[i];bytes=0;used=0;}
    void Block(){
        constexpr std::uint32_t k[64]{0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
            0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,
            0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,
            0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,
            0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
            0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,
            0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
        std::uint32_t w[64];for(unsigned i=0;i!=16;++i)w[i]=(std::uint32_t(pending[i*4])<<24)|(std::uint32_t(pending[i*4+1])<<16)|(std::uint32_t(pending[i*4+2])<<8)|pending[i*4+3];
        for(unsigned i=16;i!=64;++i)w[i]=w[i-16]+(Rotate(w[i-15],7)^Rotate(w[i-15],18)^(w[i-15]>>3))+w[i-7]+(Rotate(w[i-2],17)^Rotate(w[i-2],19)^(w[i-2]>>10));
        auto a=state[0],b=state[1],c=state[2],d=state[3],e=state[4],f=state[5],g=state[6],h=state[7];
        for(unsigned i=0;i!=64;++i){const auto t1=h+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+k[i]+w[i];const auto t2=(Rotate(a,2)^Rotate(a,13)^Rotate(a,22))+((a&b)^(a&c)^(b&c));h=g;g=f;f=e;e=d+t1;d=c;c=b;b=a;a=t1+t2;}
        state[0]+=a;state[1]+=b;state[2]+=c;state[3]+=d;state[4]+=e;state[5]+=f;state[6]+=g;state[7]+=h;
    }
    void Feed(const unsigned char* data,unsigned count){bytes+=count;for(unsigned i=0;i!=count;++i){pending[used++]=data[i];if(used==64){Block();used=0;}}}
    void Finish(unsigned char* out){const auto bits=bytes*8;const unsigned char one=0x80,zero=0;Feed(&one,1);while(used!=56)Feed(&zero,1);unsigned char suffix[8];for(unsigned i=0;i!=8;++i)suffix[i]=static_cast<unsigned char>(bits>>(56-i*8));Feed(suffix,8);for(unsigned i=0;i!=8;++i)for(unsigned j=0;j!=4;++j)out[i*4+j]=static_cast<unsigned char>(state[i]>>(24-j*8));}
};
bool Memory(const void* pointer,std::size_t bytes,HMODULE module,bool executable,bool writable,DWORD* protection=nullptr)
{
    const auto start=reinterpret_cast<std::uintptr_t>(pointer);if(!start||!bytes||start>UINTPTR_MAX-bytes)return false;
    auto at=start;const auto end=start+bytes;
    while(at<end){MEMORY_BASIC_INFORMATION m{};if(!VirtualQuery(reinterpret_cast<const void*>(at),&m,sizeof(m))||m.State!=MEM_COMMIT
            ||(m.Protect&(PAGE_GUARD|PAGE_NOACCESS))||(module&&(m.AllocationBase!=module||m.Type!=MEM_IMAGE)))return false;
        const auto p=m.Protect&0xffu;
        if(executable&&p!=PAGE_EXECUTE&&p!=PAGE_EXECUTE_READ&&p!=PAGE_EXECUTE_READWRITE&&p!=PAGE_EXECUTE_WRITECOPY)return false;
        if(writable&&p!=PAGE_READWRITE&&p!=PAGE_WRITECOPY&&p!=PAGE_EXECUTE_READWRITE&&p!=PAGE_EXECUTE_WRITECOPY)return false;
        if(at==start&&protection)*protection=m.Protect;
        const auto next=reinterpret_cast<std::uintptr_t>(m.BaseAddress)+m.RegionSize;if(next<=at)return false;at=next<end?next:end;
    }return true;
}
void InitializeInfo()
{information.version=RizFixedVersion;information.byteSize=sizeof(information);for(unsigned k=0;k!=4;++k){auto& r=information.receipts[k];r.version=RizFixedVersion;r.byteSize=sizeof(r);r.kind=k;r.prefixBytes=lengths[k];}}
void Failed(unsigned failure)
{
    if(!information.failure)information.failure=failure;
    information.state=information.sourceWriteAttempts||information.protectionOutstanding||information.state==RizFixedUnknown
        ?RizFixedUnknown:RizFixedDisabledNoSourceWrite;
}
bool Record(unsigned stage,unsigned kind,unsigned result=0,unsigned error=0,unsigned flags=0)
{
    if(information.eventCount==RizFixedEventCapacity){information.overflow=1;information.state=RizFixedUnknown;Failed(RizFixedLogOverflow);return false;}
    auto& event=information.events[information.eventCount++];event.sequence=information.eventCount;
    event.stage=stage;event.kind=kind;event.state=information.state;event.failure=information.failure;
    event.result=result;event.win32Error=error;event.flags=flags;event.epoch=information.epoch;
    if(kind<4){const auto& r=information.receipts[kind];event.target=r.target;event.original=r.original;event.entry=r.entry;
        event.sourceBytesWritten=r.sourceBytesWritten;event.originalProtection=r.originalProtection;event.observedProtection=r.observedProtection;}
    return true;
}
bool Reject(unsigned failure,unsigned kind=4,unsigned error=0)
{Failed(failure);Record(RizFixedFailureObserved,kind,failure,error);return false;}
bool Owner(){return information.ownerThread==GetCurrentThreadId();}
bool Fault(unsigned stage,unsigned kind)
{return selectedFault.stage==stage&&selectedFault.kind==kind;}
bool Inject(unsigned stage,unsigned kind)
{if(!Fault(stage,kind))return false;if(!information.failure)information.failure=RizFixedFaultInjected;Record(RizFixedFailureObserved,kind,RizFixedFaultInjected,0,RizFixedEventInjected);return true;}
bool HashFile(HMODULE module,const RizFixedConfigV1& config,unsigned char* digest)
{
    wchar_t path[1024];const auto length=GetModuleFileNameW(module,path,1024);if(!length||length>=1024)return false;
    const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return false;
    LARGE_INTEGER size{};bool okay=GetFileSizeEx(file,&size)&&size.QuadPart>0&&static_cast<std::uint64_t>(size.QuadPart)==config.fileBytes
        &&config.fileBytes<=128u*1024u*1024u;
    // Keep this no-CRT frame below the stack-probe threshold as well as using
    // bounded storage. SHA consumes a stream; it needs no 4 KiB file buffer.
    Sha256 hash;hash.Initialize();unsigned char buffer[256];std::uint64_t total=0;
    if(okay)for(;;){DWORD read=0;if(!ReadFile(file,buffer,sizeof(buffer),&read,nullptr)){okay=false;break;}if(!read)break;total+=read;hash.Feed(buffer,read);}
    CloseHandle(file);hash.Finish(digest);return okay&&total==config.fileBytes&&Equal(digest,config.fileSha256,32);
}
bool ImageHeader(HMODULE module,unsigned size,unsigned stamp)
{
    if(!Memory(module,sizeof(IMAGE_DOS_HEADER),module,false,false))return false;
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x100000)return false;
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const unsigned char*>(module)+dos->e_lfanew);
    return Memory(nt,sizeof(*nt),module,false,false)&&nt->Signature==IMAGE_NT_SIGNATURE&&nt->FileHeader.Machine==IMAGE_FILE_MACHINE_AMD64
        &&nt->OptionalHeader.Magic==IMAGE_NT_OPTIONAL_HDR64_MAGIC&&nt->OptionalHeader.SizeOfImage==size&&nt->FileHeader.TimeDateStamp==stamp
        &&(nt->FileHeader.Characteristics&IMAGE_FILE_DLL)!=0;
}
bool VerifyAllSources()
{
    for(unsigned k=0;k!=4;++k){auto& r=information.receipts[k];DWORD protection=0;
        const auto* expected=(r.flags&4)?r.patchBytes:r.originalBytes;
        if(!Memory(reinterpret_cast<const void*>(r.target),32,reinterpret_cast<HMODULE>(r.module),true,false,&protection)
            ||protection!=r.originalProtection||!Equal(reinterpret_cast<const void*>(r.target),expected,32))return Reject(RizFixedOriginalChanged,k);
        r.observedProtection=protection;
    }return true;
}
bool Prepare(const RizFixedConfigV1& config)
{
    InitializeInfo();
    if(information.claimed||information.state!=RizFixedFresh)return Reject(RizFixedWrongState);
    information.claimed=1;information.ownerThread=GetCurrentThreadId();information.state=RizFixedPreparing;
    Record(RizFixedPrepareBegin,4);
    if(config.version!=RizFixedVersion||config.byteSize!=sizeof(config)||!Zero(config.reserved,4)||config.fault.reserved
        ||config.fault.stage>RizFixedFlushApiFailure||config.fault.kind>=4
        ||(config.fault.stage==RizFixedPartialWrite?(config.fault.partialWriteBytes==0||config.fault.partialWriteBytes>=lengths[config.fault.kind]):config.fault.partialWriteBytes!=0))return Reject(RizFixedInvalidRecord);
    selectedFault=config.fault;
    const auto module=GetModuleHandleW(RizFixedPublisherProfile::TargetsModule);const auto self=GetModuleHandleW(RizFixedPublisherProfile::PublisherModule);
    if(!module||!self||module!=config.module||module==self)return Reject(RizFixedWrongModule);
    if(!ImageHeader(module,config.imageSize,config.timeDateStamp))return Reject(RizFixedWrongImage);
    const auto* identity=reinterpret_cast<const unsigned char*>(GetProcAddress(module,"RizFixedOwnedMarker"));
    DWORD markerProtection=0;
    if(!Memory(identity,16,module,false,false,&markerProtection)||!Equal(identity,marker,16)
        ||(markerProtection&0xffu)!=PAGE_READONLY)return Reject(RizFixedWrongModule);
    unsigned char digest[32];if(!HashFile(module,config,digest))return Reject(RizFixedWrongHash);
    const char* const names[4]{"RizFixedOwnedStart","RizFixedOwnedExecute","RizFixedOwnedRelease","RizFixedOwnedDetach"};
    SYSTEM_INFO system{};GetSystemInfo(&system);if(!system.dwPageSize)return Reject(RizFixedWrongMemory);
    for(unsigned k=0;k!=4;++k){auto& r=information.receipts[k];const auto target=GetProcAddress(module,names[k]);
        if(!target||reinterpret_cast<void*>(target)!=config.targets[k])return Reject(RizFixedWrongModule,k);
        const auto address=reinterpret_cast<std::uintptr_t>(target),base=reinterpret_cast<std::uintptr_t>(module);
        DWORD protection=0;
        if(address<base||address-base>config.imageSize||config.imageSize-(address-base)<32
            ||!Memory(reinterpret_cast<void*>(address),32,module,true,false,&protection)
            ||protection!=PAGE_EXECUTE_READ||address/system.dwPageSize!=(address+31)/system.dwPageSize)return Reject(RizFixedWrongMemory,k);
        if(!Equal(reinterpret_cast<void*>(address),prefixes[k],lengths[k]))return Reject(RizFixedWrongPrefix,k);
        for(unsigned j=0;j!=k;++j)if(address<information.receipts[j].target+32&&information.receipts[j].target<address+32)return Reject(RizFixedOverlappingRange,k);
        if(!Memory(originalStubs[k],lengths[k]+10,self,true,false)||!Equal(originalStubs[k],prefixes[k],lengths[k]))return Reject(RizFixedWrongPrefix,k);
        const auto* tail=static_cast<const unsigned char*>(originalStubs[k])+lengths[k];
        if(tail[0]!=0x4c||tail[1]!=0x8b||tail[2]!=0x1d||tail[7]!=0x41||tail[8]!=0xff||tail[9]!=0xe3)return Reject(RizFixedWrongPrefix,k);
        const auto slot=reinterpret_cast<std::uintptr_t>(tail)+7+*reinterpret_cast<const std::int32_t*>(tail+3);
        if(slot!=reinterpret_cast<std::uintptr_t>(&RizFixedContinuation[k])||RizFixedContinuation[k])return Reject(RizFixedWrongPrefix,k);
        r.module=base;r.target=address;r.original=reinterpret_cast<std::uint64_t>(originalStubs[k]);r.continuation=address+lengths[k];r.rva=static_cast<std::uint32_t>(address-base);
        r.originalProtection=protection;r.observedProtection=protection;Copy(r.originalBytes,reinterpret_cast<void*>(address),32);Copy(r.moduleSha256,digest,32);
        Record(RizFixedPrepareValidated,k);
    }
    // No continuation is published until all four rows pass validation.
    information.epoch=1;
    for(unsigned k=0;k!=4;++k){auto& r=information.receipts[k];r.epoch=information.epoch;
        InterlockedExchangePointer(&RizFixedContinuation[k],reinterpret_cast<void*>(r.continuation));r.flags|=1;++information.preparedCount;Record(RizFixedContinuationBound,k);}
    information.state=RizFixedPrepared;Record(RizFixedPrepareEnd,4);return true;
}
using HelperVersion=std::uint32_t(__cdecl*)();
using HelperEntry=void*(__cdecl*)(std::uint32_t,std::uint32_t);
using HelperInfo=std::uint32_t(__cdecl*)(std::uint32_t,std::uint32_t,RizEngineNodeInfoV1*);
bool Bind(const RizFixedBindingV1& binding)
{
    if(information.state!=RizFixedPrepared||!Owner())return Reject(Owner()?RizFixedWrongState:RizFixedWrongOwner);
    if(binding.version!=RizFixedVersion||binding.byteSize!=sizeof(binding)||binding.epoch!=information.epoch||!Zero(binding.reserved,4))return Reject(RizFixedInvalidRecord);
    Record(RizFixedBindBegin,4);
    const auto helper=GetModuleHandleW(RizFixedPublisherProfile::HelperModule);
    if(!helper||helper!=binding.helperModule)return Reject(RizFixedWrongModule);
    const auto version=reinterpret_cast<HelperVersion>(GetProcAddress(helper,"RizEngineNodeContext_GetAbiVersion"));
    const auto entry=reinterpret_cast<HelperEntry>(GetProcAddress(helper,"RizEngineNodeContext_GetEntry"));
    const auto info=reinterpret_cast<HelperInfo>(GetProcAddress(helper,"RizEngineNodeContext_GetInfo"));
    if(!Memory(reinterpret_cast<void*>(version),16,helper,true,false)||!Memory(reinterpret_cast<void*>(entry),16,helper,true,false)
        ||!Memory(reinterpret_cast<void*>(info),16,helper,true,false)||version()!=2)return Reject(RizFixedInvalidEntry);
    if(!VerifyAllSources())return false;
    for(unsigned k=0;k!=4;++k){const auto& r=information.receipts[k];
        if(binding.helperSlots[k]!=RizFixedHelperSlot||!binding.helperEpochs[k]||!binding.entries[k]
            ||!Memory(binding.entries[k],16,helper,true,false)||entry(k,RizFixedHelperSlot)!=binding.entries[k])return Reject(RizFixedInvalidEntry,k);
        for(unsigned j=0;j!=4;++j)if(reinterpret_cast<std::uint64_t>(binding.entries[k])==information.receipts[j].target
            ||reinterpret_cast<std::uint64_t>(binding.entries[k])==information.receipts[j].original||(j<k&&binding.entries[k]==binding.entries[j]))return Reject(RizFixedInvalidEntry,k);
        RizEngineNodeInfoV1 actual;
        auto* actualBytes=reinterpret_cast<volatile unsigned char*>(&actual);
        for(std::size_t j=0;j!=sizeof(actual);++j)actualBytes[j]=0;
        actual.abiVersion=1;actual.byteSize=sizeof(actual);
        if(info(k,RizFixedHelperSlot,&actual)||actual.abiVersion!=1||actual.byteSize!=sizeof(actual)||actual.kind!=k||actual.slot!=RizFixedHelperSlot
            ||actual.attached||actual.unknown||actual.entered||actual.exited||actual.inFlight||actual.enterFaults||actual.exitFaults||actual.snapshotFaults||actual.outputFaults
            ||reinterpret_cast<std::uint64_t>(actual.target)!=r.target||reinterpret_cast<std::uint64_t>(actual.original)!=r.original
            ||actual.entry!=binding.entries[k]||actual.epoch!=binding.helperEpochs[k])return Reject(RizFixedInvalidEntry,k);
    }
    for(unsigned k=0;k!=4;++k){auto& r=information.receipts[k];r.entry=reinterpret_cast<std::uint64_t>(binding.entries[k]);r.helperEpoch=binding.helperEpochs[k];r.helperSlot=binding.helperSlots[k];
        Copy(r.patchBytes,r.originalBytes,32);auto* patch=reinterpret_cast<volatile unsigned char*>(r.patchBytes);
        patch[0]=0xff;patch[1]=0x25;for(unsigned j=2;j!=6;++j)patch[j]=0;
        for(unsigned j=0;j!=8;++j)patch[6+j]=static_cast<unsigned char>(r.entry>>(j*8));for(unsigned j=14;j<lengths[k];++j)patch[j]=0x90;
        r.flags|=2;++information.boundCount;Record(RizFixedEntryBound,k);}
    information.state=RizFixedBound;Record(RizFixedBindEnd,4);return true;
}
bool Restore(unsigned kind,bool cleanup)
{
    auto& r=information.receipts[kind];if(!r.protectionOutstanding)return true;
    const auto flags=cleanup?RizFixedEventCleanup:0u;
    Record(RizFixedRestoreBegin,kind,0,0,flags);
    DWORD ignored=0;BOOL okay=FALSE;DWORD error=0;
    if(Fault(RizFixedRestoreApiFailure,kind)){error=ERROR_ACCESS_DENIED;Record(RizFixedRestoreEnd,kind,0,error,flags|RizFixedEventInjected);}
    else{++r.restoreAttempts;SetLastError(ERROR_SUCCESS);okay=VirtualProtect(reinterpret_cast<void*>(r.target),r.prefixBytes,r.originalProtection,&ignored);error=okay?ERROR_SUCCESS:GetLastError();
        Record(RizFixedRestoreEnd,kind,okay?1u:0u,error,flags|RizFixedEventApiCalled|(okay?RizFixedEventSucceeded:0u));}
    r.lastWin32Error=error;
    DWORD actual=0;
    if(!okay||!Memory(reinterpret_cast<void*>(r.target),32,reinterpret_cast<HMODULE>(r.module),true,false,&actual)||actual!=r.originalProtection){r.observedProtection=actual;return Reject(RizFixedRestoreFailed,kind,error);}
    r.observedProtection=actual;++r.restoreSucceeded;r.protectionOutstanding=0;--information.protectionOutstanding;return true;
}
bool Abort(unsigned kind,unsigned failure)
{
    if(!information.failure)information.failure=failure;
    Record(RizFixedFailureObserved,kind,failure);
    if(information.receipts[kind].protectionOutstanding)Restore(kind,true);
    // Decide the terminal state after the pre-write cleanup attempt. There is
    // no provisional Unknown -> Disabled transition: once set, Unknown stays.
    Failed(failure);
    return false;
}
bool Commit(unsigned kind)
{
    if(!Owner()||kind>=4||(information.state!=RizFixedBound&&information.state!=RizFixedPublishing)||kind!=information.nextCommit)return Reject(!Owner()?RizFixedWrongOwner:RizFixedWrongState,kind);
    if(!VerifyAllSources())return false;
    auto& r=information.receipts[kind];information.state=RizFixedPublishing;Record(RizFixedCommitBegin,kind);
    if(Inject(RizFixedBeforeProtect,kind))return Abort(kind,RizFixedFaultInjected);
    Record(RizFixedProtectBegin,kind);DWORD prior=0,error=0;BOOL protectedOkay=FALSE;
    if(Fault(RizFixedProtectApiFailure,kind)){error=ERROR_ACCESS_DENIED;Record(RizFixedProtectEnd,kind,0,error,RizFixedEventInjected);}
    else{++r.protectAttempts;SetLastError(ERROR_SUCCESS);protectedOkay=VirtualProtect(reinterpret_cast<void*>(r.target),r.prefixBytes,PAGE_EXECUTE_READWRITE,&prior);error=protectedOkay?ERROR_SUCCESS:GetLastError();
        Record(RizFixedProtectEnd,kind,protectedOkay?1u:0u,error,RizFixedEventApiCalled|(protectedOkay?RizFixedEventSucceeded:0u));}
    r.lastWin32Error=error;if(!protectedOkay)return Abort(kind,RizFixedProtectFailed);
    ++r.protectSucceeded;r.protectionOutstanding=1;++information.protectionOutstanding;r.observedProtection=PAGE_EXECUTE_READWRITE;
    if(prior!=r.originalProtection)return Abort(kind,RizFixedOriginalChanged);
    if(Inject(RizFixedAfterProtect,kind)||Inject(RizFixedBeforeWrite,kind))return Abort(kind,RizFixedFaultInjected);
    Record(RizFixedSourceWriteBegin,kind);++r.sourceWriteAttempts;++information.sourceWriteAttempts;
    const auto count=Fault(RizFixedPartialWrite,kind)?selectedFault.partialWriteBytes:r.prefixBytes;
    auto* target=reinterpret_cast<volatile unsigned char*>(r.target);
    for(unsigned j=0;j!=count;++j){target[j]=r.patchBytes[j];++r.sourceBytesWritten;}
    Record(RizFixedSourceWriteEnd,kind,1,0,count==r.prefixBytes?RizFixedEventSucceeded:(RizFixedEventInjected|RizFixedEventPartial));
    if(count!=r.prefixBytes)return Abort(kind,RizFixedWriteFailed);
    ++information.sourceWriteCompletions;
    if(Inject(RizFixedAfterWrite,kind)||Inject(RizFixedBeforeReadback,kind))return Abort(kind,RizFixedFaultInjected);
    Record(RizFixedReadbackBegin,kind);++r.readbackAttempts;Copy(r.observedBytes,reinterpret_cast<void*>(r.target),32);
    const bool equal=Equal(r.observedBytes,r.patchBytes,32);Record(RizFixedReadbackEnd,kind,equal?1u:0u,0,equal?RizFixedEventSucceeded:0u);
    if(!equal)return Abort(kind,RizFixedReadbackFailed);++r.readbackVerified;
    if(Inject(RizFixedAfterReadback,kind)||Inject(RizFixedBeforeRestore,kind))return Abort(kind,RizFixedFaultInjected);
    if(!Restore(kind,false))return false;
    if(Inject(RizFixedAfterRestore,kind)||Inject(RizFixedBeforeFlush,kind))return Abort(kind,RizFixedFaultInjected);
    Record(RizFixedFlushBegin,kind);BOOL flushed=FALSE;error=0;
    if(Fault(RizFixedFlushApiFailure,kind)){error=ERROR_GEN_FAILURE;Record(RizFixedFlushEnd,kind,0,error,RizFixedEventInjected);}
    else{++r.flushAttempts;SetLastError(ERROR_SUCCESS);flushed=FlushInstructionCache(GetCurrentProcess(),reinterpret_cast<void*>(r.target),r.prefixBytes);error=flushed?ERROR_SUCCESS:GetLastError();
        Record(RizFixedFlushEnd,kind,flushed?1u:0u,error,RizFixedEventApiCalled|(flushed?RizFixedEventSucceeded:0u));}
    r.lastWin32Error=error;if(!flushed)return Abort(kind,RizFixedFlushFailed);++r.flushSucceeded;
    if(Inject(RizFixedAfterFlush,kind))return Abort(kind,RizFixedFaultInjected);
    DWORD observed=0;if(!Memory(reinterpret_cast<void*>(r.target),32,reinterpret_cast<HMODULE>(r.module),true,false,&observed)
        ||observed!=r.originalProtection||!Equal(reinterpret_cast<void*>(r.target),r.patchBytes,32))return Abort(kind,RizFixedReadbackFailed);
    r.observedProtection=observed;r.flags|=4;++information.nextCommit;Record(RizFixedCommitEnd,kind);
    if(information.nextCommit==4){if(!VerifyAllSources())return false;Record(RizFixedFinalReadback,4);information.state=RizFixedFixtureReady;Record(RizFixedReady,4);}
    return true;
}
bool Enter(){return InterlockedCompareExchange(&gate,1,0)==0;}
void Leave(){InterlockedExchange(&gate,0);}
void ExceptionFailure(unsigned kind)
{Failed(RizFixedNativeException);Record(RizFixedFailureObserved,kind,RizFixedNativeException,GetLastError());if(kind<4&&information.receipts[kind].protectionOutstanding)Restore(kind,true);information.state=RizFixedUnknown;}
}
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_GetVersion(){return RizFixedVersion;}
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_GetInfoSize(){return sizeof(RizFixedInfoV1);}
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_Prepare(const RizFixedConfigV1* config)
{
    if(!Enter())return 2;std::uint32_t result=1;
    __try{if(!Memory(config,sizeof(*config),nullptr,false,false)){result=3;}else{RizFixedConfigV1 copied;Copy(&copied,config,sizeof(copied));result=Prepare(copied)?0u:1u;}}
    __except(EXCEPTION_EXECUTE_HANDLER){ExceptionFailure(4);result=1;}
    Leave();return result;
}
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_Bind(const RizFixedBindingV1* binding)
{
    if(!Enter())return 2;std::uint32_t result=1;
    __try{if(!Memory(binding,sizeof(*binding),nullptr,false,false)){result=3;}else{RizFixedBindingV1 copied;Copy(&copied,binding,sizeof(copied));result=Bind(copied)?0u:1u;}}
    __except(EXCEPTION_EXECUTE_HANDLER){ExceptionFailure(4);result=1;}
    Leave();return result;
}
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_Commit(std::uint32_t kind)
{
    if(!Enter())return 2;std::uint32_t result=1;
    __try{result=Commit(kind)?0u:1u;}
    __except(EXCEPTION_EXECUTE_HANDLER){ExceptionFailure(kind);result=1;}
    Leave();return result;
}
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_GetInfo(RizFixedInfoV1* output)
{
    if(!Enter())return 2;std::uint32_t result=3;
    __try{if(Memory(output,sizeof(*output),nullptr,false,true)&&output->version==RizFixedVersion&&output->byteSize==sizeof(*output)){
        if(!information.version)InitializeInfo();Copy(output,&information,sizeof(*output));result=0;}}
    __except(EXCEPTION_EXECUTE_HANDLER){result=3;}
    Leave();return result;
}
