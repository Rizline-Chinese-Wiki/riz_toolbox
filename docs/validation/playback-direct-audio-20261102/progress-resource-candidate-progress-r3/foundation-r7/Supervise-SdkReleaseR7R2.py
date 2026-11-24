"""Durable WMI-owned supervisor: capture exact child identity and its actual OS exit."""
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
RUNNER = O / 'Complete-SdkReleaseR7R2.py'
EXPECTED_RUNNER_SHA256 = 'a14ee8e684eef1059ff18eabcc7490ecc91e8e4546863cacda6e4d9bffeda47f'
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
K = ctypes.WinDLL('kernel32', use_last_error=True)
K.GetCurrentProcess.restype = wintypes.HANDLE
K.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
K.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME)]


def created(handle):
    a, b, c, d = (wintypes.FILETIME() for _ in range(4))
    assert K.GetProcessTimes(handle, ctypes.byref(a), ctypes.byref(b), ctypes.byref(c), ctypes.byref(d))
    return (datetime.datetime(1601, 1, 1, tzinfo=datetime.timezone.utc) + datetime.timedelta(microseconds=((a.dwHighDateTime << 32) + a.dwLowDateTime) // 10)).isoformat()


def save(path, value):
    q = path.with_suffix(path.suffix + '.writing')
    q.write_text(json.dumps(value, indent=2), encoding='utf-8')
    q.replace(path)


assert not (O / 'continuation-r2-supervisor.launch.json').exists(), 'Never duplicate a retained supervisor.'
assert sha(RUNNER) == EXPECTED_RUNNER_SHA256, 'The independently reviewed runner changed.'
inside = wintypes.BOOL()
assert K.IsProcessInJob(K.GetCurrentProcess(), None, ctypes.byref(inside))
result = dict(kind='foundation-r7-sdk-release-r2-exact-process-supervisor', collectionCompleted=False,
    runnerPid=os.getpid(), parentPid=os.getppid(), inJob=bool(inside.value), creationTimeUtc=created(K.GetCurrentProcess()),
    startedAtUtc=now(), supervisor=fact(Path(__file__).resolve()), driver=fact(RUNNER),
    command=['C:/Python314/pythonw.exe', str(RUNNER)], gameExecuted=False, installed=False)
save(O / 'continuation-r2-supervisor.launch.json', result)
try:
    assert not inside.value
    with (O / 'continuation-r2-driver.stdout.log').open('xb') as stdout, (O / 'continuation-r2-driver.stderr.log').open('xb') as stderr:
        process = subprocess.Popen(result['command'], cwd=O, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        result.update(driverPid=process.pid, driverParentPid=os.getpid(), driverCreationTimeUtc=created(wintypes.HANDLE(int(process._handle))))
        save(O / 'continuation-r2-supervisor.progress.json', result)
        result['actualExitCode'] = process.wait()
    result.update(collectionCompleted=True, finishedAtUtc=now(), stdout=fact(O / 'continuation-r2-driver.stdout.log'), stderr=fact(O / 'continuation-r2-driver.stderr.log'))
except BaseException:
    result.update(failure=traceback.format_exc(), finishedAtUtc=now())
finally:
    save(O / 'continuation-r2-os-exit-observation.json', result)
audit = O / 'Audit-FoundationR7Final.py'
if result['collectionCompleted'] and audit.is_file():
    row = dict(command=['C:/Python314/python.exe', str(audit)], script=fact(audit), startedAtUtc=now())
    with (O / 'foundation-final-audit.stdout.log').open('xb') as stdout, (O / 'foundation-final-audit.stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=O, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        row.update(pid=process.pid, creationTimeUtc=created(wintypes.HANDLE(int(process._handle))))
        save(O / 'foundation-final-audit.launch.json', row)
        row['actualExitCode'] = process.wait()
    row.update(finishedAtUtc=now(), stdout=fact(O / 'foundation-final-audit.stdout.log'), stderr=fact(O / 'foundation-final-audit.stderr.log'))
    save(O / 'foundation-final-audit.process-result.json', row)
raise SystemExit(0 if result.get('actualExitCode') == 0 and result['collectionCompleted'] else 1)
