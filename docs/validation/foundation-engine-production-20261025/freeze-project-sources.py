"""Freeze project sources and verify the exact declared candidate build inputs.

This archive preserves source bytes, not a complete Windows/.NET/MSVC toolchain.
It neither executes targets nor grants production startup admission.
"""
from pathlib import Path
import argparse
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[3]
EXTENSIONS = {'.cs', '.csproj', '.cpp', '.c', '.h', '.asm', '.ps1', '.py', '.json',
              '.lua', '.def', '.md', '.txt', '.js', '.css', '.html', '.props', '.targets', '.sln'}
IGNORED = {'bin', 'obj', 'build', 'node_modules', 'dist', 'artifacts', '__pycache__'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if not args.build.is_absolute() or not args.output.is_absolute() or args.output.exists():
        raise ValueError('Use an absolute completed build and a fresh absolute ZIP output.')
    build = args.build.resolve()
    native = json.loads((build / 'production-build-manifest.json').read_text(encoding='utf-8-sig'))
    managed = json.loads((build / 'managed-build-manifest.json').read_text(encoding='utf-8-sig'))
    if native['kind'] != 'fixed-game-engine-production-candidate-build' or not native['managedBuilt']:
        raise ValueError('A complete production candidate build is required.')
    paths = set()
    for name in ('native', 'src', 'scripts', 'tests', 'examples', 'templates', 'tools'):
        for path in (ROOT / name).rglob('*'):
            relative = path.relative_to(ROOT)
            if not (set(relative.parts) & IGNORED) and path.suffix.lower() in EXTENSIONS and path.is_file():
                if path.is_symlink():
                    raise ValueError('Linked source refused: ' + str(path))
                paths.add(path)
    for name in ('Directory.Build.props', 'global.json', 'dependencies.lock.json',
                 'RizToolbox.sln', 'THIRD_PARTY_NOTICES.md'):
        paths.add(ROOT / name)
    paths.update(Path(__file__).parent.glob('*.py'))
    data = {path: path.read_bytes() for path in sorted(paths)}
    declared = native['sources'] + managed['sources']
    for row in declared:
        path = Path(row['path']).resolve()
        if path not in data or sha(data[path]) != row['sha256']:
            raise ValueError('Declared build input is missing or changed: ' + str(path))
    generated = {name: (build / name).read_bytes() for name in (
        'EngineBootstrapProductionProfile.h', 'NativeEngineProductionProfile.Generated.cs')}
    rows = [dict(path=path.relative_to(ROOT).as_posix(), absolutePath=str(path), sha256=sha(value))
            for path, value in data.items()]
    generated_rows = [dict(path='generated/' + name, absolutePath=str(build / name), sha256=sha(value))
                      for name, value in generated.items()]
    with zipfile.ZipFile(args.output, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, value in [(row['path'], data[Path(row['absolutePath'])]) for row in rows] + [
                ('generated/' + name, value) for name, value in generated.items()]:
            info = zipfile.ZipInfo(name, (2026, 10, 26, 0, 0, 0))
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, value)
    for path, value in data.items():
        if path.read_bytes() != value:
            raise ValueError('Source changed during archive: ' + str(path))
    with zipfile.ZipFile(args.output) as archive:
        if archive.testzip() is not None:
            raise ValueError('Source ZIP CRC verification failed.')
        for row in rows + generated_rows:
            if sha(archive.read(row['path'])) != row['sha256']:
                raise ValueError('Source ZIP byte identity differs: ' + row['path'])
    result = dict(kind='production-candidate-project-source-freeze', schemaVersion=1,
                  sourceFiles=len(rows), declaredBuildSourceRows=len(declared), generatedFiles=len(generated_rows),
                  zipPath=str(args.output), zipSha256=sha(args.output.read_bytes()), files=rows + generated_rows,
                  completeTransitiveToolchainBundle=False, gameExecuted=False, installed=False,
                  productionAdmissionProved=False)
    manifest = args.output.with_suffix('.json')
    with manifest.open('x', encoding='utf-8') as stream:
        stream.write(json.dumps(result, indent=2) + '\n')
    print(json.dumps({key: result[key] for key in ('sourceFiles', 'declaredBuildSourceRows', 'generatedFiles', 'zipSha256')}))


if __name__ == '__main__':
    main()
