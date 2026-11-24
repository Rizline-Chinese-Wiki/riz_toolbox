"""Audit and atomically archive the fresh offline dynamic-consumer main validation.

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
WORK = ROOT / 'work/line-dynamic-consumer-evidence-20261113'
INTEGRATION = ROOT / 'work/line-dynamic-consumer-integration-20261113'
CANDIDATE = ROOT / 'work/line-dynamic-consumer-next-20261113'
REVIEW = ROOT / 'work/line-dynamic-consumer-v4-review-20261113'
PRIOR_PRODUCT_REVIEW = ROOT / 'work/line-dynamic-consumer-review-20261113/review.md'
SHADOW = ROOT / 'work/line-dynamic-consumer-root-20261113/root-r2-snapshot'
SHADOW_EXIT = ROOT / 'work/line-dynamic-consumer-root-20261113/root-shadow-exit.json'
SHADOW_CONTROL_BASE = ROOT / 'work/line-dynamic-consumer-root-20261113'
SHADOW_RAW = Path('D:/riz-build-cache-next/line-dynamic-consumer-read-20261113/root-r2')
PRIOR = ROOT / 'docs/validation/line-camera-scene-source-20261113'
RUN = Path('D:/riz-build-cache-next/line-dynamic-consumer-integration-20261113/main-r1')
ARCHIVE = ROOT / 'docs/validation/line-dynamic-consumer-20261113'
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
PRIOR_SUMMARY_SHA = '98e554f44d1ae84f30368e1c13162e2c45a1a9a758cb666e621612bd9e7c4bef'
PRIOR_RESULT_SHA = '3dfaaf45458db2a76bea37d541d9db0d6ce2cf6fe3c55728eca38ec654857ff0'
INITIAL_SHA = '2ddcaf6c8e1b5ee193d62efdc8a2a921e4656689db7079d199d91128da46b172'
REVIEW_SHA = '5ece16e56c69f43aa3ba9233af097564795be786672fb0c59329caa3b2b92401'
REVIEW_JSON_SHA = '44ba66d8f9978212cae65645f58eb978e9bbe919e1fadb01d64f1333868bd646'
PRIOR_PRODUCT_REVIEW_SHA = '5a3aeb9f746c8d48741ea4035f82994d9fa02ea5c9b51a691afc4d8de5a0e156'
BUILD_OLD_SHA = '1da3db9f7130555493a852ef9f05daf44a738d2248473a9e9387023f8e9f9dc9'
SHA_RE = re.compile(r'[0-9a-f]{64}\Z')
MAX_FILES = 1450
MAX_BYTES = 320 * 1024 * 1024
MAX_FILE_BYTES = 64 * 1024 * 1024
NEW_SOURCE = tuple(range(25, 31))
NEW_FILES = {
    'src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasBasisCommands.LineRingDynamic.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingDynamicConsumer.cs',
    'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.Dynamic.cs',
}
SHADOW_CONTROL_NAMES = (
    'Adapter.DynamicShadow.csproj', 'Directory.Build.props',
    'Judgements.DynamicShadow.csproj', 'NuGet.Config', 'prepare_shadow.py',
    'Program.cs', 'validate.py', 'observe.py', 'prepared.json',
    'root-review-receipt.json',
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
        return self.at('shadow/logs/' + filename, SHADOW / 'logs' / filename)

    def shadow_candidate(self, name):
        return self.at('shadow/candidate/' + safe_name(name), SHADOW / 'candidate' / name)

    def shadow_control(self, name):
        require(name in SHADOW_CONTROL_NAMES,
                'Unexpected shadow control')
        return self.at('shadow/controls/' + name, SHADOW_CONTROL_BASE / name)

    def shadow_binary(self, original):
        path = Path(original)
        require(path.is_relative_to(SHADOW_RAW / 'artifacts/bin')
                and path.suffix in ('.dll', '.pdb'),
                'Unexpected shadow binary path')
        return self.at('shadow/binaries/' + path.relative_to(SHADOW_RAW / 'artifacts/bin').as_posix(), path)

    def review(self, index, path):
        return self.at('reviews/' + str(index).zfill(2) + '-' + safe_name(Path(path).name),
                       review_path(path))

    def prior_product_review(self):
        return self.at('reviews/prior-product-review.md', PRIOR_PRODUCT_REVIEW)

    def pin(self):
        return self.at('run/publication-pin.json', self.live_pin)

    def root_exit(self, pin):
        return self.at('run/root-exit.json', pin_path(pin['rootExitPath']))


def step_order():
    return (['six-release', 'solution', 'legacy-listing']
            + ['writer-' + str(index).zfill(2) for index in range(1, 37)]
            + list(GROUPS) + ['invalid-writer-name', 'invalid-writer-arity']
            + ['source-' + str(index).zfill(2) for index in range(1, 31)]
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
        require(1 <= index <= 30, 'Source index outside registry')
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
    require(summary['schema'] == 'line-camera-scene-main-evidence/v1'
            and summary['acceptedThisBatch'] is True
            and summary['resultSha256'] == PRIOR_RESULT_SHA
            and summary['inputGraphSha256'] == sha(graph_path)
            and summary['compilerInputs'] == 895
            and summary['writerCases'] == 36
            and summary['sourceCases'] == 24
            and summary['fullSource24ExecutedThisBatch'] is True
            and summary['defaultExpectedCases'] == 413
            and summary['legacyCasesListed'] == 300
            and summary['sdkGoalComplete'] is False
            and summary['completeSeekPermitted'] is False
            and result['passed'] is True
            and result['compiledBytesUnchanged'] is True
            and result['inputsUnchanged'] is True
            and len(graph) == 895,
            'Accepted scene scope, result, or 895-input graph differs')
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
            'Accepted scene production source differs: ' + name)
    return summary, result, graph, listing


def candidate_snapshot(hashes):
    return hashlib.sha256(json.dumps(hashes, sort_keys=True,
                         separators=(',', ':')).encode('utf-8')).hexdigest()


def check_candidate_and_shadow(view, prior_graph, pin):
    manifest_path = view.control('candidate-manifest.json')
    manifest = read(manifest_path)
    require(sha(manifest_path) == INITIAL_SHA
            and manifest['schema'] == 'line-ring-observed-dynamic-producer-consumer/v4'
            and manifest['mainApplied'] is False
            and manifest['built'] is False
            and manifest['testsExecuted'] is False
            and manifest['accepted'] is False
            and manifest['completeSeekPermitted'] is False
            and len(manifest['files']) == 8
            and manifest['newSourceCaseIndexes'] == list(NEW_SOURCE),
            'Frozen eight-file dynamic candidate scope differs')
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
                    'New dynamic source was not absent from prior graph')
        else:
            require(base == prior_graph[target],
                    'Candidate observed base differs from accepted scene graph')
        target_map[source] = target
        candidate_hashes[source] = digest
        expected_graph[target] = digest
    require(len(target_map) == 8 and set(NEW_FILES) <= set(target_map)
            and len(expected_graph) == 898,
            'Dynamic target map does not add exactly three compiler inputs')
    snapshot = candidate_snapshot(candidate_hashes)
    prepared = read(view.shadow_control('prepared.json'))
    root_review = read(view.shadow_control('root-review-receipt.json'))
    require(prepared['schema'] == 'line-ring-dynamic-shadow-prepared/v1'
            and prepared['candidateManifestSha256'] == INITIAL_SHA
            and prepared['basisSummarySha256'] == PRIOR_SUMMARY_SHA
            and prepared['basisResultSha256'] == PRIOR_RESULT_SHA
            and prepared['basisInputGraphSha256'] ==
                sha(view.prior('run/inputs-after.json'))
            and prepared['candidateFilesSha256'] == candidate_hashes
            and prepared['candidateSnapshotSha256'] == snapshot
            and prepared['reviewReceiptSha256'] ==
                sha(view.shadow_control('root-review-receipt.json'))
            and prepared['mainInputs'] == 895 and prepared['shadowInputs'] == 898
            and prepared['sourceCases'] == 30
            and prepared['selectedSourceCases'] == list(NEW_SOURCE)
            and prepared['selectedSceneCases'] == list(range(20, 25))
            and prepared['selectedBasisCases'] == [1]
            and all(prepared[key] is False for key in
                    ('mainApplied', 'built', 'testsExecuted',
                     'sdkGoalComplete', 'completeSeekPermitted'))
            and root_review['reviewedBy'] == 'root'
            and root_review['readyForShadow'] is True
            and root_review['candidateManifestSha256'] == INITIAL_SHA
            and root_review['candidateFilesSha256'] == candidate_hashes
            and root_review['candidateSnapshotSha256'] == snapshot
            and root_review['basisSummarySha256'] == PRIOR_SUMMARY_SHA
            and root_review['review']['path'] ==
                'work/line-dynamic-consumer-v4-review-20261113/review.md'
            and root_review['review']['sha256'] == REVIEW_SHA
            and root_review['priorProductReview'] ==
                {'path': 'work/line-dynamic-consumer-review-20261113/review.md',
                 'sha256': PRIOR_PRODUCT_REVIEW_SHA}
            and sha(view.prior_product_review()) == PRIOR_PRODUCT_REVIEW_SHA
            and root_review['productionBytesUnchangedFromV3'] is True
            and root_review['priorAttemptFailed'] is True
            and root_review['priorAttemptResultSha256'] ==
                'e92f9742200b690342bddfc51b1e029b623406a92644709d18314e76ff915e53',
            'Root preparation or independent shadow review differs')
    require(all(name in SHADOW_CONTROL_NAMES and
                sha(view.shadow_control(name)) == digest
                for name, digest in prepared['generatedControlsSha256'].items()),
            'Prepared generated shadow control hashes differ')
    review_path = view.control('static-review.json')
    review = read(review_path)
    require(sha(review_path) == REVIEW_JSON_SHA
            and sha(view.control('static-review.md')) == REVIEW_SHA
            and review['schema'] == 'line-dynamic-consumer-v4-independent-static-review/1'
            and review['verdict'] == 'static-review-pass; execution-pending'
            and review['candidateManifestSha256'] == INITIAL_SHA
            and review['changedDynamicTestSha256'] ==
                candidate_hashes['tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.Dynamic.cs']
            and review['unchangedFromFrozenV3']['productionFiles'] == 6
            and review['unchangedFromFrozenV3']['registryFiles'] == 1
            and review['unchangedFromFrozenV3']['allCandidateHashesMatched'] is True
            and review['scope']['registeredDynamicCases'] == list(NEW_SOURCE)
            and review['scope']['rendererWrites'] is False
            and review['scope']['completeSeekPermitted'] is False
            and review['buildRun'] is False and review['testsRun'] is False,
            'Independent static review differs')
    shadow_path = view.shadow('result.json')
    shadow_exit_path = view.shadow_exit()
    require(sha(shadow_path) == pin['shadowResultSha256']
            and sha(shadow_exit_path) == pin['shadowRootExitSha256'],
            'Frozen shadow result or independent root exit differs')
    shadow, root_exit = read(shadow_path), read(shadow_exit_path)
    shadow_receipt_path = view.shadow('controller-handle-exit.json')
    shadow_receipt = read(shadow_receipt_path)
    expected_controls = {str(SHADOW_CONTROL_BASE / name): sha(view.shadow_control(name))
                         for name in SHADOW_CONTROL_NAMES}
    expected_controls.update({
        str(REVIEW / 'review.md'): sha(view.control('static-review.md')),
        str(INITIAL): sha(view.control('candidate-manifest.json')),
        str(SCANNER): sha(view.control('input-scanner.py')),
    })
    for name in ('validation-summary.json', 'artifact-manifest.json',
                 'run/result.json', 'run/inputs-after.json',
                 'run/controller-handle-exit.json', 'run/root-exit.json',
                 'run/publication-pin.json',
                 'fixtures/line-ring-numeric-goldens.json'):
        expected_controls[str(PRIOR / name)] = sha(view.prior(name))
    require(shadow['schema'] == 'line-ring-dynamic-shadow-validation/v1'
            and shadow['passed'] is True and shadow['mainApplied'] is False
            and shadow['candidateAndControlsUnchanged'] is True
            and shadow['mainInputsUnchanged'] is True
            and shadow['referencesUnchanged'] is True
            and shadow['fixturesUnchanged'] is True
            and shadow['goldenUnchanged'] is True
            and shadow['compiledBytesUnchanged'] is True
            and shadow['mainInputsBefore'] == prior_graph
            and shadow['mainInputsAfter'] == prior_graph
            and shadow['expectedShadowInputs'] == expected_graph
            and shadow['candidateInputsBefore'] == candidate_hashes
            and shadow['candidateSnapshotSha256'] == snapshot
            and shadow['basisSummarySha256'] == PRIOR_SUMMARY_SHA
            and shadow['basisResultSha256'] == PRIOR_RESULT_SHA
            and shadow['basisInputGraphSha256'] ==
                sha(view.prior('run/inputs-after.json'))
            and shadow['controlsBefore'] == expected_controls
            and shadow['selectedCases'] ==
                [f'source-{i:02}' for i in NEW_SOURCE] +
                [f'scene-{i:02}' for i in range(20, 25)] + ['basis-01']
            and shadow['sdkGoalComplete'] is False
            and shadow['completeSeekPermitted'] is False
            and shadow['atomicSnapshot'] is False
            and shadow['gameStarted'] is False
            and shadow['installed'] is False
            and shadow['activeChildren'] == []
            and shadow['maxSimultaneousTestChildren'] == 2
            and root_exit['observedBy'] == 'root'
            and root_exit['observedRootExec']['actualExitCode'] == 0
            and root_exit['resultSha256'] == pin['shadowResultSha256']
            and root_exit['handleExitSha256'] == sha(shadow_receipt_path)
            and root_exit['candidateManifestSha256'] == pin['candidateManifestSha256']
            and shadow_receipt['actualExitCode'] == 0
            and shadow_receipt['resultSha256AtExit'] == pin['shadowResultSha256']
            and shadow_receipt['controllerPid'] == shadow['controllerPid']
            and shadow_receipt['handleKeptUntilExit'] is True
            and shadow_receipt['controllerScriptUnchanged'] is True
            and shadow_receipt['sdkGoalComplete'] is False
            and shadow_receipt['completeSeekPermitted'] is False
            and shadow_receipt['controllerScriptSha256'] ==
                sha(view.shadow_control('validate.py'))
            and shadow_receipt['observerSha256'] == sha(view.shadow_control('observe.py'))
            and sha(shadow_receipt_path) == pin['shadowHandleExitSha256']
            and utc(shadow['startedUtc']) <= utc(shadow['finishedUtc'])
            and utc(shadow['finishedUtc']) <= utc(shadow_receipt['finishedUtc'])
            and utc(root_exit['observedUtc']) >= utc(shadow_receipt['finishedUtc']),
            'Original shadow twelve-case execution lacks exact provenance')
    prior_products = read(view.prior('run/result.json'))['productsBeforeTests']
    references = {str(PRIOR / 'binaries/production' / (component + ext)): prior_products[component][ext]
                  for component in SIX[:3] for ext in ('.dll', '.pdb')}
    require(shadow['references'] == references,
            'Shadow source references differ from accepted scene products')
    require(prepared['referenceFilesSha256'] == references,
            'Prepared reference pair hashes differ')
    prior_fixtures = read(view.prior('run/result.json'))['nativeFixtures']
    expected_fixtures = {}
    for key, item in prior_fixtures.items():
        name = 'fixtures/native/' + key + '/' + Path(item['path']).name
        expected_fixtures[key] = {'path': str(PRIOR / name), 'sha256': sha(view.prior(name))}
    prior_manifest = read(view.prior('artifact-manifest.json'))
    for key, item in expected_fixtures.items():
        name = 'fixtures/native/' + key + '/' + Path(item['path']).name
        require(prior_manifest.get(name) == item['sha256'],
                'Accepted scene artifact manifest does not pin fixture: ' + key)
    require(prior_manifest.get('fixtures/line-ring-numeric-goldens.json') ==
                sha(view.prior('fixtures/line-ring-numeric-goldens.json')),
            'Accepted scene artifact manifest does not pin numeric golden')
    require(shadow['nativeFixtures'] == expected_fixtures
            and prepared['nativeFixtures'] == expected_fixtures
            and shadow['goldenSha256'] ==
                sha(view.prior('fixtures/line-ring-numeric-goldens.json'))
            and prepared['goldenSha256'] == shadow['goldenSha256'],
            'Shadow fixture or numeric golden differs from accepted scene archive')
    binaries = shadow['binariesAfterTests']
    require(binaries == shadow['binariesBeforeTests'] and len(binaries) >= 18,
            'Shadow built binary inventory differs')
    for path, digest in binaries.items():
        require(SHA_RE.fullmatch(digest) and sha(view.shadow_binary(path)) == digest,
                'Shadow built binary differs: ' + path)
    require(all(binaries.get(path) == digest for group in
                ('productionPair', 'testPair', 'dependencies')
                for path, digest in shadow[group].items())
            and len(shadow['productionPair']) == 2
            and len(shadow['testPair']) == 2
            and len(shadow['dependencies']) == 14,
            'Shadow production, test, or dependency pairs differ')
    source_names = registry_names(view.candidate(SOURCE_FILE), '[] Cases =', 30)
    selected = [f'source-{i:02}' for i in NEW_SOURCE] + [
        f'scene-{i:02}' for i in range(20, 25)] + ['basis-01']
    require(shadow['caseNames'] == source_names and len(shadow['steps']) == 13
            and [row['name'] for row in shadow['steps']] == ['build', *selected],
            'Frozen dynamic shadow cases differ')
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
            expected_build = [
                str(DOTNET), 'build', str(SHADOW_CONTROL_BASE / 'Judgements.DynamicShadow.csproj'),
                '-c', 'Release', '--artifacts-path', str(SHADOW_RAW / 'artifacts'),
                '--verbosity', 'minimal', '--disable-build-servers', '-m:1',
                '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                '-p:RestoreConfigFile=' + str(SHADOW_CONTROL_BASE / 'NuGet.Config'),
            ]
            require(not passes and not runs
                    and row['arguments'] == expected_build
                    and ('已成功生成。' in body or 'Build succeeded.' in body)
                    and re.findall(r'^\s*(\d+) (?:个错误|Error\(s\))\s*$', body, re.M) == ['0']
                    and re.findall(r'^\s*(\d+) (?:个警告|Warning\(s\))\s*$', body, re.M) == ['0'],
                    'Shadow build output differs')
        else:
            index = int(name[-2:])
            expected = (registry_names(view.prior('compiler-inputs/' + BASIS_FILE),
                                       '[] Cases =', 16)[0]
                        if name == 'basis-01' else source_names[index - 1])
            selector = 'basis' if name == 'basis-01' else 'source'
            assembly = SHADOW_RAW / 'artifacts/bin/Judgements.DynamicShadow/release/RizToolbox.Judgements.Tests.dll'
            require(row['arguments'] == [str(DOTNET), str(assembly), selector, str(index)]
                    and passes == runs == [expected]
                    and re.findall(r'(\d+)/(\d+) passed\.', body) == [('1', '1')],
                    'Shadow actual source selector differs: ' + name)
    if not view.archived:
        require(sha(SHADOW_RAW / 'result.json') == sha(shadow_path)
                and sha(SHADOW_RAW / 'controller-handle-exit.json') == sha(shadow_receipt_path)
                and all(sha(SHADOW_RAW / (row['name'] + '.' + stream + '.log')) ==
                        sha(view.shadow_log(row['name'], stream))
                        for row in shadow['steps'] for stream in ('stdout', 'stderr')),
                'Root-r2 snapshot differs from original shadow result, handle, or logs')
    return manifest, target_map, candidate_hashes, expected_graph, snapshot, source_names


def check_manifest_controls(view, manifest, target_map, pin):
    shadow = read(view.shadow('result.json'))
    require(manifest['shadowResult'] ==
                {'path': str(SHADOW_RAW / 'result.json'), 'sha256': pin['shadowResultSha256']}
            and manifest['shadowHandle'] ==
                {'path': str(SHADOW_RAW / 'controller-handle-exit.json'),
                 'sha256': pin['shadowHandleExitSha256']}
            and manifest['shadowRootExit'] ==
                {'path': str(SHADOW_EXIT), 'sha256': pin['shadowRootExitSha256']},
            'Payload does not bind exact root-r2 terminal evidence')
    expected = dict(shadow['controlsBefore'])
    expected.update(shadow['references'])
    expected.update({item['path']: item['sha256']
                     for item in shadow['nativeFixtures'].values()})
    expected.update(shadow['binariesAfterTests'])
    expected.update({
        str(INTEGRATION / 'prepare.py'): sha(view.control('prepare.py')),
        str(INTEGRATION / 'apply.py'): sha(view.control('apply.py')),
        str(INTEGRATION / 'validate_main.py'): sha(view.control('validate_main.py')),
        str(INTEGRATION / 'observe.py'): sha(view.control('observe.py')),
        str(INTEGRATION / 'root-ready.json'): sha(view.control('root-ready.json')),
        str(SCANNER): sha(view.control('input-scanner.py')),
        str(INITIAL): sha(view.control('candidate-manifest.json')),
        str(REVIEW / 'review.md'): sha(view.control('static-review.md')),
        str(REVIEW / 'review.json'): sha(view.control('static-review.json')),
        str(SHADOW_CONTROL_BASE / 'prepare_shadow.py'): sha(view.shadow_control('prepare_shadow.py')),
        str(SHADOW_CONTROL_BASE / 'validate.py'): sha(view.shadow_control('validate.py')),
        str(SHADOW_CONTROL_BASE / 'observe.py'): sha(view.shadow_control('observe.py')),
        str(SHADOW_RAW / 'result.json'): sha(view.shadow('result.json')),
        str(SHADOW_RAW / 'controller-handle-exit.json'):
            sha(view.shadow('controller-handle-exit.json')),
        str(SHADOW_EXIT): sha(view.shadow_exit()),
    })
    for name in ('validation-summary.json', 'artifact-manifest.json',
                 'run/result.json', 'run/inputs-after.json',
                 'run/controller-handle-exit.json', 'run/root-exit.json',
                 'run/publication-pin.json'):
        expected[str(PRIOR / name)] = sha(view.prior(name))
    for name in target_map:
        expected[str(CANDIDATE / name)] = sha(view.candidate(name))
    require(manifest['controlPins'] == expected,
            'Staging control pin map differs from frozen original sources')


def check_payload(view, prior_graph, inputs, target_map, hashes, snapshot, source_names, pin):
    manifest_path = view.control('payload-manifest.json')
    manifest = read(manifest_path)
    ready_path = view.control('review-receipt.json')
    root_ready_path = view.control('root-ready.json')
    ready = read(ready_path)
    require(sha(manifest_path) == pin['payloadManifestSha256']
            and manifest['schema'] == 'line-ring-dynamic-main-payload/v1'
            and manifest['acceptedSummarySha256'] == PRIOR_SUMMARY_SHA
            and manifest['candidateManifestSha256'] == INITIAL_SHA
            and manifest['inputScannerSha256'] == sha(view.control('input-scanner.py'))
            and manifest['buildScriptSha256'] == BUILD_OLD_SHA
            and sha(view.payload('before', 'scripts/Build.ps1')) == BUILD_OLD_SHA
            and manifest['acceptedInputs'] == 895
            and manifest['expectedInputs'] == 898
            and manifest['writerCases'] == 36
            and manifest['oldSourceCases'] == 24
            and manifest['sourceCases'] == 30
            and manifest['defaultCases'] == 419
            and manifest['legacyCasesListed'] == 300
            and manifest['sourceCaseNames'] == source_names
            and manifest['candidateSnapshotSha256'] == snapshot
            and manifest['candidateFilesSha256'] == hashes
            and manifest['rootReadySha256'] == sha(ready_path)
            and manifest['shadowResult']['sha256'] == pin['shadowResultSha256']
            and manifest['shadowHandle']['sha256'] == sha(view.shadow('controller-handle-exit.json'))
            and manifest['shadowRootExit']['sha256'] == pin['shadowRootExitSha256']
            and manifest['freshMainValidationPassed'] is False
            and manifest['sdkGoalComplete'] is False
            and manifest['completeSeekPermitted'] is False,
            'Nine-file payload scope differs')
    require(sha(root_ready_path) == sha(ready_path)
            and read(root_ready_path) == ready
            and ready['reviewedBy'] == 'root'
            and ready['readyForMainIntegration'] is True
            and ready['candidateSnapshotSha256'] == snapshot
            and ready['candidateFilesSha256'] == hashes
            and ready['sourceCases'] == 30 and ready['writerCases'] == 36
            and ready['targets'] == sorted([*target_map.values(), 'scripts/Build.ps1'])
            and ready['expectedMainInputs'] == 898
            and ready['expectedDefaultCases'] == 419
            and ready['review']['sha256'] == sha(view.control('static-review.md'))
            and ready['shadowResult']['sha256'] == pin['shadowResultSha256']
            and ready['shadowHandle']['sha256'] == sha(view.shadow('controller-handle-exit.json'))
            and ready['shadowRootExitSha256'] == pin['shadowRootExitSha256']
            and ready['freshMainValidationPassed'] is False
            and ready['sdkGoalComplete'] is False
            and ready['completeSeekPermitted'] is False
            and ready['atomicSnapshot'] is False
            and ready['deterministicTarget'] is False,
            'Root candidate review or root-ready receipt differs')
    check_manifest_controls(view, manifest, target_map, pin)
    rows = manifest['files']
    require(len(rows) == 9
            and {row['path'] for row in rows} == set(target_map.values()) | {'scripts/Build.ps1'}
            and len({row['path'] for row in rows}) == 9,
            'Payload is not the exact eight C# files and Build selector')
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
                    and payload_digest == pin['controlSha256']['controls/Build.ps1']
                    and payload_digest == sha(view.control('Build.ps1')),
                    'Build selector payload or accepted original differs')
            before_text = safe_file(view.payload('before', target)).read_text(encoding='utf-8-sig')
            after_text = safe_file(view.control('Build.ps1')).read_text(encoding='utf-8-sig')
            old = "$lastCase = if ($selector -eq '--line-ring-source-case') { 24 } else { 16 }"
            new = "$lastCase = if ($selector -eq '--line-ring-source-case') { 30 } else { 16 }"
            require(before_text.count(old) == 1 and after_text == before_text.replace(old, new),
                    'Build source dispatch differs beyond 24-to-30 selector')
        else:
            require(source == target and payload_digest == hashes[source]
                    and payload_digest == sha(view.candidate(source))
                    and payload_digest == sha(view.input(target)),
                    'Candidate, staged, or compiled source differs: ' + target)
            if target in NEW_FILES:
                require(row['baseSha256'] is None and target not in prior_graph,
                        'New dynamic C# source has an accepted base')
            else:
                require(row['baseSha256'] == prior_graph[target]
                        and row['baseSha256'] == sha(view.payload('before', target)),
                        'Staged base differs from accepted scene source: ' + target)
            expected_graph[target] = payload_digest
    require(inputs == expected_graph and len(inputs) == 898,
            'Applied compiler input graph is not exact 895-plus-three dynamic source')
    application_path = view.control('application-record.json')
    application = read(application_path)
    require(application['schema'] == 'line-ring-dynamic-main-application/v1'
            and application['appliedToMain'] is True
            and application['acceptedInputs'] == 895
            and application['newInputs'] == 898
            and application['manifestSha256'] == sha(manifest_path)
            and application['rootReadySha256'] == sha(ready_path)
            and application['shadowResultSha256'] == pin['shadowResultSha256']
            and application['shadowHandleSha256'] == sha(view.shadow('controller-handle-exit.json'))
            and application['shadowRootExitSha256'] == pin['shadowRootExitSha256']
            and application['writerCases'] == 36
            and application['sourceCases'] == 30
            and application['defaultCases'] == 419
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
            'payloadManifestSha256', 'candidateSnapshotSha256', 'candidateManifestSha256',
            'shadowResultSha256', 'shadowHandleExitSha256', 'shadowRootExitSha256')
    require(pin['schema'] == 'line-dynamic-consumer-publication-pin/v1'
            and pin['authoredBy'] == 'root'
            and all(SHA_RE.fullmatch(pin.get(key, '')) for key in keys)
            and pin['resultSha256'] == sha(view.run('result.json'))
            and pin['payloadManifestSha256'] == sha(view.control('payload-manifest.json'))
            and pin['candidateSnapshotSha256'] == snapshot
            and pin['candidateManifestSha256'] == INITIAL_SHA
            and pin['rootExitPath'] ==
                'work/line-dynamic-consumer-integration-20261113/root-exit.json'
            and isinstance(pin['reviews'], list) and len(pin['reviews']) >= 1,
            'Explicit root publication pin differs')
    control_hashes = {'controls/' + name: sha(view.control(name))
                      for name in CONTROL_LIVE}
    control_hashes.update({'shadow/controls/' + name: sha(view.shadow_control(name))
                           for name in SHADOW_CONTROL_NAMES})
    require(pin['controlSha256'] == control_hashes,
            'Root publication control SHA-256 map differs')
    review_paths = {item['path'] for item in pin['reviews']}
    require({'work/line-dynamic-consumer-v4-review-20261113/review.md',
             'work/line-dynamic-consumer-v4-review-20261113/review.json'} <= review_paths,
            'Final independent v4 review is not root pinned')
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
    require(len(rows) == len(names) == 78
            and [row['name'] for row in rows] == names
            and len({row['name'] for row in rows}) == 78,
            'Fresh main step list is incomplete or reordered')
    listing = read(view.run('legacy-case-list.json'))
    require(listing == legacy, 'Legacy 300 names or order changed')
    writer_names = registry_names(view.input(WRITER_FILE), '[] EndpointWriterCases =', 36)
    require(writer_names == registry_names(view.prior('compiler-inputs/' + WRITER_FILE),
                                           '[] EndpointWriterCases =', 36),
            'Previously accepted 36 writer names or order changed')
    require(source_names == registry_names(view.input(SOURCE_FILE), '[] Cases =', 30)
            and source_names[:24] == registry_names(view.prior('compiler-inputs/' + SOURCE_FILE),
                                                    '[] Cases =', 24),
            'Previously accepted 24 source names or order changed')
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
    require(total == 155, 'Fresh main focused total is not 155')
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
    require(result['schema'] == 'line-ring-dynamic-main-validation/v1'
            and result['passed'] is True
            and result['compilerInputs'] == 898
            and result['defaultExpectedCases'] == 419
            and result['writerCases'] == 36
            and result['sourceCases'] == 30
            and result['expectedFocusedPassedCases'] == 155
            and result['maxSimultaneousTestChildren'] == 2
            and result['inputsUnchanged'] is True
            and result['controlsUnchanged'] is True
            and result['nativeFixturesUnchanged'] is True
            and result['goldenUnchanged'] is True
            and result['compiledBytesUnchanged'] is True
            and result['activeChildren'] == []
            and result['old300ExecutedThisBatch'] is False
            and result['default419SingleProcessExecuted'] is False
            and result['fullSource30ExecutedThisBatch'] is True
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
            and len(inputs) == 898
            and all(SHA_RE.fullmatch(value) and sha(view.input(name)) == value
                    for name, value in inputs.items()),
            'Complete 898 compiler-input byte graph differs')
    pin = read(view.pin())
    require(pin['candidateManifestSha256'] == INITIAL_SHA
            and pin['shadowResultSha256'] == sha(view.shadow('result.json'))
            and pin['shadowHandleExitSha256'] == sha(view.shadow('controller-handle-exit.json'))
            and pin['shadowRootExitSha256'] == sha(view.shadow_exit()),
            'Root pin does not identify final v4 shadow evidence')
    candidate, target_map, hashes, expected_graph, snapshot, source_names = \
        check_candidate_and_shadow(view, prior_graph, pin)
    require(inputs == expected_graph,
            'Compiler input graph differs from exactly eight dynamic replacements')
    manifest, application = check_payload(view, prior_graph, inputs,
                                          target_map, hashes, snapshot, source_names, pin)
    controls = {str(path): sha(view.control(name)) for name, path in CONTROL_LIVE.items()
                if name in ('validate_main.py', 'prepare.py', 'apply.py', 'observe.py',
                            'payload-manifest.json', 'review-receipt.json',
                            'root-ready.json', 'application-record.json', 'Build.ps1',
                            'candidate-manifest.json', 'input-scanner.py', 'NuGet.Config')}
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
    controls.update(manifest['controlPins'])
    controls[str(PRIOR / 'fixtures/line-ring-numeric-goldens.json')] = \
        sha(view.prior('fixtures/line-ring-numeric-goldens.json'))
    require(result['controlsBefore'] == controls,
            'Fresh controller control pin map differs')
    pin = check_pin_and_exit(view, result, snapshot)
    passed = check_steps(view, result, prior_result, legacy, source_names)
    check_binaries(view, result, inputs)
    fixtures = result['nativeFixtures']
    require(fixtures == read(view.shadow('result.json'))['nativeFixtures']
            and len(fixtures) == 3
            and set(fixtures) == {'RIZ_INITIALIZATION_ABI_FIXTURE',
                                  'RIZ_INITIALIZATION_CONTEXT_HELPER',
                                  'RIZ_INITIALIZATION_ABI_DOBBY'},
            'Three native fixtures differ from accepted world result')
    for key, item in fixtures.items():
        require(item['sha256'] == sha(view.fixture(key, item['path'])),
                'Native fixture differs: ' + key)
    require(result['goldenSha256'] ==
                sha(view.prior('fixtures/line-ring-numeric-goldens.json')),
            'Executed golden fixture differs from accepted scene source')
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
                 'compiler-inputs/' + SOURCE_FILE,
                 'compiler-inputs/' + BASIS_FILE,
                 'artifact-manifest.json', 'run/controller-handle-exit.json',
                 'run/root-exit.json', 'run/publication-pin.json',
                 'fixtures/line-ring-numeric-goldens.json'):
        plan.add(view.prior(name), 'prior/' + name)
    for component in SIX[:3]:
        for ext in ('.dll', '.pdb'):
            name = 'binaries/production/' + component + ext
            plan.add(view.prior(name), 'prior/' + name)
    for key, item in read(view.prior('run/result.json'))['nativeFixtures'].items():
        name = 'fixtures/native/' + key + '/' + Path(item['path']).name
        plan.add(view.prior(name), 'prior/' + name, item['sha256'])
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
    plan.add(view.shadow('result.json'), 'shadow/result.json', pin['shadowResultSha256'])
    plan.add(view.shadow('controller-handle-exit.json'),
             'shadow/controller-handle-exit.json', pin['shadowHandleExitSha256'])
    plan.add(view.shadow_exit(), 'shadow/root-shadow-exit.json', pin['shadowRootExitSha256'])
    for name in SHADOW_CONTROL_NAMES:
        plan.add(view.shadow_control(name), 'shadow/controls/' + name)
    for row in read(view.shadow('result.json'))['steps']:
        for stream in ('stdout', 'stderr'):
            name = row['name']
            plan.add(view.shadow_log(name, stream),
                     'shadow/logs/' + name + '.' + stream + '.log', row[stream + 'Sha256'])
    for original, digest in read(view.shadow('result.json'))['binariesAfterTests'].items():
        name = 'shadow/binaries/' + Path(original).relative_to(SHADOW_RAW / 'artifacts/bin').as_posix()
        plan.add(view.shadow_binary(original), name, digest)
    for index, item in enumerate(pin['reviews'], 1):
        plan.add(view.review(index, item['path']),
                 'reviews/' + str(index).zfill(2) + '-' + Path(item['path']).name,
                 item['sha256'])
    plan.add(view.prior_product_review(), 'reviews/prior-product-review.md',
             PRIOR_PRODUCT_REVIEW_SHA)
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
        'schema': 'line-dynamic-consumer-main-evidence/v1',
        'acceptedAtUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'acceptedThisBatch': True, 'compilerInputs': 898,
        'defaultExpectedCases': 419, 'writerCases': 36, 'sourceCases': 30,
        'naturalSteps': 78, 'focusedPassedCases': passed,
        'judgementsPassedCases': 104, 'runtimeAbiPassedCases': 51,
        'sixReleaseWarnings': 0, 'buildErrors': 0,
        'productionPairs': 6, 'testPairs': 2, 'dependencyFiles': 12,
        'nativeFixtures': 3, 'legacyCasesListed': 300,
        'old300ExecutedThisBatch': False,
        'default419SingleProcessExecuted': False,
        'fullSource30ExecutedThisBatch': True,
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
        'priorSceneSummarySha256': PRIOR_SUMMARY_SHA,
        'priorSceneResultSha256': PRIOR_RESULT_SHA,
        'shadowResultSha256': pin['shadowResultSha256'],
        'shadowHandleExitSha256': pin['shadowHandleExitSha256'],
        'shadowRootExitSha256': pin['shadowRootExitSha256'],
        'scope': 'Offline original Canvas and endpoint inputs through observed dynamic SetPositions producer buffers; sequential history only',
    }
    plan.generate('validation-summary.json', encoded(summary))
    plan.generate('README.md', (
        '# Dynamic producer consumer evidence\n\n'
        'The exact 898-input main graph completed 78 natural scoped steps with '
        '155 focused PASS results (104 Judgements, 51 Runtime ABI), including all '
        '30 source cases and 36 endpoint writer cases. The root pinned the original '
        'controller handle exit, an independent exit observation, the nine-file '
        'payload, final v4 candidate, and reviews. The accepted 895-input scene result '
        'and twelve-case dynamic shadow are included as ancestors to fresh main acceptance.\n\n'
        'The 300 legacy names were listed in order; their cases, the full 16 basis '
        'cases, and the default 419 registry in one process were not executed here. '
        'Dynamic producer inputs and post-write world observations remain sequential, '
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
    require(summary['schema'] == 'line-dynamic-consumer-main-evidence/v1'
            and summary['acceptedThisBatch'] is True
            and summary['compilerInputs'] == len(inputs) == 898
            and summary['defaultExpectedCases'] == 419
            and summary['writerCases'] == 36
            and summary['sourceCases'] == 30
            and summary['naturalSteps'] == len(result['steps']) == 78
            and summary['focusedPassedCases'] == passed == 155
            and summary['judgementsPassedCases'] == 104
            and summary['runtimeAbiPassedCases'] == 51
            and summary['productionPairs'] == 6
            and summary['testPairs'] == 2
            and summary['dependencyFiles'] == 12
            and summary['nativeFixtures'] == 3
            and summary['legacyCasesListed'] == 300
            and summary['sixReleaseWarnings'] == summary['buildErrors'] == 0
            and summary['old300ExecutedThisBatch'] is False
            and summary['default419SingleProcessExecuted'] is False
            and summary['fullSource30ExecutedThisBatch'] is True
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
            and summary['priorSceneSummarySha256'] == PRIOR_SUMMARY_SHA
            and summary['priorSceneResultSha256'] == PRIOR_RESULT_SHA
            and summary['shadowResultSha256'] == pin['shadowResultSha256']
            and summary['shadowHandleExitSha256'] == pin['shadowHandleExitSha256']
            and summary['shadowRootExitSha256'] == pin['shadowRootExitSha256'],
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
    require(required <= found and 'publisher/publish.py' in found,
            'Archive self-contained compiler, logs, binaries, or publisher missing')
    return {'acceptedThisBatch': True, 'archiveFiles': len(found),
            'archiveBytes': total, 'summarySha256': sha(path / 'validation-summary.json'),
            'sdkGoalComplete': False, 'completeSeekPermitted': False}


def publish(pin_path_arg):
    require(not ARCHIVE.exists(), 'Preserve existing dynamic evidence archive')
    view = View(RUN, False, pin_path_arg)
    result, inputs, pin, passed = audit(view)
    plan = make_plan(view, result, inputs, pin, passed)
    staging = ARCHIVE.parent / ('.line-dynamic-consumer-staging-' + uuid.uuid4().hex)
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
    require(not ARCHIVE.exists(), 'Concurrent dynamic evidence archive appeared')
    staging.rename(ARCHIVE)
    return check_archive(ARCHIVE)


def preflight(pin_path_arg):
    require(not ARCHIVE.exists(), 'Dynamic evidence archive already exists')
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
