"""Prepare exact supervised SDK terminal/archive controls without running them."""
import ast
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
OLD = ROOT / 'work/sdk-current-hold-snapshot-20261114'
targets = [WORK / 'record_exit.py', WORK / 'publish_evidence.py']
if any(path.exists() for path in targets): raise RuntimeError('Preserve SDK evidence controls')


def replace(body, old, new, count=1):
    if body.count(old) != count: raise RuntimeError('SDK evidence anchor differs: ' + old)
    return body.replace(old, new)


recorder = '''"""Record only the original SDK supervisor's actual successful Popen.wait."""
import hashlib
import json
from pathlib import Path
WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/sdk-current-integration-20261114/run-r1')
SUP = RUN.parent / 'run-r1-supervision'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))
target = WORK / 'root-exit.json'
if target.exists(): raise RuntimeError('Preserve SDK root receipts')
state = read(RUN / 'result.json'); waited = read(SUP / 'controller-exit.json')
if not (waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == state['controllerPid'] == 35948
        and waited['supervisorPid'] == 15176 and waited['resultPassed'] and waited['resultInputsUnchanged']
        and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and waited['resultSha256'] == sha(RUN / 'result.json')
        and waited['controllerControlSha256'] == sha(WORK / 'run_candidate.py')
        and waited['supervisorControlSha256'] == sha(WORK / 'supervise.py')
        and state['passed'] and state['checksPassed'] == 14 and state['frozenInputsUnchanged']
        and len(state['frozenInputs']) == 1171 and len(state['steps']) == 2
        and all(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps'])):
    raise RuntimeError('Exact SDK14 run is not successfully terminal')
receipt = dict(schema='sdk-current-supervised-root-exit/v1', controllerPid=35948, supervisorPid=15176,
    actualControllerExitCode=0, exitObservedBy=waited['exitObservedBy'], resultSha256=sha(RUN / 'result.json'),
    supervisionSha256=sha(SUP / 'controller-exit.json'), launchToolChunk='a2fddf',
    launchToolExitCode=0, launchToolExitIsControllerExit=False,
    checksPassed=14, fullManagedMatrix=False, mainApplied=False, sdkGoalComplete=False)
target.write_text(json.dumps(receipt, indent=2) + '\\n', encoding='utf-8')
print(json.dumps(dict(recorded=True, actualControllerExitCode=0, receiptSha256=sha(target))))
'''
publisher = (OLD / 'publish_evidence.py').read_text(encoding='utf-8')
publisher = publisher.replace('sdk-current-hold-20261114/run-r3', 'sdk-current-integration-20261114/run-r1')
publisher = publisher.replace('docs/validation/sdk-current-hold-20261114', 'docs/validation/sdk-current-integration-20261114')
publisher = publisher.replace('sdk-candidate-distribution-evidence/v1', 'sdk-candidate-distribution-evidence/v2')
publisher = publisher.replace('run-r3-root-exit.json', 'root-exit.json').replace('compilerInputs=939', 'compilerInputs=1171')
publisher = publisher.replace("state['compilerInputs'] == 939", "state['compilerInputs'] == 1171").replace("len(state['frozenInputs']) == 939", "len(state['frozenInputs']) == 1171")
publisher = publisher.replace("witness['actualToolExitCode']", "witness['actualControllerExitCode']")
publisher = replace(publisher, "    for name in ('root-exit.json', 'publish_evidence.py', 'source-review.md'): copy(WORK / name, 'controls/' + name)",
    "    for name in ('root-exit.json', 'record_exit.py', 'publish_evidence.py', 'supervise.py', 'prepare_controls.py', 'prepare_evidence_controls.py', 'launch-preflight-failure.json'): copy(WORK / name, 'controls/' + name)")
start = publisher.index("    for label in ('run-r1', 'run-r2'):")
end = publisher.index("    summary = dict(", start)
publisher = publisher[:start] + '''    for name in ('launch.json', 'supervision.json', 'controller-exit.json', 'controller.log', 'supervisor.log'):
        copy(RUN.parent / 'run-r1-supervision' / name, 'supervision/' + name)
''' + publisher[end:]
publisher = replace(publisher, "    require(not state['fullManagedMatrix'] and not state['sdkGoalComplete'] and not state['runtimeGameVerification']", """    waited = read(local(base, 'supervision/controller-exit.json'))
    require(waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == state['controllerPid'] == 35948
        and waited['supervisorPid'] == witness['supervisorPid'] == 15176
        and waited['resultPassed'] and waited['resultInputsUnchanged']
        and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and waited['resultSha256'] == witness['resultSha256'] == sha(local(base, 'run/result.json'))
        and waited['controllerControlSha256'] == sha(local(base, 'controls/run_candidate.py'))
        and waited['supervisorControlSha256'] == sha(local(base, 'controls/supervise.py'))
        and witness['supervisionSha256'] == sha(local(base, 'supervision/controller-exit.json')), 'Original SDK wait differs')
    manifest = read(local(base, 'controls/source-manifest.json'))
    require(state['sourceManifestSha256'] == sha(local(base, 'controls/source-manifest.json'))
        and state['frozenInputs'] == manifest['expectedInputs'] and state['baseCurrentControllerPid'] == 28536,
        'SDK source differs from the current rebased graph')
    preflight = read(local(base, 'controls/launch-preflight-failure.json'))
    require(preflight['actualToolExitCode'] == 1 and not preflight['controllerCreated'], 'Prelaunch failure misclassified')
    require(not state['fullManagedMatrix'] and not state['sdkGoalComplete'] and not state['runtimeGameVerification']""")
for path, body in zip(targets, (recorder, publisher)):
    ast.parse(body); path.write_text(body, encoding='utf-8', newline='\n')
print(json.dumps(dict(prepared=True, executed=False, controls={path.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in targets})))
