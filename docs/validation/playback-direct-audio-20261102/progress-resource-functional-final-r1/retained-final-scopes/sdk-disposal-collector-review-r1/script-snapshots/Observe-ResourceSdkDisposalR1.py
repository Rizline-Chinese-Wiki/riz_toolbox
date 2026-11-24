"""Persistently wait for the one real SDK terminal result, then collect without applying files."""
from pathlib import Path
from ctypes import wintypes
import ctypes
import datetime
import hashlib
import json
import os
import subprocess
import time
import traceback

U = Path(__file__).resolve().parent
O = U / 'sdk-disposal-observer-r1'
assert not O.exists()
O.mkdir()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
fact = lambda p: dict(path=str(p), sha256=hashlib.sha256(p.read_bytes()).hexdigest(), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(value):
    p = O / 'observer-result.json'
    q = p.with_suffix('.writing')
    q.write_text(json.dumps(value, indent=2), encoding='utf-8'); q.replace(p)


k = ctypes.WinDLL('kernel32', use_last_error=True)
k.GetCurrentProcess.restype = wintypes.HANDLE
k.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
inside = wintypes.BOOL()
assert k.IsProcessInJob(k.GetCurrentProcess(), None, ctypes.byref(inside))
result = dict(kind='observe-actual-corrected-sdk-disposal-terminal', state='waiting', passed=False,
    runnerPid=os.getpid(), parentPid=os.getppid(), inJob=bool(inside.value), startedAtUtc=now(),
    gameExecuted=False, installed=False, mainApplied=False, collectorExecuted=False)
try:
    assert not inside.value
    collector = U / 'Collect-ResourceSdkDisposalR1.py'
    result['collector'] = fact(collector)
    while True:
        p = U / 'sdk-current-dispose-r1/test-result.json'
        state = read(p)
        result.update(observedAtUtc=now(), observedDriverPid=state.get('runnerPid'), sdkTerminal=bool(state.get('finishedAtUtc')))
        if state.get('finishedAtUtc'):
            result['sdkResult'] = fact(p)
            if not state.get('passed'):
                result.update(state='actual-sdk-failure-retained', passed=False, collectorExecuted=False)
                save(result); break
            assert fact(collector) == result['collector']
            with (O / 'collector.stdout.log').open('xb') as stdout, (O / 'collector.stderr.log').open('xb') as stderr:
                process = subprocess.Popen(['C:/Python314/python.exe', '-B', str(collector)], cwd=U,
                    stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
                result.update(collectorExecuted=True, collectorPid=process.pid)
                save(result)
                result['collectorActualExitCode'] = process.wait()
            result.update(collectorStdout=fact(O / 'collector.stdout.log'), collectorStderr=fact(O / 'collector.stderr.log'))
            if result['collectorActualExitCode'] == 0:
                result['closure'] = fact(U / 'functional-combined-after-disposal-r1/test-result.json')
                result.update(state='collected', passed=True)
            else:
                result.update(state='collector-failed-retained', passed=False)
            save(result); break
        save(result)
        time.sleep(15)
except BaseException:
    result.update(state='observer-error-retained', passed=False, failure=traceback.format_exc())
    save(result)
finally:
    result['finishedAtUtc'] = now(); save(result)
