#define RIZ_ENGINE_BOOTSTRAP_BUILD
#include "EnginePublication.h"
#include "EngineBootstrapProfile.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include <intrin.h>

// Aggregate zeroing may be emitted as this compiler helper under /O2 even
// though the publisher does not call a CRT. Volatile stores prevent loop
// recognition from rewriting this implementation into a call to itself.
#pragma function(memset)
extern "C" void* __cdecl memset(void* destination,int value,std::size_t count)
{
 auto* bytes=static_cast<volatile unsigned char*>(destination);
 for(std::size_t index=0;index!=count;++index)bytes[index]=static_cast<unsigned char>(value);
 return destination;
}

// All eight Kernel exports are ordinary imports, including the observer API
// which this native publisher must never invoke. These force references retain
// the import thunk without a dynamically initialized imported-function table.
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_GetAbiVersion")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_GetLimit")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_GetFrameSize")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_GetCompletionSize")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_GetEntry")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_Configure")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_Attach")
#pragma comment(linker, "/INCLUDE:RizEngineNodeContext_GetInfo")
#pragma comment(linker, "/INCLUDE:RizEnginePublicationRecord")

extern "C" {
// A named public-link symbol retained in the linker MAP, deliberately without
// dllexport. A parent-owned debugger can preserve the actual terminal 4096
// bytes at fail-fast without calling an API or modifying the child.
RizEnginePublicationV4 RizEnginePublicationRecord{4,sizeof(RizEnginePublicationV4)};
void* RizEngineContinuation[4]{};
void RizEngineOriginalStart();
void RizEngineOriginalExecute();
void RizEngineOriginalRelease();
void RizEngineOriginalDetach();
BOOL WINAPI RizEngineBootstrapEntry(HINSTANCE, DWORD, LPVOID);
}

