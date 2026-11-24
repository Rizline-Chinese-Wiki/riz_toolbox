"""Detached supervisor with an actual wait() on the full validation controller.

The supervisor never infers a zero exit from a result file. It owns the exact
child process handle and records its real exit and creation/argv provenance.
"""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
SUP = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2-supervision')
RUN = SUP.parent / 'run-r2'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save(path, data):
    staging = path.with_suffix(path.suffix + '.pending')
    staging.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staging, path)


def now(): return dt.datetime.now(dt.timezone.utc).isoformat()


def supervise():
    argv = [sys.executable, str(WORK / 'validate_candidate_r2.py'), '--out', str(RUN)]
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    data = dict(schema='current-foundation-supervised-root/v1', supervisorPid=os.getpid(),
        supervisorStartedUtc=now(), argv=argv, controllerControlSha256=sha(WORK / 'validate_candidate_r2.py'),
        supervisorControlSha256=sha(__file__), controllerExited=False, completeSeekPermitted=False)
    with (SUP / 'controller.log').open('xb') as stream:
        child = subprocess.Popen(argv, cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
            stdout=stream, stderr=subprocess.STDOUT)
        data.update(controllerPid=child.pid, childCreatedUtc=now())
        save(SUP / 'supervision.json', data)
        data['actualControllerExitCode'] = child.wait()
    data.update(controllerExited=True, controllerEndedUtc=now(), exitObservedBy='original subprocess.Popen.wait process handle')
    result_path = RUN / 'result.json'
    if result_path.exists():
        result = json.loads(result_path.read_text(encoding='utf-8'))
        data.update(resultSha256=sha(result_path), resultControllerPid=result['controllerPid'],
            resultPassed=result.get('passed'), resultInputsUnchanged=result.get('fullFrozenInputsUnchanged'), resultMainSourceUnchanged=result.get('mainSourceUnchanged'))
    save(SUP / 'supervision.json', data)
    save(SUP / 'controller-exit.json', data)
    return data['actualControllerExitCode']


def launch():
    if SUP.exists() or RUN.exists(): raise RuntimeError('Preserve prior supervised runs')
    SUP.mkdir()
    startup = subprocess.STARTUPINFO(); startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = 0
    # Break away from the desktop tool's job so a chat/app tool reset does not
    # revoke the validation controller or its exact wait handle.
    flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_BREAKAWAY_FROM_JOB
    with (SUP / 'supervisor.log').open('xb') as stream:
        child = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), '--supervise'],
            cwd=ROOT, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT,
            startupinfo=startup, creationflags=flags, close_fds=True)
    data = dict(schema='current-foundation-supervisor-launch/v1', launchedUtc=now(),
        supervisorPid=child.pid, creationFlags=flags, hidden=True,
        supervisorControlSha256=sha(__file__), actualLaunchToolExitIsNotControllerExit=True)
    save(SUP / 'launch.json', data)
    for _ in range(25):
        status = SUP / 'supervision.json'
        if status.exists():
            data['controllerPid'] = json.loads(status.read_text(encoding='utf-8'))['controllerPid']
            break
        if child.poll() is not None: raise RuntimeError('Detached supervisor stopped before child publication')
        time.sleep(.2)
    print(json.dumps(data), flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(supervise() if sys.argv[1:] == ['--supervise'] else launch())
