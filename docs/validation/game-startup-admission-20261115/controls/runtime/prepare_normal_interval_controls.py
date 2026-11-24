"""Preserve passed r1, prepare fresh current-source validation for the normal positive interval."""
import ast
import datetime as dt
import hashlib
import json
from pathlib import Path

WORK=Path(__file__).resolve().parent
CACHE=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115')
sha=lambda path:hashlib.sha256(Path(path).read_bytes()).hexdigest()
read=lambda path:json.loads(Path(path).read_text(encoding='utf-8-sig'))


def one(text,old,new):
    if text.count(old)!=1:raise RuntimeError('Normal interval revision seam differs: '+old[:100])
    return text.replace(old,new)


r1=read(CACHE / 'run-r1/result.json');wait=read(CACHE / 'run-r1-supervision/controller-exit.json')
if not(r1['passed'] and wait['controllerExited'] and wait['actualControllerExitCode']==0 and wait['resultSha256']==sha(CACHE / 'run-r1/result.json')):
    raise RuntimeError('Keep original actual r1 terminal before a new revision')
validator=(WORK / 'validate_candidate.py').read_text(encoding='utf-8')
validator=one(validator,"OUT=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r1')","OUT=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r2')")
validator=one(validator,"MODULE=ROOT / 'work/module-manifest-startup-next-20261115'","MODULE=ROOT / 'work/module-manifest-startup-next-20261115/v2'")
validator=one(validator,"for suite in ('Runtime','Hosting','Adapter','Hooks','Gameplay','Module','GameCamera'):","for suite in ('Runtime','Module','GameCamera'):")
supervisor=(WORK / 'supervise_candidate.py').read_text(encoding='utf-8')
supervisor=supervisor.replace('run-r1','run-r2').replace('validate_candidate.py','validate_candidate_r2.py')
supervisor=one(supervisor,"ROOT / 'work/module-manifest-startup-next-20261115/overlay-manifest.json'","ROOT / 'work/module-manifest-startup-next-20261115/v2/overlay-manifest.json'")
installer=(WORK / 'install_candidate.py').read_text(encoding='utf-8')
installer=installer.replace('run-r1','run-r2').replace('install-r1','install-r2').replace('validate_candidate.py','validate_candidate_r2.py').replace('supervise_candidate.py','supervise_candidate_r2.py')
installer=one(installer,"len(result['steps'])==10","len(result['steps'])==6")
outputs={'validate_candidate_r2.py':validator,'supervise_candidate_r2.py':supervisor,'install_candidate_r2.py':installer}
for name,text in outputs.items():
    if (WORK / name).exists():raise RuntimeError('Preserve revision controls')
    ast.parse(text,filename=name)
for name,text in outputs.items():(WORK / name).write_text(text,encoding='utf-8',newline=chr(10))
receipt=dict(schema='normal-positive-interval-controls/v1',preparedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),prepared=True,executed=False,
    priorPassedResultSha256=sha(CACHE / 'run-r1/result.json'),priorOriginalExitSha256=sha(CACHE / 'run-r1-supervision/controller-exit.json'),
    priorActualControllerExitCode=0,priorSourceScope=1172,priorSteps=10,priorFocusedCases=8,
    priorSuites={row['id']:len(row['passNames']) for row in r1['steps'] if row['id'].startswith('suite-')},
    newScope='Fresh six and complete solution, focused tick8 plus Runtime383/Module31/Camera55 against normal-interval source; no r1 product reuse',
    files={name:sha(WORK / name) for name in outputs},installed=False,gameStarted=False,sdkGoalComplete=False)
path=WORK / 'normal-interval-controls.json'
if path.exists():raise RuntimeError('Preserve preparation receipt')
path.write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(prepared=True,executed=False,priorOriginalExit=0,newSteps=6,files=list(outputs))))
