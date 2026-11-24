#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define RIZ_ENGINE_TRANSPORT_PUBLICATION_BUILD
#include <Windows.h>
#include <TlHelp32.h>
#include <intrin.h>
#include "EngineTransportProfile.h"
#include "EngineTransportPublication.h"
#include "EngineTransportAdmissionSha256.h"
#include "EngineTransportRelays.h"
#include "EngineTransportFamilies.h"
#include "EngineTransportSourceWrites.h"
#include "EngineTransportOriginals.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/EngineSchedulerContext.h"
#include "../RizToolbox.JudgeContext/EngineWaitContext.h"

// This is the independent V5 publisher. V4 remains in its original translation
// unit and is never linked into this DLL. No external input can provide Ready,
// admission bits, a synthetic frame, a Configure receipt or a source-write mask.
extern "C" {
RizEngineTransportPublicationV5 RizEngineTransportPublicationRecord{
    5,sizeof(RizEngineTransportPublicationV5),RizEngineTransportNeverV5,0,
    RizEngineTransportPublicationLayout::Magic};
void* RizEngineContinuation[4]{};
std::uintptr_t RizEngineTransportContinuation[9]{};
void RizEngineOriginalStart();void RizEngineOriginalExecute();
void RizEngineOriginalRelease();void RizEngineOriginalDetach();
BOOL WINAPI RizEngineTransportBootstrapEntry(HINSTANCE,DWORD,LPVOID);
}
#pragma comment(linker,"/INCLUDE:RizEngineTransportPublicationRecord")

namespace {
namespace C=RizEngineTransportCompileContract;
namespace P=RizEngineTransportProfile;
namespace F=RizEngineTransportGameProfile;
namespace R=RizEngineTransportRelays;
namespace K=RizEngineTransportFamilies;
namespace W=RizEngineTransportSourceWrites;
namespace L=RizEngineTransportPublicationLayout;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
RizEngineTransportPublicationV5& info=RizEngineTransportPublicationRecord;
RizEngineTransportPublicationV5 readySnapshot{};
R::Plan relayPlan{};
K::Request familyRequest{};K::Receipt familyReceipt{};
W::Receipt writeReceipt{};
HMODULE modules[5]{};const IMAGE_NT_HEADERS64* headers[5]{};
bool pinned[5]{},modulesValidated{},continuationsPrepared{},readyFrozen{};
wchar_t paths[5][1024]{},checkPath[1024]{};
U32 actualLookup[44]{},actualIat[44]{},actualExport[44]{};
U8 fileReadBuffer[65536]{};
volatile LONG gate{},claimed{},stickyUnknown{};
const void* const originals[13]{
    reinterpret_cast<const void*>(&RizEngineOriginalStart),reinterpret_cast<const void*>(&RizEngineOriginalExecute),
    reinterpret_cast<const void*>(&RizEngineOriginalRelease),reinterpret_cast<const void*>(&RizEngineOriginalDetach),
    reinterpret_cast<const void*>(&RizEngineTransportOriginalRegister),reinterpret_cast<const void*>(&RizEngineTransportOriginalRemove),
    reinterpret_cast<const void*>(&RizEngineTransportOriginalConsume),reinterpret_cast<const void*>(&RizEngineTransportOriginalPump),
    reinterpret_cast<const void*>(&RizEngineTransportOriginalDrain),reinterpret_cast<const void*>(&RizEngineTransportOriginalCallback),
    reinterpret_cast<const void*>(&RizEngineTransportOriginalYield),reinterpret_cast<const void*>(&RizEngineTransportOriginalDispatch),
    reinterpret_cast<const void*>(&RizEngineTransportOriginalBatch)};

U64 Address(const void* pointer){return reinterpret_cast<U64>(pointer);}
void* Pointer(U64 address){return reinterpret_cast<void*>(address);}
void Zero(void* destination,std::size_t bytes){__stosb(static_cast<U8*>(destination),0,bytes);}
void Copy(void* destination,const void* source,std::size_t bytes){__movsb(static_cast<U8*>(destination),static_cast<const U8*>(source),bytes);}
bool Equal(const void* left,const void* right,std::size_t bytes){const auto* a=static_cast<const volatile U8*>(left);
    const auto* b=static_cast<const volatile U8*>(right);for(std::size_t i=0;i<bytes;++i)if(a[i]!=b[i])return false;return true;}
bool Empty(const void* value,std::size_t bytes){const auto* data=static_cast<const volatile U8*>(value);
    for(std::size_t i=0;i<bytes;++i)if(data[i])return false;return true;}
bool Add(U64 a,U64 b,U64* result){if(a>UINT64_MAX-b)return false;*result=a+b;return true;}
bool Fits(U64 at,U64 bytes,U64 total){return at<=total&&bytes<=total-at;}
bool Overlap(U64 a,U64 na,U64 b,U64 nb){U64 ae{},be{};return !Add(a,na,&ae)||!Add(b,nb,&be)||(a<be&&b<ae);}
U32 Lower(U32 value){return value>='A'&&value<='Z'?value+('a'-'A'):value;}
bool SameWide(const wchar_t* left,const wchar_t* right,U32 bound=1024){
    for(U32 i=0;i<bound;++i){if(Lower(left[i])!=Lower(right[i]))return false;if(!left[i])return true;}return false;}
bool SameText(const char* left,const char* right){for(U32 i=0;i<512;++i){if(left[i]!=right[i])return false;if(!left[i])return true;}return false;}
U32 Word(const void* value){U32 result;Copy(&result,value,4);return result;}
U64 Quad(const void* value){U64 result;Copy(&result,value,8);return result;}
bool Memory(U64 start,U64 bytes,U64 allocation,DWORD type,DWORD protection=0,bool write=false){
    U64 end{};if(!start||!bytes||!Add(start,bytes,&end))return false;
    for(U64 at=start;at<end;){MEMORY_BASIC_INFORMATION memory;Zero(&memory,sizeof(memory));
        if(VirtualQuery(Pointer(at),&memory,sizeof(memory))!=sizeof(memory)||memory.State!=MEM_COMMIT
            ||(memory.Protect&(PAGE_GUARD|PAGE_NOACCESS))||(allocation&&Address(memory.AllocationBase)!=allocation)
            ||(type&&memory.Type!=type)||(protection&&memory.Protect!=protection))return false;
        if(write&&memory.Protect!=PAGE_READWRITE&&memory.Protect!=PAGE_WRITECOPY)return false;
        const DWORD basic=memory.Protect&0xffu;
        if(!protection&&!write&&basic!=PAGE_READONLY&&basic!=PAGE_READWRITE&&basic!=PAGE_WRITECOPY&&basic!=PAGE_EXECUTE_READ)return false;
        U64 next{};if(!Add(Address(memory.BaseAddress),memory.RegionSize,&next)||next<=at)return false;at=next<end?next:end;
    }return true;
}
bool Image(U32 module,U32 rva,U64 bytes){return module<5&&headers[module]&&rva
    &&Fits(rva,bytes,headers[module]->OptionalHeader.SizeOfImage)&&Memory(Address(modules[module])+rva,bytes,Address(modules[module]),MEM_IMAGE);}
const U8* At(U32 module,U32 rva){return static_cast<const U8*>(static_cast<const void*>(modules[module]))+rva;}
bool Directory(U32 module,U32 kind){const auto& value=headers[module]->OptionalHeader.DataDirectory[kind];return value.VirtualAddress&&value.Size;}
bool ImageName(U32 module,U32 rva,const char* expected,bool insensitive=false){for(U32 i=0;i<512;++i){
    if(rva>UINT32_MAX-i||!Image(module,rva+i,1))return false;U32 a=At(module,rva)[i],b=static_cast<U8>(expected[i]);
    if(insensitive){a=Lower(a);b=Lower(b);}if(a!=b)return false;if(!a)return true;}return false;}
bool ImageWideName(U32 module,U32 rva,const wchar_t* expected){for(U32 i=0;i<512;++i){
    if(rva>UINT32_MAX-i||!Image(module,rva+i,1)||expected[i]>127)return false;
    const U32 value=At(module,rva)[i];if(Lower(value)!=Lower(expected[i]))return false;if(!value)return true;}return false;}
bool Record(U32 stage,U32 object=RizEngineTransportPublicationObjectV5,U32 index=L::NoIndex,U32 error=0,U32 bytes=0,U64 detail=0){
    if(info.eventCount>=L::EventCapacity){info.eventOverflow=1;InterlockedExchange(&stickyUnknown,1);
        info.state=RizEngineTransportUnknownV5;if(!info.failure)info.failure=RizEngineTransportEventOverflowV5;return false;}
    auto& row=info.events[info.eventCount++];row.sequence=info.eventCount;row.stage=stage;row.objectKind=object;row.objectIndex=index;
    row.state=info.state;row.failure=info.failure;row.win32Error=error;row.bytesWritten=bytes;row.detail=detail;++info.snapshotSequence;return true;
}
bool HasWrites(){return info.sourceWriteAttempts||info.protectionOutstanding;}
bool HasResponsibility(){return HasWrites()||info.retainedFamilies||info.retainedResources;}
bool Reject(U32 failure,U32 object=RizEngineTransportPublicationObjectV5,U32 index=L::NoIndex,U32 error=0){
    if(!info.failure)info.failure=failure;info.lastWin32Error=error;
    if(HasWrites()||InterlockedCompareExchange(&stickyUnknown,0,0)){InterlockedExchange(&stickyUnknown,1);info.state=RizEngineTransportUnknownV5;}
    else info.state=RizEngineTransportDisabledNoSourceWriteV5;
    (void)Record(RizEngineTransportFailedV5,object,index,error);return false;
}
[[noreturn]] void Fatal(){RaiseFailFastException(nullptr,nullptr,0);TerminateProcess(GetCurrentProcess(),0xe052495d);for(;;)__fastfail(7);}
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
    void Finish(U8* digest){const U64 bits=bytes*8;const U8 one=0x80,zero=0;Feed(&one,1);while(used!=56)Feed(&zero,1);
        U8 suffix[8];for(U32 i=0;i<8;++i)suffix[i]=static_cast<U8>(bits>>(56-i*8));Feed(suffix,8);
        for(U32 i=0;i<8;++i)for(U32 part=0;part<4;++part)digest[i*4+part]=static_cast<U8>(state[i]>>(24-part*8));}
};
void Hash(const void* data,U32 bytes,U8* digest){Sha256 hash;hash.Initialize();hash.Feed(static_cast<const U8*>(data),bytes);hash.Finish(digest);}
bool Digest(const char* text,U8* out){for(U32 i=0;i<32;++i){U32 value=0;for(U32 n=0;n<2;++n){const auto ch=text[i*2+n];
    const U32 digit=ch>='0'&&ch<='9'?U32(ch-'0'):ch>='a'&&ch<='f'?U32(ch-'a'+10):16;if(digit>15)return false;value=(value<<4)|digit;}out[i]=static_cast<U8>(value);}return text[64]==0;}
bool HashFile(HANDLE file,U64 expected,U8* digest){LARGE_INTEGER begin,size;Zero(&begin,sizeof(begin));Zero(&size,sizeof(size));
    if(!GetFileSizeEx(file,&size)||size.QuadPart<=0||U64(size.QuadPart)!=expected||expected>512ull*1024*1024
        ||!SetFilePointerEx(file,begin,nullptr,FILE_BEGIN))return false;
    Sha256 hash;hash.Initialize();U64 total=0;
    // The caller holds this producer's nonwaiting gate, including the direct
    // owned-image helper verifier. BSS avoids a loader-time CRT stack probe and
    // batches I/O without weakening the repeated full held-file digest check.
    for(;;){DWORD count=0;if(!ReadFile(file,fileReadBuffer,sizeof(fileReadBuffer),&count,nullptr))return false;if(!count)break;
        total+=count;if(total>expected)return false;hash.Feed(fileReadBuffer,count);}hash.Finish(digest);return total==expected;}

