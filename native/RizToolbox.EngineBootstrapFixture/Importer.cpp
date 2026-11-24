#ifndef RIZ_ENGINE_OWNED_TARGETS_BUILD
#define RIZ_ENGINE_OWNED_TARGETS_BUILD
#endif
#include "FixtureApi.h"

namespace {
int __cdecl CInitialize(){RizEngineOwned_RunPhase(RizEngineOwned::C);return 0;}
void __cdecl CppInitialize(){RizEngineOwned_RunPhase(RizEngineOwned::Cpp);}
}
extern "C" void NTAPI RizEngineOwned_Tls(PVOID,DWORD reason,PVOID)
{if(reason==DLL_PROCESS_ATTACH)RizEngineOwned_RunPhase(RizEngineOwned::Tls);}
#pragma section(".CRT$XLB",long,read)
#pragma section(".CRT$XIC",long,read)
#pragma section(".CRT$XCU",long,read)
extern "C" __declspec(allocate(".CRT$XLB")) PIMAGE_TLS_CALLBACK const RizEngineOwned_TlsSlot=&RizEngineOwned_Tls;
extern "C" __declspec(allocate(".CRT$XIC")) int(__cdecl* const RizEngineOwned_CSlot)()=&CInitialize;
extern "C" __declspec(allocate(".CRT$XCU")) void(__cdecl* const RizEngineOwned_CppSlot)()=&CppInitialize;
#pragma comment(linker,"/INCLUDE:_tls_used")
#pragma comment(linker,"/INCLUDE:RizEngineOwned_TlsSlot")
#pragma comment(linker,"/INCLUDE:RizEngineOwned_CSlot")
#pragma comment(linker,"/INCLUDE:RizEngineOwned_CppSlot")
BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID)
{if(reason==DLL_PROCESS_ATTACH)RizEngineOwned_RunPhase(RizEngineOwned::Dll);return TRUE;}
