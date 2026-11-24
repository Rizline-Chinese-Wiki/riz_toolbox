#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstddef>
extern "C" void __cdecl StockProgressMusicPlayer(void*, void*, void*);
namespace {
struct Type { void* Data; std::uint32_t Bits, Padding; };
struct Method { void* Entry; void* Virtual; void* Invoker; const char* Name; void* Class; void* Returns;
    void* Parameters; void* Runtime; void* Generic; std::uint32_t Token;
    std::uint16_t Flags, Implementation, Slot; unsigned char Count, Bits; };
std::uint64_t progressClass = 0x70726f6772657373ull, playerClass = 0x706c617965722d31ull;
Type voidType{nullptr,0x00010000u,0}, playerType{&playerClass,0x00120000u,0};
Type* parameters[]{&playerType};
Method method{reinterpret_cast<void*>(&StockProgressMusicPlayer),reinterpret_cast<void*>(&StockProgressMusicPlayer),nullptr,
    "set_MusicPlayer",&progressClass,&voidType,parameters,nullptr,nullptr,0x0600100au,0x86,0,0xffff,1,0};
volatile LONG calls{}, mode{};
void* lastField{}; void* lastValue{}; void* lastMethod{};
}
// This is only the owned fixture tail ABI. It makes no claim about the real
// 0x263f60 target's semantic name or ABI beyond the observed tail transfer.
extern "C" __declspec(dllexport) void __cdecl StockProgressPlayerTail(void* field, void* value, void* incoming) {
    InterlockedIncrement(&calls); lastField=field; lastValue=value; lastMethod=incoming;
    if (*static_cast<void**>(field) != value) RaiseException(0xe2400020u,0,0,nullptr);
    if (InterlockedCompareExchange(&mode,0,0)==1) RaiseException(0xe2400021u,0,0,nullptr);
}
extern "C" __declspec(dllexport) void* __cdecl StockMethodProgressMusicPlayer() { return &method; }
extern "C" __declspec(dllexport) void __cdecl StockProgressWriterMode(std::uint32_t value) { InterlockedExchange(&mode,static_cast<LONG>(value)); }
extern "C" __declspec(dllexport) std::uint64_t __cdecl StockProgressWriterRead(std::uint32_t field) {
    switch(field) { case 0:return static_cast<std::uint64_t>(InterlockedCompareExchange(&calls,0,0));
    case 1:return reinterpret_cast<std::uint64_t>(lastField); case 2:return reinterpret_cast<std::uint64_t>(lastValue);
    case 3:return reinterpret_cast<std::uint64_t>(lastMethod); default:return 0; }
}