const IMAGE_NT_HEADERS64* ImageHeader(HMODULE module){const U64 base=Address(module);
    if(!Memory(base,sizeof(IMAGE_DOS_HEADER),base,MEM_IMAGE))return nullptr;const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<64||dos->e_lfanew>0x100000)return nullptr;
    const U64 address=base+static_cast<U32>(dos->e_lfanew);if(!Memory(address,sizeof(IMAGE_NT_HEADERS64),base,MEM_IMAGE))return nullptr;
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(address);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64
        ||nt->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64)||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC
        ||nt->OptionalHeader.NumberOfRvaAndSizes!=16||nt->OptionalHeader.SizeOfImage<4096||nt->OptionalHeader.SizeOfImage>0x20000000)return nullptr;
    return nt;
}
bool DiskHeader(HANDLE file,U32 index){LARGE_INTEGER position;Zero(&position,sizeof(position));DWORD count=0;
    IMAGE_DOS_HEADER dos;IMAGE_NT_HEADERS64 nt;Zero(&dos,sizeof(dos));Zero(&nt,sizeof(nt));
    if(!SetFilePointerEx(file,position,nullptr,FILE_BEGIN)||!ReadFile(file,&dos,sizeof(dos),&count,nullptr)||count!=sizeof(dos)
        ||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<64||dos.e_lfanew>0x100000)return false;
    position.QuadPart=dos.e_lfanew;if(!SetFilePointerEx(file,position,nullptr,FILE_BEGIN)||!ReadFile(file,&nt,sizeof(nt),&count,nullptr)||count!=sizeof(nt))return false;
    const auto mappedBase=headers[index]->OptionalHeader.ImageBase;
    if(mappedBase!=nt.OptionalHeader.ImageBase&&mappedBase!=Address(modules[index]))return false;
    nt.OptionalHeader.ImageBase=mappedBase;return Equal(&nt,headers[index],sizeof(nt));
}
bool ExpectedPath(U32 index){const auto& expected=P::Images[index];
    const U32 length=GetModuleFileNameW(modules[index],paths[index],1024);if(!length||length>=1024||!expected.module)return false;
    U32 start=length;while(start&&paths[index][start-1]!=L'\\')--start;
    if(!start||!SameWide(paths[index]+start,expected.module))return false;
    if(index!=C::Executable){U32 prefix=0;for(U32 i=0;paths[C::Executable][i];++i)if(paths[C::Executable][i]==L'\\')prefix=i+1;
        if(prefix!=start)return false;for(U32 i=0;i<start;++i)if(Lower(paths[index][i])!=Lower(paths[C::Executable][i]))return false;}
    return true;
}
bool ValidateModule(U32 index){const auto& expected=P::Images[index];auto& row=info.modules[index];
    headers[index]=ImageHeader(modules[index]);if(!headers[index]||!ExpectedPath(index)||expected.reserved)return Reject(RizEngineTransportBadImageV5,RizEngineTransportModuleObjectV5,index);
    const auto* nt=headers[index];if(((nt->FileHeader.Characteristics&IMAGE_FILE_DLL)!=0)!=(index!=C::Executable))return Reject(RizEngineTransportBadImageV5,RizEngineTransportModuleObjectV5,index);
    const bool self=index==C::Bootstrap;if(expected.hashPolicy!=(self?C::ObserveHeldSelf:C::PinWholeFile))return Reject(RizEngineTransportBadHashV5,RizEngineTransportModuleObjectV5,index);
    if(self){if(expected.fileBytes||expected.imageSize||expected.timeDateStamp||expected.entryRva||expected.exceptionRva||!Empty(expected.sha256,32))
            return Reject(RizEngineTransportBadHashV5,RizEngineTransportModuleObjectV5,index);}
    else if(!expected.fileBytes||!expected.imageSize||nt->OptionalHeader.SizeOfImage!=expected.imageSize||nt->FileHeader.TimeDateStamp!=expected.timeDateStamp
        ||nt->OptionalHeader.AddressOfEntryPoint!=expected.entryRva||nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].VirtualAddress!=expected.exceptionRva
        ||Empty(expected.sha256,32))return Reject(RizEngineTransportBadImageV5,RizEngineTransportModuleObjectV5,index);
    if((index==C::Bootstrap||index==C::Kernel)&&(Directory(index,IMAGE_DIRECTORY_ENTRY_TLS)||Directory(index,IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT)
        ||(index==C::Kernel&&nt->OptionalHeader.AddressOfEntryPoint)))return Reject(RizEngineTransportBadDependencyV5,RizEngineTransportModuleObjectV5,index);
    row.kind=index;row.machine=nt->FileHeader.Machine;row.module=Address(modules[index]);row.imageSize=nt->OptionalHeader.SizeOfImage;
    row.timeDateStamp=nt->FileHeader.TimeDateStamp;row.entryRva=nt->OptionalHeader.AddressOfEntryPoint;
    row.exceptionRva=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].VirtualAddress;row.flags=4;
    const HANDLE file=CreateFileW(paths[index],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return Reject(RizEngineTransportBadHashV5,RizEngineTransportModuleObjectV5,index,GetLastError());
    row.heldFile=Address(file);row.flags|=1;BY_HANDLE_FILE_INFORMATION identity;LARGE_INTEGER size;Zero(&identity,sizeof(identity));Zero(&size,sizeof(size));
    if(!GetFileInformationByHandle(file,&identity)||!GetFileSizeEx(file,&size)||size.QuadPart<=0)return Reject(RizEngineTransportBadHashV5,RizEngineTransportModuleObjectV5,index,GetLastError());
    row.fileBytes=static_cast<U64>(size.QuadPart);row.fileIndex=(U64(identity.nFileIndexHigh)<<32)|identity.nFileIndexLow;row.volumeSerial=identity.dwVolumeSerialNumber;
    if((!self&&row.fileBytes!=expected.fileBytes)||!DiskHeader(file,index)||!HashFile(file,row.fileBytes,row.sha256)
        ||(!self&&!Equal(row.sha256,expected.sha256,32)))return Reject(RizEngineTransportBadHashV5,RizEngineTransportModuleObjectV5,index);
    row.flags|=2;if(!self)row.flags|=8;return Record(RizEngineTransportModuleValidatedV5,RizEngineTransportModuleObjectV5,index);
}
bool SameFile(U32 index){const auto& row=info.modules[index];BY_HANDLE_FILE_INFORMATION held,current;Zero(&held,sizeof(held));Zero(&current,sizeof(current));
    const U32 length=GetModuleFileNameW(modules[index],checkPath,1024);if(!length||length>=1024||!SameWide(checkPath,paths[index]))return false;
    const HANDLE file=reinterpret_cast<HANDLE>(row.heldFile);
    if(!GetFileInformationByHandle(file,&held)||held.dwVolumeSerialNumber!=row.volumeSerial||((U64(held.nFileIndexHigh)<<32)|held.nFileIndexLow)!=row.fileIndex
        ||((U64(held.nFileSizeHigh)<<32)|held.nFileSizeLow)!=row.fileBytes||!DiskHeader(file,index))return false;
    const HANDLE opened=CreateFileW(checkPath,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(opened==INVALID_HANDLE_VALUE)return false;
    const bool same=GetFileInformationByHandle(opened,&current)&&held.dwVolumeSerialNumber==current.dwVolumeSerialNumber&&held.nFileIndexHigh==current.nFileIndexHigh
        &&held.nFileIndexLow==current.nFileIndexLow&&held.nFileSizeHigh==current.nFileSizeHigh&&held.nFileSizeLow==current.nFileSizeLow;CloseHandle(opened);
    U8 digest[32];return same&&HashFile(file,row.fileBytes,digest)&&Equal(digest,row.sha256,32);
}
bool Pin(U32 index){HMODULE actual=nullptr;if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN|GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
    reinterpret_cast<LPCWSTR>(modules[index]),&actual)||actual!=modules[index])return false;pinned[index]=true;return true;}

bool Export(U32 module,const char* symbol,U32* result,bool executable=true){if(!Directory(module,IMAGE_DIRECTORY_ENTRY_EXPORT))return false;
    const auto directory=headers[module]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];
    if(directory.Size<sizeof(IMAGE_EXPORT_DIRECTORY)||!Image(module,directory.VirtualAddress,directory.Size))return false;
    const auto* table=reinterpret_cast<const IMAGE_EXPORT_DIRECTORY*>(At(module,directory.VirtualAddress));
    if(!table->NumberOfNames||table->NumberOfNames>65536||!table->NumberOfFunctions||table->NumberOfFunctions>65536
        ||!Image(module,table->AddressOfNames,U64(table->NumberOfNames)*4)||!Image(module,table->AddressOfNameOrdinals,U64(table->NumberOfNames)*2)
        ||!Image(module,table->AddressOfFunctions,U64(table->NumberOfFunctions)*4))return false;
    const auto* names=reinterpret_cast<const U32*>(At(module,table->AddressOfNames));
    const auto* ordinals=reinterpret_cast<const std::uint16_t*>(At(module,table->AddressOfNameOrdinals));
    const auto* values=reinterpret_cast<const U32*>(At(module,table->AddressOfFunctions));U32 found=0,rva=0;
    for(U32 i=0;i<table->NumberOfNames;++i)if(ImageName(module,names[i],symbol)){if(++found!=1||ordinals[i]>=table->NumberOfFunctions)return false;rva=values[ordinals[i]];}
    if(found!=1||!Image(module,rva,1)||(rva>=directory.VirtualAddress&&rva-directory.VirtualAddress<directory.Size)
        ||!Memory(Address(At(module,rva)),1,Address(modules[module]),MEM_IMAGE,executable?PAGE_EXECUTE_READ:PAGE_READONLY))return false;
    *result=rva;return true;
}
const void* SelfExport(U32 index){switch(index){case 0:return reinterpret_cast<const void*>(&RizEngineTransport_GetVersion);
    case 1:return reinterpret_cast<const void*>(&RizEngineTransport_GetInfoV5);case 2:return reinterpret_cast<const void*>(&RizEngineTransport_VerifyV5);
    case 3:return reinterpret_cast<const void*>(&RizEngineTransport_AbortAfterPublication);default:return nullptr;}}
bool Group(U32 source,U32 destination,U32 first,U32 count,bool establish){
    if(!Directory(source,IMAGE_DIRECTORY_ENTRY_IMPORT)||Directory(source,IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT))return false;
    const auto directory=headers[source]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if(directory.Size<20||!Image(source,directory.VirtualAddress,directory.Size)||count>35)return false;
    U32 descriptorCount=0;bool ended=false;U64 observed=0;
    for(U32 offset=0;offset+sizeof(IMAGE_IMPORT_DESCRIPTOR)<=directory.Size;offset+=sizeof(IMAGE_IMPORT_DESCRIPTOR)){
        const auto* descriptor=reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(At(source,directory.VirtualAddress+offset));
        if(Empty(descriptor,sizeof(*descriptor))){ended=true;break;}
        if(source==C::Bootstrap&&!ImageName(source,descriptor->Name,"KERNEL32.dll",true)&&!ImageName(source,descriptor->Name,"ntdll.dll",true)
            &&!ImageWideName(source,descriptor->Name,P::Images[C::Kernel].module))return false;
        if(!ImageWideName(source,descriptor->Name,P::Images[destination].module))continue;
        if(++descriptorCount!=1||!descriptor->OriginalFirstThunk||!descriptor->FirstThunk)return false;bool terminated=false;
        for(U32 slot=0;slot<=count;++slot){const U64 lookup64=U64(descriptor->OriginalFirstThunk)+U64(slot)*8,iat64=U64(descriptor->FirstThunk)+U64(slot)*8;
            if(lookup64>UINT32_MAX||iat64>UINT32_MAX)return false;const U32 lookup=static_cast<U32>(lookup64),iat=static_cast<U32>(iat64);
            if(!Image(source,lookup,8)||!Image(source,iat,8))return false;const U64 thunk=Quad(At(source,lookup)),pointer=Quad(At(source,iat));
            if(!thunk){if(pointer)return false;terminated=true;break;}
            if(IMAGE_SNAP_BY_ORDINAL64(thunk)||thunk>0xfffffffdULL||!Image(source,static_cast<U32>(thunk),3))return false;
            U32 selected=count;for(U32 n=0;n<count;++n)if(ImageName(source,static_cast<U32>(thunk)+2,C::Symbols[first+n])){if(selected!=count)return false;selected=n;}
            if(selected==count||(observed&(1ull<<selected)))return false;const U32 edge=first+selected;const auto& expected=P::Edges[edge];
            if(expected.source!=source||expected.destination!=destination||C::Source(edge)!=source||C::Destination(edge)!=destination||!SameText(expected.symbol,C::Symbols[edge]))return false;
            if(source==C::Bootstrap){if(expected.lookupRva||expected.iatRva)return false;}
            else if(!expected.lookupRva||!expected.iatRva||lookup!=expected.lookupRva||iat!=expected.iatRva)return false;
            U32 exported=0;if(!Export(destination,expected.symbol,&exported))return false;
            if(destination==C::Bootstrap){if(expected.exportRva||first!=5||Address(At(destination,exported))!=Address(SelfExport(selected)))return false;}
            else if(!expected.exportRva||exported!=expected.exportRva)return false;
            if(pointer!=Address(At(destination,exported)))return false;
            if(establish){actualLookup[edge]=lookup;actualIat[edge]=iat;actualExport[edge]=exported;}
            else if(actualLookup[edge]!=lookup||actualIat[edge]!=iat||actualExport[edge]!=exported)return false;
            observed|=1ull<<selected;
        }if(!terminated||observed!=((1ull<<count)-1))return false;
    }return ended&&descriptorCount==1;
}
bool Graph(bool establish){if(!Group(C::Executable,C::Target,0,1,establish)||!Group(C::Target,C::Doorstop,1,4,establish)
    ||!Group(C::Doorstop,C::Bootstrap,5,4,establish)||!Group(C::Bootstrap,C::Kernel,9,35,establish))return false;
    Sha256 hash;hash.Initialize();
    for(U32 i=0;i<44;++i){const U32 data[5]{C::Source(i),C::Destination(i),actualLookup[i],actualIat[i],actualExport[i]};hash.Feed(reinterpret_cast<const U8*>(data),sizeof(data));
        U32 bytes=0;while(C::Symbols[i][bytes]&&bytes<511)++bytes;if(bytes==511)return false;hash.Feed(reinterpret_cast<const U8*>(C::Symbols[i]),bytes+1);}
    U8 digest[32];hash.Finish(digest);if(establish)Copy(info.dependencyGraphSha256,digest,32);else if(!Equal(info.dependencyGraphSha256,digest,32))return false;return true;
}

