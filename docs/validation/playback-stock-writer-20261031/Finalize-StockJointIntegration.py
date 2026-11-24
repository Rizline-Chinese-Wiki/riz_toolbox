"""Accept the exact 778-source S5/S6 composition from fresh execution evidence."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
ROOT = CACHE / 'playback-stock-handoff-main-integration-r2-20261031'
OUT = ROOT / 'validation-r1'
SPECIAL = CACHE / 'playback-stock-joint-778-checks-r1-20261031/completion-r1.json'
files = {}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def check(path, expected=None):
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
        for row in value:
            collect(row)
    elif isinstance(value, dict):
        for path_key, hash_key in (('path', 'sha256'), ('manifestPath', 'sha256'),
                                   ('image', 'imageSha256'), ('pdb', 'pdbSha256')):
            if isinstance(value.get(path_key), str) and isinstance(value.get(hash_key), str):
                check(value[path_key], value[hash_key])
        for row in value.values():
            collect(row)


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def run():
    assert not OUT.exists(), 'Keep earlier checks intact.'
    paths = {name: CACHE / ('foundation-stock-joint-778-' + name + '-r1-20261031') /
             ('managed-build-manifest.json' if name == 'release' else 'test-result.json')
             for name in ('core', 'full', 'release', 'solution', 'sdk')}
    proofs = {name: read(path) for name, path in paths.items()}
    for name, proof in proofs.items():
        assert proof['passed'] and not proof.get('failure'), name
        assert not proof.get('gameExecuted', False) and not proof.get('installed', False), name
        collect(proof)
        check(paths[name])
        assert (ROOT / (name + '-r1.driver.exit.txt')).read_text().strip() == '0'
        for suffix in ('exit.txt', 'stdout.log', 'stderr.log'):
            check(ROOT / (name + '-r1.driver.' + suffix))
    core, full, release, solution, sdk = (proofs[n] for n in ('core', 'full', 'release', 'solution', 'sdk'))
    assert core['runnerExitCode'] == 0 and len(core['inputs']) == 756
    assert full['sourceProof']['sha256'] == check(paths['core'])['sha256']
    assert full['fullManagedMatrix'] and len(full['projects']) == 27
    assert sum(row['checks'] for row in full['projects']) == 2465
    assert all(row['passed'] and row['buildExitCode'] == row['testExitCode'] == 0 for row in full['projects'])
    assert release['buildExitCode'] == 0 and release['sourceHashesUnchanged']
    assert release['sourceSnapshot']['sha256'] == check(paths['core'])['sha256']
    assert len(release['files']) == 6 and all(row['path'].lower().endswith('.dll') for row in release['files'])
    assert solution['buildExitCode'] == solution['errors'] == solution['warnings'] == 0
    assert sdk['runnerExitCode'] == 0 and sdk['checks'] == 14
    distribution = read(sdk['result']['path'])
    assert distribution['checksPassed'] == 14 and distribution['runtimeGameVerification'] is False
    plugins = list(Path(distribution['generatedProjects']).rglob('*.csproj'))
    assert len(plugins) == 2
    for plugin in plugins:
        check(plugin)

    integration = read(ROOT / 'test-result.json')
    assert integration['passed'] and integration['sourceFilesCompared'] == 778 and integration['changedFiles'] == 23
    assert integration['s5ModuleAndFaultPumpPreserved'] and integration['bridgeSehFixPreserved']
    # Only immutable archives/current after inputs are collected here. The
    # pre-merge file names in old manifests describe historical bytes.
    collect(integration)
    check(ROOT / 'test-result.json')
    with zipfile.ZipFile(integration['sourceArchive']['path']) as archive:
        entries = [row for row in archive.infolist() if not row.is_dir()]
        assert len(entries) == 778
        for entry in entries:
            check(REPO / entry.filename, hashlib.sha256(archive.read(entry)).hexdigest())
    source_rows = read(ROOT / 'source-manifest.json')
    assert len(source_rows) == 778
    for row in source_rows:
        check(REPO / row['relativePath'], row['sha256'])
    check(ROOT / 'source-manifest.json')

    special = read(SPECIAL)
    assert special['passed'] and special['sourceFiles'] == 778 and special['sourceHashesUnchanged']
    assert special['productSourceEdits'] == 0 and not special['guardRelaxed']
    assert special['sourceArchive']['sha256'] == integration['sourceArchive']['sha256']
    assert special['actualManagedProcesses'] == 22 and special['actualManagedChecks'] == 1397
    assert special['actualManagedPePdbPairs'] == 15 and special['actualManagedPortablePdbDocuments'] == 1429
    assert not special.get('gameExecuted', False) and not special.get('installed', False)
    collect(special)
    check(SPECIAL)
    # The special-result manifest declares its exact scope. Its nested source
    # and PE/PDB proofs are independently rehashed, rather than inferred from a
    # broad green flag or copied historical process count.
    expected = {'provider': (7, 1021), 'clock': (10, 179), 'handoff': (5, 197)}
    assert len(special['scopes']) == 3
    for scope in special['scopes']:
        assert scope['passed'] and scope['new778ManagedCompilation'] and scope['allActualExitCodesZero']
        assert (scope['processes'], scope['checks']) == expected.pop(scope['scope'])
        assert len(scope['modes']) == scope['processes']
        assert all(mode['actualExitCode'] == 0 and mode['result']['passed'] for mode in scope['modes'])
        for key in ('execution', 'symbols'):
            nested = read(scope[key]['path'])
            assert nested['passed'], scope[key]['path']
            collect(nested)
    assert not expected
    native_reuse = read(special['nativeReuseProof']['path'])
    assert native_reuse['passed']
    collect(native_reuse)
    check(__file__)
    for row in files.values():
        assert hashlib.sha256(Path(row['path']).read_bytes()).hexdigest() == row['sha256'], row['path']
    result = dict(kind='stock-provider-and-joint-handoff-current-source-validation', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(),
                  sourceScope='Exact S5/S6 merged 778-source tree, before formal stable-host adoption',
                  currentSourceAccepted=True, sourceFilesCompared=778, coreInputs=756,
                  managedSuites=27, managedChecks=2465, releaseAssemblies=6,
                  solutionErrors=0, solutionWarnings=0, sdkIndependentPlugins=2, sdkChecks=14,
                  targetedProcesses=22, targetedChecks=1397, targetedPePdbPairs=15,
                  targetedPortablePdbDocuments=1429,
                  proofs={name: check(path) for name, path in paths.items()},
                  targetedExecution=check(SPECIAL), integration=check(ROOT / 'test-result.json'),
                  verifiedUniqueFiles=len(files),
                  singleSessionFormalProviderImplemented=True, jointNativeLogicalHandoffImplemented=True,
                  formalModuleCrossRunAdoptionImplemented=False,
                  sourceGenerationAdmissionProven=False, completeLowerWriterCoverage=False,
                  arbitrarySeekImplemented=False, fullB01B19Completed=False,
                  productionAdmission=False, gameExecuted=False, installed=False)
    OUT.mkdir()
    write(OUT / 'test-result.json', result)
    index = dict(kind='stock-provider-and-joint-handoff-evidence-index', passed=True, flavor=None,
                 selfHashExcluded=True, result=check(OUT / 'test-result.json'),
                 files=sorted(files.values(), key=lambda row: row['path'].casefold()))
    write(OUT / 'evidence-index.json', index)
    write(REPO / 'docs/validation/playback-stock-writer-20261031/stock-provider-handoff-validation-index.json',
          dict(result=check(OUT / 'test-result.json'), evidenceIndex=check(OUT / 'evidence-index.json')))
    print(json.dumps({key: result[key] for key in ('passed', 'managedSuites', 'managedChecks', 'verifiedUniqueFiles')}))


if __name__ == '__main__':
    run()
