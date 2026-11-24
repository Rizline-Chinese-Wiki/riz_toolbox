/* Modified for RizToolbox, 2026-10-24: shared production native init call
 * contract, also compiled directly into its independent offline fixture. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "riz_init_call.h"

static volatile LONG riz_init_invoked;
static __declspec(noreturn) void riz_init_invalid(void) {
    OutputDebugStringW(L"RizToolbox: IL2CPP init call contract rejected repeat, reentry or invalid input.\n");
    RaiseFailFastException(NULL, NULL, 0);
    TerminateProcess(GetCurrentProcess(), 0xE052494E);
    for (;;) { /* TerminateProcess is not declared noreturn. */ }
}

int RizInvokeIl2CppInit(int ready, const char* domain_name,
    RizIl2CppInit original, RizManagedBootstrap bootstrap) {
    if ((ready != 0 && ready != 1) || !original || (ready && !bootstrap)
        || InterlockedCompareExchange(&riz_init_invoked, 1, 0) != 0)
        riz_init_invalid();
    /* No exception handler wraps either call. In particular, native original
     * SEH/C++ unwind cannot enter CoreCLR or be translated into an int result. */
    const int result = original(domain_name);
    if (ready && result != 0) bootstrap();
    return result;
}
