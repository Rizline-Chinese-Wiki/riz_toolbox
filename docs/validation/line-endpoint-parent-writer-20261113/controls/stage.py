"""Snapshot a reviewed parent-writer candidate into an isolated, exact-byte payload.

This script never changes the accepted main tree. First run --inventory and put its
candidateSnapshotSha256 into a root-authored review receipt. Staging requires the
SHA-256 of that receipt on the command line, so a later repair cannot be mistaken
for the candidate that was reviewed.
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
CANDIDATE = ROOT / 'work/line-endpoint-parent-writer-20261113'
ACCEPTED = ROOT / 'docs/validation/line-endpoint-writer-20261113'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
ACCEPTED_SUMMARY_SHA = 'adbc97f1301e5c163165d2fa21fc4725a8c9892c9960f0a58c69e65e10f60fc6'
SCANNER_SHA = '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4'
BUILD_SHA = '19624697b8976c2d55c7be8971dbaa97a3dd2c67e2693164fba943f6d2b1e62b'

# Each candidate source is enumerated. Files identical to accepted main are omitted.
SOURCE_PATHS = (
    'src/RizToolbox.Runtime/NativeRuntime.LineEndpointParentMetadata.cs',
    'src/RizToolbox.Runtime/NativeRuntime.LineEndpointPublication.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekCanvasPositionNativeProfile.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.EndpointAccess.cs',
    'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs',
    'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs',
)
TEST_MAP = {
    'tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs':
        'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs',
    'tests/FakeNative/FakeNative.CanvasPositions.cs':
        'tests/RizToolbox.Judgements.Tests/CanvasPositions/FakeNative.CanvasPositions.cs',
}
PROGRAM = 'tests/RizToolbox.Judgements.Tests/Program.cs'
EXPECTED_TARGETS = {
    SOURCE_PATHS[0], SOURCE_PATHS[2], SOURCE_PATHS[3], SOURCE_PATHS[4], SOURCE_PATHS[7],
    *TEST_MAP.values(), PROGRAM,
}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def sha(path):
    return digest(Path(path).read_bytes())


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_new(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def input_scanner():
    require(sha(SCANNER) == SCANNER_SHA, 'Accepted input scanner changed')
    spec = importlib.util.spec_from_file_location('parent_writer_input_scan', SCANNER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def candidate_snapshot():
    expected = {str(CANDIDATE / path) for path in SOURCE_PATHS}
    actual = {str(path) for path in (CANDIDATE / 'src').rglob('*.cs')}
    require(actual == expected, 'Candidate source inventory changed; review its scope')
    expected_tests = {str(CANDIDATE / path) for path in TEST_MAP}
    actual_tests = {str(path) for path in (CANDIDATE / 'tests').rglob('*.cs')}
    actual_tests.discard(str(CANDIDATE / 'tests/Program.cs'))  # shadow harness only
    require(actual_tests == expected_tests, 'Candidate test inventory changed; review its scope')
    names = (*SOURCE_PATHS, *TEST_MAP.keys())
    hashes = {name: sha(CANDIDATE / name) for name in names}
    return hashes, digest(json.dumps(hashes, sort_keys=True, separators=(',', ':')).encode('utf-8'))


def registry(candidate):
    rows = re.findall(rb'\("actual (endpoint|parent) writer ([^"\r\n]+)",\s*([A-Za-z_]\w*)\)', candidate)
    require(len(rows) >= 29, 'Expected at least 29 independent writer cases')
    names = [('Line ring source ' + group.decode() + ' writer ' + label.decode()).strip()
             for group, label, _ in rows]
    require(len(set(names)) == len(names), 'Writer case names are not unique')
    require(len(re.findall(rb'\("actual (?:endpoint|parent) writer ', candidate)) == len(rows),
            'A writer registry entry was not parsed')
    require(len([row for row in rows if row[0] == b'endpoint']) == 17,
            'The original 17 endpoint cases changed their count')
    require(len([row for row in rows if row[0] == b'parent']) >= 12,
            'Expected the parent and metadata regressions')
    return names


def main_program(before):
    newline = b'\r\n' if b'\r\n' in before else b'\n'
    anchor = b'else if (args.Length == 2 && args[0] == "--line-ring-source-case"'
    require(before.count(anchor) == 1 and b'--line-ring-endpoint-writer-case' not in before,
            'Main writer case selector is unexpected')
    insertion = newline.join((
        b'else if (args.Length == 2 && args[0] == "--line-ring-endpoint-writer-case" && int.TryParse(args[1], out var writerCase)',
        b'    && writerCase >= 1 && writerCase <= RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases.Length)',
        b'{',
        b'    tests = new[] { RizToolbox.Judgements.Tests.LineRingSource.NativeSeekLineRingSourceChecks.EndpointWriterCases[writerCase - 1] };',
        b'}', b''))
    after = before.replace(anchor, insertion + anchor)
    old = b'--line-ring-endpoint-writer-checks|--line-ring-source-checks'
    new = b'--line-ring-endpoint-writer-checks|--line-ring-endpoint-writer-case N|--line-ring-source-checks'
    require(after.count(old) == 1, 'Exact usage insertion point changed')
    after = after.replace(old, new)
    require(after.count(b'"--line-ring-endpoint-writer-checks" =>') == 1,
            'Existing group writer selector changed')
    require(after.count(b'!test.Name.StartsWith("Line ring source ", StringComparison.Ordinal)') >= 1,
            'Legacy writer exclusion changed')
    return after


def inventory():
    require(sha(ACCEPTED / 'validation-summary.json') == ACCEPTED_SUMMARY_SHA,
            'Accepted 891 evidence summary changed')
    summary = read_json(ACCEPTED / 'validation-summary.json')
    require(summary['acceptedThisBatch'] is True and summary['compilerInputs'] == 891
            and summary['writerCases'] == 17 and summary['legacyCasesListed'] == 300,
            'Accepted main summary has different scope')
    require(sha(ROOT / 'scripts/Build.ps1') == BUILD_SHA, 'Main Build.ps1 changed')
    before = read_json(ACCEPTED / 'run/inputs-after.json')
    require(len(before) == 891 and input_scanner().inputs() == before,
            'Current main is not the accepted 891 compiler-input graph')
    hashes, snapshot = candidate_snapshot()
    candidate_test = (CANDIDATE / 'tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs').read_bytes()
    names = registry(candidate_test)
    require(b'"actual endpoint writer ' in candidate_test and b'"actual parent writer ' in candidate_test,
            'Candidate registry labels changed')
    payload_test = candidate_test.replace(b'"actual endpoint writer ', b'"Line ring source endpoint writer ')
    payload_test = payload_test.replace(b'"actual parent writer ', b'"Line ring source parent writer ')
    require(len(registry(candidate_test)) == len(names), 'Candidate registry changed while reading')
    files = {}
    for name in SOURCE_PATHS:
        candidate = (CANDIDATE / name).read_bytes()
        main = ROOT / name
        if not main.exists() or main.read_bytes() != candidate:
            files[name] = (candidate, name, candidate)
    for candidate_name, target_name in TEST_MAP.items():
        candidate = (CANDIDATE / candidate_name).read_bytes()
        payload = payload_test if candidate_name.endswith('EndpointWriter.cs') else candidate
        if (ROOT / target_name).read_bytes() != payload:
            files[target_name] = (payload, candidate_name, candidate)
    program = (ROOT / PROGRAM).read_bytes()
    files[PROGRAM] = (main_program(program), None, None)
    require(set(files) == EXPECTED_TARGETS, 'Target set differs from reviewed eight-file scope')
    require(sum(not (ROOT / name).exists() for name in files) == 1,
            'Expected exactly one new compiler input')
    require(all(hashes[name] == digest((CANDIDATE / name).read_bytes()) for name in hashes),
            'Candidate changed during inventory')
    require(input_scanner().inputs() == before, 'Main changed during inventory')
    return before, hashes, snapshot, names, files


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--inventory', action='store_true', help='read-only candidate and main inventory')
    parser.add_argument('--review-receipt', type=Path)
    parser.add_argument('--review-sha256')
    opt = parser.parse_args()
    before, hashes, snapshot, names, files = inventory()
    if opt.inventory:
        require(not opt.review_receipt and not opt.review_sha256, 'Inventory accepts no receipt')
        print(json.dumps({'candidateSnapshotSha256': snapshot, 'candidateFilesSha256': hashes,
                          'targets': sorted(files), 'writerCases': len(names),
                          'expectedMainInputs': 892, 'expectedDefaultCases': 372 + len(names)}, indent=2))
        return
    require(opt.review_receipt and opt.review_sha256, 'A root review receipt and explicit SHA-256 are required')
    receipt = opt.review_receipt.resolve().read_bytes()
    require(digest(receipt) == opt.review_sha256, 'Review receipt hash differs')
    review = json.loads(receipt.decode('utf-8-sig'))
    require(review.get('readyForMainIntegration') is True
            and review.get('reviewedBy') == 'root'
            and review.get('candidateSnapshotSha256') == snapshot,
            'Root review receipt does not authorize this exact candidate')
    require(not (WORK / 'payload-manifest.json').exists()
            and not (WORK / 'application-record.json').exists(), 'Preserve previous staging/application history')
    rows = []
    for name in sorted(files):
        payload, candidate_name, candidate = files[name]
        target = ROOT / name
        rows.append({'path': name, 'kind': 'replacement' if target.exists() else 'new',
                     'baseSha256': sha(target) if target.exists() else None,
                     'payloadSha256': digest(payload), 'candidatePath': candidate_name,
                     'candidateSha256': digest(candidate) if candidate is not None else None})
    manifest = {'schema': 'line-endpoint-parent-main-payload/v1',
                'stagedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                'acceptedSummarySha256': ACCEPTED_SUMMARY_SHA, 'inputScannerSha256': SCANNER_SHA,
                'buildScriptSha256': BUILD_SHA, 'acceptedInputs': 891, 'expectedInputs': 892,
                'oldWriterCases': 17, 'writerCases': len(names), 'defaultCases': 372 + len(names),
                'legacyCasesListed': 300, 'writerCaseNames': names,
                'candidateSnapshotSha256': snapshot, 'candidateFilesSha256': hashes,
                'reviewReceiptSha256': digest(receipt), 'files': rows,
                'freshMainValidationPassed': False, 'sdkGoalComplete': False,
                'completeSeekPermitted': False}
    for name, (payload, _, _) in files.items():
        write_new(WORK / 'files' / name, payload)
        target = ROOT / name
        if target.exists():
            write_new(WORK / 'before' / name, target.read_bytes())
    write_new(WORK / 'inputs-before.json', json_bytes(before))
    write_new(WORK / 'review-receipt.json', receipt)
    write_new(WORK / 'payload-manifest.json', json_bytes(manifest))
    print(json.dumps({'stagedTargets': sorted(files), 'payloadManifestSha256': sha(WORK / 'payload-manifest.json'),
                      'reviewReceiptSha256': digest(receipt), 'writerCases': len(names),
                      'expectedMainInputs': 892, 'mainChanged': False}, indent=2))


if __name__ == '__main__':
    main()
