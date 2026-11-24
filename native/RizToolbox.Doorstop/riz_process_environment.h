#ifndef RIZ_PROCESS_ENVIRONMENT_H
#define RIZ_PROCESS_ENVIRONMENT_H

#include <windows.h>

/* Internal ownership is separate from the user's DOORSTOP_DISABLE switch.
 * Child processes inherit environment strings, but never this process lifetime.
 * Creation time also prevents a later process from borrowing a recycled PID. */
#define RIZ_DOORSTOP_HOOK_PROCESS L"RIZ_TOOLBOX_DOORSTOP_HOOK_PROCESS"
#define RIZ_DOORSTOP_CLR_PROCESS L"RIZ_TOOLBOX_DOORSTOP_CLR_PROCESS"
#define RIZ_DOORSTOP_PROCESS_MARKER_CCH 27

static __inline BOOL RizDoorstopCurrentProcessMarker(
    WCHAR value[RIZ_DOORSTOP_PROCESS_MARKER_CCH]) {
    FILETIME creation, exit_time, kernel_time, user_time;
    DWORD words[3];
    const WCHAR hex[] = L"0123456789abcdef";
    unsigned int word, nibble, cursor = 0;
    if (!GetProcessTimes(GetCurrentProcess(), &creation, &exit_time,
                         &kernel_time, &user_time))
        return FALSE;
    words[0] = GetCurrentProcessId();
    words[1] = creation.dwHighDateTime;
    words[2] = creation.dwLowDateTime;
    for (word = 0; word < 3; ++word) {
        for (nibble = 0; nibble < 8; ++nibble)
            value[cursor++] = hex[(words[word] >> (28 - 4 * nibble)) & 15];
        if (word < 2) value[cursor++] = L':';
    }
    value[cursor] = L'\0';
    return TRUE;
}

static __inline BOOL RizDoorstopOwnsProcessMarker(const WCHAR *name) {
    WCHAR actual[RIZ_DOORSTOP_PROCESS_MARKER_CCH];
    WCHAR expected[RIZ_DOORSTOP_PROCESS_MARKER_CCH];
    unsigned int index;
    DWORD length = GetEnvironmentVariableW(name, actual,
                                          RIZ_DOORSTOP_PROCESS_MARKER_CCH);
    if (length != RIZ_DOORSTOP_PROCESS_MARKER_CCH - 1 ||
        !RizDoorstopCurrentProcessMarker(expected))
        return FALSE;
    for (index = 0; index < RIZ_DOORSTOP_PROCESS_MARKER_CCH; ++index)
        if (actual[index] != expected[index]) return FALSE;
    return TRUE;
}

static __inline BOOL RizDoorstopMarkProcessMarker(const WCHAR *name) {
    WCHAR value[RIZ_DOORSTOP_PROCESS_MARKER_CCH];
    return RizDoorstopCurrentProcessMarker(value) &&
           SetEnvironmentVariableW(name, value);
}

#endif
