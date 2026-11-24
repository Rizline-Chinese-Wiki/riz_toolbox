"""Check the changed default Runtime test registry on the already-built bytes."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
MAIN = Path('D:/riz-build-cache-next/line-vector3-array-integration-20261113/main-r1')
OUT = MAIN.parent / 'default-runtime-r1'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    state = json.loads((MAIN / 'result.json').read_text())
    assert state['passed'] and state['compilerInputs'] == 901
    assert not OUT.exists()
    gate_path = ROOT / 'work/line-dynamic-consumer-integration-20261113/validate_main.py'
    assert sha(gate_path) == '81d0e494849fc9b5fac1bc5bacfb81d264da6d0e71e7a74f4e8c02677b7cd3cc'
    spec = importlib.util.spec_from_file_location('default_runtime_gate', gate_path)
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
    OUT.mkdir()
    gate.OUT = OUT
    assembly = MAIN / 'artifacts/bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
    frozen = {**state['testPairs'], **state['dependencies']}
    assert all(sha(p) == digest for p, digest in frozen.items())
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for name, row in state['nativeFixtures'].items():
        assert sha(row['path']) == row['sha256']
        env[name] = row['path']
    report = {'schema': 'line-vector3-array-default-runtime-supplement/v1',
              'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
              'controllerPid': os.getpid(), 'passed': False, 'compilerInputs': 901,
              'mainResultSha256': sha(MAIN / 'result.json'), 'testAndDependencyBytes': frozen,
              'nativeFixtures': state['nativeFixtures'], 'sdkGoalComplete': False, 'completeSeekPermitted': False}
    row, body, errors = gate.execute('default-runtime', [Path('C:/Program Files/dotnet/dotnet.exe'), assembly],
                                    env, gate.load_prepare().input_scanner())
    totals = re.findall(r'Native runtime checks: (\d+)/(\d+) passed\.', body)
    report.update(step=row, totals=totals,
                  testedBytesUnchanged=all(sha(p) == digest for p, digest in frozen.items()),
                  finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
    report['passed'] = (row['naturalExit'] and row['exitCode'] == 0 and not errors
                        and row['matchesExpected'] and len(totals) == 1
                        and totals[0][0] == totals[0][1] == str(len(row['passNames']))
                        and len(set(row['passNames'])) == len(row['passNames'])
                        and report['testedBytesUnchanged'])
    (OUT / 'result.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'passed': report['passed'], 'totals': totals, 'actualChildExit': row['exitCode'],
                      'naturalExit': row['naturalExit'], 'resultSha256': sha(OUT / 'result.json')}))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
