#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <intrin.h>
#include "EngineTransportFamilies.h"
#include "EngineTransportGameProfile.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/EngineSchedulerContext.h"
#include "../RizToolbox.JudgeContext/EngineWaitContext.h"

#ifdef RIZ_ENGINE_TRANSPORT_OWNED_IMAGE
// Direct link-private verifier owned by the containing publisher. It checks
// the already-held exact generated target file and immutable marker; there is
// no exported setter or caller-provided admission flag.
extern "C" bool __cdecl RizEngineTransportValidateOwnedImage(std::uint64_t target);
#endif
#ifdef RIZ_ENGINE_TRANSPORT_PRODUCTION
extern "C" bool __cdecl RizEngineTransportValidateProductionAdmission(std::uint64_t target);
#endif

// Keep exactly the 35 ordinary Kernel imports, including observer APIs whose
// addresses are verified but which this Configure component never calls.
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetAbiVersion")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetLimit")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetFrameSize")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetCompletionSize")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetEntry")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_Configure")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_Attach")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetInfo")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetInlineAbiVersion")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetInlineLimit")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetInlineFrameSize")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetInlineEntry")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_ConfigureInline")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_AttachInline")
#pragma comment(linker,"/INCLUDE:RizEngineNodeContext_GetInlineInfo")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetAbiVersion")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetLimit")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetFrameSize")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetCompletionSize")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetRowFrameSize")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetEntry")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetDrainRowEntry")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_Configure")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_Attach")
#pragma comment(linker,"/INCLUDE:RizEngineSchedulerContext_GetInfo")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetAbiVersion")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetLimit")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetFrameSize")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetCompletionSize")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetPhaseFrameSize")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetEntry")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetPhaseEntry")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_Configure")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_Attach")
#pragma comment(linker,"/INCLUDE:RizEngineWaitContext_GetInfo")

