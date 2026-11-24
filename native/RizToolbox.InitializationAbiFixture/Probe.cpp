#include <Windows.h>
#include <stdio.h>
#include "Fixture.h"

static void Fail(const char* text) { fprintf(stderr, "%s\n", text); ExitProcess(1); }
int main()
{
    if (sizeof(void*) != 8 || !RtInitReset()) Fail("Windows x64/reset required");
    auto self = reinterpret_cast<void*>(0x12345678ABCDEF00ULL);
    auto argument = reinterpret_cast<void*>(0x23456789BCDEF010ULL);
    auto method = reinterpret_cast<void*>(0x3456789ACDEF0120ULL);
    auto reference = reinterpret_cast<void*>(0x456789ABDEF01230ULL);
    RtInitSetReference(reference);
    const uint32_t bits[]{ 0, 0x80000000U, 0xBF400000U, 0x7FC01234U, 0xFFC12345U };
    unsigned calls = 0;
    for (uint32_t kind = RtStart; kind <= RtTooltip; ++kind) {
        for (uint32_t variant = 0; variant < (kind == RtTooltip ? 5U : kind == RtPlay ? 2U : 1U); ++variant) {
            RtInitRequest request{ sizeof(request), kind, RtInitTarget(kind), self, argument, method,
                kind == RtTooltip ? bits[variant] : kind == RtPlay ? 0xA5B6C700U | variant : variant, 0 };
            RtInitResult result{ sizeof(result) };
            if (!RtInitInvoke(&request, &result) || result.exceptionCode || (kind == RtGetter && result.reference != reference)) Fail("native ABI call failed");
            RtInitObservation observation{ sizeof(observation) };
            if (!RtInitRead(kind, &observation) || observation.self != self || observation.method != method
                || observation.entered != observation.exited || (kind == RtGetter && observation.argument != argument)
                || (kind == RtTooltip && observation.scalar != bits[variant]) || (kind == RtPlay && observation.scalar != variant)) Fail("native ABI observation differs");
            if (kind == RtPlay && RtInitPlayInputBits() != request.scalar) Fail("MASM caller truncated the raw EDX poison");
            if (!RtInitInvokeWorker(&request, &result) || result.threadId == GetCurrentThreadId() || result.exceptionCode) Fail("native worker call failed");
            calls += 2;
        }
    }
    printf("{\"kind\":\"native-only-ABI-probe\",\"calls\":%u,\"worker\":true,\"rawPlayEdxPoison\":true,\"managedTransportVerified\":false,\"gameRuntimeVerified\":false}\n", calls);
    return 0;
}
