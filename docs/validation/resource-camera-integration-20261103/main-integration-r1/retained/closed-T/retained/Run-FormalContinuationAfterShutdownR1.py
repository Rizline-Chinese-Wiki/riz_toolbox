"""Run only the 12 unfinished T SDK modes after the verified machine shutdown.

The six original terminal successes stay in formal-r2. No original file is
modified, no timed-out or shutdown exit is fabricated, and no process is killed.
"""
import json
import os
import subprocess
import sys
import time
import traceback
from continuation_common import *


def main():
    assert not (O / 'test-result.json').exists(), 'Preserve any existing continuation attempt.'
    sys.stdout = (O / 'driver.stdout.log').open('x', encoding='utf-8', buffering=1)
    sys.stderr = (O / 'driver.stderr.log').open('x', encoding='utf-8', buffering=1)
    state = {'kind': 'same-frozen-T-formal-sdk-after-shutdown-continuation', 'passed': False,
             'runnerPid': os.getpid(), 'pythonExecutable': sys.executable, 'inJob': in_job(),
             'startedAtUtc': now(), 'parallelLimit': 3, 'rows': [], 'pending': REMAINING[:],
             'livePids': [], 'state': 'verifying-frozen-package',
             'originalRun': fact(OLD / 'test-result.json'), 'originalDriverPid': 4104,
             'originalCompletedModesNotRerun': list(BASE_CHECKS), 'remainingModes': REMAINING,
             'processesTerminated': 0, 'gameExecuted': False, 'installed': False,
             'productionAdmission': False, 'fullBCompleted': False}
    save(O / 'test-result.json', state)
    jobs = []
    try:
        assert state['inJob'] is False, 'WMI runner must be independent of a tool job.'
        assert Path(sys.executable) == Path('C:/Python314/pythonw.exe')
        preflight = verify_preserved()
        frozen = verify_frozen()
        save(O / 'frozen-before.json', frozen)
        state.update(sourceManifest=frozen['sourceManifest'], products=frozen['products'],
                     nativeInputs=frozen['nativeInputs'], preflight=fact(E / 'preflight-result-r3.json'),
                     frozenBefore=fact(O / 'frozen-before.json'), powerAtStart=power_status(),
                     script=fact(Path(__file__)), commonScript=fact(O / 'continuation_common.py'),
                     state='running-unfinished-modes')
        env = {**os.environ, 'RIZ_INITIALIZER_TRACE': '1', 'DOTNET_NOLOGO': '1',
               'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_CLI_UI_LANGUAGE': 'en'}
        pending = REMAINING[:]
        while pending or jobs:
            while pending and len(jobs) < 3:
                mode = pending.pop(0)
                command = command_for(mode)
                so = (O / (mode + '.stdout.log')).open('xb')
                se = (O / (mode + '.stderr.log')).open('xb')
                p = subprocess.Popen(command, cwd=S, env=env, stdout=so, stderr=se,
                                     creationflags=subprocess.CREATE_NO_WINDOW)
                row = {'mode': mode, 'command': command, 'pid': p.pid,
                       'startedAtUtc': now(), 'actualExitCode': None, 'passed': False,
                       'inJob': in_job(p._handle)}
                assert row['inJob'] is False
                state['rows'].append(row)
                jobs.append((p, so, se, row))
                save(O / (mode + '.launch.json'), row)
                print(json.dumps({'launched': mode, 'pid': p.pid, 'inJob': row['inJob']}), flush=True)
            for job in jobs[:]:
                p, so, se, row = job
                code = p.poll()
                if code is None:
                    continue
                so.close()
                se.close()
                mode = row['mode']
                row.update(actualExitCode=code, finishedAtUtc=now(),
                           stdout=fact(O / (mode + '.stdout.log')), stderr=fact(O / (mode + '.stderr.log')))
                (O / (mode + '.exit.txt')).write_text(str(code), encoding='utf-8')
                try:
                    row['actual'] = json.loads((O / (mode + '.stdout.log')).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
                except (ValueError, IndexError):
                    pass
                row['passed'] = code == 0 and row.get('actual', {}).get('passed') is True and row['actual'].get('mode') == mode
                save(O / (mode + '.result.json'), row)
                jobs.remove(job)
                print(json.dumps({'exited': mode, 'actualExitCode': code, 'passed': row['passed']}), flush=True)
            state.update(pending=pending[:], livePids=[job[0].pid for job in jobs],
                         lastObservedAliveUtc=now(), powerLastObserved=power_status())
            save(O / 'test-result.json', state)
            if jobs:
                time.sleep(3)
        after = verify_frozen()
        verify_preserved()
        save(O / 'frozen-after.json', after)
        assert state['script']['sha256'] == sha(Path(__file__))
        assert state['commonScript']['sha256'] == sha(O / 'continuation_common.py')
        for row in state['rows']:
            verify_row(row, O)
        state.update(inputsUnchanged=True, originalEvidenceUnchanged=True,
                     frozenAfter=fact(O / 'frozen-after.json'), processes=len(state['rows']),
                     checks=sum(row.get('actual', {}).get('checks', 0) for row in state['rows']),
                     passed=len(state['rows']) == 12 and all(row['passed'] for row in state['rows']),
                     finishedAtUtc=now(), state='finished')
        save(O / 'test-result.json', state)
        return 0 if state['passed'] else 1
    except BaseException:
        state.update(exception=traceback.format_exc(), state='runner-error',
                     lastObservedAliveUtc=now(), livePids=[job[0].pid for job in jobs if job[0].poll() is None])
        save(O / 'test-result.json', state)
        traceback.print_exc()
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
