"""Read only the selected game's working directory and an allowlist of launcher keys.
Windows x64 diagnostic; no UI operations, process writes or full environment export.
Usage: Python inspect-launch-environment.py <actual Rizline PID>
"""
import ctypes as c
from ctypes import wintypes as w
import json
import struct
import sys

assert c.sizeof(c.c_void_p) == 8 and sys.platform == "win32"
k = c.WinDLL("kernel32", use_last_error=True); n = c.WinDLL("ntdll")
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]; k.OpenProcess.restype = w.HANDLE
k.CloseHandle.argtypes = [w.HANDLE]
k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
n.NtQueryInformationProcess.argtypes = [w.HANDLE, w.ULONG, c.c_void_p, w.ULONG, c.POINTER(w.ULONG)]
n.NtQueryInformationProcess.restype = c.c_long
handle = k.OpenProcess(0x410, False, int(sys.argv[1]))
if not handle: raise c.WinError(c.get_last_error())
try:
    name = c.create_unicode_buffer(32768); size = w.DWORD(len(name))
    if not k.QueryFullProcessImageNameW(handle, 0, name, c.byref(size)): raise c.WinError(c.get_last_error())
    assert name.value.casefold() == r"D:\SteamLibrary\steamapps\common\Rizline\Rizline.exe".casefold(), "Refusing another process"
    def read(address, length):
        assert 0 < length <= 65536 and address > 65536
        buffer = c.create_string_buffer(length); got = c.c_size_t()
        if not k.ReadProcessMemory(handle, address, buffer, length, c.byref(got)) or got.value != length:
            raise RuntimeError("Selected launch-parameter memory is unavailable")
        return buffer.raw
    basic = c.create_string_buffer(48); returned = w.ULONG()
    if n.NtQueryInformationProcess(handle, 0, basic, len(basic), c.byref(returned)) != 0: raise RuntimeError("Process information unavailable")
    peb = struct.unpack_from("<Q", basic.raw, 8)[0]
    parameters = struct.unpack("<Q", read(peb + 0x20, 8))[0]
    length, maximum, pointer = struct.unpack("<HH4xQ", read(parameters + 0x38, 16))
    assert 0 < length <= maximum <= 8192 and length % 2 == 0
    directory = read(pointer, length).decode("utf-16-le")
    environment = struct.unpack("<Q", read(parameters + 0x80, 8))[0]
    data = bytearray()
    for offset in range(0, 65536, 1024):
        data.extend(read(environment + offset, 1024)); units = data.decode("utf-16-le", errors="replace")
        if "\0\0" in units: break
    else: raise RuntimeError("Environment exceeds diagnostic read limit")
    allowed = {"DOORSTOP_DISABLE", "DOORSTOP_INITIALIZED", "DOORSTOP_INVOKE_DLL_PATH", "DOORSTOP_CORLIB_OVERRIDE", "STEAMAPPID", "STEAMGAMEID"}
    selected = {}
    for entry in units.split("\0\0", 1)[0].split("\0"):
        key, separator, value = entry.partition("=")
        if separator and key.upper() in allowed: selected[key] = value
    print(json.dumps({"pid": int(sys.argv[1]), "executable": name.value, "currentDirectory": directory,
                      "selectedLauncherKeys": selected, "allOtherEnvironmentValuesOmitted": True}, ensure_ascii=False, indent=2))
finally: k.CloseHandle(handle)
