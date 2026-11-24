"""Apply an exact reviewed scene payload to the accepted 892-input graph."""
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
    spec = importlib.util.spec_from_file_location('scene_integration_prepare_apply', WORK / 'prepare.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def replace_exact(target, data, expected):
    require(target.resolve().is_relative_to(ROOT.resolve())
            and not any(path.is_symlink() for path in (target, *target.parents) if path.exists()),
            'Target escaped main or traversed a symlink')
    require(target.parent.is_dir(), 'Target parent is absent')
    require((not target.exists() if expected is None else target.is_file() and sha(target) == expected),
            'Concurrent target change')
    with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
        staged = Path(stream.name)
        stream.write(data)
    try:
        if expected is None:
            require(not target.exists(), 'Concurrent new target')
            os.link(staged, target)  # Atomic create-if-absent, never overwrite a concurrent file.
        else:
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
    require(manifest['schema'] == 'line-ring-scene-main-payload/v1'
            and manifest['acceptedSummarySha256'] == prepare.ACCEPTED_SUMMARY_SHA
            and manifest['initialCandidateManifestSha256'] == prepare.INITIAL_MANIFEST_SHA
            and manifest['inputScannerSha256'] == prepare.SCANNER_SHA
            and manifest['buildScriptSha256'] == prepare.BUILD_SHA
            and manifest['acceptedInputs'] == 892 and manifest['expectedInputs'] == 895
            and manifest['writerCases'] == 36 and manifest['oldSourceCases'] == 19
            and manifest['sourceCases'] == 24 and manifest['defaultCases'] == 413
            and manifest['legacyCasesListed'] == 300,
            'Staged payload scope differs')
    require(sha(WORK / 'review-receipt.json') == opt.review_sha256 == manifest['reviewReceiptSha256'],
            'Root receipt changed or its hash was not supplied')
    before, hashes, snapshot, names = prepare.inventory()
    require(before == read(WORK / 'inputs-before.json')
            and hashes == manifest['candidateFilesSha256']
            and snapshot == manifest['candidateSnapshotSha256']
            and names == manifest['sourceCaseNames'],
            'Accepted base or final candidate changed after staging')
    shadow, handle = prepare.verify_review((WORK / 'review-receipt.json').read_bytes(),
                                            hashes, snapshot, names, before)
    require(shadow == manifest['shadowResult'] and handle == manifest['shadowHandle'],
            'Final shadow evidence changed after staging')
    rows = manifest['files']
    require(len(rows) == 11 and len({row['path'] for row in rows}) == 11
            and {row['candidatePath']: row['path'] for row in rows if row['candidatePath'] is not None}
                == prepare.TARGET_MAP
            and rows[-1]['candidatePath'] is None and rows[-1]['path'] == prepare.BUILD,
            'Target set differs from ten C# files and Build.ps1')
    expected = dict(before)
    for row in rows:
        name = row['path']
        target = ROOT / name
        payload = WORK / 'files' / name
        backup = WORK / 'before' / name
        payload_hash = (hashlib.sha256(prepare.build_payload()).hexdigest() if name == prepare.BUILD
                        else hashes[row['candidatePath']])
        require(row['baseSha256'] == (prepare.BUILD_SHA if name == prepare.BUILD else before.get(name))
                and row['payloadSha256'] == payload_hash
                and sha(payload) == payload_hash
                and ((target.is_file() and sha(target) == row['baseSha256']
                      and sha(backup) == row['baseSha256']) if row['baseSha256'] is not None
                     else not target.exists() and not backup.exists()),
                'Exact candidate, staged payload or rollback bytes differ: ' + name)
        if name != prepare.BUILD:
            require(payload.read_bytes() == (prepare.CANDIDATE / row['candidatePath']).read_bytes(),
                    'Staged C# bytes differ: ' + name)
            expected[name] = payload_hash
    require(len(expected) == 895 and set(expected) == set(before) | set(prepare.TARGET_MAP.values()),
            'Ten C# changes must add exactly three compiler inputs')
    scanner = prepare.input_scanner()
    require(scanner.inputs() == before and sha(ROOT / prepare.BUILD) == prepare.BUILD_SHA,
            'Accepted main graph or build dispatch changed')
    changed = []
    try:
        incremental = dict(before)
        for row in rows:
            require(scanner.inputs() == incremental, 'Concurrent compiler-input change')
            name = row['path']
            replace_exact(ROOT / name, (WORK / 'files' / name).read_bytes(), row['baseSha256'])
            changed.append(row)
            if name != prepare.BUILD:
                incremental[name] = row['payloadSha256']
        require(scanner.inputs() == expected
                and sha(ROOT / prepare.BUILD) == rows[-1]['payloadSha256'],
                'Written graph differs from reviewed eleven-file payload')
        write_new(WORK / 'application-record.json', {
            'schema': 'line-ring-scene-main-application/v1',
            'appliedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
            'appliedToMain': True, 'acceptedInputs': 892, 'newInputs': 895,
            'reviewReceiptSha256': opt.review_sha256,
            'manifestSha256': opt.manifest_sha256,
            'shadowResultSha256': shadow['sha256'],
            'shadowHandleSha256': handle['sha256'],
            'writerCases': 36, 'sourceCases': 24, 'defaultCases': 413,
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
                if row['baseSha256'] is None:
                    target.unlink()
                else:
                    replace_exact(target, (WORK / 'before' / name).read_bytes(), row['payloadSha256'])
                rollback.append({'path': name, 'restored': True})
            except Exception as restore_error:
                rollback.append({'path': name, 'restored': False, 'reason': str(restore_error)})
        write_new(WORK / 'failed-application.json',
                  {'failure': str(error), 'rollback': rollback,
                   'compilerInputsAfterRollback': scanner.inputs(),
                   'buildScriptSha256': sha(ROOT / prepare.BUILD)})
        raise
    print('Applied ten reviewed C# files and one Build.ps1 range edit; compiler inputs are 895. Fresh main validation is required.')


if __name__ == '__main__':
    main()