namespace {
namespace Profile = RizEngineBootstrapProfile;
namespace Production = RizEngineBootstrapProductionContract;
RizEnginePublicationV4& information=RizEnginePublicationRecord;
volatile LONG gate{}, claimed{}, stickyUnknown{};
wchar_t paths[Production::ModuleCount][1024]{};
wchar_t checkPath[1024]{};
HMODULE modules[Production::ModuleCount]{};
bool modulePinned[Production::ModuleCount]{};
const IMAGE_NT_HEADERS64* headers[Production::ModuleCount]{};
// The fifth identity is retained privately, including its deny-write file
// handle. The V4 four-module array and every reserved byte remain unchanged.
RizEngineModuleV4 doorstopIdentity{};
RizEngineModuleV4& ModuleFacts(unsigned index)
{return index==Production::Doorstop?doorstopIdentity:information.modules[index];}
void* const originals[4]{reinterpret_cast<void*>(&RizEngineOriginalStart),
    reinterpret_cast<void*>(&RizEngineOriginalExecute),reinterpret_cast<void*>(&RizEngineOriginalRelease),
    reinterpret_cast<void*>(&RizEngineOriginalDetach)};
const char* const kernelNames[8]{"RizEngineNodeContext_GetAbiVersion","RizEngineNodeContext_GetLimit",
    "RizEngineNodeContext_GetFrameSize","RizEngineNodeContext_GetCompletionSize","RizEngineNodeContext_GetEntry",
    "RizEngineNodeContext_Configure","RizEngineNodeContext_Attach","RizEngineNodeContext_GetInfo"};
const char* const winHttpNames[4]{"WinHttpOpen","WinHttpGetProxyForUrl",
    "WinHttpGetIEProxyConfigForCurrentUser","WinHttpCloseHandle"};
const char* const bootstrapNames[4]{"RizEngineFirstInstall_GetVersion","RizEngineFirstInstall_GetInfoV4",
    "RizEngineFirstInstall_VerifyV4","RizEngineFirstInstall_AbortAfterPublication"};
enum Stage : unsigned { Entered=1,ModuleValidated,TargetValidated,ContinuationPrepared,
    ConfigureBegin,ConfigureEnd,EntryBound,ProtectBegin,ProtectEnd,WriteBegin,WriteEnd,
    Readback,Restored,Flushed,Committed,Ready,Failure,Reentry };

void Copy(void* destination,const void* source,std::size_t count)
{auto* out=static_cast<volatile unsigned char*>(destination);const auto* in=static_cast<const volatile unsigned char*>(source);
 for(std::size_t index=0;index!=count;++index)out[index]=in[index];}
bool Equal(const void* left,const void* right,std::size_t count)
{const auto* a=static_cast<const volatile unsigned char*>(left);const auto* b=static_cast<const volatile unsigned char*>(right);
 unsigned diff=0;for(std::size_t index=0;index!=count;++index)diff|=a[index]^b[index];return diff==0;}
unsigned Lower(unsigned value){return value>='A'&&value<='Z'?value+('a'-'A'):value;}
bool SameWide(const wchar_t* left,const wchar_t* right,unsigned bound=1024)
{for(unsigned index=0;index!=bound;++index){if(Lower(left[index])!=Lower(right[index]))return false;if(!left[index])return true;}return false;}
bool Memory(const void* pointer,std::size_t bytes,HMODULE module,bool executable,bool writable,DWORD* protection=nullptr)
{
 const auto start=reinterpret_cast<std::uintptr_t>(pointer);if(!start||!bytes||start>UINTPTR_MAX-bytes)return false;
 auto at=start;const auto end=start+bytes;
 while(at<end){MEMORY_BASIC_INFORMATION memory{};
  if(!VirtualQuery(reinterpret_cast<void*>(at),&memory,sizeof(memory))||memory.State!=MEM_COMMIT
    ||(memory.Protect&(PAGE_GUARD|PAGE_NOACCESS))||(module&&(memory.AllocationBase!=module||memory.Type!=MEM_IMAGE)))return false;
  const auto p=memory.Protect&0xffu;
  if(executable&&p!=PAGE_EXECUTE_READ)return false;
  if(writable&&p!=PAGE_READWRITE&&p!=PAGE_WRITECOPY&&p!=PAGE_EXECUTE_READWRITE&&p!=PAGE_EXECUTE_WRITECOPY)return false;
  if(at==start&&protection)*protection=memory.Protect;
  const auto next=reinterpret_cast<std::uintptr_t>(memory.BaseAddress)+memory.RegionSize;if(next<=at)return false;
  at=next<end?next:end;
 }return true;
}
bool InImage(unsigned module,unsigned rva,std::size_t bytes)
{return headers[module]&&rva&&rva<headers[module]->OptionalHeader.SizeOfImage
  &&bytes<=headers[module]->OptionalHeader.SizeOfImage-rva
  &&Memory(reinterpret_cast<const unsigned char*>(modules[module])+rva,bytes,modules[module],false,false);}
const unsigned char* At(unsigned module,unsigned rva)
{return reinterpret_cast<const unsigned char*>(modules[module])+rva;}
bool ImageString(unsigned module,unsigned rva,const char* expected)
{for(unsigned index=0;index!=512;++index){if(!InImage(module,rva+index,1))return false;
 const unsigned value=At(module,rva)[index];if(Lower(value)!=Lower(static_cast<unsigned char>(expected[index])))return false;
 if(!value)return true;}return false;}
bool ImageStringExact(unsigned module,unsigned rva,const char* expected)
{for(unsigned index=0;index!=512;++index){if(!InImage(module,rva+index,1))return false;
 const unsigned value=At(module,rva)[index];if(value!=static_cast<unsigned char>(expected[index]))return false;
 if(!value)return true;}return false;}
bool HasDirectory(unsigned module,unsigned index)
{const auto& optional=headers[module]->OptionalHeader;return optional.NumberOfRvaAndSizes>index
 &&optional.DataDirectory[index].VirtualAddress&&optional.DataDirectory[index].Size;}
bool Record(unsigned stage,unsigned kind=4,unsigned error=0)
{
 if(information.eventCount==64){information.overflow=1;InterlockedExchange(&stickyUnknown,1);
  information.state=RizEnginePublicationUnknown;if(!information.failure)information.failure=RizEngineEventOverflow;return false;}
 auto& event=information.events[information.eventCount++];event.sequence=information.eventCount;
 event.stage=stage;event.kind=kind;event.state=information.state;event.failure=information.failure;event.win32Error=error;return true;
}
bool Responsibility(){return information.sourceWriteAttempts||information.protectionOutstanding;}
bool RetainedClaims(){return information.configuredMask||information.boundMask;}
bool Reject(unsigned failure,unsigned kind=4,unsigned error=0)
{
 if(!information.failure)information.failure=failure;
 if(Responsibility()||InterlockedCompareExchange(&stickyUnknown,0,0)){
  InterlockedExchange(&stickyUnknown,1);information.state=RizEnginePublicationUnknown;
 }else information.state=RizEngineDisabledNoSourceWrite;
 Record(Failure,kind,error);return false;
}
[[noreturn]] void Fatal()
{RaiseFailFastException(nullptr,nullptr,0);TerminateProcess(GetCurrentProcess(),0xe052495b);for(;;)__fastfail(7);}
bool Fault(unsigned stage,unsigned kind=0){return Profile::FaultStage==stage&&Profile::FaultIndex==kind;}
unsigned Rotate(unsigned value,unsigned count){return(value>>count)|(value<<(32-count));}
struct Sha256 {
 unsigned state[8];unsigned char pending[64];std::uint64_t bytes;unsigned used;
 void Initialize(){constexpr unsigned initial[8]{0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
  for(unsigned index=0;index!=8;++index)state[index]=initial[index];bytes=0;used=0;}
 void Block(){
  constexpr unsigned constants[64]{0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
   0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
   0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
   0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
   0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
   0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
   0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
   0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
  unsigned words[64];for(unsigned index=0;index!=16;++index)words[index]=(unsigned(pending[index*4])<<24)
   |(unsigned(pending[index*4+1])<<16)|(unsigned(pending[index*4+2])<<8)|pending[index*4+3];
  for(unsigned index=16;index!=64;++index)words[index]=words[index-16]+(Rotate(words[index-15],7)^Rotate(words[index-15],18)^(words[index-15]>>3))
   +words[index-7]+(Rotate(words[index-2],17)^Rotate(words[index-2],19)^(words[index-2]>>10));
  auto a=state[0],b=state[1],c=state[2],d=state[3],e=state[4],f=state[5],g=state[6],h=state[7];
  for(unsigned index=0;index!=64;++index){const auto first=h+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+constants[index]+words[index];
   const auto second=(Rotate(a,2)^Rotate(a,13)^Rotate(a,22))+((a&b)^(a&c)^(b&c));h=g;g=f;f=e;e=d+first;d=c;c=b;b=a;a=first+second;}
  state[0]+=a;state[1]+=b;state[2]+=c;state[3]+=d;state[4]+=e;state[5]+=f;state[6]+=g;state[7]+=h;
 }
 void Feed(const unsigned char* data,unsigned count){bytes+=count;for(unsigned index=0;index!=count;++index){pending[used++]=data[index];if(used==64){Block();used=0;}}}
 void Finish(unsigned char* digest){const auto bits=bytes*8;const unsigned char one=0x80,zero=0;Feed(&one,1);while(used!=56)Feed(&zero,1);
  unsigned char suffix[8];for(unsigned index=0;index!=8;++index)suffix[index]=static_cast<unsigned char>(bits>>(56-index*8));Feed(suffix,8);
  for(unsigned index=0;index!=8;++index)for(unsigned part=0;part!=4;++part)digest[index*4+part]=static_cast<unsigned char>(state[index]>>(24-part*8));}
};
bool HashFile(HANDLE file,std::uint64_t expectedBytes,unsigned char* digest)
{
 LARGE_INTEGER begin{},size{};if(!GetFileSizeEx(file,&size)||size.QuadPart<=0||static_cast<std::uint64_t>(size.QuadPart)!=expectedBytes
  ||expectedBytes>256ull*1024ull*1024ull||!SetFilePointerEx(file,begin,nullptr,FILE_BEGIN))return false;
 Sha256 hash;hash.Initialize();unsigned char buffer[256];std::uint64_t total=0;
 for(;;){DWORD count=0;if(!ReadFile(file,buffer,sizeof(buffer),&count,nullptr))return false;if(!count)break;
  total+=count;if(total>expectedBytes)return false;hash.Feed(buffer,count);}
 hash.Finish(digest);return total==expectedBytes;
}
bool DiskHeaderMatches(HANDLE file,HMODULE actualModule,const IMAGE_NT_HEADERS64* mapped)
{
 LARGE_INTEGER position{};DWORD count=0;IMAGE_DOS_HEADER dos{};IMAGE_NT_HEADERS64 nt{};
 if(!SetFilePointerEx(file,position,nullptr,FILE_BEGIN)||!ReadFile(file,&dos,sizeof(dos),&count,nullptr)||count!=sizeof(dos)
  ||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<0||dos.e_lfanew>0x100000)return false;
 position.QuadPart=dos.e_lfanew;
 if(!SetFilePointerEx(file,position,nullptr,FILE_BEGIN)||!ReadFile(file,&nt,sizeof(nt),&count,nullptr)
   ||count!=sizeof(nt))return false;
 // The observed loader changes only ImageBase in the mapped NT header.
 // Accept its exact disk-preferred value or this actual HMODULE, never an
 // arbitrary masked field. Compare every other NT header byte unchanged.
 const auto mappedBase=mapped->OptionalHeader.ImageBase;
 if(mappedBase!=nt.OptionalHeader.ImageBase&&mappedBase!=reinterpret_cast<std::uint64_t>(actualModule))return false;
 nt.OptionalHeader.ImageBase=mappedBase;
 return Equal(&nt,mapped,sizeof(nt));
}
const IMAGE_NT_HEADERS64* ImageHeader(HMODULE module)
{
 if(!Memory(module,sizeof(IMAGE_DOS_HEADER),module,false,false))return nullptr;
 const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
 if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x100000)return nullptr;
 const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(reinterpret_cast<const unsigned char*>(module)+dos->e_lfanew);
 if(!Memory(nt,sizeof(*nt),module,false,false)||nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64
  ||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64)
  ||nt->OptionalHeader.SizeOfImage<0x1000||nt->OptionalHeader.SizeOfImage>0x20000000
  ||nt->OptionalHeader.NumberOfRvaAndSizes!=IMAGE_NUMBEROF_DIRECTORY_ENTRIES)return nullptr;
 return nt;
}
bool ExpectedPath(unsigned module,const wchar_t* name)
{
 const auto length=GetModuleFileNameW(modules[module],paths[module],1024);if(!length||length>=1024)return false;
 unsigned suffix=length;while(suffix&&paths[module][suffix-1]!=L'\\')--suffix;
 if(!suffix||!SameWide(paths[module]+suffix,name))return false;
 if(module!=3){unsigned executablePrefix=0;for(unsigned index=0;paths[3][index];++index)if(paths[3][index]==L'\\')executablePrefix=index+1;
  if(executablePrefix!=suffix)return false;
  for(unsigned index=0;index!=suffix;++index)if(Lower(paths[module][index])!=Lower(paths[3][index]))return false;
 }return true;
}
bool ValidateModule(unsigned index,const Profile::ImageIdentity* expected,const wchar_t* name)
{
 const auto* nt=ImageHeader(modules[index]);if(!nt||!ExpectedPath(index,name))return Reject(RizEngineBadImage,index);
 if(((nt->FileHeader.Characteristics&IMAGE_FILE_DLL)!=0)!=(index!=3))return Reject(RizEngineBadImage,index);
 headers[index]=nt;auto& row=ModuleFacts(index);row.kind=index;row.machine=nt->FileHeader.Machine;
 row.module=reinterpret_cast<std::uint64_t>(modules[index]);row.imageSize=nt->OptionalHeader.SizeOfImage;
 row.timeDateStamp=nt->FileHeader.TimeDateStamp;row.entryRva=nt->OptionalHeader.AddressOfEntryPoint;
 row.exceptionRva=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].VirtualAddress;row.flags=4;
 if(expected&&(expected->machine!=row.machine||expected->imageSize!=row.imageSize||expected->timeDateStamp!=row.timeDateStamp
  ||expected->entryRva!=row.entryRva||expected->exceptionRva!=row.exceptionRva
  ||expected->exceptionBytes!=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION].Size))return Reject(RizEngineBadImage,index);
 if((index==0||index==1)&&(HasDirectory(index,IMAGE_DIRECTORY_ENTRY_TLS)||HasDirectory(index,IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT)
  ||(index==1&&row.entryRva)))return Reject(RizEngineBadDependency,index);
 const HANDLE file=CreateFileW(paths[index],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(file==INVALID_HANDLE_VALUE)return Reject(RizEngineBadHash,index,GetLastError());
 row.heldFile=reinterpret_cast<std::uint64_t>(file);row.flags|=1;
 BY_HANDLE_FILE_INFORMATION identity{};LARGE_INTEGER size{};
 if(!GetFileInformationByHandle(file,&identity)||!GetFileSizeEx(file,&size)||size.QuadPart<=0)return Reject(RizEngineBadHash,index,GetLastError());
 row.fileBytes=static_cast<std::uint64_t>(size.QuadPart);row.fileIndex=(std::uint64_t(identity.nFileIndexHigh)<<32)|identity.nFileIndexLow;
 row.volumeSerial=identity.dwVolumeSerialNumber;
 if((expected&&row.fileBytes!=expected->fileBytes)||!DiskHeaderMatches(file,modules[index],nt)||!HashFile(file,row.fileBytes,row.sha256)
  ||(expected&&!Equal(row.sha256,expected->sha256,32)))return Reject(RizEngineBadHash,index);
 row.flags|=2;if(expected)row.flags|=8;return Record(ModuleValidated,index);
}
bool SameFile(unsigned index)
{
 const auto& row=ModuleFacts(index);BY_HANDLE_FILE_INFORMATION held{},current{};
 const auto length=GetModuleFileNameW(modules[index],checkPath,1024);if(!length||length>=1024||!SameWide(checkPath,paths[index]))return false;
 const HANDLE file=reinterpret_cast<HANDLE>(row.heldFile);
 if(!GetFileInformationByHandle(file,&held)||held.dwVolumeSerialNumber!=row.volumeSerial
  ||((std::uint64_t(held.nFileIndexHigh)<<32)|held.nFileIndexLow)!=row.fileIndex
  ||((std::uint64_t(held.nFileSizeHigh)<<32)|held.nFileSizeLow)!=row.fileBytes)return false;
 const HANDLE reopened=CreateFileW(checkPath,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(reopened==INVALID_HANDLE_VALUE)return false;
 const bool same=GetFileInformationByHandle(reopened,&current)&&held.dwVolumeSerialNumber==current.dwVolumeSerialNumber
  &&held.nFileIndexHigh==current.nFileIndexHigh&&held.nFileIndexLow==current.nFileIndexLow
  &&held.nFileSizeHigh==current.nFileSizeHigh&&held.nFileSizeLow==current.nFileSizeLow;
 CloseHandle(reopened);if(!same)return false;
 unsigned char digest[32];return HashFile(file,row.fileBytes,digest)&&Equal(digest,row.sha256,32);
}
bool ImportEdge(unsigned source,unsigned destination,bool exactKernel)
{
 if(!HasDirectory(source,IMAGE_DIRECTORY_ENTRY_IMPORT))return false;
 const auto directory=headers[source]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
 if(!InImage(source,directory.VirtualAddress,directory.Size))return false;
 const char* name=destination==0?"RizToolbox.EngineBootstrap.dll":destination==1?"RizToolbox.EngineNodeKernel.dll":nullptr;
 unsigned found=0;bool terminated=false;
 for(unsigned offset=0;offset+sizeof(IMAGE_IMPORT_DESCRIPTOR)<=directory.Size;offset+=sizeof(IMAGE_IMPORT_DESCRIPTOR)){
  const auto* descriptor=reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(At(source,directory.VirtualAddress+offset));
  if(!descriptor->Name){terminated=true;break;}
  bool selected=false;
  if(destination==2){const wchar_t* target=Profile::Target.module;unsigned position=0;
   for(;position!=256;++position){if(!InImage(source,descriptor->Name+position,1))return false;
    const auto value=At(source,descriptor->Name)[position];if(value>127||Lower(value)!=Lower(target[position]))break;if(!value){selected=true;break;}}
  }else selected=ImageString(source,descriptor->Name,name);
  if(source==0&&!ImageString(source,descriptor->Name,"KERNEL32.dll")&&!ImageString(source,descriptor->Name,"ntdll.dll")
    &&!ImageString(source,descriptor->Name,"RizToolbox.EngineNodeKernel.dll"))return false;
  if(!selected)continue;if(++found!=1||!descriptor->OriginalFirstThunk||!descriptor->FirstThunk)return false;
  unsigned symbols=0,mask=0;bool thunkEnd=false;
  for(unsigned slot=0;slot!=1024;++slot){const unsigned ilt=descriptor->OriginalFirstThunk+slot*8,iat=descriptor->FirstThunk+slot*8;
   if(!InImage(source,ilt,8)||!InImage(source,iat,8))return false;
   const auto value=*reinterpret_cast<const std::uint64_t*>(At(source,ilt));if(!value){thunkEnd=true;break;}
   if(IMAGE_SNAP_BY_ORDINAL64(value)||value>0xffffffffu||!InImage(source,static_cast<unsigned>(value),3))return false;
   const auto symbolRva=static_cast<unsigned>(value)+2;bool end=false;
   for(unsigned at=0;at!=512;++at){if(!InImage(source,symbolRva+at,1))return false;if(!At(source,symbolRva)[at]){end=true;break;}}
   if(!end)return false;
   const auto target=GetProcAddress(modules[destination],reinterpret_cast<const char*>(At(source,symbolRva)));
   if(!target||reinterpret_cast<std::uint64_t>(target)!=*reinterpret_cast<const std::uint64_t*>(At(source,iat))
    ||!Memory(reinterpret_cast<const void*>(target),1,modules[destination],true,false))return false;
   if(exactKernel){bool known=false;for(unsigned kind=0;kind!=8;++kind)if(ImageString(source,symbolRva,kernelNames[kind])){
      if(mask&(1u<<kind))return false;mask|=1u<<kind;known=true;break;}if(!known)return false;}
   ++symbols;
  }
  if(!thunkEnd||!symbols||(exactKernel&&(symbols!=8||mask!=255)))return false;
 }return terminated&&found==1;
}
bool SameText(const char* actual,const char* expected)
{if(!actual||!expected)return false;for(unsigned index=0;index!=512;++index){
 if(actual[index]!=expected[index])return false;if(!actual[index])return true;}return false;}
const char* ProductionModuleName(unsigned index)
{switch(index){case Production::Bootstrap:return "RizToolbox.EngineBootstrap.dll";
 case Production::Kernel:return "RizToolbox.EngineNodeKernel.dll";
 case Production::UnityPlayer:return "UnityPlayer.dll";
 case Production::Doorstop:return "winhttp.dll";default:return nullptr;}}
bool ExportRva(unsigned module,const char* symbol,unsigned* result)
{
 // Resolve the actual named EAT entry directly. A forwarded export cannot
 // stand in for an ordinary edge into the fixed destination image.
 if(!HasDirectory(module,IMAGE_DIRECTORY_ENTRY_EXPORT))return false;
 const auto directory=headers[module]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];
 if(directory.Size<sizeof(IMAGE_EXPORT_DIRECTORY)||!InImage(module,directory.VirtualAddress,directory.Size))return false;
 const auto* exports=reinterpret_cast<const IMAGE_EXPORT_DIRECTORY*>(At(module,directory.VirtualAddress));
 if(!exports->NumberOfFunctions||exports->NumberOfFunctions>65536||!exports->NumberOfNames||exports->NumberOfNames>65536
  ||!InImage(module,exports->AddressOfFunctions,std::size_t(exports->NumberOfFunctions)*4)
  ||!InImage(module,exports->AddressOfNames,std::size_t(exports->NumberOfNames)*4)
  ||!InImage(module,exports->AddressOfNameOrdinals,std::size_t(exports->NumberOfNames)*2))return false;
 const auto* names=reinterpret_cast<const std::uint32_t*>(At(module,exports->AddressOfNames));
 const auto* ordinals=reinterpret_cast<const std::uint16_t*>(At(module,exports->AddressOfNameOrdinals));
 const auto* functions=reinterpret_cast<const std::uint32_t*>(At(module,exports->AddressOfFunctions));
 unsigned found=0,rva=0;
 for(unsigned index=0;index!=exports->NumberOfNames;++index)if(ImageStringExact(module,names[index],symbol)){
  if(++found!=1||ordinals[index]>=exports->NumberOfFunctions)return false;rva=functions[ordinals[index]];}
 if(found!=1||!InImage(module,rva,1)||(rva>=directory.VirtualAddress&&rva-directory.VirtualAddress<directory.Size)
  ||!Memory(At(module,rva),1,modules[module],true,false))return false;
 *result=rva;return true;
}
const char* ProductionSymbol(unsigned edge)
{if(!edge)return "UnityMain2";if(edge<5)return winHttpNames[edge-1];
 if(edge<9)return bootstrapNames[edge-5];if(edge<17)return kernelNames[edge-9];return nullptr;}