namespace RizEngineTransportFamilies {
namespace {
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
namespace Fixed=RizEngineTransportGameProfile;
volatile LONG gate{},attempted{},stickyFailure{};
Request retainedRequest{};
Receipt retained{};
bool attachedSeen[FamilyCount]{};
U64 apiAddresses[35]{};
const char* const ApiNames[35]{
    "RizEngineNodeContext_GetAbiVersion","RizEngineNodeContext_GetLimit",
    "RizEngineNodeContext_GetFrameSize","RizEngineNodeContext_GetCompletionSize",
    "RizEngineNodeContext_GetEntry","RizEngineNodeContext_Configure",
    "RizEngineNodeContext_Attach","RizEngineNodeContext_GetInfo",
    "RizEngineNodeContext_GetInlineAbiVersion","RizEngineNodeContext_GetInlineLimit",
    "RizEngineNodeContext_GetInlineFrameSize","RizEngineNodeContext_GetInlineEntry",
    "RizEngineNodeContext_ConfigureInline","RizEngineNodeContext_AttachInline",
    "RizEngineNodeContext_GetInlineInfo","RizEngineSchedulerContext_GetAbiVersion",
    "RizEngineSchedulerContext_GetLimit","RizEngineSchedulerContext_GetFrameSize",
    "RizEngineSchedulerContext_GetCompletionSize","RizEngineSchedulerContext_GetRowFrameSize",
    "RizEngineSchedulerContext_GetEntry","RizEngineSchedulerContext_GetDrainRowEntry",
    "RizEngineSchedulerContext_Configure","RizEngineSchedulerContext_Attach",
    "RizEngineSchedulerContext_GetInfo","RizEngineWaitContext_GetAbiVersion",
    "RizEngineWaitContext_GetLimit","RizEngineWaitContext_GetFrameSize",
    "RizEngineWaitContext_GetCompletionSize","RizEngineWaitContext_GetPhaseFrameSize",
    "RizEngineWaitContext_GetEntry","RizEngineWaitContext_GetPhaseEntry",
    "RizEngineWaitContext_Configure","RizEngineWaitContext_Attach","RizEngineWaitContext_GetInfo"};

// Volatile byte loops avoid CRT aggregate copy/zero helpers under /O2 /Zl.
void Zero(void* destination,std::size_t bytes){auto* out=static_cast<volatile U8*>(destination);for(std::size_t i=0;i<bytes;++i)out[i]=0;}
void Copy(void* destination,const void* source,std::size_t bytes){auto* out=static_cast<volatile U8*>(destination);
    const auto* in=static_cast<const volatile U8*>(source);for(std::size_t i=0;i<bytes;++i)out[i]=in[i];}
bool Equal(const void* left,const void* right,std::size_t bytes){const auto* a=static_cast<const volatile U8*>(left);
    const auto* b=static_cast<const volatile U8*>(right);for(std::size_t i=0;i<bytes;++i)if(a[i]!=b[i])return false;return true;}
bool Empty(const void* value,std::size_t bytes){const auto* p=static_cast<const volatile U8*>(value);for(std::size_t i=0;i<bytes;++i)if(p[i])return false;return true;}
U64 Address(const void* pointer){return reinterpret_cast<U64>(pointer);}
void* Pointer(U64 address){return reinterpret_cast<void*>(address);}
bool Add(U64 a,U64 b,U64* out){if(a>UINT64_MAX-b)return false;*out=a+b;return true;}
bool Overlap(U64 a,U64 na,U64 b,U64 nb){U64 ae{},be{};return !Add(a,na,&ae)||!Add(b,nb,&be)||(a<be&&b<ae);}
bool Memory(U64 start,std::size_t bytes,U64 allocation,bool code,bool write,bool imageOnly=false,DWORD exactType=0){
    U64 end{};if(!start||!bytes||!Add(start,bytes,&end))return false;
    for(U64 at=start;at<end;){MEMORY_BASIC_INFORMATION info;
        Zero(&info,sizeof(info));if(!VirtualQuery(Pointer(at),&info,sizeof(info))||info.State!=MEM_COMMIT
            ||(info.Protect&(PAGE_GUARD|PAGE_NOACCESS))||(allocation&&Address(info.AllocationBase)!=allocation)
            ||(imageOnly&&info.Type!=MEM_IMAGE)||(exactType&&info.Type!=exactType))return false;
        const DWORD protection=info.Protect&0xffu;
        if(code&&protection!=PAGE_EXECUTE_READ)return false;
        if(write&&protection!=PAGE_READWRITE&&protection!=PAGE_WRITECOPY)return false;
        if(!write&&!code&&protection!=PAGE_READONLY&&protection!=PAGE_READWRITE&&protection!=PAGE_WRITECOPY
            &&protection!=PAGE_EXECUTE_READ)return false;
        U64 next{};if(!Add(Address(info.BaseAddress),info.RegionSize,&next)||next<=at)return false;at=next<end?next:end;
    }return true;
}
const IMAGE_NT_HEADERS64* Header(U64 module,bool imageOnly){
    if(!Memory(module,sizeof(IMAGE_DOS_HEADER),module,false,false,imageOnly))return nullptr;
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<64||dos->e_lfanew>0x100000)return nullptr;
    U64 nt{};if(!Add(module,static_cast<U32>(dos->e_lfanew),&nt)||!Memory(nt,sizeof(IMAGE_NT_HEADERS64),module,false,false,imageOnly))return nullptr;
    const auto* value=reinterpret_cast<const IMAGE_NT_HEADERS64*>(nt);
    if(value->Signature!=IMAGE_NT_SIGNATURE||value->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64
        ||value->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||value->OptionalHeader.NumberOfRvaAndSizes<16
        ||value->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64)||value->OptionalHeader.SizeOfImage<4096)return nullptr;
    return value;
}
const U8* ImageAt(U64 module,const IMAGE_NT_HEADERS64* header,U32 rva,std::size_t bytes){
    if(!rva||rva>=header->OptionalHeader.SizeOfImage||bytes>header->OptionalHeader.SizeOfImage-rva)return nullptr;
    U64 address{};if(!Add(module,rva,&address)||!Memory(address,bytes,module,false,false,true))return nullptr;
    return reinterpret_cast<const U8*>(address);
}
U32 Word(const void* data){U32 value;Copy(&value,data,sizeof(value));return value;}
U64 Quad(const void* data){U64 value;Copy(&value,data,sizeof(value));return value;}
bool Name(U64 module,const IMAGE_NT_HEADERS64* header,U32 rva,const char* wanted,bool insensitive=false){
    for(U32 i=0;i<512;++i){if(rva>UINT32_MAX-i)return false;const auto* p=ImageAt(module,header,rva+i,1);if(!p)return false;
        U32 a=*p,b=static_cast<U8>(wanted[i]);if(insensitive){if(a>='A'&&a<='Z')a+='a'-'A';if(b>='A'&&b<='Z')b+='a'-'A';}
        if(a!=b)return false;if(!a)return true;}return false;
}
U32 Rotate(U32 value,U32 count){return(value>>count)|(value<<(32-count));}
struct Sha256 {
    U32 state[8];U8 pending[64];U64 bytes;U32 used;
    void Initialize(){constexpr U32 initial[8]{0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
        Copy(state,initial,sizeof(state));bytes=0;used=0;}
    void Block(){constexpr U32 constants[64]{0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
        U32 words[64];for(U32 i=0;i<16;++i)words[i]=(U32(pending[i*4])<<24)|(U32(pending[i*4+1])<<16)|(U32(pending[i*4+2])<<8)|pending[i*4+3];
        for(U32 i=16;i<64;++i)words[i]=words[i-16]+(Rotate(words[i-15],7)^Rotate(words[i-15],18)^(words[i-15]>>3))
            +words[i-7]+(Rotate(words[i-2],17)^Rotate(words[i-2],19)^(words[i-2]>>10));
        auto a=state[0],b=state[1],c=state[2],d=state[3],e=state[4],f=state[5],g=state[6],h=state[7];
        for(U32 i=0;i<64;++i){const auto one=h+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+constants[i]+words[i];
            const auto two=(Rotate(a,2)^Rotate(a,13)^Rotate(a,22))+((a&b)^(a&c)^(b&c));h=g;g=f;f=e;e=d+one;d=c;c=b;b=a;a=one+two;}
        state[0]+=a;state[1]+=b;state[2]+=c;state[3]+=d;state[4]+=e;state[5]+=f;state[6]+=g;state[7]+=h;}
    void Feed(const U8* data,U32 count){bytes+=count;for(U32 i=0;i<count;++i){pending[used++]=data[i];if(used==64){Block();used=0;}}}
    void Finish(U8* digest){const auto bits=bytes*8;const U8 one=0x80,zero=0;Feed(&one,1);while(used!=56)Feed(&zero,1);
        U8 suffix[8];for(U32 i=0;i<8;++i)suffix[i]=static_cast<U8>(bits>>(56-i*8));Feed(suffix,8);
        for(U32 i=0;i<8;++i)for(U32 j=0;j<4;++j)digest[i*4+j]=static_cast<U8>(state[i]>>(24-j*8));}
};
void Hash(const void* data,U32 bytes,U8* digest){Sha256 hash;hash.Initialize();hash.Feed(static_cast<const U8*>(data),bytes);hash.Finish(digest);}
U8 Hex(char c){return static_cast<U8>(c>='0'&&c<='9'?c-'0':c-'a'+10);}
bool Digest(const U8* digest,const char* expected){for(U32 i=0;i<32;++i)if(digest[i]!=static_cast<U8>((Hex(expected[i*2])<<4)|Hex(expected[i*2+1])))return false;return expected[64]==0;}
Result Fail(Result value,U32 family=NoIndex){retained.state=Failed;retained.result=value;retained.failureFamily=family;
    retained.lastWin32Error=GetLastError();InterlockedExchange(&stickyFailure,1);return value;}
bool ValidEpoch(U64 epoch){return epoch&&epoch<=0x7fffffffffffffffull;}
// GetInfo samples individual atomics, not a transaction. Only independent
// representability is required during later live verification; zero is strict
// during fresh/initial checks below.
bool Counters(U64 entered,U64 exited,U64 flight){return entered<=0x7fffffffffffffffull&&exited<=0x7fffffffffffffffull&&flight<=0x7fffffffffffffffull;}

bool Exports(){const auto& request=retainedRequest;const auto* header=Header(request.kernelModule,true);
    if(!header||header->OptionalHeader.AddressOfEntryPoint||header->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].Size)return false;
    const auto& directory=header->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];
    const auto* raw=ImageAt(request.kernelModule,header,directory.VirtualAddress,sizeof(IMAGE_EXPORT_DIRECTORY));
    if(!raw||directory.Size<sizeof(IMAGE_EXPORT_DIRECTORY))return false;
    const auto* exports=reinterpret_cast<const IMAGE_EXPORT_DIRECTORY*>(raw);
    if(exports->NumberOfFunctions!=35||exports->NumberOfNames!=35)return false;
    const auto* functions=ImageAt(request.kernelModule,header,exports->AddressOfFunctions,35*4);
    const auto* names=ImageAt(request.kernelModule,header,exports->AddressOfNames,35*4);
    const auto* ordinals=ImageAt(request.kernelModule,header,exports->AddressOfNameOrdinals,35*2);
    if(!functions||!names||!ordinals)return false;U64 found=0;
    for(U32 n=0;n<35;++n){U32 selected=NoIndex;const U32 nameRva=Word(names+n*4);
        for(U32 api=0;api<35;++api)if(Name(request.kernelModule,header,nameRva,ApiNames[api])){if(selected!=NoIndex)return false;selected=api;}
        if(selected==NoIndex||(found&(1ull<<selected)))return false;
        std::uint16_t ordinal;Copy(&ordinal,ordinals+n*2,2);if(ordinal>=35)return false;
        const U32 rva=Word(functions+ordinal*4);const U64 exportEnd=U64(directory.VirtualAddress)+directory.Size;
        if(!rva||rva>=header->OptionalHeader.SizeOfImage||(rva>=directory.VirtualAddress&&rva<exportEnd))return false;
        U64 address{};if(!Add(request.kernelModule,rva,&address)||!Memory(address,1,request.kernelModule,true,false,true))return false;
        // Only an initially unfilled slot is written; subsequent Verify checks
        // the exact same EAT address against the first preflight.
        if(apiAddresses[selected]&&apiAddresses[selected]!=address)return false;
        apiAddresses[selected]=address;found|=1ull<<selected;
    }return found==((1ull<<35)-1);
}
bool Imports(){const auto& request=retainedRequest;const auto* header=Header(request.originalModule,true);
    // The inspected ordinary edge must be the module containing THIS component,
    // not another module chosen by a caller that happens to import the Kernel.
    if(!header||!Memory(Address(reinterpret_cast<const void*>(&PrepareAndConfigure)),1,request.originalModule,true,false,true))return false;
    const auto& directory=header->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if(!directory.VirtualAddress||directory.Size<20||directory.Size>1024*1024)return false;
    U32 descriptors=0;bool ended=false;U64 found=0;
    for(U32 offset=0;offset+20<=directory.Size;offset+=20){
        if(directory.VirtualAddress>UINT32_MAX-offset)return false;
        const auto* descriptor=ImageAt(request.originalModule,header,directory.VirtualAddress+offset,20);if(!descriptor)return false;
        if(Empty(descriptor,20)){ended=true;break;}
        if(!Name(request.originalModule,header,Word(descriptor+12),"RizToolbox.EngineNodeKernel.dll",true))continue;
        if(++descriptors!=1)return false;
        const U32 lookup=Word(descriptor),iat=Word(descriptor+16);if(!lookup||!iat)return false;bool terminated=false;
        for(U32 n=0;n<=35;++n){
            if(lookup>UINT32_MAX-n*8u||iat>UINT32_MAX-n*8u)return false;
            const auto* held=ImageAt(request.originalModule,header,lookup+n*8,8);
            const auto* mapped=ImageAt(request.originalModule,header,iat+n*8,8);if(!held||!mapped)return false;
            const U64 name=Quad(held),actual=Quad(mapped);
            if(!name){if(actual)return false;terminated=true;break;}
            if(name>UINT32_MAX-2u)return false;U32 selected=NoIndex;
            for(U32 api=0;api<35;++api)if(Name(request.originalModule,header,static_cast<U32>(name)+2,ApiNames[api])){
                if(selected!=NoIndex)return false;selected=api;}
            if(selected==NoIndex||(found&(1ull<<selected))||actual!=apiAddresses[selected])return false;
            found|=1ull<<selected;
        }if(!terminated)return false;
    }
    // A delay descriptor cannot supplement or replace this exact ordinary edge.
    const auto& delayed=header->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT];
    if(delayed.VirtualAddress||delayed.Size)return false;
    return ended&&descriptors==1&&found==((1ull<<35)-1);
}
bool Abi(){
    if(RizEngineNodeContext_GetAbiVersion()!=2||RizEngineNodeContext_GetLimit()!=32||RizEngineNodeContext_GetFrameSize()!=112
        ||RizEngineNodeContext_GetCompletionSize()!=32||RizEngineNodeContext_GetInlineAbiVersion()!=1
        ||RizEngineNodeContext_GetInlineLimit()!=32||RizEngineNodeContext_GetInlineFrameSize()!=144
        ||RizEngineSchedulerContext_GetAbiVersion()!=1||RizEngineSchedulerContext_GetLimit()!=32
        ||RizEngineSchedulerContext_GetFrameSize()!=256||RizEngineSchedulerContext_GetCompletionSize()!=48
        ||RizEngineSchedulerContext_GetRowFrameSize()!=224||RizEngineWaitContext_GetAbiVersion()!=1
        ||RizEngineWaitContext_GetLimit()!=32||RizEngineWaitContext_GetFrameSize()!=240
        ||RizEngineWaitContext_GetCompletionSize()!=48||RizEngineWaitContext_GetPhaseFrameSize()!=288)return false;
    U64 entries[16];for(U32 i=0;i<4;++i)entries[i]=Address(RizEngineNodeContext_GetEntry(i,retainedRequest.slot));
    for(U32 i=0;i<6;++i)entries[i+4]=Address(RizEngineSchedulerContext_GetEntry(i,retainedRequest.slot));
    for(U32 i=0;i<3;++i)entries[i+10]=Address(RizEngineWaitContext_GetEntry(i,retainedRequest.slot));
    entries[13]=Address(RizEngineNodeContext_GetInlineEntry());entries[14]=Address(RizEngineSchedulerContext_GetDrainRowEntry());
    entries[15]=Address(RizEngineWaitContext_GetPhaseEntry());
    for(U32 i=0;i<16;++i){if(!entries[i]||!Memory(entries[i],1,retainedRequest.kernelModule,true,false,true))return false;
        for(U32 prior=0;prior<i;++prior)if(entries[i]==entries[prior])return false;
        const U64 expected=i<13?retained.typedEntries[i]:i==13?retained.inlineEntry:i==14?retained.drainRowEntry:retained.waitPhaseEntry;
        if(expected&&entries[i]!=expected)return false;
    }
    for(U32 i=0;i<13;++i)retained.typedEntries[i]=entries[i];retained.inlineEntry=entries[13];
    retained.drainRowEntry=entries[14];retained.waitPhaseEntry=entries[15];return true;
}
bool Originals(){const auto& request=retainedRequest;const auto* header=Header(request.originalModule,true);if(!header)return false;
    for(U32 index=0;index<TypedCount;++index){const auto& fact=Fixed::Functions[index];const U64 original=request.originals[index];
        const U32 bytes=fact.prefixBytes+10;
        if(original<request.originalModule||original-request.originalModule>header->OptionalHeader.SizeOfImage
            ||bytes>header->OptionalHeader.SizeOfImage-(original-request.originalModule)
            ||!Memory(original,bytes,request.originalModule,true,false,true))return false;
        const auto* code=reinterpret_cast<const U8*>(original);if(!Equal(code,fact.prefix,fact.prefixBytes))return false;
        DWORD64 lookupBase=0;const auto* function=RtlLookupFunctionEntry(original,&lookupBase,nullptr);
        if(!function||lookupBase!=request.originalModule||!Memory(Address(function),12,request.originalModule,false,false,true)
            ||U64(function->BeginAddress)!=original-request.originalModule
            ||U64(function->EndAddress)!=original-request.originalModule+bytes)return false;
        const auto* tail=code+fact.prefixBytes;const bool callback=index==9;
        if(tail[0]!=0x4c||tail[1]!=0x8b||tail[2]!=(callback?0x15:0x1d)||tail[7]!=0x41||tail[8]!=0xff
            ||tail[9]!=(callback?0xe2:0xe3))return false;
        std::int32_t displacement;Copy(&displacement,tail+3,4);U64 slot{};const U64 after=original+fact.prefixBytes+7;
        if(displacement<0){const U64 distance=static_cast<U64>(-static_cast<std::int64_t>(displacement));if(after<distance)return false;slot=after-distance;}
        else if(!Add(after,static_cast<U32>(displacement),&slot))return false;
        if(slot%8||!Memory(slot,8,request.originalModule,false,false,true)||Quad(Pointer(slot))!=request.targets[index]+fact.prefixBytes)return false;
        for(U32 prior=0;prior<index;++prior)if(request.originals[prior]==original||request.targets[prior]==original)return false;
    }return true;
}
bool Sites(){const auto& request=retainedRequest;U64 arenaEnd{};
    if(!request.relayAllocationBase||request.relayAllocationBytes<4096
        ||request.relayAllocationBytes>1024*1024||!Add(request.relayAllocationBase,request.relayAllocationBytes,&arenaEnd))return false;
    for(U32 index=0;index<SiteCount;++index){const auto& pair=request.sites[index];const auto& fact=Fixed::Sites[index];
        U64 expected{};if(!Add(request.targetModule,fact.observationRva,&expected)||pair.logicalPc!=expected||pair.relayReturnPc<request.relayAllocationBase+6
            ||pair.relayReturnPc>arenaEnd||!Memory(pair.relayReturnPc-6,6,request.relayAllocationBase,true,false,false,MEM_PRIVATE))return false;
        for(U32 prior=0;prior<index;++prior)if(pair.logicalPc==request.sites[prior].logicalPc||pair.relayReturnPc==request.sites[prior].relayReturnPc)return false;
        const auto* call=reinterpret_cast<const U8*>(pair.relayReturnPc-6);if(call[0]!=0xff||call[1]!=0x15)return false;
        std::int32_t displacement;Copy(&displacement,call+2,4);U64 literal{};
        if(displacement<0){const U64 distance=static_cast<U64>(-static_cast<std::int64_t>(displacement));if(pair.relayReturnPc<distance)return false;literal=pair.relayReturnPc-distance;}
        else if(!Add(pair.relayReturnPc,static_cast<U32>(displacement),&literal))return false;
        if(literal<request.relayAllocationBase||literal>arenaEnd-8||!Memory(literal,8,request.relayAllocationBase,true,false,false,MEM_PRIVATE))return false;
        const U64 probe=index<3?retained.inlineEntry:index<5?retained.drainRowEntry:retained.waitPhaseEntry;
        if(Quad(Pointer(literal))!=probe||Overlap(pair.relayReturnPc-6,6,pair.logicalPc,1))return false;
    }return true;
}
bool Targets(bool fullBodies){const auto& request=retainedRequest;const auto* header=Header(request.targetModule,false);
    if(!header||header->OptionalHeader.SizeOfImage!=Fixed::ImageSize||header->FileHeader.TimeDateStamp!=Fixed::TimeDateStamp
        ||header->OptionalHeader.AddressOfEntryPoint!=Fixed::EntryRva)return false;
    DWORD sourceType=request.profile==Owned?MEM_PRIVATE:MEM_IMAGE;
#ifdef RIZ_ENGINE_TRANSPORT_OWNED_IMAGE
    if(request.profile==Owned){if(!RizEngineTransportValidateOwnedImage(request.targetModule))return false;sourceType=MEM_IMAGE;}
#endif
    for(U32 i=0;i<TypedCount;++i){const auto& fact=Fixed::Functions[i];U64 target{};
        if(!Add(request.targetModule,fact.beginRva,&target)||request.targets[i]!=target
            ||target==request.originals[i]||!Memory(target,fullBodies?fact.bodyBytes:1,request.targetModule,true,false,false,
                sourceType))return false;
        if(fullBodies){U8 digest[32];Hash(Pointer(target),fact.bodyBytes,digest);if(!Digest(digest,fact.bodySha256)||!Equal(Pointer(target),fact.body,fact.bodyBytes))return false;}
    }return true;
}

