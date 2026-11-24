"""Preserve a missing-tool incomplete run and prepare the same scope with an owning wait."""
import ast
import ctypes
from ctypes import wintypes
import datetime as dt
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CACHE = Path('D:/riz-build-cache-next/foundation-current-integration-20261114')
RUN = CACHE / 'run-r1'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value: raise RuntimeError(reason)


def missing(pid):
    api = ctypes.WinDLL('kernel32', use_last_error=True)
    api.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]; api.OpenProcess.restype = wintypes.HANDLE
    api.CloseHandle.argtypes = [wintypes.HANDLE]; api.CloseHandle.restype = wintypes.BOOL
    handle = api.OpenProcess(0x1000 | 0x100000, False, pid)
    if handle:
        api.CloseHandle(handle); return False
    error = ctypes.get_last_error()
    require(error == 87, 'Cannot establish absence of original process: ' + str(error))
    return True


def main():
    target = WORK / 'run-r1-interruption-receipt.json'
    controller = WORK / 'validate_candidate_r2.py'; supervisor = WORK / 'supervise_candidate_r2.py'
    require(not any(path.exists() for path in (target, controller, supervisor)), 'Preserve supervision preparation')
    state = read(RUN / 'result.json'); manifest = read(WORK / 'candidate-manifest.json')
    require(state['controllerPid'] == 17632 and not state['passed'] and 'finishedUtc' not in state
        and len(state['steps']) == 73 and state['steps'][-1]['id'] == 'suite-Runtime'
        and not state['steps'][-1]['naturalExit'], 'Original incomplete state differs')
    unfinished = [row['pid'] for row in state['steps'] if not row['naturalExit'] and row.get('pid')]
    require(missing(state['controllerPid']) and all(missing(pid) for pid in unfinished), 'Original process still exists; do not duplicate it')
    require(all(sha(RUN / 'source' / name) == digest for name,digest in {**state['fullFrozenInputs'],**state['auxiliaryInputs']}.items())
        and all(sha(WORK / name) == digest for name,digest in state['controls'].items())
        and all(sha(ROOT / name) == digest for name,digest in manifest['parentFullInputs'].items()), 'Original source/control graph changed')
    receipt = dict(schema='current-foundation-tool-session-loss/v1', observedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
        controllerPid=17632, unfinishedChildPids=unfinished, originalToolSession=79606,
        toolTerminalError='write_stdin failed: Unknown process id 79606', controllerExists=False, unfinishedChildrenExist=False,
        originalToolExitCode=None, actualControllerExitCode=None, functionalPass=False, intentionalInterruption=False,
        reason='Original tool handle unavailable and original controller/unfinished child absent. Exact cause and original exit codes unknown.',
        resultSha256=sha(RUN / 'result.json'), completeFrozenInputsUnchanged=True,
        naturallySuccessfulSteps=sum(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps']),
        interruptedStep='suite-Runtime', incompleteLogSha256=sha(RUN / 'suite-Runtime.log'),
        sourceCount=1171, sameSourceAndFunctionalScopeRequired=True, sdkGoalComplete=False)
    target.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    body = (WORK / 'validate_candidate.py').read_text(encoding='utf-8')
    old = "('candidate-manifest.json', 'prepare_candidate.py', 'validate_candidate.py', 'source-review.md')"
    require(body.count(old) == 1, 'Current validation control anchor differs')
    body = body.replace(old, "('candidate-manifest.json', 'prepare_candidate.py', 'validate_candidate_r2.py', 'source-review.md', 'run-r1-interruption-receipt.json')")
    ast.parse(body); controller.write_text(body, encoding='utf-8', newline='\n')
    # Reuse the already exercised detached/hidden process-handle supervision,
    # changing only its controller arguments and scoped paths.
    base = ROOT / 'work/line-producer-control-next-20261114/supervise_main_r4.py'
    text = base.read_text(encoding='utf-8')
    start = text.index('    argv = [sys.executable,')
    end = text.index('    env = os.environ.copy()', start)
    text = text[:start] + "    argv = [sys.executable, str(WORK / 'validate_candidate_r2.py'), '--out', str(RUN)]\n" + text[end:]
    text = text.replace("META = ROOT / 'work/producer-metadata-history-next-20261114'\n", '')
    text = text.replace('D:/riz-build-cache-next/line-producer-control-20261114/main-r4-supervision', 'D:/riz-build-cache-next/foundation-current-integration-20261114/run-r2-supervision')
    text = text.replace("RUN = SUP.parent / 'main-r4'", "RUN = SUP.parent / 'run-r2'")
    text = text.replace('validate_main_r4.py', 'validate_candidate_r2.py')
    text = text.replace('producer-main-supervised-root/v1', 'current-foundation-supervised-root/v1').replace('producer-main-supervisor-launch/v1', 'current-foundation-supervisor-launch/v1')
    text = text.replace("resultInputsUnchanged=result.get('inputsUnchanged')", "resultInputsUnchanged=result.get('fullFrozenInputsUnchanged'), resultMainSourceUnchanged=result.get('mainSourceUnchanged')")
    ast.parse(text); supervisor.write_text(text, encoding='utf-8', newline='\n')
    print(json.dumps(dict(prepared=True, oldRunFunctionalPass=False, missingController=True, missingUnfinishedChildren=True,
        interruptionReceiptSha256=sha(target), controllerSha256=sha(controller), supervisorSha256=sha(supervisor))))


if __name__ == '__main__': main()
