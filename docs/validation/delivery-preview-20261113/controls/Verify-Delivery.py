"""Verify the new ZIP, then independently unpack and build QUICKSTART examples offline.

No game binary is read, and no game installation or execution is performed.
"""
import argparse
import datetime
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import zipfile

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox').resolve()
SCOPE = Path(__file__).resolve().parent
OUTPUT = ROOT / 'artifacts/delivery-20261113'
ZIP = OUTPUT / 'RizToolbox-0.3.0-sdk-runtime-20261113.zip'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PRODUCTS = [f'RizToolbox.{name}' for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')]
NATIVE = {'winhttp.dll', 'BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll',
          'BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll',
          'BepInEx/plugins/RizToolbox/licenses/doorstop-corresponding-source.zip',
          'BepInEx/plugins/RizToolbox/licenses/UnityDoorstop.LGPL-2.1.txt'}
INSTALL_PATHS = NATIVE | {'.doorstop_version', 'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'}
INSTALL_PATHS |= {'BepInEx/plugins/RizToolbox/' + name + '.dll' for name in PRODUCTS}
INSTALL_PATHS |= {'BepInEx/RizToolbox/mods/' + plugin + '/' + name
                  for plugin in ('example.panel', 'example.events', 'example.color-low', 'example.color-high', 'example.scores')
                  for name in ('plugin.json', 'RizToolbox.Examples.dll')}


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def fact(path):
    path = Path(path).resolve()
    raw = path.read_bytes()
    return {'path': str(path), 'bytes': len(raw), 'sha256': digest(raw)}


def pin(row):
    require(fact(row['path']) == {name: row[name] for name in ('path', 'bytes', 'sha256')},
            'External delivery proof changed: ' + row['path'])


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def safe(name):
    path = PurePosixPath(name)
    require(name == path.as_posix() and not path.is_absolute()
            and all(part not in ('', '.', '..') for part in path.parts)
            and '\\' not in name and not name.startswith(('\\', '/')) and ':' not in name,
            'Unsafe ZIP entry: ' + name)


def run(argv, cwd, env, label, logdir):
    with (logdir / (label + '.stdout.log')).open('xb') as stdout, \
         (logdir / (label + '.stderr.log')).open('xb') as stderr:
        result = subprocess.run([str(item) for item in argv], cwd=cwd, env=env,
                                stdout=stdout, stderr=stderr, stdin=subprocess.DEVNULL,
                                creationflags=subprocess.CREATE_NO_WINDOW)
    require(result.returncode == 0, 'Independent QUICKSTART failed; preserve ' + label + ' logs.')
    return {'name': label, 'arguments': [str(item) for item in argv], 'exitCode': result.returncode,
            'stdout': fact(logdir / (label + '.stdout.log')),
            'stderr': fact(logdir / (label + '.stderr.log'))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--execute', action='store_true')
    args = parser.parse_args()
    require(args.execute and SCOPE == Path('D:/riz-build-cache-next/delivery-20261113-r1').resolve(),
            'Execute only the new delivery verifier scope.')
    require(len(INSTALL_PATHS) == 23, 'Original installer path contract changed.')
    raw_manifest = (OUTPUT / 'delivery-manifest.json').read_bytes()
    manifest = json.loads(raw_manifest)
    raw_zip = ZIP.read_bytes()
    require((OUTPUT / 'SHA256SUMS.txt').read_text(encoding='utf-8-sig').split() ==
            [digest(raw_zip), ZIP.name], 'New ZIP SHA256SUMS differs.')
    require(manifest['kind'] == 'riz-toolbox-sdk-runtime-delivery-v1'
            and manifest['sdkContractVersion'] == '1.2.0'
            and manifest['runtimeVersion'] == '0.3.0'
            and manifest['exampleCompiles'] is True
            and manifest['completeSpeedEnabled'] is False
            and manifest['completeSeekEnabled'] is False
            and manifest['productionAdmission'] is False
            and manifest['runtimeGameVerified'] is False
            and manifest['gameExecuted'] is False
            and all(row['osExitCode'] == 0 for row in manifest['commands']),
            'Delivery manifest status or build receipts differ.')
    for name in ('sixProductAcceptance', 'graphMetadata', 'nativeManifest', 'nativeAbiReview', 'sdkReference'):
        pin(manifest[name])
    graph = read(manifest['graphMetadata']['path'])
    review = read(manifest['sixProductAcceptance']['path'])
    abi = read(manifest['nativeAbiReview']['path'])
    require(graph['passed'] is True and review['passed'] is True and review['graphMetadata'] == manifest['graphMetadata']
            and abi['passed'] is True and abi['nativeManifest'] == manifest['nativeManifest'],
            'Actual final source/native proof chain differs.')
    with zipfile.ZipFile(io.BytesIO(raw_zip)) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)) and archive.testzip() is None,
                'New ZIP has duplicate paths or CRC failure.')
        for name in names:
            safe(name)
        require(archive.read('delivery-manifest.json') == raw_manifest
                and set(names) == {row['path'] for row in manifest['files']} | {'delivery-manifest.json'},
                'New ZIP file inventory differs from manifest.')
        for row in manifest['files']:
            content = archive.read(row['path'])
            require(len(content) == row['bytes'] and digest(content) == row['sha256'],
                    'New ZIP member bytes differ: ' + row['path'])
        for name in ('LICENSE', 'THIRD_PARTY_NOTICES.md'):
            require(archive.read(name) == (ROOT / name).read_bytes(), 'Root license bytes differ.')
        sdk = archive.read('sdk/feed/RizToolbox.Sdk.1.2.0.nupkg')
        with zipfile.ZipFile(io.BytesIO(sdk)) as package:
            require(not any(name.startswith('lib/') for name in package.namelist())
                    and digest(package.read('ref/net6.0/RizToolbox.Sdk.dll')) == manifest['sdkReference']['sha256'],
                    'SDK package is not the accepted reference-only assembly.')
        runtime = json.loads(archive.read('runtime/artifacts/package-manifest.json'))
        paths = {row['path'] for row in runtime['files']}
        require(len(runtime['files']) == 23 and paths == INSTALL_PATHS,
                'Runtime installer path whitelist differs.')
        for row in runtime['files']:
            name = 'runtime/artifacts/package/' + row['path']
            require(digest(archive.read(name)) == row['sha256'], 'Runtime payload differs: ' + name)
        for project in graph['projects']:
            name = 'runtime/artifacts/package/BepInEx/plugins/RizToolbox/' + project['projectName'] + '.dll'
            require(digest(archive.read(name)) == project['dll']['sha256'],
                    'Managed DLL differs from final six graph.')
        profile = 'runtime/artifacts/package/BepInEx/plugins/RizToolbox/profiles/steam-25977441.json'
        require(archive.read(profile) == (ROOT / 'src/RizToolbox.Adapter/profiles/steam-25977441.json').read_bytes(),
                'Shipped target profile differs from current source.')
        quickstart = archive.read('QUICKSTART.txt').decode('utf-8')
        for text in ('SDK 1.2.0 / Runtime 0.3.0', '线/环目标', '完整 Speed / Seek',
                     'dotnet restore examples/RizModProject/RizModProject.csproj',
                     'dotnet restore examples/RizToolbox.Examples/RizToolbox.Examples.csproj'):
            require(text in quickstart, 'QUICKSTART misses the current delivery boundary or command.')
        work = SCOPE / 'verify-unpacked'
        require(not work.exists(), 'Preserve prior independent unpacked verification attempt.')
        work.mkdir()
        unpacked = work / 'payload'
        unpacked.mkdir()
        for name in names:
            target = unpacked / name
            require(unpacked in target.resolve().parents, 'ZIP entry escaped independent unpack root.')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(name))
    environment = os.environ.copy()
    for name, directory in {'TEMP': 'temp', 'TMP': 'temp',
                            'NUGET_PACKAGES': 'packages',
                            'NUGET_HTTP_CACHE_PATH': 'http-cache',
                            'NUGET_PLUGINS_CACHE_PATH': 'plugin-cache',
                            'NUGET_MIGRATION_CACHE_PATH': 'migration-cache',
                            'DOTNET_BUNDLE_EXTRACT_BASE_DIR': 'bundle'}.items():
        folder = work / 'environment' / directory
        folder.mkdir(parents=True, exist_ok=True)
        environment[name] = str(folder)
    environment.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                       DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1',
                       DOTNET_CLI_USE_MSBUILD_SERVER='0', DOTNET_NOLOGO='1',
                       MSBUILDDISABLENODEREUSE='1')
    commands = []
    for label, relative in (('template', 'examples/RizModProject/RizModProject.csproj'),
                            ('examples', 'examples/RizToolbox.Examples/RizToolbox.Examples.csproj')):
        project = unpacked / relative
        commands.append(run([DOTNET, 'restore', project, '--configfile', unpacked / 'NuGet.Config',
                             '--packages', work / 'environment/packages', '-p:NuGetAudit=false',
                             '--verbosity', 'minimal'], unpacked, environment, label + '-restore', work))
        commands.append(run([DOTNET, 'build', project, '-c', 'Release', '--no-restore',
                             '--verbosity', 'minimal'], unpacked, environment, label + '-build', work))
        require(not (project.parent / 'bin/Release/net6.0/RizToolbox.Sdk.dll').exists(),
                'Unpacked plugin copied host SDK implementation.')
    result = {'kind': 'delivery-20261113-verification-v1', 'passed': True,
              'verifiedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'archive': fact(ZIP), 'archiveFileCount': len(names),
              'everyArchiveEntryVerified': True, 'sdkReferenceOnly': True,
              'runtimeManifestFilesVerified': 23, 'targetProfileBytesVerified': True,
              'unpackedQuickstartBuilds': commands, 'networkUsed': False,
              'gameExecuted': False, 'gameRead': False,
              'completeSpeedEnabled': False, 'completeSeekEnabled': False,
              'productionAdmission': False}
    target = OUTPUT / 'delivery-verification.json'
    require(not target.exists(), 'Preserve existing delivery verification receipt.')
    with target.open('xb') as stream:
        stream.write((json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
        stream.flush(); os.fsync(stream.fileno())
    print(json.dumps({'passed': True, 'verification': fact(target), 'zip': fact(ZIP)}))


if __name__ == '__main__':
    main()