bool ExportSurface(U32 module,U32 count,U32 first){
    if(!Directory(module,IMAGE_DIRECTORY_ENTRY_EXPORT))return false;
    const auto directory=headers[module]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];
    if(!Image(module,directory.VirtualAddress,sizeof(IMAGE_EXPORT_DIRECTORY)))return false;
    const auto* table=reinterpret_cast<const IMAGE_EXPORT_DIRECTORY*>(At(module,directory.VirtualAddress));
    if(table->NumberOfFunctions!=count||table->NumberOfNames!=count)return false;
    for(U32 i=0;i<count;++i){U32 rva=0;if(!Export(module,C::Symbols[first+i],&rva))return false;}
    return true;
}
bool OwnedMarker(){if constexpr(!P::Owned)return false;else{
    constexpr U8 expected[16]{'R','I','Z','T','R','N','S','P','O','W','N','E','D','V','1',0};
    U32 rva=0;if(!Equal(P::OwnedMarker,expected,16)
        ||!Export(C::Target,"RizEngineTransportOwnedMarker",&rva,false)||rva!=P::OwnedMarkerRva
        ||!Memory(Address(At(C::Target,rva)),16,Address(modules[C::Target]),MEM_IMAGE,PAGE_READONLY)
        ||!Equal(At(C::Target,rva),expected,16))return false;
    for(const auto& function:F::Functions)if(Overlap(rva,16,function.beginRva,function.bodyBytes))return false;
    return true;
}}
bool ValidateModules(HINSTANCE self){
    if(!SameWide(P::Images[C::Bootstrap].module,L"RizToolbox.EngineBootstrap.dll")
        ||!SameWide(P::Images[C::Kernel].module,L"RizToolbox.EngineNodeKernel.dll")
        ||!SameWide(P::Images[C::Doorstop].module,L"winhttp.dll"))return Reject(RizEngineTransportBadDependencyV5);
    if constexpr(P::Owned){if(SameWide(P::Images[C::Target].module,L"UnityPlayer.dll")||SameWide(P::Images[C::Executable].module,L"Rizline.exe")
        ||GetModuleHandleW(L"UnityPlayer.dll")||GetModuleHandleW(L"GameAssembly.dll"))return Reject(RizEngineTransportAdmissionUnprovenV5);}
    else if(!SameWide(P::Images[C::Target].module,L"UnityPlayer.dll")||!SameWide(P::Images[C::Executable].module,L"Rizline.exe"))return Reject(RizEngineTransportBadDependencyV5);
    modules[C::Executable]=GetModuleHandleW(nullptr);modules[C::Bootstrap]=self;
    modules[C::Kernel]=GetModuleHandleW(P::Images[C::Kernel].module);
    modules[C::Target]=GetModuleHandleW(P::Images[C::Target].module);
    modules[C::Doorstop]=GetModuleHandleW(P::Images[C::Doorstop].module);
    for(U32 i=0;i<5;++i){if(!modules[i])return Reject(RizEngineTransportBadImageV5,RizEngineTransportModuleObjectV5,i);
        for(U32 n=0;n<i;++n)if(modules[n]==modules[i])return Reject(RizEngineTransportBadImageV5,RizEngineTransportModuleObjectV5,i);}
    if(!ValidateModule(C::Executable))return false;
    for(U32 i=0;i<5;++i)if(i!=C::Executable&&!ValidateModule(i))return false;
    info.bootstrapModule=Address(modules[0]);info.kernelModule=Address(modules[1]);info.targetModule=Address(modules[2]);
    info.executableModule=Address(modules[3]);info.doorstopModule=Address(modules[4]);
    if(!ExportSurface(C::Bootstrap,4,5)||!ExportSurface(C::Kernel,35,9)||!Graph(true))return Reject(RizEngineTransportBadDependencyV5);
    if constexpr(P::Owned){if(!OwnedMarker())return Reject(RizEngineTransportBadImageV5,RizEngineTransportModuleObjectV5,C::Target);}
    for(U32 i=0;i<5;++i)if(!SameFile(i))return Reject(RizEngineTransportBadHashV5,RizEngineTransportModuleObjectV5,i);
    modulesValidated=true;return true;
}
U32 TypedFamily(U32 typed){return typed<4?typed:typed<10?typed+1:11;}
U32 TypedKind(U32 typed){return typed<4?typed:typed<10?typed-4:typed-10;}
U32 SiteFamily(U32 site){return site<3?4:site<5?9:11;}
U32 SiteChannel(U32 site){if(site<3)return 0;if(site<5)return 4;const U32 f=F::PhysicalWindows[13+site].functionIndex;
    return f<4?f+3:f<10?f+3:f-10;}
U32 SitePhase(U32 site){return site<3?F::Sites[site].familyIndex+1:F::Sites[site].familyIndex;}
bool SameRf(const RUNTIME_FUNCTION& left,const F::RuntimeFunction& right){return left.BeginAddress==right.beginRva&&left.EndAddress==right.endRva&&left.UnwindData==right.unwindRva;}
const F::UnwindRecord* SourceUnwind(U32 rva){for(const auto& row:F::Unwinds)if(row.rva==rva)return &row;return nullptr;}
U32 SourceRfIndex(U32 begin,U32 end,U32 unwind){U32 flat=0;for(const auto& f:F::Functions)for(U32 n=0;n<f.runtimeFunctionCount;++n,++flat){
    const auto& r=f.runtimeFunctions[n];if(r.beginRva==begin&&r.endRva==end&&r.unwindRva==unwind)return flat;}return L::NoIndex;}