U32 TypedForFamily(U32 family){return family<4?family:family>=5&&family<=10?family-1:NoIndex;}
void Template(U32 index,U32 kind,U32 channel,U32 abi,U32 frame,U32 completion,U32 info,U32 config,U32 observer){
    auto& row=retained.families[index];row.version=5;row.byteSize=sizeof(row);row.index=index;
    row.familyKind=kind;row.channelKind=channel;row.slot=retainedRequest.slot;row.abiVersion=abi;
    row.frameBytes=frame;row.completionBytes=completion;row.infoBytes=info;row.configBytes=config;row.observerBytes=observer;
    row.module=retainedRequest.kernelModule;row.mainOsThread=kind?retainedRequest.mainOsTid:0;
    const U32 typed=TypedForFamily(index);
    if(typed!=NoIndex){row.target=retainedRequest.targets[typed];row.original=retainedRequest.originals[typed];
        row.entry=retained.typedEntries[typed];row.typedMask=1ull<<typed;row.windowMask=1ull<<typed;}
}
void BuildTemplates(){
    for(U32 i=0;i<4;++i){Template(i,0,i,2,112,32,112,24,32);
        auto* value=reinterpret_cast<RizEngineNodeConfigV1*>(retained.configurations[i]);
        value->abiVersion=1;value->byteSize=sizeof(*value);value->target=Pointer(retainedRequest.targets[i]);value->original=Pointer(retainedRequest.originals[i]);}
    Template(4,1,NoIndex,1,144,0,160,88,24);
    auto* side=reinterpret_cast<RizEngineInlineConfigV1*>(retained.configurations[4]);
    side->abiVersion=1;side->byteSize=sizeof(*side);side->startSlot=retainedRequest.slot;side->mainThreadId=retainedRequest.mainOsTid;
    side->expectedStartTarget=Pointer(retainedRequest.targets[0]); // Epoch remains zero until actual Start Info.
    for(U32 i=0;i<3;++i){side->sites[i].sitePc=Pointer(retainedRequest.sites[i].logicalPc);
        side->sites[i].relayReturnPc=Pointer(retainedRequest.sites[i].relayReturnPc);}
    retained.families[4].typedMask=1;retained.families[4].siteMask=7;retained.families[4].windowMask=7ull<<13;
    for(U32 i=0;i<6;++i){const U32 index=i+5;Template(index,2,i,1,256,48,144,64,40);
        auto* value=reinterpret_cast<RizEngineSchedulerConfigV1*>(retained.configurations[index]);
        value->abiVersion=1;value->byteSize=sizeof(*value);value->target=Pointer(retainedRequest.targets[i+4]);
        value->original=Pointer(retainedRequest.originals[i+4]);value->mainThreadId=retainedRequest.mainOsTid;
        if(i==RizEngineSchedulerDrain){for(U32 site=0;site<2;++site){value->sites[site].sitePc=Pointer(retainedRequest.sites[site+3].logicalPc);
                value->sites[site].relayReturnPc=Pointer(retainedRequest.sites[site+3].relayReturnPc);}
            retained.families[index].siteMask=3ull<<3;retained.families[index].windowMask|=3ull<<16;}
    }
    Template(11,3,NoIndex,1,240,48,176,432,40);auto* wait=reinterpret_cast<RizEngineWaitConfigV1*>(retained.configurations[11]);
    wait->abiVersion=1;wait->byteSize=sizeof(*wait);wait->mainThreadId=retainedRequest.mainOsTid;
    for(U32 i=0;i<3;++i){wait->targets[i]=Pointer(retainedRequest.targets[i+10]);wait->originals[i]=Pointer(retainedRequest.originals[i+10]);}
    for(U32 i=0;i<4;++i)wait->nodeTargets[i]=Pointer(retainedRequest.targets[i]);
    for(U32 i=0;i<6;++i)wait->schedulerTargets[i]=Pointer(retainedRequest.targets[i+4]);
    for(U32 i=0;i<18;++i){wait->sites[i].sitePc=Pointer(retainedRequest.sites[i+5].logicalPc);
        wait->sites[i].relayReturnPc=Pointer(retainedRequest.sites[i+5].relayReturnPc);}
    retained.families[11].typedMask=7ull<<10;retained.families[11].siteMask=((1ull<<18)-1)<<5;
    retained.families[11].windowMask=(7ull<<10)|(((1ull<<18)-1)<<18);
    for(U32 i=0;i<FamilyCount;++i){if(i!=4)Hash(retained.configurations[i],retained.families[i].configBytes,retained.families[i].configSha256);}
    retained.descriptorsPrepared=FamilyCount;
}

