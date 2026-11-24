"""Apply only a root-reviewed four-file payload to the exact accepted 892 graph."""
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


def load_prepare():
    spec = importlib.util.spec_from_file_location('world_integration_prepare_apply', WORK / 'prepare.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def replace_exact(target, data, expected):
    require(target.resolve().is_relative_to(ROOT.resolve())
            and not any(path.is_symlink() for path in (target, *target.parents) if path.exists()),
            'Target escaped main or traversed a symlink')
    require(target.is_file() and sha(target) == expected, 'Concurrent target change')
    with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
        staged = Path(stream.name)
        stream.write(data)
    try:
        require(sha(target) == expected, 'Concurrent target change before replacement')
        os.replace(staged, target)
    finally:
        staged.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--review-sha256', required=True)
    parser.add_argument('--manifest-sha256', required=True)
    opt = parser.parse_args()
    require(not (WORK / 'application-record.json').exists()
            and not (WORK / 'failed-application.json').exists(),
            'Preserve earlier application history')
    require(sha(WORK / 'payload-manifest.json') == opt.manifest_sha256,
            'Payload manifest differs from explicit reviewed hash')
    manifest = read(WORK / 'payload-manifest.json')
    prepare = load_prepare()
    require(manifest['schema'] == 'line-ring-world-main-payload/v1'
            and manifest['acceptedSummarySha256'] == prepare.ACCEPTED_SUMMARY_SHA
            and manifest['initialCandidateManifestSha256'] == prepare.INITIAL_MANIFEST_SHA
            and manifest['inputScannerSha256'] == prepare.SCANNER_SHA
            and manifest['buildScriptSha256'] == prepare.BUILD_SHA
            and manifest['acceptedInputs'] == manifest['expectedInputs'] == 892
            and manifest['oldWriterCases'] == 31
            and manifest['legacyCasesListed'] == 300,
            'Staged payload scope differs')
    require(sha(WORK / 'review-receipt.json') == opt.review_sha256 == manifest['reviewReceiptSha256'],
            'Root receipt changed or its hash was not supplied')
    before, hashes, snapshot, names = prepare.inventory()
    require(before == read(WORK / 'inputs-before.json')
            and hashes == manifest['candidateFilesSha256']
            and snapshot == manifest['candidateSnapshotSha256']
            and names == manifest['writerCaseNames']
            and manifest['writerCases'] == len(names)
            and manifest['defaultCases'] == 403 - 31 + len(names),
            'Accepted base or final candidate changed after staging')
    shadow = prepare.verify_review((WORK / 'review-receipt.json').read_bytes(),
                                   hashes, snapshot, names, before)
    require(shadow == manifest['shadowResult'], 'Final shadow evidence changed after staging')
    rows = manifest['files']
    require(len(rows) == 4 and {row['candidatePath']: row['path'] for row in rows} == prepare.TARGET_MAP
            and len({row['path'] for row in rows}) == 4,
            'Target set differs from four-file whitelist')
    after = dict(before)
    for row in rows:
        name = row['path']
        source = prepare.CANDIDATE / row['candidatePath']
        target = ROOT / name
        payload = WORK / 'files' / name
        backup = WORK / 'before' / name
        require(row['baseSha256'] == before[name]
                and row['payloadSha256'] == hashes[row['candidatePath']]
                and sha(target) == row['baseSha256']
                and sha(payload) == row['payloadSha256']
                and sha(backup) == row['baseSha256']
                and payload.read_bytes() == source.read_bytes(),
                'Exact candidate, staged payload or rollback bytes differ: ' + name)
        after[name] = row['payloadSha256']
    require(len(after) == 892 and set(after) == set(before),
            'Four replacements must preserve the compiler input set')
    scanner = prepare.input_scanner()
    require(scanner.inputs() == before and sha(ROOT / 'scripts/Build.ps1') == prepare.BUILD_SHA,
            'Accepted main graph or build dispatch changed')
    changed = []
    try:
        expected = dict(before)
        for row in rows:
            require(scanner.inputs() == expected, 'Concurrent compiler-input change')
            replace_exact(ROOT / row['path'], (WORK / 'files' / row['path']).read_bytes(),
                          row['baseSha256'])
            changed.append(row)
            expected[row['path']] = row['payloadSha256']
        require(scanner.inputs() == after and sha(ROOT / 'scripts/Build.ps1') == prepare.BUILD_SHA,
                'Written graph differs from reviewed four-file payload')
        write_new(WORK / 'application-record.json', {
            'schema': 'line-ring-world-main-application/v1',
            'appliedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
            'appliedToMain': True, 'acceptedInputs': 892, 'newInputs': 892,
            'reviewReceiptSha256': opt.review_sha256,
            'manifestSha256': opt.manifest_sha256,
            'shadowResultSha256': shadow['sha256'],
            'writerCases': len(names), 'defaultCases': manifest['defaultCases'],
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
                require(target.is_file() and sha(target) == row['payloadSha256'],
                        'Concurrent user edit left untouched')
                replace_exact(target, (WORK / 'before' / name).read_bytes(),
                              row['payloadSha256'])
                rollback.append({'path': name, 'restored': True})
            except Exception as restore_error:
                rollback.append({'path': name, 'restored': False, 'reason': str(restore_error)})
        write_new(WORK / 'failed-application.json',
                  {'failure': str(error), 'rollback': rollback,
                   'compilerInputsAfterRollback': scanner.inputs()})
        raise
    print('Applied four reviewed candidate files; compiler inputs remain 892. Fresh main validation is required.')


if __name__ == '__main__':
    main()
