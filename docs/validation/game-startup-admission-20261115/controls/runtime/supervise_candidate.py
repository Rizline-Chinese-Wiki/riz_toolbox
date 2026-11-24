"""Keep an original wait handle for the fresh combined startup validation."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

WORK=Path(__file__).resolve().parent;ROOT=WORK.parents[1]
CACHE=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115')
SUP=CACHE / 'run-r1-supervision';RUN=CACHE / 'run-r1'
sha=lambda path:hashlib.sha256(Path(path).read_bytes()).hexdigest()
now=lambda:dt.datetime.now(dt.timezone.utc).isoformat()


def save(path,data):
    pending=Path(str(path)+'.pending');pending.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8');os.replace(pending,path)


def supervise():
    argv=[sys.executable,str(WORK / 'validate_candidate.py')]
    data=dict(schema='combined-startup-supervised-root/v1',supervisorPid=os.getpid(),startedUtc=now(),
        controllerControlSha256=sha(WORK / 'validate_candidate.py'),supervisorControlSha256=sha(__file__),
        argv=argv,controllerExited=False,sdkGoalComplete=False)
    with (SUP / 'controller.log').open('xb') as stream:
        child=subprocess.Popen(argv,cwd=ROOT,stdin=subprocess.DEVNULL,stdout=stream,stderr=subprocess.STDOUT)
        data['controllerPid']=child.pid;data['childCreatedUtc']=now();save(SUP / 'supervision.json',data)
        data['actualControllerExitCode']=child.wait()
    data.update(controllerExited=True,finishedUtc=now(),exitObservedBy='original subprocess.Popen.wait process handle')
    if (RUN / 'result.json').exists():
        result=json.loads((RUN / 'result.json').read_text(encoding='utf-8'))
        data.update(resultSha256=sha(RUN / 'result.json'),resultPassed=result['passed'],resultControllerPid=result['controllerPid'])
    save(SUP / 'supervision.json',data);save(SUP / 'controller-exit.json',data)
    return data['actualControllerExitCode']


def launch():
    if SUP.exists() or RUN.exists():raise RuntimeError('Preserve previous combined startup validation')
    # No process launch until both independent implementation overlays are frozen.
    for path in (WORK / 'overlay-manifest.json',ROOT / 'work/module-manifest-startup-next-20261115/overlay-manifest.json'):
        manifest=json.loads(path.read_text(encoding='utf-8-sig'))
        if not(manifest.get('ready') and manifest.get('frozen')):raise RuntimeError('Startup candidate not ready')
    SUP.mkdir(parents=True)
    startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
    flags=subprocess.DETACHED_PROCESS|subprocess.CREATE_NEW_PROCESS_GROUP|subprocess.CREATE_BREAKAWAY_FROM_JOB
    with (SUP / 'supervisor.log').open('xb') as stream:
        child=subprocess.Popen([sys.executable,str(Path(__file__).resolve()),'--supervise'],cwd=ROOT,
            stdin=subprocess.DEVNULL,stdout=stream,stderr=subprocess.STDOUT,startupinfo=startup,creationflags=flags,close_fds=True)
    data=dict(schema='combined-startup-supervisor-launch/v1',launchedUtc=now(),supervisorPid=child.pid,
        creationFlags=flags,hidden=True,supervisorControlSha256=sha(__file__),launchExitIsNotControllerExit=True)
    save(SUP / 'launch.json',data)
    for _ in range(25):
        if (SUP / 'supervision.json').exists():
            data['controllerPid']=json.loads((SUP / 'supervision.json').read_text(encoding='utf-8'))['controllerPid'];break
        if child.poll() is not None:raise RuntimeError('Supervisor ended before controller publication')
        time.sleep(.2)
    print(json.dumps(data),flush=True);return 0


if __name__=='__main__':raise SystemExit(supervise() if sys.argv[1:]==['--supervise'] else launch())
