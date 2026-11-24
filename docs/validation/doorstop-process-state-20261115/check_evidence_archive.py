"""Read a portable evidence ZIP only. Never extract, build, install, or execute it."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import sys
import zipfile


def require(condition, reason):
    if not condition:
        raise RuntimeError(reason)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def check(archive, expected_sha256=None):
    raw = Path(archive).read_bytes()
    archive_sha = digest(raw)
    if expected_sha256:
        require(archive_sha == expected_sha256.lower(), 'ZIP SHA-256 differs from the supplied receipt')
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        members = z.infolist()
        names = [m.filename for m in members]
        require(len(names) == len(set(names)) == len({n.casefold() for n in names}), 'Duplicate ZIP entry')
        require(len(names) < 400 and sum(m.file_size for m in members) < 20_000_000, 'Evidence ZIP exceeds its bounded scope')
        for member in members:
            path = PurePosixPath(member.filename)
            require(not member.is_dir() and not path.is_absolute() and '..' not in path.parts and '\\' not in member.filename and ':' not in member.filename, 'Unsafe ZIP entry')
        get = lambda name: z.read(name)
        read = lambda name: json.loads(get(name).decode('utf-8-sig'))
        h = lambda name: digest(get(name))
        index = read('hash-index.json')
        require(index['schema'] == 'doorstop-process-state-portable-index/v1', 'Unexpected index schema')
        rows = index['files']
        require(len(rows) == len({r['path'] for r in rows}), 'Duplicate index row')
        require(set(names) == {r['path'] for r in rows} | {'hash-index.json'}, 'ZIP/index membership differs')
        for row in rows:
            data = get(row['path'])
            require(len(data) == row['bytes'] and digest(data) == row['sha256'], 'Indexed file differs: ' + row['path'])
        app = read('root-application/application-record.json')
        accept = read('root-application/acceptance-r2.json')
        build = read('root-build/result.json')
        fixture = read('root-native-fixture-r2/result.json')
        header = read('header-fixture-r2/result.json')
        summary = read('summary.json')
        require(h('root-application/application-record.json') == '00afc1d808350feb23400a475896e678b8230d99dd72662952556715aa40890f', 'Application identity differs')
        require(h('root-application/acceptance-r1.json') == 'd86fda0caa4dc2cbf1169e80dd0a0bc1fb362a6f0bca08d1c806ac4c44869f47', 'Prior limited acceptance was changed')
        require(accept['schema'] == 'actual-root-native-process-state-acceptance/v2' and accept['nativeProcessStateFixAccepted'], 'r2 acceptance missing')
        require(accept['sourceApplicationSha256'] == h('root-application/application-record.json') == build['sourceApplicationSha256'] == fixture['sourceApplicationSha256'], 'Application links differ')
        require(accept['actualRootBuildResultSha256'] == fixture['nativeBuildResultSha256'] == h('root-build/result.json'), 'Root build links differ')
        require(accept['actualRootDllFixtureSha256'] == h('root-native-fixture-r2/result.json') and accept['headerFixtureResultSha256'] == h('header-fixture-r2/result.json'), 'Fixture acceptance links differ')
        for key in ('gameSdkVerified', 'sdkGoalComplete', 'completeSeekPermitted', 'foundationManagedMainApplied', 'diagnosticProducerOverlayApplied'):
            require(accept[key] is False, 'Native acceptance scope expanded: ' + key)
        require(accept['rootBuildTool'] == dict(session=70747, chunk='bf1141', exitCode=0), 'Root build tool identity differs')
        require(accept['fixtureTool'] == dict(chunk='390b0e', exitCode=0) and accept['headerFixtureTool'] == dict(chunk='15280c', exitCode=0), 'Fixture tool identities differ')
        require(build['passed'] and build['naturalExit'] and build['actualBuildExitCode'] == 0 and build['sourceInputsUnchanged'], 'Root build did not pass')
        for name, expected in build['inputs'].items():
            require(h('root-build/controls/' + name) == expected, 'Frozen Root source differs: ' + name)
        for name, expected in app['appliedFiles'].items():
            require(h('root-build/controls/' + name) == expected == accept['appliedFiles'][name], 'Applied source differs')
        require(h('root-application/source-before/Build-Doorstop.ps1') == app['oldBuildScriptSha256'], 'Before source differs')
        manifest = read('root-build/native/doorstop-manifest.json')
        require(h('root-build/native/doorstop-manifest.json') == build['nativeManifestSha256'], 'Native manifest differs')
        require({r['path']: r['sha256'] for r in manifest['files']} == build['products'], 'Manifest/product set differs')
        for name, expected in build['products'].items():
            require(h('root-build/native/' + name) == expected, 'Native product differs: ' + name)

        def check_steps(prefix, state):
            require(all(s['naturalExit'] and s['exitCode'] == 0 for s in state['steps']), 'Fixture step did not pass naturally')
            for step in state['steps']:
                require(h(prefix + '/' + step['id'] + '.log') == step['logSha256'], 'Fixture step log differs')

        prefix = 'root-native-fixture-r2'
        require(fixture['schema'] == 'doorstop-actual-proxy-fixture/v2' and fixture['passed'] and fixture['cases'] == accept['actualRootDllCases'] == 6, 'Root DLL six-case result differs')
        require(fixture['inputsUnchanged'] and fixture['rootNativeInputsUnchangedBeforeRun'] and fixture['rootNativeInputsUnchangedAfterRun'] and not fixture['gameExecuted'], 'Root fixture scope/stability differs')
        controller = fixture['actualController']
        require(controller == 'run_root_native_fixture_r2.py', 'Actual Root controller not frozen')
        require(fixture['actualControllerSha256'] == accept['actualControllerSha256'] == h(prefix + '/' + controller) == '4e6e185e102888e3adefdd1ed87347000c12d2516eef628b2b5e7104f213a1f7', 'Actual Root controller hash differs')
        for name, expected in fixture['inputs'].items():
            frozen_name = 'doorstop_config.initial.ini' if name == 'doorstop_config.ini' else name
            require(h(prefix + '/' + frozen_name) == expected, 'Root fixture frozen input differs: ' + name)
        require(h(prefix + '/doorstop_config.initial.ini') == fixture['initialFixtureIniSha256'] == accept['initialFixtureIniSha256'], 'Initial INI differs')
        require(h(prefix + '/doorstop_config.final.ini') == h(prefix + '/doorstop_config.ini') == fixture['finalFixtureIniSha256'] == accept['finalFixtureIniSha256'], 'Final INI differs')
        require(b'ignore_disable_switch=false' in get(prefix + '/doorstop_config.initial.ini') and b'ignore_disable_switch=true' in get(prefix + '/doorstop_config.final.ini'), 'Fixture INI transition differs')
        require(h(prefix + '/NativeProxyProcessFixture.exe') == fixture['fixtureExeSha256'], 'Root fixture executable differs')
        require(fixture['inputs']['winhttp.dll'] == fixture['inputs']['version.dll'] == build['products']['winhttp.dll'], 'Fixture DLL is not the Root product')
        check_steps(prefix, fixture)
        lines = get(prefix + '/actual-proxy-parent-child.log').decode('utf-8').splitlines()
        require(sum(line.startswith('PASS ') for line in lines) == 6 and any('fixture: 6/6;' in line for line in lines), 'Root six-case log differs')

        prefix = 'header-fixture-r2'
        require(header['passed'] and header['cases'] == accept['originalHeaderCases'] == 8 and header['inputsUnchanged'] and not header['gameExecuted'], 'Header eight-case result differs')
        for name, expected in header['inputs'].items():
            require(h(prefix + '/' + name) == expected, 'Header fixture input differs')
        require(h(prefix + '/header-c11.c') == header['generatedC11Sha256'] and h(prefix + '/ProcessEnvironmentFixture.exe') == header['fixtureExeSha256'], 'Header fixture products differ')
        require(header['inputs']['riz_process_environment.h'] == app['appliedFiles']['native/RizToolbox.Doorstop/riz_process_environment.h'], 'Header is not the applied source')
        check_steps(prefix, header)
        lines = get(prefix + '/real-parent-child.log').decode('utf-8').splitlines()
        require(sum(line.startswith('PASS ') for line in lines) == 8 and any('fixture: 8/8; 22 assertions;' in line for line in lines), 'Header eight-case log differs')
        with zipfile.ZipFile(io.BytesIO(get('root-build/native/doorstop-corresponding-source.zip'))) as source:
            require(digest(source.read('src/windows/riz_process_environment.h')) == app['appliedFiles']['native/RizToolbox.Doorstop/riz_process_environment.h'], 'LGPL corresponding header differs')
            require(b'RizDoorstopOwnsProcessMarker' in source.read('src/windows/config.c') and b'RizDoorstopMarkProcessMarker' in source.read('src/windows/entrypoint.c') and b'RIZ_DOORSTOP_CLR_PROCESS' in source.read('src/bootstrap.c'), 'Corresponding source misses process-state changes')
        old = read('limited/root-native-fixture-r1/result.json')
        require(old['passed'] and old['cases'] == 6 and h('limited/root-native-fixture-r1/result.json') == read('root-application/acceptance-r1.json')['actualRootDllFixtureSha256'], 'Preserved r1 linkage differs')
        require('run_native_proxy_fixture.py' in old['inputs'] and 'run_root_native_fixture.py' not in old['inputs'], 'Documented r1 controller mistake differs')
        failed = read('limited/c11-sdk-warning-r1/result.json')
        require(not failed['passed'] and failed['steps'][0]['exitCode'] == 2 and h('limited/c11-sdk-warning-r1/compile-c11.log') == failed['steps'][0]['logSha256'], 'C11 SDK warning failure evidence differs')
        diag = read('limited/diagnostic-crlf-r1/result.json')
        diag_receipt = read('limited/diagnostic-crlf-r1/failure-record.json')
        require(not diag['passed'] and [s['exitCode'] for s in diag['steps']] == [0, 1] and not diag['installed'] and not diag_receipt['functionalPass'], 'Diagnostic CRLF failure evidence differs')
        require(h('limited/diagnostic-crlf-r1/result.json') == diag_receipt['resultSha256'], 'Diagnostic failure receipt link differs')
        for attempt in ('runtime-r1', 'runtime-r2', 'runtime-r3'):
            observation = read('runtime/' + attempt + '/observation.json')
            files = observation.get('files', observation.get('logFiles'))
            for name, expected in files.items():
                require(h('runtime/' + attempt + '/' + name) == expected, 'Runtime raw evidence differs')
            require(not observation['sdkGoalComplete'] and not observation['completeSeekPermitted'], 'Runtime scope expanded')
        runtime3 = read('runtime/runtime-r3/observation.json')
        current_log = get('runtime/runtime-r3/toolbox-new-session.log').decode('utf-8-sig')
        require(runtime3['unityTickBoundObserved'] and not runtime3['currentSdkStartupVerified'] and runtime3['exitKind'] == 'owned-unresponsive-test-terminated', 'runtime-r3 limitation differs')
        require('Unity tick bound' in current_log and 'Module manifest or interval is invalid' in current_log, 'runtime-r3 tick/manifest failure missing')
        require(summary['nativeProcessStateFixAccepted'] and not summary['gameSdkVerified'] and not summary['sdkGoalComplete'] and not summary['completeSeekPermitted'], 'Summary scope differs')
        require(summary['actualRootDllCases'] == 6 and summary['headerCases'] == 8 and summary['runtime3UnityTickObserved'] is True, 'Summary matrix differs')
    return dict(schema='doorstop-process-state-archive-check/v1', passed=True, archiveSha256=archive_sha,
                indexSha256=digest(get_index_bytes(raw)), filesVerified=len(rows),
                rootNativeBuildInputs=len(build['inputs']), actualControllerFrozen=True,
                actualRootDllCases=6, headerCases=8, initialAndFinalFixtureIniPreserved=True,
                limitedAttemptsPreserved=True, runtime3UnityTickObserved=True,
                gameSdkVerified=False, sdkGoalComplete=False, completeSeekPermitted=False,
                executionScope='ZIP bytes and nested corresponding-source ZIP only; no extraction or process execution')


def get_index_bytes(raw):
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        return z.read('hash-index.json')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive')
    parser.add_argument('--expected-sha256')
    args = parser.parse_args()
    try:
        print(json.dumps(check(args.archive, args.expected_sha256), indent=2))
    except Exception as error:
        print(json.dumps(dict(passed=False, failure=str(error)), indent=2))
        sys.exit(1)
