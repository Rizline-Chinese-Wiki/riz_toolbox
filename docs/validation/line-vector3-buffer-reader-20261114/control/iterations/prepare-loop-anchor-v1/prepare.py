"""Pin the actual passing reader r2 overlay against accepted main904."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CANDIDATE = ROOT / 'work/line-vector3-buffer-reader-next-20261114'
BASE = ROOT / 'docs/validation/line-vector3-buffer-runtime-20261114'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def main():
    assert not (WORK / 'payload-manifest.json').exists()
    assert sha(BASE / 'validation-summary.json') == 'ca67b7f90cb9ca1d3e67803d04c2fb2c250de8c1ad27dd9b918272b1466231f5'
    base = read(BASE / 'run/result.json')['inputsAfter']
    assert len(base) == 904 and all(sha(ROOT / n) == v for n, v in base.items())
    run = CANDIDATE / 'reader-r2'
    assert sha(run / 'result.json') == 'b7d8c6e0375c9857aa0cdc25301d33c3d7ff6473587248480e0c63444f3a9f1d'
    state = read(run / 'result.json')
    assert state['passed'] and all(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps'])
    snapshots = read(run / 'inputs-before.json')
    assert snapshots == read(run / 'inputs-after.json')
    assert all(sha(run / 'shadow' / n) == v for n, v in snapshots.items())
    files = []
    for entry in read(CANDIDATE / 'candidate-r2.json')['files']:
        name = entry['target']
        assert sha(CANDIDATE / name) == entry['sha256'] == snapshots[name], 'Candidate/shadow mismatch: ' + name
        assert base.get(name) == entry['observedMainSha256'], 'Candidate main base mismatch: ' + name
        files.append(dict(target=name, candidate=(CANDIDATE / name).relative_to(ROOT).as_posix(),
                          baseSha256=base.get(name), sha256=entry['sha256']))
    script = 'scripts/Build.ps1'
    text = (ROOT / script).read_text(encoding='utf-8-sig')
    marker = '                for ($caseIndex = 1; $caseIndex -le 300; $caseIndex++) {'
    assert text.count(marker) == 1
    text = text.replace(marker,
        "                # Original retained scratch reader/owner; external consumer effects remain open.\n"
        "                & dotnet $judgementsAssembly '--line-ring-buffer-checks'\n"
        "                if ($LASTEXITCODE -ne 0) { throw \"Judgements original line buffer checks failed with exit code $LASTEXITCODE.\" }\n" + marker)
    target = WORK / script
    target.parent.mkdir(parents=True, exist_ok=True)
    assert not target.exists()
    target.write_text(text, encoding='utf-8', newline='\n')
    files.append(dict(target=script, candidate=target.relative_to(ROOT).as_posix(),
                      baseSha256=sha(ROOT / script), sha256=sha(target), compilerInput=False))
    expected = dict(base)
    for row in files:
        if row.get('compilerInput', True):
            expected[row['target']] = row['sha256']
    assert len(files) == 11 and len(expected) == 909
    manifest = dict(schema='line-buffer-reader-main-payload/v1', baseInputs=base,
                    baseSummarySha256=sha(BASE / 'validation-summary.json'), expectedInputs=expected,
                    shadowResultSha256=sha(run / 'result.json'), files=files, readerCases=7,
                    endpointWriterCases=36, defaultRuntimeCases=366, mainApplied=False,
                    completeSeekPermitted=False, sdkGoalComplete=False)
    (WORK / 'payload-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(payloadSha256=sha(WORK / 'payload-manifest.json'), expectedCompilerInputs=909, payloadFiles=11)))


if __name__ == '__main__':
    main()
