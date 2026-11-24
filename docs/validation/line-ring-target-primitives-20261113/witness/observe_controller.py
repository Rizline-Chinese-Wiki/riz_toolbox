"""Observe the original controller handle; never infer its process exit from JSON."""
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import sys

WORK = Path(__file__).resolve().parent
PID = 41520
CREATED = dt.datetime.fromisoformat('2026-11-13T13:26:05.624595+08:00').astimezone(dt.timezone.utc)
LAUNCH_SHA = '33c7728e3a8a59aa1e10f81c63c5842e4b6344a04a558ccef54efdbee8ce3c7c'
VALIDATOR_SHA = 'fd9d4dc720395efe435760c214983afd2b0067395d1463e94039cf0c74783b73'
REPORT = Path('D:/riz-build-cache-next/line-ring-target-primitives-integration-20261113/main-r1/result.json')
OUTPUT = WORK / 'controller-exit-observation.json'

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()

def save(record):
    temp = OUTPUT.with_suffix('.tmp')
    temp.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    os.replace(temp, OUTPUT)

def main():
    assert not OUTPUT.exists(), 'Never overwrite an exit observation.'
    assert sha(WORK / 'root-launch.json') == LAUNCH_SHA
    assert sha(WORK / 'validate_main.py') == VALIDATOR_SHA
    launch = json.loads((WORK / 'root-launch.json').read_text(encoding='utf-8-sig'))
    assert launch['pid'] == PID and launch['scriptSha256'] == VALIDATOR_SHA
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = (wintypes.HANDLE, ctypes.POINTER(wintypes.FILETIME),
                                      ctypes.POINTER(wintypes.FILETIME), ctypes.POINTER(wintypes.FILETIME),
                                      ctypes.POINTER(wintypes.FILETIME))
    kernel.GetProcessTimes.restype = wintypes.BOOL
    kernel.GetExitCodeProcess.argtypes = (wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD))
    kernel.GetExitCodeProcess.restype = wintypes.BOOL
    kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    handle = kernel.OpenProcess(0x1000 | 0x100000, False, PID)
    assert handle, 'Cannot open the recorded controller handle.'
    try:
        created, exited, kernel_time, user_time = (wintypes.FILETIME() for _ in range(4))
        assert kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited),
                                      ctypes.byref(kernel_time), ctypes.byref(user_time))
        ticks = (created.dwHighDateTime << 32) | created.dwLowDateTime
        actual_created = dt.datetime(1601, 1, 1, tzinfo=dt.timezone.utc) + dt.timedelta(microseconds=ticks // 10)
        assert actual_created == CREATED, 'PID was reused; do not observe another process.'
        exit_code = wintypes.DWORD()
        assert kernel.GetExitCodeProcess(handle, ctypes.byref(exit_code)) and exit_code.value == 259
        state = {'schema': 'original-controller-handle-exit/v1', 'observerPid': os.getpid(),
                 'observerSha256': sha(__file__), 'observedPid': PID, 'observedCreatedUtc': actual_created.isoformat(),
                 'observedLaunchRecordSha256': LAUNCH_SHA, 'observedValidatorSha256': VALIDATOR_SHA,
                 'observedAliveAtUtc': now(), 'handleKeptUntilExit': True, 'finishedUtc': None,
                 'actualExitCode': None, 'resultSha256AtExit': None, 'acceptedThisBatch': False,
                 'sdkGoalComplete': False, 'completeSeekPermitted': False}
        save(state)
        while True:
            status = kernel.WaitForSingleObject(handle, 2000)
            if status == 0:
                break
            assert status == 258, 'Controller wait failed.'
        assert kernel.GetExitCodeProcess(handle, ctypes.byref(exit_code))
        state['actualExitCode'] = exit_code.value
        state['finishedUtc'] = now()
        state['resultSha256AtExit'] = sha(REPORT)
        state['validatorUnchangedAtExit'] = sha(WORK / 'validate_main.py') == VALIDATOR_SHA
        state['launchRecordUnchangedAtExit'] = sha(WORK / 'root-launch.json') == LAUNCH_SHA
        save(state)
        print(json.dumps({'observedPid': PID, 'actualExitCode': exit_code.value,
                          'finishedUtc': state['finishedUtc']}), flush=True)
        return 0
    finally:
        kernel.CloseHandle(handle)

if __name__ == '__main__':
    sys.exit(main())