bool SourceUnwinds(){const auto directory=headers[C::Target]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    if(directory.VirtualAddress!=F::ExceptionRva||directory.Size!=F::ExceptionBytes||directory.Size%sizeof(RUNTIME_FUNCTION)
        ||!Image(C::Target,directory.VirtualAddress,directory.Size))return false;
    const auto* table=reinterpret_cast<const RUNTIME_FUNCTION*>(At(C::Target,directory.VirtualAddress));const U32 count=directory.Size/sizeof(RUNTIME_FUNCTION);
    for(const auto& unwind:F::Unwinds){if(!Image(C::Target,unwind.rva,unwind.byteCount)||!Equal(At(C::Target,unwind.rva),unwind.raw,unwind.byteCount))return false;}
    U32 flat=0;for(const auto& function:F::Functions){U32 overlapCount=0;
        if(function.index>=13||function.bodyBytes!=function.endRva-function.beginRva)return false;
        for(U32 t=0;t<count;++t)if(table[t].BeginAddress<function.endRva&&table[t].EndAddress>function.beginRva){
            if(overlapCount>=function.runtimeFunctionCount||!SameRf(table[t],function.runtimeFunctions[overlapCount]))return false;++overlapCount;}
        if(overlapCount!=function.runtimeFunctionCount)return false;
        for(U32 n=0;n<function.runtimeFunctionCount;++n,++flat){const auto& expected=function.runtimeFunctions[n];DWORD64 base=0;
            const auto* actual=RtlLookupFunctionEntry(Address(modules[C::Target])+expected.beginRva,&base,nullptr);
            if(!actual||base!=Address(modules[C::Target])||!SameRf(*actual,expected)
                ||Address(actual)<Address(table)||Address(actual)-Address(table)>=directory.Size||(Address(actual)-Address(table))%12)return false;
            const auto* unwind=SourceUnwind(expected.unwindRva);if(!unwind)return false;
        }
    }return flat==C::SourceRfCount;
}
bool Bodies(U64 committed){if(committed&~L::AllWindows)return false;
    for(const auto& function:F::Functions){if(!Memory(Address(modules[2])+function.beginRva,function.bodyBytes,Address(modules[2]),MEM_IMAGE,PAGE_EXECUTE_READ))return false;
        for(U32 b=0;b<function.bodyBytes;++b){const U32 rva=function.beginRva+b;U8 expected=function.body[b];
            for(U32 w=0;w<36;++w)if(committed&(1ull<<w)){const auto& window=F::PhysicalWindows[w];
                if(rva>=window.beginRva&&rva<window.endRva){expected=relayPlan.windows[w].patch[rva-window.beginRva];break;}}
            if(At(2,rva)[0]!=expected)return false;
        }
    }return SourceUnwinds();
}
U32 UnwindBytes(const RUNTIME_FUNCTION& rf,U32 module){if(!Image(module,rf.UnwindData,4))return 0;
    const U8* raw=At(module,rf.UnwindData);if((raw[0]&7)!=1||(raw[0]>>3)>4)return 0;const U32 flags=raw[0]>>3;
    if(flags!=0&&flags!=4)return 0;const U32 bytes=4+((U32(raw[2])+1)&~1u)*2+(flags==4?12u:0u);
    return Image(module,rf.UnwindData,bytes)?bytes:0;
}
bool OriginalShape(U32 index){const auto& fixed=F::Functions[index];const U64 original=Address(originals[index]);
    if(!Memory(original,fixed.prefixBytes+10,Address(modules[0]),MEM_IMAGE,PAGE_EXECUTE_READ)||!Equal(Pointer(original),fixed.prefix,fixed.prefixBytes))return false;
    const U8* tail=static_cast<const U8*>(originals[index])+fixed.prefixBytes;const bool callback=index==9;
    if(tail[0]!=0x4c||tail[1]!=0x8b||tail[2]!=(callback?0x15:0x1d)||tail[7]!=0x41||tail[8]!=0xff||tail[9]!=(callback?0xe2:0xe3))return false;
    std::int32_t relative;Copy(&relative,tail+3,4);const U64 after=original+fixed.prefixBytes+7;
    const U64 slot=relative<0?after-static_cast<U64>(-static_cast<std::int64_t>(relative)):after+static_cast<U32>(relative);
    const U64 expectedSlot=index<4?Address(&RizEngineContinuation[index]):Address(&RizEngineTransportContinuation[index-4]);
    if(slot!=expectedSlot||slot%8||!Memory(slot,8,Address(modules[0]),MEM_IMAGE)||Quad(Pointer(slot))!=Address(modules[2])+fixed.continuationRva)return false;
    DWORD64 base=0;const auto* rf=RtlLookupFunctionEntry(original,&base,nullptr);
    if(!rf||base!=Address(modules[0])||base+rf->BeginAddress!=original||rf->EndAddress-rf->BeginAddress!=fixed.prefixBytes+10)return false;
    const U32 bytes=UnwindBytes(*rf,0);if(!bytes)return false;const auto* raw=At(0,rf->UnwindData);
    return (raw[0]>>3)==0&&raw[1]==fixed.prefixBytes+10&&raw[3]==0;
}
bool PrepareOriginals(){if(continuationsPrepared)return false;
    for(U32 i=0;i<13;++i){const auto& fixed=F::Functions[i];const U64 target=Address(modules[2])+fixed.beginRva;
        const U64 value=target+fixed.prefixBytes;if(i<4){if(RizEngineContinuation[i])return false;
            InterlockedExchangePointer(&RizEngineContinuation[i],Pointer(value));}
        else{if(RizEngineTransportContinuation[i-4])return false;InterlockedExchange64(reinterpret_cast<volatile LONG64*>(&RizEngineTransportContinuation[i-4]),static_cast<LONG64>(value));}
        if(!OriginalShape(i))return false;auto& row=info.typed[i];row.version=5;row.byteSize=sizeof(row);row.index=i;row.channelKind=TypedKind(i);
        row.familyIndex=TypedFamily(i);row.moduleIndex=2;row.windowIndex=i;row.flags=1;row.module=Address(modules[2]);row.target=target;
        row.original=Address(originals[i]);row.continuation=value;row.rva=fixed.beginRva;row.endRva=fixed.endRva;row.prefixBytes=fixed.prefixBytes;
        row.sourceRfCount=fixed.runtimeFunctionCount;row.originalRfFirst=C::OriginalRfFirst+i;row.originalRfCount=1;
        row.originalBytes=fixed.prefixBytes+10;row.originalModuleIndex=0;row.scratchRegister=i==9?10:11;row.sourceBodyBytes=fixed.bodyBytes;
        if(!Digest(fixed.bodySha256,row.bodySha256))return false;Hash(originals[i],row.originalBytes,row.originalCodeSha256);
        if(!Record(RizEngineTransportOriginalPreparedV5,RizEngineTransportWindowObjectV5,i))return false;
    }continuationsPrepared=true;return true;
}
bool KernelEntries(R::Request* request){
    if(RizEngineNodeContext_GetAbiVersion()!=2||RizEngineNodeContext_GetLimit()!=32||RizEngineNodeContext_GetFrameSize()!=112
        ||RizEngineNodeContext_GetCompletionSize()!=32||RizEngineNodeContext_GetInlineAbiVersion()!=1||RizEngineNodeContext_GetInlineLimit()!=32
        ||RizEngineNodeContext_GetInlineFrameSize()!=144||RizEngineSchedulerContext_GetAbiVersion()!=1||RizEngineSchedulerContext_GetLimit()!=32
        ||RizEngineSchedulerContext_GetFrameSize()!=256||RizEngineSchedulerContext_GetCompletionSize()!=48||RizEngineSchedulerContext_GetRowFrameSize()!=224
        ||RizEngineWaitContext_GetAbiVersion()!=1||RizEngineWaitContext_GetLimit()!=32||RizEngineWaitContext_GetFrameSize()!=240
        ||RizEngineWaitContext_GetCompletionSize()!=48||RizEngineWaitContext_GetPhaseFrameSize()!=288)return false;
    for(U32 i=0;i<4;++i)request->typedEntries[i]=Address(RizEngineNodeContext_GetEntry(i,P::Slot));
    for(U32 i=0;i<6;++i)request->typedEntries[4+i]=Address(RizEngineSchedulerContext_GetEntry(i,P::Slot));
    for(U32 i=0;i<3;++i)request->typedEntries[10+i]=Address(RizEngineWaitContext_GetEntry(i,P::Slot));
    request->inlineEntry=Address(RizEngineNodeContext_GetInlineEntry());request->rowEntry=Address(RizEngineSchedulerContext_GetDrainRowEntry());
    request->waitPhaseEntry=Address(RizEngineWaitContext_GetPhaseEntry());U64 values[16];
    for(U32 i=0;i<13;++i)values[i]=request->typedEntries[i];values[13]=request->inlineEntry;values[14]=request->rowEntry;values[15]=request->waitPhaseEntry;
    for(U32 i=0;i<16;++i){if(!Memory(values[i],1,Address(modules[1]),MEM_IMAGE,PAGE_EXECUTE_READ))return false;
        for(U32 n=0;n<i;++n)if(values[i]==values[n])return false;}return true;
}
bool AllocateNear(){auto& resource=info.resources[0];resource.version=5;resource.byteSize=sizeof(resource);resource.index=0;info.resourceCount=1;
    resource.tableBase=Address(modules[2]);resource.tableCount=23;resource.rfFirst=C::RelayRfFirst;resource.rfCount=23;
    SYSTEM_INFO system;Zero(&system,sizeof(system));GetSystemInfo(&system);const U64 granularity=system.dwAllocationGranularity;
    if(!granularity||(granularity&(granularity-1))||granularity>0x10000)return Reject(RizEngineTransportNearAllocationFailedV5,RizEngineTransportResourceObjectV5,0);
    U64 at=Address(modules[2])+headers[2]->OptionalHeader.SizeOfImage;
    if(at>UINT64_MAX-granularity)return Reject(RizEngineTransportNearAllocationFailedV5,RizEngineTransportResourceObjectV5,0);
    at=(at+granularity-1)&~(granularity-1);U64 limit{};
    if(!Add(Address(modules[2]),0x7fff0000ULL,&limit))return Reject(RizEngineTransportNearAllocationFailedV5,RizEngineTransportResourceObjectV5,0);
    for(U32 attempts=0;attempts<65536&&at<limit;++attempts){MEMORY_BASIC_INFORMATION memory;Zero(&memory,sizeof(memory));
        if(VirtualQuery(Pointer(at),&memory,sizeof(memory))!=sizeof(memory))break;U64 next{};
        if(!Add(Address(memory.BaseAddress),memory.RegionSize,&next)||next<=at)break;
        if(memory.State==MEM_FREE&&next-at>=R::ArenaMinimumBytes){++resource.allocationAttempts;SetLastError(ERROR_SUCCESS);
            void* block=VirtualAlloc(Pointer(at),R::ArenaMinimumBytes,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);
            if(block){resource.allocationBase=Address(block);resource.allocationBytes=R::ArenaMinimumBytes;resource.tableAddress=Address(block);
                resource.allocationOwned=1;resource.allocationSucceeded=1;resource.allocationProtection=PAGE_READWRITE;resource.observedProtection=PAGE_READWRITE;
                resource.rootRetained=1;resource.flags=RizEngineTransportAllocationOwnedV5|RizEngineTransportResourceRetainedV5;info.retainedResources=1;
                if(Address(block)!=at||!Memory(Address(block),R::ArenaMinimumBytes,Address(block),MEM_PRIVATE,PAGE_READWRITE))
                    return Reject(RizEngineTransportNearAllocationFailedV5,RizEngineTransportResourceObjectV5,0);
                return Record(RizEngineTransportResourceAllocatedV5,RizEngineTransportResourceObjectV5,0,0,0,Address(block));}
            resource.lastWin32Error=GetLastError();
        }
        if(next>UINT64_MAX-granularity)break;at=(next+granularity-1)&~(granularity-1);
    }return Reject(RizEngineTransportNearAllocationFailedV5,RizEngineTransportResourceObjectV5,0,resource.lastWin32Error);
}
bool PrepareRelays(){if(!AllocateNear())return false;R::Request request;Zero(&request,sizeof(request));request.version=R::Version;request.byteSize=sizeof(request);
    request.fixedImageBase=Address(modules[2]);request.fixedImageBytes=F::ImageSize;request.nearRw=info.resources[0].allocationBase;request.nearCapacity=R::ArenaMinimumBytes;
    if(!KernelEntries(&request))return Reject(RizEngineTransportBadKernelV5);
    const auto result=R::Prepare(&request,&relayPlan);if(result!=R::Success)return Reject(RizEngineTransportBadCoverageV5,RizEngineTransportResourceObjectV5,0,result);
    if(R::VerifyPrepared(&relayPlan)!=R::Success)return Reject(RizEngineTransportBadCoverageV5,RizEngineTransportResourceObjectV5,0);
    auto& resource=info.resources[0];resource.tableAddress=relayPlan.tableAddress;resource.tableBase=relayPlan.tableBase;
    Hash(Pointer(resource.tableAddress),23*sizeof(RUNTIME_FUNCTION),resource.tableSha256);
    if(!Record(RizEngineTransportRelayPreparedV5,RizEngineTransportResourceObjectV5,0,0,relayPlan.nearBytesWritten))return false;
    ++resource.tableRegisterAttempts;SetLastError(ERROR_SUCCESS);
    if(!RtlAddFunctionTable(reinterpret_cast<PRUNTIME_FUNCTION>(resource.tableAddress),23,resource.tableBase))
        return Reject(RizEngineTransportRuntimeTableFailedV5,RizEngineTransportResourceObjectV5,0,GetLastError());
    ++resource.tableRegisterSucceeded;resource.tableRegistered=1;resource.flags|=RizEngineTransportFunctionTableRegisteredV5;
    if(!Record(RizEngineTransportTableRegisteredV5,RizEngineTransportResourceObjectV5,0))return false;
    ++resource.rxProtectAttempts;DWORD old=0;SetLastError(ERROR_SUCCESS);
    if(!VirtualProtect(Pointer(resource.allocationBase),resource.allocationBytes,PAGE_EXECUTE_READ,&old)){
        resource.lastWin32Error=GetLastError();return Reject(RizEngineTransportProtectFailedV5,RizEngineTransportResourceObjectV5,0,resource.lastWin32Error);}
    resource.observedProtection=PAGE_EXECUTE_READ;resource.protectionOutstanding=0;++resource.rxProtectSucceeded;resource.flags|=RizEngineTransportAllocationRxV5;
    if(old!=PAGE_READWRITE||!Memory(resource.allocationBase,resource.allocationBytes,resource.allocationBase,MEM_PRIVATE,PAGE_EXECUTE_READ))
        return Reject(RizEngineTransportProtectFailedV5,RizEngineTransportResourceObjectV5,0);
    SetLastError(ERROR_SUCCESS);if(!FlushInstructionCache(GetCurrentProcess(),Pointer(resource.allocationBase),resource.allocationBytes))
        return Reject(RizEngineTransportFlushFailedV5,RizEngineTransportResourceObjectV5,0,GetLastError());
    if(!Record(RizEngineTransportResourceProtectedV5,RizEngineTransportResourceObjectV5,0))return false;
    for(U32 i=0;i<13;++i)info.typed[i].entry=request.typedEntries[i];
    for(U32 i=0;i<23;++i){const auto& source=F::Sites[i];const auto& relay=relayPlan.sites[i];auto& row=info.sites[i];
        row.version=5;row.byteSize=sizeof(row);row.index=i;row.familyIndex=SiteFamily(i);row.channelKind=SiteChannel(i);row.phaseKind=SitePhase(i);
        row.windowIndex=13+i;row.functionIndex=F::PhysicalWindows[13+i].functionIndex;row.observationRva=source.observationRva;
        row.logicalBytes=source.logicalSpanBytes;row.logicalContinuationRva=source.logicalContinuationRva;row.physicalContinuationRva=source.physicalContinuationRva;
        row.branchTargetRva=source.branchTargetRva;row.sitePc=relay.logicalSitePc;row.relayReturnPc=relay.relayReturnPc;row.probeEntry=relay.probeEntry;
        row.sourceRfIndex=relay.sourceRfIndex;row.resourceIndex=0;row.relay=relay.relay;row.alternateContinuation=relay.alternateContinuation;row.flags=1;
        if(source.expandedPhysicalBlock){const auto& bypass=F::ConditionalBypasses[i==9?0:i==11?1:2];row.bypassReplayRva=bypass.zeroReplayRva;
            row.bypassReplayBytes=bypass.zeroReplayBytes;row.bypassContinuationRva=bypass.zeroContinuationRva;}
    }return true;
}
bool FillRf(U32 index,U32 kind,U32 module,U32 resource,U32 tableIndex,U64 imageBase,const RUNTIME_FUNCTION* actual,U32 bytes,U32 parent){
    if(index>=128||!actual||!bytes||bytes>128)return false;auto& row=info.runtimeFunctions[index];
    const U64 unwind=imageBase+actual->UnwindData;const DWORD type=resource==L::NoIndex?MEM_IMAGE:MEM_PRIVATE;
    const U64 allocation=resource==L::NoIndex?Address(modules[module]):info.resources[resource].allocationBase;
    if(!Memory(Address(actual),12,allocation,type)||!Memory(unwind,bytes,allocation,type))return false;
    const auto* raw=static_cast<const U8*>(Pointer(unwind));if((raw[0]&7)!=1||(raw[0]>>3)>4)return false;
    row.version=5;row.byteSize=sizeof(row);row.index=index;row.kind=kind;row.moduleIndex=module;row.resourceIndex=resource;row.tableEntryIndex=tableIndex;
    row.parentIndex=parent;row.imageBase=imageBase;row.runtimeFunctionAddress=Address(actual);row.beginRva=actual->BeginAddress;
    row.endRva=actual->EndAddress;row.unwindRva=actual->UnwindData;row.unwindBytes=bytes;row.unwindFlags=raw[0]>>3;
    row.prologBytes=raw[1];row.codeSlots=raw[2];row.frameRegisterAndOffset=(raw[3]&15)|((U32(raw[3]>>4)*16)<<8);Hash(Pointer(unwind),bytes,row.unwindSha256);return true;
}
bool ExactRelayUnwind(U32 index){const auto& relay=relayPlan.sites[index];const auto& source=F::PhysicalWindows[13+index].runtimeFunction;
    if(relay.unwindBytes!=16||relay.unwind!=relay.relay+R::UnwindOffset)return false;const U8* raw=static_cast<const U8*>(Pointer(relay.unwind));
    if(raw[0]!=0x21||raw[1]||raw[2]||raw[3]||Word(raw+4)!=source.beginRva||Word(raw+8)!=source.endRva)return false;
    if(index<17||index>20)return Word(raw+12)==source.unwindRva;
    constexpr U8 partial[12]{0x01,0x00,0x03,0x00,0x00,0x01,0x2a,0x00,0x00,0x70,0x00,0x00};
    return Word(raw+12)==relay.toImageStart+192&&Equal(Pointer(relay.relay+192),partial,sizeof(partial));
}
bool StaticRuntimeFunctions(){U32 flat=0;const auto directory=headers[2]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    for(U32 i=0;i<13;++i){const auto& function=F::Functions[i];info.typed[i].sourceRfFirst=flat;
        for(U32 n=0;n<function.runtimeFunctionCount;++n,++flat){const auto& expected=function.runtimeFunctions[n];DWORD64 base=0;
            const auto* actual=RtlLookupFunctionEntry(Address(modules[2])+expected.beginRva,&base,nullptr);const auto* unwind=SourceUnwind(expected.unwindRva);
            if(!actual||base!=Address(modules[2])||!SameRf(*actual,expected)||!unwind)return false;
            const U32 parent=unwind->flags==4?SourceRfIndex(unwind->chain.beginRva,unwind->chain.endRva,unwind->chain.unwindRva):L::NoIndex;
            if(unwind->flags==4&&parent==L::NoIndex)return false;
            const U32 position=static_cast<U32>((Address(actual)-Address(At(2,directory.VirtualAddress)))/12);
            if(!FillRf(flat,RizEngineTransportSourceRuntimeFunctionV5,2,L::NoIndex,position,base,actual,unwind->byteCount,parent))return false;
        }
    }if(flat!=27)return false;
    for(U32 i=0;i<13;++i){DWORD64 base=0;const auto* actual=RtlLookupFunctionEntry(Address(originals[i]),&base,nullptr);
        if(!actual||base!=Address(modules[0])||!OriginalShape(i))return false;const auto ownDirectory=headers[0]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
        if(Address(actual)<Address(At(0,ownDirectory.VirtualAddress))||Address(actual)-Address(At(0,ownDirectory.VirtualAddress))>=ownDirectory.Size)return false;
        const U32 position=static_cast<U32>((Address(actual)-Address(At(0,ownDirectory.VirtualAddress)))/12);
        if(!FillRf(27+i,RizEngineTransportOriginalRuntimeFunctionV5,0,L::NoIndex,position,base,actual,UnwindBytes(*actual,0),L::NoIndex))return false;
    }
    info.runtimeFunctionCount=40;return true;
}
bool RuntimeFunctions(){if(!StaticRuntimeFunctions())return false;
    for(U32 i=0;i<23;++i){const auto& relay=relayPlan.sites[i];DWORD64 base=0;const auto* actual=RtlLookupFunctionEntry(relay.relay,&base,nullptr);
        if(!actual||base!=Address(modules[2])||Address(actual)!=relayPlan.tableAddress+U64(i)*12||!Equal(actual,&relay.runtimeFunction,12)||!ExactRelayUnwind(i))return false;
        const U32 parent=i>=17&&i<=20?63+i-17:relay.sourceRfIndex;
        if(!FillRf(40+i,RizEngineTransportRelayRuntimeFunctionV5,2,0,i,base,actual,16,parent))return false;
        DWORD64 returnBase=0;if(RtlLookupFunctionEntry(relay.relayReturnPc,&returnBase,nullptr)!=actual||returnBase!=base)return false;
    }
    for(U32 i=0;i<4;++i){const auto& relay=relayPlan.sites[17+i];const auto* parent=reinterpret_cast<const RUNTIME_FUNCTION*>(relay.unwind+4);
        if(!FillRf(63+i,RizEngineTransportPartialPrimaryRuntimeFunctionV5,2,0,L::NoIndex,Address(modules[2]),parent,12,L::NoIndex))return false;
    }info.runtimeFunctionCount=67;info.resources[0].rfCount=27;return true;
}
void WindowFacts(U32 index){const auto& fixed=F::PhysicalWindows[index];const auto& planned=relayPlan.windows[index];auto& row=info.windows[index];
    row.version=5;row.byteSize=sizeof(row);row.index=index;row.role=index<13?RizEngineTransportTypedWindowV5:RizEngineTransportSideWindowV5;
    row.typedIndex=fixed.functionIndex;row.siteIndex=index<13?L::NoIndex:index-13;row.familyIndex=index<13?TypedFamily(index):SiteFamily(index-13);
    row.moduleIndex=2;row.sourceRfIndex=SourceRfIndex(fixed.runtimeFunction.beginRva,fixed.runtimeFunction.endRva,fixed.runtimeFunction.unwindRva);
    row.physicalRva=fixed.beginRva;row.physicalBytes=fixed.byteCount;row.logicalRva=fixed.observationRva;row.resourceIndex=index<13?L::NoIndex:0;
    row.source=planned.source;row.patchTarget=index<13?planned.entry:planned.relay;row.logicalBytes=index<13?fixed.byteCount:F::Sites[index-13].logicalSpanBytes;
    Copy(row.expectedBytes,planned.expected,fixed.byteCount);Copy(row.patchBytes,planned.patch,fixed.byteCount);Hash(fixed.bytes,fixed.byteCount,row.sourceWindowSha256);
    row.originalProtection=PAGE_EXECUTE_READ;if(!row.observedProtection)row.observedProtection=PAGE_EXECUTE_READ;
    if(index>=13){const auto& site=relayPlan.sites[index-13];row.relay=site.relay;row.relayReturnPc=site.relayReturnPc;row.continuation=site.continuation;
        row.alternateContinuation=site.alternateContinuation;row.probeEntry=site.probeEntry;row.literalAddress=site.literal;row.literalValue=site.probeEntry;
        row.relayBytes=site.codeBytes;row.literalBytes=8;row.relayRfFirst=40+index-13;row.relayRfCount=1;
        row.observedLiteralValue=Quad(Pointer(site.literal));row.relayOriginalProtection=PAGE_READWRITE;row.relayObservedProtection=info.resources[0].observedProtection;
        Hash(Pointer(site.relay),site.codeBytes,row.relayCodeSha256);
        if(site.expandedPhysicalBlock)row.flags|=RizEngineTransportWindowExpandedV5|RizEngineTransportWindowConditionalBypassV5;
    }
    row.flags|=RizEngineTransportWindowPreparedV5;
}
bool PreparedWindows(){for(U32 i=0;i<36;++i){WindowFacts(i);const auto& a=info.windows[i];
        if(a.sourceRfIndex==L::NoIndex||a.source!=Address(modules[2])+F::PhysicalWindows[i].beginRva||a.physicalBytes>32||!a.physicalBytes)return false;
        for(U32 n=0;n<i;++n)if(Overlap(a.source,a.physicalBytes,info.windows[n].source,info.windows[n].physicalBytes))return false;
        info.preparedWindows|=1ull<<i;
    }return info.preparedWindows==L::AllWindows&&Record(RizEngineTransportCoverageValidatedV5);
}
void SyncFamilies(){if(familyReceipt.version!=K::Version||familyReceipt.byteSize!=sizeof(familyReceipt))return;
    info.configuredFamilies=familyReceipt.configuredFamilies;info.boundFamilies=familyReceipt.verifiedFamilies;info.retainedFamilies=familyReceipt.retainedFamilies;
    info.failureFamilyIndex=familyReceipt.failureFamily;Copy(info.families,familyReceipt.families,sizeof(info.families));
    for(U32 i=0;i<13;++i){auto& row=info.typed[i];const auto& family=info.families[TypedFamily(i)];
        row.entry=familyReceipt.typedEntries[i];row.helperSlot=family.slot;row.helperEpoch=family.epoch;
        if(family.flags&RizEngineTransportFamilyIdentityVerifiedV5)row.flags|=2;
    }
    for(U32 i=0;i<23;++i){auto& row=info.sites[i];const auto& family=info.families[SiteFamily(i)];row.helperSlot=family.slot;row.helperEpoch=family.epoch;
        if(family.flags&RizEngineTransportFamilyIdentityVerifiedV5)row.flags|=2;
    }
}
bool ConfigureFamilies(){Zero(&familyRequest,sizeof(familyRequest));familyRequest.version=K::Version;familyRequest.byteSize=sizeof(familyRequest);
    familyRequest.slot=P::Slot;familyRequest.mainOsTid=info.mainThread;familyRequest.profile=P::Profile;familyRequest.kernelModule=Address(modules[1]);
    familyRequest.targetModule=Address(modules[2]);familyRequest.originalModule=Address(modules[0]);familyRequest.relayAllocationBase=info.resources[0].allocationBase;
    familyRequest.relayAllocationBytes=info.resources[0].allocationBytes;
    for(U32 i=0;i<13;++i){familyRequest.targets[i]=info.typed[i].target;familyRequest.originals[i]=info.typed[i].original;}
    for(U32 i=0;i<23;++i){familyRequest.sites[i].logicalPc=info.sites[i].sitePc;familyRequest.sites[i].relayReturnPc=info.sites[i].relayReturnPc;}
    info.state=RizEngineTransportConfiguringV5;const auto result=K::PrepareAndConfigure(&familyRequest,&familyReceipt);SyncFamilies();
    // The helper publishes all possible claims privately before each real call.
    // A failed/exceptional result is copied before this producer can return.
    for(U32 i=0;i<12;++i)if(familyReceipt.attemptedFamilies&(1ull<<i))
        if(!Record(RizEngineTransportFamilyConfiguredEventV5,RizEngineTransportFamilyObjectV5,i,
            familyReceipt.configureResults[i],familyReceipt.families[i].configureSucceeded,familyReceipt.families[i].epoch))return false;
    if(result!=K::Success||info.configuredFamilies!=L::AllFamilies||info.boundFamilies!=L::AllFamilies||info.retainedFamilies!=L::AllFamilies)
        return Reject(RizEngineTransportConfigureFailedV5,RizEngineTransportFamilyObjectV5,info.failureFamilyIndex,result);
    for(U32 i=0;i<13;++i)if(info.typed[i].entry!=relayPlan.windows[i].entry||info.typed[i].flags!=3)return Reject(RizEngineTransportBadKernelV5,RizEngineTransportFamilyObjectV5,TypedFamily(i));
    return K::Verify(&familyRequest,&familyReceipt)==K::Success;
}
U32 WriteStage(U32 stage){switch(stage){case W::Preflight:return RizEngineTransportCoverageValidatedV5;
    case W::ProtectBegin:return RizEngineTransportProtectAttemptedV5;case W::Protected:return RizEngineTransportProtectSucceededEventV5;
    case W::WriteBegin:return RizEngineTransportWriteAttemptedEventV5;case W::Written:return RizEngineTransportWriteProgressV5;
    case W::Readback:return RizEngineTransportReadbackVerifiedV5;case W::Restored:return RizEngineTransportProtectionRestoredEventV5;
    case W::Flushed:return RizEngineTransportFlushedV5;case W::Committed:return RizEngineTransportCommittedV5;default:return RizEngineTransportFailedV5;}}
