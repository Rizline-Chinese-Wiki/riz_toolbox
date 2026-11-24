#define RIZ_FIXED_TARGETS_BUILD
#include "FixedFixtureApi.h"

// Bodies belong only to the separately named self-owned target DLL. The exact
// non-NOP entry instructions and their unwind tables are in FixedTargets.asm.
#pragma comment(linker, "/EXPORT:RizFixedOwnedMarker,DATA")
#pragma comment(linker, "/EXPORT:RizFixedOwnedStart")
#pragma comment(linker, "/EXPORT:RizFixedOwnedExecute")
#pragma comment(linker, "/EXPORT:RizFixedOwnedRelease")
#pragma comment(linker, "/EXPORT:RizFixedOwnedDetach")
namespace {
__declspec(thread) RizFixedCase* current;
RizFixedCase* Enter(unsigned kind)
{
    if (!current || kind >= 4) RaiseFailFastException(nullptr, nullptr, 0);
    ++current->calls[kind];
    if (!current->nested && current->throwKind == kind) {
        if (current->throwMode == 1) {
            ULONG_PTR information[]{static_cast<ULONG_PTR>(RizFixedPayload), kind};
            RaiseException(RizFixedSehCode, 0, 2, information);
        }
        if (current->throwMode == 2) throw RizFixedOriginalException{RizFixedPayload, kind};
    }
    return current;
}
}
RIZ_FIXED_TARGET_API RizFixedCase* __cdecl RizFixedOwnedSetCase(RizFixedCase* value)
{ auto* previous = current; current = value; return previous; }
extern "C" std::uint8_t __cdecl RizFixedBodyStart(void* owner, void* enumerator, void* name, void** output)
{
    auto* test = Enter(0);
    if (owner != test->owner || enumerator != test->enumerator || name != test->name || output != &test->outputNode)
        ++test->diagnosticsFailed;
    test->nested = 1;
    if (RizFixedOwnedExecute(test->node, &test->outputByte) != 0xa5c3fe81u) ++test->diagnosticsFailed;
    if (RizFixedOwnedRelease(test->node) != 0xe7u) ++test->diagnosticsFailed;
    test->nested = 0;
    *output = test->node;
    return 0x81;
}
extern "C" std::uint32_t __cdecl RizFixedBodyExecute(void* node, unsigned char* output)
{
    auto* test = Enter(1);
    if (node != test->node || output != &test->outputByte) ++test->diagnosticsFailed;
    *output = 0x9d;
    return 0xa5c3fe81u;
}
extern "C" std::uint8_t __cdecl RizFixedBodyRelease(void* node, std::uint32_t afterCount, unsigned char zeroFlag)
{
    auto* test = Enter(2);
    test->releaseCount = afterCount; test->releaseZeroFlag = zeroFlag;
    if (node != test->node || afterCount != *reinterpret_cast<std::uint32_t*>(test->node + 0x60)
        || zeroFlag != (afterCount == 0 ? 1u : 0u)) ++test->diagnosticsFailed;
    return 0xe7;
}
extern "C" void __cdecl RizFixedBodyDetach(void* node, std::uint32_t clearedEbp)
{
    auto* test = Enter(3); test->detachZero = clearedEbp;
    if (node != test->node || clearedEbp) ++test->diagnosticsFailed;
}
