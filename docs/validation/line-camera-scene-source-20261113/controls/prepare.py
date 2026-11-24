"""Stage the exact scene candidate and source-dispatch edit after root review.

--inventory is read only. Staging snapshots payload and rollback bytes without
changing the accepted main tree. The initial frozen candidate manifest is
provenance; a repaired candidate is accepted only through a new review receipt.
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
CANDIDATE = ROOT / 'work/line-camera-scene-next-20261113'
INITIAL = CANDIDATE / 'candidate-manifest.json'
ACCEPTED = ROOT / 'docs/validation/line-ring-world-read-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
ACCEPTED_SUMMARY_SHA = 'ce8b17aa85be2ad8923c9792abfb375c38389ee34ee2b8c17fe2c5f6126cdc9b'
INITIAL_MANIFEST_SHA = 'bc3e4e119041ad1eef20a0efb0a80ab4f0f0c92fb1b74375d8fafc64710aa006'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
BUILD_SHA = '19624697b8976c2d55c7be8971dbaa97a3dd2c67e2693164fba943f6d2b1e62b'
SOURCE_TEST = 'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.cs'
TARGET_MAP = {row['candidatePath']: row['targetPath'] for row in json.loads(INITIAL.read_text(encoding='utf-8-sig'))['files']}
BUILD = 'scripts/Build.ps1'
SHADOW_BASE = Path('D:/riz-build-cache-next/line-camera-scene-read-20261113')
SHADOW_SHA = '5d91b993cc2de2d977ded043b07e5fdb8e5f69b8921a061bce181db89b64435a'
SHADOW_HANDLE_SHA = 'fa4e05b90d6e71fde27e6f54a214b9501cf2422c73145569de71ea8368402fdf'
REVIEW = ROOT / 'work/line-camera-scene-review-20261113/review.md'
REVIEW_SHA = '3c4362bdef59b5e3c5b7ef51d4328df8d2e25e307ac1b02176334790bcc082ca'
BUILD_OLD = "$lastCase = if ($selector -eq '--line-ring-source-case') { 19 } else { 16 }"
BUILD_NEW = "$lastCase = if ($selector -eq '--line-ring-source-case') { 24 } else { 16 }"


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


def input_scanner():
    require(sha(SCANNER) == SCANNER_SHA, 'Accepted input scanner changed')
    spec = importlib.util.spec_from_file_location('world_integration_inputs', SCANNER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def registry(data, minimum=24):
    text = data.decode('utf-8-sig')
    require(text.count('[] Cases =') == 1, 'Source registry declaration changed')
    block = text.split('[] Cases =', 1)[1].split('};', 1)[0]
    rows = re.findall(r'\("([^"\r\n]+)",\s*([A-Za-z_]\w*)\)', block)
    names = [name for name, _ in rows]
    require(len(names) >= minimum and len(names) == len(set(names))
            and block.count('("') == len(rows)
            and all(name.startswith('Line ring source ') for name in names),
            'Source registry names or entries changed unexpectedly')
    return names


def initial_provenance(before):
    require(sha(INITIAL) == INITIAL_MANIFEST_SHA, 'Initial frozen manifest changed')
    record = read(INITIAL)
    require(record['schema'] == 'line-ring-scene-current-source-candidate/v2'
            and record['newSourceCaseIndexes'] == list(range(20, 25))
            and record['legacyCasePrefixPreserved'] is True
            and record['mainApplied'] is False
            and record['built'] is False and record['testsExecuted'] is False,
            'Initial candidate provenance differs')
    rows = record['files']
    require(len(rows) == 10 and {row['candidatePath']: row['targetPath'] for row in rows} == TARGET_MAP
            and set(TARGET_MAP) == set(TARGET_MAP.values())
            and all(name.startswith(('src/', 'tests/')) and name.endswith('.cs') for name in TARGET_MAP),
            'Initial manifest target map differs')
    new = {name for name in TARGET_MAP.values() if name not in before}
    require(new == {
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Scene.cs',
        'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingSource.Scene.cs',
        'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.Scene.cs',
    } and all(row['observedMainBaseSha256'] == before.get(row['targetPath'])
              and row['candidateSha256'] == sha(CANDIDATE / row['candidatePath']) for row in rows),
            'Candidate base, additions or payload hashes differ')


def candidate_snapshot():
    actual = {path.relative_to(CANDIDATE).as_posix()
              for folder in ('src', 'tests') for path in (CANDIDATE / folder).rglob('*.cs')}
    require(actual == set(TARGET_MAP), 'Candidate C# inventory differs from ten-file whitelist')
    hashes = {name: sha(CANDIDATE / name) for name in sorted(TARGET_MAP)}
    snapshot = digest(json.dumps(hashes, sort_keys=True, separators=(',', ':')).encode('utf-8'))
    return hashes, snapshot


def build_payload():
    original = (ROOT / BUILD).read_bytes()
    old = BUILD_OLD.encode('utf-8')
    require(original.count(old) == 1 and original.count(BUILD_NEW.encode('utf-8')) == 0,
            'Build source range anchor changed or duplicated')
    return original.replace(old, BUILD_NEW.encode('utf-8'))


def inventory():
    require(sha(ACCEPTED / 'validation-summary.json') == ACCEPTED_SUMMARY_SHA,
            'Accepted 892 evidence summary changed')
    summary = read(ACCEPTED / 'validation-summary.json')
    require(summary['acceptedThisBatch'] is True and summary['compilerInputs'] == 892
            and summary['writerCases'] == 36 and summary['defaultExpectedCases'] == 408
            and summary['legacyCasesListed'] == 300, 'Accepted main scope differs')
    require(sha(ROOT / 'scripts/Build.ps1') == BUILD_SHA, 'Build dispatch changed')
    before = read(ACCEPTED / 'run/inputs-after.json')
    require(len(before) == 892 and input_scanner().inputs() == before,
            'Main is not the accepted 892-input graph')
    initial_provenance(before)
    hashes, snapshot = candidate_snapshot()
    names = registry((CANDIDATE / SOURCE_TEST).read_bytes())
    prior = registry((ROOT / SOURCE_TEST).read_bytes(), 19)
    require(len(names) == 24 and len(prior) == 19 and names[:19] == prior,
            'Accepted first 19 source names or order changed')
    require(all((ROOT / target).is_file() == (target in before) for target in TARGET_MAP.values()),
            'Expected seven replacements and three new main targets')
    require(sha(REVIEW) == REVIEW_SHA, 'Independent candidate review changed')
    build_payload()
    require(candidate_snapshot() == (hashes, snapshot)
            and input_scanner().inputs() == before, 'Candidate or main changed during inventory')
    return before, hashes, snapshot, names


def verify_review(receipt, hashes, snapshot, names, before):
    review = json.loads(receipt.decode('utf-8-sig'))
    require(review.get('reviewedBy') == 'root'
            and review.get('readyForMainIntegration') is True
            and review.get('candidateSnapshotSha256') == snapshot
            and review.get('candidateFilesSha256') == hashes
            and review.get('sourceCases') == len(names)
            and review.get('targets') == sorted([*TARGET_MAP.values(), BUILD]),
            'Root review receipt does not identify the final eleven-file payload')
    note = review.get('review')
    require(isinstance(note, dict) and isinstance(note.get('path'), str)
            and isinstance(note.get('sha256'), str), 'Final root review evidence is required')
    note_path = (ROOT / note['path']).resolve()
    require(note_path == REVIEW.resolve() and note['sha256'] == REVIEW_SHA
            and sha(note_path) == note['sha256'],
            'Final root review evidence changed or escaped workspace')
    evidence = review.get('shadowResult')
    require(isinstance(evidence, dict) and isinstance(evidence.get('path'), str)
            and isinstance(evidence.get('sha256'), str), 'Passing shadow result is required')
    shadow_path = Path(evidence['path']).resolve()
    require(shadow_path == (SHADOW_BASE / 'root-r1/result.json').resolve()
            and sha(shadow_path) == evidence['sha256'] == SHADOW_SHA,
            'Shadow result path or hash differs')
    shadow = read(shadow_path)
    new_indexes = list(range(20, 25))
    require(shadow.get('passed') is True and shadow.get('mainApplied') is False
            and shadow.get('sourceAndControlsUnchanged') is True
            and shadow.get('fixturesUnchanged') is True
            and shadow.get('mainInputsBefore') == before
            and shadow.get('candidateInputsBefore') == hashes
            and shadow.get('caseNames') == names
            and shadow.get('selectedCases') == new_indexes,
            'Shadow result is not a passing final-candidate run')
    steps = shadow.get('steps')
    require(isinstance(steps, list) and len(steps) == len(new_indexes) + 1,
            'Shadow build or new-case steps are missing')
    require(steps[0]['name'] == 'build' and steps[0]['exitCode'] == 0
            and steps[0]['naturalExit'] and steps[0]['matchesExpected'],
            'Shadow build did not pass naturally')
    by_name = {row['name']: row for row in steps[1:]}
    require(len(by_name) == len(new_indexes), 'Duplicate shadow case step')
    for index in new_indexes:
        row = by_name.get(f'scene-{index:02}')
        require(row is not None and row['exitCode'] == 0 and row['naturalExit']
                and row['matchesExpected'] and row['passNames'] == [names[index - 1]]
                and str(row['arguments'][-1]) == str(index),
                f'Shadow case {index} is not the reviewed pass')
    handle_evidence = review.get('shadowHandle')
    require(isinstance(handle_evidence, dict)
            and handle_evidence.get('path') == str((SHADOW_BASE / 'root-r1/controller-handle-exit.json').resolve())
            and handle_evidence.get('sha256') == SHADOW_HANDLE_SHA
            and sha(handle_evidence['path']) == SHADOW_HANDLE_SHA,
            'Original shadow controller handle receipt changed')
    handle = read(handle_evidence['path'])
    require(handle.get('schema') == 'original-controller-handle-exit/v1'
            and handle.get('controllerPid') == shadow.get('controllerPid')
            and handle.get('actualExitCode') == 0
            and handle.get('handleKeptUntilExit') is True
            and handle.get('controllerScriptUnchanged') is True
            and handle.get('resultSha256AtExit') == SHADOW_SHA,
            'Shadow controller did not have a proven original natural exit')
    return evidence, handle_evidence


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--inventory', action='store_true')
    parser.add_argument('--review-receipt', type=Path)
    parser.add_argument('--review-sha256')
    opt = parser.parse_args()
    before, hashes, snapshot, names = inventory()
    if opt.inventory:
        require(not opt.review_receipt and not opt.review_sha256, 'Inventory accepts no receipt')
        print(json.dumps({'candidateSnapshotSha256': snapshot, 'candidateFilesSha256': hashes,
                          'targets': sorted([*TARGET_MAP.values(), BUILD]), 'sourceCases': len(names),
                          'sourceCaseNames': names, 'expectedMainInputs': 895,
                          'expectedDefaultCases': 408 + len(names) - 19}, indent=2))
        return
    require(opt.review_receipt and opt.review_sha256, 'Explicit root receipt and SHA-256 are required')
    receipt = opt.review_receipt.resolve().read_bytes()
    require(digest(receipt) == opt.review_sha256, 'Root review receipt hash differs')
    shadow, handle = verify_review(receipt, hashes, snapshot, names, before)
    require(not any((WORK / name).exists() for name in
                    ('payload-manifest.json', 'review-receipt.json', 'application-record.json',
                     'failed-application.json', 'files', 'before', 'inputs-before.json')),
            'Preserve prior staging or application history')
    rows = []
    for candidate_name, target_name in sorted(TARGET_MAP.items()):
        rows.append({'candidatePath': candidate_name, 'path': target_name,
                     'baseSha256': before.get(target_name), 'payloadSha256': hashes[candidate_name]})
    rows.append({'candidatePath': None, 'path': BUILD,
                 'baseSha256': BUILD_SHA, 'payloadSha256': digest(build_payload())})
    manifest = {'schema': 'line-ring-scene-main-payload/v1',
                'stagedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                'acceptedSummarySha256': ACCEPTED_SUMMARY_SHA,
                'initialCandidateManifestSha256': INITIAL_MANIFEST_SHA,
                'inputScannerSha256': SCANNER_SHA, 'buildScriptSha256': BUILD_SHA,
                'acceptedInputs': 892, 'expectedInputs': 895,
                'writerCases': 36, 'oldSourceCases': 19, 'sourceCases': len(names),
                'defaultCases': 408 + len(names) - 19, 'legacyCasesListed': 300,
                'sourceCaseNames': names, 'candidateSnapshotSha256': snapshot,
                'candidateFilesSha256': hashes, 'reviewReceiptSha256': digest(receipt),
                'shadowResult': shadow, 'shadowHandle': handle, 'files': rows,
                'freshMainValidationPassed': False, 'sdkGoalComplete': False,
                'completeSeekPermitted': False}
    require(candidate_snapshot() == (hashes, snapshot)
            and input_scanner().inputs() == before, 'Candidate or main changed before staging')
    for row in rows:
        payload = build_payload() if row['path'] == BUILD else (CANDIDATE / row['candidatePath']).read_bytes()
        write_new(WORK / 'files' / row['path'], payload)
        if row['baseSha256'] is not None:
            write_new(WORK / 'before' / row['path'], (ROOT / row['path']).read_bytes())
    write_new(WORK / 'inputs-before.json', json_bytes(before))
    write_new(WORK / 'review-receipt.json', receipt)
    require(all(sha(WORK / 'files' / row['path']) == row['payloadSha256']
                and (sha(WORK / 'before' / row['path']) == row['baseSha256']
                     if row['baseSha256'] is not None else not (WORK / 'before' / row['path']).exists())
                for row in rows)
            and candidate_snapshot() == (hashes, snapshot)
            and input_scanner().inputs() == before, 'Staged or live bytes changed')
    write_new(WORK / 'payload-manifest.json', json_bytes(manifest))
    print(json.dumps({'stagedTargets': sorted([*TARGET_MAP.values(), BUILD]),
                      'payloadManifestSha256': sha(WORK / 'payload-manifest.json'),
                      'reviewReceiptSha256': digest(receipt), 'sourceCases': len(names),
                      'expectedMainInputs': 895, 'mainChanged': False}, indent=2))


if __name__ == '__main__':
    main()
