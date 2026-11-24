"""Validate the new Runtime array path on its exact fresh 901-input main graph.

Reuse the frozen child execution/log gate, not the prior batch's test results.
Existing production method bodies are unchanged; scope is vector7/single3/ABI51
plus the six actual dynamic consumer cases, two builds and the legacy listing.
"""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
BASIS = ROOT / 'docs/validation/line-dynamic-consumer-20261113'
OUT = Path('D:/riz-build-cache-next/line-vector3-array-integration-20261113/main-r1')
CHILD_CONTROL = ROOT / 'work/line-dynamic-consumer-integration-20261113/validate_main.py'
SHADOW = ROOT / 'work/line-vector3-array-root-20261113/root-r2-snapshot'


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def save(path, value):
    temp = Path(str(path) + '.pending')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(temp, path)


def same(values):
    try:
        return all(sha(path) == digest for path, digest in values.items())
    except OSError:
        return False


def main():
    require(not OUT.exists(), 'Preserve any previous main attempt')
    prepare = load(WORK / 'prepare_apply.py', 'vector_array_main_prepare')
    manifest = read(WORK / 'payload-manifest.json')
    application = read(WORK / 'application-record.json')
    require(application['mainApplied'] and application['newInputs'] == 901
            and application['payloadManifestSha256'] == sha(WORK / 'payload-manifest.json')
            and not application['freshMainPassed'], 'Exact array application receipt absent')
    summary, before, sources, expected = prepare.preflight(manifest['basisSummarySha256'])
    require(expected == manifest['inputsAfter'] and before == manifest['inputsBefore']
            and len(expected) == 901 and all(sha(path) == digest for path, digest in manifest['controls'].items()), 'Array payload or accepted ancestor changed')
    scanner = prepare.scanner()
    require(scanner.inputs() == expected, 'Current main is not the exact array payload graph')
    require(sha(CHILD_CONTROL) == '81d0e494849fc9b5fac1bc5bacfb81d264da6d0e71e7a74f4e8c02677b7cd3cc', 'Frozen execution gate changed')
    child_control = load(CHILD_CONTROL, 'array_main_child_execution')
    child_control.OUT = OUT  # Logs of this new execution belong only to this run.
    shadow = read(SHADOW / 'result.json')
    old = read(BASIS / 'run/result.json')
    old_steps = {row['name']: row for row in old['steps']}
    require(old['passed'] and old['compilerInputs'] == 898
            and len(old['steps']) == 78 and sum(len(row['passNames']) for row in old['steps']) == 155, 'Accepted dynamic run is incomplete')
    fixtures = old['nativeFixtures']
    require(len(fixtures) == 3 and all(sha(row['path']) == row['sha256'] for row in fixtures.values()), 'Pinned native fixtures changed')
    controls = {str(path): sha(path) for path in (
        Path(__file__).resolve(), WORK / 'prepare_apply.py', WORK / 'observe.py',
        WORK / 'payload-manifest.json', WORK / 'application-record.json', CHILD_CONTROL,
        ROOT / 'scripts/Build.ps1', ROOT / 'work/ring-x-numeric-20261113/NuGet.Config',
        BASIS / 'validation-summary.json', BASIS / 'run/result.json', BASIS / 'run/inputs-after.json',
        SHADOW / 'result.json', *(sources.values()),
        *(WORK / 'files' / row['path'] for row in manifest['files']),
        *(WORK / 'before' / row['path'] for row in manifest['files'] if row['baseSha256'] is not None))}
    controls.update(manifest['controls'])
    env = os.environ.copy()
    env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for key, row in fixtures.items():
        env[key] = row['path']
    OUT.mkdir(parents=True)
    artifacts = OUT / 'artifacts'
    save(OUT / 'inputs-before.json', expected)
    state = {'schema': 'line-vector3-array-main-validation/v1',
             'controllerPid': os.getpid(), 'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
             'passed': False, 'compilerInputs': 901, 'expectedFocusedPassedCases': 67,
             'expectedNaturalSteps': 12, 'controlsBefore': controls, 'nativeFixtures': fixtures,
             'basisSummarySha256': manifest['basisSummarySha256'],
             'maxSimultaneousTestChildren': 2, 'minimumAvailableCommitBytes': child_control.MIN_COMMIT,
             'childTimeoutSeconds': child_control.CHILD_SECONDS,
             'steps': [], 'activeChildren': [], 'fullSource30ExecutedThisBatch': False,
             'fullLegacy300ExecutedThisBatch': False, 'fullRuntimeDefaultExecutedThisBatch': False,
             'sdkGoalComplete': False, 'completeSeekPermitted': False,
             'gameStarted': False, 'installed': False}

    def stable():
        return scanner.inputs() == expected and same(controls)

    def record(row):
        state['steps'].append(row)
        save(OUT / 'result.json', state)
        require(row['matchesExpected'], 'Actual execution failed: ' + row['name'])

    def run(name, argv, **kwargs):
        require(stable(), 'Source/control changed before ' + name)
        state['activeChildren'] = [name]
        save(OUT / 'result.json', state)
        row, body, error = child_control.execute(name, argv, env, scanner, **kwargs)
        state['activeChildren'] = []
        record(row)
        require(stable(), 'Source/control changed after ' + name)
        return body

    try:
        for name, project in (('six-release', ROOT / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj'),
                              ('solution', ROOT / 'RizToolbox.sln')):
            run(name, [scanner.DOTNET, 'build', project, '-c', 'Release', '--artifacts-path', artifacts,
                       '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                       '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                       '-p:RestoreConfigFile=' + str(ROOT / 'work/ring-x-numeric-20261113/NuGet.Config')])
            if name == 'six-release':
                state['productsAfterSix'] = scanner.product_hashes(artifacts)
        state['productsBeforeTests'] = scanner.product_hashes(artifacts)
        require(state['productsAfterSix'] == state['productsBeforeTests'], 'Solution replaced production bytes')
        runtime = artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
        judgement = artifacts / 'bin/RizToolbox.Judgements.Tests/release/RizToolbox.Judgements.Tests.dll'
        state['testPairs'] = {str(p): sha(p) for a in (runtime, judgement) for p in (a, a.with_suffix('.pdb'))}
        deps = {}
        for a, components in ((runtime, ('RizToolbox.Runtime', 'RizToolbox.Sdk')),
                              (judgement, scanner.SIX[:4])):
            for component in components:
                for ext in ('.dll', '.pdb'):
                    p = a.parent / (component + ext)
                    digest = state['productsBeforeTests'][component][ext]
                    require(sha(p) == digest, 'Fresh test dependency differs: ' + str(p))
                    deps[str(p)] = digest
        require(len(deps) == 12, 'Twelve fresh test dependencies required')
        state['dependencies'] = deps
        golden = judgement.parent / 'line-ring-numeric-goldens.json'
        require(sha(golden) == expected['tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'], 'Actual test golden differs')
        state['goldenSha256'] = sha(golden)
        listed = json.loads(run('legacy-listing', [scanner.DOTNET, judgement, '--list-legacy-cases']))
        require(listed == read(BASIS / 'run/legacy-case-list.json') and len(listed) == 300, 'Legacy registry changed')
        save(OUT / 'legacy-case-list.json', listed)
        for name, selector, names in (
            ('vector', '--vector3-array-checks', shadow['vectorCaseNames']),
            ('single', '--single-array-checks', shadow['singleCaseNames'])):
            run(name, [scanner.DOTNET, runtime, selector], count=len(names), expected_names=names)
        run('renderer-abi', [scanner.DOTNET, runtime, '--renderer-abi-checks'], count=51,
            expected_names=old_steps['renderer-abi']['passNames'], runs_expected=False)
        with ThreadPoolExecutor(max_workers=2) as pool:
            for start in range(25, 31, 2):
                require(stable(), 'Source/control change before dynamic pair')
                jobs = []
                for index in (start, start + 1):
                    name = f'source-{index:02}'
                    jobs.append(pool.submit(child_control.execute, name,
                        [scanner.DOTNET, judgement, '--line-ring-source-case', index], env, scanner,
                        count=1, expected_names=old_steps[name]['passNames']))
                state['activeChildren'] = [f'source-{index:02}' for index in (start, start + 1)]
                save(OUT / 'result.json', state)
                rows = [job.result()[0] for job in jobs]
                state['activeChildren'] = []
                for row in rows:
                    record(row)
                require(stable(), 'Source/control changed after dynamic pair')
        state['productsAfterTests'] = scanner.product_hashes(artifacts)
        state['compiledBytesUnchanged'] = (state['productsAfterTests'] == state['productsBeforeTests']
            and same({**state['testPairs'], **state['dependencies']}) and sha(golden) == state['goldenSha256'])
        require(state['compiledBytesUnchanged'] and len(state['steps']) == 12
                and sum(len(row['passNames']) for row in state['steps']) == 67
                and all(row['naturalExit'] and row['matchesExpected'] for row in state['steps'])
                and stable(), 'Fresh scoped main validation did not close')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        after = scanner.inputs()
        save(OUT / 'inputs-after.json', after)
        state['inputsUnchanged'] = after == expected
        state['controlsUnchanged'] = same(controls)
        state['nativeFixturesUnchanged'] = all(sha(row['path']) == row['sha256'] for row in fixtures.values())
        state['passed'] = state['passed'] and state['inputsUnchanged'] and state['controlsUnchanged'] and state['nativeFixturesUnchanged']
        state['activeChildren'] = []
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        save(OUT / 'result.json', state)
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']),
                          'positiveChecks': sum(len(row['passNames']) for row in state['steps']),
                          'failure': state.get('failure'), 'resultSha256': sha(OUT / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
