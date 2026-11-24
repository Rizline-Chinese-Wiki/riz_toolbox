/* RizToolbox Doorstop integration, 2026-10-24. Independent of the C++ CRT. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "riz_preinit.h"

typedef unsigned int (__cdecl* RizProducerBefore)(const void*, const wchar_t*);
static volatile LONG riz_attempted;
static HMODULE riz_helper_owner;
static wchar_t riz_process_path[32768];
static wchar_t riz_helper_path[32768];
static wchar_t riz_dobby_path[32768];

static int riz_join(wchar_t* destination, DWORD prefix_size, const wchar_t* suffix) {
    DWORD index = 0;
    if (prefix_size >= 32768) return 0;
    for (; index < prefix_size; ++index) destination[index] = riz_process_path[index];
    for (DWORD source = 0; suffix[source]; ++source) {
        if (index >= 32767) return 0;
        destination[index++] = suffix[source];
    }
    destination[index] = L'\0';
    return 1;
}
static void riz_unknown(void) {
    OutputDebugStringW(L"RizToolbox: native first-install outcome unknown; original IL2CPP init will not execute.\n");
    RaiseFailFastException(NULL, NULL, 0);
    TerminateProcess(GetCurrentProcess(), 0xE052495A);
}
int RizBeforeIl2CppInit(const void* original_init) {
    if (InterlockedCompareExchange(&riz_attempted, 1, 0) != 0) {
        riz_unknown(); return 0;
    }
    const DWORD length = GetModuleFileNameW(NULL, riz_process_path, 32768);
    if (!original_init || length == 0 || length >= 32768) return 0;
    DWORD prefix_size = length;
    while (prefix_size && riz_process_path[prefix_size - 1] != L'\\'
        && riz_process_path[prefix_size - 1] != L'/') --prefix_size;
    if (!prefix_size || !riz_join(riz_helper_path, prefix_size,
            L"BepInEx\\plugins\\RizToolbox\\RizToolbox.JudgeContext.dll")
        || !riz_join(riz_dobby_path, prefix_size, L"BepInEx\\core\\dobby.dll")) return 0;
    riz_helper_owner = LoadLibraryExW(riz_helper_path, NULL,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!riz_helper_owner) {
        OutputDebugStringW(L"RizToolbox: pre-init helper unavailable; starting game without framework.\n");
        return 0;
    }
    union { FARPROC symbol; RizProducerBefore invoke; } before;
    before.symbol = GetProcAddress(riz_helper_owner, "RizFirstInstall_BeforeIl2CppInit");
    if (!before.symbol) {
        OutputDebugStringW(L"RizToolbox: pre-init producer export unavailable; starting game without framework.\n");
        return 0;
    }
    const unsigned int result = before.invoke(original_init, riz_dobby_path);
    if (result == 0) {
        OutputDebugStringW(L"RizToolbox: native profile declined before game writes; starting without framework.\n");
        return 0;
    }
    if (result != 1) { riz_unknown(); return 0; }
    return 1;
}
