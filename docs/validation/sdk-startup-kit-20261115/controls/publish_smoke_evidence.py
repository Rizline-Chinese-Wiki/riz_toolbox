"""Freeze the delivered kit's actual smoke; check only explicit evidence and ZIP."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent
RUN = Path('D:/riz-build-cache-next/sdk-startup-kit-20261115/smoke-r1')
DEST = ROOT / 'docs/validation/sdk-startup-kit-20261115'
sha = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
read = lambda p: json.loads(Path(p).read_text(encoding='utf-8-sig'))


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


def local(base, name):
    require(all(part and part not in ('.', '..') and ':' not in part and '\\' not in part for part in name.split('/')), 'Unsafe evidence path')
    path = base / name
    require(path.resolve().is_relative_to(base.resolve()) and path.resolve() != base.resolve(), 'Evidence escaped')
    for parent in (path, *path.parents):
        require(not parent.is_symlink() and not getattr(parent.lstat(), 'st_file_attributes', 0) & 0x400, 'Reparse evidence')
        if parent == base:
            break
    require(path.is_file(), 'Missing evidence')
    return path


def check(base, delivery):
    base, delivery = Path(base).resolve(), Path(delivery).resolve()
    at = lambda name: local(base, name)
    summary = read(at('validation-summary.json'))
    require({p.relative_to(base).as_posix() for p in base.rglob('*') if p.is_file()}
            == set(summary['files']) | {'validation-summary.json'}, 'Evidence inventory differs')
    for name, row in summary['files'].items():
        require(sha(at(name)) == row['sha256'] and at(name).stat().st_size == row['bytes'], 'Evidence bytes differ')
    receipt = read(at('delivery-receipt.json'))
    state = read(at('run/result.json'))
    observed = read(at('root-exit.json'))
    require(sha(delivery) == receipt['sha256'] == state['deliverySha256']
            == '3dc92a00099da0c55276defe963826597d4c43acaa0ff89a97f066d44f212817', 'Actual delivered ZIP differs')
    with zipfile.ZipFile(delivery) as archive:
        require(archive.testzip() is None and len(archive.namelist()) == len(set(archive.namelist())), 'ZIP invalid')
        manifest = json.loads(archive.read('manifest.json'))
        require(set(archive.namelist()) == set(manifest['files']) | {'manifest.json'}
                and hashlib.sha256(archive.read('manifest.json')).hexdigest() == state['manifestSha256'], 'ZIP manifest differs')
        for name, row in manifest['files'].items():
            data = archive.read(name)
            require(hashlib.sha256(data).hexdigest() == row['sha256'] and len(data) == row['bytes'], 'ZIP member differs')
        require(archive.read('manifest.json') == at('kit-manifest.json').read_bytes(), 'Extracted manifest differs')
        require(len(manifest['templateInputs']) == 5 and len(manifest['apiInputs']) == 29
                and manifest['buildCompilerInputs'] == 1172 and manifest['sdk14Checks'] == 14
                and manifest['referenceKitOnly'] and manifest['dependencyPackageBytesIncluded']
                and not manifest['runtimeIncluded'] and not manifest['sdkGoalComplete'], 'Kit scope differs')
        packages = [name for name in manifest['files'] if name.startswith('feed/') and name.endswith('.nupkg')]
        require(len(packages) == 5, 'SDK plus four pinned offline packages are missing')
        require(manifest['sdkPackageSha256'] == state['restoredSdkSha256'] == sha(at('restored-sdk.nupkg'))
                == hashlib.sha256(archive.read('feed/RizToolbox.Sdk.1.2.0.nupkg')).hexdigest(), 'Exact SDK restore identity differs')
    require(state['passed'] and state['allDeliveredInputsUnchanged'] and state['controllerPid'] == observed['controllerPid'] == 32812
            and observed['originalExecSession'] == 11344 and observed['actualControllerToolExitCode'] == 0
            and observed['resultSha256'] == sha(at('run/result.json')), 'Original smoke controller completion differs')
    require([row['id'] for row in state['steps']] == ['isolated-template', 'generate-project', 'offline-restore', 'release-build']
            and all(row['naturalExit'] and row['exitCode'] == 0 for row in state['steps']), 'Documented flow was not completed')
    for row in state['steps']:
        require(sha(at('run/' + row['id'] + '.log')) == row['logSha256'], 'Direct CLI log differs')
    body = at('run/release-build.log').read_text(encoding='utf-8-sig')
    require(re.search(r'0\s+个警告\s+0\s+个错误', body) or re.search(r'0 Warning\(s\)\s+0 Error\(s\)', body), 'Release is not W0/E0')
    require(sha(at('generated/MyMod.dll')) == state['pluginSha256']
            and sha(at('generated/plugin.json')) == state['pluginManifestSha256'], 'Compiled plugin identity differs')
    plugin = read(at('generated/plugin.json'))
    require(plugin['id'] == 'author.mymod' and plugin['entryType'] == 'MyMod.Plugin'
            and plugin['entryAssembly'] == 'MyMod.dll', 'Generated plugin identity differs')
    require(set(state['childEnvironmentOverrides']) <= {'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH',
            'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_NOLOGO'}, 'Unexpected environment override')
    require(not any(state[name] for name in ('gameStarted', 'installedIntoGame', 'globalTemplateHiveModified', 'sdkGoalComplete')),
            'Unexecuted runtime scope was promoted')
    output = dict(passed=True, evidenceAndDeliveryOnly=True, documentedCommands=4, kitMembers=len(manifest['files']) + 1,
                  pluginSha256=state['pluginSha256'], summarySha256=sha(at('validation-summary.json')), sdkGoalComplete=False)
    print(json.dumps(output))
    return output


def publish():
    state = read(RUN / 'result.json')
    receipt = read(WORK / 'delivery-receipt.json')
    require(state['passed'] and state['controllerPid'] == 32812 and not DEST.exists(), 'Preserve evidence or smoke incomplete')
    staging = DEST.with_name(DEST.name + '.staging')
    require(not staging.exists(), 'Preserve partial evidence')
    staging.mkdir(parents=True)

    def copy(origin, relative):
        target = staging / relative
        require(target.resolve().is_relative_to(staging.resolve()) and not target.exists(), 'Duplicate or escaped role')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(origin, target)
        require(sha(origin) == sha(target), 'Evidence changed')

    copy(WORK / 'delivery-receipt.json', 'delivery-receipt.json')
    copy(RUN / 'result.json', 'run/result.json')
    copy(RUN / 'kit/manifest.json', 'kit-manifest.json')
    for row in state['steps']:
        copy(RUN / (row['id'] + '.log'), 'run/' + row['id'] + '.log')
    for name in ('Plugin.cs', 'MyMod.csproj', 'plugin.json'):
        copy(RUN / 'kit/MyMod' / name, 'generated/' + name)
    copy(RUN / 'kit/MyMod/bin/Release/net6.0/MyMod.dll', 'generated/MyMod.dll')
    copy(RUN / 'kit/MyMod/obj/project.assets.json', 'generated/project.assets.json')
    copy(RUN / 'kit/.nuget-cache/riztoolbox.sdk/1.2.0/riztoolbox.sdk.1.2.0.nupkg', 'restored-sdk.nupkg')
    for name in ('create_reference_kit.py', 'verify_reference_kit.py', 'publish_smoke_evidence.py'):
        copy(WORK / name, 'controls/' + name)
    observed = dict(schema='current-sdk-kit-original-exec-completion/v1', controllerPid=32812,
                    originalExecSession=11344, launchChunk='fcf902', completionChunk='f13ea9', actualControllerToolExitCode=0,
                    exitObservedBy='Original exec session completion; each child observed by its own original Popen.wait',
                    resultSha256=sha(RUN / 'result.json'), sdkGoalComplete=False)
    (staging / 'root-exit.json').write_text(json.dumps(observed, indent=2) + '\n', encoding='utf-8')
    summary = dict(schema='current-sdk-kit-smoke-evidence/v1', requiresDeliveredZip=True, sdkGoalComplete=False,
                   files={p.relative_to(staging).as_posix(): dict(bytes=p.stat().st_size, sha256=sha(p))
                          for p in sorted(staging.rglob('*')) if p.is_file()})
    (staging / 'validation-summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    check(staging, receipt['zip'])
    os.replace(staging, DEST)
    print(json.dumps(dict(published=str(DEST), summarySha256=sha(DEST / 'validation-summary.json'))))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check-only', type=Path)
    parser.add_argument('--delivery', type=Path)
    args = parser.parse_args()
    if args.check_only:
        require(args.delivery is not None, 'Pass the explicitly delivered ZIP')
        check(args.check_only, args.delivery)
    else:
        publish()