void SyncWrites(U32 eventBase){if(writeReceipt.version!=W::Version||writeReceipt.byteSize!=sizeof(writeReceipt))return;
    info.sourceWriteAttempts=writeReceipt.sourceWriteAttempts;info.sourceWriteCompletions=writeReceipt.sourceWriteCompletions;
    info.protectionOutstanding=writeReceipt.protectionOutstanding;info.preparedWindows=writeReceipt.preparedWindows;
    info.writeAttemptedWindows=writeReceipt.attemptedWindows;info.committedWindows=writeReceipt.committedWindows;
    info.outstandingProtectionWindows=writeReceipt.outstandingWindows;info.failureWindowIndex=writeReceipt.failureWindow;
    for(U32 i=0;i<36;++i){if(writeReceipt.windows[i].version==5){Copy(&info.windows[i],&writeReceipt.windows[i],sizeof(info.windows[i]));
            auto& row=info.windows[i];if(row.writeSequence)row.writeSequence+=eventBase;if(row.commitSequence)row.commitSequence+=eventBase;
            if(row.readbackSequence)row.readbackSequence+=eventBase;}
        WindowFacts(i);
        if(info.committedWindows&(1ull<<i)){if(i<13)info.typed[i].flags|=4;else info.sites[i-13].flags|=4;}
    }
}
bool CommitWindows(){W::Request request;Zero(&request,sizeof(request));request.version=W::Version;request.byteSize=sizeof(request);request.profile=P::Profile;request.prepared=&relayPlan;
    info.state=RizEngineTransportPublishingV5;const U32 eventBase=info.eventCount;const auto result=W::Commit(&request,&writeReceipt);SyncWrites(eventBase);
    for(U32 i=0;i<writeReceipt.eventCount;++i){const auto& event=writeReceipt.events[i];
        if(!Record(WriteStage(event.stage),event.window==W::NoIndex?RizEngineTransportPublicationObjectV5:RizEngineTransportWindowObjectV5,event.window,
            event.lastWin32Error,event.bytesWritten,event.result))return false;
    }
    if(result!=W::Success){U32 failure=RizEngineTransportWriteFailedV5;
        if(result==W::BadSource||result==W::BadPlan)failure=RizEngineTransportBadSourceV5;
        else if(result==W::BadRuntimeTable)failure=RizEngineTransportRuntimeTableFailedV5;
        else if(result==W::ProtectFailed||result==W::BadMemory)failure=RizEngineTransportProtectFailedV5;
        else if(result==W::ReadbackFailed)failure=RizEngineTransportBadReadbackV5;
        else if(result==W::RestoreFailed)failure=RizEngineTransportRestoreFailedV5;
        else if(result==W::FlushFailed)failure=RizEngineTransportFlushFailedV5;
        return Reject(failure,RizEngineTransportWindowObjectV5,info.failureWindowIndex,result);
    }return info.committedWindows==L::AllWindows&&info.sourceWriteAttempts==36&&info.sourceWriteCompletions==36&&!info.protectionOutstanding;
}
bool VerifyRuntimeFunctions(){if(info.runtimeFunctionCount!=67)return false;
    for(U32 index=0;index<67;++index){const auto& row=info.runtimeFunctions[index];
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=index||row.moduleIndex>4||!row.unwindBytes||row.unwindBytes>128)return false;
        const auto* actual=reinterpret_cast<const RUNTIME_FUNCTION*>(row.runtimeFunctionAddress);const bool inNearArena=index>=40;
        if(!Memory(Address(actual),12,inNearArena?info.resources[0].allocationBase:Address(modules[row.moduleIndex]),inNearArena?MEM_PRIVATE:MEM_IMAGE))return false;
        if(actual->BeginAddress!=row.beginRva||actual->EndAddress!=row.endRva||actual->UnwindData!=row.unwindRva)return false;
        const U64 unwind=row.imageBase+row.unwindRva;
        if(!Memory(unwind,row.unwindBytes,inNearArena?info.resources[0].allocationBase:Address(modules[row.moduleIndex]),inNearArena?MEM_PRIVATE:MEM_IMAGE))return false;
        const U8* raw=static_cast<const U8*>(Pointer(unwind));U8 digest[32];Hash(raw,row.unwindBytes,digest);
        if(!Equal(digest,row.unwindSha256,32)||(raw[0]&7)!=1||U32(raw[0]>>3)!=row.unwindFlags||raw[1]!=row.prologBytes||raw[2]!=row.codeSlots
            ||((raw[3]&15)|((U32(raw[3]>>4)*16)<<8))!=row.frameRegisterAndOffset)return false;
        if(row.unwindFlags==4){const U32 at=4+((row.codeSlots+1)&~1u)*2;
            if(row.parentIndex>=67||at+12!=row.unwindBytes)return false;const auto& parent=info.runtimeFunctions[row.parentIndex];
            if(Word(raw+at)!=parent.beginRva||Word(raw+at+4)!=parent.endRva||Word(raw+at+8)!=parent.unwindRva)return false;
        }else if(row.parentIndex!=L::NoIndex)return false;
        if(index<63){DWORD64 base=0;const auto* looked=RtlLookupFunctionEntry(row.imageBase+row.beginRva,&base,nullptr);
            if(looked!=actual||base!=row.imageBase)return false;
        }else{const U32 site=17+index-63;const auto& relay=relayPlan.sites[site];const auto& fixed=F::PhysicalWindows[13+site].runtimeFunction;
            if(row.kind!=RizEngineTransportPartialPrimaryRuntimeFunctionV5||row.resourceIndex!=0||row.moduleIndex!=2||row.tableEntryIndex!=L::NoIndex
                ||row.runtimeFunctionAddress!=relay.unwind+4||row.beginRva!=fixed.beginRva||row.endRva!=fixed.endRva
                ||row.unwindRva!=relay.toImageStart+192||row.unwindBytes!=12||!ExactRelayUnwind(site))return false;}
    }return true;
}
bool VerifyNear(){const auto& resource=info.resources[0];if(info.resourceCount!=1||info.retainedResources!=1||resource.version!=5||resource.byteSize!=sizeof(resource)||resource.index
    ||resource.flags!=15||!resource.allocationOwned||resource.allocationSucceeded!=1||resource.allocationBytes!=R::ArenaMinimumBytes
    ||resource.allocationBase!=relayPlan.nearRw||resource.tableAddress!=relayPlan.tableAddress||resource.tableBase!=Address(modules[2])
    ||resource.tableCount!=23||resource.rfFirst!=40||resource.rfCount!=27||resource.tableRegisterAttempts!=1||resource.tableRegisterSucceeded!=1
    ||resource.tableRegistered!=1||resource.rootRetained!=1||resource.rxProtectAttempts!=1||resource.rxProtectSucceeded!=1
    ||resource.protectionOutstanding||resource.observedProtection!=PAGE_EXECUTE_READ
    ||!Memory(resource.allocationBase,resource.allocationBytes,resource.allocationBase,MEM_PRIVATE,PAGE_EXECUTE_READ)
    ||!Equal(Pointer(resource.allocationBase),relayPlan.preparedBytes,R::ArenaUsedBytes))return false;
    U8 digest[32];Hash(Pointer(resource.tableAddress),23*sizeof(RUNTIME_FUNCTION),digest);if(!Equal(digest,resource.tableSha256,32))return false;
    for(U32 i=0;i<23;++i){const auto& site=relayPlan.sites[i];const auto& row=info.sites[i];const auto& fixed=F::Sites[i];const auto& window=info.windows[13+i];
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=i||row.familyIndex!=SiteFamily(i)||row.channelKind!=SiteChannel(i)||row.phaseKind!=SitePhase(i)
            ||row.flags!=7||row.sitePc!=Address(modules[2])+fixed.observationRva||row.relay!=site.relay||row.relayReturnPc!=site.relayReturnPc
            ||row.helperSlot!=P::Slot||row.helperEpoch!=info.families[SiteFamily(i)].epoch||row.probeEntry!=site.probeEntry
            ||row.sourceRfIndex!=site.sourceRfIndex||row.resourceIndex||!ExactRelayUnwind(i)||site.relayReturnPc>=site.relay+site.codeBytes)return false;
        DWORD64 base=0;if(RtlLookupFunctionEntry(site.relayReturnPc,&base,nullptr)!=reinterpret_cast<const RUNTIME_FUNCTION*>(resource.tableAddress+U64(i)*12)||base!=resource.tableBase)return false;
        if(Quad(Pointer(site.literal))!=site.probeEntry||window.literalAddress!=site.literal||window.literalValue!=site.probeEntry||window.observedLiteralValue!=site.probeEntry)return false;
        Hash(Pointer(site.relay),site.codeBytes,digest);if(!Equal(digest,window.relayCodeSha256,32))return false;
    }return VerifyRuntimeFunctions();
}
bool VerifyCurrent(bool frozen){if(info.version!=5||info.byteSize!=sizeof(info)||info.schemaMagic!=L::Magic||info.profile!=P::Profile
    ||info.processId!=GetCurrentProcessId()||!info.claimed||!info.startupStatic||info.attachReason!=DLL_PROCESS_ATTACH||!info.epoch
    ||info.state!=RizEngineTransportReadyV5||info.failure||info.eventOverflow||info.eventCount>L::EventCapacity
    ||info.configuredFamilies!=L::AllFamilies||info.boundFamilies!=L::AllFamilies||info.retainedFamilies!=L::AllFamilies
    ||info.preparedWindows!=L::AllWindows||info.writeAttemptedWindows!=L::AllWindows||info.committedWindows!=L::AllWindows
    ||info.sourceWriteAttempts!=36||info.sourceWriteCompletions!=36||info.protectionOutstanding||info.outstandingProtectionWindows
    ||info.moduleCount!=5||info.typedCount!=13||info.siteCount!=23||info.windowCount!=36||info.familyCount!=12||info.eventCapacity!=1024
    ||info.mainThreadProof!=(P::Owned?RizEngineTransportMainThreadOwnedContractV5:RizEngineTransportMainThreadFixedProductionContractV5)
    ||!info.mainThread||InterlockedCompareExchange(&stickyUnknown,0,0))return false;
    if(frozen&&(!readyFrozen||!Equal(&info,&readySnapshot,sizeof(info))))return false;
    if constexpr(P::Production){if(!P::ProductionFirstInstallAuthorized)return false;}
    for(U32 i=0;i<5;++i){const auto* actual=ImageHeader(modules[i]);const auto& record=info.modules[i];
        if(!actual||actual!=headers[i]||record.module!=Address(modules[i])||record.flags!=(i==0?7u:15u)
            ||record.imageSize!=actual->OptionalHeader.SizeOfImage||record.timeDateStamp!=actual->FileHeader.TimeDateStamp||!SameFile(i)
            ||(i!=3&&!pinned[i]))return false;}
    if(!Graph(false)||(P::Owned&&!OwnedMarker())||!Bodies(L::AllWindows)||!VerifyNear()||K::Verify(&familyRequest,&familyReceipt)!=K::Success)return false;
    for(U32 i=0;i<13;++i){const auto& row=info.typed[i];const auto& fixed=F::Functions[i];
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=i||row.channelKind!=TypedKind(i)||row.familyIndex!=TypedFamily(i)||row.flags!=7
            ||row.module!=Address(modules[2])||row.moduleIndex!=2||row.target!=Address(modules[2])+fixed.beginRva||row.original!=Address(originals[i])
            ||row.continuation!=row.target+fixed.prefixBytes||row.helperSlot!=P::Slot||row.helperEpoch!=info.families[TypedFamily(i)].epoch
            ||row.entry!=familyReceipt.typedEntries[i]||row.entry!=relayPlan.windows[i].entry||!OriginalShape(i))return false;
        U8 digest[32];Hash(originals[i],row.originalBytes,digest);if(!Equal(digest,row.originalCodeSha256,32))return false;
    }
    for(U32 i=0;i<36;++i){const auto& row=info.windows[i];const auto& fixed=F::PhysicalWindows[i];
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=i||row.source!=Address(modules[2])+fixed.beginRva||row.physicalBytes!=fixed.byteCount
            ||row.sourceWriteAttempts!=1||row.sourceBytesWritten!=fixed.byteCount||row.readbackAttempts!=1||row.readbackVerified!=1
            ||row.protectAttempts!=1||row.protectSucceeded!=1||row.restoreAttempts!=1||row.restoreSucceeded!=1||row.flushAttempts!=1||row.flushSucceeded!=1
            ||row.protectionOutstanding||row.observedProtection!=PAGE_EXECUTE_READ||row.readbackBytes!=fixed.byteCount
            ||!Equal(Pointer(row.source),row.patchBytes,row.physicalBytes)||!Equal(row.observedBytes,row.patchBytes,row.physicalBytes)
            ||!Memory(row.source,row.physicalBytes,Address(modules[2]),MEM_IMAGE,PAGE_EXECUTE_READ))return false;
    }
    for(U32 i=0;i<info.eventCount;++i)if(info.events[i].sequence!=i+1)return false;
    if constexpr(P::Production){
        namespace AF=RizEngineTransportAdmissionFacts;
        namespace AC=RizEngineTransportAdmissionCrypto;
        if constexpr(!P::ProductionFirstInstallAuthorized){return false;}else{
        if(info.mainThread!=info.producerThread||info.mainThread!=info.ownerThread)return false;
        const U32 requiredFlags=frozen?RizEngineTransportWitnessAllV5
            :(RizEngineTransportWitnessStageACompiledV5|RizEngineTransportWitnessEarliestMainV5
              |RizEngineTransportWitnessSuspendedCheckedV5|RizEngineTransportWitnessPreWriteCleanV5
              |RizEngineTransportWitnessExclusivityHeldV5);
        if(info.admission.version!=5||info.admission.byteSize!=sizeof(info.admission)
            ||(info.admission.witnessFlags&requiredFlags)!=requiredFlags
            ||info.admission.earliestThreadId!=info.mainThread||!info.admission.preWriteThreadCount
            ||info.admission.nonMainInteriorRipCount||!info.admission.postCommitThreadCount)return false;
        U8 expectedAuth[32],expectedExcl[32];Zero(expectedAuth,sizeof(expectedAuth));Zero(expectedExcl,sizeof(expectedExcl));
        if(!AC::ComputeAdmissionAuthoritySha256(info,AF::InteriorClosureSha256,AF::ColdTraceSha256,expectedAuth))return false;
        AC::ComputeExclusivitySha256(info,info.admission.witnessFlags,expectedExcl);
        if(!Equal(info.admission.admissionAuthoritySha256,expectedAuth,32)
            ||!Equal(info.admission.exclusivitySha256,expectedExcl,32))return false;
        }
    }
    return !frozen||Equal(&info,&readySnapshot,sizeof(info));
}
struct ProductionExclusivityState {
    volatile LONG active;
    U32 count;
    U32 tids[64];
    HANDLE handles[64];
};
ProductionExclusivityState activeExclusivity{};
bool InModuleRange(U64 address,U32 moduleIndex){
    if(moduleIndex>=5||!modules[moduleIndex]||!headers[moduleIndex])return false;
    const U64 base=Address(modules[moduleIndex]);
    const U64 size=headers[moduleIndex]->OptionalHeader.SizeOfImage;
    return address>=base&&(address-base)<size;
}
bool ReleaseProductionExclusivity(bool requireResumeSuccess){
    bool ok=true;
    for(U32 i=0;i<activeExclusivity.count;++i){
        HANDLE h=activeExclusivity.handles[i];
        if(h){
            if(ResumeThread(h)==static_cast<DWORD>(-1))ok=false;
            CloseHandle(h);
            activeExclusivity.handles[i]=nullptr;
        }
        activeExclusivity.tids[i]=0;
    }
    activeExclusivity.count=0;
    InterlockedExchange(&activeExclusivity.active,0);
    return !requireResumeSuccess||ok;
}
bool AcquireProductionExclusivity(){
    Zero(&activeExclusivity,sizeof(activeExclusivity));
    const U32 selfTid=GetCurrentThreadId(),pid=GetCurrentProcessId();
    FILETIME selfCreated,selfExit,selfKernel,selfUser;
    Zero(&selfCreated,sizeof(selfCreated));Zero(&selfExit,sizeof(selfExit));Zero(&selfKernel,sizeof(selfKernel));Zero(&selfUser,sizeof(selfUser));
    if(!GetThreadTimes(GetCurrentThread(),&selfCreated,&selfExit,&selfKernel,&selfUser))return false;
    HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD,0);
    if(snap==INVALID_HANDLE_VALUE)return false;
    THREADENTRY32 te;Zero(&te,sizeof(te));te.dwSize=sizeof(te);
    U32 totalThreads=0,interiorRipHits=0;
    bool ok=Thread32First(snap,&te)!=FALSE;
    while(ok){
        if(te.dwSize>=offsetof(THREADENTRY32,th32OwnerProcessID)+sizeof(te.th32OwnerProcessID)&&te.th32OwnerProcessID==pid){
            ++totalThreads;
            if(te.th32ThreadID!=selfTid){
                if(activeExclusivity.count>=64){ok=false;break;}
                HANDLE h=OpenThread(THREAD_SUSPEND_RESUME|THREAD_GET_CONTEXT|THREAD_QUERY_INFORMATION,FALSE,te.th32ThreadID);
                if(!h){ok=false;break;}
                FILETIME tc,teTime,tk,tu;Zero(&tc,sizeof(tc));Zero(&teTime,sizeof(teTime));Zero(&tk,sizeof(tk));Zero(&tu,sizeof(tu));
                if(!GetThreadTimes(h,&tc,&teTime,&tk,&tu)||CompareFileTime(&tc,&selfCreated)<0){CloseHandle(h);ok=false;break;}
                if(SuspendThread(h)==static_cast<DWORD>(-1)){CloseHandle(h);ok=false;break;}
                activeExclusivity.handles[activeExclusivity.count]=h;
                activeExclusivity.tids[activeExclusivity.count]=te.th32ThreadID;
                ++activeExclusivity.count;
                CONTEXT ctx;Zero(&ctx,sizeof(ctx));ctx.ContextFlags=CONTEXT_CONTROL;
                if(!GetThreadContext(h,&ctx)){ok=false;break;}
                if(InModuleRange(ctx.Rip,2)||InModuleRange(ctx.Rip,3)){++interiorRipHits;ok=false;break;}
            }
        }
        Zero(&te,sizeof(te));te.dwSize=sizeof(te);
        if(!Thread32Next(snap,&te)){if(GetLastError()!=ERROR_NO_MORE_FILES)ok=false;break;}
    }
    CloseHandle(snap);
    if(!ok||!totalThreads||interiorRipHits){
        info.admission.nonMainInteriorRipCount=interiorRipHits;
        (void)ReleaseProductionExclusivity(false);
        return false;
    }
    InterlockedExchange(&activeExclusivity.active,1);
    info.admission.version=5;info.admission.byteSize=sizeof(info.admission);
    info.admission.earliestThreadId=selfTid;
    info.admission.preWriteThreadCount=totalThreads;
    info.admission.suspendedThreadCount=activeExclusivity.count;
    info.admission.nonMainInteriorRipCount=0;
    info.admission.postCommitThreadCount=totalThreads;
    info.admission.witnessFlags=RizEngineTransportWitnessStageACompiledV5|RizEngineTransportWitnessEarliestMainV5
        |RizEngineTransportWitnessSuspendedCheckedV5|RizEngineTransportWitnessPreWriteCleanV5;
    return true;
}
bool VerifyExclusivityStillHeld(){
    const U32 selfTid=GetCurrentThreadId(),pid=GetCurrentProcessId();
    HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD,0);
    if(snap==INVALID_HANDLE_VALUE)return false;
    THREADENTRY32 te;Zero(&te,sizeof(te));te.dwSize=sizeof(te);
    U32 count=0;bool ok=Thread32First(snap,&te)!=FALSE;
    while(ok){
        if(te.dwSize>=offsetof(THREADENTRY32,th32OwnerProcessID)+sizeof(te.th32OwnerProcessID)&&te.th32OwnerProcessID==pid){
            ++count;
            if(te.th32ThreadID!=selfTid){
                bool found=false;
                for(U32 i=0;i<activeExclusivity.count;++i)if(activeExclusivity.tids[i]==te.th32ThreadID){found=true;break;}
                if(!found){ok=false;break;}
            }
        }
        Zero(&te,sizeof(te));te.dwSize=sizeof(te);
        if(!Thread32Next(snap,&te)){if(GetLastError()!=ERROR_NO_MORE_FILES)ok=false;break;}
    }
    CloseHandle(snap);
    if(!ok||!count)return false;
    info.admission.postCommitThreadCount=count;
    return true;
}
bool Initialize(HINSTANCE self){if(!ValidateModules(self)||!Bodies(0))return Reject(RizEngineTransportBadSourceV5);
    if(!Digest(F::FactsSha256,info.fixedContractSha256))return Reject(RizEngineTransportBadCoverageV5);
    if(!Record(RizEngineTransportBodyValidatedV5))return false;
    // Describing/binding this DLL's private continuations creates no Kernel
    // claim, registered near table or game source write. Keep all thirteen
    // independently verified descriptors visible in a facts-only refusal.
    if(!PrepareOriginals()||!StaticRuntimeFunctions())return Reject(RizEngineTransportBadUnwindV5);
    if constexpr(P::Production){
        namespace AF=RizEngineTransportAdmissionFacts;
        namespace AC=RizEngineTransportAdmissionCrypto;
        if constexpr(!P::ProductionFirstInstallAuthorized||!AF::InteriorEntryClosureVerified||!AF::ProductionColdTraceVerified
            ||!AF::ProductionAdmissionVerified){
            return Reject(RizEngineTransportAdmissionUnprovenV5);
        }else{
        if(!info.startupStatic||info.attachReason!=DLL_PROCESS_ATTACH
            ||info.ownerThread!=info.producerThread||info.ownerThread!=GetCurrentThreadId()){
            return Reject(RizEngineTransportAdmissionUnprovenV5);
        }
        info.mainThread=GetCurrentThreadId();
        info.mainThreadProof=RizEngineTransportMainThreadFixedProductionContractV5;
        for(U32 i=0;i<5;++i)if(i!=C::Executable&&!Pin(i))return Reject(RizEngineTransportBadDependencyV5,RizEngineTransportModuleObjectV5,i,GetLastError());
        if(!AcquireProductionExclusivity())return Reject(RizEngineTransportAdmissionUnprovenV5,RizEngineTransportPublicationObjectV5,L::NoIndex,GetLastError());
        if(!AC::ComputeAdmissionAuthoritySha256(info,AF::InteriorClosureSha256,AF::ColdTraceSha256,info.admission.admissionAuthoritySha256)){
            (void)ReleaseProductionExclusivity(false);
            return Reject(RizEngineTransportAdmissionUnprovenV5);
        }
        info.epoch=1;
        if(!PrepareRelays()||!RuntimeFunctions()||!PreparedWindows()){(void)ReleaseProductionExclusivity(false);return Reject(RizEngineTransportBadCoverageV5);}
        if(!ConfigureFamilies()||!CommitWindows()){(void)ReleaseProductionExclusivity(false);return false;}
        if(!VerifyExclusivityStillHeld()){(void)ReleaseProductionExclusivity(false);return Reject(RizEngineTransportAdmissionUnprovenV5,RizEngineTransportPublicationObjectV5,L::NoIndex,GetLastError());}
        info.admission.witnessFlags|=RizEngineTransportWitnessExclusivityHeldV5;
        AC::ComputeExclusivitySha256(info,info.admission.witnessFlags,info.admission.exclusivitySha256);
        info.state=RizEngineTransportReadyV5;info.lastWin32Error=0;info.failureFamilyIndex=info.failureWindowIndex=info.failureResourceIndex=L::NoIndex;
        if(!VerifyCurrent(false)){(void)ReleaseProductionExclusivity(false);return Reject(RizEngineTransportBadReadbackV5);}
        if(!FlushInstructionCache(GetCurrentProcess(),modules[2],headers[2]->OptionalHeader.SizeOfImage)){
            (void)ReleaseProductionExclusivity(false);return Reject(RizEngineTransportFlushFailedV5,RizEngineTransportPublicationObjectV5,L::NoIndex,GetLastError());
        }
        if(!ReleaseProductionExclusivity(true))return Reject(RizEngineTransportAdmissionUnprovenV5,RizEngineTransportPublicationObjectV5,L::NoIndex,GetLastError());
        info.admission.witnessFlags|=RizEngineTransportWitnessPostCommitResumedV5;
        AC::ComputeExclusivitySha256(info,info.admission.witnessFlags,info.admission.exclusivitySha256);
        if(!Record(RizEngineTransportPublishedV5)||!VerifyCurrent(false))return Reject(RizEngineTransportBadReadbackV5);
        Copy(&readySnapshot,&info,sizeof(info));readyFrozen=true;return true;
        }
    }else{
        if constexpr(P::Owned){if(!OwnedMarker())return Reject(RizEngineTransportAdmissionUnprovenV5);
            info.mainThread=GetCurrentThreadId();info.mainThreadProof=RizEngineTransportMainThreadOwnedContractV5;}
        else return Reject(RizEngineTransportAdmissionUnprovenV5);
        for(U32 i=0;i<5;++i)if(i!=C::Executable&&!Pin(i))return Reject(RizEngineTransportBadDependencyV5,RizEngineTransportModuleObjectV5,i,GetLastError());
        info.epoch=1;if(!PrepareRelays()||!RuntimeFunctions()||!PreparedWindows())return Reject(RizEngineTransportBadCoverageV5);
        if(!ConfigureFamilies()||!CommitWindows())return false;
        info.state=RizEngineTransportReadyV5;info.lastWin32Error=0;info.failureFamilyIndex=info.failureWindowIndex=info.failureResourceIndex=L::NoIndex;
        if(!Record(RizEngineTransportPublishedV5)||!VerifyCurrent(false))return Reject(RizEngineTransportBadReadbackV5);
        Copy(&readySnapshot,&info,sizeof(info));readyFrozen=true;return true;
    }
}
bool EnterGate(){return InterlockedCompareExchange(&gate,1,0)==0;}
void LeaveGate(){InterlockedExchange(&gate,0);}
void NativeFailure(){
    // No current source/near/file access belongs in an exception handler: the
    // exception may be the evidence that one of those regions is inaccessible.
    // Retain progress from our own fixed BSS receipts without rereading code.
    (void)ReleaseProductionExclusivity(false);
    InterlockedExchange(&stickyUnknown,1);
    if(familyReceipt.version==K::Version&&familyReceipt.byteSize==sizeof(familyReceipt)){
        info.configuredFamilies|=familyReceipt.configuredFamilies;info.boundFamilies|=familyReceipt.verifiedFamilies;
        info.retainedFamilies|=familyReceipt.retainedFamilies;Copy(info.families,familyReceipt.families,sizeof(info.families));
    }
    if(writeReceipt.version==W::Version&&writeReceipt.byteSize==sizeof(writeReceipt)){
        if(writeReceipt.sourceWriteAttempts>info.sourceWriteAttempts)info.sourceWriteAttempts=writeReceipt.sourceWriteAttempts;
        if(writeReceipt.sourceWriteCompletions>info.sourceWriteCompletions)info.sourceWriteCompletions=writeReceipt.sourceWriteCompletions;
        if(writeReceipt.protectionOutstanding>info.protectionOutstanding)info.protectionOutstanding=writeReceipt.protectionOutstanding;
        info.writeAttemptedWindows|=writeReceipt.attemptedWindows;info.committedWindows|=writeReceipt.committedWindows;
        info.outstandingProtectionWindows|=writeReceipt.outstandingWindows;
        constexpr std::size_t first=offsetof(RizEngineTransportWindowV5,originalProtection);
        constexpr std::size_t bytes=offsetof(RizEngineTransportWindowV5,readbackBytes)+sizeof(U32)-first;
        for(U32 i=0;i<36;++i)if(writeReceipt.windows[i].version==5){
            Copy(reinterpret_cast<U8*>(&info.windows[i])+first,reinterpret_cast<const U8*>(&writeReceipt.windows[i])+first,bytes);
            info.windows[i].flags|=writeReceipt.windows[i].flags;
        }
    }
    (void)Reject(RizEngineTransportNativeExceptionV5);
}
bool OutputStorage(const void* output){const U64 address=Address(output);if(!Memory(address,sizeof(info),0,0,0,true))return false;
    if(modules[0]&&headers[0]&&Overlap(address,sizeof(info),Address(modules[0]),headers[0]->OptionalHeader.SizeOfImage))return false;
    return !Overlap(address,sizeof(info),Address(&info),sizeof(info))&&!Overlap(address,sizeof(info),Address(&readySnapshot),sizeof(readySnapshot))
        &&!Overlap(address,sizeof(info),Address(&relayPlan),sizeof(relayPlan))&&!Overlap(address,sizeof(info),Address(&familyRequest),sizeof(familyRequest))
        &&!Overlap(address,sizeof(info),Address(&familyReceipt),sizeof(familyReceipt))&&!Overlap(address,sizeof(info),Address(&writeReceipt),sizeof(writeReceipt));
}
} // anonymous namespace

