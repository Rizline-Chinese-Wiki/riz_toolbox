"""Close the actual U functional scope and carry back only its tested disposal fix."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil
import zipfile

P = Path(__file__).resolve().parent
U = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102')
O = U / 'final-sdk-disposal-integration-r1'
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
LIFE = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
OLD_HASH = 'f8fea816545050b81a328fdae0b3ed7dd4e1788a5e12465fd8dd69b038c78348'
NEW_HASH = '0abcb623cebb5f6bef5e03e69efb37ccd214b3f70a877e6bdf3ed37ec71d8904'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def facts(value):
    if isinstance(value, dict):
        if isinstance(value.get('path'), str) and Path(value['path']).is_absolute() and isinstance(value.get('sha256'), str):
            yield value
        for v in value.values():
            yield from facts(v)
    elif isinstance(value, list):
        for v in value:
            yield from facts(v)


def verify(row):
    p = Path(row['path'])
    assert sha(p) == row['sha256'] and ('bytes' not in row or p.stat().st_size == row['bytes']), str(p)


def check(root, rows):
    for row in rows:
        assert sha(root / row['path']) == row['sha256'], str(root / row['path'])


def archive(path, root, rows):
    with zipfile.ZipFile(path, 'x', compression=zipfile.ZIP_DEFLATED) as z:
        for row in rows:
            z.write(root / row['path'], row['path'])
    with zipfile.ZipFile(path) as z:
        assert len(z.namelist()) == 913
        for row in rows:
            assert hashlib.sha256(z.read(row['path'])).hexdigest() == row['sha256']
    return fact(path)


def main():
    assert not O.exists(), 'Preserve completed acceptance boundaries.'
    closed = read(P / 'closed-U-input-processes-r1.json')
    assert closed['matchingCount'] == 0
    original = read(U / 'functional-matrix-r2/test-result.json')
    retry = read(U / 'sdk-current-dispose-r1/test-result.json')
    combined = read(U / 'functional-combined-after-disposal-r1/test-result.json')
    review = read(U / 'sdk-disposal-collector-review-r1/final-closure-review.json')
    observer = read(U / 'sdk-disposal-observer-r1/observer-result.json')
    watcher = read(U / 'sdk-disposal-collector-review-r1/review-watcher-result.json')
    assert original['passed'] is False
    assert retry['passed'] and retry['checks'] == 38 and retry['finishedAtUtc']
    assert combined['passed'] and combined['checks'] == 275 and combined['processes'] == 12 and len(combined['rows']) == 12
    assert all(row['passed'] and row['actualExitCode'] == 0 and row['actual']['passed'] for row in combined['rows'])
    assert review['passed'] and review['autoCoverageClosed'] and not review['issues']
    assert observer['passed'] and observer['state'] == 'collected' and observer['collectorActualExitCode'] == 0
    assert watcher['passed'] and watcher['state'] == 'verified' and watcher['validatorActualExitCode'] == 0
    reviewed_facts = {}
    for relative in ['functional-combined-after-disposal-r1/test-result.json',
                     'sdk-disposal-collector-review-r1/final-closure-review.json',
                     'sdk-disposal-collector-review-r1/review-hash-manifest.json',
                     'sdk-disposal-observer-r1/observer-result.json']:
        for row in facts(read(U / relative)):
            reviewed_facts[(row['path'], row['sha256'])] = row
    for row in reviewed_facts.values():
        verify(row)
    before_rows = read(U / 'foundation-r7/source-manifest.json')['files']
    next_rows = read(U / 'sdk-current-dispose-r1/source-manifest.json')['files']
    assert len(before_rows) == len(next_rows) == 913
    check(U / 'source', before_rows)
    check(U / 'sdk-current-dispose-r1/source', next_rows)
    before_map, next_map = ({row['path']: row for row in rows} for rows in (before_rows, next_rows))
    assert list(p for p in before_map if before_map[p] != next_map[p]) == [LIFE]
    assert before_map[LIFE]['sha256'] == OLD_HASH and next_map[LIFE]['sha256'] == NEW_HASH
    for rel in next_map:
        if rel.startswith(('src/', 'native/')):
            assert before_map[rel] == next_map[rel]
    main_rows = read(U / 'baseline-manifest.json')['files']
    check(MAIN, main_rows)
    assert sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
    O.mkdir()
    save(O / 'before-canonical-source-manifest.json', dict(source=str(U / 'source'), files=before_rows))
    save(O / 'verified-original-facts-before-patch.json', dict(passed=True, checkedAtUtc=now(), count=len(reviewed_facts), files=list(reviewed_facts.values())))
    before_zip = archive(O / 'canonical-before-913.zip', U / 'source', before_rows)
    old_copy = O / 'retained-before' / LIFE
    old_copy.parent.mkdir(parents=True)
    shutil.copy2(U / 'source' / LIFE, old_copy)
    assert sha(old_copy) == OLD_HASH
    save(O / 'patch-journal.json', dict(kind='exact-tested-sdk-disposal-test-patch', startedAtUtc=now(), applied=False,
         target=str(U / 'source' / LIFE), beforeSha256=OLD_HASH, afterSha256=NEW_HASH, beforeRetained=fact(old_copy),
         testedSource=fact(U / 'sdk-current-dispose-r1/source' / LIFE), mainApplied=False))
    temporary = U / 'source' / (LIFE + '.riz-staged')
    assert not temporary.exists()
    shutil.copy2(U / 'sdk-current-dispose-r1/source' / LIFE, temporary)
    assert sha(temporary) == NEW_HASH
    temporary.replace(U / 'source' / LIFE)
    check(U / 'source', next_rows)
    check(MAIN, main_rows)
    after_zip = archive(O / 'canonical-after-tested-disposal-913.zip', U / 'source', next_rows)
    save(O / 'final-candidate-source-manifest.json', dict(source=str(U / 'source'), files=next_rows))
    redirect = dict(kind='historical-canonical-reference-after-verified-test-patch',
                    oldCanonicalReference=dict(path=str(U / 'source' / LIFE), sha256=OLD_HASH, bytes=before_map[LIFE]['bytes']),
                    historicalBytesRetained=fact(old_copy), currentCanonical=fact(U / 'source' / LIFE),
                    exactTestedSnapshot=fact(U / 'sdk-current-dispose-r1/source' / LIFE),
                    allowedRedirectCount=1, reason='Independent review was completed against the unmodified r7 canonical candidate before this tested one-file transition.',
                    oldReviewRewritten=False, oldFailureRewritten=False)
    save(O / 'historical-canonical-reference-redirect.json', redirect)
    for row in reviewed_facts.values():
        if Path(row['path']).resolve() == (U / 'source' / LIFE).resolve() and row['sha256'] == OLD_HASH:
            assert sha(old_copy) == row['sha256'] and old_copy.stat().st_size == row['bytes']
        else:
            verify(row)
    save(O / 'patch-journal.json', dict(kind='exact-tested-sdk-disposal-test-patch', finishedAtUtc=now(), applied=True,
         target=str(U / 'source' / LIFE), beforeSha256=OLD_HASH, afterSha256=NEW_HASH, beforeRetained=fact(old_copy),
         testedSource=fact(U / 'sdk-current-dispose-r1/source' / LIFE), all913MatchSuccessfulSdkSnapshot=True,
         managed484AndNative48Unchanged=True, main894Unchanged=True, mainApplied=False))
    retained = []
    for folder in ['functional-combined-after-disposal-r1','sdk-current-dispose-r1',
                   'sdk-disposal-observer-r1','sdk-disposal-collector-review-r1']:
        for original_path in (U / folder).rglob('*'):
            if not original_path.is_file():
                continue
            relative = original_path.relative_to(U / folder)
            if relative.parts[0] in {'source','inputs','environment','bin','obj'}:
                continue
            target = O / 'retained-final-scopes' / folder / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(original_path, target)
            assert sha(target) == sha(original_path)
            retained.append(dict(original=fact(original_path), retained=fact(target)))
    shutil.copy2(P / 'closed-U-input-processes-r1.json', O / 'closed-U-input-processes-r1.json')
    shutil.copy2(Path(__file__).resolve(), O / Path(__file__).name)
    save(O / 'retained-final-scopes.json', dict(passed=True, count=len(retained), files=retained))
    result = dict(kind='resource-functional-final-and-exact-tested-canonical-disposal-transition', passed=True,
                  finishedAtUtc=now(), functionalSubscopeComplete=True, functionalModes=12, functionalChecks=275,
                  originalSuccessfulChecks=237, newSdkChecks=38, originalFailedSdkRetained=True,
                  originalAggregateRetainedUnchanged=True, oldOriginalAggregatePassed=False,
                  independentAutoCoverageClosed=True, independentIssues=[], verifiedPriorFacts=len(reviewed_facts),
                  canonicalTestPatchApplied=True, canonicalSourceFiles=913, canonicalMatchesSuccessfulSdkSnapshot=True,
                  canonicalProductInputs=484, canonicalNativeInputs=48, productOrNativeSourceModified=False,
                  beforeArchive=before_zip, afterArchive=after_zip, historicalReferenceRedirect=fact(O / 'historical-canonical-reference-redirect.json'),
                  sourceManifest=fact(O / 'final-candidate-source-manifest.json'), patchJournal=fact(O / 'patch-journal.json'),
                  functionalScope=fact(U / 'functional-combined-after-disposal-r1/test-result.json'),
                  independentReview=fact(U / 'sdk-disposal-collector-review-r1/final-closure-review.json'),
                  retainedFinalScopes=fact(O / 'retained-final-scopes.json'), sourceDifferenceFromR7=[LIFE],
                  newTestsStartedByThisSeal=False, mainApplied=False, formalStockStopSdkComplete=False,
                  fullIncrementAcceptanceComplete=False, gameExecuted=False, installed=False,
                  productionAdmission=False, fullBCompleted=False)
    save(O / 'test-result.json', result)
    manifest = [fact(path) for path in sorted(O.rglob('*')) if path.is_file()]
    save(O / 'hash-manifest.json', dict(passed=True, files=manifest))
    print(json.dumps(dict(passed=True, functionalModes=12, functionalChecks=275,
                         candidateTestPatchApplied=True, canonicalSourceFiles=913, mainApplied=False, retainedFiles=len(manifest))))


if __name__ == '__main__':
    main()
