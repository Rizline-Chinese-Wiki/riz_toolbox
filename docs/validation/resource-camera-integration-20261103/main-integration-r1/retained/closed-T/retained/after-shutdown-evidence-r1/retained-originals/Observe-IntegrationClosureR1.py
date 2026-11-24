"""Observe the existing SDK driver, then collect once; never launch SDK tests."""
from pathlib import Path
import ctypes
import datetime
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time
import traceback

R = Path(__file__).resolve().parent
O = R / 'closure-observer-r1'
O.mkdir(exist_ok=False)
sys.stdout = (O / 'observer.stdout.log').open('x', encoding='utf-8', buffering=1)
sys.stderr = (O / 'observer.stderr.log').open('x', encoding='utf-8', buffering=1)
script = R / 'Collect-IntegrationClosureR1.py'
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
original_digest = digest(script)
spec = importlib.util.spec_from_file_location('stock_stop_closure', script)
closure = importlib.util.module_from_spec(spec)
spec.loader.exec_module(closure)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()

def save(value):
    p = O / 'observer-result.json'
    q = O / 'observer-result.json.writing'
    q.write_text(json.dumps(value, indent=2), encoding='utf-8')
    q.replace(p)

in_job = ctypes.c_int()
kernel = ctypes.WinDLL('kernel32', use_last_error=True)
kernel.GetCurrentProcess.restype = ctypes.c_void_p
kernel.IsProcessInJob.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_int)]
assert kernel.IsProcessInJob(kernel.GetCurrentProcess(), None, ctypes.byref(in_job))
result = {
    'kind': 'observe-original-stock-stop-n3-sdk-then-collect-once',
    'observerPid': os.getpid(), 'inJob': bool(in_job.value),
    'startedAtUtc': now(), 'state': 'waiting-for-original-sdk',
    'sdkDriverPid': 4104, 'collectorSha256': original_digest,
    'sdkTestsLaunched': 0, 'sdkProcessesTerminated': 0,
    'productSourcesChanged': False, 'gameExecuted': False,
}
save(result)
try:
    while True:
        sdk = closure.read(closure.SDK)
        assert sdk['runnerPid'] == 4104
        assert sdk['startedAtUtc'] == '2026-11-02T13:44:07.145028+00:00'
        result.update(lastObservedAtUtc=now(),
                      actualCompleted=sum(row.get('actualExitCode') is not None for row in sdk['rows']),
                      actualFailed=[row['mode'] for row in sdk['rows']
                                    if row.get('actualExitCode') is not None and row.get('passed') is not True],
                      sdkLivePids=sdk.get('livePids'), sdkPending=sdk.get('pending'))
        save(result)
        if sdk.get('finishedAtUtc'):
            if not closure.ready(sdk):
                result.update(state='sdk-terminal-not-accepted', collectorExecuted=False,
                              finishedAtUtc=now())
                save(result)
                sys.exit(2)
            break
        time.sleep(15)
    assert digest(script) == original_digest, 'Collector changed while SDK was running.'
    result.update(state='collecting-completed-sdk', collectorExecuted=True)
    save(result)
    command = ['C:/Python314/python.exe', str(script), '--collect']
    with (O / 'collector.stdout.log').open('wb') as so, (O / 'collector.stderr.log').open('wb') as se:
        p = subprocess.Popen(command, cwd=R, stdout=so, stderr=se,
                             creationflags=subprocess.CREATE_NO_WINDOW)
        result.update(collectorPid=p.pid, collectorCommand=command)
        save(result)
        code = p.wait()
    result.update(state='collected' if code == 0 else 'collection-failed',
                  collectorActualExitCode=code, finishedAtUtc=now())
    save(result)
    sys.exit(code)
except Exception:
    result.update(state='observer-failed', exception=traceback.format_exc(), finishedAtUtc=now())
    save(result)
    traceback.print_exc()
    sys.exit(1)
