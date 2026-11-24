"""Audit and atomically archive the final offline parent/local writer batch.

Publication needs explicit hashes for the root's actual controller exit, the
original Windows-handle receipt, and the terminal main result. --check-only
reads only the archive and this script's built-in path constants.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import uuid

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox')
WORK = ROOT / 'work/line-endpoint-parent-evidence-20261113'
INTEGRATION = ROOT / 'work/line-endpoint-parent-integration-20261113'
RUN = Path('D:/riz-build-cache-next/line-endpoint-parent-integration-20261113/main-r1')
CONTINUATION = RUN.parent / 'continuation-r1'
ORIGINAL_RESULT_SHA = '8de722ada3bcd136895b814a5cead57ee35cf39add77d3f94082113170ac260d'
RESUME_SHA = 'bf8720621e10d0abdb9d949f431968c42cf42c98cad3095fd6e803b0b3968c1d'
ARCHIVE = ROOT / 'docs/validation/line-endpoint-parent-writer-20261113'
PRIOR = ROOT / 'docs/validation/line-endpoint-writer-20261113'
PRELIMINARY = ROOT / 'work/line-endpoint-parent-root-20261113/root-r2-snapshot'
CANDIDATE = ROOT / 'work/line-endpoint-parent-writer-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
OFFLINE = ROOT / 'work/ring-x-numeric-20261113/NuGet.Config'
FIXTURE_RECORD = ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
JUDGEMENTS = 'RizToolbox.Judgements.Tests'
RUNTIME_TEST = 'RizToolbox.Runtime.Tests'
SIX = ['RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
SHA_RE = re.compile(r'[0-9a-f]{64}\Z')
PRIOR_SUMMARY_SHA = 'adbc97f1301e5c163165d2fa21fc4725a8c9892c9960f0a58c69e65e10f60fc6'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
MAX_FILES = 1600
MAX_BYTES = 256 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024
GROUPS = {
    'target-provider': ('--line-ring-target-provider-checks', 6, 'Line ring source target provider ',
                        'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.TargetProvider.cs',
                        '[] TargetProviderCases ='),
    'chart-occurrence': ('--line-chart-occurrence-checks', 5, 'Line chart occurrence ',
                         'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineChartOccurrenceChecks.cs',
                         '[] Cases ='),
    'endpoint-local': ('--line-ring-endpoint-local-checks', 6, 'Line ring numeric endpoint ',
                       'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs',
                       '[] Cases ='),
    'numeric': ('--line-ring-numeric-checks', 13, 'Line ring numeric ',
                'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingNumericChecks.cs',
                '[] Cases ='),
    'ring-x': ('--line-ring-ring-x-numeric-checks', 7, 'Line ring numeric ring ',
               'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingXNumericChecks.cs',
               '[] Cases ='),
}
SOURCE_FILE = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
BASIS_FILE = 'tests/RizToolbox.Judgements.Tests/CanvasBasis/NativeSeekCanvasBasisChecks.cs'
WRITER_FILE = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs'
PROGRAM_FILE = 'tests/RizToolbox.Judgements.Tests/Program.cs'
RUNTIME_CASES = (
    ('VoidPublicationRegression', 11), ('ExactFieldWriteRegression', 20),
    ('GuardedFieldPublicationRegression', 5), ('RendererQuaternionRegression', 8),
    ('TweenGraphReadRegression', 7),
)
CONTROLS = {
    'validate_main.py': INTEGRATION / 'validate_main.py',
    'stage.py': INTEGRATION / 'stage.py',
    'apply.py': INTEGRATION / 'apply.py',
    'observe.py': INTEGRATION / 'observe.py',
    'resume_main.py': INTEGRATION / 'resume_main.py',
    'INTEGRATION-PLAN.md': INTEGRATION / 'INTEGRATION-PLAN.md',
    'payload-manifest.json': INTEGRATION / 'payload-manifest.json',
    'application-record.json': INTEGRATION / 'application-record.json',
    'review-receipt.json': INTEGRATION / 'review-receipt.json',
    'inputs-before.json': INTEGRATION / 'inputs-before.json',
    'Build.ps1': ROOT / 'scripts/Build.ps1',
    'input-scanner.py': SCANNER,
    'NuGet.Config': OFFLINE,
}
RUN_CONTROLS = (INTEGRATION / 'validate_main.py', INTEGRATION / 'stage.py',
                INTEGRATION / 'apply.py', INTEGRATION / 'payload-manifest.json',
                INTEGRATION / 'review-receipt.json', INTEGRATION / 'application-record.json',
                ROOT / 'scripts/Build.ps1', INTEGRATION / 'resume_main.py', INTEGRATION / 'observe.py')


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def safe_name(name):
    require(isinstance(name, str) and name and '\\' not in name and not name.startswith('/'),
            'Unsafe archive name')
    require(all(part not in ('', '.', '..') and ':' not in part for part in name.split('/')),
            'Unsafe archive name: ' + name)
    return name


def safe_file(path):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), 'Missing or linked evidence file: ' + str(path))
    require(path.stat().st_size <= MAX_FILE_BYTES, 'Evidence file exceeds 64 MiB: ' + str(path))
    return path


def sha(path):
    with safe_file(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def unique_keys(pairs):
    value = {}
    for key, item in pairs:
        require(key not in value, 'Duplicate JSON key: ' + key)
        value[key] = item
    return value


def read(path):
    return json.loads(safe_file(path).read_text(encoding='utf-8-sig'), object_pairs_hook=unique_keys)


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def timestamp(value):
    parsed = dt.datetime.fromisoformat(value)
    require(parsed.tzinfo is not None, 'Unzoned evidence time')
    return parsed.astimezone(dt.timezone.utc)


def case_names(path, marker, expected=None):
    body = safe_file(path).read_text(encoding='utf-8-sig')
    require(body.count(marker) == 1, 'Registry marker ambiguous: ' + str(path))
    block = body.split(marker, 1)[1].split('};', 1)[0]
    names = re.findall(r'(?m)^\s*,?\s*\("([^"\r\n]+)",', block)
    require(names and len(set(names)) == len(names)
            and (expected is None or len(names) == expected),
            'Registry names/count differ: ' + str(path))
    return names


class View:
    def __init__(self, base, archived):
        self.base = Path(base)
        self.archived = archived

    def input(self, name):
        return self.base / 'compiler-inputs' / safe_name(name) if self.archived else ROOT / safe_name(name)

    def control(self, name):
        require(name in CONTROLS, 'Unknown control: ' + name)
        return self.base / 'controls' / name if self.archived else CONTROLS[name]

    def run(self, name):
        if self.archived:
            return self.base / 'run' / safe_name(name)
        return (RUN if name == 'legacy-case-list.json' else self.base) / safe_name(name)

    def log(self, name, stream):
        filename = safe_name(name + '.' + stream + '.log')
        if self.archived:
            return self.base / 'run/logs' / filename
        original = name in ('six-release', 'solution', 'legacy-listing') or (
            name.startswith('writer-') and int(name[7:]) <= 14)
        return (RUN if original else self.base) / filename

    def prior(self, name):
        return self.base / 'prior-891' / safe_name(name) if self.archived else PRIOR / safe_name(name)

    def payload(self, section, name):
        require(section in ('files', 'before'), 'Unknown payload section')
        return (self.base / 'payload' / section / safe_name(name) if self.archived
                else INTEGRATION / section / safe_name(name))

    def candidate(self, name):
        return (self.base / 'candidate-files' / safe_name(name) if self.archived
                else CANDIDATE / safe_name(name))

    def production(self, component, ext):
        return (self.base / 'binaries/production' / (component + ext) if self.archived
                else RUN / 'artifacts/bin' / component / 'release' / (component + ext))

    def test(self, component, ext):
        return (self.base / 'binaries/test' / component / (component + ext) if self.archived
                else RUN / 'artifacts/bin' / component / 'release' / (component + ext))

    def dependency(self, component, product, ext):
        return (self.base / 'binaries/dependencies' / component / (product + ext) if self.archived
                else RUN / 'artifacts/bin' / component / 'release' / (product + ext))

    def fixture(self, key, original):
        return (self.base / 'fixtures/native' / key / Path(original).name if self.archived
                else Path(original))

    def extra(self, name, original):
        return self.base / safe_name(name) if self.archived else Path(original)


def step_order(writer_count):
    return (['six-release', 'solution', 'legacy-listing']
            + ['writer-' + str(index).zfill(2) for index in range(1, writer_count + 1)]
            + list(GROUPS) + ['invalid-writer-name', 'invalid-writer-arity']
            + ['source-' + str(index) for index in (13, 14, 17, 18, 19)]
            + ['basis-01', 'renderer-abi'])


def step_spec(name, writer_names):
    test = RUN / 'artifacts/bin' / JUDGEMENTS / 'release' / (JUDGEMENTS + '.dll')
    prefix = [str(DOTNET), str(test)]
    if name in ('six-release', 'solution'):
        project = ROOT / ('src/RizToolbox.Loader/RizToolbox.Loader.csproj' if name == 'six-release' else 'RizToolbox.sln')
        return ([str(DOTNET), 'build', str(project), '-c', 'Release', '--artifacts-path', str(RUN / 'artifacts'),
                 '--verbosity', 'minimal', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                 '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(OFFLINE)], 0, [], True)
    if name == 'legacy-listing':
        return prefix + ['--list-legacy-cases'], 0, [], True
    if name.startswith('writer-'):
        index = int(name[7:])
        require(1 <= index <= len(writer_names), 'Writer index outside final registry')
        return prefix + ['--line-ring-endpoint-writer-case', str(index)], 0, [writer_names[index - 1]], True
    if name in GROUPS:
        selector, _, _, _, _ = GROUPS[name]
        return prefix + [selector], 0, None, True
    if name == 'invalid-writer-name':
        return prefix + ['--line-ring-endpoint-writer-cases'], 2, [], False
    if name == 'invalid-writer-arity':
        return prefix + ['--line-ring-endpoint-writer-case', '1', 'extra'], 2, [], False
    if name.startswith('source-'):
        index = int(name[7:])
        require(index in (13, 14, 17, 18, 19), 'Unexpected selected source case')
        return prefix + ['--line-ring-source-case', str(index)], 0, None, True
    if name == 'basis-01':
        return prefix + ['--canvas-basis-case', '1'], 0, None, True
    if name == 'renderer-abi':
        runtime = RUN / 'artifacts/bin' / RUNTIME_TEST / 'release' / (RUNTIME_TEST + '.dll')
        return [str(DOTNET), str(runtime), '--renderer-abi-checks'], 0, None, True
    raise RuntimeError('Unexpected validation step: ' + name)


def check_preliminary(view, meta):
    result_path = view.extra('preliminary/root-r2-result.json', PRELIMINARY / 'result.json')
    observation_path = view.extra('preliminary/root-r2-root-observation.json',
                                  PRELIMINARY / 'root-observation.json')
    require(sha(result_path) == meta['preliminaryResultSha256']
            and sha(observation_path) == meta['preliminaryRootObservationSha256'],
            'Preliminary 29-case files differ from explicit pins')
    preliminary = read(result_path)
    observation = read(observation_path)
    require(preliminary['passed'] is True
            and observation['preliminaryCandidatePassed'] is True
            and observation['cases'] == 29
            and observation['observedRootExec']['actualExitCode'] == 0
            and observation['resultSha256'] == meta['preliminaryResultSha256']
            and observation['laterRepairedCandidateValidated'] is False
            and observation['mainApplied'] is False
            and observation['sdkGoalComplete'] is False
            and observation['completeSeekPermitted'] is False,
            'Preliminary candidate record was mistaken for final repaired validation')


def check_exit_receipts(view, meta, result):
    result_path = view.run('result.json')
    root_path = view.run('root-controller-exit.json') if view.archived else Path(meta['rootExitPath'])
    handle_path = view.run('controller-handle-exit.json')
    require(sha(result_path) == meta['resultSha256']
            and sha(root_path) == meta['rootExitSha256']
            and sha(handle_path) == meta['handleExitSha256'],
            'Main result or actual exit receipts differ from supplied SHA-256 pins')
    root = read(root_path)
    root_exit = root.get('observedRootExec', root)
    require(root.get('observedBy') == 'root'
            and root.get('controllerPid') == result['controllerPid']
            and root.get('controllerScriptSha256') == sha(view.control('resume_main.py'))
            and root.get('resultSha256') == meta['resultSha256']
            and root_exit.get('actualExitCode') == 0,
            'Root did not record original main controller exit 0 with exact result/script')
    root_observed = timestamp(root['observedUtc'])
    handle = read(handle_path)
    require(handle['schema'] == 'original-controller-handle-exit/v1'
            and handle['controllerPid'] == result['controllerPid']
            and handle['controllerScriptSha256'] == sha(view.control('resume_main.py'))
            and handle['observerSha256'] == sha(view.control('observe.py'))
            and handle['handleKeptUntilExit'] is True
            and handle['actualExitCode'] == 0
            and handle['resultSha256AtExit'] == meta['resultSha256']
            and handle['controllerScriptUnchanged'] is True
            and handle['sdkGoalComplete'] is False
            and handle['completeSeekPermitted'] is False,
            'Original Windows handle receipt does not certify the final main run')
    created, started, alive, finished, result_finished = map(timestamp, (
        handle['createdUtc'], result['startedUtc'], handle['observedAliveUtc'],
        handle['finishedUtc'], result['finishedUtc']))
    require(created <= started <= alive <= finished <= root_observed
            and result_finished <= finished
            and (started - created).total_seconds() < 600,
            'Original controller/handle/root exit chronology differs')


def check_payload(view, meta, inputs, prior_inputs):
    manifest_path = view.control('payload-manifest.json')
    manifest = read(manifest_path)
    require(sha(manifest_path) == meta['payloadManifestSha256']
            and manifest['schema'] == 'line-endpoint-parent-main-payload/v1'
            and manifest['acceptedSummarySha256'] == PRIOR_SUMMARY_SHA
            and manifest['inputScannerSha256'] == SCANNER_SHA
            and manifest['acceptedInputs'] == 891 and manifest['expectedInputs'] == 892
            and manifest['oldWriterCases'] == 17
            and manifest['legacyCasesListed'] == 300
            and manifest['freshMainValidationPassed'] is False
            and manifest['sdkGoalComplete'] is False
            and manifest['completeSeekPermitted'] is False,
            'Final payload manifest changed scope or overclaims validation')
    records = manifest['files']
    require(len(records) == 8 and len({row['path'] for row in records}) == 8,
            'Final payload must contain the exact eight reviewed paths')
    require({row['path'] for row in records} == {
        'src/RizToolbox.Runtime/NativeRuntime.LineEndpointParentMetadata.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasPositionNativeProfile.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs',
        'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs',
        'tests/RizToolbox.Judgements.Tests/CanvasPositions/FakeNative.CanvasPositions.cs',
        WRITER_FILE, PROGRAM_FILE}, 'Payload target whitelist changed')
    require(manifest['writerCases'] == len(manifest['writerCaseNames']) >= 29
            and manifest['defaultCases'] == 372 + manifest['writerCases'],
            'Final writer/default registry counts differ')
    candidate_hashes = manifest['candidateFilesSha256']
    require(len(candidate_hashes) == 10
            and all(sha(view.candidate(name)) == digest for name, digest in candidate_hashes.items()),
            'Final candidate source bytes differ from staged review')
    snapshot = hashlib.sha256(json.dumps(candidate_hashes, sort_keys=True,
                                         separators=(',', ':')).encode('utf-8')).hexdigest()
    require(snapshot == manifest['candidateSnapshotSha256'], 'Final candidate snapshot differs')
    ready = read(view.control('review-receipt.json'))
    require(sha(view.control('review-receipt.json')) == manifest['reviewReceiptSha256']
            and ready['readyForMainIntegration'] is True
            and ready['reviewedBy'] == 'root'
            and ready['candidateSnapshotSha256'] == snapshot,
            'Root ready review did not authorize these candidate bytes')
    application = read(view.control('application-record.json'))
    require(application['schema'] == 'line-endpoint-parent-main-application/v1'
            and application['appliedToMain'] is True
            and application['acceptedInputs'] == 891 and application['newInputs'] == 892
            and application['manifestSha256'] == meta['payloadManifestSha256']
            and application['reviewReceiptSha256'] == manifest['reviewReceiptSha256']
            and application['writerCases'] == manifest['writerCases']
            and application['defaultCases'] == manifest['defaultCases']
            and application['freshMainValidationPassed'] is False
            and application['sdkGoalComplete'] is False
            and application['completeSeekPermitted'] is False,
            'Main application record differs from final payload')
    require(read(view.control('inputs-before.json')) == prior_inputs,
            'Integration baseline graph differs from accepted 891')
    expected = dict(prior_inputs)
    for row in records:
        name = safe_name(row['path'])
        require(row['kind'] == ('replacement' if name in prior_inputs else 'new')
                and row['baseSha256'] == prior_inputs.get(name)
                and row['payloadSha256'] == sha(view.payload('files', name)) == inputs[name],
                'Staged/main source or accepted base differs: ' + name)
        if row['baseSha256'] is not None:
            require(sha(view.payload('before', name)) == row['baseSha256'],
                    'Rollback bytes differ: ' + name)
        if row['candidatePath'] is not None:
            candidate = safe_file(view.candidate(row['candidatePath'])).read_bytes()
            require(row['candidateSha256'] == hashlib.sha256(candidate).hexdigest(),
                    'Candidate payload provenance differs: ' + name)
            if name == WRITER_FILE:
                candidate = candidate.replace(b'"actual endpoint writer ', b'"Line ring source endpoint writer ')
                candidate = candidate.replace(b'"actual parent writer ', b'"Line ring source parent writer ')
            require(candidate == safe_file(view.payload('files', name)).read_bytes(),
                    'Payload edits exceed registry label conversion: ' + name)
        else:
            require(name == PROGRAM_FILE, 'Only Program may lack a candidate source')
        expected[name] = row['payloadSha256']
    require(expected == inputs and set(inputs) - set(prior_inputs) == {
        'src/RizToolbox.Runtime/NativeRuntime.LineEndpointParentMetadata.cs'},
        '892/891 compiler graph delta differs')
    return manifest


def check_registry_and_steps(view, result, manifest, legacy):
    writer_names = case_names(view.input(WRITER_FILE), '[] EndpointWriterCases =')
    require(writer_names == manifest['writerCaseNames']
            and all(name.startswith(('Line ring source endpoint writer ',
                                     'Line ring source parent writer ')) for name in writer_names),
            'Final main writer registry names/order differ from reviewed payload')
    group_names = {name: case_names(view.input(file), marker, count)
                   for name, (_, count, _, file, marker) in GROUPS.items()}
    source_names = case_names(view.input(SOURCE_FILE), '[] Cases =', 19)
    basis_names = case_names(view.input(BASIS_FILE), '[] Cases =', 16)
    runtime_names = [name for file, count in RUNTIME_CASES
                     for name in case_names(view.input('tests/RizToolbox.Runtime.Tests/' + file + '.cs'),
                                            '[] Cases =', count)]
    require(len(runtime_names) == len(set(runtime_names)) == 51,
            'Runtime ABI registry names differ')
    program = safe_file(view.input(PROGRAM_FILE)).read_text(encoding='utf-8-sig')
    usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', program)
    require(len(usage) == 1
            and '--line-ring-endpoint-writer-case N' in usage[0]
            and 'EndpointWriterCases[writerCase - 1]' in program
            and 'writerCase <= RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases.Length' in program
            and '!test.Name.StartsWith("Line ring source ", StringComparison.Ordinal)' in program,
            'Main numbered writer selector, usage or legacy exclusion differs')
    order = step_order(len(writer_names))
    steps = result['steps']
    require([row['name'] for row in steps] == order and len(steps) == len(writer_names) + 17,
            'Fresh main step order/count differs from final writer registry')
    listing = read(view.run('legacy-case-list.json'))
    require(listing == legacy and len(listing) == 300
            and [row['index'] for row in listing] == list(range(1, 301))
            and len({row['name'] for row in listing}) == 300,
            'Old 300 names/order changed')
    for row in steps:
        name = row['name']
        argv, expected_exit, names, stderr_empty = step_spec(name, writer_names)
        if name in GROUPS:
            names = group_names[name]
        elif name.startswith('source-'):
            names = [source_names[int(name[7:]) - 1]]
        elif name == 'basis-01':
            names = [basis_names[0]]
        elif name == 'renderer-abi':
            names = runtime_names
        require(row['arguments'] == argv and row['exitCode'] == expected_exit
                and row['naturalExit'] is True and row['matchesExpected'] is True
                and isinstance(row['pid'], int) and row['pid'] > 0,
                'Step argv, process or exit differs: ' + name)
        stdout, stderr = view.log(name, 'stdout'), view.log(name, 'stderr')
        require(sha(stdout) == row['stdoutSha256'] and sha(stderr) == row['stderrSha256'],
                'Step log bytes differ: ' + name)
        body = stdout.read_text(encoding='utf-8-sig')
        errors = stderr.read_text(encoding='utf-8-sig')
        runs = re.findall(r'(?m)^RUN (.+)$', body)
        passes = re.findall(r'(?m)^PASS (.+)$', body)
        totals = re.findall(r'(\d+)/(\d+) passed\.', body)
        require(row['runNames'] == runs and row['passNames'] == passes
                and not re.search(r'(?m)^FAIL\b', body + errors),
                'Step RUN/PASS record or failure differs: ' + name)
        if stderr_empty:
            require(not errors, 'Successful step has stderr: ' + name)
        if name in ('six-release', 'solution'):
            require(not runs and not passes and not totals
                    and ('已成功生成。' in body or 'Build succeeded.' in body)
                    and re.findall(r'(?m)^\s*(\d+) (?:个错误|Error\(s\))\s*$', body) == ['0'],
                    'Build success/zero errors differ: ' + name)
            if name == 'six-release':
                require(re.findall(r'(?m)^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body) == ['0'],
                        'Six production Release warnings differ')
        elif name == 'legacy-listing':
            require(not runs and not passes and not totals
                    and json.loads(body, object_pairs_hook=unique_keys) == listing,
                    'Legacy stdout listing differs')
        elif name.startswith('invalid-'):
            require(not body and errors == usage[0] + '\n'
                    and not runs and not passes and not totals,
                    'Invalid selector exact usage/exit differs: ' + name)
        elif name == 'renderer-abi':
            require(not runs and passes == names and totals == [('51', '51')],
                    'Runtime ABI 51 PASS-only names/order differ')
        else:
            require(runs == passes == names and len(passes) == len(set(passes))
                    and totals == [(str(len(names)), str(len(names)))],
                    'Judgements RUN/PASS names/order/total differ: ' + name)
    require(sum(len(row['passNames']) for row in steps) == len(writer_names) + 94,
            'Focused PASS total differs from independent registry counts')
    return len(steps), len(writer_names) + 94


def check_binaries_and_fixtures(view, result, inputs):
    products = result['productsBeforeTests']
    require(result['productsAfterSix'] == products == result['productsAfterTests']
            and set(products) == set(SIX)
            and all(set(products[name]) == {'.dll', '.pdb'} for name in SIX),
            'Six production DLL/PDB pairs changed after fresh builds/tests')
    for component in SIX:
        for ext in ('.dll', '.pdb'):
            require(sha(view.production(component, ext)) == products[component][ext],
                    'Fresh production binary byte differs: ' + component + ext)
    test_paths = {str(RUN / 'artifacts/bin' / component / 'release' / (component + ext))
                  for component in (JUDGEMENTS, RUNTIME_TEST) for ext in ('.dll', '.pdb')}
    test_pairs = result['testPairs']
    require(set(test_pairs) == test_paths and len(test_pairs) == 4,
            'Two fresh test DLL/PDB pair inventory differs')
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in ('.dll', '.pdb'):
            original = str(RUN / 'artifacts/bin' / component / 'release' / (component + ext))
            require(sha(view.test(component, ext)) == test_pairs[original],
                    'Fresh test binary byte differs: ' + component + ext)
    dependencies = result['dependencies']
    expected = {
        str(RUN / 'artifacts/bin' / component / 'release' / (product + ext)): products[product][ext]
        for component, chosen in ((JUDGEMENTS, SIX[:4]),
                                  (RUNTIME_TEST, ('RizToolbox.Runtime', 'RizToolbox.Sdk')))
        for product in chosen for ext in ('.dll', '.pdb')}
    require(dependencies == expected and len(dependencies) == 12,
            'Twelve fresh test dependency copies differ from production')
    for component, chosen in ((JUDGEMENTS, SIX[:4]),
                              (RUNTIME_TEST, ('RizToolbox.Runtime', 'RizToolbox.Sdk'))):
        for product in chosen:
            for ext in ('.dll', '.pdb'):
                require(sha(view.dependency(component, product, ext)) == products[product][ext],
                        'Fresh test dependency byte differs: ' + component + '/' + product + ext)
    fixtures = result['nativeFixtures']
    require(set(fixtures) == {'RIZ_INITIALIZATION_ABI_FIXTURE', 'RIZ_INITIALIZATION_ABI_DOBBY',
                              'RIZ_INITIALIZATION_CONTEXT_HELPER'}
            and read(view.extra('fixtures/native-source-record.json', FIXTURE_RECORD))['nativeFixtures'] == fixtures,
            'Three pinned native fixture source records differ')
    for key, item in fixtures.items():
        require(SHA_RE.fullmatch(item['sha256']) is not None
                and sha(view.fixture(key, item['path'])) == item['sha256'],
                'Native fixture byte differs: ' + key)
    golden = 'tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'
    require(sha(view.extra('fixtures/line-ring-numeric-goldens.json',
                           RUN / 'artifacts/bin' / JUDGEMENTS / 'release/line-ring-numeric-goldens.json'))
            == inputs[golden], 'Built numeric golden fixture differs from fresh source graph')


def audit(view, meta):
    require(all(SHA_RE.fullmatch(meta[name]) for name in (
        'resultSha256', 'rootExitSha256', 'handleExitSha256',
        'payloadManifestSha256', 'preliminaryResultSha256',
        'preliminaryRootObservationSha256')), 'Explicit evidence hash is malformed')
    result = read(view.run('result.json'))
    writer_count = result['writerCases']
    require(result['schema'] == 'line-endpoint-parent-main-continuation/v1'
            and result['passed'] is True and not result.get('failure')
            and result['compilerInputs'] == 892
            and writer_count >= 29
            and result['defaultExpectedCases'] == 389 - 17 + writer_count
            and result['expectedFocusedPassedCases'] == writer_count + 94
            and result['maxSimultaneousTestChildren'] == 2
            and result['minimumAvailableCommitBytes'] == 512 * 1024 * 1024
            and result['childTimeoutSeconds'] == 1800
            and result['activeChildren'] == []
            and result['old300ExecutedThisBatch'] is False
            and result['defaultRegistrySingleProcessExecuted'] is False
            and result['fullSource19ExecutedThisBatch'] is False
            and result['fullBasis16ExecutedThisBatch'] is False
            and result['sdkGoalComplete'] is False
            and result['completeSeekPermitted'] is False
            and result['atomicConditionalWrites'] is False
            and result['beforeIsAtomicPreState'] is False
            and result['externalEffectsUnclosed'] is True
            and result['gameStarted'] is False and result['installed'] is False,
            'Fresh main result scope, limits or terminal success differs')
    require(all(result[name] is True for name in (
        'inputsUnchanged', 'controlsUnchanged', 'nativeFixturesUnchanged', 'compiledBytesUnchanged')),
        'Fresh main byte-stability gate is false')
    expected_controls = {str(path): sha(view.control(path.name)) for path in RUN_CONTROLS}
    require(result['controlsBefore'] == expected_controls,
            'Controller controls map differs from archived/current exact bytes')
    require(sha(view.control('resume_main.py')) == RESUME_SHA,
            'Reviewed continuation controller changed')
    check_continuation(view, result)
    require(sha(view.control('input-scanner.py')) == SCANNER_SHA
            and sha(view.control('Build.ps1')) == read(view.control('payload-manifest.json'))['buildScriptSha256'],
            'Scanner or accepted Build dispatch changed')
    before, after = read(view.run('inputs-before.json')), read(view.run('inputs-after.json'))
    require(before == after and len(before) == 892
            and all(SHA_RE.fullmatch(value) for value in before.values()),
            '892 compiler-input graph changed during fresh main validation')
    for name, digest in before.items():
        require(safe_name(name) == name and sha(view.input(name)) == digest,
                'Compiler-input source byte differs: ' + name)
    prior_summary_path = view.prior('validation-summary.json')
    prior_manifest_path = view.prior('artifact-manifest.json')
    prior_inputs_path = view.prior('inputs-after.json') if view.archived else PRIOR / 'run/inputs-after.json'
    prior_legacy_path = view.prior('legacy-case-list.json') if view.archived else PRIOR / 'run/legacy-case-list.json'
    prior_summary = read(prior_summary_path)
    prior_manifest = read(prior_manifest_path)
    prior_inputs, prior_legacy = read(prior_inputs_path), read(prior_legacy_path)
    require(sha(prior_summary_path) == PRIOR_SUMMARY_SHA
            and prior_summary['acceptedThisBatch'] is True
            and prior_summary['compilerInputs'] == 891
            and len(prior_inputs) == 891
            and prior_manifest['validation-summary.json'] == PRIOR_SUMMARY_SHA
            and prior_manifest['run/inputs-after.json'] == sha(prior_inputs_path)
            and prior_manifest['run/legacy-case-list.json'] == sha(prior_legacy_path),
            'Prior accepted 891 summary/manifest/source/list differs')
    manifest = check_payload(view, meta, before, prior_inputs)
    require(manifest['writerCases'] == writer_count,
            'Final writer case count differs across payload and main result')
    check_exit_receipts(view, meta, result)
    check_preliminary(view, meta)
    reviews = meta['finalReviews']
    require(isinstance(reviews, dict) and len(reviews) >= 2
            and any(name.endswith('.json') for name in reviews)
            and any(name.endswith('.md') for name in reviews),
            'At least final independent JSON and Markdown reviews are required')
    for name, item in reviews.items():
        require(safe_name(name) == name and '/' not in name
                and SHA_RE.fullmatch(item['sha256']) is not None,
                'Final review name or SHA is unsafe')
        source = view.extra('reviews/' + name, item['originalPath'])
        require(sha(source) == item['sha256'], 'Final independent review byte differs: ' + name)
    steps, passes = check_registry_and_steps(view, result, manifest, prior_legacy)
    check_binaries_and_fixtures(view, result, before)
    return {'result': result, 'inputs': before, 'manifest': manifest,
            'naturalSteps': steps, 'focusedPassedCases': passes,
            'writerCases': writer_count, 'sourceGraphSha256': sha(view.run('inputs-after.json'))}


def load_scanner():
    require(sha(SCANNER) == SCANNER_SHA, 'Accepted compiler-input scanner changed')
    spec = importlib.util.spec_from_file_location('parent_evidence_scanner', SCANNER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def check_continuation(view, result):
    original = view.run('interrupted-original-result.json')
    require(sha(original) == ORIGINAL_RESULT_SHA == result['originalResultSha256']
            and result['continuedOriginalRun'] == str(RUN)
            and result['originalBatchAccepted'] is False
            and result['originalControllerExitCodeKnown'] is False
            and result['originalResultUnchanged'] is True
            and result['reusedCompletedChildRows'] == 17
            and result['continuationExecutedSteps'] == 31,
            'Continuation misstates the original interruption or replay coverage')
    prior = read(original)
    require(prior['controllerPid'] == 40464 and prior['passed'] is False
            and 'finishedUtc' not in prior and len(prior['steps']) == 17
            and prior['schema'] == 'line-endpoint-parent-main-validation/v1'
            and prior['activeChildren'] == ['writer-15', 'writer-16'],
            'Interrupted original was not preserved as incomplete')
    require(result['originalControlsBefore'] == prior['controlsBefore']
            == {str(path): sha(view.control(path.name)) for path in RUN_CONTROLS[:-2]}
            and all(result[key] == prior[key] for key in (
                'productsAfterSix', 'productsBeforeTests', 'testPairs', 'dependencies', 'nativeFixtures')),
            'Continued source controls, production, tests, dependencies or fixtures differ')
    expected_prefix = ['six-release', 'solution', 'legacy-listing'] + [f'writer-{index:02}' for index in range(1, 15)]
    require([row['name'] for row in prior['steps']] == expected_prefix
            and result['steps'][:17] == [{**row, 'logSegment': 'original'} for row in prior['steps']]
            and all(row['logSegment'] == 'continuation' for row in result['steps'][17:]),
            'Only the 17 completed original child rows may contribute to acceptance')
    observation = read(view.run('interruption-observation.json'))
    require(observation['originalControllerPid'] == 40464
            and observation['originalControllerNoLongerExists'] is True
            and observation['originalControllerExitCode'] is None
            and observation['originalControllerExitCodeKnown'] is False
            and observation['originalBatchAccepted'] is False
            and observation['completedChildRowsReused'] == 17
            and observation['unconfirmedAttemptsRerun'] == ['writer-15', 'writer-16']
            and observation['sameExact892SourceAndBuildBytesVerified'] is True
            and observation['originalResultSha256'] == ORIGINAL_RESULT_SHA,
            'Interruption observation claims an unobserved successful original exit')
    original_started, resumed = map(timestamp, (prior['startedUtc'], result['startedUtc']))
    require(original_started < timestamp(observation['observedUtc']) <= resumed,
            'Original/continuation timeline differs')


def file_plan(view, meta, audited):
    plan = {}
    def add(name, path):
        name = safe_name(name)
        require(name not in plan, 'Duplicate archive member: ' + name)
        path = safe_file(path)
        plan[name] = {'source': path, 'sha256': sha(path), 'size': path.stat().st_size}
    for name in audited['inputs']:
        add('compiler-inputs/' + name, view.input(name))
    for name in CONTROLS:
        add('controls/' + name, view.control(name))
    for name in ('result.json', 'inputs-before.json', 'inputs-after.json', 'legacy-case-list.json',
                 'controller-handle-exit.json', 'interrupted-original-result.json', 'interruption-observation.json'):
        add('run/' + name, view.run(name))
    add('run/root-controller-exit.json', Path(meta['rootExitPath']))
    for row in audited['result']['steps']:
        for stream in ('stdout', 'stderr'):
            add('run/logs/' + row['name'] + '.' + stream + '.log', view.log(row['name'], stream))
    for index in (15, 16):
        for stream in ('stdout', 'stderr'):
            add(f'run/unconfirmed-original/writer-{index:02}.{stream}.log', RUN / f'writer-{index:02}.{stream}.log')
    for component in SIX:
        for ext in ('.dll', '.pdb'):
            add('binaries/production/' + component + ext, view.production(component, ext))
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in ('.dll', '.pdb'):
            add('binaries/test/' + component + '/' + component + ext, view.test(component, ext))
        for ext in ('.deps.json', '.runtimeconfig.json'):
            add('binaries/test/' + component + '/' + component + ext,
                RUN / 'artifacts/bin' / component / 'release' / (component + ext))
    for component, chosen in ((JUDGEMENTS, SIX[:4]), (RUNTIME_TEST, ('RizToolbox.Runtime', 'RizToolbox.Sdk'))):
        for product in chosen:
            for ext in ('.dll', '.pdb'):
                add('binaries/dependencies/' + component + '/' + product + ext,
                    view.dependency(component, product, ext))
    for key, row in audited['result']['nativeFixtures'].items():
        add('fixtures/native/' + key + '/' + Path(row['path']).name, view.fixture(key, row['path']))
    add('fixtures/native-source-record.json', FIXTURE_RECORD)
    add('fixtures/line-ring-numeric-goldens.json',
        RUN / 'artifacts/bin' / JUDGEMENTS / 'release/line-ring-numeric-goldens.json')
    for name, path in (
        ('validation-summary.json', PRIOR / 'validation-summary.json'),
        ('artifact-manifest.json', PRIOR / 'artifact-manifest.json'),
        ('inputs-after.json', PRIOR / 'run/inputs-after.json'),
        ('legacy-case-list.json', PRIOR / 'run/legacy-case-list.json')):
        add('prior-891/' + name, path)
    for row in audited['manifest']['files']:
        name = row['path']
        add('payload/files/' + name, view.payload('files', name))
        if row['baseSha256'] is not None:
            add('payload/before/' + name, view.payload('before', name))
    for name in audited['manifest']['candidateFilesSha256']:
        add('candidate-files/' + name, view.candidate(name))
    for name, item in meta['finalReviews'].items():
        add('reviews/' + name, Path(item['originalPath']))
    for name in ('result.json', 'root-observation.json', 'recovery-receipt.json'):
        target = {'result.json': 'root-r2-result.json', 'root-observation.json': 'root-r2-root-observation.json',
                  'recovery-receipt.json': 'root-r2-recovery-receipt.json'}[name]
        add('preliminary/' + target, PRELIMINARY / name)
    preliminary = read(PRELIMINARY / 'result.json')
    for name, digest in preliminary['candidateInputsBefore'].items():
        source = PRELIMINARY / 'candidate' / name
        require(sha(source) == digest, 'Preliminary exact source snapshot differs: ' + name)
        add('preliminary/candidate/' + name, source)
    for row in preliminary['steps']:
        for stream in ('stdout', 'stderr'):
            add('preliminary/logs/' + row['name'] + '.' + stream + '.log', PRELIMINARY / (row['name'] + '.' + stream + '.log'))
    add('publisher/publish.py', Path(__file__).resolve())
    require(len(plan) + 3 <= MAX_FILES and sum(row['size'] for row in plan.values()) < MAX_BYTES,
            'Bounded evidence plan exceeds file or byte limit')
    return plan


def summary_for(audited, meta):
    return {
        'schema': 'line-endpoint-parent-accepted-offline-scope/v1',
        'acceptedThisBatch': True, 'compilerInputs': 892,
        'writerCases': audited['writerCases'], 'defaultExpectedCases': 403,
        'defaultRegistrySingleProcessExecuted': False,
        'naturalSteps': audited['naturalSteps'], 'focusedPassedCases': audited['focusedPassedCases'],
        'reusedCompletedOriginalSteps': 17, 'continuationExecutedSteps': 31,
        'originalControllerExitCodeKnown': False, 'originalBatchAccepted': False,
        'sixProductionReleaseWarnings': 0, 'sixProductionReleaseErrors': 0, 'solutionErrors': 0,
        'legacyCasesListed': 300, 'old300ExecutedThisBatch': False,
        'fullSource19ExecutedThisBatch': False, 'fullBasis16ExecutedThisBatch': False,
        'sourceCasesExecuted': [13, 14, 17, 18, 19], 'basisCasesExecuted': [1],
        'runtimeAbiCases': 51, 'atomicConditionalWrites': False, 'beforeIsAtomicPreState': False,
        'externalEffectsUnclosed': True, 'sdkGoalComplete': False, 'completeSeekPermitted': False,
        'gameStarted': False, 'installed': False,
        'resultSha256': meta['resultSha256'], 'originalInterruptedResultSha256': ORIGINAL_RESULT_SHA,
        'handleExitSha256': meta['handleExitSha256'], 'rootExitSha256': meta['rootExitSha256'],
        'publisherSha256': meta['publisherSha256'], 'sourceGraphSha256': audited['sourceGraphSha256'],
        'scope': 'Same exact 892-input fresh build; observed parent/local writer and adjacent checks across an interrupted and resumed execution.',
    }


def check_archive(archive):
    archive = Path(archive).resolve()
    manifest = read(archive / 'artifact-manifest.json')
    require(isinstance(manifest, dict) and 900 <= len(manifest) <= MAX_FILES,
            'Archive member count differs from bounded plan')
    actual = {path.relative_to(archive).as_posix() for path in archive.rglob('*') if path.is_file()}
    require(actual == set(manifest) | {'artifact-manifest.json'}, 'Archive has missing or extra members')
    total = 0
    for name, digest in manifest.items():
        require(safe_name(name) == name and SHA_RE.fullmatch(digest), 'Unsafe manifest member/hash')
        file = safe_file(archive / name)
        require(sha(file) == digest, 'Archive byte changed: ' + name)
        total += file.stat().st_size
    require(total <= MAX_BYTES, 'Archive exceeds bounded byte size')
    meta = read(archive / 'publication-meta.json')
    require(meta['publisherSha256'] == sha(Path(__file__).resolve())
            == sha(archive / 'publisher/publish.py'), 'Publisher byte differs from frozen archive')
    audited = audit(View(archive, True), meta)
    require(read(archive / 'validation-summary.json') == summary_for(audited, meta),
            'Accepted summary differs from independent archived evidence')
    preliminary = read(archive / 'preliminary/root-r2-result.json')
    for name, digest in preliminary['candidateInputsBefore'].items():
        require(sha(archive / 'preliminary/candidate' / safe_name(name)) == digest,
                'Preliminary candidate source changed')
    return {'verified': True, 'archive': str(archive), 'members': len(manifest), 'bytes': total,
            'compilerInputs': 892, 'naturalSteps': audited['naturalSteps'],
            'focusedPassedCases': audited['focusedPassedCases'],
            'summarySha256': sha(archive / 'validation-summary.json')}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--archive', type=Path, default=ARCHIVE)
    parser.add_argument('--preflight', action='store_true')
    parser.add_argument('--result-sha256')
    parser.add_argument('--handle-exit-sha256')
    parser.add_argument('--root-exit', type=Path)
    parser.add_argument('--root-exit-sha256')
    parser.add_argument('--review', type=Path, action='append', default=[])
    options = parser.parse_args()
    if options.check_only:
        require(not options.preflight and not options.result_sha256 and not options.review,
                'Archive check accepts no live publication inputs')
        print(json.dumps(check_archive(options.archive)), flush=True)
        return
    require(options.archive.resolve() == ARCHIVE.resolve() and not ARCHIVE.exists(),
            'Publish only to the new fixed archive; preserve previous archive')
    require(options.root_exit and options.result_sha256 and options.handle_exit_sha256
            and options.root_exit_sha256, 'Explicit original-handle/root/result hashes are required')
    reviews = {}
    for path in options.review:
        path = path.resolve()
        require(path.is_relative_to(ROOT / 'work') and path.name not in reviews,
                'Final review must have a unique name and remain inside work')
        reviews[path.name] = {'originalPath': str(path), 'sha256': sha(path)}
    meta = {'schema': 'line-endpoint-parent-publication/v1',
            'resultSha256': options.result_sha256, 'handleExitSha256': options.handle_exit_sha256,
            'rootExitPath': str(options.root_exit.resolve()), 'rootExitSha256': options.root_exit_sha256,
            'payloadManifestSha256': '844ca3cf9b84a3220cb156b934d068e54a605f4845522c3bb37ca2e114d58035',
            'preliminaryResultSha256': '34e20c6e9607fb684169dd928bf5cd01e048107f40388445cdf0cadeb7040b05',
            'preliminaryRootObservationSha256': sha(PRELIMINARY / 'root-observation.json'),
            'finalReviews': reviews, 'publisherSha256': sha(__file__),
            'originalInterruptedResultSha256': ORIGINAL_RESULT_SHA}
    scanner = load_scanner()
    view = View(CONTINUATION, False)
    audited = audit(view, meta)
    require(scanner.inputs() == audited['inputs'], 'Main changed during evidence review')
    plan = file_plan(view, meta, audited)
    if options.preflight:
        print(json.dumps({'preflightPassed': True, 'plannedFiles': len(plan) + 3,
                          'plannedBytes': sum(row['size'] for row in plan.values()),
                          'naturalSteps': audited['naturalSteps'], 'focusedPassedCases': audited['focusedPassedCases'],
                          'publisherSha256': sha(__file__)}), flush=True)
        return
    staging = ARCHIVE.with_name(ARCHIVE.name + '.staging-' + uuid.uuid4().hex)
    staging.mkdir()
    for name, row in plan.items():
        require(sha(row['source']) == row['sha256'], 'Evidence changed during copy: ' + name)
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream:
            stream.write(row['source'].read_bytes())
        require(sha(target) == row['sha256'], 'Copied evidence bytes differ')
    (staging / 'publication-meta.json').write_bytes(encoded(meta))
    (staging / 'validation-summary.json').write_bytes(encoded(summary_for(audited, meta)))
    members = {path.relative_to(staging).as_posix(): sha(path)
               for path in sorted(staging.rglob('*')) if path.is_file()}
    (staging / 'artifact-manifest.json').write_bytes(encoded(members))
    check_archive(staging)
    require(scanner.inputs() == audited['inputs'] and not ARCHIVE.exists()
            and all(sha(row['source']) == row['sha256'] for row in plan.values()),
            'Evidence or main changed before atomic archive publication')
    os.rename(staging, ARCHIVE)
    print(json.dumps(check_archive(ARCHIVE)), flush=True)


if __name__ == '__main__':
    main()
