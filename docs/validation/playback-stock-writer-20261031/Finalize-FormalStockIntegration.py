"""Accept the exact stable-host source and its actual scoped execution proofs."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
ROOT = CACHE / 'playback-stock-formal-main-integration-r1-20261101'
OUT = ROOT / 'validation-r1'
STAGE = CACHE / 'playback-stock-formal-handoff-stage-r1-20261031/delivery-root-r1'
REVIEW = CACHE / 'playback-stock-formal-host-review-r1-20261031/root-completion-r1'
files = {}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path, expected=None):
    path = Path(path).resolve()
    key = str(path).casefold()
    if key not in files:
        body = path.read_bytes()
        files[key] = dict(path=str(path), sha256=hashlib.sha256(body).hexdigest(), bytes=len(body))
    row = files[key]
    assert expected is None or row['sha256'] == expected, str(path)
    return row


def collect(value):
    if isinstance(value, list):
        for item in value:
            collect(item)
    elif isinstance(value, dict):
        for pk, hk in (('path', 'sha256'), ('manifestPath', 'sha256'),
                       ('image', 'imageSha256'), ('pdb', 'pdbSha256')):
            if isinstance(value.get(pk), str) and isinstance(value.get(hk), str):
                fact(value[pk], value[hk])
        for item in value.values():
            collect(item)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')


def proof(path):
    value = read(path)
    assert value['passed'] and not value.get('failure'), str(path)
    assert not value.get('gameExecuted', False) and not value.get('installed', False)
    collect(value)
    fact(path)
    return value


def run():
    assert not OUT.exists(), 'Preserve previous acceptance records.'
    paths = dict(core=CACHE / 'foundation-stock-formal-core-r1-20261101/test-result.json',
                 full=CACHE / 'foundation-stock-formal-full-joined-r1-20261101/test-result.json',
                 release=CACHE / 'foundation-stock-formal-release-r2-20261101/managed-build-manifest.json',
                 solution=CACHE / 'foundation-stock-formal-solution-r2-20261101/test-result.json',
                 sdk=CACHE / 'foundation-stock-formal-sdk-r1-20261101/test-result.json')
    checks = {name: proof(path) for name, path in paths.items()}
    core, full, release, solution, sdk = (checks[name] for name in ('core', 'full', 'release', 'solution', 'sdk'))
    assert core['runnerExitCode'] == 0 and len(core['inputs']) == 760
    assert full['sourceProof']['sha256'] == fact(paths['core'])['sha256']
    assert full['fullManagedMatrix'] and len(full['projects']) == 27
    assert sum(row['checks'] for row in full['projects']) == 2465
    assert all(row['passed'] and row['buildExitCode'] == row['testExitCode'] == 0 for row in full['projects'])
    assert full['continuation'] and full['sourceSetsByteIdentical']
    assert full['reusedCompletedSuites'] == 22 and full['newlyExecutedSuites'] == 5
    assert not full['originalParentTerminalSuccessClaimed']
    continuation = proof(paths['full'].parent / 'continuation-proof.json')
    assert continuation['actualCompletedChildRows'] == 22 and continuation['actualFreshSuites'] == 5
    assert continuation['actualPassingChecks'] == 2465
    assert release['buildExitCode'] == 0 and release['sourceHashesUnchanged'] and len(release['files']) == 6
    assert release['sourceSnapshot']['sha256'] == fact(paths['core'])['sha256']
    assert solution['buildExitCode'] == solution['errors'] == 0 and solution['sourceHashesUnchanged']
    assert solution['foundationProof']['sha256'] == fact(paths['full'])['sha256']
    assert sdk['runnerExitCode'] == 0 and sdk['checks'] == 14
    assert sdk['foundationProof']['sha256'] == fact(paths['full'])['sha256']
    distribution = read(sdk['result']['path'])
    assert distribution['checksPassed'] == 14 and not distribution['runtimeGameVerification']
    projects = list(Path(distribution['generatedProjects']).rglob('*.csproj'))
    assert len(projects) == 2
    for project in projects:
        fact(project)
    for label in ('core-r1', 'release-r2', 'solution-r2', 'sdk-r1', 'full-continuation-r4'):
        assert (ROOT / (label+'.driver.exit.txt')).read_text().strip() == '0'
        for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
            fact(ROOT / (label+'.driver.'+suffix))
    preparation = proof(ROOT / 'solution-fixture-preparation-r1/test-result.json')
    assert preparation['copiedFiles'] == 25 and not preparation['cleanSolutionBuildProven']
    failed_solution = read(CACHE / 'foundation-stock-formal-solution-r1-20261101/test-result.json')
    assert not failed_solution['passed'] and failed_solution['errors'] == 2
    collect(failed_solution)
    fact(CACHE / 'foundation-stock-formal-solution-r1-20261101/test-result.json')
    fact(ROOT / 'solution-r1.driver.exit.txt')

    integration = proof(ROOT / 'test-result.json')
    assert integration['sourceFilesCompared'] == 785 and integration['changedFiles'] == 12
    assert integration['actualGitApplyCheck'] == integration['actualGitApply'] == 0
    assert integration['sourceArchive']['sha256'] == '299d9cb8286b5f651dc4a79e536473a3b101a16b0c20d8bda2c84bf0f2183c3e'
    product_files = 0
    with zipfile.ZipFile(integration['sourceArchive']['path']) as archive:
        entries = [row for row in archive.infolist() if not row.is_dir()]
        assert len(entries) == 785
        for entry in entries:
            fact(REPO / entry.filename, hashlib.sha256(archive.read(entry)).hexdigest())
            product_files += entry.filename.startswith('src/')
    assert product_files == 471
    fact(REPO / 'src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.Handoff.cs',
         '9c515c3bfce25f1f191227f1d4dd4f8a58927c5678f7c208eb41bbd4cd90f51b')
    fact(REPO / 'src/RizToolbox.Runtime/Hooks/NativePlaybackSingleReturnBridge.cs',
         '79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07')
    stage = proof(STAGE / 'test-result.json')
    assert stage['sourceFiles'] == 785 and stage['sourceArchive']['sha256'] == integration['sourceArchive']['sha256']
    expected = dict(formal=(8, 10159), race=(1, 621), provider=(7, 1021), clock=(10, 179), handoff=(6, 1390))
    for name, pair in expected.items():
        group = stage['groups'][name]
        assert (group['processes'], group['checks']) == pair
        assert len(group['rows']) == pair[0]
        assert all(row['actualExitCode'] == 0 and row['result']['passed'] for row in group['rows'])
        assert sum(row['result']['checks'] for row in group['rows']) == pair[1]
    assert not stage['interruptedParentAcceptance'] and not stage['oldSnapshotR1SignedAsCurrent']
    stage_index = proof(STAGE / 'index.json')
    assert len(stage_index['files']) >= 4447
    review = proof(REVIEW / 'test-result.json')
    assert review['sourceFilesCompared'] == 785 and review['sourceArchive']['sha256'] == integration['sourceArchive']['sha256']
    assert review['actualProcesses'] == 3 and review['actualChecks'] == 80
    assert review['managedPePdbPairs'] == 10 and review['portablePdbDocuments'] == 958
    assert review['copiedInternalSessionRejected'] and review['exactWithdrawnPredecessorPreserved']
    proof(REVIEW / 'evidence-index.json')
    fact(__file__)
    for row in list(files.values()):
        assert hashlib.sha256(Path(row['path']).read_bytes()).hexdigest() == row['sha256'], row['path']
    result = dict(kind='formal-stock-provider-stable-host-current-source-validation', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(), flavor=None,
                  sourceFilesCompared=785, productSourceFiles=471, coreInputs=760,
                  managedSuites=27, managedChecks=2465, releaseAssemblies=6,
                  solutionErrors=0, solutionWarnings=solution['warnings'],
                  sdkIndependentPlugins=2, sdkChecks=14,
                  matrixContinuation=dict(recordedCompletedSuites=22, freshSuites=5,
                                          interruptedParentTerminalSuccessClaimed=False),
                  solutionScope='After recreating actual Hosting suite fixture outputs from the same frozen source; not a clean solution build.',
                  cleanSolutionBuildProven=False, cleanSolutionFixtureConfigFixPending=True,
                  targetedGroups={name: dict(processes=pair[0], checks=pair[1]) for name, pair in expected.items()},
                  targetedProcesses=32, targetedChecks=13370,
                  separateIndependentProcesses=3, separateIndependentChecks=80,
                  proofs={name: fact(path) for name, path in paths.items()},
                  targetedStage=fact(STAGE / 'test-result.json'), targetedIndex=fact(STAGE / 'index.json'),
                  independentVerification=fact(REVIEW / 'test-result.json'),
                  integration=fact(ROOT / 'test-result.json'), verifiedUniqueFiles=len(files),
                  stableFormalProviderImplemented=True, formalModuleCrossRunAdoptionImplemented=True,
                  exactWithdrawnPredecessorSupported=True, copiedInternalSessionRejected=True,
                  completeSourceGeneration=False, completeLowerWriterCoverage=False,
                  arbitrarySeek=False, productionAdmission=False, fullB01B19Completed=False,
                  gameExecuted=False, installed=False)
    OUT.mkdir()
    write(OUT / 'test-result.json', result)
    write(OUT / 'evidence-index.json', dict(kind='formal-stock-current-source-evidence-index', passed=True,
          selfHashExcluded=True, result=fact(OUT / 'test-result.json'),
          files=sorted(files.values(), key=lambda row: row['path'].casefold())))
    write(REPO / 'docs/validation/playback-stock-writer-20261031/formal-stock-validation-index.json',
          dict(result=fact(OUT / 'test-result.json'), evidenceIndex=fact(OUT / 'evidence-index.json')))
    print(json.dumps(dict(passed=True, suites=27, checks=2465, targetedProcesses=32,
                          targetedChecks=13370, files=len(files), result=fact(OUT / 'test-result.json'))))


if __name__ == '__main__':
    run()