bool Information(U32 family,bool fresh,bool initial,bool save){
    const U32 slot=retainedRequest.slot;const auto& row=retained.families[family];U32 status=1;bool okay=false;
    U8 bytes[InfoCapacity];Zero(bytes,sizeof(bytes));U32 size=0,attached=0;U64 epoch=0;
    if(family<4){RizEngineNodeInfoV1 value;Zero(&value,sizeof(value));value.abiVersion=1;value.byteSize=sizeof(value);
        status=RizEngineNodeContext_GetInfo(family,slot,&value);size=sizeof(value);attached=value.attached;epoch=value.epoch;
        okay=value.abiVersion==1&&value.byteSize==sizeof(value)&&value.kind==family&&value.slot==slot&&value.attached<=1&&!value.unknown
            &&Address(value.entry)==retained.typedEntries[family]&&Counters(value.entered,value.exited,value.inFlight)
            &&!value.enterFaults&&!value.exitFaults&&!value.snapshotFaults&&!value.outputFaults;
        if(fresh)okay=okay&&status==2&&!value.target&&!value.original&&!epoch&&!attached&&!value.entered&&!value.exited&&!value.inFlight;
        else okay=okay&&!status&&Address(value.target)==row.target&&Address(value.original)==row.original&&ValidEpoch(epoch);
        if(initial)okay=okay&&!attached&&!value.entered&&!value.exited&&!value.inFlight;
        Copy(bytes,&value,size);
    }else if(family==4){RizEngineInlineInfoV1 value;Zero(&value,sizeof(value));value.abiVersion=1;value.byteSize=sizeof(value);
        status=RizEngineNodeContext_GetInlineInfo(slot,&value);size=sizeof(value);attached=value.attached;epoch=value.epoch;
        okay=value.abiVersion==1&&value.byteSize==sizeof(value)&&value.slot==slot&&value.attached<=1&&!value.unknown&&!value.reserved
            &&Address(value.entry)==retained.inlineEntry&&Counters(value.startsEntered,value.startsExited,value.startsInFlight)
            &&value.probesInFlight<=0x7fffffffffffffffull&&!value.faults&&!value.snapshotFaults&&!value.orphanProbes&&!value.unmatchedStarts;
        if(fresh)okay=okay&&status==2&&!value.configured&&!epoch&&!attached&&!value.mainThreadId&&!value.startEpoch&&!value.startTarget
            &&!value.startsEntered&&!value.startsExited&&!value.startsInFlight&&!value.probesInFlight&&!value.begins&&!value.endsFree&&!value.endsRetained&&!value.events;
        else okay=okay&&!status&&value.configured==1&&value.mainThreadId==retainedRequest.mainOsTid&&ValidEpoch(epoch)
            &&value.startEpoch==retained.families[0].epoch&&Address(value.startTarget)==retainedRequest.targets[0];
        if(initial)okay=okay&&!attached&&!value.startsEntered&&!value.startsExited&&!value.startsInFlight&&!value.probesInFlight
            &&!value.begins&&!value.endsFree&&!value.endsRetained&&!value.events;
        Copy(bytes,&value,size);
    }else if(family<=10){const U32 kind=family-5;RizEngineSchedulerInfoV1 value;Zero(&value,sizeof(value));value.abiVersion=1;value.byteSize=sizeof(value);
        status=RizEngineSchedulerContext_GetInfo(kind,slot,&value);size=sizeof(value);attached=value.attached;epoch=value.epoch;
        okay=value.abiVersion==1&&value.byteSize==sizeof(value)&&value.kind==kind&&value.slot==slot&&value.attached<=1&&!value.unknown
            &&Address(value.entry)==retained.typedEntries[kind+4]&&Address(value.rowEntry)==retained.drainRowEntry
            &&Counters(value.entered,value.exited,value.inFlight)&&value.rowsInFlight<=0x7fffffffffffffffull
            &&!value.faults&&!value.snapshotFaults&&!value.unmatchedRows;
        if(fresh)okay=okay&&status==2&&!value.configured&&!epoch&&!attached&&!value.mainThreadId&&!value.target&&!value.original
            &&!value.entered&&!value.exited&&!value.inFlight&&!value.rowBegins&&!value.rowEnds&&!value.rowsInFlight;
        else okay=okay&&!status&&value.configured==1&&value.mainThreadId==retainedRequest.mainOsTid
            &&Address(value.target)==row.target&&Address(value.original)==row.original&&ValidEpoch(epoch);
        if(initial)okay=okay&&!attached&&!value.entered&&!value.exited&&!value.inFlight&&!value.rowBegins&&!value.rowEnds&&!value.rowsInFlight;
        Copy(bytes,&value,size);
    }else{RizEngineWaitInfoV1 value;Zero(&value,sizeof(value));value.abiVersion=1;value.byteSize=sizeof(value);
        status=RizEngineWaitContext_GetInfo(slot,&value);size=sizeof(value);attached=value.attached;epoch=value.epoch;
        okay=value.abiVersion==1&&value.byteSize==sizeof(value)&&value.slot==slot&&value.attached<=1&&!value.unknown&&!value.reserved
            &&Address(value.phaseEntry)==retained.waitPhaseEntry&&Counters(value.entered,value.exited,value.inFlight)
            &&!value.faults&&!value.snapshotFaults&&!value.unmatched;
        for(U32 i=0;i<3;++i){okay=okay&&Address(value.entries[i])==retained.typedEntries[i+10];
            if(fresh)okay=okay&&!value.targets[i]&&!value.originals[i];
            else okay=okay&&Address(value.targets[i])==retainedRequest.targets[i+10]&&Address(value.originals[i])==retainedRequest.originals[i+10];}
        if(fresh)okay=okay&&status==2&&!value.configured&&!epoch&&!attached&&!value.mainThreadId&&!value.entered&&!value.exited&&!value.inFlight&&!value.phases;
        else okay=okay&&!status&&value.configured==1&&value.mainThreadId==retainedRequest.mainOsTid&&ValidEpoch(epoch);
        if(initial)okay=okay&&!attached&&!value.entered&&!value.exited&&!value.inFlight&&!value.phases;
        Copy(bytes,&value,size);
    }
    if(!fresh&&!initial&&epoch!=row.epoch)okay=false;
    if(!fresh&&attachedSeen[family]&&!attached)okay=false;
    if(save){retained.informationResults[family]=status;retained.informationBytes[family]=size;Copy(retained.information[family],bytes,size);
        if(okay&&!fresh)retained.families[family].epoch=epoch;}
    if(okay&&!fresh&&attached)attachedSeen[family]=true;
    return okay;
}
Result Preflight(){
    if(!Exports())return Fail(BadExports);if(!Imports())return Fail(BadImports);retained.apiCount=35;
    if(!Abi())return Fail(BadAbi);if(!Targets(true))return Fail(BadTarget);if(!Originals())return Fail(BadOriginal);if(!Sites())return Fail(BadSite);
    BuildTemplates();for(U32 family=0;family<FamilyCount;++family)if(!Information(family,true,false,true))return Fail(SlotNotFresh,family);
    return Success;
}
Result ConfigureAll(){retained.state=Configuring;
    for(U32 family=0;family<FamilyCount;++family){auto& row=retained.families[family];retained.failureFamily=family;
        if(family==4){auto* config=reinterpret_cast<RizEngineInlineConfigV1*>(retained.configurations[4]);
            if(!ValidEpoch(retained.families[0].epoch)||!Information(0,false,false,false))return Fail(BadInformation,0);
            config->expectedStartEpoch=retained.families[0].epoch;
            Hash(config,sizeof(*config),row.configSha256);retained.inlineEpochBound=1;}
        // Mark responsibility BEFORE calling native: an exception or failed
        // Info cannot erase a successful or incompletely observed permanent claim.
        const U64 bit=1ull<<family;row.configureAttempts=1;row.retainedClaim=1;row.flags=RizEngineTransportFamilyRetainedV5;
        retained.attemptedFamilies|=bit;retained.retainedFamilies|=bit;++retained.configureAttempts;
        U32 status=1;SetLastError(ERROR_SUCCESS);
        if(family<4)status=RizEngineNodeContext_Configure(family,retainedRequest.slot,reinterpret_cast<const RizEngineNodeConfigV1*>(retained.configurations[family]));
        else if(family==4)status=RizEngineNodeContext_ConfigureInline(retainedRequest.slot,reinterpret_cast<const RizEngineInlineConfigV1*>(retained.configurations[family]));
        else if(family<=10)status=RizEngineSchedulerContext_Configure(family-5,retainedRequest.slot,reinterpret_cast<const RizEngineSchedulerConfigV1*>(retained.configurations[family]));
        else status=RizEngineWaitContext_Configure(retainedRequest.slot,reinterpret_cast<const RizEngineWaitConfigV1*>(retained.configurations[family]));
        row.lastWin32Error=GetLastError();retained.configureResults[family]=status;
        if(status)return Fail(ConfigureFailed,family);
        row.configureSucceeded=1;row.flags|=RizEngineTransportFamilyConfiguredV5;
        retained.configuredFamilies|=bit;++retained.configureSucceeded;
        if(!Information(family,false,true,true))return Fail(BadInformation,family);
        row.flags|=RizEngineTransportFamilyIdentityVerifiedV5;retained.verifiedFamilies|=bit;
    }
    retained.failureFamily=NoIndex;retained.state=Configured;retained.result=Success;return Success;
}

