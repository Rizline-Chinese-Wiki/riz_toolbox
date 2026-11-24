"""Apply only the four hash-pinned, shadow-tested files; no install or package mutation."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
MANIFEST_SHA = '2396de28eb6dc0525110d0c2aeec0e816703b6ce4e4c0a276402d75945f021d7'
SHADOW = Path('D:/riz-build-cache-next/line-vector3-buffer-20261114/shadow-r3')
SHADOW_RESULT = 'a5b53640be0dd3cdff565fe8caf75528203a65c18c9d9013ac330efca59b1124'
README = 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def main():
    assert not (WORK / 'application-record.json').exists()
    assert sha(WORK / 'payload-manifest.json') == MANIFEST_SHA
    manifest = read(WORK / 'payload-manifest.json')
    base, files = manifest['baseInputs'], manifest['files']
    assert len(base) == 901 and len(files) == 4
    assert sha(ROOT / 'README.md') == README
    assert all(sha(ROOT / name) == digest for name, digest in base.items())
    assert sha(SHADOW / 'result.json') == SHADOW_RESULT
    result = read(SHADOW / 'result.json')
    assert result['passed'] and result['payloadManifestSha256'] == MANIFEST_SHA
    assert len(result['inputs']) == 904 and len(result['steps']) == 6
    assert all(row['naturalExit'] and row['exitCode'] == 0 for row in result['steps'])
    assert all(sha(SHADOW / path) == digest for path, digest in result['binaries'].items())
    assert all(sha(SHADOW / 'source' / name) == digest for name, digest in result['inputs'].items())
    for row in files:
        target = ROOT / row['target']
        assert sha(ROOT / row['candidate']) == row['sha256']
        assert row['baseSha256'] == base.get(row['target'])
        assert (not target.exists()) if row['baseSha256'] is None else sha(target) == row['baseSha256']
    backup = WORK / 'main-before'
    backup.mkdir(exist_ok=False)
    for row in files:
        if row['baseSha256'] is not None:
            target = backup / row['target']
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / row['target'], target)
    applied = []
    try:
        for row in files:
            target = ROOT / row['target']
            pending = target.with_name(target.name + '.buffer-pending')
            assert not pending.exists()
            shutil.copy2(ROOT / row['candidate'], pending)
            assert sha(pending) == row['sha256']
            os.replace(pending, target)
            applied.append(row)
        assert all(sha(ROOT / name) == digest for name, digest in result['inputs'].items())
    except Exception:
        for row in reversed(applied):
            target = ROOT / row['target']
            if row['baseSha256'] is None:
                # Only this explicitly applied new single file is rolled back.
                target.unlink()
            else:
                shutil.copy2(backup / row['target'], target)
        raise
    record = dict(schema='line-vector3-buffer-main-application/v1',
                  appliedAtUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                  payloadManifestSha256=MANIFEST_SHA, shadowResultSha256=SHADOW_RESULT,
                  actualRootShadowSession=3154, actualRootShadowExit=0, rootToolChunk='4d63b0',
                  files=files, beforeInputs=901, afterInputs=904, freshMainPassed=False,
                  installed=False, gameStarted=False, sdkGoalComplete=False,
                  completeSeekPermitted=False, readmeSha256=sha(ROOT / 'README.md'))
    (WORK / 'application-record.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(appliedFiles=len(files), currentInputs=904, freshMainPassed=False)))


if __name__ == '__main__':
    main()
