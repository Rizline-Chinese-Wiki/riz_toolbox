"""Record only the original SDK supervisor's actual successful Popen.wait."""
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
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(recorded=True, actualControllerExitCode=0, receiptSha256=sha(target))))
