"""Preserve the completed SDK14 original wait and local dependency-feed fixity."""
import hashlib
import json
from pathlib import Path

WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/sdk-startup-candidate-20261115/run-r1')
SUP = RUN.parent / 'run-r1-supervision'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))


def require(ok, message):
    if not ok:
        raise RuntimeError(message)


target = WORK / 'root-exit.json'
feed_target = WORK / 'dependency-feed-fixity.json'
require(not target.exists() and not feed_target.exists(), 'Preserve existing receipts')
state = read(RUN / 'result.json')
waited = read(SUP / 'controller-exit.json')
require(sha(RUN / 'result.json') == '4813c991852dd9cf54d75777cbe693c769df980b867bca14a0e643efc8380a95'
    and state['passed'] and state['checksPassed'] == 14 and state['frozenInputsUnchanged']
    and len(state['frozenInputs']) == state['compilerInputs'] == state['distinctCompilerInputs'] == 1172
    and state['additionalCompilerInputs'] == 0 and state['distributionCompilerInputsOverlap'] == 2
    and len(state['steps']) == 2 and all(s['naturalExit'] and s['exitCode'] == 0 for s in state['steps'])
    and waited['controllerExited'] and waited['actualControllerExitCode'] == 0
    and waited['supervisorPid'] == 34952 and waited['controllerPid'] == waited['resultControllerPid'] == state['controllerPid'] == 15816
    and waited['resultPassed'] and waited['resultInputsUnchanged']
    and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
    and waited['resultSha256'] == sha(RUN / 'result.json')
    and waited['controllerControlSha256'] == sha(WORK / 'run_candidate.py')
    and waited['supervisorControlSha256'] == sha(WORK / 'supervise.py'), 'Original SDK14 terminal evidence differs')
manifest = read(WORK / 'source-manifest.json')
packages = {}
for name, row in state['dependencies'].items():
    package = RUN / 'dependency-feed' / name
    require(sha(package) == row['sha256'] == manifest['dependencyPins'][name]['sha256'], 'Frozen dependency feed differs')
    packages[name] = dict(bytes=package.stat().st_size, sha256=sha(package), originalCachePath=row['original'])
feed = dict(schema='sdk-startup-dependency-feed-fixity/v1', packages=packages,
    observedFrom='completed run-r1/dependency-feed', packageBytesIncluded=False,
    archiveCheckVerifiesRecordedPinsOnly=True, independentRebuildFromArchive=False)
feed_target.write_text(json.dumps(feed, indent=2) + '\n', encoding='utf-8')
receipt = dict(schema='sdk-startup-supervised-root-exit/v1', controllerPid=15816, supervisorPid=34952,
    actualControllerExitCode=0, exitObservedBy=waited['exitObservedBy'], controllerEndedUtc=waited['controllerEndedUtc'],
    resultSha256=sha(RUN / 'result.json'), supervisionSha256=sha(SUP / 'controller-exit.json'),
    rootObservedToolChunk='0d4c4d', rootObservedToolExitCode=0, launchToolExitIsControllerExit=False,
    checksPassed=14, compilerInputs=1172, additionalCompilerInputs=0, distributionCompilerInputsOverlap=2,
    candidateOnly=True, mainApplied=False, fullManagedMatrix=False, sdkGoalComplete=False)
target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(recorded=True, actualControllerExitCode=0, checks=14,
    receiptSha256=sha(target), dependencyFixitySha256=sha(feed_target))))
