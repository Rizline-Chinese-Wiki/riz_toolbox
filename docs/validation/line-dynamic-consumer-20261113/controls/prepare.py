"""Stage the exact dynamic candidate after the real root shadow exits.

Only this work directory is written. Main, builds and tests are untouched.
"""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
from pathlib import Path
import re

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
CANDIDATE = ROOT / 'work/line-dynamic-consumer-next-20261113'
SCENE = ROOT / 'docs/validation/line-camera-scene-source-20261113'
SHADOW_WORK = ROOT / 'work/line-dynamic-consumer-root-20261113'
SHADOW_BASE = Path('D:/riz-build-cache-next/line-dynamic-consumer-read-20261113')
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
REVIEW = ROOT / 'work/line-dynamic-consumer-v4-review-20261113/review.md'
REVIEW_JSON = REVIEW.with_suffix('.json')
INITIAL = CANDIDATE / 'candidate-manifest.json'
ROOT_READY = WORK / 'root-ready.json'
BUILD = 'scripts/Build.ps1'
SOURCE_TEST = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
ACCEPTED_SUMMARY_SHA = '98e554f44d1ae84f30368e1c13162e2c45a1a9a758cb666e621612bd9e7c4bef'
INITIAL_MANIFEST_SHA = '2ddcaf6c8e1b5ee193d62efdc8a2a921e4656689db7079d199d91128da46b172'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
SHADOW_PREPARE_SHA = '92d478241560c6368ccc1418bfef958f5828c7ecfe6076000860d6abbc60f414'
REVIEW_SHA = '5ece16e56c69f43aa3ba9233af097564795be786672fb0c59329caa3b2b92401'
REVIEW_JSON_SHA = '44ba66d8f9978212cae65645f58eb978e9bbe919e1fadb01d64f1333868bd646'
BUILD_SHA = '1da3db9f7130555493a852ef9f05daf44a738d2248473a9e9387023f8e9f9dc9'
BUILD_OLD = "$lastCase = if ($selector -eq '--line-ring-source-case') { 24 } else { 16 }"
BUILD_NEW = "$lastCase = if ($selector -eq '--line-ring-source-case') { 30 } else { 16 }"
TARGET_MAP = {row['candidatePath']: row['targetPath']
              for row in json.loads(INITIAL.read_text(encoding='utf-8-sig'))['files']}


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def write_new(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def shadow_prepare():
    path = SHADOW_WORK / 'prepare_shadow.py'
    require(sha(path) == SHADOW_PREPARE_SHA, 'Root shadow preparation control changed')
    spec = importlib.util.spec_from_file_location('dynamic_integration_shadow_prepare', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def input_scanner():
    require(sha(SCANNER) == SCANNER_SHA, 'Compiler-input scanner changed')
    spec = importlib.util.spec_from_file_location('dynamic_integration_inputs', SCANNER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def registry(data, expected):
    body = data.decode('utf-8-sig')
    require(body.count('[] Cases =') == 1, 'Source registry declaration changed')
    block = body.split('[] Cases =', 1)[1].split('};', 1)[0]
    rows = re.findall(r'\("([^"\r\n]+)",\s*([A-Za-z_]\w*)\)', block)
    names = [name for name, _ in rows]
    require(len(names) == expected and len(set(names)) == expected
            and block.count('("') == expected
            and all(name.startswith('Line ring source ') for name in names),
            'Source registry entries changed')
    return names


def build_payload():
    original = (ROOT / BUILD).read_bytes()
    old, new = BUILD_OLD.encode('utf-8'), BUILD_NEW.encode('utf-8')
    require(sha(ROOT / BUILD) == BUILD_SHA and original.count(old) == 1
            and original.count(new) == 0, 'Build source range anchor changed')
    return original.replace(old, new)


def inventory(basis_sha):
    require(re.fullmatch(r'[0-9a-f]{64}', basis_sha)
            and basis_sha == ACCEPTED_SUMMARY_SHA, 'Explicit accepted scene SHA-256 differs')
    helper = shadow_prepare()
    summary, before, _, _, _ = helper.accepted_basis(basis_sha)
    require(summary['resultSha256'] == '3dfaaf45458db2a76bea37d541d9db0d6ce2cf6fe3c55728eca38ec654857ff0'
            and summary['sourceCases'] == 24 and summary['writerCases'] == 36
            and summary['legacyCasesListed'] == 300, 'Accepted scene result or registry changed')
    require(input_scanner().inputs() == before and len(before) == 895,
            'Main is not the accepted 895-input graph')
    require(sha(INITIAL) == INITIAL_MANIFEST_SHA
            and sha(REVIEW) == REVIEW_SHA and sha(REVIEW_JSON) == REVIEW_JSON_SHA,
            'Final candidate manifest or independent review changed')
    review = read(REVIEW_JSON)
    require(review['schema'] == 'line-dynamic-consumer-v4-independent-static-review/1'
            and review['verdict'] == 'static-review-pass; execution-pending'
            and review['candidateManifestSha256'] == INITIAL_MANIFEST_SHA
            and review['candidateHandoffSha256'] == read(INITIAL)['handoffSha256']
            and review['changedDynamicTestSha256'] == read(INITIAL)['files'][-1]['candidateSha256']
            and review['unchangedFromFrozenV3'] == {
                'productionFiles': 6, 'registryFiles': 1, 'allCandidateHashesMatched': True}
            and review['scope']['registeredDynamicCases'] == list(range(25, 31))
            and review['scope']['rendererWrites'] is False
            and review['scope']['completeSeekPermitted'] is False
            and review['buildRun'] is False and review['testsRun'] is False,
            'Independent v4 static review differs')
    _, hashes, snapshot, after, names = helper.candidate_snapshot(INITIAL_MANIFEST_SHA, before)
    require(len(hashes) == 8 and len(after) == 898 and len(names) == 30
            and names[:24] == registry((ROOT / SOURCE_TEST).read_bytes(), 24)
            and names == registry((CANDIDATE / SOURCE_TEST).read_bytes(), 30)
            and set(TARGET_MAP) == set(hashes)
            and all(TARGET_MAP[name] == name for name in hashes),
            'Eight-file candidate or source registry changed')
    require(sha(ROOT / BUILD) == BUILD_SHA and len(build_payload()) == (ROOT / BUILD).stat().st_size,
            'Build source range edit must be one line and same length')
    return before, hashes, snapshot, names, after


def verified_path(row, expected):
    require(isinstance(row, dict) and isinstance(row.get('path'), str)
            and re.fullmatch(r'[0-9a-f]{64}', str(row.get('sha256', ''))),
            'Missing path and SHA-256 evidence')
    path = Path(row['path'])
    path = (path if path.is_absolute() else ROOT / path).resolve()
    require(path == expected.resolve() and sha(path) == row['sha256'],
            'Evidence path or content changed: ' + str(expected))
    return path


def verify_review(receipt_bytes, basis_sha, before, hashes, snapshot, names, after):
    record = json.loads(receipt_bytes.decode('utf-8-sig'))
    require(record.get('reviewedBy') == 'root'
            and record.get('readyForMainIntegration') is True
            and record.get('candidateManifestSha256') == INITIAL_MANIFEST_SHA
            and record.get('candidateFilesSha256') == hashes
            and record.get('candidateSnapshotSha256') == snapshot
            and record.get('basisSummarySha256') == basis_sha
            and record.get('sourceCases') == 30 and record.get('writerCases') == 36
            and record.get('expectedMainInputs') == 898
            and record.get('expectedDefaultCases') == 419
            and all(record.get(key) is False for key in
                    ('freshMainValidationPassed', 'sdkGoalComplete', 'completeSeekPermitted',
                     'atomicSnapshot', 'deterministicTarget', 'gameStarted', 'installed')),
            'Root readiness does not bind the exact reviewed 898-input scope')
    verified_path(record.get('review'), REVIEW)
    require(record['review']['sha256'] == REVIEW_SHA,
            'Root readiness does not bind independent review')
    result_row = record.get('shadowResult')
    require(isinstance(result_row, dict) and isinstance(result_row.get('path'), str),
            'Root shadow result missing')
    result_path = Path(result_row['path']).resolve()
    require(result_path.parent.parent == SHADOW_BASE.resolve()
            and re.fullmatch(r'root-r[1-9][0-9]*', result_path.parent.name)
            and result_path.name == 'result.json', 'Shadow result must be an actual root attempt')
    verified_path(result_row, result_path)
    result = read(result_path)
    selected = [f'source-{index:02}' for index in range(25, 31)] + [
        f'scene-{index:02}' for index in range(20, 25)] + ['basis-01']
    require(result.get('schema') == 'line-ring-dynamic-shadow-validation/v1'
            and result.get('passed') is True and result.get('mainApplied') is False
            and result.get('candidateManifestSha256') == INITIAL_MANIFEST_SHA
            and result.get('candidateInputsBefore') == hashes
            and result.get('candidateSnapshotSha256') == snapshot
            and result.get('basisSummarySha256') == basis_sha
            and result.get('mainInputsBefore') == before
            and result.get('mainInputsAfter') == before
            and result.get('expectedShadowInputs') == after
            and result.get('caseNames') == names
            and result.get('selectedCases') == selected
            and all(result.get(key) is True for key in
                    ('mainInputsUnchanged', 'candidateAndControlsUnchanged',
                     'referencesUnchanged', 'fixturesUnchanged', 'goldenUnchanged',
                     'compiledBytesUnchanged'))
            and all(result.get(key) is False for key in
                    ('sdkGoalComplete', 'completeSeekPermitted', 'atomicSnapshot',
                     'gameStarted', 'installed')),
            'Shadow result is not the passing final candidate and accepted base')
    steps = result.get('steps')
    basis_name = {row['name']: row for row in read(SCENE / 'run/result.json')['steps']}['basis-01']['passNames'][0]
    expected_names = [names[int(name[-2:]) - 1] if name != 'basis-01' else basis_name
                      for name in selected]
    require(isinstance(steps, list) and len(steps) == 13
            and [row.get('name') for row in steps] == ['build', *selected]
            and all(row.get('exitCode') == 0 and row.get('naturalExit') is True
                    and row.get('matchesExpected') is True for row in steps)
            and steps[0]['passNames'] == [] and steps[0]['runNames'] == []
            and all(row.get('passNames') == row.get('runNames') == [expected]
                    for row, expected in zip(steps[1:], expected_names)),
            'Shadow did not finish the twelve selected actual checks')
    require(all(sha(result_path.parent / (row['name'] + ext)) == row[key]
                for row in steps for ext, key in
                (('.stdout.log', 'stdoutSha256'), ('.stderr.log', 'stderrSha256'))),
            'Shadow child logs changed')
    frozen = dict(result['controlsBefore'])
    frozen.update(result['references'])
    frozen.update({item['path']: item['sha256']
                   for item in result['nativeFixtures'].values()})
    frozen.update(result['binariesAfterTests'])
    require(result['binariesBeforeTests'] == result['binariesAfterTests']
            and all(sha(path) == value for path, value in frozen.items()),
            'Shadow controls, native fixtures, references or built binaries changed')
    handle_path = verified_path(record.get('shadowHandle'), result_path.with_name('controller-handle-exit.json'))
    handle = read(handle_path)
    require(handle.get('schema') == 'original-controller-handle-exit/v1'
            and handle.get('controllerPid') == result['controllerPid']
            and handle.get('actualExitCode') == 0
            and handle.get('handleKeptUntilExit') is True
            and handle.get('controllerScriptUnchanged') is True
            and handle.get('resultSha256AtExit') == result_row['sha256']
            and handle.get('controllerScriptSha256') ==
                result['controlsBefore'][str(SHADOW_WORK / 'validate.py')],
            'Original shadow controller did not exit naturally')
    root_path = Path(record.get('rootShadowExitPath', ''))
    root_path = (root_path if root_path.is_absolute() else ROOT / root_path).resolve()
    require(root_path == (SHADOW_WORK / 'root-shadow-exit.json').resolve()
            and sha(root_path) == record.get('shadowRootExitSha256'),
            'Root-observed shadow exit record changed or escaped workspace')
    root_exit = read(root_path)
    require(root_exit.get('observedBy') == 'root'
            and root_exit.get('observedRootExec', {}).get('actualExitCode') == 0
            and root_exit.get('resultSha256') == result_row['sha256']
            and root_exit.get('handleExitSha256') == record['shadowHandle']['sha256']
            and root_exit.get('controllerPid') == result['controllerPid']
            and root_exit.get('scriptSha256', root_exit.get('controllerScriptSha256')) ==
                handle['controllerScriptSha256'],
            'Root did not observe the same original shadow controller exit')
    return record['shadowResult'], record['shadowHandle'], {
        'path': str(root_path), 'sha256': sha(root_path)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--basis-summary-sha256', required=True)
    parser.add_argument('--root-ready-sha256', required=True)
    parser.add_argument('--inventory', action='store_true')
    opt = parser.parse_args()
    require(re.fullmatch(r'[0-9a-f]{64}', opt.root_ready_sha256)
            and sha(ROOT_READY) == opt.root_ready_sha256, 'Explicit root-ready SHA-256 differs')
    before, hashes, snapshot, names, after = inventory(opt.basis_summary_sha256)
    receipt = ROOT_READY.read_bytes()
    shadow, handle, root_exit = verify_review(
        receipt, opt.basis_summary_sha256, before, hashes, snapshot, names, after)
    if opt.inventory:
        print(json.dumps({'candidateSnapshotSha256': snapshot,
                          'candidateFilesSha256': hashes,
                          'targets': sorted([*TARGET_MAP.values(), BUILD]),
                          'sourceCases': 30, 'expectedMainInputs': 898,
                          'rootReadySha256': opt.root_ready_sha256,
                          'mainChanged': False}, indent=2))
        return
    require(not any((WORK / name).exists() for name in
                    ('payload-manifest.json', 'review-receipt.json',
                     'application-record.json', 'failed-application.json',
                     'files', 'before', 'inputs-before.json')),
            'Preserve prior staging or application history')
    rows = [{'candidatePath': candidate_name, 'path': target_name,
             'baseSha256': before.get(target_name), 'payloadSha256': hashes[candidate_name]}
            for candidate_name, target_name in sorted(TARGET_MAP.items())]
    rows.append({'candidatePath': None, 'path': BUILD,
                 'baseSha256': BUILD_SHA, 'payloadSha256': digest(build_payload())})
    pins = {str(path): sha(path) for path in
            (WORK / 'prepare.py', WORK / 'apply.py', WORK / 'validate_main.py',
             WORK / 'observe.py', ROOT_READY,
             SCANNER, INITIAL, REVIEW, REVIEW_JSON, SHADOW_WORK / 'prepare_shadow.py',
             SHADOW_WORK / 'validate.py', SHADOW_WORK / 'observe.py',
             SCENE / 'validation-summary.json', SCENE / 'artifact-manifest.json',
             SCENE / 'run/result.json', SCENE / 'run/inputs-after.json',
             SCENE / 'run/controller-handle-exit.json', SCENE / 'run/root-exit.json',
             SCENE / 'run/publication-pin.json', Path(shadow['path']),
             Path(handle['path']), Path(root_exit['path']),
             *(CANDIDATE / name for name in TARGET_MAP))}
    shadow_result = read(shadow['path'])
    pins.update(shadow_result['controlsBefore'])
    pins.update(shadow_result['references'])
    pins.update({item['path']: item['sha256']
                 for item in shadow_result['nativeFixtures'].values()})
    pins.update(shadow_result['binariesAfterTests'])
    manifest = {
        'schema': 'line-ring-dynamic-main-payload/v1',
        'stagedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
        'acceptedSummarySha256': opt.basis_summary_sha256,
        'candidateManifestSha256': INITIAL_MANIFEST_SHA,
        'inputScannerSha256': SCANNER_SHA, 'buildScriptSha256': BUILD_SHA,
        'acceptedInputs': 895, 'expectedInputs': 898,
        'writerCases': 36, 'oldSourceCases': 24, 'sourceCases': 30,
        'defaultCases': 419, 'legacyCasesListed': 300,
        'sourceCaseNames': names, 'candidateSnapshotSha256': snapshot,
        'candidateFilesSha256': hashes, 'rootReadySha256': digest(receipt),
        'shadowResult': shadow, 'shadowHandle': handle,
        'shadowRootExit': root_exit, 'controlPins': pins, 'files': rows,
        'freshMainValidationPassed': False, 'sdkGoalComplete': False,
        'completeSeekPermitted': False,
    }
    require(input_scanner().inputs() == before
            and all(sha(CANDIDATE / name) == value for name, value in hashes.items()),
            'Candidate or main changed before staging')
    for row in rows:
        data = build_payload() if row['path'] == BUILD else (CANDIDATE / row['candidatePath']).read_bytes()
        write_new(WORK / 'files' / row['path'], data)
        if row['baseSha256'] is not None:
            write_new(WORK / 'before' / row['path'], (ROOT / row['path']).read_bytes())
    write_new(WORK / 'inputs-before.json', json_bytes(before))
    write_new(WORK / 'review-receipt.json', receipt)
    require(all(sha(WORK / 'files' / row['path']) == row['payloadSha256']
                and (sha(WORK / 'before' / row['path']) == row['baseSha256']
                     if row['baseSha256'] is not None else
                     not (WORK / 'before' / row['path']).exists())
                for row in rows)
            and all(sha(path) == value for path, value in pins.items())
            and sha(ROOT_READY) == opt.root_ready_sha256
            and input_scanner().inputs() == before, 'Staged or live bytes changed')
    write_new(WORK / 'payload-manifest.json', json_bytes(manifest))
    print(json.dumps({'stagedTargets': sorted([*TARGET_MAP.values(), BUILD]),
                      'payloadManifestSha256': sha(WORK / 'payload-manifest.json'),
                      'rootReadySha256': digest(receipt),
                      'expectedMainInputs': 898, 'mainChanged': False}, indent=2))


if __name__ == '__main__':
    main()
