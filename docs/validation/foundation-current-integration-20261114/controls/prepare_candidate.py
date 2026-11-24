"""Rebase the accepted descriptor/Hold candidate onto the repaired parent bytes."""
import datetime as dt
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
BASE = ROOT / 'docs/validation/gameplay-descriptor-readiness-20261114'
PARENT = Path('D:/riz-build-cache-next/line-producer-control-20261114/main-r4')
META = ROOT / 'work/producer-metadata-history-next-20261114'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value: raise RuntimeError(reason)


def main():
    manifest_path = WORK / 'candidate-manifest.json'; overlay = WORK / 'overlay'
    require(not manifest_path.exists() and not overlay.exists(), 'Preserve prepared candidates')
    require(sha(BASE / 'validation-summary.json') == '7c1d950ddbfa491ea3316de872a9fa68e2a40c03a4b1379cd55c3cb011717492', 'Accepted descriptor snapshot differs')
    candidate = read(BASE / 'run/result.json'); receipt = read(BASE / 'controls/run-r3-root-exit.json')
    require(candidate['passed'] and candidate['fullFrozenInputsUnchanged'] and candidate['suiteCount'] == 27
        and receipt['actualToolExitCode'] == 0 and receipt['actualToolSession'] == 78272
        and receipt['resultSha256'] == sha(BASE / 'run/result.json'), 'Descriptor candidate is not naturally terminal')
    metadata = read(META / 'payload-manifest.json'); application = read(META / 'application-record.json')
    require(sha(META / 'payload-manifest.json') == 'fb359759bd4a0e8d715f2777c8fe7ba9a45ab7f29a6fb7149122d9a29dfcd7e8'
        and application['applied'] and not application['freshMainPassed'] and not application['productionSourcesChanged'], 'Parent test repair differs')
    parent = read(PARENT / 'result.json'); original = {**parent['inputsBefore'], **parent['additionalSolutionInputsBefore']}
    require(parent['controllerPid'] == 44740 and len(original) == parent['completeSolutionInputs'] == 1160
        and parent['inputsBefore'] == metadata['expectedInputs']
        and parent['additionalSolutionInputsBefore'] == metadata['additionalSolutionExpectedInputs'], 'Exact repaired parent differs')
    require(all(sha(ROOT / name) == sha(PARENT / 'source' / name) == digest for name, digest in original.items()), 'Current or frozen parent source differs')
    expected = dict(candidate['fullFrozenInputs'])
    fixture = 'tests/RizToolbox.Judgements.Tests/LineRingSource/FakeNative.LineProducerControl.cs'
    # This is the one deliberate rebase conflict. Keep the parent's new bounded
    # metadata history; never restore the older fixture from the Hold snapshot.
    expected[fixture] = original[fixture]
    require(len(expected) == 1171 and set(original) <= set(expected)
        and {name for name in expected if expected[name] != candidate['fullFrozenInputs'][name]} == {fixture}, 'Rebase escaped the one test fixture')
    changed = sorted(name for name, digest in expected.items() if original.get(name) != digest)
    require(len(changed) == 20 and all(name.endswith('.cs') for name in changed)
        and len(set(expected) - set(original)) == 11, 'Expected twenty current C# payload files')
    files = []
    for name in changed:
        origin = BASE / 'source' / name; target = overlay / name
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(origin, target)
        require(sha(target) == expected[name], 'Current source overlay differs')
        files.append(dict(target=name, sha256=expected[name], baseSha256=original.get(name)))
    script = 'scripts/Build.ps1'; body = (ROOT / script).read_text(encoding='utf-8-sig')
    script_base = sha(ROOT / script)
    require(parent['controlsBefore'][str(ROOT / script)] == script_base, 'Parent managed matrix changed')
    anchor = "                for ($caseIndex = 1; $caseIndex -le 300; $caseIndex++) {\n                    & dotnet $judgementsAssembly '--legacy-case' $caseIndex"
    require(body.count(anchor) == 1 and '--hold-discovery-guard-case' not in body, 'Build matrix anchor differs')
    insert = '                # Exact current Hold guards; engine terminal/Idle admission remains separate.\n'
    for selector, count, label in (('--hold-native-owner-case', 15, 'native owner'), ('--hold-source-guard-case', 10, 'source guard'),
        ('--hold-alive-guard-case', 6, 'alive guard'), ('--hold-discovery-guard-case', 14, 'discovery guard')):
        insert += f'''                for ($caseIndex = 1; $caseIndex -le {count}; $caseIndex++) {{
                    & dotnet $judgementsAssembly '{selector}' $caseIndex
                    if ($LASTEXITCODE -ne 0) {{ throw "Hold {label} case $caseIndex failed with exit code $LASTEXITCODE." }}
                }}
'''
    script_path = overlay / script; script_path.parent.mkdir(parents=True, exist_ok=True)
    script_path.write_text(body.replace(anchor, insert + anchor), encoding='utf-8', newline='\n')
    files.append(dict(target=script, sha256=sha(script_path), baseSha256=script_base))
    scoped_names = set(parent['inputsBefore']) | (set(expected) - set(original))
    scoped = {name:expected[name] for name in sorted(scoped_names)}
    extra = {name:digest for name, digest in expected.items() if name not in scoped}
    require(len(scoped) == 939 and len(extra) == 232, 'Current complete solution closure differs')
    require(sha(ROOT / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364', 'Preserve README')
    manifest = dict(schema='current-foundation-rebased-candidate/v1', preparedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
        descriptorSummarySha256=sha(BASE / 'validation-summary.json'), descriptorResultSha256=sha(BASE / 'run/result.json'),
        descriptorPublication=dict(actualToolSession=80909, actualToolChunk='3cfc66', actualToolExitCode=0),
        descriptorArchiveOnlyCheck=dict(actualToolSession=8614, actualToolChunk='74fd71', actualToolExitCode=0),
        parentMetadataPayloadSha256=sha(META / 'payload-manifest.json'), parentControllerPid=44740,
        parentAccepted=False, parentTerminalRequiredBeforeMainApplication=True, parentFullInputs=original,
        expectedInputs=expected, scopedInputs=scoped, additionalSolutionInputs=extra,
        files=files, rebasePreservedTestFixture=fixture, scriptBaseSha256=script_base,
        sourceChanges=20, buildScriptChanges=1, completeSolutionInputs=1171,
        mainApplied=False, freshMainPassed=False, fullSdkMatrixRerun=False, sdkGoalComplete=False,
        completeSeekPermitted=False, holdIdleKnown=False, productionStartupAdmissionKnown=False,
        installed=False, gameStarted=False)
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(prepared=True, mainApplied=False, payloadFiles=len(files), completeInputs=1171,
        manifestSha256=sha(manifest_path))))


if __name__ == '__main__': main()
