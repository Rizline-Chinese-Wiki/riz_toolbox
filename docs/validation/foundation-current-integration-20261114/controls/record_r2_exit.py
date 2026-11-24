"""Accept only the actual r2 exit observed by its original owning supervisor."""
import hashlib
import json
from pathlib import Path
WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2')
SUP = RUN.parent / 'run-r2-supervision'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))
target = WORK / 'run-r2-root-exit.json'
if target.exists(): raise RuntimeError('Preserve supervised root receipts')
state = read(RUN / 'result.json'); waited = read(SUP / 'controller-exit.json')
if not (waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == state['controllerPid'] == 28536
        and waited['supervisorPid'] == 34024
        and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and waited['resultSha256'] == sha(RUN / 'result.json')
        and waited['resultPassed'] and waited['resultInputsUnchanged'] and waited['resultMainSourceUnchanged']
        and waited['controllerControlSha256'] == sha(WORK / 'validate_candidate_r2.py')
        and waited['supervisorControlSha256'] == sha(WORK / 'supervise_candidate_r2.py')):
    raise RuntimeError('Original supervised current exit is not successfully terminal')
if not (state['passed'] and state['fullFrozenInputsUnchanged'] and state['mainSourceUnchanged']
        and len(state['steps']) == 95 and all(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps'])
        and all(sha(RUN / 'source' / name) == digest for name,digest in {**state['fullFrozenInputs'],**state['auxiliaryInputs']}.items())
        and all(sha(WORK / name) == digest for name,digest in state['controls'].items())):
    raise RuntimeError('Current successful scope/source differs')
receipt = dict(schema='current-foundation-supervised-root-exit/v1', controllerPid=28536, supervisorPid=34024,
    actualControllerExitCode=0, resultSha256=sha(RUN / 'result.json'),
    supervisionSha256=sha(SUP / 'controller-exit.json'), exitObservedBy=waited['exitObservedBy'],
    launchToolChunk='60a94e', launchToolExitCode=0, launchToolExitIsControllerExit=False,
    functionalPass=True, mainApplied=False, sdkGoalComplete=False)
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(recorded=True, actualControllerExitCode=0, receiptSha256=sha(target))))
