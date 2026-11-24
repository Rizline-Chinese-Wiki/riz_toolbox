#ifndef RIZ_PREINIT_H
#define RIZ_PREINIT_H
#include "riz_init_call.h"
/* Called synchronously from the fixed Doorstop IL2CPP wrapper, outside DllMain.
 * 0: known disabled without game writes; 1: native producer ready.
 * Unknown or repeated initialization never returns to the original init. */
int RizBeforeIl2CppInit(const void* original_init);
#endif