const void* BootstrapExport(unsigned index)
{switch(index){case 0:return reinterpret_cast<const void*>(&RizEngineFirstInstall_GetVersion);
 case 1:return reinterpret_cast<const void*>(&RizEngineFirstInstall_GetInfoV4);
 case 2:return reinterpret_cast<const void*>(&RizEngineFirstInstall_VerifyV4);
 case 3:return reinterpret_cast<const void*>(&RizEngineFirstInstall_AbortAfterPublication);default:return nullptr;}}
bool ProductionImportGroup(unsigned source,unsigned destination,unsigned first,unsigned count)
{
 if constexpr(!Profile::Production||!Profile::ProductionEdges||Profile::ProductionEdgeCount!=Production::EdgeCount){return false;}
 else{
 if(!HasDirectory(source,IMAGE_DIRECTORY_ENTRY_IMPORT))return false;
 const auto directory=headers[source]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
 if(!InImage(source,directory.VirtualAddress,directory.Size))return false;
 const char* moduleName=ProductionModuleName(destination);if(!moduleName)return false;
 unsigned found=0;bool terminated=false;
 for(unsigned offset=0;offset+sizeof(IMAGE_IMPORT_DESCRIPTOR)<=directory.Size;offset+=sizeof(IMAGE_IMPORT_DESCRIPTOR)){
  const auto* descriptor=reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(At(source,directory.VirtualAddress+offset));
  if(!descriptor->Name){terminated=true;break;}
  if(source==Production::Bootstrap&&!ImageString(source,descriptor->Name,"KERNEL32.dll")
   &&!ImageString(source,descriptor->Name,"ntdll.dll")&&!ImageString(source,descriptor->Name,ProductionModuleName(Production::Kernel)))return false;
  if(!ImageString(source,descriptor->Name,moduleName))continue;
  if(++found!=1||!InImage(source,descriptor->OriginalFirstThunk,8)||!InImage(source,descriptor->FirstThunk,8))return false;
  unsigned symbols=0,mask=0;bool thunkEnd=false;
  for(unsigned slot=0;slot!=1024;++slot){
   const auto slotOffset=std::uint64_t(slot)*8ull;
   const auto lookup64=std::uint64_t(descriptor->OriginalFirstThunk)+slotOffset,iat64=std::uint64_t(descriptor->FirstThunk)+slotOffset;
   if(lookup64>0xffffffffu||iat64>0xffffffffu)return false;
   const auto lookup=static_cast<unsigned>(lookup64),iat=static_cast<unsigned>(iat64);
   if(!InImage(source,lookup,8)||!InImage(source,iat,8))return false;
   const auto value=*reinterpret_cast<const std::uint64_t*>(At(source,lookup));
   if(!value){if(*reinterpret_cast<const std::uint64_t*>(At(source,iat)))return false;thunkEnd=true;break;}
   if(IMAGE_SNAP_BY_ORDINAL64(value)||value>0xfffffffdull||!InImage(source,static_cast<unsigned>(value),3))return false;
   const auto symbolRva=static_cast<unsigned>(value)+2;unsigned selected=count;
   for(unsigned index=0;index!=count;++index)if(ImageStringExact(source,symbolRva,ProductionSymbol(first+index))){
    if(selected!=count)return false;selected=index;}
   if(selected==count||(mask&(1u<<selected)))return false;
   const auto& expected=Profile::ProductionEdges[first+selected];
   if(expected.source!=source||expected.destination!=destination||!SameText(expected.symbol,ProductionSymbol(first+selected)))return false;
   if(source==Production::Bootstrap){if(expected.lookupRva||expected.iatRva)return false;}
   else if(!expected.lookupRva||!expected.iatRva||expected.lookupRva!=lookup||expected.iatRva!=iat)return false;
   unsigned targetRva=0;if(!ExportRva(destination,expected.symbol,&targetRva))return false;
   if(destination==Production::Bootstrap){if(expected.exportRva||first!=5||At(destination,targetRva)!=BootstrapExport(selected))return false;}
   else if(!expected.exportRva||expected.exportRva!=targetRva)return false;
   if(*reinterpret_cast<const std::uint64_t*>(At(source,iat))!=reinterpret_cast<std::uint64_t>(At(destination,targetRva)))return false;
   mask|=1u<<selected;++symbols;
  }
  if(!thunkEnd||symbols!=count||mask!=(1u<<count)-1)return false;
 }
 return terminated&&found==1;
 }
}
bool ProductionGraph(bool recordFacts)
{
 if(!ProductionImportGroup(3,2,0,1))return false;
 if(recordFacts)information.admissionFlags|=Production::ExecutableToUnity;
 if(!ProductionImportGroup(2,4,1,4))return false;
 if(recordFacts)information.admissionFlags|=Production::UnityToDoorstop;
 if(!ProductionImportGroup(4,0,5,4))return false;
 if(recordFacts)information.admissionFlags|=Production::DoorstopToBootstrap;
 if(!ProductionImportGroup(0,1,9,8))return false;
 if(recordFacts)information.admissionFlags|=Production::BootstrapToKernel;
 return true;
}
bool Pinned(unsigned index)
{
 HMODULE actual=nullptr;
 if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN|GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
   reinterpret_cast<LPCWSTR>(modules[index]),&actual)||actual!=modules[index])return false;
 modulePinned[index]=true;return true;
}
bool Unwinds()
{
 const auto directory=headers[2]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
 if(!directory.Size||directory.Size%sizeof(RUNTIME_FUNCTION)||!InImage(2,directory.VirtualAddress,directory.Size))return false;
 const auto* actual=reinterpret_cast<const RUNTIME_FUNCTION*>(At(2,directory.VirtualAddress));
 const auto count=directory.Size/sizeof(RUNTIME_FUNCTION);
 for(const auto& expected:Profile::Unwinds){
  if(!InImage(2,expected.rva,expected.bytes)||!Equal(At(2,expected.rva),expected.raw,expected.bytes))return false;
  Sha256 hash;hash.Initialize();unsigned char digest[32];hash.Feed(At(2,expected.rva),expected.bytes);hash.Finish(digest);
  if(!Equal(digest,expected.sha256,32))return false;
 }
 for(unsigned kind=0;kind!=4;++kind){const auto& function=Profile::Functions[kind];unsigned observed=0;
  if(function.kind!=kind||function.prefixBytes!=Profile::PrefixLengths[kind]||function.rva>=function.endRva
    ||function.bodyBytes!=function.endRva-function.rva||!function.runtimeFunctionCount||function.runtimeFunctionCount>16)return false;
  for(unsigned index=0;index!=count;++index){const auto& row=actual[index];
   if(row.BeginAddress<function.endRva&&row.EndAddress>function.rva){if(observed>=function.runtimeFunctionCount)return false;
    const auto& expected=function.runtimeFunctions[observed++];
    if(row.BeginAddress!=expected.beginRva||row.EndAddress!=expected.endRva||row.UnwindData!=expected.unwindRva)return false;}}
  if(observed!=function.runtimeFunctionCount)return false;
 }return true;
}
bool Bodies(bool preparing)
{
 if(!Unwinds())return false;
 for(unsigned kind=0;kind!=4;++kind){const auto& fact=Profile::Functions[kind];auto& row=information.rows[kind];DWORD protection=0;
  if(!InImage(2,fact.rva,fact.bodyBytes)||!Memory(At(2,fact.rva),fact.bodyBytes,modules[2],true,false,&protection)
   ||protection!=PAGE_EXECUTE_READ)return false;
  const bool published=(information.committedMask&(1u<<kind))!=0;
  if(!Equal(At(2,fact.rva),published?row.patchBytes:fact.body,fact.prefixBytes)
   ||!Equal(At(2,fact.rva)+fact.prefixBytes,fact.body+fact.prefixBytes,fact.bodyBytes-fact.prefixBytes))return false;
  if(preparing){Sha256 hash;hash.Initialize();hash.Feed(At(2,fact.rva),fact.bodyBytes);hash.Finish(row.bodySha256);
   if(!Equal(row.bodySha256,fact.bodySha256,32))return false;}
 }
 return true;
}
bool OriginalShape(unsigned kind)
{
 const auto& fact=Profile::Functions[kind];const auto* original=static_cast<const unsigned char*>(originals[kind]);
 if(!Memory(original,fact.prefixBytes+10,modules[0],true,false)||!Equal(original,fact.prefix,fact.prefixBytes))return false;
 const auto* tail=original+fact.prefixBytes;
 if(tail[0]!=0x4c||tail[1]!=0x8b||tail[2]!=0x1d||tail[7]!=0x41||tail[8]!=0xff||tail[9]!=0xe3)return false;
 const auto address=reinterpret_cast<std::uintptr_t>(tail)+7+*reinterpret_cast<const std::int32_t*>(tail+3);
 if(address!=reinterpret_cast<std::uintptr_t>(&RizEngineContinuation[kind]))return false;
 const auto directory=headers[0]->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
 if(!directory.Size||directory.Size%sizeof(RUNTIME_FUNCTION)||!InImage(0,directory.VirtualAddress,directory.Size))return false;
 const auto rva=static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(original)-reinterpret_cast<std::uintptr_t>(modules[0]));
 const auto* table=reinterpret_cast<const RUNTIME_FUNCTION*>(At(0,directory.VirtualAddress));unsigned matched=0;
 for(unsigned index=0;index!=directory.Size/sizeof(RUNTIME_FUNCTION);++index)if(table[index].BeginAddress==rva){
  const auto& rf=table[index];if(++matched!=1||rf.EndAddress!=rva+fact.prefixBytes+10||!InImage(0,rf.UnwindData,4))return false;
  const auto* unwind=At(0,rf.UnwindData);if(unwind[0]!=1||unwind[1]!=fact.prefixBytes+10||unwind[3]!=0)return false;
 }
 return matched==1;
}
bool KernelIdentity(unsigned kind,bool initial)
{
 const auto& row=information.rows[kind];RizEngineNodeInfoV1 actual{};actual.abiVersion=1;actual.byteSize=sizeof(actual);
 if(RizEngineNodeContext_GetInfo(kind,Profile::Slot,&actual)||actual.abiVersion!=1||actual.byteSize!=sizeof(actual)
   ||actual.kind!=kind||actual.slot!=Profile::Slot||actual.unknown||actual.attached>1
   ||reinterpret_cast<std::uint64_t>(actual.target)!=row.target||reinterpret_cast<std::uint64_t>(actual.original)!=row.original
   ||reinterpret_cast<std::uint64_t>(actual.entry)!=row.entry||actual.epoch!=row.helperEpoch
   ||!actual.epoch||actual.enterFaults||actual.exitFaults||actual.snapshotFaults||actual.outputFaults)return false;
 // A transparent pre-CLR call may have entered/exited before managed adoption.
 // Counter fields are intentionally not copied into the immutable V4 record.
 return !initial||(!actual.attached&&!actual.entered&&!actual.exited&&!actual.inFlight);
}
bool Validate(HINSTANCE self)
{
 modules[0]=self;modules[1]=GetModuleHandleW(Profile::KernelModule);modules[2]=GetModuleHandleW(Profile::Target.module);modules[3]=GetModuleHandleW(nullptr);
 if(Profile::Production)modules[4]=GetModuleHandleW(Profile::DoorstopModule);
 if(!modules[0]||!modules[1]||!modules[2]||!modules[3]||modules[0]==modules[1]||modules[0]==modules[2]
  ||GetModuleHandleW(Profile::BootstrapModule)!=self)return Reject(RizEngineBadDependency);
 const unsigned moduleCount=Profile::Production?Production::ModuleCount:4;
 for(unsigned index=0;index!=moduleCount;++index){if(!modules[index])return Reject(RizEngineBadDependency,index);
  for(unsigned other=0;other!=index;++other)if(modules[index]==modules[other])return Reject(RizEngineBadDependency,index);}
 information.bootstrapModule=reinterpret_cast<std::uint64_t>(modules[0]);information.kernelModule=reinterpret_cast<std::uint64_t>(modules[1]);
 information.targetModule=reinterpret_cast<std::uint64_t>(modules[2]);information.executableModule=reinterpret_cast<std::uint64_t>(modules[3]);
 if(!ValidateModule(3,&Profile::Executable,Profile::Executable.module)||!ValidateModule(0,nullptr,Profile::BootstrapModule)
  ||!ValidateModule(1,Profile::KernelIdentity,Profile::KernelModule)||!ValidateModule(2,&Profile::Target,Profile::Target.module))return false;
 if(HasDirectory(3,IMAGE_DIRECTORY_ENTRY_TLS))return Reject(RizEngineBadDependency);
 if constexpr(Profile::Production){
  if constexpr(!Profile::KernelIdentity||!Profile::DoorstopIdentity)return Reject(RizEngineBadDependency);
  else{
  if(!SameWide(Profile::KernelIdentity->module,Profile::KernelModule)
   ||!SameWide(Profile::DoorstopIdentity->module,Profile::DoorstopModule))return Reject(RizEngineBadDependency);
  if(!ValidateModule(4,Profile::DoorstopIdentity,Profile::DoorstopModule))return false;
  information.admissionFlags|=Production::FixedDoorstop;
  if(!ProductionGraph(true))return Reject(RizEngineBadDependency);
  }
 }else{
  if(!ImportEdge(3,2,false))return Reject(RizEngineBadDependency);information.admissionFlags|=2;
  if(Profile::Owned){if(!ImportEdge(2,0,false))return Reject(RizEngineBadDependency);information.admissionFlags|=4;}
  if(!ImportEdge(0,1,true))return Reject(RizEngineBadDependency);information.admissionFlags|=8;
 }
 if(Profile::Owned){DWORD protection=0;
  const auto* marker=At(2,Profile::MarkerRva);
  if(!InImage(2,Profile::MarkerRva,16)||!Memory(marker,16,modules[2],false,false,&protection)||protection!=PAGE_READONLY
   ||!Equal(marker,Profile::OwnedMarker,16)||reinterpret_cast<const void*>(GetProcAddress(modules[2],"RizFixedOwnedMarker"))!=marker)
   return Reject(RizEngineBadSource);
  information.admissionFlags|=16;
 }
 if(!Bodies(true))return Reject(RizEngineBadUnwind);
 if(Profile::Production)information.admissionFlags|=Production::FixedGameBodies;
 for(unsigned index=0;index!=moduleCount;++index)if(!SameFile(index))return Reject(RizEngineBadHash,index);
 for(unsigned kind=0;kind!=4;++kind){const auto& fact=Profile::Functions[kind];auto& row=information.rows[kind];
  if(!OriginalShape(kind))return Reject(RizEngineBadSource,kind);
  row.version=4;row.byteSize=sizeof(row);row.kind=kind;row.prefixBytes=fact.prefixBytes;row.module=information.targetModule;
  row.target=reinterpret_cast<std::uint64_t>(At(2,fact.rva));row.original=reinterpret_cast<std::uint64_t>(originals[kind]);
  row.continuation=row.target+row.prefixBytes;row.epoch=1;row.rva=fact.rva;row.originalProtection=PAGE_EXECUTE_READ;row.observedProtection=PAGE_EXECUTE_READ;
  row.runtimeFunctionCount=fact.runtimeFunctionCount;Copy(row.originalBytes,At(2,fact.rva),32);Copy(row.moduleSha256,Profile::Target.sha256,32);
  SYSTEM_INFO system{};GetSystemInfo(&system);if(!system.dwPageSize||row.target/system.dwPageSize!=(row.target+31)/system.dwPageSize)return Reject(RizEngineBadSource,kind);
  for(unsigned other=0;other!=kind;++other)if(row.target<information.rows[other].target+32&&information.rows[other].target<row.target+32)return Reject(RizEngineBadSource,kind);
  if(!Record(TargetValidated,kind))return false;
 }
 // The fixed game's complete startup/indirect-caller admission is still
 // unproven. Neither matching bytes nor owned startup can grant it authority.
 if constexpr((Profile::Production&&!Profile::ProductionFirstInstallAuthorized)||(!Profile::Production&&!Profile::Owned)){
  // All available static facts above are recorded before this permanent
  // no-write refusal. Keep the authorized tail in the opposite compile-time
  // branch so a facts-only build contains no unreachable permission bypass.
  return Reject(RizEngineAdmissionUnproven);
 }else{
  if constexpr(Profile::Production){
   if(information.admissionFlags!=Production::StaticFactsMask)return Reject(RizEngineAdmissionUnproven);
   // A separate, source-reviewed execution-window permission would be needed.
   // Neither lpReserved nor the ordinary graph can grant it.
   information.admissionFlags|=Production::ExecutionWindowAuthorized;
  }else if(information.admissionFlags!=31||!Profile::KernelIdentity)return Reject(RizEngineAdmissionUnproven);
  for(unsigned index=0;index!=3;++index)if(!Pinned(index))return Reject(RizEngineBadDependency,index,GetLastError());
  if constexpr(Profile::Production){if(!Pinned(4))return Reject(RizEngineBadDependency,4,GetLastError());}
  if(RizEngineNodeContext_GetAbiVersion()!=2||RizEngineNodeContext_GetLimit()!=32
   ||RizEngineNodeContext_GetFrameSize()!=112||RizEngineNodeContext_GetCompletionSize()!=32)return Reject(RizEngineBadKernel);
  return true;
 }
}
bool PrepareAndConfigure()
{
 information.epoch=1;
 // Complete all four descriptors/originals and all four Kernel claims before
 // the first source protection change. A rejected later Configure is no-write,
 // but its earlier permanent Kernel claims are retained and never retried.
 for(unsigned kind=0;kind!=4;++kind){auto& row=information.rows[kind];
  if(InterlockedCompareExchange(&stickyUnknown,0,0))return Reject(RizEngineRepeatedAttach,kind);
  if(RizEngineContinuation[kind])return Reject(RizEngineBadSource,kind);
  InterlockedExchangePointer(&RizEngineContinuation[kind],reinterpret_cast<void*>(row.continuation));row.flags|=1;
  if(!Record(ContinuationPrepared,kind))return false;
 }
 information.state=RizEngineConfiguring;
 for(unsigned kind=0;kind!=4;++kind){auto& row=information.rows[kind];
  if(InterlockedCompareExchange(&stickyUnknown,0,0))return Reject(RizEngineRepeatedAttach,kind);
  if(Fault(2,kind))return Reject(RizEngineConfigureFailed,kind);
  if(!Record(ConfigureBegin,kind))return false;
  RizEngineNodeConfigV1 config{1,sizeof(RizEngineNodeConfigV1),reinterpret_cast<void*>(row.target),reinterpret_cast<void*>(row.original)};
  if(RizEngineNodeContext_Configure(kind,Profile::Slot,&config))return Reject(RizEngineConfigureFailed,kind);
  information.configuredMask|=1u<<kind;
  RizEngineNodeInfoV1 actual{};actual.abiVersion=1;actual.byteSize=sizeof(actual);
  if(RizEngineNodeContext_GetInfo(kind,Profile::Slot,&actual))return Reject(RizEngineBadKernel,kind);
  row.entry=reinterpret_cast<std::uint64_t>(RizEngineNodeContext_GetEntry(kind,Profile::Slot));row.helperEpoch=actual.epoch;row.helperSlot=Profile::Slot;
  if(!row.entry||!Memory(reinterpret_cast<void*>(row.entry),1,modules[1],true,false)||!KernelIdentity(kind,true))return Reject(RizEngineBadKernel,kind);
  if(!Record(ConfigureEnd,kind))return false;
 }
 for(unsigned kind=0;kind!=4;++kind){auto& row=information.rows[kind];
  Copy(row.patchBytes,row.originalBytes,32);row.patchBytes[0]=0xff;row.patchBytes[1]=0x25;
  for(unsigned index=2;index!=6;++index)row.patchBytes[index]=0;
  for(unsigned index=0;index!=8;++index)row.patchBytes[6+index]=static_cast<unsigned char>(row.entry>>(index*8));
  for(unsigned index=14;index!=row.prefixBytes;++index)row.patchBytes[index]=0x90;
  row.flags|=2;information.boundMask|=1u<<kind;if(!Record(EntryBound,kind))return false;
 }return true;
}
bool Restore(unsigned kind,bool inject)
{
 auto& row=information.rows[kind];if(!row.protectionOutstanding)return true;
 if(inject&&Fault(6,kind)){row.lastWin32Error=ERROR_ACCESS_DENIED;return Reject(RizEngineRestoreFailed,kind,ERROR_ACCESS_DENIED);}
 ++row.restoreAttempts;DWORD ignored=0;SetLastError(ERROR_SUCCESS);
 if(!VirtualProtect(reinterpret_cast<void*>(row.target),row.prefixBytes,row.originalProtection,&ignored)){
  row.lastWin32Error=GetLastError();return Reject(RizEngineRestoreFailed,kind,row.lastWin32Error);}
 DWORD actual=0;
 if(!Memory(reinterpret_cast<void*>(row.target),32,modules[2],true,false,&actual)||actual!=row.originalProtection){
  row.observedProtection=actual;return Reject(RizEngineRestoreFailed,kind);}
 ++row.restoreSucceeded;row.observedProtection=actual;row.protectionOutstanding=0;--information.protectionOutstanding;return Record(Restored,kind);
}
bool AbortCommit(unsigned kind,unsigned failure,unsigned error=0)
{
 if(information.rows[kind].protectionOutstanding)(void)Restore(kind,false);
 return Reject(failure,kind,error);
}
bool Commit(unsigned kind)
{
 if(InterlockedCompareExchange(&stickyUnknown,0,0))return Reject(RizEngineRepeatedAttach,kind);
 if(!Bodies(false))return Reject(RizEngineBadSource,kind);
 auto& row=information.rows[kind];information.state=RizEnginePublishing;
 if(Fault(3,kind))return Reject(RizEngineProtectFailed,kind);
 if(!Record(ProtectBegin,kind))return false;
 DWORD before=0;++row.protectAttempts;SetLastError(ERROR_SUCCESS);
 if(!VirtualProtect(reinterpret_cast<void*>(row.target),row.prefixBytes,PAGE_EXECUTE_READWRITE,&before)){
  row.lastWin32Error=GetLastError();return Reject(RizEngineProtectFailed,kind,row.lastWin32Error);}
 ++row.protectSucceeded;row.protectionOutstanding=1;++information.protectionOutstanding;row.observedProtection=PAGE_EXECUTE_READWRITE;
 if(!Record(ProtectEnd,kind))return false;
 if(before!=row.originalProtection||Fault(4,kind))return AbortCommit(kind,RizEngineProtectFailed);
 if(!Record(WriteBegin,kind))return false;
 ++row.sourceWriteAttempts;++information.sourceWriteAttempts;
 const unsigned bytes=Fault(5,kind)?1:row.prefixBytes;auto* target=reinterpret_cast<volatile unsigned char*>(row.target);
 for(unsigned index=0;index!=bytes;++index){target[index]=row.patchBytes[index];++row.sourceBytesWritten;}
 if(!Record(WriteEnd,kind))return false;
 if(bytes!=row.prefixBytes)return AbortCommit(kind,RizEngineWriteFailed);
 ++information.sourceWriteCompletions;++row.readbackAttempts;Copy(row.observedBytes,reinterpret_cast<void*>(row.target),32);
 if(!Equal(row.observedBytes,row.patchBytes,32))return AbortCommit(kind,RizEngineBadReadback);
 ++row.readbackVerified;if(!Record(Readback,kind)||!Restore(kind,true))return false;
 if(Fault(7,kind)){row.lastWin32Error=ERROR_GEN_FAILURE;return Reject(RizEngineFlushFailed,kind,ERROR_GEN_FAILURE);}
 ++row.flushAttempts;SetLastError(ERROR_SUCCESS);
 if(!FlushInstructionCache(GetCurrentProcess(),reinterpret_cast<void*>(row.target),row.prefixBytes)){
  row.lastWin32Error=GetLastError();return Reject(RizEngineFlushFailed,kind,row.lastWin32Error);}
 ++row.flushSucceeded;if(!Record(Flushed,kind))return false;
 row.flags|=4;information.committedMask|=1u<<kind;return Record(Committed,kind);
}
bool VerifyCurrent()
{
 if constexpr(Profile::Owned||(Profile::Production&&Profile::ProductionFirstInstallAuthorized)){
  if(information.state!=RizEnginePublicationReady||InterlockedCompareExchange(&stickyUnknown,0,0)
   ||information.profile!=Profile::Profile||information.configuredMask!=15||information.boundMask!=15
   ||information.committedMask!=15||information.sourceWriteAttempts!=4||information.sourceWriteCompletions!=4||information.protectionOutstanding
   ||information.overflow||!information.epoch||!modulePinned[0]||!modulePinned[1]||!modulePinned[2])return false;
  if constexpr(Profile::Production){
   if(information.admissionFlags!=Production::ReadyMask||!modulePinned[4])return false;
  }else if(information.admissionFlags!=31)return false;
  const unsigned moduleCount=Profile::Production?Production::ModuleCount:4;
  for(unsigned index=0;index!=moduleCount;++index){const auto* nt=ImageHeader(modules[index]);const auto& expected=ModuleFacts(index);
   if(!nt||nt->OptionalHeader.SizeOfImage!=expected.imageSize||nt->FileHeader.TimeDateStamp!=expected.timeDateStamp
    ||nt->OptionalHeader.AddressOfEntryPoint!=expected.entryRva||!SameFile(index)
    ||!DiskHeaderMatches(reinterpret_cast<HANDLE>(expected.heldFile),modules[index],nt))return false;}
  if constexpr(Profile::Production){if(!ProductionGraph(false))return false;}
  else if(!ImportEdge(3,2,false)||!ImportEdge(2,0,false)||!ImportEdge(0,1,true))return false;
  // Profile 2 binds the fixed game bodies and all six actual RF/unwind facts;
  // it never substitutes the owned export marker for game provenance.
  if(!Bodies(false))return false;
  for(unsigned kind=0;kind!=4;++kind){const auto& row=information.rows[kind];DWORD protection=0;
   if(row.flags!=7||row.helperSlot!=Profile::Slot||row.epoch!=information.epoch||!OriginalShape(kind)
     ||reinterpret_cast<std::uint64_t>(RizEngineContinuation[kind])!=row.continuation
     ||row.continuation!=row.target+row.prefixBytes||!Memory(reinterpret_cast<void*>(row.target),32,modules[2],true,false,&protection)
     ||protection!=row.originalProtection||!Equal(reinterpret_cast<void*>(row.target),row.patchBytes,32)||!KernelIdentity(kind,false))return false;
  }
  return true;
 }else{
  return false;
 }
}
bool Initialize(HINSTANCE module)
{
 if(Fault(1))return Reject(RizEngineBadImage);
 if(Fault(8)){(void)RizEngineBootstrapEntry(module,DLL_PROCESS_ATTACH,reinterpret_cast<void*>(information.reservedPointer));
  return Reject(RizEngineRepeatedAttach);}
 if(!Validate(module)||!PrepareAndConfigure())return false;
 for(unsigned kind=0;kind!=4;++kind)if(!Commit(kind))return false;
 information.state=RizEnginePublicationReady;
 if(!VerifyCurrent())return Reject(RizEngineBadReadback);
 return Record(Ready);
}
bool EnterGate(){return InterlockedCompareExchange(&gate,1,0)==0;}
void LeaveGate(){InterlockedExchange(&gate,0);}
void NativeFailure()
{InterlockedExchange(&stickyUnknown,1);(void)Reject(RizEngineNativeException,4,GetLastError());}
}

