"""Keep a real Windows handle to the already running original controller."""
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import json
from pathlib import Path

WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/line-ring-target-provider-integration-20261113/main-r1')
PID = 40232
EXPECTED_SCRIPT = 'b397c10117d124db254b4c33a68d99577c31323fc4385093f7ef2a58ee99a9d8'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    record = WORK / 'controller-handle-exit.json'
    if record.exists():
        raise RuntimeError('Preserve original handle observation')
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = (wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4))
    kernel.GetExitCodeProcess.argtypes = (wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD))
    kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    handle = kernel.OpenProcess(0x100000 | 0x1000, False, PID)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        times = [wintypes.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(handle, *map(ctypes.byref, times)):
            raise ctypes.WinError(ctypes.get_last_error())
        creation = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
        created = dt.datetime(1601, 1, 1, tzinfo=dt.timezone.utc) + dt.timedelta(microseconds=creation // 10)
        state = json.loads((RUN / 'result.json').read_text(encoding='utf-8'))
        delta = (dt.datetime.fromisoformat(state['startedUtc']) - created).total_seconds()
        if (state['controllerPid'] != PID or not 0 <= delta < 20
                or sha(WORK / 'validate.py') != EXPECTED_SCRIPT
                or kernel.WaitForSingleObject(handle, 0) != 258):
            raise RuntimeError('Original controller identity is not live and exact')
        output = {'schema': 'original-controller-handle-exit/v1', 'controllerPid': PID,
                  'createdUtc': created.isoformat(), 'observedAliveUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                  'controllerScriptSha256': EXPECTED_SCRIPT, 'observerSha256': sha(__file__),
                  'handleKeptUntilExit': True, 'sdkGoalComplete': False, 'completeSeekPermitted': False}
        print(json.dumps(output), flush=True)
        while True:
            wait = kernel.WaitForSingleObject(handle, 1000)
            if wait == 0:
                break
            if wait != 258:
                raise ctypes.WinError(ctypes.get_last_error())
        code = wintypes.DWORD()
        if not kernel.GetExitCodeProcess(handle, ctypes.byref(code)):
            raise ctypes.WinError(ctypes.get_last_error())
        output.update(actualExitCode=code.value, finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                      resultSha256AtExit=sha(RUN / 'result.json'),
                      controllerScriptUnchanged=sha(WORK / 'validate.py') == EXPECTED_SCRIPT)
        with record.open('x', encoding='utf-8') as stream:
            stream.write(json.dumps(output, indent=2) + '\n')
        print(json.dumps(output), flush=True)
    finally:
        kernel.CloseHandle(handle)


if __name__ == '__main__':
    main()