bool RequestShape(const Request& value){
    if(value.version!=Version||value.byteSize!=sizeof(value)||value.slot>=32||!value.mainOsTid||value.reserved
        ||(value.profile!=Owned&&value.profile!=FixedGame)||!value.kernelModule||!value.targetModule||!value.originalModule
        ||value.kernelModule==value.targetModule||value.kernelModule==value.originalModule||value.targetModule==value.originalModule)return false;
    for(U32 i=0;i<TypedCount;++i)if(!value.targets[i]||!value.originals[i])return false;
    return true;
}
bool ConfigHashes(){for(U32 index=0;index<FamilyCount;++index){const auto& row=retained.families[index];
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=index||row.configBytes>ConfigCapacity
            ||row.configureAttempts!=1||row.configureSucceeded!=1||row.retainedClaim!=1||row.flags!=7||!ValidEpoch(row.epoch)
            ||!Empty(retained.configurations[index]+row.configBytes,ConfigCapacity-row.configBytes))return false;
        U8 digest[32];Hash(retained.configurations[index],row.configBytes,digest);if(!Equal(digest,row.configSha256,32))return false;
    }return retained.inlineEpochBound==1;
}
Result Current(){
    if(retained.state!=Configured||retained.result!=Success||retained.descriptorsPrepared!=FamilyCount
        ||retained.configureAttempts!=FamilyCount||retained.configureSucceeded!=FamilyCount
        ||retained.attemptedFamilies!=AllFamilies||retained.configuredFamilies!=AllFamilies
        ||retained.verifiedFamilies!=AllFamilies||retained.retainedFamilies!=AllFamilies)return Fail(ReceiptChanged);
    if(!Exports())return Fail(BadExports);if(!Imports())return Fail(BadImports);if(!Abi())return Fail(BadAbi);
    if(!Targets(false))return Fail(BadTarget);if(!Originals())return Fail(BadOriginal);if(!Sites())return Fail(BadSite);
    if(!ConfigHashes())return Fail(ReceiptChanged);
    for(U32 family=0;family<FamilyCount;++family)if(!Information(family,false,false,false))return Fail(BadInformation,family);
    return Success;
}
bool Storage(const Request* request,const Receipt* output,bool fresh){
    if(!request||!output||!Memory(Address(request),sizeof(*request),0,false,false)
        ||!Memory(Address(output),sizeof(*output),0,false,fresh)||Overlap(Address(request),sizeof(*request),Address(output),sizeof(*output)))return false;
    if(Overlap(Address(request),sizeof(*request),Address(&retained),sizeof(retained))
        ||Overlap(Address(output),sizeof(*output),Address(&retained),sizeof(retained))
        ||Overlap(Address(request),sizeof(*request),Address(&retainedRequest),sizeof(retainedRequest))
        ||Overlap(Address(output),sizeof(*output),Address(&retainedRequest),sizeof(retainedRequest)))return false;
    return !fresh||Empty(output,sizeof(*output));
}
void Initialize(const Request* request){
    Copy(&retainedRequest,request,sizeof(retainedRequest));Zero(&retained,sizeof(retained));
    retained.version=Version;retained.byteSize=sizeof(retained);retained.state=Preparing;retained.result=Success;
    retained.failureFamily=NoIndex;retained.profile=retainedRequest.profile;
    retained.kernelModule=retainedRequest.kernelModule;retained.targetModule=retainedRequest.targetModule;
    retained.originalModule=retainedRequest.originalModule;retained.mainOsTid=retainedRequest.mainOsTid;
    for(U32 i=0;i<FamilyCount;++i){retained.configureResults[i]=NoIndex;retained.informationResults[i]=NoIndex;}
}
} // anonymous namespace

