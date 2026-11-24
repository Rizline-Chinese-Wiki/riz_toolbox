"""Fresh offline validation of the reviewed 892-input parent/local writer on main."""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
ACCEPTED = ROOT / 'docs/validation/line-endpoint-writer-20261113'
OUT = Path('D:/riz-build-cache-next/line-endpoint-parent-integration-20261113/main-r1')
MIN_COMMIT = 512 * 1024 * 1024
CHILD_SECONDS = 1800


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    staged = Path(str(path) + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def load_stage():
    spec = importlib.util.spec_from_file_location('parent_writer_stage_validate', WORK / 'stage.py')
    stage = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(stage)
    return stage


def execute(name, argv, environment, scanner, count=None, prefix=None, exact_name=None,
            expected_exit=0, runs_expected=True):
    """Run one child with independent logs; no shared mutable report state."""
    require(scanner.available_commit() >= MIN_COMMIT, 'Available commit below 512 MiB before ' + name)
    stdout = OUT / (name + '.stdout.log')
    stderr = OUT / (name + '.stderr.log')
    print('START ' + name, flush=True)
    with stdout.open('xb') as output, stderr.open('xb') as errors:
        child = subprocess.Popen(list(map(str, argv)), cwd=ROOT, env=environment,
                                 stdin=subprocess.DEVNULL, stdout=output, stderr=errors,
                                 creationflags=subprocess.CREATE_NO_WINDOW)
        forced = False
        deadline = time.monotonic() + CHILD_SECONDS
        while child.poll() is None:
            try:
                child.wait(timeout=2)
            except subprocess.TimeoutExpired:
                if time.monotonic() >= deadline or scanner.available_commit() < MIN_COMMIT:
                    forced = True
                    child.kill()
                    child.wait()
                    break
    body = stdout.read_text(encoding='utf-8-sig')
    error_body = stderr.read_text(encoding='utf-8-sig')
    passes = re.findall(r'^PASS (.+)$', body, re.M)
    runs = re.findall(r'^RUN (.+)$', body, re.M)
    good = not forced and child.returncode == expected_exit and not re.search(r'^FAIL\b', body + error_body, re.M)
    if expected_exit == 0:
        good = good and not error_body
    if count is not None:
        good = (good and len(passes) == count and len(set(passes)) == count
                and re.findall(r'(\d+)/(\d+) passed\.', body) == [(str(count), str(count))]
                and (runs == passes if runs_expected else not runs)
                and (prefix is None or all(value.startswith(prefix) for value in passes))
                and (exact_name is None or passes == [exact_name]))
    if name in ('six-release', 'solution'):
        good = good and ('已成功生成。' in body or 'Build succeeded.' in body)
        good = good and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0']
        if name == 'six-release':
            good = good and re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0']
    row = {'name': name, 'arguments': list(map(str, argv)), 'pid': child.pid,
           'exitCode': child.returncode, 'naturalExit': not forced, 'matchesExpected': bool(good),
           'passNames': passes, 'runNames': runs, 'stdoutSha256': sha(stdout), 'stderrSha256': sha(stderr)}
    print('END ' + name + ' exit=' + str(child.returncode), flush=True)
    return row, body, error_body


def main():
    require(not OUT.exists(), 'Preserve previous main validation attempt')
    stage = load_stage()
    manifest = read(WORK / 'payload-manifest.json')
    application = read(WORK / 'application-record.json')
    require(manifest['schema'] == 'line-endpoint-parent-main-payload/v1'
            and application['schema'] == 'line-endpoint-parent-main-application/v1'
            and application['appliedToMain'] is True
            and application['manifestSha256'] == sha(WORK / 'payload-manifest.json')
            and application['reviewReceiptSha256'] == sha(WORK / 'review-receipt.json')
            and manifest['acceptedSummarySha256'] == stage.ACCEPTED_SUMMARY_SHA
            and sha(ACCEPTED / 'validation-summary.json') == stage.ACCEPTED_SUMMARY_SHA,
            'Main application or accepted baseline evidence changed')
    scanner = stage.input_scanner()
    before = read(WORK / 'inputs-before.json')
    expected = dict(before)
    for row in manifest['files']:
        expected[row['path']] = row['payloadSha256']
    require(before == read(ACCEPTED / 'run/inputs-after.json')
            and len(before) == 891 and len(expected) == 892
            and scanner.inputs() == expected, 'Exact applied 892-input graph is absent')
    require(sha(ROOT / 'scripts/Build.ps1') == stage.BUILD_SHA,
            'Existing build dispatch changed')
    writer_names = manifest['writerCaseNames']
    require(len(writer_names) == manifest['writerCases'] >= 29
            and manifest['defaultCases'] == 389 - 17 + len(writer_names),
            'Expected registry totals changed')
    controls = {str(path): sha(path) for path in (
        Path(__file__).resolve(), WORK / 'stage.py', WORK / 'apply.py',
        WORK / 'payload-manifest.json', WORK / 'review-receipt.json',
        WORK / 'application-record.json', ROOT / 'scripts/Build.ps1')}
    OUT.mkdir(parents=True)
    artifacts = OUT / 'artifacts'
    write(OUT / 'inputs-before.json', expected)
    fixtures = read(ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json')['nativeFixtures']
    environment = os.environ.copy()
    environment.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                       DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1',
                       DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for key, item in fixtures.items():
        require(sha(item['path']) == item['sha256'], 'Native fixture changed: ' + key)
        environment[key] = item['path']
    state = {
        'schema': 'line-endpoint-parent-main-validation/v1',
        'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'controllerPid': os.getpid(),
        'passed': False, 'compilerInputs': 892, 'defaultExpectedCases': manifest['defaultCases'],
        'writerCases': len(writer_names), 'expectedFocusedPassedCases': len(writer_names) + 94,
        'maxSimultaneousTestChildren': 2, 'minimumAvailableCommitBytes': MIN_COMMIT,
        'childTimeoutSeconds': CHILD_SECONDS, 'nativeFixtures': fixtures,
        'scope': 'Offline SetParent(target,false) plus local endpoint writer; observational journal is not atomic',
        'old300ExecutedThisBatch': False, 'defaultRegistrySingleProcessExecuted': False,
        'fullSource19ExecutedThisBatch': False, 'fullBasis16ExecutedThisBatch': False,
        'sdkGoalComplete': False, 'completeSeekPermitted': False, 'atomicConditionalWrites': False,
        'beforeIsAtomicPreState': False, 'externalEffectsUnclosed': True,
        'gameStarted': False, 'installed': False, 'steps': [], 'activeChildren': [],
        'controlsBefore': controls,
    }
    def save():
        write(OUT / 'result.json', state)
    def stable():
        return scanner.inputs() == expected and all(sha(path) == value for path, value in controls.items())
    def record(row):
        state['steps'].append(row)
        save()
        require(row['matchesExpected'], 'Execution gate failed: ' + row['name'])
    def run(name, argv, **kwargs):
        require(stable(), 'Source/control change before ' + name)
        state['activeChildren'] = [name]
        save()
        result = execute(name, argv, environment, scanner, **kwargs)
        state['activeChildren'] = []
        record(result[0])
        require(stable(), 'Source/control change after ' + name)
        return result[1:]
    try:
        dotnet = scanner.DOTNET
        for name, project in (('six-release', ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'),
                              ('solution', ROOT / 'RizToolbox.sln')):
            run(name, [dotnet, 'build', project, '-c', 'Release', '--artifacts-path', artifacts,
                       '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                       '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                       '-p:RestoreConfigFile=' + str(ROOT / 'work/ring-x-numeric-20261113/NuGet.Config')])
            if name == 'six-release':
                state['productsAfterSix'] = scanner.product_hashes(artifacts)
        state['productsBeforeTests'] = scanner.product_hashes(artifacts)
        require(state['productsAfterSix'] == state['productsBeforeTests'],
                'Solution replaced six production Release pairs')
        test = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        runtime_test = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
        state['testPairs'] = {str(path): sha(path) for assembly in (test, runtime_test)
                              for path in (assembly, assembly.with_suffix('.pdb'))}
        state['dependencies'] = {
            str(test.parent / (component + ext)): value
            for component, values in state['productsBeforeTests'].items() if component in scanner.SIX[:4]
            for ext, value in values.items()}
        state['dependencies'].update({
            str(runtime_test.parent / (component + ext)): value
            for component, values in state['productsBeforeTests'].items()
            if component in ('RizToolbox.Runtime', 'RizToolbox.Sdk')
            for ext, value in values.items()})
        require(len(state['testPairs']) == 4 and len(state['dependencies']) == 12
                and all(sha(path) == value for path, value in state['dependencies'].items()),
                'Built test or dependency bytes differ from fresh production products')
        listing, _ = run('legacy-listing', [dotnet, test, '--list-legacy-cases'])
        legacy = json.loads(listing)
        require(legacy == read(ACCEPTED / 'run/legacy-case-list.json') and len(legacy) == 300,
                'Old 300 registry names or order changed')
        write(OUT / 'legacy-case-list.json', legacy)
        with ThreadPoolExecutor(max_workers=2) as pool:
            for start in range(0, len(writer_names), 2):
                require(stable(), 'Source/control change before writer pair')
                jobs = []
                for index in range(start, min(start + 2, len(writer_names))):
                    name = 'writer-' + str(index + 1).zfill(2)
                    argv = [dotnet, test, '--line-ring-endpoint-writer-case', index + 1]
                    jobs.append((name, pool.submit(execute, name, argv, environment, scanner,
                                                   count=1, exact_name=writer_names[index])))
                state['activeChildren'] = [name for name, _ in jobs]
                save()
                results = [future.result() for _, future in jobs]
                state['activeChildren'] = []
                for result in results:
                    record(result[0])
                require(stable(), 'Source/control change after writer pair')
        for name, selector, count, prefix in (
            ('target-provider', '--line-ring-target-provider-checks', 6, 'Line ring source target provider '),
            ('chart-occurrence', '--line-chart-occurrence-checks', 5, 'Line chart occurrence '),
            ('endpoint-local', '--line-ring-endpoint-local-checks', 6, 'Line ring numeric endpoint '),
            ('numeric', '--line-ring-numeric-checks', 13, 'Line ring numeric '),
            ('ring-x', '--line-ring-ring-x-numeric-checks', 7, 'Line ring numeric ring ')):
            run(name, [dotnet, test, selector], count=count, prefix=prefix)
        program = (ROOT / 'tests/RizToolbox.Judgements.Tests/Program.cs').read_text(encoding='utf-8-sig')
        usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', program)
        require(len(usage) == 1, 'Program usage is ambiguous')
        for name, argv in (
            ('invalid-writer-name', ['--line-ring-endpoint-writer-cases']),
            ('invalid-writer-arity', ['--line-ring-endpoint-writer-case', '1', 'extra'])):
            body, error_body = run(name, [dotnet, test, *argv], expected_exit=2)
            require(not body and error_body == usage[0] + '\n',
                    'Illegal writer selector did not return exact usage')
        for index in (13, 14, 17, 18, 19):
            run('source-' + str(index), [dotnet, test, '--line-ring-source-case', index],
                count=1, prefix='Line ring source ')
        run('basis-01', [dotnet, test, '--canvas-basis-case', 1], count=1, prefix='Canvas basis ')
        run('renderer-abi', [dotnet, runtime_test, '--renderer-abi-checks'],
            count=51, runs_expected=False)
        require(stable(), 'Source/control change after scoped validation')
        state['productsAfterTests'] = scanner.product_hashes(artifacts)
        state['compiledBytesUnchanged'] = (
            state['productsAfterTests'] == state['productsBeforeTests']
            and all(sha(path) == value for path, value in {**state['testPairs'], **state['dependencies']}.items()))
        require(state['compiledBytesUnchanged'], 'Tested production, test or dependency bytes changed')
        expected_steps = len(writer_names) + 17
        require(len(state['steps']) == expected_steps
                and sum(len(row['passNames']) for row in state['steps']) == len(writer_names) + 94
                and all(row['naturalExit'] and row['matchesExpected'] for row in state['steps']),
                'Scoped step count or passed-case total differs')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        after = scanner.inputs()
        write(OUT / 'inputs-after.json', after)
        state['inputsUnchanged'] = after == expected
        state['controlsUnchanged'] = all(sha(path) == value for path, value in controls.items())
        state['nativeFixturesUnchanged'] = all(sha(item['path']) == item['sha256'] for item in fixtures.values())
        state['passed'] = (state['passed'] and state['inputsUnchanged']
                           and state['controlsUnchanged'] and state['nativeFixturesUnchanged'])
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        state['activeChildren'] = []
        save()
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']),
                          'passNames': sum(len(row['passNames']) for row in state['steps']),
                          'failure': state.get('failure'), 'resultSha256': sha(OUT / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
