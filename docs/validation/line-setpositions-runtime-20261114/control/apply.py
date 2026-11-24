"""Apply the exact nine-case SetPositions overlay; fresh main validation remains separate."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
PAYLOAD_SHA = '8ceb7283aef196c82760b846b09db4e4f079bb24f214dc04fa2d604a53f25e82'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def main():
    assert not (WORK / 'application-record.json').exists()
    assert sha(WORK / 'payload-manifest-v4.json') == PAYLOAD_SHA
    manifest = read(WORK / 'payload-manifest-v4.json')
    assert len(manifest['baseInputs']) == 909 and len(manifest['expectedInputs']) == 913
    assert all(sha(ROOT / n) == v for n, v in manifest['baseInputs'].items())
    assert sha(ROOT / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
    witness = read(WORK / 'shadow-root-exit.json')
    assert witness['actualExitCode'] == 0 and witness['actualRootSession'] == 68569
    assert witness['payloadManifestSha256'] == PAYLOAD_SHA
    shadow = Path('D:/riz-build-cache-next/line-setpositions-profile-20261114/root-r3')
    assert sha(shadow / 'result.json') == witness['shadowResultSha256'] == manifest['shadowResultSha256']
    assert sha(WORK / 'root-review-v4.md') == witness['rootReviewSha256']
    for row in manifest['files']:
        target = ROOT / row['target']
        assert target.resolve().is_relative_to(ROOT.resolve())
        assert sha(ROOT / row['candidate']) == row['sha256']
        if row['baseSha256'] is None:
            assert not target.exists()
        else:
            assert sha(target) == row['baseSha256']
    backup = WORK / 'main-before'
    backup.mkdir(exist_ok=False)
    for row in manifest['files']:
        if row['baseSha256'] is not None:
            path = backup / row['target']
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / row['target'], path)
    applied = []
    try:
        for row in manifest['files']:
            target = ROOT / row['target']
            pending = target.with_name(target.name + '.setpositions-pending')
            assert not pending.exists()
            shutil.copy2(ROOT / row['candidate'], pending)
            assert sha(pending) == row['sha256']
            os.replace(pending, target)
            applied.append(row)
        assert all(sha(ROOT / n) == v for n, v in manifest['expectedInputs'].items())
    except Exception:
        for row in reversed(applied):
            target = ROOT / row['target']
            if row['baseSha256'] is None:
                target.unlink()
            else:
                shutil.copy2(backup / row['target'], target)
        raise
    record = dict(schema='line-setpositions-main-application/v1',
                  appliedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                  payloadManifestSha256=PAYLOAD_SHA, files=manifest['files'],
                  shadowResultSha256=manifest['shadowResultSha256'],
                  compilerInputsBefore=909, compilerInputsAfter=913,
                  freshMainPassed=False, installed=False, gameStarted=False,
                  completeSeekPermitted=False, sdkGoalComplete=False)
    (WORK / 'application-record.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(appliedFiles=len(applied), currentCompilerInputs=913, freshMainPassed=False)))


if __name__ == '__main__':
    main()
