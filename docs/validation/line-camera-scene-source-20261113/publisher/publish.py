"""Audit and atomically archive the fresh offline camera/UI/scene main validation.

Live preflight reads but writes nothing. Publication requires a separate root-authored
SHA-256 pin and an independently observed original controller exit. Archived check
uses only the frozen archive, including this publisher. Nothing here builds or runs
the checked binaries.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import uuid


ROOT = Path('C:/Users/71957/Desktop/riz_toolbox')
WORK = ROOT / 'work/line-camera-scene-evidence-20261113'
INTEGRATION = ROOT / 'work/line-camera-scene-integration-20261113'
CANDIDATE = ROOT / 'work/line-camera-scene-next-20261113'
REVIEW = ROOT / 'work/line-camera-scene-review-20261113'
SHADOW = ROOT / 'work/line-camera-scene-root-20261113/root-r1-snapshot'
SHADOW_EXIT = ROOT / 'work/line-camera-scene-root-20261113/root-shadow-exit.json'
SHADOW_CONTROL_BASE = ROOT / 'work/line-camera-scene-root-20261113'
PRIOR = ROOT / 'docs/validation/line-ring-world-read-20261113'
RUN = Path('D:/riz-build-cache-next/line-camera-scene-integration-20261113/main-r1')
ARCHIVE = ROOT / 'docs/validation/line-camera-scene-source-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
BUILD = ROOT / 'scripts/Build.ps1'
OFFLINE = ROOT / 'work/ring-x-numeric-20261113/NuGet.Config'
INITIAL = CANDIDATE / 'candidate-manifest.json'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
JUDGEMENTS = 'RizToolbox.Judgements.Tests'
RUNTIME_TEST = 'RizToolbox.Runtime.Tests'
SIX = tuple('RizToolbox.' + name for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader'))
WRITER_FILE = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs'
SOURCE_FILE = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
BASIS_FILE = 'tests/RizToolbox.Judgements.Tests/CanvasBasis/NativeSeekCanvasBasisChecks.cs'
PROGRAM_FILE = 'tests/RizToolbox.Judgements.Tests/Program.cs'
PRIOR_SUMMARY_SHA = 'ce8b17aa85be2ad8923c9792abfb375c38389ee34ee2b8c17fe2c5f6126cdc9b'
PRIOR_RESULT_SHA = '5563eb5458409ccc41938a4785ef4f9b5a8fd17f280c1d7d06ab227a5c8c1ae9'
INITIAL_SHA = 'bc3e4e119041ad1eef20a0efb0a80ab4f0f0c92fb1b74375d8fafc64710aa006'
MANIFEST_SHA = '72614fb285e719d7d3ba6e509d8c8705c144f14f492a0e76e1768f52d9e86a2b'
ROOT_READY_SHA = 'c70dc0fc626577363b641d619dfc5b69bfd826440a4c39450754125090b630b6'
VALIDATE_SHA = '5ca91bb1ea8bb6f49eac4e07a49a803655c481343752bb5730fcfb197179295c'
PREPARE_SHA = 'a6d3a423df3de7c2876c43b803135fc24f1203028920d2ff72f16dc362d0eb35'
APPLY_SHA = 'a50e87481d3be83007f0e3b567ddbadd6badcfeb126f5585831d73a5df1c32aa'
OBSERVE_SHA = 'b16fee338005e9968651d18e441d977f186bf727e254d9fd661db91677a7dd6b'
BUILD_NEW_SHA = '1da3db9f7130555493a852ef9f05daf44a738d2248473a9e9387023f8e9f9dc9'
SHADOW_RESULT_SHA = '5d91b993cc2de2d977ded043b07e5fdb8e5f69b8921a061bce181db89b64435a'
SHADOW_EXIT_SHA = '10cef85bb6025ca9b3616cc73bf8b595b12e6be3ac41709381d8ee51d4cfe3e1'
SHA_RE = re.compile(r'[0-9a-f]{64}\Z')
MAX_FILES = 1450
MAX_BYTES = 320 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024
NEW_SOURCE = tuple(range(20, 25))
NEW_FILES = {
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Scene.cs',
    'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingSource.Scene.cs',
    'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.Scene.cs',
}
SHADOW_CONTROL_NAMES = (
    'Adapter.SceneShadow.csproj', 'Directory.Build.props',
    'Judgements.SceneShadow.csproj', 'NuGet.Config', 'prepare_shadow.py',
    'Program.cs', 'Runtime.SceneShadow.csproj', 'validate.py',
)
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
    'root-ready.json': INTEGRATION / 'root-ready.json',
    'application-record.json': INTEGRATION / 'application-record.json',
    'inputs-before.json': INTEGRATION / 'inputs-before.json',
    'Build.ps1': BUILD,
    'input-scanner.py': SCANNER,
    'NuGet.Config': OFFLINE,
    'candidate-manifest.json': INITIAL,
    'static-review.json': REVIEW / 'review.json',
    'static-review.md': REVIEW / 'review.md',
}


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


def pin_path(value):
    require(isinstance(value, str) and value, 'Root exit path missing')
    path = Path(value)
    if not path.is_absolute():
        path = ROOT / safe_name(value)
    path = path.resolve()
    require(path.is_relative_to(ROOT.resolve()) and path.name.endswith('.json'),
            'Root exit path escaped the workspace')
    return path


def review_path(value):
    require(isinstance(value, str) and value, 'Pinned review path missing')
    path = Path(value)
    if not path.is_absolute():
        path = ROOT / safe_name(value)
    path = path.resolve()
    require(path.is_relative_to(ROOT.resolve()) and path.suffix in ('.md', '.json'),
            'Pinned review path escaped the workspace')
    return path


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

    def test_asset(self, component, ext):
        require(component in (JUDGEMENTS, RUNTIME_TEST)
                and ext in ('.deps.json', '.runtimeconfig.json'),
                'Unexpected test replay asset')
        return self.at('binaries/test/' + component + '/' + component + ext,
                       RUN / 'artifacts/bin' / component / 'release' / (component + ext))

    def fixture(self, key, original):
        return self.at('fixtures/native/' + safe_name(key) + '/' + Path(original).name,
                       original)

    def golden(self):
        return self.at('fixtures/line-ring-numeric-goldens.json',
                       RUN / 'artifacts/bin' / JUDGEMENTS / 'release' /
                       'line-ring-numeric-goldens.json')

    def shadow(self, name):
        return self.at('shadow/' + safe_name(name), SHADOW / name)

    def shadow_exit(self):
        return self.at('shadow/root-shadow-exit.json', SHADOW_EXIT)

    def shadow_log(self, name, stream):
        filename = safe_name(name + '.' + stream + '.log')
        return self.at('shadow/logs/' + filename, SHADOW / filename)

    def shadow_candidate(self, name):
        return self.at('shadow/candidate/' + safe_name(name), SHADOW / 'candidate' / name)

    def shadow_control(self, name):
        require(name in SHADOW_CONTROL_NAMES or name == 'observe.py',
                'Unexpected shadow control')
        return self.at('shadow/controls/' + name, SHADOW_CONTROL_BASE / name)

    def review(self, index, path):
        return self.at('reviews/' + str(index).zfill(2) + '-' + safe_name(Path(path).name),
                       review_path(path))

    def pin(self):
        return self.at('run/publication-pin.json', self.live_pin)

    def root_exit(self, pin):
        return self.at('run/root-exit.json', pin_path(pin['rootExitPath']))


def step_order():
    return (['six-release', 'solution', 'legacy-listing']
            + ['writer-' + str(index).zfill(2) for index in range(1, 37)]
            + list(GROUPS) + ['invalid-writer-name', 'invalid-writer-arity']
            + ['source-' + str(index).zfill(2) for index in range(1, 25)]
            + ['basis-01', 'renderer-abi'])


def step_spec(name):
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
        require(1 <= index <= 36, 'Writer index outside registry')
        return prefix + ['--line-ring-endpoint-writer-case', str(index)], 0
    if name in GROUPS:
        return prefix + [GROUPS[name][0]], 0
    if name == 'invalid-writer-name':
        return prefix + ['--line-ring-endpoint-writer-cases'], 2
    if name == 'invalid-writer-arity':
        return prefix + ['--line-ring-endpoint-writer-case', '1', 'extra'], 2
    if name.startswith('source-'):
        index = int(name[7:])
        require(1 <= index <= 24, 'Source index outside registry')
        return prefix + ['--line-ring-source-case', str(index)], 0
    if name == 'basis-01':
        return prefix + ['--canvas-basis-case', '1'], 0
    if name == 'renderer-abi':
        runtime = RUN / 'artifacts/bin' / RUNTIME_TEST / 'release' / (RUNTIME_TEST + '.dll')
        return [str(DOTNET), str(runtime), '--renderer-abi-checks'], 0
    raise RuntimeError('Unexpected validation step: ' + name)


def check_prior(view):
    summary_path = view.prior('validation-summary.json')
    result_path = view.prior('run/result.json')
    graph_path = view.prior('run/inputs-after.json')
    require(sha(summary_path) == PRIOR_SUMMARY_SHA and sha(result_path) == PRIOR_RESULT_SHA,
            'Accepted world summary or original result differs')
    summary, result, graph = read(summary_path), read(result_path), read(graph_path)
    require(summary['schema'] == 'line-ring-world-main-evidence/v1'
            and summary['acceptedThisBatch'] is True
            and summary['resultSha256'] == PRIOR_RESULT_SHA
            and summary['inputGraphSha256'] == sha(graph_path)
            and summary['compilerInputs'] == 892
            and summary['writerCases'] == 36
            and summary['defaultExpectedCases'] == 408
            and summary['legacyCasesListed'] == 300
            and summary['sdkGoalComplete'] is False
            and summary['completeSeekPermitted'] is False
            and result['passed'] is True
            and result['compiledBytesUnchanged'] is True
            and result['inputsUnchanged'] is True
            and len(graph) == 892,
            'Accepted world scope, result, or 892-input graph differs')
    writer = view.prior('compiler-inputs/' + WRITER_FILE)
    source = view.prior('compiler-inputs/' + SOURCE_FILE)
    require(sha(writer) == graph[WRITER_FILE] and sha(source) == graph[SOURCE_FILE],
            'Previously accepted writer or source bytes differ from graph')
    listing_row = [row for row in result['steps'] if row['name'] == 'legacy-listing']
    listing_path = view.prior('run/logs/legacy-listing.stdout.log')
    require(len(listing_row) == 1 and sha(listing_path) == listing_row[0]['stdoutSha256'],
            'Prior legacy listing is not linked to original passing step')
    listing = read(listing_path)
    require(len(listing) == 300
            and [row['index'] for row in listing] == list(range(1, 301))
            and len({row['name'] for row in listing}) == 300,
            'Prior legacy listing does not contain 300 ordered distinct cases')
    manifest = read(view.prior('artifact-manifest.json'))
    for name in ('validation-summary.json', 'run/result.json', 'run/inputs-after.json',
                 'run/logs/legacy-listing.stdout.log',
                 'compiler-inputs/' + WRITER_FILE, 'compiler-inputs/' + SOURCE_FILE):
        require(manifest.get(name) == sha(view.prior(name)),
                'Prior artifact manifest does not corroborate ' + name)
    products = result['productsBeforeTests']
    for component in SIX[:3]:
        for ext in ('.dll', '.pdb'):
            name = 'binaries/production/' + component + ext
            require(products[component][ext] == sha(view.prior(name))
                    and manifest.get(name) == products[component][ext],
                    'Accepted world production source differs: ' + name)
    return summary, result, graph, listing


def candidate_snapshot(hashes):
    return hashlib.sha256(json.dumps(hashes, sort_keys=True,
                         separators=(',', ':')).encode('utf-8')).hexdigest()


def check_candidate_and_shadow(view, prior_graph):
    manifest_path = view.control('candidate-manifest.json')
    manifest = read(manifest_path)
    require(sha(manifest_path) == INITIAL_SHA
            and manifest['schema'] == 'line-ring-scene-current-source-candidate/v2'
            and manifest['mainApplied'] is False
            and manifest['built'] is False
            and manifest['testsExecuted'] is False
            and manifest['accepted'] is False
            and manifest['completeSeekPermitted'] is False
            and len(manifest['files']) == 10
            and manifest['newSourceCaseIndexes'] == list(NEW_SOURCE),
            'Frozen ten-file scene candidate scope differs')
    target_map = {}
    candidate_hashes = {}
    expected_graph = dict(prior_graph)
    for row in manifest['files']:
        source, target = safe_name(row['candidatePath']), safe_name(row['targetPath'])
        require(source == target and target not in target_map,
                'Candidate target map is not one-to-one')
        digest = sha(view.candidate(source))
        require(digest == row['candidateSha256']
                and digest == sha(view.shadow_candidate(source)),
                'Candidate or frozen shadow byte differs: ' + target)
        base = row['observedMainBaseSha256']
        if target in NEW_FILES:
            require(base is None and target not in prior_graph,
                    'New scene source was not absent from prior graph')
        else:
            require(base == prior_graph[target],
                    'Candidate observed base differs from accepted world graph')
        target_map[source] = target
        candidate_hashes[source] = digest
        expected_graph[target] = digest
    require(len(target_map) == 10 and set(NEW_FILES) <= set(target_map)
            and len(expected_graph) == 895,
            'Scene target map does not add exactly three compiler inputs')
    snapshot = candidate_snapshot(candidate_hashes)
    review_path = view.control('static-review.json')
    review = read(review_path)
    require(review['schema'] == 'line-camera-scene-static-review/v1'
            and review['candidateManifestSha256'] == INITIAL_SHA
            and review['candidateFilesVerified'] == 10
            and review['disposition'] == 'no_remaining_material_scoped_code_defect_found'
            and review['candidateBuilt'] is False
            and review['candidateTestsExecuted'] is False
            and review['receiptIssuanceGapClosed'] is True,
            'Independent static review differs')
    shadow_path = view.shadow('result.json')
    shadow_exit_path = view.shadow_exit()
    require(sha(shadow_path) == SHADOW_RESULT_SHA
            and sha(shadow_exit_path) == SHADOW_EXIT_SHA,
            'Frozen shadow result or independent root exit differs')
    shadow, root_exit = read(shadow_path), read(shadow_exit_path)
    shadow_receipt_path = view.shadow('controller-handle-exit.json')
    shadow_receipt = read(shadow_receipt_path)
    expected_controls = {str(SHADOW_CONTROL_BASE / name): sha(view.shadow_control(name))
                         for name in SHADOW_CONTROL_NAMES}
    require(shadow['passed'] is True and shadow['mainApplied'] is False
            and shadow['sourceAndControlsUnchanged'] is True
            and shadow['fixturesUnchanged'] is True
            and shadow['mainInputsBefore'] == prior_graph
            and shadow['candidateInputsBefore'] == candidate_hashes
            and shadow['controls'] == expected_controls
            and shadow['nativeFixtures'] == read(view.prior('run/result.json'))['nativeFixtures']
            and shadow['selectedCases'] == list(NEW_SOURCE)
            and shadow['sdkGoalComplete'] is False
            and shadow['completeSeekPermitted'] is False
            and root_exit['observedBy'] == 'root'
            and root_exit['observedRootExec']['actualExitCode'] == 0
            and root_exit['resultSha256'] == SHADOW_RESULT_SHA
            and root_exit['handleExitSha256'] == sha(shadow_receipt_path)
            and root_exit['candidateManifestSha256'] == INITIAL_SHA
            and root_exit['candidateInputsBefore'] == candidate_hashes
            and shadow_receipt['actualExitCode'] == 0
            and shadow_receipt['resultSha256AtExit'] == SHADOW_RESULT_SHA
            and shadow_receipt['controllerPid'] == shadow['controllerPid']
            and shadow_receipt['handleKeptUntilExit'] is True
            and shadow_receipt['controllerScriptUnchanged'] is True
            and shadow_receipt['sdkGoalComplete'] is False
            and shadow_receipt['completeSeekPermitted'] is False
            and shadow_receipt['controllerScriptSha256'] ==
                sha(view.shadow_control('validate.py'))
            and shadow_receipt['observerSha256'] == sha(view.shadow_control('observe.py'))
            and utc(shadow['startedUtc']) <= utc(shadow['finishedUtc'])
            and utc(shadow['finishedUtc']) <= utc(shadow_receipt['finishedUtc'])
            and utc(root_exit['observedUtc']) >= utc(shadow_receipt['finishedUtc']),
            'Original shadow five-case execution lacks exact provenance')
    prior_products = read(view.prior('run/result.json'))['productsBeforeTests']
    references = {str(PRIOR / 'binaries/production' / (component + ext)): prior_products[component][ext]
                  for component in SIX[:3] for ext in ('.dll', '.pdb')}
    require(shadow['references'] == references,
            'Shadow source references differ from accepted world products')
    source_names = registry_names(view.candidate(SOURCE_FILE), '[] Cases =', 24)
    require(shadow['caseNames'] == source_names and len(shadow['steps']) == 6
            and {row['name'] for row in shadow['steps']} ==
                {'build'} | {'scene-' + str(index) for index in NEW_SOURCE},
            'Frozen scene shadow cases differ')
    for row in shadow['steps']:
        name = row['name']
        stdout_path = view.shadow_log(name, 'stdout')
        stderr_path = view.shadow_log(name, 'stderr')
        require(row['exitCode'] == 0 and row['naturalExit'] is True
                and row['matchesExpected'] is True
                and row['stdoutSha256'] == sha(stdout_path)
                and row['stderrSha256'] == sha(stderr_path),
                'Shadow original step or log differs: ' + name)
        body = safe_file(stdout_path).read_text(encoding='utf-8-sig')
        errors = safe_file(stderr_path).read_text(encoding='utf-8-sig')
        passes = re.findall(r'^PASS (.+)$', body, re.M)
        runs = re.findall(r'^RUN (.+)$', body, re.M)
        require(not errors and passes == row['passNames']
                and not re.search(r'^FAIL\b', body, re.M),
                'Shadow original PASS log differs: ' + name)
        if name == 'build':
            require(not passes and not runs
                    and ('已成功生成。' in body or 'Build succeeded.' in body)
                    and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0']
                    and re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0'],
                    'Shadow build output differs')
        else:
            index = int(name[6:])
            require(row['arguments'][-2:] == ['source', str(index)]
                    and passes == runs == [source_names[index - 1]]
                    and re.findall(r'(\d+)/(\d+) passed\.', body) == [('1', '1')],
                    'Shadow actual source selector differs: ' + name)
    return manifest, target_map, candidate_hashes, expected_graph, snapshot, source_names


def check_payload(view, prior_graph, inputs, target_map, hashes, snapshot, source_names):
    manifest_path = view.control('payload-manifest.json')
    manifest = read(manifest_path)
    ready_path = view.control('review-receipt.json')
    root_ready_path = view.control('root-ready.json')
    ready = read(ready_path)
    require(sha(manifest_path) == MANIFEST_SHA
            and manifest['schema'] == 'line-ring-scene-main-payload/v1'
            and manifest['acceptedSummarySha256'] == PRIOR_SUMMARY_SHA
            and manifest['initialCandidateManifestSha256'] == INITIAL_SHA
            and manifest['inputScannerSha256'] == sha(view.control('input-scanner.py'))
            and manifest['buildScriptSha256'] == sha(view.payload('before', 'scripts/Build.ps1'))
            and manifest['acceptedInputs'] == 892
            and manifest['expectedInputs'] == 895
            and manifest['writerCases'] == 36
            and manifest['oldSourceCases'] == 19
            and manifest['sourceCases'] == 24
            and manifest['defaultCases'] == 413
            and manifest['legacyCasesListed'] == 300
            and manifest['sourceCaseNames'] == source_names
            and manifest['candidateSnapshotSha256'] == snapshot
            and manifest['candidateFilesSha256'] == hashes
            and manifest['reviewReceiptSha256'] == sha(ready_path)
            and manifest['shadowResult']['sha256'] == SHADOW_RESULT_SHA
            and manifest['shadowHandle']['sha256'] == sha(view.shadow('controller-handle-exit.json'))
            and manifest['freshMainValidationPassed'] is False
            and manifest['sdkGoalComplete'] is False
            and manifest['completeSeekPermitted'] is False,
            'Eleven-file payload scope differs')
    require(sha(root_ready_path) == sha(ready_path) == ROOT_READY_SHA
            and read(root_ready_path) == ready
            and ready['reviewedBy'] == 'root'
            and ready['readyForMainIntegration'] is True
            and ready['candidateSnapshotSha256'] == snapshot
            and ready['candidateFilesSha256'] == hashes
            and ready['sourceCases'] == 24 and ready['writerCases'] == 36
            and ready['targets'] == sorted([*target_map.values(), 'scripts/Build.ps1'])
            and ready['expectedMainInputs'] == 895
            and ready['expectedDefaultCases'] == 413
            and ready['review']['sha256'] == sha(view.control('static-review.md'))
            and ready['shadowResult']['sha256'] == SHADOW_RESULT_SHA
            and ready['shadowHandle']['sha256'] == sha(view.shadow('controller-handle-exit.json'))
            and ready['shadowRootExitSha256'] == SHADOW_EXIT_SHA
            and ready['freshMainValidationPassed'] is False
            and ready['sdkGoalComplete'] is False
            and ready['completeSeekPermitted'] is False
            and ready['atomicSnapshot'] is False
            and ready['deterministicTarget'] is False,
            'Root candidate review or root-ready receipt differs')
    rows = manifest['files']
    require(len(rows) == 11
            and {row['path'] for row in rows} == set(target_map.values()) | {'scripts/Build.ps1'}
            and len({row['path'] for row in rows}) == 11,
            'Payload is not the exact ten C# files and Build selector')
    expected_graph = dict(prior_graph)
    for row in rows:
        target = safe_name(row['path'])
        source = row['candidatePath']
        payload_digest = row['payloadSha256']
        require(payload_digest == sha(view.payload('files', target)),
                'Staged payload byte differs: ' + target)
        if target == 'scripts/Build.ps1':
            require(source is None
                    and row['baseSha256'] == sha(view.payload('before', target))
                    and payload_digest == BUILD_NEW_SHA
                    and payload_digest == sha(view.control('Build.ps1')),
                    'Build selector payload or accepted original differs')
            before_text = safe_file(view.payload('before', target)).read_text(encoding='utf-8-sig')
            after_text = safe_file(view.control('Build.ps1')).read_text(encoding='utf-8-sig')
            old = "$lastCase = if ($selector -eq '--line-ring-source-case') { 19 } else { 16 }"
            new = "$lastCase = if ($selector -eq '--line-ring-source-case') { 24 } else { 16 }"
            require(before_text.count(old) == 1 and after_text == before_text.replace(old, new),
                    'Build source dispatch differs beyond 19-to-24 selector')
        else:
            require(source == target and payload_digest == hashes[source]
                    and payload_digest == sha(view.candidate(source))
                    and payload_digest == sha(view.input(target)),
                    'Candidate, staged, or compiled source differs: ' + target)
            if target in NEW_FILES:
                require(row['baseSha256'] is None and target not in prior_graph,
                        'New C# source has an accepted base')
            else:
                require(row['baseSha256'] == prior_graph[target]
                        and row['baseSha256'] == sha(view.payload('before', target)),
                        'Staged base differs from accepted world source: ' + target)
            expected_graph[target] = payload_digest
    require(inputs == expected_graph and len(inputs) == 895,
            'Applied compiler input graph is not exact 892-plus-three scene source')
    application_path = view.control('application-record.json')
    application = read(application_path)
    require(application['schema'] == 'line-ring-scene-main-application/v1'
            and application['appliedToMain'] is True
            and application['acceptedInputs'] == 892
            and application['newInputs'] == 895
            and application['manifestSha256'] == sha(manifest_path)
            and application['reviewReceiptSha256'] == sha(ready_path)
            and application['shadowResultSha256'] == SHADOW_RESULT_SHA
            and application['shadowHandleSha256'] == sha(view.shadow('controller-handle-exit.json'))
            and application['writerCases'] == 36
            and application['sourceCases'] == 24
            and application['defaultCases'] == 413
            and application['freshMainValidationPassed'] is False
            and application['sdkGoalComplete'] is False
            and application['completeSeekPermitted'] is False
            and application['atomicConditionalWrites'] is False
            and application['gameStarted'] is False
            and application['installed'] is False,
            'Main application receipt differs')
    return manifest, application


def check_pin_and_exit(view, result, snapshot):
    pin = read(view.pin())
    keys = ('resultSha256', 'handleExitSha256', 'rootExitSha256',
            'payloadManifestSha256', 'candidateSnapshotSha256', 'candidateManifestSha256')
    require(pin['schema'] == 'line-camera-scene-publication-pin/v1'
            and pin['authoredBy'] == 'root'
            and all(SHA_RE.fullmatch(pin.get(key, '')) for key in keys)
            and pin['resultSha256'] == sha(view.run('result.json'))
            and pin['payloadManifestSha256'] == sha(view.control('payload-manifest.json'))
            and pin['candidateSnapshotSha256'] == snapshot
            and pin['candidateManifestSha256'] == INITIAL_SHA
            and isinstance(pin['reviews'], list) and len(pin['reviews']) >= 1,
            'Explicit root publication pin differs')
    for index, item in enumerate(pin['reviews'], 1):
        require(isinstance(item, dict) and isinstance(item.get('path'), str)
                and SHA_RE.fullmatch(item.get('sha256', ''))
                and sha(view.review(index, item['path'])) == item['sha256'],
                'Pinned review differs')
    receipt_path = view.run('controller-handle-exit.json')
    root_exit_path = view.root_exit(pin)
    require(sha(receipt_path) == pin['handleExitSha256']
            and sha(root_exit_path) == pin['rootExitSha256'],
            'Root-pinned actual exit evidence differs')
    receipt, root_exit = read(receipt_path), read(root_exit_path)
    script = str(INTEGRATION / 'validate_main.py')
    require(receipt['schema'] == 'original-controller-handle-exit/v1'
            and receipt['controllerPid'] == result['controllerPid']
            and receipt['controllerScript'] == script
            and receipt['controllerScriptSha256'] == result['controlsBefore'][script]
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
    return pin


def check_steps(view, result, prior_result, legacy, source_names):
    names = step_order()
    rows = result['steps']
    require(len(rows) == len(names) == 72
            and [row['name'] for row in rows] == names
            and len({row['name'] for row in rows}) == 72,
            'Fresh main step list is incomplete or reordered')
    listing = read(view.run('legacy-case-list.json'))
    require(listing == legacy, 'Legacy 300 names or order changed')
    writer_names = registry_names(view.input(WRITER_FILE), '[] EndpointWriterCases =', 36)
    require(writer_names == registry_names(view.prior('compiler-inputs/' + WRITER_FILE),
                                           '[] EndpointWriterCases =', 36),
            'Previously accepted 36 writer names or order changed')
    require(source_names == registry_names(view.input(SOURCE_FILE), '[] Cases =', 24)
            and source_names[:19] == registry_names(view.prior('compiler-inputs/' + SOURCE_FILE),
                                                    '[] Cases =', 19),
            'Previously accepted 19 source names or order changed')
    basis_names = registry_names(view.input(BASIS_FILE), '[] Cases =', 16)
    group_names = {key: registry_names(view.input(file), marker, count)
                   for key, (_, count, file, marker) in GROUPS.items()}
    runtime_names = []
    for classname, count in RUNTIME_CASES:
        file = 'tests/' + RUNTIME_TEST + '/' + classname + '.cs'
        runtime_names.extend(registry_names(view.input(file), '[] Cases =', count))
    old_steps = {row['name']: row for row in prior_result['steps']}
    usage_body = safe_file(view.input(PROGRAM_FILE)).read_text(encoding='utf-8-sig')
    usage = re.findall(r'Console\.Error\.WriteLine\("(Usage: [^"\r\n]+)"\);', usage_body)
    require(len(usage) == 1, 'Judgements usage declaration is ambiguous')
    total = 0
    for row in rows:
        name = row['name']
        argv, exit_code = step_spec(name)
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
                require(expected == old_steps[name]['passNames'],
                        'Previously accepted writer case name changed')
            elif name in group_names:
                expected = group_names[name]
                require(expected == old_steps[name]['passNames'],
                        'Adjacent group registry changed: ' + name)
            elif name.startswith('source-'):
                index = int(name[7:])
                expected = [source_names[index - 1]]
                if name in old_steps:
                    require(expected == old_steps[name]['passNames'],
                            'Previously executed source case name changed')
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
    require(total == 149, 'Fresh main focused total is not 149')
    return total


def check_binaries(view, result, inputs):
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
    require(sha(view.golden()) == inputs[goldens],
            'Executed Judgements golden fixture differs from compiled input')
    for component in (JUDGEMENTS, RUNTIME_TEST):
        for ext in ('.deps.json', '.runtimeconfig.json'):
            safe_file(view.test_asset(component, ext))


def audit(view):
    result = read(view.run('result.json'))
    require(result['schema'] == 'line-ring-scene-main-validation/v1'
            and result['passed'] is True
            and result['compilerInputs'] == 895
            and result['defaultExpectedCases'] == 413
            and result['writerCases'] == 36
            and result['sourceCases'] == 24
            and result['expectedFocusedPassedCases'] == 149
            and result['maxSimultaneousTestChildren'] == 2
            and result['inputsUnchanged'] is True
            and result['controlsUnchanged'] is True
            and result['nativeFixturesUnchanged'] is True
            and result['compiledBytesUnchanged'] is True
            and result['activeChildren'] == []
            and result['old300ExecutedThisBatch'] is False
            and result['default413SingleProcessExecuted'] is False
            and result['fullSource24ExecutedThisBatch'] is True
            and result['fullBasis16ExecutedThisBatch'] is False
            and result['sdkGoalComplete'] is False
            and result['completeSeekPermitted'] is False
            and result['atomicConditionalWrites'] is False
            and result['beforeIsAtomicPreState'] is False
            and result['externalEffectsUnclosed'] is True
            and result['gameStarted'] is False and result['installed'] is False,
            'Fresh main result overclaims, failed, or changed scope')
    prior_summary, prior_result, prior_graph, legacy = check_prior(view)
    inputs = read(view.run('inputs-before.json'))
    require(inputs == read(view.run('inputs-after.json'))
            and prior_graph == read(view.control('inputs-before.json'))
            and len(inputs) == 895
            and all(SHA_RE.fullmatch(value) and sha(view.input(name)) == value
                    for name, value in inputs.items()),
            'Complete 895 compiler-input byte graph differs')
    candidate, target_map, hashes, expected_graph, snapshot, source_names = \
        check_candidate_and_shadow(view, prior_graph)
    require(inputs == expected_graph,
            'Compiler input graph differs from exactly ten scene replacements')
    manifest, application = check_payload(view, prior_graph, inputs,
                                          target_map, hashes, snapshot, source_names)
    controls = {str(path): sha(view.control(name)) for name, path in CONTROL_LIVE.items()
                if name in ('validate_main.py', 'prepare.py', 'apply.py',
                            'payload-manifest.json', 'review-receipt.json',
                            'application-record.json', 'Build.ps1',
                            'candidate-manifest.json', 'input-scanner.py')}
    controls.update({str(PRIOR / 'validation-summary.json'): sha(view.prior('validation-summary.json')),
                     str(PRIOR / 'run/result.json'): sha(view.prior('run/result.json')),
                     str(PRIOR / 'run/inputs-after.json'): sha(view.prior('run/inputs-after.json'))})
    for row in manifest['files']:
        target = row['path']
        controls[str(INTEGRATION / 'files' / target)] = sha(view.payload('files', target))
        if row['baseSha256'] is not None:
            controls[str(INTEGRATION / 'before' / target)] = sha(view.payload('before', target))
    for name in target_map:
        controls[str(CANDIDATE / name)] = sha(view.candidate(name))
    require(result['controlsBefore'] == controls,
            'Fresh controller control pin map differs')
    require(sha(view.control('validate_main.py')) == VALIDATE_SHA
            and sha(view.control('prepare.py')) == PREPARE_SHA
            and sha(view.control('apply.py')) == APPLY_SHA
            and sha(view.control('observe.py')) == OBSERVE_SHA
            and sha(view.control('Build.ps1')) == BUILD_NEW_SHA,
            'Frozen scene validation, observer, integration, or Build control differs')
    pin = check_pin_and_exit(view, result, snapshot)
    passed = check_steps(view, result, prior_result, legacy, source_names)
    check_binaries(view, result, inputs)
    fixtures = result['nativeFixtures']
    require(fixtures == prior_result['nativeFixtures']
            and len(fixtures) == 3
            and set(fixtures) == {'RIZ_INITIALIZATION_ABI_FIXTURE',
                                  'RIZ_INITIALIZATION_CONTEXT_HELPER',
                                  'RIZ_INITIALIZATION_ABI_DOBBY'},
            'Three native fixtures differ from accepted world result')
    for key, item in fixtures.items():
        require(item['sha256'] == sha(view.fixture(key, item['path'])),
                'Native fixture differs: ' + key)
    require(utc(result['startedUtc']) <= utc(result['finishedUtc']),
            'Fresh run chronology differs')
    return result, inputs, pin, passed


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


def make_plan(view, result, inputs, pin, passed):
    plan = Plan()
    for name, digest in inputs.items():
        plan.add(view.input(name), 'compiler-inputs/' + name, digest)
    for name in CONTROL_LIVE:
        plan.add(view.control(name), 'controls/' + name)
    for name in ('validation-summary.json', 'run/result.json',
                 'run/inputs-after.json', 'run/logs/legacy-listing.stdout.log',
                 'compiler-inputs/' + WRITER_FILE,
                 'compiler-inputs/' + SOURCE_FILE, 'artifact-manifest.json'):
        plan.add(view.prior(name), 'prior/' + name)
    for component in SIX[:3]:
        for ext in ('.dll', '.pdb'):
            name = 'binaries/production/' + component + ext
            plan.add(view.prior(name), 'prior/' + name)
    rows = read(view.control('payload-manifest.json'))['files']
    for row in rows:
        name = row['path']
        plan.add(view.payload('files', name), 'payload/files/' + name, row['payloadSha256'])
        if row['baseSha256'] is not None:
            plan.add(view.payload('before', name), 'payload/before/' + name, row['baseSha256'])
    for name in read(view.control('candidate-manifest.json'))['files']:
        candidate = name['candidatePath']
        plan.add(view.candidate(candidate), 'candidate-files/' + candidate,
                 name['candidateSha256'])
        plan.add(view.shadow_candidate(candidate), 'shadow/candidate/' + candidate,
                 name['candidateSha256'])
    plan.add(view.shadow('result.json'), 'shadow/result.json', SHADOW_RESULT_SHA)
    plan.add(view.shadow('controller-handle-exit.json'),
             'shadow/controller-handle-exit.json')
    plan.add(view.shadow_exit(), 'shadow/root-shadow-exit.json', SHADOW_EXIT_SHA)
    for name in (*SHADOW_CONTROL_NAMES, 'observe.py'):
        plan.add(view.shadow_control(name), 'shadow/controls/' + name)
    for row in read(view.shadow('result.json'))['steps']:
        for stream in ('stdout', 'stderr'):
            name = row['name']
            plan.add(view.shadow_log(name, stream),
                     'shadow/logs/' + name + '.' + stream + '.log', row[stream + 'Sha256'])
    for index, item in enumerate(pin['reviews'], 1):
        plan.add(view.review(index, item['path']),
                 'reviews/' + str(index).zfill(2) + '-' + Path(item['path']).name,
                 item['sha256'])
    for name in ('result.json', 'inputs-before.json', 'inputs-after.json',
                 'legacy-case-list.json', 'controller-handle-exit.json'):
        plan.add(view.run(name), 'run/' + name)
    plan.add(view.pin(), 'run/publication-pin.json')
    plan.add(view.root_exit(pin), 'run/root-exit.json', pin['rootExitSha256'])
    for name in step_order():
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
        require(component in (JUDGEMENTS, RUNTIME_TEST), 'Unexpected dependency owner')
        plan.add(view.dependency(component, path.stem, path.suffix),
                 'binaries/dependencies/' + component + '/' + path.name, digest)
    for key, item in result['nativeFixtures'].items():
        plan.add(view.fixture(key, item['path']),
                 'fixtures/native/' + key + '/' + Path(item['path']).name,
                 item['sha256'])
    goldens = 'tests/RizToolbox.Judgements.Tests/LineRingNumeric/single-rounding-goldens.json'
    plan.add(view.golden(), 'fixtures/line-ring-numeric-goldens.json', inputs[goldens])
    plan.add(Path(__file__), 'publisher/publish.py')
    summary = {
        'schema': 'line-camera-scene-main-evidence/v1',
        'acceptedAtUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'acceptedThisBatch': True, 'compilerInputs': 895,
        'defaultExpectedCases': 413, 'writerCases': 36, 'sourceCases': 24,
        'naturalSteps': 72, 'focusedPassedCases': passed,
        'judgementsPassedCases': 98, 'runtimeAbiPassedCases': 51,
        'sixReleaseWarnings': 0, 'buildErrors': 0,
        'productionPairs': 6, 'testPairs': 2, 'dependencyFiles': 12,
        'nativeFixtures': 3, 'legacyCasesListed': 300,
        'old300ExecutedThisBatch': False,
        'default413SingleProcessExecuted': False,
        'fullSource24ExecutedThisBatch': True,
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
        'candidateManifestSha256': INITIAL_SHA,
        'inputGraphSha256': sha(view.run('inputs-after.json')),
        'priorWorldSummarySha256': PRIOR_SUMMARY_SHA,
        'priorWorldResultSha256': PRIOR_RESULT_SHA,
        'shadowResultSha256': SHADOW_RESULT_SHA,
        'shadowRootExitSha256': SHADOW_EXIT_SHA,
        'scope': 'Offline original-source actual post-write world and camera/UI/scene observation; sequential history only',
    }
    plan.generate('validation-summary.json', encoded(summary))
    plan.generate('README.md', (
        '# Camera/UI/scene source evidence\n\n'
        'The exact 895-input main graph completed 72 natural scoped steps with '
        '149 focused PASS results (98 Judgements, 51 Runtime ABI), including all '
        '24 source cases and 36 endpoint writer cases. The root pinned the original '
        'controller handle exit, an independent exit observation, the eleven-file '
        'payload, final candidate, and reviews. The prior 892-input world result '
        'and five-case shadow are included as ancestors, not as fresh main acceptance.\n\n'
        'The 300 legacy names were listed in order; their cases, the full 16 basis '
        'cases, and the default 413 registry in one process were not executed here. '
        'Camera/UI/scene and post-write world observations remain sequential, '
        'historical, and non-atomic. Complete Seek and the full SDK goal remain open.\n\n'
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
    require(summary['schema'] == 'line-camera-scene-main-evidence/v1'
            and summary['acceptedThisBatch'] is True
            and summary['compilerInputs'] == len(inputs) == 895
            and summary['defaultExpectedCases'] == 413
            and summary['writerCases'] == 36
            and summary['sourceCases'] == 24
            and summary['naturalSteps'] == len(result['steps']) == 72
            and summary['focusedPassedCases'] == passed == 149
            and summary['judgementsPassedCases'] == 98
            and summary['runtimeAbiPassedCases'] == 51
            and summary['productionPairs'] == 6
            and summary['testPairs'] == 2
            and summary['dependencyFiles'] == 12
            and summary['nativeFixtures'] == 3
            and summary['legacyCasesListed'] == 300
            and summary['sixReleaseWarnings'] == summary['buildErrors'] == 0
            and summary['old300ExecutedThisBatch'] is False
            and summary['default413SingleProcessExecuted'] is False
            and summary['fullSource24ExecutedThisBatch'] is True
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
            and summary['candidateManifestSha256'] == INITIAL_SHA
            and summary['inputGraphSha256'] == sha(view.run('inputs-after.json'))
            and summary['priorWorldSummarySha256'] == PRIOR_SUMMARY_SHA
            and summary['priorWorldResultSha256'] == PRIOR_RESULT_SHA
            and summary['shadowResultSha256'] == SHADOW_RESULT_SHA
            and summary['shadowRootExitSha256'] == SHADOW_EXIT_SHA,
            'Archive summary does not match independently audited evidence')
    required = {'compiler-inputs/' + name for name in inputs}
    required |= {'run/logs/' + name + '.' + stream + '.log'
                 for name in step_order() for stream in ('stdout', 'stderr')}
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
    require(not ARCHIVE.exists(), 'Preserve existing scene evidence archive')
    view = View(RUN, False, pin_path_arg)
    result, inputs, pin, passed = audit(view)
    plan = make_plan(view, result, inputs, pin, passed)
    staging = ARCHIVE.parent / ('.line-camera-scene-staging-' + uuid.uuid4().hex)
    staging.mkdir()
    # A failed staging directory remains for diagnosis; never merge partial evidence.
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
    require(not ARCHIVE.exists(), 'Concurrent scene evidence archive appeared')
    staging.rename(ARCHIVE)
    return check_archive(ARCHIVE)


def preflight(pin_path_arg):
    require(not ARCHIVE.exists(), 'Scene evidence archive already exists')
    view = View(RUN, False, pin_path_arg)
    result, inputs, pin, passed = audit(view)
    plan = make_plan(view, result, inputs, pin, passed)
    plan.recheck()
    return {'preflightPassed': True,
            'plannedFiles': len(plan.files) + len(plan.generated) + 1,
            'plannedBytesBeforeManifest': plan.total,
            'resultSha256': pin['resultSha256'],
            'publicationPinSha256': sha(view.pin()),
            'focusedPassedCases': passed,
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
