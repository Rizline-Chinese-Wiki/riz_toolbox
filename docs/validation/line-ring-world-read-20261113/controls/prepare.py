"""Stage four exact candidate files after root review and a passing shadow run.

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
CANDIDATE = ROOT / 'work/line-ring-next-functional-20261113/candidate'
INITIAL = ROOT / 'work/line-ring-next-functional-20261113/candidate-manifest.json'
ACCEPTED = ROOT / 'docs/validation/line-endpoint-parent-writer-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
ACCEPTED_SUMMARY_SHA = 'b21930d90ff9f3970d4441d4b77b104551496af75b6c97e9725983c876e344f0'
INITIAL_MANIFEST_SHA = '42bda5978831e1fe51b5cc0e89ea77ac72e12e6f5c8d68ca18baa539593000ee'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
BUILD_SHA = '19624697b8976c2d55c7be8971dbaa97a3dd2c67e2693164fba943f6d2b1e62b'
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
TEST = 'tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs'
SHADOW_BASE = Path('D:/riz-build-cache-next/line-ring-world-read-20261113')


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


def registry(data, minimum=36):
    text = data.decode('utf-8-sig')
    require(text.count('[] EndpointWriterCases =') == 1, 'Writer registry declaration changed')
    block = text.split('[] EndpointWriterCases =', 1)[1].split('};', 1)[0]
    rows = re.findall(r'\("([^"\r\n]+)",\s*([A-Za-z_]\w*)\)', block)
    names = [name for name, _ in rows]
    require(len(names) >= minimum and len(names) == len(set(names))
            and block.count('("') == len(rows)
            and all(name.startswith('Line ring source ') for name in names),
            'Writer registry names or entries changed unexpectedly')
    return names


def initial_provenance(before):
    require(sha(INITIAL) == INITIAL_MANIFEST_SHA, 'Initial frozen manifest changed')
    record = read(INITIAL)
    require(record['schema'] == 'line-ring-postwrite-world-candidate/v1'
            and record['acceptedBaseInputs'] == 892
            and record['acceptedBaseSummarySha256'] == ACCEPTED_SUMMARY_SHA
            and record['writerRegistryCases'] == 36
            and record['newCaseIndexes'] == list(range(32, 37))
            and record['expectedMainInputsIfApplied'] == 892
            and record['mainApplied'] is False,
            'Initial candidate provenance differs')
    rows = record['files']
    require(len(rows) == 4 and {row['candidatePath']: row['targetPath'] for row in rows} == TARGET_MAP,
            'Initial manifest target map differs')
    require(all(row['acceptedBaseSha256'] == before[row['targetPath']] for row in rows),
            'Initial candidate base differs from accepted main')


def candidate_snapshot():
    actual = {path.relative_to(CANDIDATE).as_posix() for path in CANDIDATE.rglob('*.cs')}
    require(actual == set(TARGET_MAP), 'Candidate C# inventory is outside the four-file whitelist')
    hashes = {name: sha(CANDIDATE / name) for name in sorted(TARGET_MAP)}
    snapshot = digest(json.dumps(hashes, sort_keys=True, separators=(',', ':')).encode('utf-8'))
    return hashes, snapshot


def inventory():
    require(sha(ACCEPTED / 'validation-summary.json') == ACCEPTED_SUMMARY_SHA,
            'Accepted 892 evidence summary changed')
    summary = read(ACCEPTED / 'validation-summary.json')
    require(summary['acceptedThisBatch'] is True and summary['compilerInputs'] == 892
            and summary['writerCases'] == 31 and summary['defaultExpectedCases'] == 403
            and summary['legacyCasesListed'] == 300, 'Accepted main scope differs')
    require(sha(ROOT / 'scripts/Build.ps1') == BUILD_SHA, 'Build dispatch changed')
    before = read(ACCEPTED / 'run/inputs-after.json')
    require(len(before) == 892 and input_scanner().inputs() == before,
            'Main is not the accepted 892-input graph')
    initial_provenance(before)
    hashes, snapshot = candidate_snapshot()
    names = registry((CANDIDATE / TEST).read_bytes())
    prior = registry((ROOT / TARGET_MAP[TEST]).read_bytes(), 31)
    require(len(prior) == 31 and names[:31] == prior,
            'Accepted 31 writer names or order changed')
    require(all((ROOT / target).is_file() for target in TARGET_MAP.values()),
            'Expected four existing main targets')
    require(candidate_snapshot() == (hashes, snapshot)
            and input_scanner().inputs() == before, 'Candidate or main changed during inventory')
    return before, hashes, snapshot, names


def verify_review(receipt, hashes, snapshot, names, before):
    review = json.loads(receipt.decode('utf-8-sig'))
    require(review.get('reviewedBy') == 'root'
            and review.get('readyForMainIntegration') is True
            and review.get('candidateSnapshotSha256') == snapshot
            and review.get('candidateFilesSha256') == hashes
            and review.get('writerCases') == len(names)
            and review.get('targets') == sorted(TARGET_MAP.values()),
            'Root review receipt does not identify the final four-file candidate')
    note = review.get('review')
    require(isinstance(note, dict) and isinstance(note.get('path'), str)
            and isinstance(note.get('sha256'), str), 'Final root review evidence is required')
    note_path = (ROOT / note['path']).resolve()
    require(note_path.is_relative_to(ROOT.resolve()) and sha(note_path) == note['sha256'],
            'Final root review evidence changed or escaped workspace')
    evidence = review.get('shadowResult')
    require(isinstance(evidence, dict) and isinstance(evidence.get('path'), str)
            and isinstance(evidence.get('sha256'), str), 'Passing shadow result is required')
    shadow_path = Path(evidence['path']).resolve()
    require(shadow_path.name == 'result.json'
            and re.fullmatch(r'root-r[1-9][0-9]*', shadow_path.parent.name)
            and shadow_path.parent.parent == SHADOW_BASE.resolve()
            and sha(shadow_path) == evidence['sha256'], 'Shadow result path or hash differs')
    shadow = read(shadow_path)
    new_indexes = list(range(32, len(names) + 1))
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
        row = by_name.get(f'world-{index:02}')
        require(row is not None and row['exitCode'] == 0 and row['naturalExit']
                and row['matchesExpected'] and row['passNames'] == [names[index - 1]]
                and str(row['arguments'][-1]) == str(index),
                f'Shadow case {index} is not the reviewed pass')
    return evidence


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
                          'targets': sorted(TARGET_MAP.values()), 'writerCases': len(names),
                          'expectedMainInputs': 892, 'expectedDefaultCases': 403 - 31 + len(names)}, indent=2))
        return
    require(opt.review_receipt and opt.review_sha256, 'Explicit root receipt and SHA-256 are required')
    receipt = opt.review_receipt.resolve().read_bytes()
    require(digest(receipt) == opt.review_sha256, 'Root review receipt hash differs')
    shadow = verify_review(receipt, hashes, snapshot, names, before)
    require(not any((WORK / name).exists() for name in
                    ('payload-manifest.json', 'review-receipt.json', 'application-record.json',
                     'failed-application.json', 'files', 'before', 'inputs-before.json')),
            'Preserve prior staging or application history')
    rows = []
    for candidate_name, target_name in sorted(TARGET_MAP.items()):
        rows.append({'candidatePath': candidate_name, 'path': target_name,
                     'baseSha256': before[target_name], 'payloadSha256': hashes[candidate_name]})
    manifest = {'schema': 'line-ring-world-main-payload/v1',
                'stagedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                'acceptedSummarySha256': ACCEPTED_SUMMARY_SHA,
                'initialCandidateManifestSha256': INITIAL_MANIFEST_SHA,
                'inputScannerSha256': SCANNER_SHA, 'buildScriptSha256': BUILD_SHA,
                'acceptedInputs': 892, 'expectedInputs': 892,
                'oldWriterCases': 31, 'writerCases': len(names),
                'defaultCases': 403 - 31 + len(names), 'legacyCasesListed': 300,
                'writerCaseNames': names, 'candidateSnapshotSha256': snapshot,
                'candidateFilesSha256': hashes, 'reviewReceiptSha256': digest(receipt),
                'shadowResult': shadow, 'files': rows,
                'freshMainValidationPassed': False, 'sdkGoalComplete': False,
                'completeSeekPermitted': False}
    require(candidate_snapshot() == (hashes, snapshot)
            and input_scanner().inputs() == before, 'Candidate or main changed before staging')
    for row in rows:
        write_new(WORK / 'files' / row['path'], (CANDIDATE / row['candidatePath']).read_bytes())
        write_new(WORK / 'before' / row['path'], (ROOT / row['path']).read_bytes())
    write_new(WORK / 'inputs-before.json', json_bytes(before))
    write_new(WORK / 'review-receipt.json', receipt)
    require(all(sha(WORK / 'files' / row['path']) == row['payloadSha256']
                and sha(WORK / 'before' / row['path']) == row['baseSha256'] for row in rows)
            and candidate_snapshot() == (hashes, snapshot)
            and input_scanner().inputs() == before, 'Staged or live bytes changed')
    write_new(WORK / 'payload-manifest.json', json_bytes(manifest))
    print(json.dumps({'stagedTargets': sorted(TARGET_MAP.values()),
                      'payloadManifestSha256': sha(WORK / 'payload-manifest.json'),
                      'reviewReceiptSha256': digest(receipt), 'writerCases': len(names),
                      'expectedMainInputs': 892, 'mainChanged': False}, indent=2))


if __name__ == '__main__':
    main()
