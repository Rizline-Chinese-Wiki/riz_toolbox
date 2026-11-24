"""Apply only the root-reviewed staged payload to the exact accepted 891 graph."""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
ACCEPTED = ROOT / 'docs/validation/line-endpoint-writer-20261113'


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_new(path, value):
    with Path(path).open('x', encoding='utf-8') as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write('\n')


def load_stage():
    spec = importlib.util.spec_from_file_location('parent_writer_stage_apply', WORK / 'stage.py')
    stage = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(stage)
    return stage


def replace_exact(target, data, expected):
    require(target.resolve().is_relative_to(ROOT.resolve())
            and not any(path.is_symlink() for path in (target, *target.parents) if path.exists()),
            'Target escaped main or traversed a symlink')
    require((sha(target) if target.exists() else None) == expected, 'Concurrent target change')
    if expected is None:
        with target.open('xb') as stream:
            stream.write(data)
        return
    with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
        staged = Path(stream.name)
        stream.write(data)
    try:
        require(sha(target) == expected, 'Concurrent target change before atomic replacement')
        os.replace(staged, target)
    finally:
        staged.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--review-sha256', required=True,
                        help='SHA-256 of the root-authored ready receipt copied by stage.py')
    parser.add_argument('--manifest-sha256', required=True,
                        help='SHA-256 of the reviewed payload-manifest.json')
    opt = parser.parse_args()
    require(not (WORK / 'application-record.json').exists(), 'Preserve original application record')
    require(not (WORK / 'failed-application.json').exists(), 'Resolve prior failed application first')
    require(sha(WORK / 'payload-manifest.json') == opt.manifest_sha256,
            'Payload manifest changed since root review')
    manifest = read(WORK / 'payload-manifest.json')
    stage = load_stage()
    require(manifest['schema'] == 'line-endpoint-parent-main-payload/v1'
            and manifest['acceptedSummarySha256'] == stage.ACCEPTED_SUMMARY_SHA
            and manifest['inputScannerSha256'] == stage.SCANNER_SHA
            and manifest['buildScriptSha256'] == stage.BUILD_SHA
            and manifest['acceptedInputs'] == 891 and manifest['expectedInputs'] == 892,
            'Staged payload scope differs')
    require(sha(WORK / 'review-receipt.json') == opt.review_sha256 == manifest['reviewReceiptSha256'],
            'Root ready receipt changed or was not explicitly supplied')
    review = read(WORK / 'review-receipt.json')
    require(review.get('readyForMainIntegration') is True and review.get('reviewedBy') == 'root'
            and review.get('candidateSnapshotSha256') == manifest['candidateSnapshotSha256'],
            'Ready receipt does not name the staged candidate')
    require(sha(ACCEPTED / 'validation-summary.json') == stage.ACCEPTED_SUMMARY_SHA
            and read(ACCEPTED / 'validation-summary.json')['acceptedThisBatch'] is True,
            'Prior accepted evidence changed')
    require(sha(ROOT / 'scripts/Build.ps1') == stage.BUILD_SHA,
            'Accepted build dispatch changed')
    scanner = stage.input_scanner()
    before = read(WORK / 'inputs-before.json')
    require(before == read(ACCEPTED / 'run/inputs-after.json') and len(before) == 891
            and scanner.inputs() == before, 'Main is not the accepted 891 graph')
    hashes, snapshot = stage.candidate_snapshot()
    require(snapshot == manifest['candidateSnapshotSha256']
            and hashes == manifest['candidateFilesSha256'], 'Candidate changed after staging')
    candidate_test = (stage.CANDIDATE / 'tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs').read_bytes()
    require(stage.registry(candidate_test) == manifest['writerCaseNames']
            and manifest['writerCases'] == len(manifest['writerCaseNames'])
            and manifest['defaultCases'] == 372 + manifest['writerCases'],
            'Final writer registry changed after review')
    rows = manifest['files']
    require(len(rows) == 8 and {row['path'] for row in rows} == stage.EXPECTED_TARGETS
            and len({row['path'] for row in rows}) == len(rows), 'Unreviewed target set')
    after = dict(before)
    for row in rows:
        name = row['path']
        target = ROOT / name
        payload = WORK / 'files' / name
        require((sha(target) if target.exists() else None) == row['baseSha256']
                and sha(payload) == row['payloadSha256'], 'Target or payload changed: ' + name)
        require(row['kind'] == ('replacement' if target.exists() else 'new'),
                'Replacement classification changed: ' + name)
        if target.exists():
            require(sha(WORK / 'before' / name) == row['baseSha256'],
                    'Exact rollback bytes missing: ' + name)
        if row['candidatePath'] is not None:
            source = stage.CANDIDATE / row['candidatePath']
            require(sha(source) == row['candidateSha256'], 'Candidate file changed: ' + name)
            if name.endswith('NativeSeekLineRingSourceChecks.EndpointWriter.cs'):
                expected = source.read_bytes().replace(b'"actual endpoint writer ', b'"Line ring source endpoint writer ')
                expected = expected.replace(b'"actual parent writer ', b'"Line ring source parent writer ')
                require(payload.read_bytes() == expected, 'Writer test bodies changed during registry conversion')
            else:
                require(payload.read_bytes() == source.read_bytes(), 'Candidate source was edited in payload: ' + name)
        elif name == stage.PROGRAM:
            require(payload.read_bytes() == stage.main_program(target.read_bytes()),
                    'Main Program modification is not the isolated numbered selector')
        else:
            raise RuntimeError('Payload without candidate source: ' + name)
        after[name] = row['payloadSha256']
    require(len(after) == 892 and set(after) - set(before) == {stage.SOURCE_PATHS[0]},
            'Expected exactly one new compiler input')
    changed = []
    try:
        expected = dict(before)
        for row in rows:
            require(scanner.inputs() == expected, 'Concurrent compiler input change')
            replace_exact(ROOT / row['path'], (WORK / 'files' / row['path']).read_bytes(), row['baseSha256'])
            changed.append(row)
            expected[row['path']] = row['payloadSha256']
        require(scanner.inputs() == after and sha(ROOT / 'scripts/Build.ps1') == stage.BUILD_SHA,
                'Written graph differs from reviewed payload')
        write_new(WORK / 'application-record.json', {
            'schema': 'line-endpoint-parent-main-application/v1',
            'appliedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
            'appliedToMain': True, 'acceptedInputs': 891, 'newInputs': 892,
            'reviewReceiptSha256': opt.review_sha256, 'manifestSha256': opt.manifest_sha256,
            'writerCases': manifest['writerCases'], 'defaultCases': manifest['defaultCases'],
            'freshMainValidationPassed': False, 'sdkGoalComplete': False,
            'completeSeekPermitted': False, 'atomicConditionalWrites': False,
            'gameStarted': False, 'installed': False,
        })
    except Exception as error:
        rollback = []
        for row in reversed(changed):
            name = row['path']
            target = ROOT / name
            try:
                require(sha(target) == row['payloadSha256'], 'Concurrent user edit left untouched')
                if row['baseSha256'] is None:
                    target.unlink()
                else:
                    replace_exact(target, (WORK / 'before' / name).read_bytes(), row['payloadSha256'])
                rollback.append({'path': name, 'restored': True})
            except Exception as restore_error:
                rollback.append({'path': name, 'restored': False, 'reason': str(restore_error)})
        write_new(WORK / 'failed-application.json', {'failure': str(error), 'rollback': rollback})
        raise
    print('Applied eight reviewed files; compiler inputs 891 -> 892. Fresh main validation remains required.')


if __name__ == '__main__':
    main()