#ifdef RIZ_ENGINE_TRANSPORT_OWNED_IMAGE
extern "C" bool __cdecl RizEngineTransportValidateOwnedImage(std::uint64_t target){
    // Called directly by owned-image helper code while this actual publisher
    // holds its gate. It does not acquire that gate, write a record, configure a
    // family or grant a permission. No caller DTO can create the module proof.
    if constexpr(!P::Owned)return false;
    if(!modulesValidated||target!=Address(modules[2])||InterlockedCompareExchange(&gate,0,0)!=1
        ||InterlockedCompareExchange(&stickyUnknown,0,0)||!headers[2])return false;
    return Memory(target,headers[2]->OptionalHeader.SizeOfHeaders,target,MEM_IMAGE)&&SameFile(2)&&OwnedMarker();
}
#endif
#ifdef RIZ_ENGINE_TRANSPORT_PRODUCTION
extern "C" bool __cdecl RizEngineTransportValidateProductionAdmission(std::uint64_t target){
    if constexpr(!P::Production||P::Owned||!P::ProductionFirstInstallAuthorized)return false;
    if(!modulesValidated||target!=Address(modules[2])
        ||InterlockedCompareExchange(&gate,0,0)!=1||InterlockedCompareExchange(&stickyUnknown,0,0)
        ||InterlockedCompareExchange(&activeExclusivity.active,0,0)!=1||!headers[2])return false;
    if(info.claimed!=1||info.attachReason!=DLL_PROCESS_ATTACH||!info.startupStatic
        ||!info.mainThread||info.mainThread!=info.producerThread||info.mainThread!=GetCurrentThreadId()
        ||info.mainThreadProof!=RizEngineTransportMainThreadFixedProductionContractV5)return false;
    const U32 required=RizEngineTransportWitnessStageACompiledV5|RizEngineTransportWitnessEarliestMainV5
        |RizEngineTransportWitnessSuspendedCheckedV5|RizEngineTransportWitnessPreWriteCleanV5;
    if((info.admission.witnessFlags&required)!=required||info.admission.nonMainInteriorRipCount)return false;
    U8 expectedFacts[32];Zero(expectedFacts,sizeof(expectedFacts));
    return Digest(F::FactsSha256,expectedFacts)&&Equal(info.fixedContractSha256,expectedFacts,32)
        &&Memory(target,headers[2]->OptionalHeader.SizeOfHeaders,target,MEM_IMAGE)&&SameFile(2);
}
#endif

