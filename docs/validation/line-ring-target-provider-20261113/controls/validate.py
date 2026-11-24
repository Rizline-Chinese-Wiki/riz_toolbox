"""Fresh offline validation of the retained target provider and chart mapping.

The preceding 881 preview is immutable and is not used as execution evidence
for new bytes. Existing native source fixtures supply only offline test shims.
"""
import concurrent.futures
import ctypes
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import threading
import time

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
OUT = Path('D:/riz-build-cache-next/line-ring-target-provider-integration-20261113/main-r1')
BASE = ROOT / 'docs/validation/line-ring-endpoint-local-20261113'
HELPER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
BUILD = ROOT / 'scripts/Build.ps1'


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    staged = Path(str(path) + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def main():
    require(not OUT.exists(), 'Preserve the previous validation attempt')
    require(sha(HELPER) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Input scanner changed')
    spec = importlib.util.spec_from_file_location('provider_input_scanner', HELPER)
    base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
    before = base.inputs()
    previous = read(BASE / 'run/inputs-after.json')
    additions = set(before) - set(previous)
    changes = {name for name in previous if name in before and previous[name] != before[name]}
    require(len(before) == 885 and len(previous) == 881 and set(previous) <= set(before)
            and additions == {'src/RizToolbox.Adapter/Gameplay/NativeSeekLineChartOccurrenceMap.cs',
                              'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Targets.cs',
                              'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineChartOccurrenceChecks.cs',
                              'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.TargetProvider.cs'}
            and changes == {'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs',
                            'tests/RizToolbox.Judgements.Tests/Program.cs'}, 'Unexpected source graph changes')
    OUT.mkdir(parents=True)
    artifacts = OUT / 'artifacts'
    controls = {str(path): sha(path) for path in (Path(__file__).resolve(), BUILD,
        ROOT / 'work/line-chart-occurrence-integration-20261113/payload-manifest.json',
        ROOT / 'work/line-chart-occurrence-integration-20261113/payload.patch')}
    state = {'controllerPid': os.getpid(), 'startedUtc': now(), 'passed': False,
             'scope': 'Retained real backend reader target provider and exact no-Note chart occurrence mapping; offline fixtures only',
             'compilerInputs': 885, 'defaultExpectedCases': 372, 'old300ExecutedThisBatch': False,
             'default372SingleProcessExecuted': False, 'sdkGoalComplete': False, 'completeSeekPermitted': False,
             'gameStarted': False, 'installed': False, 'steps': [], 'activeChildren': [], 'controlsBefore': controls}
    lock = threading.Lock()
    write(OUT / 'inputs-before.json', before)
    write(WORK / 'main-application-record.json', {'recordedUtc': now(), 'appliedToMain': True,
        'priorInputs': 881, 'newInputs': 885, 'added': sorted(additions), 'changed': sorted(changes),
        'buildSha256': sha(BUILD), 'newValidationPassed': False, 'sdkGoalComplete': False})
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    fixtures = read(ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json')['nativeFixtures']
    for name, row in fixtures.items():
        require(sha(row['path']) == row['sha256'], 'Offline native fixture changed')
        env[name] = row['path']
    state['nativeFixtures'] = fixtures

    def save():
        write(OUT / 'result.json', state)

    def unchanged():
        require(base.inputs() == before and all(sha(path) == digest for path, digest in controls.items()),
                'Source or controller changed during validation')

    def step(name, argv, count=None, prefix=None, expected_exit=0):
        unchanged()
        require(base.available_commit() >= 512 * 1024 * 1024, 'Available system commit below 512 MiB')
        stdout = OUT / (name + '.stdout.log'); stderr = OUT / (name + '.stderr.log')
        print('START ' + name, flush=True)
        with stdout.open('xb') as out, stderr.open('xb') as err:
            process = subprocess.Popen(list(map(str, argv)), cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
                                       stdout=out, stderr=err, creationflags=subprocess.CREATE_NO_WINDOW)
            with lock:
                state['activeChildren'].append({'name': name, 'pid': process.pid}); save()
            deadline = time.monotonic() + 2700
            forced = False
            while process.poll() is None:
                try:
                    process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if time.monotonic() >= deadline or base.available_commit() < 512 * 1024 * 1024:
                        forced = True; process.kill(); process.wait(); break
        body = stdout.read_text(encoding='utf-8-sig')
        errors = stderr.read_text(encoding='utf-8-sig')
        runs = re.findall(r'^RUN (.+)$', body, re.M)
        passes = re.findall(r'^PASS (.+)$', body, re.M)
        totals = re.findall(r'(\d+)/(\d+) passed', body)
        good = process.returncode == expected_exit and not forced and not re.search(r'^FAIL\b', body + errors, re.M)
        if expected_exit == 0:
            good = good and not errors
        if count is not None:
            good = good and len(passes) == count and runs == passes and len(set(passes)) == count
            good = good and totals == [(str(count), str(count))] and all(value.startswith(prefix) for value in passes)
        if name in ('six-release', 'solution'):
            success = '已成功生成。' in body or 'Build succeeded.' in body
            error_counts = re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M)
            good = good and success and error_counts == ['0']
            if name == 'six-release':
                good = good and re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0']
        row = {'name': name, 'arguments': list(map(str, argv)), 'exitCode': process.returncode,
               'naturalExit': not forced, 'matchesExpected': bool(good), 'passNames': passes,
               'stdoutSha256': sha(stdout), 'stderrSha256': sha(stderr), 'finishedUtc': now()}
        with lock:
            state['steps'].append(row)
            state['activeChildren'] = [item for item in state['activeChildren'] if item['name'] != name]
            save()
        print('END ' + name + ' exit=' + str(process.returncode), flush=True)
        require(good, 'Step failed: ' + name)
        return body, errors

    try:
        for name, project in (('six-release', ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'),
                              ('solution', ROOT / 'RizToolbox.sln')):
            step(name, [base.DOTNET, 'build', project, '-c', 'Release', '--artifacts-path', artifacts,
                '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(ROOT / 'work/ring-x-numeric-20261113/NuGet.Config')])
            if name == 'six-release':
                state['productsAfterSix'] = base.product_hashes(artifacts)
        state['productsBeforeTests'] = base.product_hashes(artifacts)
        require(state['productsAfterSix'] == state['productsBeforeTests'], 'Solution changed the six production bytes')
        test = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        state['testPair'] = {ext: sha(test.with_suffix(ext)) for ext in ('.dll', '.pdb')}
        state['dependencyCopies'] = {str(test.parent / (component + ext)): digest
            for component, values in state['productsBeforeTests'].items() if component in base.SIX[:4]
            for ext, digest in values.items()}
        require(all(sha(path) == digest for path, digest in state['dependencyCopies'].items()), 'Test dependencies differ')
        body, _ = step('legacy-listing', [base.DOTNET, test, '--list-legacy-cases'])
        listing = json.loads(body)
        require(listing == read(BASE / 'run/legacy-case-list.json') and len(listing) == 300, 'Legacy list changed')
        write(OUT / 'legacy-case-list.json', listing)
        for name, selector, count, prefix in (
            ('target-provider', '--line-ring-target-provider-checks', 6, 'Line ring source target provider '),
            ('chart-occurrence', '--line-chart-occurrence-checks', 5, 'Line chart occurrence '),
            ('endpoint-local', '--line-ring-endpoint-local-checks', 6, 'Line ring numeric endpoint '),
            ('numeric', '--line-ring-numeric-checks', 13, 'Line ring numeric '),
            ('ring-x', '--line-ring-ring-x-numeric-checks', 7, 'Line ring numeric ring ')):
            step(name, [base.DOTNET, test, selector], count, prefix)
        usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);',
                           (ROOT / 'tests/RizToolbox.Judgements.Tests/Program.cs').read_text(encoding='utf-8'))
        require(len(usage) == 1, 'Ambiguous Program usage')
        for name, argv in (
            ('invalid-chart-name', ['--line-chart-occurrence-check']),
            ('invalid-chart-arity', ['--line-chart-occurrence-checks', 'extra']),
            ('invalid-provider-name', ['--line-ring-target-provider-check']),
            ('invalid-provider-arity', ['--line-ring-target-provider-checks', 'extra'])):
            body, errors = step(name, [base.DOTNET, test, *argv], expected_exit=2)
            require(not body and errors == usage[0] + '\n', 'Invalid selector did not print exact usage')
        # Each original reader case has its own fixture lifetime. Independent
        # children are limited to two, with system commit/time guards retained.
        for label, selector, count, prefix in (
            ('source', '--line-ring-source-case', 19, 'Line ring source '),
            ('basis', '--canvas-basis-case', 16, 'Canvas basis ')):
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                futures = [pool.submit(step, f'{label}-{index:02}', [base.DOTNET, test, selector, index], 1, prefix)
                           for index in range(1, count + 1)]
                for future in concurrent.futures.as_completed(futures):
                    future.result()
        unchanged()
        state['productsAfterTests'] = base.product_hashes(artifacts)
        state['compiledBytesUnchanged'] = (state['productsAfterTests'] == state['productsBeforeTests']
            and all(sha(test.with_suffix(ext)) == digest for ext, digest in state['testPair'].items())
            and all(sha(path) == digest for path, digest in state['dependencyCopies'].items()))
        require(state['compiledBytesUnchanged'], 'Compiled/test dependency bytes changed')
        require(len(state['steps']) == 47 and not state['activeChildren']
                and all(row['naturalExit'] and row['matchesExpected'] for row in state['steps']), 'Incomplete scoped matrix')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        after = base.inputs(); write(OUT / 'inputs-after.json', after)
        state['inputsUnchanged'] = before == after
        state['controlsUnchanged'] = all(sha(path) == digest for path, digest in controls.items())
        state['nativeFixturesUnchanged'] = all(sha(row['path']) == row['sha256'] for row in fixtures.values())
        state['passed'] = state['passed'] and state['inputsUnchanged'] and state['controlsUnchanged'] and state['nativeFixturesUnchanged']
        state['finishedUtc'] = now(); save()
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']), 'failure': state.get('failure'),
                          'resultSha256': sha(OUT / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
