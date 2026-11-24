#define RIZ_WINHTTP_IMPORTER_BUILD
#include "WinHttpFixtureApi.h"

namespace {
using namespace RizWinFixture;
ImporterSnapshot snapshot{};
Salt salt{};
volatile LONG phaseMask{}, rowCount{};
volatile LONG64 checks{}, failures{}, sequence{};
// Reading dllimport IAT values into a global Api required a CRT initializer,
// which runs after the loader TLS callback. Materialize it on the current stack
// before each phase instead, while retaining all four ordinary static imports.
Api ImportedApi()
{return {&WinHttpOpen,&WinHttpGetProxyForUrl,&WinHttpGetIEProxyConfigForCurrentUser,&WinHttpCloseHandle};}
void Check(bool value){InterlockedIncrement64(&checks);if(!value)InterlockedIncrement64(&failures);}
void Append(const Row& row)
{
    const auto index=InterlockedIncrement(&rowCount)-1;
    if(index<0||static_cast<std::uint32_t>(index)>=ImporterRows){InterlockedIncrement64(&failures);return;}
    snapshot.rows[index]=row;
    Check(row.freesFailed==0);
    for(const auto& text:row.strings)Check(!text.present||text.complete==1);
}
std::uint64_t Next(){return static_cast<std::uint64_t>(InterlockedIncrement64(&sequence));}
void Run(std::uint32_t phase)
{
    const auto api=ImportedApi();
    if(phase==ImporterTls)salt=MakeSalt(&snapshot);
    const auto bit=static_cast<LONG>(1u<<phase);Check((InterlockedOr(&phaseMask,bit)&bit)==0);
    const auto seed=0x6a510000u+phase;
    // Loader-phase observation is deliberately only local synchronous refusal.
    // No IE registry query, valid Open session, PAC/WPAD or request runs here.
    const auto closed=CallClose(api,phase,seed,Next(),nullptr);Append(closed);Check(closed.rawReturn==0);
    const auto proxy=CallProxy(api,phase,seed+0x100,Next(),nullptr,salt);Append(proxy);Check(proxy.rawReturn==0);
}
void RunOutside()
{
    const auto api=ImportedApi();
    const auto bit=static_cast<LONG>(1u<<ImporterOutside);Check((InterlockedOr(&phaseMask,bit)&bit)==0);
    const auto phase=static_cast<std::uint32_t>(ImporterOutside),seed=0x6a52000bu;
    auto opened=CallOpen(api,phase,seed,Next());Append(opened);Check(opened.rawReturn!=0);
    const auto handle=reinterpret_cast<HINTERNET>(opened.rawReturn);
    const auto ie=CallIe(api,phase,seed+0x100,Next(),salt);Append(ie);
    const auto proxy=CallProxy(api,phase,seed+0x200,Next(),handle,salt);Append(proxy);Check(proxy.rawReturn==0);
    const auto closed=CallClose(api,phase,seed+0x300,Next(),handle);Append(closed);Check(closed.rawReturn!=0);
}
void NTAPI Tls(PVOID,DWORD reason,PVOID){if(reason==DLL_PROCESS_ATTACH)Run(ImporterTls);}
int __cdecl CInitializer(){Run(ImporterC);return 0;}
void __cdecl CppInitializer(){Run(ImporterCpp);}
#pragma section(".CRT$XLB",long,read)
#pragma section(".CRT$XIC",long,read)
#pragma section(".CRT$XCU",long,read)
extern "C" __declspec(allocate(".CRT$XLB")) PIMAGE_TLS_CALLBACK const RizWinHttpImporterTls=&Tls;
extern "C" __declspec(allocate(".CRT$XIC")) int(__cdecl* const RizWinHttpImporterCInitializer)()=&CInitializer;
extern "C" __declspec(allocate(".CRT$XCU")) void(__cdecl* const RizWinHttpImporterCppInitializer)()=&CppInitializer;
#pragma comment(linker,"/INCLUDE:_tls_used")
#pragma comment(linker,"/INCLUDE:RizWinHttpImporterTls")
#pragma comment(linker,"/INCLUDE:RizWinHttpImporterCInitializer")
#pragma comment(linker,"/INCLUDE:RizWinHttpImporterCppInitializer")
}
extern "C" BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID)
{if(reason==DLL_PROCESS_ATTACH)Run(RizWinFixture::ImporterDll);return TRUE;}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RizWinHttpImporter_RunOutsideLoader()
{
    if(InterlockedCompareExchange(&phaseMask,0,0)!=0xf0)return 1;
    RunOutside();return InterlockedCompareExchange64(&failures,0,0)==0?0u:1u;
}
extern "C" __declspec(dllexport) std::uint32_t __cdecl RizWinHttpImporter_GetSnapshot(RizWinFixture::ImporterSnapshot* out)
{
    if(!out||out->version!=RizWinFixture::Version||out->byteSize!=sizeof(*out))return 1;
    snapshot.version=RizWinFixture::Version;snapshot.byteSize=sizeof(snapshot);
    snapshot.phaseMask=static_cast<std::uint32_t>(InterlockedCompareExchange(&phaseMask,0,0));
    snapshot.rowCount=static_cast<std::uint32_t>(InterlockedCompareExchange(&rowCount,0,0));
    snapshot.checks=static_cast<std::uint64_t>(InterlockedCompareExchange64(&checks,0,0));
    snapshot.failures=static_cast<std::uint64_t>(InterlockedCompareExchange64(&failures,0,0));
    *out=snapshot;return 0;
}