extern "C" BOOL WINAPI RizEngineTransportBootstrapEntry(HINSTANCE module,DWORD reason,LPVOID reserved){
    const DWORD incoming=GetLastError();
    if(reason!=DLL_PROCESS_ATTACH){if(reason==DLL_PROCESS_DETACH&&!reserved&&HasResponsibility())Fatal();SetLastError(incoming);return TRUE;}
    if(InterlockedCompareExchange(&claimed,1,0)!=0){InterlockedExchange(&stickyUnknown,1);(void)Reject(RizEngineTransportRepeatedAttachV5);SetLastError(incoming);if(HasWrites())Fatal();return FALSE;}
    if(!EnterGate()){InterlockedExchange(&stickyUnknown,1);SetLastError(incoming);return FALSE;}
    info.claimed=1;info.ownerThread=info.producerThread=GetCurrentThreadId();info.processId=GetCurrentProcessId();info.attachReason=reason;info.startupStatic=reserved?1u:0u;
    info.profile=P::Profile;info.state=RizEngineTransportValidatingV5;info.moduleCount=5;info.typedCount=13;info.siteCount=23;info.windowCount=36;
    info.familyCount=12;info.eventCapacity=1024;info.failureFamilyIndex=info.failureWindowIndex=info.failureResourceIndex=L::NoIndex;
    (void)Record(RizEngineTransportEnteredV5);
    __try{if(!reserved)(void)Reject(RizEngineTransportDynamicLoadV5);else(void)Initialize(module);}
    __except(EXCEPTION_EXECUTE_HANDLER){NativeFailure();}
    if(InterlockedCompareExchange(&stickyUnknown,0,0))info.state=RizEngineTransportUnknownV5;
    const bool fatal=info.state!=RizEngineTransportReadyV5&&HasWrites();LeaveGate();SetLastError(incoming);if(fatal)Fatal();return TRUE;
}
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_GetVersion(){return 5;}
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_GetInfoV5(RizEngineTransportPublicationV5* output){
    const DWORD incoming=GetLastError();if(!EnterGate()){SetLastError(incoming);return 2;}U32 result=1;
    __try{if(OutputStorage(output)&&output->version==5&&output->byteSize==sizeof(*output)){Copy(output,&info,sizeof(info));result=0;}}
    __except(EXCEPTION_EXECUTE_HANDLER){NativeFailure();}
    LeaveGate();SetLastError(incoming);return result;
}
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_VerifyV5(){
    const DWORD incoming=GetLastError();if(!EnterGate()){SetLastError(incoming);return 2;}U32 result=1;
    __try{if(VerifyCurrent(true))result=0;else{++info.verifyFailures;InterlockedExchange(&stickyUnknown,1);
        (void)Reject(RizEngineTransportInvalidSnapshotV5);(void)Record(RizEngineTransportVerifyFailedV5);}}
    __except(EXCEPTION_EXECUTE_HANDLER){++info.verifyFailures;NativeFailure();}
    LeaveGate();SetLastError(incoming);return result;
}
RIZ_ENGINE_TRANSPORT_PUBLICATION_API void __cdecl RizEngineTransport_AbortAfterPublication(){
    const DWORD incoming=GetLastError();if(!EnterGate()){if(HasResponsibility())InterlockedExchange(&stickyUnknown,1);SetLastError(incoming);if(HasWrites())Fatal();return;}
    __try{if(HasResponsibility()||InterlockedCompareExchange(&stickyUnknown,0,0))InterlockedExchange(&stickyUnknown,1);
        (void)Reject(RizEngineTransportBadDependencyV5);(void)Record(RizEngineTransportAbortedV5);}
    __except(EXCEPTION_EXECUTE_HANDLER){NativeFailure();}
    const bool fatal=HasWrites();LeaveGate();SetLastError(incoming);if(fatal)Fatal();
}
