"""Run the fresh 879-input Canvas basis + Ring X SDK matrix, with at most two test children."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import contextlib
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import threading
import time

EXPECTED_MANIFEST_SHA = 'cd750fa21b36989cda8d6fdd1cfd56d8627d7325ea6f3c17021f2c21f529abac'
EXPECTED_AFTER_GRAPH_SHA = 'd56802e34a2f1bfd427ce4056e09c93b3c552cbcf4f45332231c84b8f85cbf63'
EXPECTED_BUILD_SHA = 'f9e008d3c1d1f4f9a42bc00e0c5770104823ce41101b5bca07cb47d00c0b9011'
EXPECTED_RING_REVIEW_SHA = 'b21099cb2b908272fe0674c20eaa6554beda9d61c736693c7f0bc36ae021ceb1'
OFFLINE_CONFIG_SHA = '964302159308872b9729e1828a1ba897f30634f7bd751cde11ea8e6fed8cfb97'
NATIVE_FIXTURE_DECLARATION_SHA = '79776e6285697456b92d34689f802cfca9d59294cb871fb765eac59a1eac76d2'
LEGACY_LISTING_BASELINE_SHA = '1ee2fc9a5b905a607e2d6969df11dfc943ac01e89a3f6b8cd4c059333d95a0aa'
COMPILER_INPUT_COUNT = 879
SOURCE_CASE_COUNT = 19
BASIS_CASE_COUNT = 16
NUMERIC_CASE_COUNT = 13
RING_CASE_COUNT = 7
LEGACY_CASE_COUNT = 300
EXPECTED_DEFAULT_COUNT = sum((SOURCE_CASE_COUNT, BASIS_CASE_COUNT, NUMERIC_CASE_COUNT,
                              RING_CASE_COUNT, LEGACY_CASE_COUNT))

PREPARE_PATH = Path(__file__).resolve().parent / 'prepare.py'
PREPARE_SHA = '367c77cb0243f80cf922ef8ae9b67a6a7d6c61c051320d2f97fb2dcc6b7873e6'
if hashlib.sha256(PREPARE_PATH.read_bytes()).hexdigest() != PREPARE_SHA:
    raise RuntimeError('Combined preparation control changed before import')
import prepare

APPLY_PATH = Path(__file__).resolve().parent / 'apply.py'
APPLY_SHA = 'abb7c652b87757aac1f50a938dba5371a30b639709fe5beb7db878aa44c80658'
if hashlib.sha256(APPLY_PATH.read_bytes()).hexdigest() != APPLY_SHA:
    raise RuntimeError('Combined application control changed before import')
import apply as application_control

ROOT = prepare.ROOT
WORK = prepare.WORK
HELPER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
OFFLINE_CONFIG = ROOT / 'work/ring-x-numeric-20261113/NuGet.Config'
HELPER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
if hashlib.sha256(HELPER.read_bytes()).hexdigest() != HELPER_SHA:
    raise RuntimeError('Input and system commit helper changed before import')
spec = importlib.util.spec_from_file_location('input_and_memory_helpers', HELPER)
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
DOTNET, SIX, TESTS = base.DOTNET, base.SIX, base.TESTS


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    opt = parser.parse_args()
    out = opt.output.resolve()
    if not out.is_relative_to(Path('D:/riz-build-cache-next/line-ring-target-primitives-integration-20261113').resolve()):
        raise RuntimeError('Output must remain inside this combined validation cache.')
    out.mkdir(parents=True, exist_ok=False)
    shutil.copyfile(__file__, out / 'validation-control.py')
    shutil.copyfile(WORK / 'patch-manifest.json', out / 'patch-manifest.json')
    artifacts = out / 'artifacts'
    inputs = base.inputs()
    write(out / 'inputs-before.json', inputs)
    state = {'scope': 'Fresh actual SDK combined Canvas basis and Ring X graph; all required cases newly executed',
             'controllerPid': os.getpid(), 'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
             'controlSha256': sha(__file__), 'artifactsDirectory': str(artifacts), 'steps': [], 'activeChildren': {},
             'passed': False, 'acceptedThisBatch': False, 'sdkGoalComplete': False, 'completeSeekPermitted': False,
             'fullSeekPermitted': False, 'gameStarted': False, 'installed': False,
             'prior874UnfinishedPreserved': True, 'oldResultRetainedAsCurrentPass': False,
             'default355SingleProcessExecuted': False, 'defaultExpectedCases': EXPECTED_DEFAULT_COUNT,
             'maximumConcurrentTestChildren': 2,
             'childTimeoutSeconds': 2700, 'minimumAvailableCommitBytes': 512 * 1024 * 1024}
    lock = threading.RLock()
    failed = threading.Event()
    invalid_usage = None
    env = os.environ.copy()
    env.update({'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
                'DOTNET_GENERATE_ASPNET_CERTIFICATE': 'false', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH': 'false', 'DOTNET_NOLOGO': '1'})

    def save():
        with lock:
            write(out / 'result.json', state)

    def step(name, arguments, total=None, exact_name=None, prefix=None, expected_exit=0):
        if failed.is_set():
            raise RuntimeError('A sibling step failed before ' + name)
        if base.inputs() != inputs:
            failed.set()
            raise RuntimeError('Frozen source graph changed before ' + name)
        if any(sha(path) != digest for path, digest in state.get('controlHashesBefore', {}).items()):
            failed.set()
            raise RuntimeError('A frozen control or fixture declaration changed before ' + name)
        if any(sha(ROOT / path) != digest for path, digest in state.get('nonCompilerControlsBefore', {}).items()):
            failed.set()
            raise RuntimeError('Build control changed before ' + name)
        if base.available_commit() < state['minimumAvailableCommitBytes']:
            failed.set()
            raise RuntimeError('Insufficient system commit before ' + name)
        log = out / (name + '.log')
        stderr_log = out / (name + '.stderr.log') if expected_exit != 0 else None
        started = dt.datetime.now(dt.timezone.utc).isoformat()
        resource_stop = None
        timed_out = False
        forced_stop = False
        print('START ' + name, flush=True)
        with contextlib.ExitStack() as stack:
            stream = stack.enter_context(log.open('wb'))
            error_stream = stack.enter_context(stderr_log.open('wb')) if stderr_log else subprocess.STDOUT
            child = subprocess.Popen([str(arg) for arg in arguments], cwd=ROOT, env=env,
                                     stdin=subprocess.DEVNULL, stdout=stream, stderr=error_stream)
            with lock:
                state['activeChildren'][name] = {'pid': child.pid, 'startedUtc': started, 'log': str(log)}
                save()
            deadline = time.monotonic() + state['childTimeoutSeconds']
            while child.poll() is None:
                try:
                    child.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    commit = base.available_commit()
                    if failed.is_set() or commit < state['minimumAvailableCommitBytes'] or time.monotonic() >= deadline:
                        timed_out = time.monotonic() >= deadline
                        if not timed_out:
                            resource_stop = {'reason': 'Sibling failure' if failed.is_set() else 'Less than 512 MiB of system commit remains.',
                                             'availableCommitBytes': commit, 'ownedPid': child.pid}
                        failed.set()
                        forced_stop = True
                        child.kill()
                        child.wait()
                        break
        content = log.read_text(encoding='utf-8-sig', errors='replace')
        error_content = stderr_log.read_text(encoding='utf-8-sig', errors='replace') if stderr_log else None
        totals = re.findall(r'(\d+)/(\d+) audio/content checks passed\.' if name == 'audio-content' else r'(\d+)/(\d+) passed', content)
        passes = re.findall(r'^PASS (.+)$', content, re.MULTILINE)
        runs = re.findall(r'^RUN (.+)$', content, re.MULTILINE)
        natural_exit = not forced_stop
        good = (child.returncode == expected_exit and natural_exit and not timed_out and resource_stop is None
                and not re.search(r'^FAIL\b', content, re.MULTILINE))
        good = good and (total is None or totals == [(str(total), str(total))])
        if exact_name is not None:
            good = good and passes == [exact_name] and runs == [exact_name]
        if prefix is not None:
            good = (good and len(passes) == total and runs == passes
                    and len(set(passes)) == total and all(case.startswith(prefix) for case in passes))
        if expected_exit != 0:
            good = (good and not runs and not passes and not totals and content == ''
                    and invalid_usage is not None and error_content == invalid_usage + '\n')
        row = {'name': name, 'arguments': [str(arg) for arg in arguments], 'startedUtc': started,
               'finishedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'exitCode': child.returncode,
               'naturalExit': natural_exit, 'timedOut': timed_out, 'resourceStop': resource_stop,
               'stderrLog': str(stderr_log) if stderr_log else None,
               'stderrLogSha256': sha(stderr_log) if stderr_log else None,
               'totals': totals, 'passNames': passes,
               'runNames': runs, 'expectedExitCode': expected_exit, 'matchesExpected': good,
               'log': str(log), 'logSha256': sha(log)}
        with lock:
            state['steps'].append(row)
            state['activeChildren'].pop(name, None)
            save()
        print('END ' + name + ' exit=' + str(child.returncode) + ' expected=' + str(good), flush=True)
        if not good:
            failed.set()
            raise RuntimeError('Child did not meet its exact gate: ' + name)
        return content

    def parallel(cases):
        with ThreadPoolExecutor(max_workers=2) as pool:
            futures = [pool.submit(step, *case) for case in cases]
            try:
                for future in futures:
                    future.result()
            except Exception:
                failed.set()
                for future in futures:
                    future.cancel()
                raise

    def dll(test):
        return artifacts / 'bin' / test / 'release' / (test + '.dll')

    try:
        manifest = read(WORK / 'patch-manifest.json')
        applied = read(WORK / 'application-record.json')
        expected = read(WORK / 'expected-after-compiler-inputs.json')
        if sha(WORK / 'patch-manifest.json') != EXPECTED_MANIFEST_SHA or sha(WORK / 'expected-after-compiler-inputs.json') != EXPECTED_AFTER_GRAPH_SHA:
            raise RuntimeError('Combined reviewed package or 879 graph snapshot changed.')
        if manifest['schema'] != 'line-ring-target-primitives-combined-main/v1' or manifest['patchSha256'] != sha(WORK / 'integration.patch'):
            raise RuntimeError('Combined patch no longer matches its manifest.')
        if manifest['runnerCaseBounds'] != {'lineRingSource': 19, 'canvasBasis': 16,
                                            'lineRingNumeric': 13, 'ringXNumeric': 7,
                                            'legacy': 300, 'defaultExpected': 355}:
            raise RuntimeError('Combined case partition changed.')
        if not applied['appliedToMain'] or applied['manifestSha256'] != EXPECTED_MANIFEST_SHA or not applied['prior874UnfinishedPreserved']:
            raise RuntimeError('Exact combined application record is required without accepting the unfinished 874 matrix.')
        if applied['compilerInputCountAfter'] != 879 or applied['newMainFullMatrixPassed'] or applied['completeSeekPermitted'] or applied['sdkGoalComplete']:
            raise RuntimeError('Application record claims another graph or acceptance state.')
        if applied['targetRecords'] != manifest['changes'] or applied['mainInputsAfterSha256'] != EXPECTED_AFTER_GRAPH_SHA:
            raise RuntimeError('Applied target records or graph digest differ from the reviewed payload.')
        stop_digest = application_control.supersession_digest()
        if applied['superseded874StopRecordSha256'] != stop_digest:
            raise RuntimeError('Application no longer binds the actual preserved intermediate termination.')
        state['superseded874StopRecordSha256'] = stop_digest
        if inputs != expected or inputs != read(WORK / 'main-inputs-after.json') or len(inputs) != COMPILER_INPUT_COUNT:
            raise RuntimeError('Exact fresh 879-input compiler graph is required.')
        if len(manifest['changes']) != 10 or any(sha(ROOT / row['targetPath']) != row['newTargetSha256'] for row in manifest['changes']):
            raise RuntimeError('An exact combined source, test, or Build target differs.')
        program_text = (ROOT / 'tests/RizToolbox.Judgements.Tests/Program.cs').read_text(encoding='utf-8')
        usage_lines = re.findall(r'Console\.Error\.WriteLine\("(Usage: RizToolbox\.Judgements\.Tests \[[^"\r\n]+\])"\);', program_text)
        if len(usage_lines) != 1:
            raise RuntimeError('Exact Program usage line is missing or ambiguous.')
        invalid_usage = usage_lines[0]
        state['invalidUsageSha256'] = hashlib.sha256(invalid_usage.encode('utf-8')).hexdigest()
        controls = applied['nonCompilerControlsAfter']
        if controls != {'scripts/Build.ps1': EXPECTED_BUILD_SHA} or sha(ROOT / 'scripts/Build.ps1') != EXPECTED_BUILD_SHA:
            raise RuntimeError('Applied Build control differs from combined payload.')
        control_paths = [WORK / name for name in ('patch-manifest.json', 'integration.patch', 'prepare.py',
                                                  'apply.py', 'expected-after-compiler-inputs.json',
                                                  'application-record.json', 'main-inputs-after.json')]
        control_paths += [HELPER, out / 'patch-manifest.json', out / 'validation-control.py']
        control_paths += [WORK / name for name in ('stop_superseded_874.ps1', 'superseded-874-stop.json',
                                                  'superseded-874-result-before-stop.json',
                                                  'superseded-874-result-after-stop.json')]
        control_paths += [ROOT / name for name in (
            'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json',
            'docs/validation/line-ring-actual-source-20261113/legacy-case-list.json',
            'work/canvas-numeric-basis-integration-20261113/patch-manifest.json',
            'work/canvas-numeric-basis-integration-20261113/integration.patch',
            'work/canvas-numeric-basis-integration-20261113/root-integration-review.json',
            'work/ring-x-numeric-20261113/FROZEN-MANIFEST.md',
            'work/ring-x-numeric-20261113/integration.patch',
            'work/ring-x-numeric-20261113/independent-review.json',
            'work/ring-x-numeric-20261113/candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs',
            'work/ring-x-numeric-20261113/candidate/tests/NativeSeekLineRingXNumericChecks.cs',
            'work/ring-x-numeric-20261113/NuGet.Config',
            'work/ring-x-numeric-20261113/single-bit-goldens.json',
            'work/ring-x-numeric-20261113/oracle_fraction.py')]
        state['controlHashesBefore'] = {str(path): sha(path) for path in control_paths}
        if sha(ROOT / 'work/ring-x-numeric-20261113/independent-review.json') != EXPECTED_RING_REVIEW_SHA:
            raise RuntimeError('Ring X independent review changed.')
        for key, name in (('canvasPackageManifestSha256', 'work/canvas-numeric-basis-integration-20261113/patch-manifest.json'),
                          ('canvasPackagePatchSha256', 'work/canvas-numeric-basis-integration-20261113/integration.patch'),
                          ('canvasRootReviewSha256', 'work/canvas-numeric-basis-integration-20261113/root-integration-review.json'),
                          ('ringFrozenManifestSha256', 'work/ring-x-numeric-20261113/FROZEN-MANIFEST.md'),
                          ('ringFrozenPatchSha256', 'work/ring-x-numeric-20261113/integration.patch'),
                          ('ringIndependentReviewSha256', 'work/ring-x-numeric-20261113/independent-review.json')):
            if sha(ROOT / name) != manifest[key]:
                raise RuntimeError('A frozen candidate or review changed: ' + name)
        for key, name in (('ringSourceSha256', 'work/ring-x-numeric-20261113/candidate/src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingXNumericMath.cs'),
                          ('ringTestSha256', 'work/ring-x-numeric-20261113/candidate/tests/NativeSeekLineRingXNumericChecks.cs')):
            if sha(ROOT / name) != manifest[key]:
                raise RuntimeError('A frozen Ring X class changed: ' + name)
        if sha(OFFLINE_CONFIG) != OFFLINE_CONFIG_SHA:
            raise RuntimeError('Offline NuGet source configuration changed.')
        state['applicationRecordSha256'] = sha(WORK / 'application-record.json')
        state['nonCompilerControlsBefore'] = controls
        fixture_declaration = ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json'
        listing_baseline = ROOT / 'docs/validation/line-ring-actual-source-20261113/legacy-case-list.json'
        if sha(fixture_declaration) != NATIVE_FIXTURE_DECLARATION_SHA or sha(listing_baseline) != LEGACY_LISTING_BASELINE_SHA:
            raise RuntimeError('Native fixture declaration or original legacy listing changed before reading.')
        fixtures = read(fixture_declaration)['nativeFixtures']
        for key, row in fixtures.items():
            if sha(row['path']) != row['sha256']:
                raise RuntimeError('Pinned fixture changed: ' + key)
            env[key] = row['path']
        state['nativeFixtures'] = fixtures
        save()
        for name, project in [('build-' + component, ROOT / 'src' / component / (component + '.csproj')) for component in SIX] + [
            ('build-' + test, ROOT / 'tests' / test / (test + '.csproj')) for test in TESTS] + [('build-solution', ROOT / 'RizToolbox.sln')]:
            step(name, [DOTNET, 'build', project, '-c', 'Release', '--artifacts-path', artifacts,
                 '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                 '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(OFFLINE_CONFIG)])
        products = base.product_hashes(artifacts)
        state['productsBeforeTests'] = products
        state['testAssemblies'] = {test: {ext: sha(artifacts / 'bin' / test / 'release' / (test + ext))
                                         for ext in ('.dll', '.pdb')} for test in TESTS}
        if len(products) != 6 or sum(len(row) for row in products.values()) != 12 or len(state['testAssemblies']) != 3 or sum(len(row) for row in state['testAssemblies'].values()) != 6:
            raise RuntimeError('Fresh six production and three test DLL/PDB pairs are required.')
        state['dependencyCopies'] = [{'test': test, 'component': component, 'extension': ext,
            'sha256': sha(artifacts / 'bin' / test / 'release' / (component + ext)),
            'matches': sha(artifacts / 'bin' / test / 'release' / (component + ext)) == products[component][ext]}
            for test in TESTS for component in SIX[:4] for ext in ('.dll', '.pdb')]
        if len(state['dependencyCopies']) != 24 or not all(row['matches'] for row in state['dependencyCopies']):
            raise RuntimeError('Current 24 dependency DLL/PDB copies differ from current production.')
        golden_source = ROOT / 'tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'
        golden_copy = dll(TESTS[0]).parent / 'line-ring-numeric-goldens.json'
        state['numericGoldenFixture'] = {'sourceSha256': sha(golden_source), 'outputSha256': sha(golden_copy), 'outputPath': str(golden_copy)}
        if state['numericGoldenFixture']['sourceSha256'] != '1bf6d52a1e1aacc01f2c0e92c951ed860f0137164860476888e473c7cea13a48' or state['numericGoldenFixture']['sourceSha256'] != state['numericGoldenFixture']['outputSha256']:
            raise RuntimeError('The copied numeric golden fixture differs from the frozen source.')
        ring_golden = ROOT / 'work/ring-x-numeric-20261113/single-bit-goldens.json'
        ring_oracle = ROOT / 'work/ring-x-numeric-20261113/oracle_fraction.py'
        state['ringNonCompilerFixturesBefore'] = {str(path): sha(path) for path in (ring_golden, ring_oracle)}
        if state['ringNonCompilerFixturesBefore'] != {
            str(ring_golden): '3ce67eb819676dac687488c8ac81b5efdc5c451c52673d7b82b314fad44fe5bb',
            str(ring_oracle): '29c015661f0bf0817c67b06221d4ac4438ddb8a77c3fcd1a04606a613e6bc86e'}:
            raise RuntimeError('Ring X frozen golden or oracle changed.')
        listing = json.loads(step('list-legacy-cases', [DOTNET, dll(TESTS[0]), '--list-legacy-cases']))
        if [row['index'] for row in listing] != list(range(1, LEGACY_CASE_COUNT + 1)) or len({row['name'] for row in listing}) != LEGACY_CASE_COUNT:
            raise RuntimeError('Current legacy listing must contain exactly 300 unique original cases.')
        old_listing = read(listing_baseline)
        if listing != old_listing:
            raise RuntimeError('Adding numeric tests changed original legacy coverage/order.')
        write(out / 'legacy-case-list.json', listing)
        step('numeric-13', [DOTNET, dll(TESTS[0]), '--line-ring-numeric-checks'], NUMERIC_CASE_COUNT, prefix='Line ring numeric ')
        numeric_names = next(row['passNames'] for row in state['steps'] if row['name'] == 'numeric-13')
        if any(case.startswith('Line ring numeric ring ') for case in numeric_names):
            raise RuntimeError('The prior numeric selector included Ring X cases.')
        step('ring-x-7', [DOTNET, dll(TESTS[0]), '--line-ring-ring-x-numeric-checks'], RING_CASE_COUNT, prefix='Line ring numeric ring ')
        ring_names = next(row['passNames'] for row in state['steps'] if row['name'] == 'ring-x-7')
        if len(set(numeric_names + ring_names)) != NUMERIC_CASE_COUNT + RING_CASE_COUNT:
            raise RuntimeError('Numeric and Ring X selectors overlap.')
        parallel([('source-' + str(i).zfill(2), [DOTNET, dll(TESTS[0]), '--line-ring-source-case', str(i)], 1, None, 'Line ring source ')
                  for i in range(17, SOURCE_CASE_COUNT + 1)])
        state['earlyFeatureGatesPassed'] = True
        save()
        for selector, invalid in (('--legacy-case', ('0', '301', 'not-an-int')),
                                  ('--line-ring-source-case', ('0', '20', 'not-an-int')),
                                  ('--canvas-basis-case', ('0', '17', 'not-an-int'))):
            for value in invalid:
                step('invalid-' + selector.removeprefix('--') + '-' + value,
                     [DOTNET, dll(TESTS[0]), selector, value], expected_exit=2)
        step('invalid-ring-selector', [DOTNET, dll(TESTS[0]), '--line-ring-ring-x-numeric-check'], expected_exit=2)
        step('invalid-ring-arity', [DOTNET, dll(TESTS[0]), '--line-ring-ring-x-numeric-checks', 'extra'], expected_exit=2)
        parallel([('source-' + str(i).zfill(2), [DOTNET, dll(TESTS[0]), '--line-ring-source-case', str(i)], 1, None, 'Line ring source ')
                  for i in range(1, 17)])
        parallel([('basis-' + str(i).zfill(2), [DOTNET, dll(TESTS[0]), '--canvas-basis-case', str(i)], 1, None, 'Canvas basis ')
                  for i in range(1, BASIS_CASE_COUNT + 1)])
        parallel([('legacy-' + str(row['index']).zfill(3), [DOTNET, dll(TESTS[0]), '--legacy-case', str(row['index'])], 1, row['name'])
                  for row in listing])
        step('renderer-abi', [DOTNET, dll(TESTS[1]), '--renderer-abi-checks'], 51)
        step('audio-content', [DOTNET, dll(TESTS[2])], 1001)
        step('exact-static-read', [DOTNET, dll(TESTS[1]), '--exact-static-field-read-checks'], 6)
        state['productsAfterTests'] = base.product_hashes(artifacts)
        state['compiledBytesUnchangedDuringTests'] = state['productsAfterTests'] == products and all(
            sha(artifacts / 'bin' / test / 'release' / (test + ext)) == digest
            for test, values in state['testAssemblies'].items() for ext, digest in values.items())
        state['dependencyCopiesUnchangedDuringTests'] = all(
            sha(artifacts / 'bin' / row['test'] / 'release' / (row['component'] + row['extension'])) == row['sha256']
            for row in state['dependencyCopies'])
        state['nativeFixturesUnchangedDuringTests'] = all(sha(row['path']) == row['sha256'] for row in fixtures.values())
        state['numericGoldenFixtureUnchangedDuringTests'] = sha(golden_copy) == state['numericGoldenFixture']['sourceSha256']
        state['ringNonCompilerFixturesAfter'] = {str(path): sha(path) for path in (ring_golden, ring_oracle)}
        state['ringNonCompilerFixturesUnchanged'] = state['ringNonCompilerFixturesAfter'] == state['ringNonCompilerFixturesBefore']
        state['controlFilesUnchangedDuringTests'] = all(sha(path) == digest for path, digest in state['controlHashesBefore'].items())
        state['stepLogsUnchanged'] = all(
            sha(row['log']) == row['logSha256']
            and (row['stderrLog'] is None or sha(row['stderrLog']) == row['stderrLogSha256'])
            for row in state['steps'])
        for prefix, count in (('source-', SOURCE_CASE_COUNT), ('basis-', BASIS_CASE_COUNT), ('legacy-', LEGACY_CASE_COUNT)):
            actual = [row for row in state['steps'] if row['name'].startswith(prefix)]
            if len(actual) != count or len({row['name'] for row in actual}) != count or len({row['passNames'][0] for row in actual}) != count or any(not row['matchesExpected'] for row in actual):
                raise RuntimeError('Current required unique case coverage differs: ' + prefix)
        expected_steps = {'build-' + component for component in SIX} | {'build-' + test for test in TESTS}
        expected_steps |= {'build-solution', 'list-legacy-cases', 'numeric-13', 'ring-x-7',
                           'renderer-abi', 'audio-content', 'exact-static-read',
                           'invalid-ring-selector', 'invalid-ring-arity'}
        for selector, invalid in (('legacy-case', ('0', '301', 'not-an-int')),
                                  ('line-ring-source-case', ('0', '20', 'not-an-int')),
                                  ('canvas-basis-case', ('0', '17', 'not-an-int'))):
            expected_steps.update('invalid-' + selector + '-' + value for value in invalid)
        expected_steps.update('source-' + str(i).zfill(2) for i in range(1, 20))
        expected_steps.update('basis-' + str(i).zfill(2) for i in range(1, 17))
        expected_steps.update('legacy-' + str(i).zfill(3) for i in range(1, 301))
        if (len(expected_steps) != 362 or len(state['steps']) != len(expected_steps)
                or {row['name'] for row in state['steps']} != expected_steps
                or any(not row['matchesExpected'] or not row['naturalExit'] for row in state['steps'])):
            raise RuntimeError('The exact 362 natural execution steps are not complete.')
        state['superseded874EvidenceUnchanged'] = application_control.supersession_digest() == stop_digest
        state['passed'] = (state['compiledBytesUnchangedDuringTests'] and state['dependencyCopiesUnchangedDuringTests']
                           and state['nativeFixturesUnchangedDuringTests'] and state['numericGoldenFixtureUnchangedDuringTests']
                           and state['ringNonCompilerFixturesUnchanged']
                           and state['controlFilesUnchangedDuringTests'] and state['stepLogsUnchanged']
                           and state['superseded874EvidenceUnchanged']
                           and not failed.is_set())
    except Exception as error:
        failed.set()
        state['failure'] = str(error)
    finally:
        try:
            after = base.inputs()
        except Exception as error:
            after = {}
            state['postInputScanFailure'] = str(error)
        write(out / 'inputs-after.json', after)
        state['inputCount'] = len(inputs)
        state['inputsUnchanged'] = inputs == after
        state['nonCompilerControlsUnchanged'] = all(sha(ROOT / name) == digest for name, digest in state.get('nonCompilerControlsBefore', {}).items())
        state['passed'] = (state['passed'] and state['inputsUnchanged'] and state['nonCompilerControlsUnchanged']
                           and not state['activeChildren'] and not failed.is_set())
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        save()
        print(json.dumps({key: state.get(key) for key in ('passed', 'inputCount', 'inputsUnchanged', 'failure')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
