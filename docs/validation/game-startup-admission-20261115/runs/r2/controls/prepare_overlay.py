"""Stage internal tick admission against accepted 1171 source; never edit root."""
import difflib
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BASIS = ROOT / 'docs/validation/foundation-current-integration-20261114'
SOURCE = BASIS / 'source'
sha = lambda path: hashlib.sha256(Path(path).read_bytes()).hexdigest()
read = lambda path: json.loads(Path(path).read_text(encoding='utf-8-sig'))


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def replace(text, old, new, count=1):
    require(text.count(old) == count, 'Accepted source seam differs: ' + old[:100])
    return text.replace(old, new)


def main():
    require(not (HERE / 'overlay').exists() and not (HERE / 'overlay-manifest.json').exists(),
        'Preserve staged overlay')
    summary = read(BASIS / 'validation-summary.json')
    accepted = read(BASIS / 'run/result.json')
    inputs = dict(accepted['fullFrozenInputs'])
    auxiliary = dict(accepted['auxiliaryInputs'])
    source_artifacts = {name.removeprefix('source/'): row['sha256'] for name, row in summary['artifacts'].items()
        if name.startswith('source/')}
    require(summary['passed'] and accepted['passed'] and accepted['fullFrozenInputsUnchanged']
        and summary['completeSolutionInputs'] == len(inputs) == 1171
        and source_artifacts == {**inputs, **auxiliary}
        and all(sha(SOURCE / name) == digest for name, digest in source_artifacts.items()),
        'Accepted complete source snapshot differs')
    runtime_path = 'src/RizToolbox.Runtime/RuntimeBridge.cs'
    bridge_path = 'src/RizToolbox.Runtime/Hooks/NativeRuntimeInvokeBridge.cs'
    tests_path = 'tests/RizToolbox.Runtime.Tests/NativeFirstInstallMappedRegression.cs'
    program_path = 'tests/RizToolbox.Runtime.Tests/Program.cs'
    bodies = {name: (SOURCE / name).read_text(encoding='utf-8')
        for name in (runtime_path, bridge_path, tests_path, program_path)}

    runtime = bodies[runtime_path]
    runtime = replace(runtime, '    private readonly Action<NativeRuntime> _tick;',
        '    private readonly Action<NativeRuntime> _tick;\n    private readonly Func<IntPtr, bool> _matchesTick;')
    runtime = replace(runtime,
        '    { _namespace = ns; _type = type; _method = method; _tick = tick; _log = log; }',
        '''    {
        _namespace = ns; _type = type; _method = method; _tick = tick; _log = log;
        _matchesTick = candidate => _api!.IsMethod(candidate, _namespace, _type, _method);
    }''')
    runtime = replace(runtime,
        '            _transport = NativeRuntimeInvokeBridge.Adopt(producer, OnAfterInvoke, _log);',
        '            _transport = NativeRuntimeInvokeBridge.Adopt(producer, this, _log);')
    runtime = replace(runtime, '    private void OnAfterInvoke(NativeRuntimeInvokeFrame frame)',
        '''    // This fixed internal consumer only selects completed native calls. It
    // cannot bind a thread/method, call the host, or confer readiness. Before
    // first binding IsMethod reads method/class names and namespace through
    // cached IL2CPP metadata delegates, before the complete publication proof.
    internal bool AcceptsTick(NativeRuntimeInvokeFrame frame)
        => IsTickCandidate(frame.Method, Volatile.Read(ref _tickMethod), Volatile.Read(ref _entered), _matchesTick);

    internal static bool IsTickCandidate(IntPtr method, IntPtr tickMethod, int entered, Func<IntPtr, bool> matchesTick)
        => entered == 0 && (tickMethod != IntPtr.Zero ? tickMethod == method : matchesTick(method));

    // Only the concrete native bridge calls this after its frame/channel checks,
    // this consumer's acceptance predicate, the complete current proof, and a
    // second native channel identity/in-flight check.
    internal void OnProvenAfterInvoke(NativeRuntimeInvokeFrame frame)''')
    runtime = replace(runtime,
        '                if (!_api!.IsMethod(method, _namespace, _type, _method)) return;\n', '')
    bodies[runtime_path] = runtime

    bridge = bodies[bridge_path]
    bridge = replace(bridge,
        '    private readonly Action<NativeRuntimeInvokeFrame> _after;\n    private readonly ILogger _log;\n    private int _unknown;',
        '    private readonly AfterAdmission _admission;')
    bridge = replace(bridge,
        '    private NativeRuntimeInvokeBridge(NativeFirstInstall producer, Action<NativeRuntimeInvokeFrame> after, ILogger log)\n'
        '    {\n        _producer = producer; _after = after; _log = log;',
        '    private NativeRuntimeInvokeBridge(NativeFirstInstall producer, RuntimeBridge consumer, ILogger log)\n'
        '    {\n        _producer = producer;')
    bridge = replace(bridge,
        '            throw new UnsupportedSignatureException("Runtime-invoke does not match the actual unattached pre-init producer receipt.");\n    }',
        '''            throw new UnsupportedSignatureException("Runtime-invoke does not match the actual unattached pre-init producer receipt.");
        _admission = new AfterAdmission(producer.RuntimeTarget, producer.RuntimeOriginal, producer.RuntimeEntry, producer.RuntimeEpoch,
            (ref NativeRuntimeInvokeInformation value) => _info(ref value), consumer.AcceptsTick, _producer.VerifyReady,
            consumer.OnProvenAfterInvoke, error => {
                try { log.Log(LogLevel.Error, "Runtime-invoke After observer failed; native result is preserved.", error); } catch { }
            });
    }''')
    bridge = replace(bridge,
        '    internal static NativeRuntimeInvokeBridge Adopt(NativeFirstInstall producer, Action<NativeRuntimeInvokeFrame> after, ILogger log)',
        '    internal static NativeRuntimeInvokeBridge Adopt(NativeFirstInstall producer, RuntimeBridge consumer, ILogger log)')
    bridge = replace(bridge,
        '        ArgumentNullException.ThrowIfNull(producer); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(log);',
        '        ArgumentNullException.ThrowIfNull(producer); ArgumentNullException.ThrowIfNull(consumer); ArgumentNullException.ThrowIfNull(log);')
    bridge = replace(bridge,
        '            var bridge = new NativeRuntimeInvokeBridge(producer, after, log);',
        '            var bridge = new NativeRuntimeInvokeBridge(producer, consumer, log);')
    bridge = replace(bridge,
        '            catch { Interlocked.Exchange(ref bridge._unknown, 1); throw; }',
        '            catch { bridge._admission.MarkUnknown(); throw; }')
    start = bridge.index('    private unsafe void After(')
    end = bridge.index('    [DllImport("kernel32.dll")]', start)
    bridge = bridge[:start] + '''    // Value-only admission is shared with controlled tests. It cannot load a
    // producer, attach an observer, call an original, or issue a Ready owner.
    internal delegate uint ReadInformation(ref NativeRuntimeInvokeInformation info);
    internal sealed class AfterAdmission
    {
        private readonly IntPtr _target, _original, _entry;
        private readonly ulong _epoch;
        private readonly ReadInformation _read;
        private readonly Func<NativeRuntimeInvokeFrame, bool> _accepts;
        private readonly Action _verifyReady;
        private readonly Action<NativeRuntimeInvokeFrame> _after;
        private readonly Action<Exception> _fault;
        private int _unknown;

        internal AfterAdmission(IntPtr target, IntPtr original, IntPtr entry, ulong epoch, ReadInformation read,
            Func<NativeRuntimeInvokeFrame, bool> accepts, Action verifyReady,
            Action<NativeRuntimeInvokeFrame> after, Action<Exception> fault)
        {
            if (target == IntPtr.Zero || original == IntPtr.Zero || entry == IntPtr.Zero || epoch == 0)
                throw new ArgumentException("Runtime-invoke admission requires the retained native channel identity.");
            ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(accepts);
            ArgumentNullException.ThrowIfNull(verifyReady); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(fault);
            _target = target; _original = original; _entry = entry; _epoch = epoch;
            _read = read; _accepts = accepts; _verifyReady = verifyReady; _after = after; _fault = fault;
        }
        internal bool Unknown => Volatile.Read(ref _unknown) != 0;
        internal void MarkUnknown() => Interlocked.Exchange(ref _unknown, 1);
        internal void Reject(Exception error)
        {
            MarkUnknown();
            try { _fault(error); } catch { /* No diagnostic exception crosses the native observer. */ }
        }
        internal void Observe(NativeRuntimeInvokeFrame frame, uint currentThreadId)
        {
            if (Unknown) return;
            try
            {
                if (!frame.IsValid || frame.OsThreadId != currentThreadId)
                    throw new UnsupportedSignatureException("Runtime-invoke After frame differs from its native calling thread.");
                var info = NewInformation();
                if (_read(ref info) != 0 || !MatchesInformation(info, _target, _original, _entry, _epoch, true)
                    || info.InFlight == 0)
                    throw new UnsupportedSignatureException("Runtime-invoke After has no known active permanent native entry.");
                // Ignored native calls receive no SDK observation. Every accepted
                // tick still proves the entire current publication before dispatch;
                // there is no proof cache, grace interval or caller-issued Ready bit.
                if (!_accepts(frame)) return;
                _verifyReady();
                // Keep the original later-timepoint native channel check too:
                // proof execution cannot lend a changed/unknown entry authority.
                info = NewInformation();
                if (_read(ref info) != 0 || !MatchesInformation(info, _target, _original, _entry, _epoch, true)
                    || info.InFlight == 0)
                    throw new UnsupportedSignatureException("Runtime-invoke After has no known active permanent native entry.");
                if (!Unknown) _after(frame);
            }
            catch (Exception error) { Reject(error); }
        }
    }
    private unsafe void After(IntPtr pointer, IntPtr user)
    {
        try
        {
            if (_admission.Unknown) return;
            if (pointer == IntPtr.Zero || user != IntPtr.Zero)
                throw new UnsupportedSignatureException("Runtime-invoke After frame or user is invalid.");
            _admission.Observe(*(NativeRuntimeInvokeFrame*)pointer, GetCurrentThreadId());
        }
        catch (Exception error) { _admission.Reject(error); }
    }
''' + bridge[end:]
    bodies[bridge_path] = bridge

    tests = bodies[tests_path]
    tests = replace(tests,
        '                var runtime = NativeRuntimeInvokeBridge.Adopt(producer, _ => counters.RuntimeAfter++, logger); Owners.Add(runtime);',
        '''                var consumer = new RuntimeBridge(dobbyPath, "mapped.fixture", "NeverInvoked", "Tick", _ => counters.RuntimeAfter++, logger);
                Owners.Add(consumer);
                var runtime = NativeRuntimeInvokeBridge.Adopt(producer, consumer, logger); Owners.Add(runtime);''')
    tests = replace(tests,
        'NativeRuntimeInvokeBridge.Adopt(producer, _ => counters.RuntimeAfter++, logger)',
        'NativeRuntimeInvokeBridge.Adopt(producer, consumer, logger)')
    test_source = (HERE / 'tick-admission-tests.cs.txt').read_text(encoding='utf-8')
    tests = replace(tests,
        '    internal const string ModeArgument = "--first-install-mapped-adoption";',
        test_source + '\n    internal const string ModeArgument = "--first-install-mapped-adoption";')
    bodies[tests_path] = tests
    program = bodies[program_path]
    program = replace(program,
        'if (args.Length > 0 && args[0] == NativeFirstInstallMappedRegression.ModeArgument)',
        '''if (args.Length > 0 && args[0] == NativeFirstInstallMappedRegression.TickAdmissionModeArgument)
    return NativeFirstInstallMappedRegression.RunTickAdmissionChecks();
if (args.Length > 0 && args[0] == NativeFirstInstallMappedRegression.ModeArgument)''')
    program = replace(program,
        '    .Concat(UnityObjectBackingRegression.Cases).ToArray();',
        '    .Concat(UnityObjectBackingRegression.Cases).Concat(NativeFirstInstallMappedRegression.TickAdmissionCases).ToArray();')
    bodies[program_path] = program

    changed = []
    for name, body in bodies.items():
        target = HERE / 'overlay' / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('x', encoding='utf-8', newline='\n') as stream:
            stream.write(body)
        original = (SOURCE / name).read_text(encoding='utf-8')
        patch = ''.join(difflib.unified_diff(original.splitlines(True), body.splitlines(True),
            fromfile='accepted1171/' + name, tofile='tick-admission/' + name))
        patch_path = HERE / (Path(name).name + '.patch')
        with patch_path.open('x', encoding='utf-8', newline='\n') as stream:
            stream.write(patch)
        changed.append(dict(targetPath=name, overlayPath='overlay/' + name, baseSha256=inputs[name],
            sha256=sha(target), bytes=target.stat().st_size, patchSha256=sha(patch_path)))
    expected = dict(inputs)
    expected.update({row['targetPath']: row['sha256'] for row in changed})
    require(len(expected) == 1171, 'Compiler input count changed')
    root1160 = read(ROOT / 'work/line-producer-control-next-20261114/main-r5-r4-interruption.json')
    root_inputs = root1160['completeInputs']
    require(len(root_inputs) == 1160 and all(sha(ROOT / name) == digest for name, digest in root_inputs.items())
        and sha(ROOT / 'README.md') == root1160['readmeSha256'], 'Frozen root1160 or README changed')
    build_path = ROOT / 'scripts/Build.ps1'
    build_hash = root1160['originalPinnedControls'][str(build_path)]
    require(sha(build_path) == build_hash, 'Frozen root Build.ps1 changed')
    require(len(changed) == 4 and len(set(row['targetPath'] for row in changed)) == 4,
        'Overlay must change exactly four existing compiler inputs')
    manifest = dict(schema='runtime-tick-admission-overlay/v1', prepared=True, ready=True, frozen=True,
        compiled=False, testsExecuted=False,
        basis=str(BASIS), basisSummarySha256=sha(BASIS / 'validation-summary.json'),
        basisResultSha256=sha(BASIS / 'run/result.json'),
        compilerInputs=1171, changedExistingInputs=len(changed), addedCompilerInputs=0,
        baseInputs=inputs, expectedInputs=expected, auxiliaryInputs=auxiliary, files=changed,
        scopedCases=8, controlledMode='--runtime-tick-admission',
        expectedFocusedCases=8, testSelector='--runtime-tick-admission',
        fixedInternalConsumer=True, frameAndChannelCheckedBeforeFilter=True,
        fullProofBeforeEveryAcceptedDispatch=True, proofCache=False, graceInterval=False,
        relevantDispatchContractPreserved=True,
        contractChange='Irrelevant/reentrant native calls no longer run the full publication proof; pre-bind selection only calls cached metadata getters.',
        preBindMetadataContract=dict(
            beforeFullProof=True, readOnly=True,
            getters=['il2cpp_method_get_name', 'il2cpp_method_get_class', 'il2cpp_class_get_name', 'il2cpp_class_get_namespace'],
            delegatesResolvedDuringRuntimeConstruction=True,
            description='IsMethod reads UTF-8 method/class/namespace names using cached native delegates. It does not bind a method or thread, invoke runtime_invoke/game code, call the host, attach/create a producer, or confer Ready authority.'),
        admissionOrder=['validate-current-frame-and-thread', 'read-current-native-channel',
            'concrete-runtime-consumer-selection', 'unconditional-full-current-VerifyReady',
            'reread-current-native-channel-and-in-flight', 'proven-consumer-thread-binding-and-host-dispatch'],
        sourceReview=dict(scope='four existing C# compiler inputs',
            productionAdoptionConsumer='concrete internal RuntimeBridge; no public/plugin filter',
            testSeam='nested value-only admission pipeline; cannot attach/create a native producer or issue Ready',
            ignoredAndReentrant='one cheap channel read; zero full proofs or SDK dispatches',
            accepted='fresh full proof and second identity/in-flight check for each accepted completion',
            failures='sticky unknown; observer and diagnostic exceptions cannot escape to native',
            nativeCompletion='copied by value; native original/result/exception path unchanged'),
        root1160Unchanged=True, rootReadmeUnchanged=True, rootBuildScriptChanged=False,
        rootReadmeSha256=root1160['readmeSha256'], rootBuildScriptSha256=build_hash,
        rootSourcesEdited=False, nativeSourcesChanged=False, nativeExportsChanged=False,
        gameExecuted=False, installed=False, sdkGoalComplete=False, completeSeekPermitted=False)
    with (HERE / 'overlay-manifest.json').open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
        stream.write('\n')
    print(json.dumps(dict(prepared=True, compiled=False, testsExecuted=False, compilerInputs=1171,
        changedExistingInputs=4, scopedCases=8, manifestSha256=sha(HERE / 'overlay-manifest.json'), files=changed)), flush=True)


if __name__ == '__main__':
    main()
