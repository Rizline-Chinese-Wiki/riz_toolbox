"""Prepare r2 receipts/archive gates without altering the interrupted controls."""
import ast
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
targets = [WORK / name for name in ('record_r2_exit.py', 'publish_evidence_r2.py', 'prepare_main_payload_r2.py')]
if any(path.exists() for path in targets): raise RuntimeError('Preserve r2 acceptance controls')


def replace(text, old, new, count=1):
    if text.count(old) != count: raise RuntimeError('Archive control anchor differs: ' + old)
    return text.replace(old, new)


recorder = '''"""Accept only the actual r2 exit observed by its original owning supervisor."""
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
target.write_text(json.dumps(receipt, indent=2) + '\\n', encoding='utf-8')
print(json.dumps(dict(recorded=True, actualControllerExitCode=0, receiptSha256=sha(target))))
'''
publisher = (WORK / 'publish_evidence.py').read_text(encoding='utf-8')
publisher = publisher.replace("/run-r1')", "/run-r2')").replace('run-r1-root-exit.json', 'run-r2-root-exit.json')
publisher = publisher.replace('current-foundation-rebased-evidence/v1', 'current-foundation-rebased-evidence/v2')
publisher = publisher.replace('== 17632', '== 28536')
publisher = replace(publisher, "        and witness['actualToolExitCode'] == 0 and witness['actualToolSession'] == 79606",
    "        and witness['actualControllerExitCode'] == 0 and witness['supervisorPid'] == 34024")
publisher = replace(publisher, "        and witness['actualToolExitCode'] == 0 and witness['resultSha256'] == sha(RUN / 'result.json'), 'Current candidate has not passed naturally')",
    "        and witness['actualControllerExitCode'] == 0 and witness['resultSha256'] == sha(RUN / 'result.json'), 'Current candidate has not passed naturally')")
publisher = replace(publisher, "    for name in ('record_terminal.py', 'run-r2-root-exit.json', 'publish_evidence.py'): copy(WORK / name, 'controls/' + name)",
    "    for name in ('record_r2_exit.py', 'run-r2-root-exit.json', 'publish_evidence_r2.py', 'supervise_candidate_r2.py', 'prepare_r2_acceptance.py', 'prepare_supervised_r2.py'): copy(WORK / name, 'controls/' + name)")
publisher = replace(publisher, "    inventory = {path.relative_to(staging).as_posix():dict(bytes=path.stat().st_size, sha256=sha(path))", """    for name in ('launch.json', 'supervision.json', 'controller-exit.json', 'controller.log', 'supervisor.log'):
        copy(RUN.parent / 'run-r2-supervision' / name, 'supervision/' + name)
    interrupted = RUN.parent / 'run-r1'; old = read(interrupted / 'result.json')
    require(old['fullFrozenInputs'] == state['fullFrozenInputs'] and old['auxiliaryInputs'] == state['auxiliaryInputs'],
        'Interrupted source differs from the new complete graph')
    copy(interrupted / 'result.json', 'history/run-r1/result.json')
    for path in (interrupted / 'controls').iterdir():
        if path.is_file(): copy(path, 'history/run-r1/controls/' + path.name)
    for row in old['steps']:
        log = interrupted / (row['id'] + '.log')
        if log.exists(): copy(log, 'history/run-r1/' + row['id'] + '.log', row.get('logSha256'))
    inventory = {path.relative_to(staging).as_posix():dict(bytes=path.stat().st_size, sha256=sha(path))""")
publisher = replace(publisher, "    # This is source lineage only, not revalidation of earlier test matrices.", """    waited = read(local(archive, 'supervision/controller-exit.json'))
    require(waited['controllerExited'] and waited['actualControllerExitCode'] == 0
        and waited['controllerPid'] == waited['resultControllerPid'] == state['controllerPid'] == 28536
        and waited['supervisorPid'] == 34024 and waited['resultPassed'] and waited['resultInputsUnchanged']
        and waited['resultMainSourceUnchanged'] and waited['exitObservedBy'] == 'original subprocess.Popen.wait process handle'
        and waited['resultSha256'] == witness['resultSha256'] == sha(local(archive, 'run/result.json'))
        and waited['controllerControlSha256'] == sha(local(archive, 'controls/validate_candidate_r2.py'))
        and waited['supervisorControlSha256'] == sha(local(archive, 'controls/supervise_candidate_r2.py'))
        and witness['supervisionSha256'] == sha(local(archive, 'supervision/controller-exit.json')),
        'Original owning supervisor terminal differs')
    old = read(local(archive, 'history/run-r1/result.json'))
    lost = read(local(archive, 'controls/run-r1-interruption-receipt.json'))
    require(not old['passed'] and 'finishedUtc' not in old and not lost['functionalPass']
        and lost['originalToolExitCode'] is None and lost['actualControllerExitCode'] is None
        and not lost['controllerExists'] and not lost['unfinishedChildrenExist']
        and old['fullFrozenInputs'] == state['fullFrozenInputs'] and old['auxiliaryInputs'] == state['auxiliaryInputs']
        and lost['resultSha256'] == sha(local(archive, 'history/run-r1/result.json'))
        and lost['incompleteLogSha256'] == sha(local(archive, 'history/run-r1/suite-Runtime.log')),
        'Interrupted/unknown exit history overclaimed')
    for row in old['steps']:
        if row.get('logSha256'):
            require(sha(local(archive, 'history/run-r1/' + row['id'] + '.log')) == row['logSha256'], 'Interrupted direct log differs')
    # This is source lineage only, not revalidation of earlier test matrices.""")
main_prepare = (WORK / 'prepare_main_payload.py').read_text(encoding='utf-8').replace('controls/run-r1-root-exit.json', 'controls/run-r2-root-exit.json').replace("current['controllerPid'] == 17632", "current['controllerPid'] == 28536")
for path, body in zip(targets, (recorder, publisher, main_prepare)):
    ast.parse(body); path.write_text(body, encoding='utf-8', newline='\n')
print(json.dumps(dict(prepared=True, executed=False, controls={path.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in targets})))
