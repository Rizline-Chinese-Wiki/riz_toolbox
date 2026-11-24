"""Integrate the five verified stock-source increments, preserving the prior tree.

Only explicitly changed paths are written. Every other archived source must match
the checkout, apart from the already declared EngineWait snapshot transformation.
No build result from the old tree is promoted to acceptance of the integrated tree.
"""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[3]
CACHE = Path('D:/riz-build-cache-next')
OUT = CACHE / 'playback-stock-main-integration-r1-20261031'
FINAL = CACHE / 'playback-stock-run-authority-r1-20261031'
DELIVERIES = [
    ('playback-stock-occurrence-stage-r1-20261031', 'completion-r1.json', 'handoff-r2/changed-files.json'),
    ('playback-stock-writer-stage-r1-20261031', 'completion-r2.json', 'handoff-r1/changed-files.json'),
    ('playback-stock-writer-lifetime-stage-r1-20261031', 'completion-r1.json', 'handoff-r1/changed-files.json'),
    ('playback-stock-rooted-source-stage-r1-20261031', 'completion-r1.json', 'handoff-r1/changed-files.json'),
    ('playback-stock-run-authority-r1-20261031', 'completion-r1.json', 'handoff-r1/changed-files.json'),
]


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path):
    path = Path(path).resolve()
    return {'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size}


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def run():
    assert not OUT.exists(), 'Preserve the prior integration evidence.'
    expected = {}
    last = {}
    deliveries = []
    for directory, manifest_name, changes_name in DELIVERIES:
        root = CACHE / directory
        manifest = read(root / manifest_name)
        assert manifest['passed'] is True
        changes = read(root / changes_name)
        if isinstance(changes, dict):
            changes = changes['files']
        for row in changes:
            name = row['relativePath']
            assert (REPOSITORY / name).resolve().is_relative_to(REPOSITORY)
            if name not in expected:
                expected[name] = row['before']
            elif row['before'] is not None:
                assert last[name]['after']['sha256'] == row['before']['sha256']
            last[name] = row
        deliveries.append(fact(root / manifest_name))
    import zipfile
    final_manifest = read(FINAL / 'completion-r1.json')
    final_archive = final_manifest['sourceArchive']
    assert fact(final_archive['path'])['sha256'] == final_archive['sha256']
    with zipfile.ZipFile(final_archive['path']) as archive:
        final_source = {item.filename: archive.read(item) for item in archive.infolist() if not item.is_dir()}
    assert len(final_source) == 745
    harness = 'scripts/Test-EngineWaitManagedChecks.ps1'
    harness_transform = {'worktree': '5c1d582c0a98db366a5bf5f2ea2fa0255e7064ea8a86f10e02f40ad56983feda',
                         'snapshot': 'edb66f48a59dfb67cb700c7ab99a6d6b416f6c5890d294a44f33c891bf6bc903'}
    for name, before in expected.items():
        path = REPOSITORY / name
        assert (not path.exists()) if before is None else (path.is_file() and fact(path)['sha256'] == before['sha256']), name
        assert hashlib.sha256(final_source[name]).hexdigest() == last[name]['after']['sha256'], name
    for name, data in final_source.items():
        if name in expected:
            continue
        path = REPOSITORY / name
        digest = hashlib.sha256(data).hexdigest()
        if name == harness:
            assert digest == harness_transform['snapshot'] and fact(path)['sha256'] == harness_transform['worktree']
        else:
            assert path.is_file() and fact(path)['sha256'] == digest, 'Unrelated source differs: ' + name
    # Preserve original live inputs before writing any implementation file. This
    # permits historical core-r4 evidence to close against its own exact source.
    OUT.mkdir()
    archived = {}
    extensions = {'.cs', '.csproj', '.cpp', '.h', '.hpp', '.asm', '.inl', '.def', '.ps1', '.py', '.json',
                  '.md', '.props', '.targets', '.sln', '.toml', '.txt', '.yml', '.yaml'}
    paths = set()
    for directory in ('src', 'tests', 'native', 'scripts', 'packaging', '.docs', 'docs'):
        for path in (REPOSITORY / directory).rglob('*'):
            if path.is_file() and path.suffix.lower() in extensions and not any(part in ('obj', 'bin', '__pycache__') for part in path.relative_to(REPOSITORY).parts):
                paths.add(path.resolve())
    paths.update(path.resolve() for path in REPOSITORY.iterdir() if path.is_file() and path.suffix.lower() in extensions)
    for path in sorted(paths):
        rel = path.relative_to(REPOSITORY)
        copy = OUT / 'before-worktree' / rel
        copy.parent.mkdir(parents=True, exist_ok=True)
        copy.write_bytes(path.read_bytes())
        original = fact(path)
        assert original['sha256'] == fact(copy)['sha256']
        archived[str(path)] = {'originalPath': str(path), 'archived': fact(copy)}
    core = CACHE / 'foundation-nine-source-core-r4-20261031/test-result.json'
    core_inputs = read(core)['inputs']
    assert len(core_inputs) == 727
    for row in core_inputs:
        assert archived[str(Path(row['path']).resolve())]['archived']['sha256'] == row['sha256'], row['path']
    capture = {'kind': 'pre-stock-integration-worktree-archive', 'passed': True,
               'capturedBeforeImplementationWrites': True, 'capturedAtUtc': datetime.now(timezone.utc).isoformat(),
               'repository': str(REPOSITORY), 'coreProof': fact(core), 'coreInputCount': 727,
               'files': list(archived.values()), 'scope': 'Historical core-r4 only; not integrated-source acceptance.'}
    write(OUT / 'before-worktree-manifest.json', capture)
    # Validate all baselines again immediately before mutation; preserve unrelated edits.
    for name, before in expected.items():
        path = REPOSITORY / name
        assert (not path.exists()) if before is None else fact(path)['sha256'] == before['sha256']
    changes = []
    for name in sorted(expected):
        path = REPOSITORY / name
        old = fact(path) if path.exists() else None
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(final_source[name])
        changes.append({'relativePath': name, 'before': old, 'after': fact(path)})
    for name, data in final_source.items():
        if name != harness:
            assert (REPOSITORY / name).read_bytes() == data, name
    result = {'kind': 'actual-stock-source-main-worktree-integration', 'passed': True,
              'implementationMerged': True, 'currentSourceBuildVerified': False, 'currentSourceFullMatrixVerified': False,
              'frozenDeliveries': deliveries, 'historicalWorktreeArchive': fact(OUT / 'before-worktree-manifest.json'),
              'sourceFilesCompared': 745, 'declaredHarnessTransformation': harness_transform,
              'changedFiles': changes, 'gameExecuted': False, 'installed': False, 'productionAdmission': False}
    write(OUT / 'test-result.json', result)
    print(json.dumps({'passed': True, 'mergedFiles': len(changes), 'sourceFilesCompared': 745,
                      'archivedBeforeFiles': len(archived), 'currentSourceMatrixStillRequired': True}))


if __name__ == '__main__':
    run()
