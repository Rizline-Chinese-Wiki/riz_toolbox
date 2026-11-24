"""Apply the reviewed stable-host and exact-session fix to the 778-source base."""
import hashlib
import json
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
STAGE = CACHE / 'playback-stock-formal-handoff-stage-r1-20261031'
SNAPSHOT = STAGE / 'snapshot-r2'
REVIEW = CACHE / 'playback-stock-formal-host-review-r1-20261031'
OUT = CACHE / 'playback-stock-formal-main-integration-r1-20261101'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return dict(path=str(path), sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def run():
    assert not OUT.exists()
    proof = read(SNAPSHOT / 'test-result.json')
    assert fact(SNAPSHOT / 'test-result.json')['sha256'] == '0ea83dbd8c06e813979724aea9c999cdbbf2500058c55d1cac085c2fd052567f'
    assert proof['passed'] and proof['baselineFiles'] == 778 and proof['sourceFiles'] == 785 and proof['changedFiles'] == 12
    assert proof['actualAppliedBytesMatch'] and all(row['actualExitCode'] == 0 for row in proof['processes'])
    assert proof['sourceArchive']['sha256'] == '299d9cb8286b5f651dc4a79e536473a3b101a16b0c20d8bda2c84bf0f2183c3e'
    for key in ('sourceArchive', 'beforeArchive', 'patch', 'manifest'):
        assert fact(proof[key]['path'])['sha256'] == proof[key]['sha256']
    sources = read(proof['manifest']['path'])
    assert len(sources) == 785
    review_sources = read(REVIEW / 'source-r2-freeze.json')
    assert {r['relativePath']: r['sha256'] for r in review_sources['rows']} == {r['path']: r['sha256'] for r in sources}
    fixed_review = read(REVIEW / 'root-fixed-execution-r1/test-result.json')
    assert fixed_review['passed'] and fixed_review['sourceAndInputsUnchanged']
    assert fixed_review['processes'] == 2 and fixed_review['checks'] == 46
    assert all(row['actualExitCode'] == 0 for row in fixed_review['rows'])
    normal = read(STAGE / 'normal-positive-r3/test-result.json')
    withdrawn = read(STAGE / 'exact-withdrawn-handoff-checks-r2/test-result.json')
    for result in (normal, withdrawn):
        assert result['passed'] and result['processes'] == 1 and result['rows'][0]['actualExitCode'] == 0
        assert result['rows'][0]['result']['actualRuns'] == 2
        for item in result['sources'] + result['inputs'] + result['managedArtifacts'] + [result['runner']]:
            assert fact(item['path'])['sha256'] == item['sha256']
    with zipfile.ZipFile(proof['beforeArchive']['path']) as archive:
        assert len(archive.namelist()) == 778
        for entry in archive.infolist():
            assert (REPO / entry.filename).read_bytes() == archive.read(entry), entry.filename
    with zipfile.ZipFile(proof['sourceArchive']['path']) as archive:
        assert len(archive.namelist()) == 785
        for item in sources:
            assert hashlib.sha256(archive.read(item['path'])).hexdigest() == item['sha256']
    for change in proof['changes']:
        path = (REPO / change['path']).resolve()
        assert path.is_relative_to(REPO)
        if change['beforeSha256'] is None:
            assert not path.exists()
        else:
            assert fact(path)['sha256'] == change['beforeSha256']
    OUT.mkdir()
    write(OUT / 'before-input-verification.json', dict(passed=True, baseline=proof['beforeArchive'],
          source=proof['sourceArchive'], exactIndependentReviewSources=785,
          fixedReview=fact(REVIEW / 'root-fixed-execution-r1/test-result.json'),
          normal=fact(STAGE / 'normal-positive-r3/test-result.json'),
          exactWithdrawn=fact(STAGE / 'exact-withdrawn-handoff-checks-r2/test-result.json')))
    for mode, argv in (('check', ['git', 'apply', '--check', '--whitespace=nowarn', proof['patch']['path']]),
                       ('apply', ['git', 'apply', '--whitespace=nowarn', proof['patch']['path']])):
        write(OUT / ('main-' + mode + '.command.json'), dict(argv=argv, cwd=str(REPO)))
        result = subprocess.run(argv, cwd=REPO, capture_output=True)
        (OUT / ('main-' + mode + '.stdout.log')).write_bytes(result.stdout)
        (OUT / ('main-' + mode + '.stderr.log')).write_bytes(result.stderr)
        (OUT / ('main-' + mode + '.exit.txt')).write_text(str(result.returncode), encoding='utf-8')
        assert result.returncode == 0, result.stderr.decode(errors='replace')
    for item in sources:
        assert fact(REPO / item['path'])['sha256'] == item['sha256']
    handoff = REPO / 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.Handoff.cs'
    assert fact(handoff)['sha256'] == '9c515c3bfce25f1f191227f1d4dd4f8a58927c5678f7c208eb41bbd4cd90f51b'
    result = dict(kind='formal-stable-provider-and-exact-session-main-integration', passed=True,
          observedAtUtc=datetime.now(timezone.utc).isoformat(), sourceFilesCompared=785, changedFiles=12,
          snapshot=fact(SNAPSHOT / 'test-result.json'), sourceArchive=proof['sourceArchive'],
          beforeArchive=proof['beforeArchive'], patch=proof['patch'], sourceManifest=proof['manifest'],
          actualGitApplyCheck=0, actualGitApply=0, normalTwoRunChecks=normal['checks'],
          exactWithdrawnTwoRunChecks=withdrawn['checks'], independentFixedProcesses=2, independentFixedChecks=46,
          newMainFullMatrixAccepted=False, stageRemainingModesPending=True,
          completeSourceGeneration=False, completeLowerWriterCoverage=False,
          arbitrarySeek=False, fullB01B19Completed=False, productionAdmission=False, gameExecuted=False, installed=False)
    write(OUT / 'test-result.json', result)
    print(json.dumps(dict(passed=True, sourceFiles=785, changedFiles=12, result=fact(OUT / 'test-result.json'))))


if __name__ == '__main__':
    run()
