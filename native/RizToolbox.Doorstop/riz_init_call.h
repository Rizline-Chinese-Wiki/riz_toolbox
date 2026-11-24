#ifndef RIZ_INIT_CALL_H
#define RIZ_INIT_CALL_H
/* RizToolbox Doorstop integration, 2026-10-24. This is a native call contract
 * after the real pre-init producer decision; ready is not publication proof. */
#ifdef __cplusplus
extern "C" {
#endif
typedef int (__cdecl* RizIl2CppInit)(const char* domain_name);
typedef void (__cdecl* RizManagedBootstrap)(void);

/* Exactly one call per process. original receives the exact domain pointer;
 * original runs once, and bootstrap runs only after a normal nonzero return
 * with ready == 1. The original int and all native exceptions are preserved.
 * Reentry, repeat calls, invalid readiness or required null functions fail fast
 * before a second original invocation. No reset or test-specific behavior. */
int RizInvokeIl2CppInit(int ready, const char* domain_name,
    RizIl2CppInit original, RizManagedBootstrap bootstrap);
#ifdef __cplusplus
}
#endif
#endif