extern "C" BOOL WINAPI RizEngineBootstrapEntry(HINSTANCE module,DWORD reason,LPVOID reserved)
{
 const DWORD incoming=GetLastError();
 if(reason!=DLL_PROCESS_ATTACH){
  if(reason==DLL_PROCESS_DETACH&&!reserved&&(Responsibility()||information.configuredMask))Fatal();
  SetLastError(incoming);return TRUE;
 }
 if(InterlockedCompareExchange(&claimed,1,0)!=0){
  InterlockedExchange(&stickyUnknown,1);information.state=RizEnginePublicationUnknown;
  if(!information.failure)information.failure=RizEngineRepeatedAttach;Record(Reentry);SetLastError(incoming);
  if(Responsibility())Fatal();return FALSE;
 }
 if(!EnterGate()){InterlockedExchange(&stickyUnknown,1);information.state=RizEnginePublicationUnknown;
  information.failure=RizEngineRepeatedAttach;SetLastError(incoming);return FALSE;}
 information.claimed=1;information.state=RizEngineValidating;information.ownerThread=GetCurrentThreadId();information.processId=GetCurrentProcessId();
 information.attachReason=reason;information.startupStatic=reserved?1u:0u;information.reservedPointer=reinterpret_cast<std::uint64_t>(reserved);
 information.profile=Profile::Profile;Record(Entered);
 __try {
  if(!reserved)(void)Reject(RizEngineDynamicLoad);
  else {information.admissionFlags=Profile::Production?Production::StaticStartup:1;(void)Initialize(module);}
 }
 __except(EXCEPTION_EXECUTE_HANDLER){NativeFailure();}
 if(InterlockedCompareExchange(&stickyUnknown,0,0))information.state=RizEnginePublicationUnknown;
 const bool failedAfterWrite=information.state!=RizEnginePublicationReady&&Responsibility();
 LeaveGate();SetLastError(incoming);if(failedAfterWrite)Fatal();return TRUE;
}
RIZ_ENGINE_PUBLICATION_API std::uint32_t __cdecl RizEngineFirstInstall_GetVersion(){return 4;}
RIZ_ENGINE_PUBLICATION_API std::uint32_t __cdecl RizEngineFirstInstall_GetInfoV4(RizEnginePublicationV4* output)
{
 const DWORD incoming=GetLastError();if(!EnterGate()){SetLastError(incoming);return 2;}unsigned result=1;
 __try {if(InterlockedCompareExchange(&stickyUnknown,0,0))information.state=RizEnginePublicationUnknown;
  if(Memory(output,sizeof(*output),nullptr,false,true)&&output->version==4&&output->byteSize==sizeof(*output)){
   Copy(output,&information,sizeof(*output));result=0;}}
 __except(EXCEPTION_EXECUTE_HANDLER){NativeFailure();result=1;}
 LeaveGate();SetLastError(incoming);return result;
}
RIZ_ENGINE_PUBLICATION_API std::uint32_t __cdecl RizEngineFirstInstall_VerifyV4()
{
 const DWORD incoming=GetLastError();if(!EnterGate()){SetLastError(incoming);return 2;}unsigned result=1;
 __try {if(VerifyCurrent())result=0;else{
   ++information.verifyFailures;InterlockedExchange(&stickyUnknown,1);information.state=RizEnginePublicationUnknown;
   if(!information.failure)information.failure=RizEngineBadReadback;(void)Record(Failure);}}
 __except(EXCEPTION_EXECUTE_HANDLER){++information.verifyFailures;NativeFailure();}
 LeaveGate();SetLastError(incoming);return result;
}
RIZ_ENGINE_PUBLICATION_API void __cdecl RizEngineFirstInstall_AbortAfterPublication()
{
 const DWORD incoming=GetLastError();
 if(!EnterGate()){InterlockedExchange(&stickyUnknown,1);if(Responsibility()||RetainedClaims())Fatal();SetLastError(incoming);return;}
 // A dependent DLL cannot abandon a Kernel claim even when the first source
 // write never happened. Original continuations and their owner stay pinned.
 const bool fatal=Responsibility()||RetainedClaims();
 if(fatal)InterlockedExchange(&stickyUnknown,1);
 (void)Reject(RizEngineBadDependency);LeaveGate();SetLastError(incoming);if(fatal)Fatal();
}
