#pragma once
#include <cstdint>

// Build-time facts only. This contract cannot authorize a source write.
// Module indices 0..3 retain their V4 meaning; index 4 is private native
// Doorstop storage and never occupies a V4 module or reserved byte.
namespace RizEngineBootstrapProductionContract {
inline constexpr std::uint32_t Bootstrap = 0, Kernel = 1, UnityPlayer = 2, Executable = 3, Doorstop = 4;
inline constexpr std::uint32_t ModuleCount = 5, EdgeCount = 17;
struct ImportEdge {
 std::uint32_t source;
 std::uint32_t destination;
 const char* symbol;
 std::uint32_t lookupRva;
 std::uint32_t iatRva;
 std::uint32_t exportRva;
};
// The generated header defines namespace RizEngineBootstrapProductionProfile:
// ImageIdentity Kernel, Doorstop; ImportEdge Edges[17], in chain order:
// EXE->UnityMain2, UnityPlayer->four WinHTTP, Doorstop->four Bootstrap APIs,
// Bootstrap->eight Kernel APIs. Names are exact, case-sensitive export names.
// Only Bootstrap's own source lookup/iat RVAs and destination export RVAs
// are zero (not knowable before linking this DLL). They must be resolved
// uniquely from actual ordinary ILT/IAT/EAT, never GetProcAddress or loading.
// Every other RVA is nonzero and pinned by the final disk image identities.
inline constexpr std::uint32_t StaticStartup = 0x100;
inline constexpr std::uint32_t ExecutableToUnity = 0x200;
inline constexpr std::uint32_t UnityToDoorstop = 0x400;
inline constexpr std::uint32_t DoorstopToBootstrap = 0x800;
inline constexpr std::uint32_t BootstrapToKernel = 0x1000;
inline constexpr std::uint32_t FixedDoorstop = 0x2000;
inline constexpr std::uint32_t FixedGameBodies = 0x4000;
inline constexpr std::uint32_t ExecutionWindowAuthorized = 0x8000;
inline constexpr std::uint32_t StaticFactsMask = 0x7f00;
inline constexpr std::uint32_t ReadyMask = 0xff00;
static_assert(sizeof(ImportEdge) == 32);
} // namespace RizEngineBootstrapProductionContract
