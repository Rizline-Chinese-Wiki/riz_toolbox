"""Observe the exact WMI driver's kernel creation identity and actual OS exit independently."""
from pathlib import Path
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
import os
import subprocess
import traceback

O = Path(__file__).resolve().parent
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
K = ctypes.WinDLL('kernel32', use_last_error=True)
K.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
K.OpenProcess.restype = wintypes.HANDLE
K.GetCurrentProcess.restype = wintypes.HANDLE
K.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME)]
K.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
K.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
K.CloseHandle.argtypes = [wintypes.HANDLE]
K.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]


def created(handle):
    a, b, c, d = (wintypes.FILETIME() for _ in range(4))
    assert K.GetProcessTimes(handle, ctypes.byref(a), ctypes.byref(b), ctypes.byref(c), ctypes.byref(d))
    return datetime.datetime(1601, 1, 1, tzinfo=datetime.timezone.utc) + datetime.timedelta(microseconds=((a.dwHighDateTime << 32) + a.dwLowDateTime) // 10)


def save(path, value):
    q = path.with_suffix(path.suffix + '.writing')
    q.write_text(json.dumps(value, indent=2), encoding='utf-8')
    q.replace(path)


assert not (O / 'continuation-exit-collector.launch.json').exists(), 'Preserve the previous independent observation.'
inside = wintypes.BOOL()
assert K.IsProcessInJob(K.GetCurrentProcess(), None, ctypes.byref(inside))
result = dict(kind='foundation-r7-independent-exact-continuation-os-exit-observation', collectionCompleted=False,
    runnerPid=os.getpid(), parentPid=os.getppid(), inJob=bool(inside.value), creationTimeUtc=created(K.GetCurrentProcess()).isoformat(),
    startedAtUtc=now(), collector=fact(Path(__file__).resolve()), gameExecuted=False, installed=False)
save(O / 'continuation-exit-collector.launch.json', result)
handle = None
try:
    assert not inside.value
    launch_path = O / 'wmi-continuation.launch.json'
    launch = read(launch_path)
    assert launch['returnedValue'] == 0 and launch['mechanism'] == 'Win32_Process.Create'
    assert 'Complete-FoundationR7.py' in launch['actualCommandLine'] and '--final-freeze' in launch['actualCommandLine']
    handle = K.OpenProcess(0x00100000 | 0x1000, False, launch['pid'])
    assert handle, 'The exact driver no longer has an observable kernel handle.'
    expected = datetime.datetime.fromisoformat(launch['creationDate']).astimezone(datetime.timezone.utc)
    actual = created(handle)
    assert expected == actual, 'PID was reused; refuse to observe another process.'
    result.update(driverPid=launch['pid'], driverCreationTimeUtc=actual.isoformat(), driverCommand=launch['actualCommandLine'], wmiLaunch=fact(launch_path))
    save(O / 'continuation-exit-collector.progress.json', result)
    while True:
        state = K.WaitForSingleObject(handle, 5000)
        if state == 0:
            break
        assert state == 258, 'Kernel wait failed.'
        result['lastObservedAliveAtUtc'] = now()
        save(O / 'continuation-exit-collector.progress.json', result)
    exit_code = wintypes.DWORD()
    assert K.GetExitCodeProcess(handle, ctypes.byref(exit_code))
    result.update(collectionCompleted=True, actualExitCode=exit_code.value, driverExitedAtObservedUtc=now())
except BaseException:
    result['failure'] = traceback.format_exc()
finally:
    if handle:
        K.CloseHandle(handle)
    result['finishedAtUtc'] = now()
    save(O / 'continuation-os-exit-observation.json', result)
audit = O / 'Audit-FoundationR7.py'
if result['collectionCompleted'] and audit.is_file():
    command = ['C:/Python314/python.exe', str(audit)]
    row = dict(command=command, startedAtUtc=now(), script=fact(audit))
    with (O / 'foundation-continuation-audit.stdout.log').open('xb') as stdout, (O / 'foundation-continuation-audit.stderr.log').open('xb') as stderr:
        process = subprocess.Popen(command, cwd=O, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        row.update(pid=process.pid, creationTimeUtc=created(wintypes.HANDLE(int(process._handle))).isoformat())
        save(O / 'foundation-continuation-audit.launch.json', row)
        row['actualExitCode'] = process.wait()
    row.update(finishedAtUtc=now(), stdout=fact(O / 'foundation-continuation-audit.stdout.log'), stderr=fact(O / 'foundation-continuation-audit.stderr.log'))
    save(O / 'foundation-continuation-audit.process-result.json', row)
raise SystemExit(0 if result['collectionCompleted'] else 1)
