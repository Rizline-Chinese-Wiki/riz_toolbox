#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <vector>

// This fixture-only component is reached from wmain after all actual native
// initializer observations. The production Bootstrap never hosts the CLR.
// API declarations are the installed .NET8 SDK headers; hosting sequence:
// https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting
namespace {
HMODULE retainedHostFxr;
template<class Function> Function Find(HMODULE module,const char* name)
{
    const auto address=GetProcAddress(module,name);
    Function function{};static_assert(sizeof(function)==sizeof(address));
    std::memcpy(&function,&address,sizeof(function));return function;
}
bool AbsoluteFile(const wchar_t* value)
{return value&&std::filesystem::path(value).is_absolute()&&std::filesystem::is_regular_file(value);}
}
std::uint32_t RizEngineOwned_RunManaged(const wchar_t* runtimeConfig,const wchar_t* assembly,
    const wchar_t* hostfxr,const wchar_t* argsJsonPath)
{
    if(retainedHostFxr||!AbsoluteFile(runtimeConfig)||!AbsoluteFile(assembly)
        ||!AbsoluteFile(hostfxr)||!AbsoluteFile(argsJsonPath))return 101;
    std::ifstream input(argsJsonPath,std::ios::binary|std::ios::ate);
    if(!input)return 102;
    const auto bytes=input.tellg();if(bytes<=0||bytes>1024*1024)return 103;
    std::vector<char> arguments(static_cast<std::size_t>(bytes));input.seekg(0);
    if(!input.read(arguments.data(),bytes))return 104;
    retainedHostFxr=LoadLibraryExW(hostfxr,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!retainedHostFxr)return 105;
    const auto initialize=Find<hostfxr_initialize_for_runtime_config_fn>(retainedHostFxr,"hostfxr_initialize_for_runtime_config");
    const auto getDelegate=Find<hostfxr_get_runtime_delegate_fn>(retainedHostFxr,"hostfxr_get_runtime_delegate");
    const auto close=Find<hostfxr_close_fn>(retainedHostFxr,"hostfxr_close");
    if(!initialize||!getDelegate||!close)return 106;
    hostfxr_handle context{};
    const auto initialized=initialize(runtimeConfig,nullptr,&context);
    if(initialized<0||!context){if(context)close(context);return 107;}
    void* loadAddress{};
    const auto delegated=getDelegate(context,hdt_load_assembly_and_get_function_pointer,&loadAddress);
    const auto closed=close(context);
    if(delegated<0||!loadAddress||closed<0)return 108;
    load_assembly_and_get_function_pointer_fn load{};
    static_assert(sizeof(load)==sizeof(loadAddress));std::memcpy(&load,&loadAddress,sizeof(load));
    void* entryAddress{};
    const auto loaded=load(assembly,L"NativeEnginePublicationManagedEntry, RizToolbox.Runtime.Tests",L"Run",
        UNMANAGEDCALLERSONLY_METHOD,nullptr,&entryAddress);
    if(loaded<0||!entryAddress)return 109;
    using Entry=int(__cdecl*)(void*,std::int32_t);
    Entry entry{};static_assert(sizeof(entry)==sizeof(entryAddress));std::memcpy(&entry,&entryAddress,sizeof(entry));
    const auto result=entry(arguments.data(),static_cast<std::int32_t>(arguments.size()));
    return result==0?0u:110u;
}
