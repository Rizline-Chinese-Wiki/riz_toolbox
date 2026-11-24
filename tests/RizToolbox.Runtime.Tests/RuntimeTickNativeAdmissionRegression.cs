using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

/// <summary>Isolated real native permanent channel -> production managed admission.
/// Owned leaf stubs replace the game original; no game, IL2CPP initializer or detour
/// is executed. Native SEH stays in the native observer fault boundary.</summary>
internal static class RuntimeTickNativeAdmissionRegression
{
    internal const string ModeArgument = "--runtime-tick-native-admission";
    private static readonly List<object> Roots = new();
    [StructLayout(LayoutKind.Sequential)] private struct Configuration
    { internal uint Version, Size; internal IntPtr Target, Original; }
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr After, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConfigureNative(ref Configuration value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(ulong epoch, ref Observer value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AfterNative(IntPtr frame, IntPtr user);

    internal static int RunChild(string[] args)
    {
        var checks = 0;
        void Check(bool condition, string message)
        { ++checks; if (!condition) throw new InvalidOperationException(message); }
        var mode = args.Length > 1 ? args[1] : "missing";
        try
        {
            if (args.Length != 3 || mode is not ("standard" or "developer" or "foreign-thread" or "native-unknown"))
                throw new ArgumentException("Usage: --runtime-tick-native-admission standard|developer|foreign-thread|native-unknown absolute-helper.dll");
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Windows x64 is required.");
            var helperPath = Path.GetFullPath(args[2]);
            Check(Path.IsPathFullyQualified(args[2]) && File.Exists(helperPath), "An existing absolute native helper is required.");
            var helper = NativeLibrary.Load(helperPath); Roots.Add(helper);
            T Export<T>(string name) where T : Delegate
                => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(helper, name));
            var read = Export<NativeRuntimeInvokeBridge.ReadInformation>("RizRuntimeInvokeContext_GetInfo");
            var configure = Export<ConfigureNative>("RizRuntimeInvokeContext_Configure");
            var attach = Export<AttachNative>("RizRuntimeInvokeContext_Attach");
            NativeRuntimeInvokeInformation Read(uint expectedStatus = 0)
            {
                var value = new NativeRuntimeInvokeInformation { Version = 1, Size = 80 };
                Check(read(ref value) == expectedStatus, "The real native runtime channel did not return its expected state.");
                return value;
            }
            var initial = Read(2);
            Check(initial.Entry != IntPtr.Zero && initial.Epoch == 0 && initial.Attached == 0 && initial.Unknown == 0,
                "A native fixture requires a fresh independent helper process.");

            // Win64 leaf: increment native *self once; copy arguments[0] to the
            // exception output when present; return *method. No CLR original.
            var original = Executable(new byte[] {
                0xf0, 0x48, 0xff, 0x02, 0x4d, 0x85, 0xc9, 0x74, 0x06,
                0x4d, 0x8b, 0x10, 0x4d, 0x89, 0x11, 0x48, 0x8b, 0x01, 0xc3
            });
            var config = new Configuration { Version = 1, Size = 24, Target = original, Original = original };
            Check(configure(ref config) == 0, "The owned native original was not configured.");
            var configured = Read();
            Check(configured.Attached == 0 && configured.Epoch != 0, "Configuration did not publish an unattached channel with its own epoch.");
            var invoke = Marshal.GetDelegateForFunctionPointer<NativeRuntime.RuntimeInvoke>(configured.Entry);
            Roots.Add(invoke);
            var tick = Data(16); var other = Data(16); var counter = Data(8); var arguments = Data(8); var exception = Data(8);
            var result = new IntPtr(0x123456789abcdef);
            Marshal.WriteIntPtr(tick, result); Marshal.WriteIntPtr(other, result);
            var fullAudit = mode == "developer";
            var audits = 0; var dispatches = 0; var faults = 0; var clocks = 0; var channelReads = 0;
            var recurse = false; var measuringThrows = true;
            Exception? lastFault = null;
            var sequences = new List<long>(); var measurements = new List<RuntimeEntryMeasurement>();

            // Exercise the installed-audit relay check against a real owned RX page.
            // No game publication or producer receipt is created by this fixture.
            var knownRelay = new byte[14]; knownRelay[0] = 0xff; knownRelay[1] = 0x25;
            BitConverter.GetBytes(configured.Entry.ToInt64()).CopyTo(knownRelay, 6);
            var relay = Executable(knownRelay);
            var verifyRelay = typeof(NativeFirstInstall).GetMethod("VerifyRelay", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Action<IntPtr, IntPtr>>();
            verifyRelay(relay, configured.Entry);

            var admission = new NativeRuntimeInvokeBridge.AfterAdmission(configured.Target, configured.Original, configured.Entry, configured.Epoch,
                (ref NativeRuntimeInvokeInformation info) => { ++channelReads; return read(ref info); },
                method => method == tick, () => { ++audits; verifyRelay(relay, configured.Entry); },
                (frame, sequence) => {
                    ++dispatches; sequences.Add(sequence);
                    Check(frame.Self == counter && frame.Arguments == arguments && frame.Exception == exception && frame.Result == result
                        && frame.OsThreadId == GetCurrentThreadId(), "The native completion changed full pointer arguments, result or thread.");
                    var during = Read();
                    Check(during.InFlight == 1 && during.Entered == during.Exited + 1, "The admitted real native call lost its in-flight exit responsibility.");
                    if (recurse)
                    {
                        recurse = false;
                        Check(invoke(tick, counter, arguments, exception) == result, "The recursive original return changed.");
                    }
                }, error => { ++faults; lastFault = error; }, new(fullAudit, fullAudit), measure: sample => {
                    measurements.Add(sample);
                    if (measuringThrows) throw new InvalidOperationException("controlled measurement sink fault");
                }, timestamp: () => { ++clocks; return Stopwatch.GetTimestamp(); });
            Roots.Add(admission);
            AfterNative after = admission.ObserveNative;
            Roots.Add(after);
            // Win64 observer leaf: method[8] != 0 raises native divide-by-zero;
            // otherwise tail-call the rooted CLR observer. The native transport
            // catches that real SEH and publishes sticky Unknown itself.
            var observerBytes = new List<byte> { 0x48, 0x8b, 0x41, 0x08, 0x80, 0x78, 0x08, 0x00, 0x74, 0x05, 0x31, 0xc0, 0xf7, 0xf0, 0xc3, 0x48, 0xb8 };
            observerBytes.AddRange(BitConverter.GetBytes(Marshal.GetFunctionPointerForDelegate(after).ToInt64()));
            observerBytes.AddRange(new byte[] { 0xff, 0xe0 });
            var observer = new Observer { Version = 1, Size = 24, After = Executable(observerBytes.ToArray()) };

            var originalCalls = 0;
            void VerifyDrained()
            {
                var current = Read();
                Check(Marshal.ReadInt64(counter) == originalCalls && current.Entered == (ulong)originalCalls
                    && current.Exited == (ulong)originalCalls && current.InFlight == 0,
                    "The native original did not execute exactly once or its finally failed to drain.");
            }
            void Invoke(IntPtr method, int nativeCalls = 1)
            {
                Check(invoke(method, counter, arguments, exception) == result, "The original pointer result changed across managed admission.");
                originalCalls += nativeCalls;
                VerifyDrained();
            }
            void InvokeOnWorker()
            {
                Exception? workerError = null;
                var worker = new Thread(() => { try { Invoke(tick); } catch (Exception error) { workerError = error; } });
                worker.Start(); Check(worker.Join(10000), "The owned foreign-thread call did not complete.");
                if (workerError is not null) throw workerError;
            }
            var staleEpoch = configured.Epoch ^ 2ul;
            Check(staleEpoch != 0 && attach(staleEpoch, ref observer) == 3,
                "The native producer accepted an observer configured for a stale epoch.");
            Invoke(tick);
            Check(Read().Attached == 0 && channelReads == 0 && dispatches == 0,
                "The unattached native entry routed an original call to CLR.");
            Check(attach(configured.Epoch, ref observer) == 0 && Read().Attached == 1, "The current native epoch could not attach its observer.");
            Roots.Add(observer);

            Invoke(other);
            Check(dispatches == 0 && audits == 0 && measurements.Count == 0,
                "An unrelated boot invocation dispatched host work, an audit or a sample.");
            Marshal.WriteIntPtr(arguments, new(0x456));
            InvokeOnWorker();
            Check(dispatches == 0 && audits == 0 && measurements.Single().Outcome == RuntimeEntryOutcome.AbnormalReturn
                && Marshal.ReadIntPtr(exception) == new IntPtr(0x456),
                "The abnormal return on the first matching native call changed its exception or bound the wrong thread.");
            Marshal.WriteIntPtr(arguments, IntPtr.Zero);
            recurse = true; Invoke(tick, 2);
            Invoke(tick);
            Check(dispatches == 2 && sequences.SequenceEqual(new long[] { 1, 2 }) && faults == 0 && !admission.Unknown
                && audits == (fullAudit ? 2 : 0), "Mode, recursion, monotonic dispatch or throwing measurement changed admission.");
            measuringThrows = false;

            long? ignoredAllocatedBytes = null;
            if (mode == "standard")
            {
                void InvokeIgnoredBatch(int count)
                {
                    for (var index = 0; index < count; ++index)
                        if (invoke(other, counter, arguments, exception) != result)
                            throw new InvalidOperationException("The ignored invocation changed its native result.");
                    originalCalls += count;
                }
                InvokeIgnoredBatch(256);
                var prior = (channelReads, clocks, measurements.Count, dispatches, audits);
                var before = GC.GetAllocatedBytesForCurrentThread();
                InvokeIgnoredBatch(4096);
                ignoredAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                VerifyDrained();
                Check(ignoredAllocatedBytes == 0 && prior == (channelReads, clocks, measurements.Count, dispatches, audits),
                    "Bound unrelated native invocations consumed managed allocations, timing or channel checks.");
            }

            var ownedRelayMutationVerified = false;
            if (mode is "standard" or "developer")
            {
                var changedRelay = (byte[])knownRelay.Clone();
                BitConverter.GetBytes(original.ToInt64()).CopyTo(changedRelay, 6);
                PublishCode(relay, changedRelay);
                try
                {
                    try { verifyRelay(relay, configured.Entry); }
                    catch (UnsupportedSignatureException) { ownedRelayMutationVerified = true; }
                    Check(ownedRelayMutationVerified, "The owned changed relay did not fail the actual publication subcheck.");
                    Invoke(tick);
                    if (fullAudit)
                        Check(audits == 3 && dispatches == 2 && faults == 1 && admission.Unknown
                            && lastFault is UnsupportedSignatureException && Read().Unknown == 0,
                            "Developer audit did not reject the actual changed relay while preserving its native completion.");
                    else
                        Check(audits == 0 && dispatches == 3 && faults == 0 && !admission.Unknown,
                            "Standard admission entered the independent publication audit despite a current runtime channel.");
                }
                finally { PublishCode(relay, knownRelay); }
                Invoke(tick);
                Check(dispatches == (fullAudit ? 2 : 4) && audits == (fullAudit ? 3 : 0),
                    "Restoring relay bytes revived a rejected Developer bridge or disrupted Standard admission.");
            }
            if (mode == "foreign-thread")
            {
                InvokeOnWorker();
                Check(admission.Unknown && faults == 1 && dispatches == 2 && Read().Unknown == 0,
                    "A real foreign-thread Tick was accepted or confused with native channel Unknown.");
                Invoke(tick);
                Check(dispatches == 2, "The original thread revived the rejected bridge.");
            }
            else if (mode == "native-unknown")
            {
                Marshal.WriteByte(tick, 8, 1); Invoke(tick); Marshal.WriteByte(tick, 8, 0);
                Check(Read().Unknown == 1 && Read().ObserverFaults == 1 && dispatches == 2,
                    "A real native observer SEH did not retain the original result and sticky native Unknown.");
                Invoke(tick);
                Check(admission.Unknown && faults == 1 && dispatches == 2, "Actual native Unknown reached the host.");
                Invoke(tick);
                Check(dispatches == 2 && Read().Unknown == 1, "A later normal native completion revived sticky Unknown.");
            }
            var final = Read();
            Check(measurements.Where(sample => sample.HostTickSequence > 0).All(sample => sample.TotalMilliseconds >= sample.HostMilliseconds
                && sample.TotalMilliseconds >= sample.AdmissionMilliseconds), "The complete managed entry was not measured.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "runtime-tick-native-admission", mode, passed = true, checks, failures = 0,
                productionAdmissionUsed = true, nativeOriginalOnce = true, nativeChannelConfigured = true,
                originalBeforeAttachPreserved = true, staleNativeAttachRejected = true, abnormalFirstTickOnWorker = true,
                originalCalls, dispatches, audits, faults, nativeUnknown = final.Unknown, observerFaults = final.ObserverFaults,
                entered = final.Entered, exited = final.Exited, inFlight = final.InFlight,
                ownedRelayMutationVerified, ignoredAllocatedBytes,
                gameRuntimeVerified = false, fullPublicationProofVerified = false, gameCodeExecuted = false,
                helperPath
            }));
            // The native permanent channel still owns original/callback roots.
            // Keep all pages/delegates/library loaded until this child exits.
            GC.KeepAlive(Roots);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "runtime-tick-native-admission", mode,
                passed = false, checks, failures = 1, failure = error.Message, gameRuntimeVerified = false }));
            return 1;
        }
    }

    private static IntPtr Data(int size)
    {
        var pointer = Marshal.AllocHGlobal(size);
        Marshal.Copy(new byte[size], 0, pointer, size); Roots.Add(pointer); return pointer;
    }
    private static IntPtr Executable(byte[] bytes)
    {
        var pointer = VirtualAlloc(IntPtr.Zero, 4096, 0x3000, 0x04);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Owned native leaf allocation failed.");
        PublishCode(pointer, bytes);
        Roots.Add(pointer); return pointer;
    }
    private static void PublishCode(IntPtr pointer, byte[] bytes)
    {
        if (!VirtualProtect(pointer, 4096, 0x04, out _))
            throw new InvalidOperationException("Owned native fixture page could not be staged for a write.");
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        if (!VirtualProtect(pointer, 4096, 0x20, out _) || !FlushInstructionCache(GetCurrentProcess(), pointer, (nuint)bytes.Length))
            throw new InvalidOperationException("Owned native leaf publication failed.");
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr address, nuint bytes, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, nuint bytes, uint protection, out uint oldProtection);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint bytes);
}
