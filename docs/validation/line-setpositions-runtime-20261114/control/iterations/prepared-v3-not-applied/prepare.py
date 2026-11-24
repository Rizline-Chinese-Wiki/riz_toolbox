"""Pin the shadow-tested exact SetPositions payload against accepted main909."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
CANDIDATE = ROOT / 'work/line-setpositions-profile-next-20261114'
BASE = ROOT / 'docs/validation/line-vector3-buffer-reader-20261114'
SHADOW = Path('D:/riz-build-cache-next/line-setpositions-profile-20261114/root-r2')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def main():
    assert not (WORK / 'payload-manifest.json').exists()
    assert sha(BASE / 'validation-summary.json') == '8df53d0819339010c4d49b2dacb54b38a386ad1a5f4ed86e35e2599be45eb469'
    base = read(BASE / 'run/result.json')['inputsAfter']
    assert len(base) == 909 and all(sha(ROOT / n) == digest for n, digest in base.items())
    manifest = CANDIDATE / 'candidate-root-v3.json'
    assert sha(manifest) == 'c33320d763d346311f424d5e0de2bfa22cabbf41d65566a9072332a23752f41a'
    candidate = read(manifest)
    assert len(candidate['files']) == 5 and candidate['expectedCases'] == 8
    assert sha(SHADOW / 'result.json') == '4d53d7e0401f924c0538fa4f1347f939646b8534808ea3568e52f18ae80047e4'
    shadow = read(SHADOW / 'result.json')
    assert shadow['passed'] and all(row['naturalExit'] and row['exitCode'] == 0 for row in shadow['steps'])
    assert all(sha(SHADOW / n) == digest for n, digest in shadow['binaries'].items())
    inputs = read(SHADOW / 'inputs-before.json')
    assert all(sha(SHADOW / 'source' / n) == digest for n, digest in inputs.items())
    for name, digest in base.items():
        if name.startswith('src/') or name.startswith('tests/RizToolbox.Runtime.Tests/') or name in ('Directory.Build.props', 'global.json'):
            if name not in candidate['files']:
                assert inputs[name] == digest, 'Shadow base differs: ' + name
    files, expected = [], dict(base)
    for name, digest in candidate['files'].items():
        assert sha(CANDIDATE / name) == digest == inputs[name]
        files.append(dict(target=name, candidate=(CANDIDATE / name).relative_to(ROOT).as_posix(),
                          baseSha256=base.get(name), sha256=digest))
        expected[name] = digest
    assert len(expected) == 913
    # Keep the existing test entry points; correct a stale source-range comment
    # while this script is already included in the reviewable main payload.
    script = 'scripts/Build.ps1'
    text = (ROOT / script).read_text(encoding='utf-8-sig')
    old = '# The fixed source range is 19 cases; the Canvas basis range remains 16.'
    assert text.count(old) == 1
    target = WORK / script
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text.replace(old, '# The fixed source range is 30 cases; the Canvas basis range remains 16.'), encoding='utf-8', newline='\n')
    files.append(dict(target=script, candidate=target.relative_to(ROOT).as_posix(),
                      baseSha256=sha(ROOT / script), sha256=sha(target), compilerInput=False))
    result = dict(schema='line-setpositions-main-payload/v1', baseInputs=base, expectedInputs=expected,
                  baseSummarySha256=sha(BASE / 'validation-summary.json'), files=files,
                  candidateManifestSha256=sha(manifest), shadowResultSha256=sha(SHADOW / 'result.json'),
                  setPositionsCases=8, defaultRuntimeCases=374, originalReaderCases=7,
                  mainApplied=False, completeSeekPermitted=False, sdkGoalComplete=False)
    (WORK / 'payload-manifest.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(payloadSha256=sha(WORK / 'payload-manifest.json'), expectedCompilerInputs=913, payloadFiles=6)))


if __name__ == '__main__':
    main()