Result PrepareAndConfigure(const Request* request,Receipt* output){
    const DWORD incoming=GetLastError();Result result=InvalidInput;
    if(InterlockedCompareExchange(&gate,1,0)){SetLastError(incoming);return Busy;}
    __try {
        if(Storage(request,output,true)){
            if(InterlockedCompareExchange(&attempted,0,0)){Copy(output,&retained,sizeof(retained));result=AlreadyAttempted;}
            else{
                Request local;Copy(&local,request,sizeof(local));
                if(RequestShape(local)){
                    Initialize(&local);InterlockedExchange(&attempted,1);
                    // Independent production gate: fixed bytes/configuration
                    // cannot open without Bootstrap's verified cold-attach exclusivity witness.
#ifdef RIZ_ENGINE_TRANSPORT_PRODUCTION
                    if(local.profile!=FixedGame||!RizEngineTransportValidateProductionAdmission(local.targetModule))result=Fail(AdmissionUnproven);
#else
                    if(local.profile==FixedGame)result=Fail(AdmissionUnproven);
#endif
                    else{result=Preflight();if(result==Success)result=ConfigureAll();if(result==Success)result=Current();}
                    Copy(output,&retained,sizeof(retained));
                }
            }
        }
    }__except(EXCEPTION_EXECUTE_HANDLER){result=NativeException;
        if(InterlockedCompareExchange(&attempted,0,0)){Fail(NativeException,retained.failureFamily);
            // Preserve a best-effort readable failure copy without losing the
            // private process-local attempted/retained family masks.
            __try {if(output&&Memory(Address(output),sizeof(*output),0,false,true))Copy(output,&retained,sizeof(retained));}
            __except(EXCEPTION_EXECUTE_HANDLER){}
        }
    }
    InterlockedExchange(&gate,0);SetLastError(incoming);return result;
}
Result Verify(const Request* request,const Receipt* receipt){
    const DWORD incoming=GetLastError();Result result=InvalidInput;
    if(InterlockedCompareExchange(&gate,1,0)){SetLastError(incoming);return Busy;}
    __try {
        if(InterlockedCompareExchange(&attempted,0,0)){
            if(InterlockedCompareExchange(&stickyFailure,0,0))result=static_cast<Result>(retained.result);
            else if(!Storage(request,receipt,false)||!Equal(request,&retainedRequest,sizeof(retainedRequest))
                ||!Equal(receipt,&retained,sizeof(retained)))result=Fail(ReceiptChanged);
            else result=Current();
        }
    }__except(EXCEPTION_EXECUTE_HANDLER){result=NativeException;if(InterlockedCompareExchange(&attempted,0,0))Fail(NativeException);}
    InterlockedExchange(&gate,0);SetLastError(incoming);return result;
}
static_assert(sizeof(RizEngineNodeConfigV1)==24&&sizeof(RizEngineNodeInfoV1)==112);
static_assert(sizeof(RizEngineInlineConfigV1)==88&&sizeof(RizEngineInlineInfoV1)==160);
static_assert(sizeof(RizEngineSchedulerConfigV1)==64&&sizeof(RizEngineSchedulerInfoV1)==144);
static_assert(sizeof(RizEngineWaitConfigV1)==432&&sizeof(RizEngineWaitInfoV1)==176);
static_assert(sizeof(RizEngineNodeObserverV1)==32&&sizeof(RizEngineInlineObserverV1)==24);
static_assert(sizeof(RizEngineSchedulerObserverV1)==40&&sizeof(RizEngineWaitObserverV1)==40);
} // namespace RizEngineTransportFamilies
