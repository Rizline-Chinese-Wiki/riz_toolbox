"""Build and validate the root-reviewed dynamic consumer in an isolated graph."""
from concurrent.futures import ThreadPoolExecutor
import argparse
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
CANDIDATE = ROOT / 'work/line-dynamic-consumer-next-20261113'
BASIS = ROOT / 'docs/validation/line-camera-scene-source-20261113'
HELPER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
MIN_COMMIT = 512 * 1024 * 1024
CHILD_SECONDS = 1800
SOURCE_TEST = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
BASIS_TEST = 'tests/RizToolbox.Judgements.Tests/CanvasBasis/NativeSeekCanvasBasisChecks.cs'


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def load_prepare():
    spec = importlib.util.spec_from_file_location('dynamic_shadow_prepare_validate',
                                                  WORK / 'prepare_shadow.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write(path, value):
    staged = Path(str(path) + '.pending')
    staged.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(staged, path)


def registry(path, expected):
    body = Path(path).read_text(encoding='utf-8-sig')
    require(body.count('[] Cases =') == 1, 'Case registry declaration changed')
    block = body.split('[] Cases =', 1)[1].split('};', 1)[0]
    names = re.findall(r'\("([^"\r\n]+)",', block)
    require(len(names) == expected and len(set(names)) == expected
            and block.count('("') == expected, 'Case registry names differ')
    return names


def execute(name, argv, environment, scanner, out, expected=None):
    require(scanner.available_commit() >= MIN_COMMIT, 'Available commit below 512 MiB before ' + name)
    stdout, stderr = out / (name + '.stdout.log'), out / (name + '.stderr.log')
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
    body, error_body = stdout.read_text(encoding='utf-8-sig'), stderr.read_text(encoding='utf-8-sig')
    passes = re.findall(r'^PASS (.+)$', body, re.M)
    runs = re.findall(r'^RUN (.+)$', body, re.M)
    good = (not forced and child.returncode == 0 and not error_body
            and not re.search(r'^FAIL\b', body, re.M))
    if expected is None:
        good = (good and ('已成功生成。' in body or 'Build succeeded.' in body)
                and re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0']
                and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0']
                and not passes and not runs)
    else:
        good = (good and passes == runs == [expected]
                and re.findall(r'(\d+)/(\d+) passed\.', body) == [('1', '1')])
    row = {'name': name, 'arguments': list(map(str, argv)), 'pid': child.pid,
           'exitCode': child.returncode, 'naturalExit': not forced,
           'matchesExpected': bool(good), 'passNames': passes, 'runNames': runs,
           'stdoutSha256': sha(stdout), 'stderrSha256': sha(stderr)}
    print('END ' + name + ' exit=' + str(child.returncode), flush=True)
    return row


def product_hashes(artifacts):
    output = {}
    folder = artifacts / 'bin'
    require(folder.is_dir(), 'Shadow build did not create binaries')
    for path in sorted(folder.rglob('*')):
        if path.is_file() and path.suffix in ('.dll', '.pdb'):
            output[str(path)] = sha(path)
    require(len(output) >= 18, 'Built production, test, or dependency pairs missing')
    return output


def same_hashes(values):
    try:
        return all(sha(path) == value for path, value in values.items())
    except OSError:
        return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--attempt', required=True)
    parser.add_argument('--candidate-sha256', required=True)
    opt = parser.parse_args()
    require(re.fullmatch(r'root-r[1-9][0-9]*', opt.attempt)
            and re.fullmatch(r'[0-9a-f]{64}', opt.candidate_sha256),
            'Explicit attempt and final candidate SHA-256 are required')
    out = Path('D:/riz-build-cache-next/line-dynamic-consumer-read-20261113') / opt.attempt
    require(not out.exists(), 'Preserve prior dynamic shadow attempt')
    prepare = load_prepare()
    prepared = read(WORK / 'prepared.json')
    require(prepared['schema'] == 'line-ring-dynamic-shadow-prepared/v1'
            and prepared['candidateManifestSha256'] == opt.candidate_sha256
            and prepared['mainInputs'] == 895 and prepared['shadowInputs'] == 898
            and prepared['sourceCases'] == 30
            and prepared['selectedSourceCases'] == list(range(25, 31))
            and prepared['selectedSceneCases'] == list(range(20, 25))
            and prepared['selectedBasisCases'] == [1]
            and prepared['mainApplied'] is False
            and prepared['built'] is False and prepared['testsExecuted'] is False,
            'Prepared scope or explicit candidate differs')
    summary, before, references, fixtures, golden = prepare.accepted_basis(
        prepared['basisSummarySha256'])
    require(prepared['basisResultSha256'] == summary['resultSha256']
            and prepared['basisInputGraphSha256'] == summary['inputGraphSha256']
            and prepared['referenceFilesSha256'] == references
            and prepared['nativeFixtures'] == fixtures
            and prepared['goldenSha256'] == sha(golden),
            'Accepted basis archive changed since preparation')
    scanner = prepare.scanner_module()
    require(scanner.inputs() == before and len(before) == 895,
            'Main is not the accepted scene graph')
    manifest, candidate, snapshot, expected_graph, source_names = prepare.candidate_snapshot(
        opt.candidate_sha256, before)
    require(candidate == prepared['candidateFilesSha256']
            and snapshot == prepared['candidateSnapshotSha256']
            and len(expected_graph) == 898 and len(source_names) == 30,
            'Candidate payload or 898 shadow graph changed')
    receipt_path = WORK / 'root-review-receipt.json'
    prepare.review_receipt(receipt_path, prepared['reviewReceiptSha256'],
                           opt.candidate_sha256, candidate, snapshot,
                           prepared['basisSummarySha256'])
    review_note = (ROOT / read(receipt_path)['review']['path']).resolve()
    controls = {str(path): sha(path) for path in (
        Path(__file__).resolve(), WORK / 'prepare_shadow.py', WORK / 'observe.py',
        WORK / 'Directory.Build.props', WORK / 'NuGet.Config',
        WORK / 'Adapter.DynamicShadow.csproj', WORK / 'Judgements.DynamicShadow.csproj',
        WORK / 'Program.cs', WORK / 'prepared.json', receipt_path,
        review_note, CANDIDATE / 'candidate-manifest.json', HELPER,
        BASIS / 'validation-summary.json', BASIS / 'run/result.json',
        BASIS / 'run/inputs-after.json', BASIS / 'artifact-manifest.json',
        BASIS / 'run/controller-handle-exit.json', BASIS / 'run/root-exit.json',
        BASIS / 'run/publication-pin.json', golden)}
    require(all(sha(WORK / name) == value
                for name, value in prepared['generatedControlsSha256'].items()),
            'Generated shadow control changed after preparation')
    candidate_paths = {str(CANDIDATE / name): value for name, value in candidate.items()}
    basis_names = registry(ROOT / BASIS_TEST, 16)
    require(before[BASIS_TEST] == sha(ROOT / BASIS_TEST)
            and source_names[:24] == registry(ROOT / SOURCE_TEST, 24),
            'Accepted basis or source registry changed')
    selections = [
        ('source-' + str(index).zfill(2), 'source', index, source_names[index - 1])
        for index in range(25, 31)
    ] + [
        ('scene-' + str(index).zfill(2), 'source', index, source_names[index - 1])
        for index in range(20, 25)
    ] + [('basis-01', 'basis', 1, basis_names[0])]
    require(len(selections) == 12 and len({name for name, _, _, _ in selections}) == 12,
            'Twelve-case shadow selection differs')
    environment = os.environ.copy()
    environment.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                       DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1',
                       DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    for key, row in fixtures.items():
        environment[key] = row['path']
    frozen = {**controls, **candidate_paths, **references,
              **{row['path']: row['sha256'] for row in fixtures.values()}}
    require(all(sha(path) == value for path, value in frozen.items())
            and sha(golden) == prepared['goldenSha256']
            and scanner.inputs() == before,
            'Source, control, reference, fixture or golden changed before run')
    out.mkdir(parents=True)
    artifacts = out / 'artifacts'
    write(out / 'main-inputs-before.json', before)
    state = {
        'schema': 'line-ring-dynamic-shadow-validation/v1',
        'controllerPid': os.getpid(),
        'startedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'passed': False, 'mainInputsBefore': before, 'expectedShadowInputs': expected_graph,
        'candidateManifestSha256': opt.candidate_sha256,
        'candidateInputsBefore': candidate, 'candidateSnapshotSha256': snapshot,
        'basisSummarySha256': prepared['basisSummarySha256'],
        'basisResultSha256': prepared['basisResultSha256'],
        'basisInputGraphSha256': prepared['basisInputGraphSha256'],
        'controlsBefore': controls, 'references': references,
        'nativeFixtures': fixtures, 'goldenSha256': prepared['goldenSha256'],
        'caseNames': source_names, 'selectedCases': [name for name, _, _, _ in selections],
        'steps': [], 'activeChildren': [],
        'maxSimultaneousTestChildren': 2, 'minimumAvailableCommitBytes': MIN_COMMIT,
        'childTimeoutSeconds': CHILD_SECONDS,
        'mainApplied': False, 'sdkGoalComplete': False,
        'completeSeekPermitted': False, 'atomicSnapshot': False,
        'gameStarted': False, 'installed': False,
    }

    def save():
        write(out / 'result.json', state)

    def stable():
        return (scanner.inputs() == before
                and all(sha(path) == value for path, value in frozen.items())
                and sha(golden) == prepared['goldenSha256'])

    def record(row):
        state['steps'].append(row)
        save()
        require(row['matchesExpected'], 'Shadow step failed: ' + row['name'])

    try:
        require(stable(), 'Source/control change before build')
        name = 'build'
        state['activeChildren'] = [name]
        save()
        dotnet = scanner.DOTNET
        row = execute(name, [dotnet, 'build', WORK / 'Judgements.DynamicShadow.csproj',
                             '-c', 'Release', '--artifacts-path', artifacts,
                             '--verbosity', 'minimal', '--disable-build-servers',
                             '-m:1', '-p:UseSharedCompilation=false',
                             '-p:NuGetAudit=false',
                             '-p:RestoreConfigFile=' + str(WORK / 'NuGet.Config')],
                      environment, scanner, out)
        state['activeChildren'] = []
        record(row)
        require(stable(), 'Source/control change after build')
        state['binariesBeforeTests'] = product_hashes(artifacts)
        assembly = artifacts / 'bin/Judgements.DynamicShadow/release/RizToolbox.Judgements.Tests.dll'
        adapter = artifacts / 'bin/Adapter.DynamicShadow/release/RizToolbox.Adapter.dll'
        require(assembly.is_file() and adapter.is_file(), 'Missing dynamic shadow outputs')
        state['productionPair'] = {str(path): sha(path)
                                   for path in (adapter, adapter.with_suffix('.pdb'))}
        state['testPair'] = {str(path): sha(path)
                             for path in (assembly, assembly.with_suffix('.pdb'))}
        dependencies = {}
        for owner, products in (
            ('Adapter.DynamicShadow', ('RizToolbox.Sdk', 'RizToolbox.Core', 'RizToolbox.Runtime')),
            ('Judgements.DynamicShadow', ('RizToolbox.Sdk', 'RizToolbox.Core',
                                          'RizToolbox.Runtime', 'RizToolbox.Adapter'))):
            for component in products:
                for ext in ('.dll', '.pdb'):
                    path = artifacts / 'bin' / owner / 'release' / (component + ext)
                    expected = (state['productionPair'][str(adapter.with_suffix(ext))]
                                if component == 'RizToolbox.Adapter'
                                else references[str(BASIS / 'binaries/production' / (component + ext))])
                    require(sha(path) == expected, 'Built dependency differs: ' + str(path))
                    dependencies[str(path)] = expected
        state['dependencies'] = dependencies
        require(len(state['productionPair']) == 2 and len(state['testPair']) == 2
                and len(dependencies) == 14,
                'Production/test pairs or dependency map differs')
        output_golden = assembly.parent / 'line-ring-numeric-goldens.json'
        require(sha(output_golden) == prepared['goldenSha256'],
                'Copied numeric golden differs from accepted basis')
        state['outputGoldenSha256'] = sha(output_golden)
        save()
        with ThreadPoolExecutor(max_workers=2) as pool:
            for start in range(0, len(selections), 2):
                require(stable(), 'Source/control change before test pair')
                jobs = []
                for name, selector, index, expected in selections[start:start + 2]:
                    argv = [dotnet, assembly, selector, index]
                    jobs.append((name, pool.submit(execute, name, argv, environment,
                                                   scanner, out, expected)))
                state['activeChildren'] = [name for name, _ in jobs]
                save()
                rows = [future.result() for _, future in jobs]
                state['activeChildren'] = []
                for row in rows:
                    record(row)
                require(stable(), 'Source/control change after test pair')
                require(product_hashes(artifacts) == state['binariesBeforeTests']
                        and sha(output_golden) == state['outputGoldenSha256'],
                        'Tested binary or golden changed during pair')
        state['binariesAfterTests'] = product_hashes(artifacts)
        require(state['binariesAfterTests'] == state['binariesBeforeTests']
                and len(state['steps']) == 13
                and sum(len(row['passNames']) for row in state['steps']) == 12
                and all(row['naturalExit'] and row['matchesExpected'] for row in state['steps'])
                and stable(), 'Fresh dynamic shadow did not finish twelve natural checks')
        state['passed'] = True
    except Exception as error:
        state['failure'] = str(error)
    finally:
        state['mainInputsAfter'] = scanner.inputs()
        state['mainInputsUnchanged'] = state['mainInputsAfter'] == before
        state['candidateAndControlsUnchanged'] = same_hashes({**controls, **candidate_paths})
        state['referencesUnchanged'] = same_hashes(references)
        state['fixturesUnchanged'] = same_hashes(
            {row['path']: row['sha256'] for row in fixtures.values()})
        state['goldenUnchanged'] = same_hashes({str(golden): prepared['goldenSha256']})
        try:
            state['compiledBytesUnchanged'] = (
                'binariesBeforeTests' in state
                and product_hashes(artifacts) == state['binariesBeforeTests']
                and same_hashes({**state.get('productionPair', {}), **state.get('testPair', {}),
                                 **state.get('dependencies', {})})
                and ('outputGoldenSha256' not in state or
                     sha(assembly.parent / 'line-ring-numeric-goldens.json') == state['outputGoldenSha256']))
        except (OSError, RuntimeError):
            state['compiledBytesUnchanged'] = False
        state['passed'] = (state['passed'] and state['mainInputsUnchanged']
                           and state['candidateAndControlsUnchanged']
                           and state['referencesUnchanged'] and state['fixturesUnchanged']
                           and state['goldenUnchanged'] and state['compiledBytesUnchanged'])
        state['activeChildren'] = []
        state['finishedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
        save()
        print(json.dumps({'passed': state['passed'], 'steps': len(state['steps']),
                          'passNames': sum(len(row['passNames']) for row in state['steps']),
                          'failure': state.get('failure'),
                          'resultSha256': sha(out / 'result.json')}), flush=True)
    return 0 if state['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
