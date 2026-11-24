#pragma once
#include <stdint.h>

// Internal fixed Windows-x64 originals, independent of the four Node originals.
// No Configure API, exported setter, loader work, source write or admission is
// provided here. The native publisher (or the owned fixture) defines this one
// array and binds all nine slots before any original can be called. The slots
// must then remain fixed while a source, relay or original can still reach them.
// These are full-RAX / four-integer-register machine carriers, not claims about
// the source-language return types of the nine observed functions. Callback's
// source consumer tests full EAX; it must never be narrowed to AL or bool.

#if defined(_MSC_VER)
#define RIZ_ENGINE_TRANSPORT_CALL __fastcall
#else
#define RIZ_ENGINE_TRANSPORT_CALL
#endif

enum RizEngineTransportOriginalIndex {
    RizEngineTransportRegisterIndex = 0,
    RizEngineTransportRemoveIndex = 1,
    RizEngineTransportConsumeIndex = 2,
    RizEngineTransportPumpIndex = 3,
    RizEngineTransportDrainIndex = 4,
    RizEngineTransportCallbackIndex = 5,
    RizEngineTransportYieldIndex = 6,
    RizEngineTransportDispatchIndex = 7,
    RizEngineTransportBatchIndex = 8,
    RizEngineTransportOriginalCount = 9
};

enum RizEngineTransportOriginalLength {
    RizEngineTransportRegisterPrefixBytes = 15,
    RizEngineTransportRemovePrefixBytes = 14,
    RizEngineTransportConsumePrefixBytes = 14,
    RizEngineTransportPumpPrefixBytes = 15,
    RizEngineTransportDrainPrefixBytes = 14,
    RizEngineTransportCallbackPrefixBytes = 17,
    RizEngineTransportYieldPrefixBytes = 15,
    RizEngineTransportDispatchPrefixBytes = 14,
    RizEngineTransportBatchPrefixBytes = 14,
    RizEngineTransportTailBytes = 10
};

typedef uint64_t (RIZ_ENGINE_TRANSPORT_CALL *RizEngineTransportOriginalCarrier)(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);

#ifdef __cplusplus
extern "C" {
#endif

// Private C-linkage storage; this declaration does not add an export to a DLL.
extern uintptr_t RizEngineTransportContinuation[RizEngineTransportOriginalCount];

uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalRegister(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalRemove(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalConsume(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalPump(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalDrain(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalCallback(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalYield(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalDispatch(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);
uint64_t RIZ_ENGINE_TRANSPORT_CALL RizEngineTransportOriginalBatch(
    uintptr_t arg0, uintptr_t arg1, uintptr_t arg2, uintptr_t arg3);

// PrefixEnd is the first byte of the 7-byte continuation MOV. End is the
// exclusive 10-byte tail / RUNTIME_FUNCTION bound. PUBLIC COFF labels let the
// owned fixture inspect every actual PC without a production EAT/setter API.
extern const uint8_t RizEngineTransportOriginalRegisterPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalRegisterEnd[];
extern const uint8_t RizEngineTransportOriginalRemovePrefixEnd[];
extern const uint8_t RizEngineTransportOriginalRemoveEnd[];
extern const uint8_t RizEngineTransportOriginalConsumePrefixEnd[];
extern const uint8_t RizEngineTransportOriginalConsumeEnd[];
extern const uint8_t RizEngineTransportOriginalPumpPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalPumpEnd[];
extern const uint8_t RizEngineTransportOriginalDrainPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalDrainEnd[];
extern const uint8_t RizEngineTransportOriginalCallbackPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalCallbackEnd[];
extern const uint8_t RizEngineTransportOriginalYieldPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalYieldEnd[];
extern const uint8_t RizEngineTransportOriginalDispatchPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalDispatchEnd[];
extern const uint8_t RizEngineTransportOriginalBatchPrefixEnd[];
extern const uint8_t RizEngineTransportOriginalBatchEnd[];

#ifdef __cplusplus
}
#endif
