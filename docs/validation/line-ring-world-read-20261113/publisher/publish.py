"""Audit and atomically archive the fresh offline post-write world validation.

Publication requires a separate root-authored SHA-256 pin record.  Check-only
uses only the frozen archive and this publisher, never live main or D: files.
This script does not build, execute the checked binaries, or modify main.
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
WORK = ROOT / 'work/line-ring-world-evidence-20261113'
INTEGRATION = ROOT / 'work/line-ring-world-integration-20261113'
CANDIDATE = ROOT / 'work/line-ring-next-functional-20261113/candidate'
INITIAL = ROOT / 'work/line-ring-next-functional-20261113/candidate-manifest.json'
PRIOR = ROOT / 'docs/validation/line-endpoint-parent-writer-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
BUILD = ROOT / 'scripts/Build.ps1'
OFFLINE = ROOT / 'work/ring-x-numeric-20261113/NuGet.Config'
FIXTURE_RECORD = ROOT / 'docs/validation/canvas-basis-shared-source-20261112/build-test-result.json'
SHADOW = ROOT / 'work/line-ring-world-root-20261113/root-r2-snapshot'
SHADOW_EXIT = ROOT / 'work/line-ring-world-root-20261113/root-shadow-exit.json'
RUN = Path('D:/riz-build-cache-next/line-ring-world-integration-20261113/main-r1')
ARCHIVE = ROOT / 'docs/validation/line-ring-world-read-20261113'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
JUDGEMENTS = 'RizToolbox.Judgements.Tests'
RUNTIME_TEST = 'RizToolbox.Runtime.Tests'
SIX = tuple('RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader'))
PRIOR_SUMMARY_SHA = 'b21930d90ff9f3970d4441d4b77b104551496af75b6c97e9725983c876e344f0'
INITIAL_SHA = '42bda5978831e1fe51b5cc0e89ea77ac72e12e6f5c8d68ca18baa539593000ee'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
BUILD_SHA = '19624697b8976c2d55c7be8971dbaa97a3dd2c67e2693164fba943f6d2b1e62b'
SHADOW_EXIT_SHA = 'ffdb7ab672c20a3a54853ef151f56a5400d282b2f41afd40701aef7fb81ebaeb'
SHADOW_RESULT_SHA = '184b1e750331568f48b0b460744ad14e767727a149fda830f48f1298c33a0812'
SHA_RE = re.compile(r'[0-9a-f]{64}\Z')
MAX_FILES = 1300
MAX_BYTES = 256 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024

TARGET_MAP = {
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs':
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs':
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs',
    'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs':
        'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs',
    'tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs':
        'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs',
}
WRITER_FILE = TARGET_MAP['tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs']
PROGRAM_FILE = 'tests/RizToolbox.Judgements.Tests/Program.cs'
SOURCE_FILE = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
BASIS_FILE = 'tests/RizToolbox.Judgements.Tests/CanvasBasis/NativeSeekCanvasBasisChecks.cs'
GROUPS = {
    'target-provider': ('--line-ring-target-provider-checks', 6,
                        'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.TargetProvider.cs',
                        '[] TargetProviderCases ='),
    'chart-occurrence': ('--line-chart-occurrence-checks', 5,
                         'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineChartOccurrenceChecks.cs',
                         '[] Cases ='),
    'endpoint-local': ('--line-ring-endpoint-local-checks', 6,
                       'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineEndpointLocalChecks.cs',
                       '[] Cases ='),
    'numeric': ('--line-ring-numeric-checks', 13,
                'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingNumericChecks.cs',
                '[] Cases ='),
    'ring-x': ('--line-ring-ring-x-numeric-checks', 7,
               'tests/RizToolbox.Judgements.Tests/LineRingNumeric/NativeSeekLineRingXNumericChecks.cs',
               '[] Cases ='),
}
RUNTIME_CASES = (
    ('VoidPublicationRegression', 11), ('ExactFieldWriteRegression', 20),
    ('GuardedFieldPublicationRegression', 5), ('RendererQuaternionRegression', 8),
    ('TweenGraphReadRegression', 7),
)
CONTROL_LIVE = {
    'validate_main.py': INTEGRATION / 'validate_main.py',
    'prepare.py': INTEGRATION / 'prepare.py',
    'apply.py': INTEGRATION / 'apply.py',
    'observe.py': INTEGRATION / 'observe.py',
    'payload-manifest.json': INTEGRATION / 'payload-manifest.json',
    'review-receipt.json': INTEGRATION / 'review-receipt.json',
    'application-record.json': INTEGRATION / 'application-record.json',
    'inputs-before.json': INTEGRATION / 'inputs-before.json',
    'Build.ps1': BUILD,
    'input-scanner.py': SCANNER,
    'NuGet.Config': OFFLINE,
    'initial-candidate-manifest.json': INITIAL,
}
RUN_CONTROL_NAMES = ('validate_main.py', 'prepare.py', 'apply.py',
                     'payload-manifest.json', 'review-receipt.json',
                     'application-record.json', 'Build.ps1')


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


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
    return json.loads(safe_file(path).read_text(encoding='utf-8-sig'),
                      object_pairs_hook=unique_keys)


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def utc(value):
    parsed = dt.datetime.fromisoformat(value)
    require(parsed.tzinfo is not None, 'Unzoned evidence time')
    return parsed.astimezone(dt.timezone.utc)


def registry_names(path, marker, expected=None):
    body = safe_file(path).read_text(encoding='utf-8-sig')
    require(body.count(marker) == 1, 'Registry marker ambiguous: ' + str(path))
    block = body.split(marker, 1)[1].split('};', 1)[0]
    names = re.findall(r'(?m)^\s*,?\s*\("([^"\r\n]+)",', block)
    require(names and len(names) == len(set(names))
            and (expected is None or len(names) == expected)
            and block.count('("') == len(names),
            'Registry names/count differ: ' + str(path))
    return names


class View:
    def __init__(self, base, archived, live_pin=None):
        self.base = Path(base)
        self.archived = archived
        self.live_pin = Path(live_pin) if live_pin is not None else None

    def at(self, name, live):
        return self.base / safe_name(name) if self.archived else Path(live)

    def input(self, name):
        return self.at('compiler-inputs/' + safe_name(name), ROOT / name)

    def control(self, name):
        require(name in CONTROL_LIVE, 'Unexpected control: ' + name)
        return self.at('controls/' + name, CONTROL_LIVE[name])

    def run(self, name):
        return self.at('run/' + safe_name(name), RUN / name)

    def log(self, name, stream):
        return self.at('run/logs/' + safe_name(name + '.' + stream + '.log'),
                       RUN / (name + '.' + stream + '.log'))

    def prior(self, name):
        return self.at('prior/' + safe_name(name), PRIOR / name)

    def payload(self, section, name):
        require(section in ('files', 'before'), 'Unknown payload section')
        return self.at('payload/' + section + '/' + safe_name(name),
                       INTEGRATION / section / name)

    def candidate(self, name):
        return self.at('candidate-files/' + safe_name(name), CANDIDATE / name)

    def production(self, component, ext):
        return self.at('binaries/production/' + component + ext,
                       RUN / 'artifacts/bin' / component / 'release' / (component + ext))

    def test(self, component, ext):
        return self.at('binaries/test/' + component + '/' + component + ext,
                       RUN / 'artifacts/bin' / component / 'release' / (component + ext))

    def dependency(self, component, product, ext):
        return self.at('binaries/dependencies/' + component + '/' + product + ext,
                       RUN / 'artifacts/bin' / component / 'release' / (product + ext))

    def fixture(self, key, original):
        return self.at('fixtures/native/' + safe_name(key) + '/' + Path(original).name,
                       original)

    def shadow(self, name):
        return self.at('shadow/' + safe_name(name), SHADOW / name)

    def shadow_log(self, name, stream):
        filename = safe_name(name + '.' + stream + '.log')
        return self.at('shadow/logs/' + filename, SHADOW / filename)

    def golden(self):
        return self.at('fixtures/line-ring-numeric-goldens.json',
                       RUN / 'artifacts/bin' / JUDGEMENTS / 'release' /
                       'line-ring-numeric-goldens.json')

    def test_asset(self, component, ext):
        require(component in (JUDGEMENTS, RUNTIME_TEST)
                and ext in ('.deps.json', '.runtimeconfig.json'),
                'Unexpected test replay asset')
        return self.at('binaries/test/' + component + '/' + component + ext,
                       RUN / 'artifacts/bin' / component / 'release' / (component + ext))

    def review(self, index, path):
        return self.at('reviews/' + str(index).zfill(2) + '-' + safe_name(Path(path).name), path)

    def pin(self):
        return self.at('run/publication-pin.json', self.live_pin)

    def root_exit(self, pin):
        return self.at('run/root-exit.json', pin_path(pin['rootExitPath']))


def step_order(writer_count):
    return (['six-release', 'solution', 'legacy-listing']
            + ['writer-' + str(index).zfill(2) for index in range(1, writer_count + 1)]
            + list(GROUPS) + ['invalid-writer-name', 'invalid-writer-arity']
            + ['source-' + str(index) for index in (13, 14, 17, 18, 19)]
            + ['basis-01', 'renderer-abi'])


def step_spec(name, names):
    test = RUN / 'artifacts/bin' / JUDGEMENTS / 'release' / (JUDGEMENTS + '.dll')
    prefix = [str(DOTNET), str(test)]
    if name in ('six-release', 'solution'):
        project = ROOT / ('src/RizToolbox.Loader/RizToolbox.Loader.csproj'
                          if name == 'six-release' else 'RizToolbox.sln')
        return ([str(DOTNET), 'build', str(project), '-c', 'Release',
                 '--artifacts-path', str(RUN / 'artifacts'), '--verbosity', 'minimal',
                 '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false',
                 '-p:NuGetAudit=false', '-p:RestoreConfigFile=' + str(OFFLINE)], 0)
    if name == 'legacy-listing':
        return prefix + ['--list-legacy-cases'], 0
    if name.startswith('writer-'):
        index = int(name[7:])
        require(1 <= index <= len(names), 'Writer index outside registry')
        return prefix + ['--line-ring-endpoint-writer-case', str(index)], 0
    if name in GROUPS:
        return prefix + [GROUPS[name][0]], 0
    if name == 'invalid-writer-name':
        return prefix + ['--line-ring-endpoint-writer-cases'], 2
    if name == 'invalid-writer-arity':
        return prefix + ['--line-ring-endpoint-writer-case', '1', 'extra'], 2
    if name.startswith('source-'):
        index = int(name[7:])
        require(index in (13, 14, 17, 18, 19), 'Unexpected source index')
        return prefix + ['--line-ring-source-case', str(index)], 0
    if name == 'basis-01':
        return prefix + ['--canvas-basis-case', '1'], 0
    if name == 'renderer-abi':
        runtime = RUN / 'artifacts/bin' / RUNTIME_TEST / 'release' / (RUNTIME_TEST + '.dll')
        return [str(DOTNET), str(runtime), '--renderer-abi-checks'], 0
    raise RuntimeError('Unexpected validation step: ' + name)


def pin_path(value):
    require(isinstance(value, str) and value, 'Root exit path missing')
    path = Path(value)
    if not path.is_absolute():
        path = ROOT / safe_name(value)
    path = path.resolve()
    require(path.is_relative_to(ROOT.resolve()) and path.name.endswith('.json'),
            'Root exit path escaped the workspace')
    return path


def check_prior(view):
    summary_path = view.prior('validation-summary.json')
    require(sha(summary_path) == PRIOR_SUMMARY_SHA,
            'Accepted 892 summary changed')
    summary = read(summary_path)
    require(summary['schema'] == 'line-endpoint-parent-accepted-offline-scope/v1'
            and summary['acceptedThisBatch'] is True
            and summary['compilerInputs'] == 892
            and summary['writerCases'] == 31
            and summary['defaultExpectedCases'] == 403
            and summary['legacyCasesListed'] == 300
            and summary['sdkGoalComplete'] is False
            and summary['completeSeekPermitted'] is False,
            'Accepted 892 scope differs')
    before = read(view.prior('run/inputs-after.json'))
    legacy = read(view.prior('run/legacy-case-list.json'))
    prior_result = read(view.prior('run/result.json'))
    require(sha(view.prior('run/inputs-after.json')) == summary['sourceGraphSha256'],
            'Accepted 892 source graph differs from its pinned summary')
    require(sha(view.prior('compiler-inputs/' + WRITER_FILE)) == before[WRITER_FILE],
            'Accepted 31-case writer source differs from pinned graph')
    prior_listing = [row for row in prior_result['steps'] if row['name'] == 'legacy-listing']
    require(len(prior_listing) == 1
            and sha(view.prior('run/logs/legacy-listing.stdout.log')) ==
                prior_listing[0]['stdoutSha256']
            and read(view.prior('run/logs/legacy-listing.stdout.log')) == legacy,
            'Accepted 300-name listing differs from pinned original step')
    prior_manifest = read(view.prior('artifact-manifest.json'))
    required_prior = ('validation-summary.json', 'run/inputs-after.json',
                      'run/legacy-case-list.json', 'run/result.json',
                      'run/logs/legacy-listing.stdout.log',
                      'fixtures/native-source-record.json',
                      'compiler-inputs/' + WRITER_FILE)
    require(all(prior_manifest[name] == sha(view.prior(name)) for name in required_prior),
            'Prior 892 artifact manifest does not link retained source and fixtures')
    require(len(before) == 892 and len(legacy) == 300
            and [row['index'] for row in legacy] == list(range(1, 301))
            and len({row['name'] for row in legacy}) == 300
            and sha(view.prior('run/result.json')) == summary['resultSha256']
            and prior_result['compiledBytesUnchanged'] is True
            and prior_result['inputsUnchanged'] is True,
            'Accepted 892 graph, legacy names, or result differs')
    return before, legacy, {row['name']: row for row in prior_result['steps']}


def check_payload(view, before, inputs, writer_names):
    manifest_path = view.control('payload-manifest.json')
    manifest = read(manifest_path)
    review_path = view.control('review-receipt.json')
    application_path = view.control('application-record.json')
    review = read(review_path)
    application = read(application_path)
    require(manifest['schema'] == 'line-ring-world-main-payload/v1'
            and manifest['acceptedSummarySha256'] == PRIOR_SUMMARY_SHA
            and manifest['initialCandidateManifestSha256'] == INITIAL_SHA
            and manifest['inputScannerSha256'] == SCANNER_SHA
            and manifest['buildScriptSha256'] == BUILD_SHA
            and manifest['acceptedInputs'] == manifest['expectedInputs'] == 892
            and manifest['oldWriterCases'] == 31
            and manifest['writerCases'] == 36
            and manifest['defaultCases'] == 408
            and manifest['legacyCasesListed'] == 300
            and manifest['writerCaseNames'] == writer_names
            and manifest['freshMainValidationPassed'] is False
            and manifest['sdkGoalComplete'] is False
            and manifest['completeSeekPermitted'] is False,
            'Four-file payload scope differs')
    require(len(manifest['files']) == 4
            and {row['candidatePath']: row['path'] for row in manifest['files']} == TARGET_MAP
            and len({row['path'] for row in manifest['files']}) == 4,
            'Payload is not the four exact replacements')
    expected = dict(before)
    for row in manifest['files']:
        source, target = row['candidatePath'], row['path']
        require(row['baseSha256'] == before[target]
                and row['payloadSha256'] == sha(view.payload('files', target))
                and row['baseSha256'] == sha(view.payload('before', target))
                and row['payloadSha256'] == sha(view.candidate(source))
                and row['payloadSha256'] == sha(view.input(target)),
                'Staged, candidate, before, or compiled replacement differs: ' + target)
        expected[target] = row['payloadSha256']
    candidate_hashes = {name: sha(view.candidate(name)) for name in sorted(TARGET_MAP)}
    snapshot = hashlib.sha256(json.dumps(candidate_hashes, sort_keys=True,
                    separators=(',', ':')).encode('utf-8')).hexdigest()
    require(expected == inputs and len(inputs) == 892
            and set(expected) == set(before)
            and manifest['candidateFilesSha256'] == candidate_hashes
            and manifest['candidateSnapshotSha256'] == snapshot,
            '892-input graph or final candidate snapshot differs')
    require(sha(review_path) == manifest['reviewReceiptSha256']
            and review['reviewedBy'] == 'root'
            and review['readyForMainIntegration'] is True
            and review['candidateSnapshotSha256'] == snapshot
            and review['candidateFilesSha256'] == candidate_hashes
            and review['writerCases'] == 36
            and review['targets'] == sorted(TARGET_MAP.values())
            and review['expectedMainInputs'] == 892
            and review['expectedDefaultCases'] == 408
            and review['review']['sha256'] == sha(view.review(0, review['review']['path']))
            and review['shadowResult']['sha256'] == SHADOW_RESULT_SHA
            and review['shadowRootExitSha256'] == SHADOW_EXIT_SHA,
            'Root candidate review or shadow pin differs')
    shadow = read(view.shadow('result.json'))
    shadow_exit = read(view.at('shadow/root-shadow-exit.json', SHADOW_EXIT))
    require(sha(view.shadow('result.json')) == SHADOW_RESULT_SHA
            and sha(view.at('shadow/root-shadow-exit.json', SHADOW_EXIT)) == SHADOW_EXIT_SHA
            and shadow.get('passed') is True and shadow.get('mainApplied') is False
            and shadow.get('sourceAndControlsUnchanged') is True
            and shadow.get('fixturesUnchanged') is True
            and shadow.get('mainInputsBefore') == before
            and shadow.get('candidateInputsBefore') == candidate_hashes
            and shadow.get('caseNames') == writer_names
            and shadow.get('selectedCases') == [32, 33, 34, 35, 36]
            and shadow_exit['observedRootExec']['actualExitCode'] == 0
            and shadow_exit['resultSha256'] == SHADOW_RESULT_SHA,
            'Passing original shadow run differs')
    prior_products = read(view.prior('run/result.json'))['productsBeforeTests']
    expected_refs = {str(PRIOR / 'binaries/production' / (component + ext)): prior_products[component][ext]
                     for component in SIX[:3] for ext in ('.dll', '.pdb')}
    require(shadow.get('references') == expected_refs,
            'Shadow source references differ from accepted production bytes')
    shadow_steps = shadow['steps']
    require(len(shadow_steps) == 6
            and {row['name'] for row in shadow_steps} ==
                {'build', 'world-32', 'world-33', 'world-34', 'world-35', 'world-36'},
            'Shadow six-step provenance differs')
    for row in shadow_steps:
        require(row['exitCode'] == 0 and row['naturalExit'] is True
                and row['matchesExpected'] is True
                and row['stdoutSha256'] == sha(view.shadow_log(row['name'], 'stdout'))
                and row['stderrSha256'] == sha(view.shadow_log(row['name'], 'stderr')),
                'Shadow log or natural exit differs: ' + row['name'])
        if row['name'] != 'build':
            index = int(row['name'][6:])
            require(row['passNames'] == [writer_names[index - 1]],
                    'Shadow new-case name differs')
    require(application['schema'] == 'line-ring-world-main-application/v1'
            and application['appliedToMain'] is True
            and application['acceptedInputs'] == application['newInputs'] == 892
            and application['manifestSha256'] == sha(manifest_path)
            and application['reviewReceiptSha256'] == sha(review_path)
            and application['shadowResultSha256'] == SHADOW_RESULT_SHA
            and application['writerCases'] == 36
            and application['defaultCases'] == 408
            and application['freshMainValidationPassed'] is False
            and application['sdkGoalComplete'] is False
            and application['completeSeekPermitted'] is False
            and application['atomicConditionalWrites'] is False
            and application['gameStarted'] is False
            and application['installed'] is False,
            'Main application receipt differs')
    return manifest, snapshot


def check_pin_and_exit(view, result, manifest, snapshot):
    pin = read(view.pin())
    require(pin['schema'] == 'line-ring-world-publication-pin/v1'
            and pin['authoredBy'] == 'root'
            and all(SHA_RE.fullmatch(pin[key]) for key in
                    ('resultSha256', 'handleExitSha256', 'rootExitSha256',
                     'payloadManifestSha256', 'candidateSnapshotSha256'))
            and pin['resultSha256'] == sha(view.run('result.json'))
            and pin['payloadManifestSha256'] == sha(view.control('payload-manifest.json'))
            and pin['candidateSnapshotSha256'] == snapshot
            and isinstance(pin['reviews'], list) and len(pin['reviews']) >= 1,
            'Explicit root publication pin differs')
    for index, item in enumerate(pin['reviews']):
        require(isinstance(item, dict) and isinstance(item.get('path'), str)
                and SHA_RE.fullmatch(item.get('sha256', ''))
                and sha(view.review(index + 1, item['path'])) == item['sha256'],
                'Pinned review differs')
    receipt_path = view.run('controller-handle-exit.json')
    root_exit_path = view.root_exit(pin)
    require(sha(receipt_path) == pin['handleExitSha256']
            and sha(root_exit_path) == pin['rootExitSha256'],
            'Root-pinned actual exit evidence differs')
    receipt, root_exit = read(receipt_path), read(root_exit_path)
    controller_script = str(INTEGRATION / 'validate_main.py')
    require(receipt['schema'] == 'original-controller-handle-exit/v1'
            and receipt['controllerPid'] == result['controllerPid']
            and receipt['controllerScript'] == controller_script
            and receipt['controllerScriptSha256'] == result['controlsBefore'][controller_script]
            and receipt['observerSha256'] == sha(view.control('observe.py'))
            and receipt['handleKeptUntilExit'] is True
            and receipt['controllerScriptUnchanged'] is True
            and receipt['actualExitCode'] == 0
            and receipt['resultSha256AtExit'] == pin['resultSha256']
            and receipt['sdkGoalComplete'] is False
            and receipt['completeSeekPermitted'] is False,
            'Original Windows controller handle did not certify this result')
    created, alive, exit_time = map(utc, (receipt['createdUtc'],
                                          receipt['observedAliveUtc'], receipt['finishedUtc']))
    started, finished = map(utc, (result['startedUtc'], result['finishedUtc']))
    require(created <= started <= alive <= exit_time and finished <= exit_time
            and (started - created).total_seconds() < 600,
            'Original controller chronology differs')
    require(root_exit['observedBy'] == 'root'
            and root_exit['controllerPid'] == result['controllerPid']
            and root_exit['controllerScriptSha256'] == receipt['controllerScriptSha256']
            and root_exit['resultSha256'] == pin['resultSha256']
            and root_exit['handleExitSha256'] == pin['handleExitSha256']
            and root_exit['observedRootExec']['actualExitCode'] == 0
            and root_exit['sdkGoalComplete'] is False
            and root_exit['completeSeekPermitted'] is False
            and utc(root_exit['observedUtc']) >= exit_time,
            'Independent root original-process exit record differs')
    require(pin_path(pin['rootExitPath']).name == 'root-exit.json'
            or pin_path(pin['rootExitPath']).name.startswith('root-'),
            'Unrecognised root exit record name')
    return pin


def check_steps(view, result, writer_names, old_steps, legacy):
    names = step_order(len(writer_names))
    rows = result['steps']
    require(len(rows) == 53 and [row['name'] for row in rows] == names
            and len({row['name'] for row in rows}) == 53,
            'Fresh main step list is incomplete or reordered')
    listing = read(view.run('legacy-case-list.json'))
    require(listing == legacy, 'Legacy 300 names or order changed')
    source_names = registry_names(view.input(SOURCE_FILE), '[] Cases =', 19)
    basis_names = registry_names(view.input(BASIS_FILE), '[] Cases =', 16)
    group_names = {key: registry_names(view.input(file), marker, count)
                   for key, (_, count, file, marker) in GROUPS.items()}
    runtime_names = []
    for classname, count in RUNTIME_CASES:
        file = 'tests/' + RUNTIME_TEST + '/' + classname + '.cs'
        runtime_names.extend(registry_names(view.input(file), '[] Cases =', count))
    program = safe_file(view.input(PROGRAM_FILE)).read_text(encoding='utf-8-sig')
    usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', program)
    require(len(usage) == 1, 'Judgements usage declaration is ambiguous')
    total = 0
    for row in rows:
        name = row['name']
        argv, exit_code = step_spec(name, writer_names)
        stdout_path, stderr_path = view.log(name, 'stdout'), view.log(name, 'stderr')
        require(row['arguments'] == argv and type(row['pid']) is int and row['pid'] > 0
                and row['exitCode'] == exit_code
                and row['naturalExit'] is True and row['matchesExpected'] is True
                and row['stdoutSha256'] == sha(stdout_path)
                and row['stderrSha256'] == sha(stderr_path),
                'Step arguments, exit, or log hash differs: ' + name)
        body = safe_file(stdout_path).read_text(encoding='utf-8-sig')
        errors = safe_file(stderr_path).read_text(encoding='utf-8-sig')
        passes = re.findall(r'^PASS (.+)$', body, re.M)
        runs = re.findall(r'^RUN (.+)$', body, re.M)
        require(row['passNames'] == passes and row['runNames'] == runs
                and not re.search(r'^FAIL\b', body + errors, re.M),
                'Step PASS/RUN log differs: ' + name)
        if name in ('six-release', 'solution'):
            require(not passes and not runs and not errors
                    and ('已成功生成。' in body or 'Build succeeded.' in body)
                    and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0'],
                    'Build error or output differs: ' + name)
            if name == 'six-release':
                require(re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0'],
                        'Six Release build has warnings')
        elif name == 'legacy-listing':
            require(not passes and not runs and not errors
                    and json.loads(body) == legacy, 'Legacy listing differs')
        elif name.startswith('invalid-writer-'):
            require(not body and errors == usage[0] + '\n'
                    and not passes and not runs, 'Invalid writer selector usage differs')
        else:
            if name.startswith('writer-'):
                expected = [writer_names[int(name[7:]) - 1]]
            elif name in group_names:
                expected = group_names[name]
                require(expected == old_steps[name]['passNames'],
                        'Adjacent group registry changed: ' + name)
            elif name.startswith('source-'):
                expected = [source_names[int(name[7:]) - 1]]
                require(expected == old_steps[name]['passNames'],
                        'Adjacent selected source name changed')
            elif name == 'basis-01':
                expected = [basis_names[0]]
                require(expected == old_steps[name]['passNames'],
                        'Adjacent selected basis name changed')
            else:
                expected = runtime_names
                require(expected == old_steps[name]['passNames'],
                        'Adjacent runtime ABI registry changed')
            require(not errors and passes == expected and len(passes) == len(set(passes))
                    and (not runs if name == 'renderer-abi' else runs == passes)
                    and re.findall(r'(\d+)/(\d+) passed\.', body) ==
                    [(str(len(expected)), str(len(expected)))],
                    'Fresh positive step output differs: ' + name)
            total += len(passes)
    require(total == 130, 'Fresh main focused total is not 130')
    return total


def check_binaries(view, result):
    products = result['productsBeforeTests']
    require(result['productsAfterSix'] == products == result['productsAfterTests']
            and set(products) == set(SIX)
            and result['compiledBytesUnchanged'] is True,
            'Six production Release pairs changed')
    for component in SIX:
        require(set(products[component]) == {'.dll', '.pdb'},
                'Missing production pair: ' + component)
        for ext in ('.dll', '.pdb'):
            require(products[component][ext] == sha(view.production(component, ext)),
                    'Production binary differs: ' + component + ext)
    pairs = result['testPairs']
    require(len(pairs) == 4, 'Two test DLL/PDB pairs missing')
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in ('.dll', '.pdb'):
            original = RUN / 'artifacts/bin' / component / 'release' / (component + ext)
            require(pairs[str(original)] == sha(view.test(component, ext)),
                    'Test binary differs: ' + component + ext)
    deps = result['dependencies']
    require(len(deps) == 12, 'Twelve test dependency files missing')
    expected = {}
    for test, components in ((JUDGEMENTS, SIX[:4]),
                             (RUNTIME_TEST, ('RizToolbox.Runtime', 'RizToolbox.Sdk'))):
        for component in components:
            for ext in ('.dll', '.pdb'):
                original = RUN / 'artifacts/bin' / test / 'release' / (component + ext)
                expected[str(original)] = products[component][ext]
                require(sha(view.dependency(test, component, ext)) == products[component][ext],
                        'Test dependency differs: ' + test + '/' + component + ext)
    require(deps == expected, 'Test dependency map differs from six Release products')
    goldens = 'tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'
    require(sha(view.golden()) == read(view.run('inputs-after.json'))[goldens],
            'Executed Judgements golden fixture differs from compiled input')
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in ('.deps.json', '.runtimeconfig.json'):
            safe_file(view.test_asset(component, ext))


def audit(view):
    result_path = view.run('result.json')
    result = read(result_path)
    require(result['schema'] == 'line-ring-world-main-validation/v1'
            and result['passed'] is True
            and result['compilerInputs'] == 892
            and result['writerCases'] == 36
            and result['defaultExpectedCases'] == 408
            and result['expectedFocusedPassedCases'] == 130
            and result['maxSimultaneousTestChildren'] == 2
            and result['inputsUnchanged'] is True
            and result['controlsUnchanged'] is True
            and result['nativeFixturesUnchanged'] is True
            and result['compiledBytesUnchanged'] is True
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
            'Fresh main result overclaims, failed, or changed scope')
    prior, legacy, old_steps = check_prior(view)
    inputs = read(view.run('inputs-before.json'))
    require(inputs == read(view.run('inputs-after.json'))
            and prior == read(view.control('inputs-before.json'))
            and len(inputs) == 892
            and all(SHA_RE.fullmatch(value) and sha(view.input(name)) == value
                    for name, value in inputs.items()),
            'Complete 892 compiler-input byte graph differs')
    writer_names = registry_names(view.input(WRITER_FILE), '[] EndpointWriterCases =', 36)
    require(writer_names[:31] == registry_names(view.prior('compiler-inputs/' + WRITER_FILE),
                                                '[] EndpointWriterCases =', 31),
            'Previously accepted 31 writer names or order changed')
    manifest, snapshot = check_payload(view, prior, inputs, writer_names)
    expected_controls = {str(path): sha(view.control(name)) for name, path in CONTROL_LIVE.items()
                         if name in RUN_CONTROL_NAMES}
    expected_controls.update({str(INTEGRATION / 'prepare.py'): sha(view.control('prepare.py')),
                              str(INTEGRATION / 'review-receipt.json'): sha(view.control('review-receipt.json')),
                              str(SCANNER): sha(view.control('input-scanner.py')),
                              str(INITIAL): sha(view.control('initial-candidate-manifest.json')),
                              str(PRIOR / 'validation-summary.json'): sha(view.prior('validation-summary.json')),
                              str(PRIOR / 'run/result.json'): sha(view.prior('run/result.json'))})
    expected_controls.update({str(CANDIDATE / name): sha(view.candidate(name)) for name in TARGET_MAP})
    require(result['controlsBefore'] == expected_controls,
            'Fresh controller control pins differ')
    require(sha(view.control('Build.ps1')) == BUILD_SHA
            and sha(view.control('input-scanner.py')) == SCANNER_SHA
            and sha(view.control('initial-candidate-manifest.json')) == INITIAL_SHA,
            'Pinned build, scanner, or initial manifest changed')
    pin = check_pin_and_exit(view, result, manifest, snapshot)
    total = check_steps(view, result, writer_names, old_steps, legacy)
    check_binaries(view, result)
    fixtures = result['nativeFixtures']
    require(len(fixtures) == 3
            and set(fixtures) == {'RIZ_INITIALIZATION_ABI_FIXTURE',
                                  'RIZ_INITIALIZATION_CONTEXT_HELPER',
                                  'RIZ_INITIALIZATION_ABI_DOBBY'},
            'Three native fixture pins differ')
    for key, item in fixtures.items():
        require(item['sha256'] == sha(view.fixture(key, item['path'])),
                'Native fixture differs: ' + key)
    prior_fixtures = read(view.prior('fixtures/native-source-record.json'))['nativeFixtures']
    prior_result_fixtures = read(view.prior('run/result.json'))['nativeFixtures']
    require(fixtures == prior_fixtures == prior_result_fixtures,
            'Native fixture paths and hashes differ from accepted 892 record')
    require(utc(result['startedUtc']) <= utc(result['finishedUtc']),
            'Fresh run chronology differs')
    return result, inputs, pin, total


class Plan:
    def __init__(self):
        self.files = {}
        self.generated = {}
        self.total = 0

    def add(self, source, name, expected=None):
        name = safe_name(name)
        source = safe_file(source)
        require(name not in self.files and name not in self.generated,
                'Duplicate archive destination: ' + name)
        digest, size = sha(source), source.stat().st_size
        require(expected is None or digest == expected,
                'Source byte changed before archive: ' + name)
        self.files[name] = (source, digest, size)
        self.total += size
        require(len(self.files) + len(self.generated) <= MAX_FILES and self.total <= MAX_BYTES,
                'Archive exceeds bounded file or byte budget')

    def generate(self, name, body):
        name = safe_name(name)
        require(name not in self.files and name not in self.generated,
                'Duplicate archive destination: ' + name)
        self.generated[name] = body
        self.total += len(body)
        require(len(self.files) + len(self.generated) <= MAX_FILES and self.total <= MAX_BYTES,
                'Archive exceeds bounded file or byte budget')

    def recheck(self):
        for name, (source, digest, size) in self.files.items():
            require(source.stat().st_size == size and sha(source) == digest,
                    'Original evidence changed during archive: ' + name)


def make_plan(view, result, inputs, pin, total):
    plan = Plan()
    for name, digest in inputs.items():
        plan.add(view.input(name), 'compiler-inputs/' + name, digest)
    for name, source in CONTROL_LIVE.items():
        plan.add(view.control(name), 'controls/' + name)
    for name in ('validation-summary.json', 'run/result.json',
                 'run/inputs-after.json', 'run/legacy-case-list.json',
                 'run/logs/legacy-listing.stdout.log',
                 'compiler-inputs/' + WRITER_FILE,
                 'artifact-manifest.json', 'fixtures/native-source-record.json'):
        plan.add(view.prior(name), 'prior/' + name)
    for section in ('files', 'before'):
        for row in read(view.control('payload-manifest.json'))['files']:
            name = row['path']
            plan.add(view.payload(section, name), 'payload/' + section + '/' + name)
    for name in TARGET_MAP:
        plan.add(view.candidate(name), 'candidate-files/' + name)
    plan.add(view.shadow('result.json'), 'shadow/result.json', SHADOW_RESULT_SHA)
    plan.add(view.at('shadow/root-shadow-exit.json', SHADOW_EXIT),
             'shadow/root-shadow-exit.json', SHADOW_EXIT_SHA)
    for row in read(view.shadow('result.json'))['steps']:
        for stream in ('stdout', 'stderr'):
            filename = row['name'] + '.' + stream + '.log'
            plan.add(view.shadow_log(row['name'], stream),
                     'shadow/logs/' + filename, row[stream + 'Sha256'])
    review = read(view.control('review-receipt.json'))['review']
    plan.add(view.review(0, review['path']),
             'reviews/00-' + Path(review['path']).name, review['sha256'])
    for index, item in enumerate(pin['reviews'], 1):
        plan.add(view.review(index, item['path']),
                 'reviews/' + str(index).zfill(2) + '-' + Path(item['path']).name,
                 item['sha256'])
    for name in ('result.json', 'inputs-before.json', 'inputs-after.json',
                 'legacy-case-list.json', 'controller-handle-exit.json'):
        plan.add(view.run(name), 'run/' + name)
    plan.add(view.pin(), 'run/publication-pin.json')
    plan.add(view.root_exit(pin), 'run/root-exit.json', pin['rootExitSha256'])
    for name in step_order(36):
        for stream in ('stdout', 'stderr'):
            plan.add(view.log(name, stream), 'run/logs/' + name + '.' + stream + '.log')
    for component in SIX:
        for ext in ('.dll', '.pdb'):
            plan.add(view.production(component, ext),
                     'binaries/production/' + component + ext,
                     result['productsBeforeTests'][component][ext])
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in ('.dll', '.pdb'):
            source = RUN / 'artifacts/bin' / component / 'release' / (component + ext)
            plan.add(view.test(component, ext),
                     'binaries/test/' + component + '/' + component + ext,
                     result['testPairs'][str(source)])
        for ext in ('.deps.json', '.runtimeconfig.json'):
            plan.add(view.test_asset(component, ext),
                     'binaries/test/' + component + '/' + component + ext)
    for source, digest in result['dependencies'].items():
        path = Path(source)
        component = path.parent.parent.name
        require(component in (JUDGEMENTS, RUNTIME_TEST),
                'Unexpected dependency owner')
        plan.add(view.dependency(component, path.stem, path.suffix),
                 'binaries/dependencies/' + component + '/' + path.name, digest)
    for key, item in result['nativeFixtures'].items():
        plan.add(view.fixture(key, item['path']),
                 'fixtures/native/' + key + '/' + Path(item['path']).name,
                 item['sha256'])
    plan.add(view.golden(), 'fixtures/line-ring-numeric-goldens.json',
             inputs['tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'])
    plan.add(Path(__file__), 'publisher/publish.py')
    summary = {
        'schema': 'line-ring-world-main-evidence/v1',
        'acceptedAtUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'acceptedThisBatch': True, 'compilerInputs': 892,
        'defaultExpectedCases': 408, 'writerCases': 36,
        'naturalSteps': 53, 'focusedPassedCases': total,
        'judgementsPassedCases': 79, 'runtimeAbiPassedCases': 51,
        'sixReleaseWarnings': 0, 'buildErrors': 0,
        'productionPairs': 6, 'testPairs': 2, 'dependencyFiles': 12,
        'nativeFixtures': 3, 'legacyCasesListed': 300,
        'old300ExecutedThisBatch': False,
        'default408SingleProcessExecuted': False,
        'fullSource19ExecutedThisBatch': False,
        'fullBasis16ExecutedThisBatch': False,
        'sdkGoalComplete': False, 'completeSeekPermitted': False,
        'atomicConditionalWrites': False,
        'beforeIsAtomicPreState': False,
        'externalEffectsUnclosed': True,
        'gameStarted': False, 'installed': False,
        'resultSha256': pin['resultSha256'],
        'handleExitSha256': pin['handleExitSha256'],
        'rootExitSha256': pin['rootExitSha256'],
        'publicationPinSha256': sha(view.pin()),
        'payloadManifestSha256': pin['payloadManifestSha256'],
        'candidateSnapshotSha256': pin['candidateSnapshotSha256'],
        'inputGraphSha256': sha(view.run('inputs-after.json')),
        'prior892SummarySha256': PRIOR_SUMMARY_SHA,
        'shadowResultSha256': SHADOW_RESULT_SHA,
        'scope': 'Offline fresh post-write endpoint world read on the exact 892-input main graph; native atomicity and complete Seek remain open',
    }
    plan.generate('validation-summary.json', encoded(summary))
    plan.generate('README.md', (
        '# Post-write endpoint world read evidence\n\n'
        'A fresh exact 892-input main build completed 53 natural scoped steps with '
        '130 focused PASS results (79 Judgements, 51 Runtime ABI). The 36 writer '
        'cases include five new actual post-write world-read cases. The root pinned '
        'the original controller handle exit, independent root exit record, final '
        'candidate, and reviews.\n\n'
        'The 300 legacy names were listed in the same order; the old 300 cases, '
        'full 19 source cases, full 16 basis cases, and default 408 registry in one '
        'process were not executed in this batch. This does not admit complete Seek '
        'or the full SDK goal.\n\n'
        'Recheck archived bytes only: python -B publisher/publish.py --check-only '
        '--archive <archive-dir>.\n').encode('utf-8'))
    return plan


def check_archive(path):
    path = Path(path)
    require(path.is_dir() and not path.is_symlink(), 'Missing or linked archive')
    found = set()
    for item in path.rglob('*'):
        require(not item.is_symlink(), 'Archive contains a symlink')
        if item.is_file():
            found.add(safe_name(item.relative_to(path).as_posix()))
    manifest = read(path / 'artifact-manifest.json')
    require(isinstance(manifest, dict)
            and found == set(manifest) | {'artifact-manifest.json'}
            and len(found) <= MAX_FILES, 'Archive manifest inventory differs')
    total = (path / 'artifact-manifest.json').stat().st_size
    for name, digest in manifest.items():
        require(safe_name(name) == name and SHA_RE.fullmatch(digest)
                and sha(path / name) == digest,
                'Archived byte differs: ' + name)
        total += (path / name).stat().st_size
    require(total <= MAX_BYTES, 'Archive exceeds byte budget')
    view = View(path, True)
    result, inputs, pin, passed = audit(view)
    summary = read(path / 'validation-summary.json')
    require(summary['schema'] == 'line-ring-world-main-evidence/v1'
            and summary['acceptedThisBatch'] is True
            and summary['compilerInputs'] == len(inputs) == 892
            and summary['defaultExpectedCases'] == 408
            and summary['writerCases'] == 36
            and summary['naturalSteps'] == len(result['steps']) == 53
            and summary['focusedPassedCases'] == passed == 130
            and summary['judgementsPassedCases'] == 79
            and summary['runtimeAbiPassedCases'] == 51
            and summary['productionPairs'] == 6
            and summary['testPairs'] == 2
            and summary['dependencyFiles'] == 12
            and summary['nativeFixtures'] == 3
            and summary['legacyCasesListed'] == 300
            and summary['sixReleaseWarnings'] == summary['buildErrors'] == 0
            and summary['old300ExecutedThisBatch'] is False
            and summary['default408SingleProcessExecuted'] is False
            and summary['fullSource19ExecutedThisBatch'] is False
            and summary['fullBasis16ExecutedThisBatch'] is False
            and summary['sdkGoalComplete'] is False
            and summary['completeSeekPermitted'] is False
            and summary['atomicConditionalWrites'] is False
            and summary['beforeIsAtomicPreState'] is False
            and summary['externalEffectsUnclosed'] is True
            and summary['gameStarted'] is False and summary['installed'] is False
            and summary['resultSha256'] == pin['resultSha256']
            and summary['handleExitSha256'] == pin['handleExitSha256']
            and summary['rootExitSha256'] == pin['rootExitSha256']
            and summary['publicationPinSha256'] == sha(view.pin())
            and summary['payloadManifestSha256'] == pin['payloadManifestSha256']
            and summary['candidateSnapshotSha256'] == pin['candidateSnapshotSha256']
            and summary['inputGraphSha256'] == sha(view.run('inputs-after.json'))
            and summary['prior892SummarySha256'] == PRIOR_SUMMARY_SHA
            and summary['shadowResultSha256'] == SHADOW_RESULT_SHA,
            'Archive summary does not match independently audited evidence')
    required = {'compiler-inputs/' + name for name in inputs}
    required |= {'run/logs/' + name + '.' + stream + '.log'
                 for name in step_order(36) for stream in ('stdout', 'stderr')}
    required |= {'binaries/production/' + component + ext
                 for component in SIX for ext in ('.dll', '.pdb')}
    required |= {'binaries/test/' + component + '/' + component + ext
                 for component in (JUDGEMENTS, RUNTIME_TEST) for ext in ('.dll', '.pdb')}
    required |= {'binaries/dependencies/' + Path(name).parent.parent.name + '/' + Path(name).name
                 for name in result['dependencies']}
    require(required <= found and 'publisher/publish.py' in found
            and sha(path / 'publisher/publish.py') == sha(Path(__file__)),
            'Archive self-contained compiler, logs, binaries, or publisher missing')
    return {'acceptedThisBatch': True, 'archiveFiles': len(found),
            'archiveBytes': total, 'summarySha256': sha(path / 'validation-summary.json'),
            'sdkGoalComplete': False, 'completeSeekPermitted': False}


def publish(pin_path_arg):
    require(not ARCHIVE.exists(), 'Preserve existing world evidence archive')
    view = View(RUN, False, pin_path_arg)
    result, inputs, pin, passed = audit(view)
    plan = make_plan(view, result, inputs, pin, passed)
    staging = ARCHIVE.parent / ('.line-ring-world-staging-' + uuid.uuid4().hex)
    staging.mkdir()
    # Keep a failed staging directory for diagnosis; never merge partial evidence.
    for name, (source, digest, size) in plan.files.items():
        destination = staging / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        with source.open('rb') as reader, destination.open('xb') as writer:
            while block := reader.read(1024 * 1024):
                writer.write(block)
        require(destination.stat().st_size == size and sha(destination) == digest,
                'Staged byte differs: ' + name)
    for name, body in plan.generated.items():
        destination = staging / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(body)
    hashes = {name: digest for name, (_, digest, _) in plan.files.items()}
    hashes.update({name: hashlib.sha256(body).hexdigest()
                   for name, body in plan.generated.items()})
    (staging / 'artifact-manifest.json').write_bytes(encoded(hashes))
    plan.recheck()
    check_archive(staging)
    require(not ARCHIVE.exists(), 'Concurrent world evidence archive appeared')
    staging.rename(ARCHIVE)
    return check_archive(ARCHIVE)


def preflight(pin_path_arg):
    require(not ARCHIVE.exists(), 'World evidence archive already exists')
    view = View(RUN, False, pin_path_arg)
    result, inputs, pin, passed = audit(view)
    plan = make_plan(view, result, inputs, pin, passed)
    plan.recheck()
    return {'preflightPassed': True, 'plannedFiles': len(plan.files) + len(plan.generated) + 1,
            'plannedBytesBeforeManifest': plan.total,
            'resultSha256': pin['resultSha256'],
            'publicationPinSha256': sha(view.pin()),
            'sdkGoalComplete': False, 'completeSeekPermitted': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--preflight', action='store_true')
    parser.add_argument('--archive', type=Path, default=ARCHIVE)
    parser.add_argument('--pin', type=Path)
    args = parser.parse_args()
    require(not (args.check_only and args.preflight),
            'Choose archived check or live read-only preflight')
    if args.check_only:
        require(args.pin is None, 'Archived check uses only archived evidence')
        outcome = check_archive(args.archive)
    elif args.preflight:
        require(args.archive == ARCHIVE and args.pin is not None,
                'Preflight requires fixed archive and explicit root pin')
        outcome = preflight(args.pin)
    else:
        require(args.archive == ARCHIVE and args.pin is not None,
                'Publication requires the fixed archive and explicit root pin')
        outcome = publish(args.pin)
    print(json.dumps(outcome), flush=True)


if __name__ == '__main__':
    main()
