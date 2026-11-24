"""Keep the original Windows controller handle until its natural exit.

Start after validate_main.py has created result.json. The explicit script SHA
prevents a later process with the same PID from being treated as this run.
"""
import argparse
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import json
from pathlib import Path
import time

DEFAULT_RUN = Path('D:/riz-build-cache-next/line-camera-scene-integration-20261113/main-r1')
DEFAULT_SCRIPT = Path(__file__).resolve().with_name('validate_main.py')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run', type=Path, default=DEFAULT_RUN)
    parser.add_argument('--controller-script', type=Path, default=DEFAULT_SCRIPT)
    parser.add_argument('--script-sha256', required=True)
    opt = parser.parse_args()
    run = opt.run.resolve()
    script = opt.controller_script.resolve()
    receipt = run / 'controller-handle-exit.json'
    require(not receipt.exists() and sha(script) == opt.script_sha256,
            'Controller receipt already exists or script differs')
    result = run / 'result.json'
    deadline = time.monotonic() + 120
    while not result.exists():
        require(time.monotonic() < deadline, 'Controller did not create result.json')
        time.sleep(0.2)
    state = json.loads(result.read_text(encoding='utf-8-sig'))
    pid = int(state['controllerPid'])
    require(state['controlsBefore'][str(script)] == opt.script_sha256,
            'Result does not name the exact running controller script')
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = (wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4))
    kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.GetExitCodeProcess.argtypes = (wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD))
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    handle = kernel.OpenProcess(0x100000 | 0x1000, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        times = [wintypes.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(handle, *map(ctypes.byref, times)):
            raise ctypes.WinError(ctypes.get_last_error())
        created_ticks = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
        created = dt.datetime(1601, 1, 1, tzinfo=dt.timezone.utc) + dt.timedelta(microseconds=created_ticks // 10)
        started = dt.datetime.fromisoformat(state['startedUtc'])
        require(0 <= (started - created).total_seconds() < 600
                and kernel.WaitForSingleObject(handle, 0) == 258,
                'PID creation time or live handle differs from original controller')
        record = {
            'schema': 'original-controller-handle-exit/v1',
            'controllerPid': pid, 'createdUtc': created.isoformat(),
            'observedAliveUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
            'controllerScript': str(script), 'controllerScriptSha256': opt.script_sha256,
            'observerSha256': sha(__file__), 'handleKeptUntilExit': True,
            'sdkGoalComplete': False, 'completeSeekPermitted': False,
        }
        print(json.dumps({'observedOriginalController': pid, 'createdUtc': record['createdUtc']}), flush=True)
        while True:
            wait = kernel.WaitForSingleObject(handle, 1000)
            if wait == 0:
                break
            require(wait == 258, 'WaitForSingleObject failed')
        exit_code = wintypes.DWORD()
        if not kernel.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
            raise ctypes.WinError(ctypes.get_last_error())
        record.update(actualExitCode=exit_code.value,
                      finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                      resultSha256AtExit=sha(result),
                      controllerScriptUnchanged=sha(script) == opt.script_sha256)
        with receipt.open('x', encoding='utf-8') as stream:
            json.dump(record, stream, ensure_ascii=False, indent=2)
            stream.write('\n')
        print(json.dumps(record), flush=True)
    finally:
        kernel.CloseHandle(handle)


if __name__ == '__main__':
    main()
