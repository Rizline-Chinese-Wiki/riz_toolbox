"""Prepare a remaining-steps-only runner; preserve the original successful cases and collection failure."""
from pathlib import Path
import ast
import hashlib
import json

O = Path(__file__).resolve().parent
original = O / 'Run-FoundationDriverR7.py'
target = O / 'Complete-FoundationR7.py'
assert not target.exists()
body = original.read_text(encoding='utf-8')
prefix = body[:body.index('def main():')].replace("'foundation-driver.progress.json'", "'foundation-continuation.progress.json'")
remaining_start = body.index("        solution = run('solution-r7'")
remaining_end = body.index('    except BaseException:', remaining_start)
remaining = body[remaining_start:remaining_end].replace('Verify-ManagedSymbolsR7.ps1', 'Verify-ManagedSymbolsR7Continuation.ps1')
main = '''def main():
    assert not (O / 'foundation-continuation.result.json').exists(), 'Preserve earlier continuation terminals.'
    inside = wintypes.BOOL()
    assert K.IsProcessInJob(K.GetCurrentProcess(), None, ctypes.byref(inside))
    result = dict(kind='foundation-r7-remaining-build-continuation', passed=False,
        runnerPid=os.getpid(), parentPid=os.getppid(), command=[sys.executable, *sys.argv],
        creationTimeUtc=created(K.GetCurrentProcess()), inJob=bool(inside.value), startedAtUtc=now(), rows=[],
        driver=fact(Path(__file__).resolve()), gameExecuted=False, installed=False, downloaded=False,
        productionAdmission=False, currentMainModified=False, rootFreeCasesReexecuted=False,
        inheritedManagedMatrixReexecuted=False)
    save(O / 'foundation-continuation.launch.json', result)
    rows = []
    try:
        assert not inside.value
        initial_path = O / 'foundation-driver.result.json'
        initial = read(initial_path)
        assert initial['passed'] is False and len(initial['rows']) == 3
        assert [row['name'] for row in initial['rows']] == ['root-free-build-r7', 'tracker-resource-free-fault', 'root-free-symbols-r7']
        assert all(row['actualExitCode'] == 0 and row['passed'] is True for row in initial['rows'])
        assert 'FileNotFoundError' in initial['failure'] and 'symbols-rootfree-r7' in initial['failure']
        verify(initial['driver'])
        manifest = read(O / 'source-manifest.json')
        rows = manifest['files']
        assert len(rows) == 913 and check_source(S, rows) and check_source(B, rows) and check_source(LIVE, rows)
        assert PROFILE.is_file() and sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
        result.update(initialDriver=fact(initial_path), sourceManifest=fact(O / 'source-manifest.json'), legacyProfile=fact(PROFILE))
        tests = read(O / 'root-free-test-result.json')
        assert tests['passed'] is True and tests['actualCaseCount'] == 5 and tests['freshProcesses'] == 1 and tests['inheritedProcesses'] == 4
        assert tests['freshChecks'] == 33 and tests['inheritedChecks'] == 80 and tests['checks'] == 113
        result['completedAffectedCases'] = fact(O / 'root-free-test-result.json')
        symbol_path = O / 'symbols-rootfree-r6/test-result.json'
        symbol_proof = read(symbol_path)
        assert symbol_proof['passed'] is True and symbol_proof['stage'] == 'RootFree' and symbol_proof['pePdbPairs'] == 5 and symbol_proof['documentCount'] == 497
        verify(symbol_proof['verifier'])
        verify(symbol_proof['sourceManifest'])
        for image in symbol_proof['images']:
            verify(image['assembly']); verify(image['pdb'])
        result['completedRootFreeSymbolProof'] = fact(symbol_path)
        basis = O / 'basis'
        environment = O / 'environment'
        local_feed = environment / 'local-feed'
        seed = read(O / 'reference-package-seed-r7.json')
        assert seed['passed'] is True and seed['packageCount'] == 3
        for row in seed['files'] + seed['localFeed']:
            verify(row)
        env = os.environ.copy()
        env.update(PATH='C:/Program Files/dotnet;' + env.get('PATH', ''), DOTNET_CLI_HOME=str(environment / 'dotnet-home'),
            NUGET_PACKAGES=str(environment / 'packages'), NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
            DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en', MSBUILDDISABLENODEREUSE='1',
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', NuGetAudit='false')
'''
footer = '''    except BaseException:
        result['failure'] = traceback.format_exc()
        result['passed'] = False
    finally:
        result['frozenSourcesUnchanged'] = bool(rows) and check_source(S, rows)
        result['originSourcesUnchanged'] = bool(rows) and check_source(LIVE, rows)
        result['passed'] = result['passed'] and result['frozenSourcesUnchanged'] and result['originSourcesUnchanged']
        result['finishedAtUtc'] = now()
        (O / 'foundation-continuation.exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
        save(O / 'foundation-continuation.result.json', result)
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
'''
output = prefix + main + remaining + footer
ast.parse(output)
target.write_text(output, encoding='utf-8')
symbol_original = O / 'Verify-ManagedSymbolsR7.ps1'
symbol_target = O / 'Verify-ManagedSymbolsR7Continuation.ps1'
text = symbol_original.read_text(encoding='utf-8')
assert text.count("+'-r6')") == 1
symbol_target.write_text(text.replace("+'-r6')", "+'-r7')"), encoding='utf-8')
fact = lambda p: dict(path=str(p), sha256=hashlib.sha256(p.read_bytes()).hexdigest(), bytes=p.stat().st_size)
(O / 'continuation-preparation.json').write_text(json.dumps(dict(
    kind='foundation-r7-remaining-only-continuation-preparation', passed=True,
    originalRunner=fact(original), continuation=fact(target), originalSymbolVerifier=fact(symbol_original),
    correctedReleaseSymbolVerifier=fact(symbol_target), sourceManifest=fact(O / 'source-manifest.json'),
    frozenCandidateSourceModified=False, rootFreeTestsReexecuted=False, alreadyPassingCommandsReexecuted=False,
    remainingCommands=['solution-r7','sdk-r7','release-r7','release-symbols-r7']), indent=2), encoding='utf-8')
print('Prepared remaining-only continuation; original cases, verifier and failure retained.')
