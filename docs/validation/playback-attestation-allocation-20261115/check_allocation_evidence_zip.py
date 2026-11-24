"""Read an allocation evidence ZIP only. Never execute an archived program."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import struct
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def export_names(data):
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    require(data[:2] == b'MZ' and data[pe:pe + 4] == b'PE\0\0', 'Invalid owned helper PE')
    count, optional_size = struct.unpack_from('<H12xH', data, pe + 6)
    optional = pe + 24
    require(struct.unpack_from('<H', data, optional)[0] == 0x20b, 'Owned helper is not PE32+')
    export_rva = struct.unpack_from('<I', data, optional + 112)[0]
    sections = []
    for i in range(count):
        at = optional + optional_size + 40 * i
        size, rva, raw_size, raw = struct.unpack_from('<IIII', data, at + 8)
        sections.append((rva, max(size, raw_size), raw))

    def offset(rva):
        for base, size, raw in sections:
            if base <= rva < base + size:
                return raw + rva - base
        raise ValueError('Owned helper export RVA outside sections')

    directory = offset(export_rva)
    number = struct.unpack_from('<I', data, directory + 24)[0]
    names = offset(struct.unpack_from('<I', data, directory + 32)[0])
    result = []
    for i in range(number):
        at = offset(struct.unpack_from('<I', data, names + 4 * i)[0])
        end = data.index(b'\0', at)
        result.append(data[at:end].decode('ascii'))
    return sorted(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--expected-sha256')
    args = parser.parse_args()
    raw = args.archive.read_bytes()
    archive_sha = digest(raw)
    if args.expected_sha256:
        require(archive_sha == args.expected_sha256.lower(), 'Outer ZIP digest mismatch')
    checks = 0
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        names = z.namelist()
        require(len(names) == len(set(names)), 'Duplicate ZIP members')
        require(all(not x.startswith('/') and '..' not in PurePosixPath(x).parts and '\\' not in x for x in names), 'Unsafe ZIP paths')
        require(z.testzip() is None, 'ZIP CRC failure')
        index = json.loads(z.read('hash-index.json'))
        indexed = [x['path'] for x in index['files']]
        require(len(indexed) == len(set(indexed)), 'Duplicate index entries')
        require(set(names) == set(indexed) | {'hash-index.json'}, 'ZIP/index inventory mismatch')
        for row in index['files']:
            data = z.read(row['path'])
            require(len(data) == row['bytes'] and digest(data) == row['sha256'], 'Member digest mismatch: ' + row['path'])
            checks += 1

        def data(name):
            return z.read(name)

        def obj(name):
            return json.loads(data(name).decode('utf-8-sig'))

        def bind(name, expected):
            nonlocal checks
            require(digest(data(name)) == expected, 'Cross-record identity mismatch: ' + name)
            checks += 1

        manifest = obj('allocation/overlay-manifest.json')
        audit = obj('allocation/source-scope-audit.json')
        acceptance = obj('allocation/fixture-acceptance.json')
        controller = obj('allocation/fixture-controller-result.json')
        bind('allocation/overlay-manifest.json', audit['overlayManifestSha256'])
        bind('allocation/overlay-manifest.json', acceptance['overlayManifestSha256'])
        bind('allocation/source-scope-audit.json', acceptance['scopeAuditSha256'])
        bind('allocation/fixture-controller-result.json', acceptance['controllerResultSha256'])
        bind('allocation/fixture-controller.log', acceptance['controllerLogSha256'])
        bind('allocation/run-fixture.py', acceptance['controllerSourceSha256'])
        bind('allocation/AllocationProveFixture.cpp', acceptance['fixtureSourceSha256'])
        require(acceptance['passed'] and acceptance['actualControllerExitCode'] == 0 and acceptance['naturalExit'], 'Focused fixture not accepted')
        require(controller['passed'] and controller['actualExitCode'] == 0 and controller['naturalExit'], 'Controller not naturally successful')
        require(not acceptance['gameExecuted'] and not acceptance['gameOrDobbyBinaryLoaded'], 'Focused fixture scope differs')
        require(audit['passed'] and audit['diagnosticObserverRemovalRecoversProductionIgnoringWhitespace'], 'Source scope audit not accepted')
        for row in manifest['files']:
            basis = row['basis']
            before = data('allocation/baselines/' + basis + '/FirstInstallPlaybackAttestation.inl')
            after_name = 'allocation/' + row['overlayPath']
            after = data(after_name)
            require(digest(before) == row['baselineSha256'] and digest(after) == row['sha256'], 'Overlay/baseline identity differs')
            def parts(value):
                value = value.decode('utf-8-sig').replace('\r\n', '\n')
                start = value.index('bool Prove(unsigned index,')
                end = value.index('bool Prepared(unsigned index,', start)
                return value[:start], value[start:end], value[end:]
            old, new = parts(before), parts(after)
            require(old[0] == new[0] and old[2] == new[2], 'Normalized non-Prove source changed')
            body = new[1]
            require(body.count('VirtualAlloc(') == 1 and 'VirtualAlloc(nullptr, 2 * pageBytes' in body, 'Allocation is not one null-hint reservation')
            require('original.block = original.code + pageBytes;' in body and 'systemInfo.dwPageSize != pageBytes' in body, 'Contiguous/page guard absent')
            require('VirtualProtect(original.code, pageBytes, PAGE_EXECUTE_READ' in body and 'VirtualProtect(original.block, pageBytes, PAGE_READONLY' in body, 'Separate RX/R protection absent')
            checks += 5
        require(not audit['files'][0]['rawBytesOutsideProveUnchangedClaimed'], 'EOL caveat lost')

        result = obj('allocation/fixture-r2/test-result.json')
        bind('allocation/fixture-r2/test-result.json', acceptance['fixtureResultSha256'])
        require(result['passed'] and len(result['modes']) == 2, 'Fixture modes missing')
        for mode in acceptance['modes']:
            basis = mode['basis']
            require(mode['buildExitCode'] == 0 and mode['actualExitCode'] == 0 and mode['naturalExit'], 'Fixture mode did not exit naturally')
            for suffix, key in (('.fixture.exe', 'fixtureSha256'), ('.stdout.log', 'stdoutSha256'), ('.stderr.log', 'stderrSha256'), ('.build.log', 'buildLogSha256'), ('.build.cmd', 'buildCommandSha256')):
                bind('allocation/fixture-r2/' + basis + suffix, mode[key])
            observed = json.loads(data('allocation/fixture-r2/' + basis + '.stdout.log').decode().strip())
            require(observed == mode['result'], 'Fixture stdout/acceptance mismatch')
            require(observed['passed'] and observed['failures'] == 0 and observed['checks'] == (801 if basis == 'production' else 802), 'Fixture check total differs')
            require(observed['provedOriginals'] == 28 and observed['lookupAndUnwindBoundariesPerSweep'] == 155 and observed['maximumMetadataBytes'] == 228, 'Fixture coverage differs')
        historical = obj('allocation/fixture-r1/test-result.json')
        require(historical['passed'] is False and b'Windows.h' in data('allocation/fixture-r1/production.build.log'), 'Historical SDK failure omitted')

        application = obj('root-application/application-record.json')
        bind('root-application/before/FirstInstallPlaybackAttestation.inl', application['beforeSha256'])
        bind('allocation/production/overlay/native/RizToolbox.JudgeContext/FirstInstallPlaybackAttestation.inl', application['afterSha256'])
        bind('allocation/fixture-acceptance.json', application['fixtureAcceptanceSha256'])
        for basis in ('root-production', 'tested-diagnostic'):
            prefix = 'builds/' + basis + '/'
            build = obj(prefix + 'result.json')
            native = obj(prefix + 'native/native-manifest.json')
            require(build['passed'] and build['actualBuildExitCode'] == 0 and build['naturalExit'], 'Actual build failed: ' + basis)
            bind(prefix + 'native/native-manifest.json', build['nativeManifestSha256'])
            bind(prefix + 'native/RizToolbox.JudgeContext.dll', build['helperSha256'])
            bind(prefix + 'native/judge-context-sources.zip', native['sourceArchive']['sha256'])
            bind(prefix + 'native/actual-export-validation.json', native['actualExportValidation']['sha256'])
            require(native['passed'] and native['inputHashesUnchanged'] and not native['fixtureExecuted'], 'Build scope differs')
            require((native['helperExportCount'], native['publicationVersion'], native['operationCount'], native['expectedWrites']) == (85, 7, 26, 64), 'Native ABI/profile facts differ')
            exports = export_names(data(prefix + 'native/RizToolbox.JudgeContext.dll'))
            require(exports == sorted(native['expectedExports']) and len(exports) == 85, 'Actual helper exports differ')
            with zipfile.ZipFile(io.BytesIO(data(prefix + 'native/judge-context-sources.zip'))) as sources:
                require(len(sources.namelist()) == len(native['sources']), 'Native source archive inventory differs')
                for row in native['sources']:
                    source = sources.read(row['relativePath'])
                    require(len(source) == row['bytes'] and digest(source) == row['sha256'], 'Archived native source mismatch')
                    checks += 1
                inl = sources.read('native/RizToolbox.JudgeContext/FirstInstallPlaybackAttestation.inl')
                expected = 'allocation/' + ('production' if basis == 'root-production' else 'diagnostic-v2') + '/overlay/native/RizToolbox.JudgeContext/FirstInstallPlaybackAttestation.inl'
                require(inl == data(expected), 'Build did not consume exact accepted overlay')
            if basis == 'root-production':
                bind('root-application/application-record.json', build['rootApplicationSha256'])
                require(build['actualRootInputsUnchanged'], 'Actual root build inputs changed')

        install = obj('runtime/install-r3/result.json')
        runtime = obj('runtime/runtime-r2/observation.json')
        bind('runtime/install-r3/result.json', runtime['installResultSha256'])
        bind('runtime/install-r3/package-manifest.json', install['packageManifestSha256'])
        bind('builds/tested-diagnostic/result.json', install['allocationHelperBuildResultSha256'])
        bind('allocation/fixture-acceptance.json', install['allocationFixtureAcceptanceSha256'])
        require(install['passed'] and install['installed'] and install['actualInstallerExitCode'] == 0 and install['naturalExit'] and install['verifiedPackageFiles'] == 23, 'Recorded installation did not succeed')
        helper_rows = [x for x in install['packageFiles'] if x['path'].endswith('/RizToolbox.JudgeContext.dll')]
        require(len(helper_rows) == 1, 'Installed helper inventory ambiguous')
        bind('builds/tested-diagnostic/native/RizToolbox.JudgeContext.dll', helper_rows[0]['sha256'])
        for name, sha in runtime['files'].items():
            bind('runtime/runtime-r2/' + name, sha)
        require(runtime['nativeProducerReady'] and runtime['originalIl2CppReturn'] == 1 and runtime['coreClrObserved'] and runtime['unityTickBound'] and runtime['hostInitialized'], 'Limited startup evidence incomplete')
        require(runtime['gameExitedNormally'] and runtime['testProcessIndependentlyAbsent'], 'Normal process absence not recorded')
        require(runtime['startupAndLimitedUiFlowAccepted'] and runtime['gameSdkStartupVerified'], 'Limited UI startup not accepted')
        require(not any(runtime[x] for x in ('fullGameAcceptance', 'gameRoundCompleted', 'normalResultVerified', 'exactBackCancellationVerified', 'sdkGoalComplete', 'completeSeekPermitted')), 'Runtime limitations were promoted')
        closure = obj('root-native-closure-acceptance.json')
        require(closure['rootNativeRepairAccepted'] and closure['rootFreshBuildAccepted'] and closure['bothExactProveFixturesAccepted'], 'Native closure incomplete')
        require(not closure['sdkGoalComplete'] and not closure['fullGameAcceptance'], 'Whole SDK completion fabricated')
        bind('builds/root-production/result.json', closure['rootBuildResultSha256'])
        bind('allocation/fixture-acceptance.json', closure['fixtureAcceptanceSha256'])
        excluded = ('gameassembly.dll', 'dobby.dll', 'global-metadata.dat', 'settings.json', 'bepinex.cfg')
        require(all(PurePosixPath(name).name.lower() not in excluded for name in names), 'Excluded content in archive')
        require(not any('/before/owned/' in name.lower() for name in names), 'Owned game backup copied')
    print(json.dumps(dict(schema='allocation-zip-only-check/v1', passed=True, archiveSha256=archive_sha, archiveBytes=len(raw), indexedMembers=len(index['files']), checks=checks, actualRootNativeBuildBound=True, bothExactProveFixturesBound=True, testedDiagnosticRuntimeBound=True, archivedProgramsExecuted=False, externalFilesRead=[str(args.archive)], sdkGoalComplete=False, fullGameAcceptance=False), ensure_ascii=False))


if __name__ == '__main__':
    main()
