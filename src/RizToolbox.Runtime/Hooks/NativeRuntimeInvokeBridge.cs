using System.Diagnostics;
using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRuntimeInvokeFrame
{
    internal uint Version, Size;
    internal IntPtr Method, Self, Arguments, Exception, Result, Caller;
    internal uint OsThreadId, Reserved;
    internal bool IsValid => Version == 1 && Size == 64 && Method != IntPtr.Zero && Caller != IntPtr.Zero && OsThreadId != 0 && Reserved == 0;
    internal bool CompletedNormally => Exception == IntPtr.Zero || Marshal.ReadIntPtr(Exception) == IntPtr.Zero;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRuntimeInvokeInformation
{
    internal uint Version, Size, Attached, Unknown;
    internal IntPtr Target, Original, Entry;
    internal ulong Epoch, Entered, Exited, InFlight, ObserverFaults;
}

/// <summary>Attaches one read-only After observer to the runtime-invoke entry
/// already installed by the native pre-init producer. The original, result and
/// native exception propagation stay entirely in the permanent native channel.</summary>
internal sealed class NativeRuntimeInvokeBridge
{
    private static readonly object Gate = new();
    private static readonly List<NativeRuntimeInvokeBridge> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr After, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(ref NativeRuntimeInvokeInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AfterNative(IntPtr frame, IntPtr user);
    private readonly NativeFirstInstall _producer;
    private readonly IntPtr _library;
    private readonly InfoNative _info;
    private readonly AttachNative _attach;
    private readonly AfterNative _afterNative;
    private readonly AfterAdmission _admission;
    private NativeRuntimeInvokeBridge(NativeFirstInstall producer, RuntimeBridge consumer, ILogger log)
    {
        _producer = producer;
        producer.VerifyReady();
        _library = NativeLibrary.Load(producer.HelperPath);
        _info = Export<InfoNative>("RizRuntimeInvokeContext_GetInfo");
        _attach = Export<AttachNative>("RizRuntimeInvokeContext_Attach");
        if (Marshal.SizeOf<NativeRuntimeInvokeFrame>() != 64 || Marshal.SizeOf<NativeRuntimeInvokeInformation>() != 80 || Marshal.SizeOf<Observer>() != 24)
            throw new UnsupportedSignatureException("Native runtime-invoke transport layout differs from version 1.");
        var info = NewInformation();
        if (_info(ref info) != 0 || !MatchesInformation(info, producer.RuntimeTarget, producer.RuntimeOriginal, producer.RuntimeEntry, producer.RuntimeEpoch, false))
            throw new UnsupportedSignatureException("Runtime-invoke does not match the actual unattached pre-init producer receipt.");
        _admission = new AfterAdmission(producer.RuntimeTarget, producer.RuntimeOriginal, producer.RuntimeEntry, producer.RuntimeEpoch,
            (ref NativeRuntimeInvokeInformation value) => _info(ref value), consumer.MatchesTick, _producer.VerifyReady,
            consumer.OnAdmittedAfterInvoke, error => {
                try { log.Log(LogLevel.Error, "Runtime-invoke After observer failed; native result is preserved.", error); } catch { }
                consumer.ReportAdmissionFailure(error);
            }, consumer.ExecutionOptions, _producer.VerifyRuntimeChannelOwnership, consumer.MeasureEntry);
        _afterNative = _admission.ObserveNative;
    }
    internal static NativeRuntimeInvokeBridge Adopt(NativeFirstInstall producer, RuntimeBridge consumer, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(producer); ArgumentNullException.ThrowIfNull(consumer); ArgumentNullException.ThrowIfNull(log);
        lock (Gate)
        {
            if (Owners.Count != 0) throw new InvalidOperationException("The permanent runtime-invoke observer already has a managed owner.");
            var bridge = new NativeRuntimeInvokeBridge(producer, consumer, log);
            Owners.Add(bridge); // Root before Attach, including an uncertain outcome.
            var observer = new Observer { Version = 1, Size = 24, After = Marshal.GetFunctionPointerForDelegate(bridge._afterNative) };
            try
            {
                producer.VerifyReady();
                if (bridge._attach(producer.RuntimeEpoch, ref observer) != 0)
                    throw new InvalidOperationException("Native runtime-invoke observer Attach was rejected.");
                var info = NewInformation();
                if (bridge._info(ref info) != 0 || !MatchesInformation(info, producer.RuntimeTarget, producer.RuntimeOriginal, producer.RuntimeEntry, producer.RuntimeEpoch, true))
                    throw new UnsupportedSignatureException("Native runtime-invoke observer Attach could not be confirmed.");
                return bridge;
            }
            catch { bridge._admission.MarkUnknown(); throw; }
        }
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    private static NativeRuntimeInvokeInformation NewInformation() => new() { Version = 1, Size = 80 };
    internal static bool MatchesInformation(NativeRuntimeInvokeInformation info, IntPtr target, IntPtr original, IntPtr entry, ulong epoch, bool attached)
        => target != IntPtr.Zero && original != IntPtr.Zero && entry != IntPtr.Zero && epoch != 0
        && info.Version == 1 && info.Size == 80 && info.Attached == (attached ? 1u : 0u) && info.Unknown == 0
        && info.Target == target && info.Original == original && info.Entry == entry && info.Epoch == epoch
        && info.InFlight <= long.MaxValue && info.ObserverFaults == 0;
    // Value-only admission is shared with controlled tests. It cannot load a
    // producer, attach an observer, call an original, or issue a Ready owner.
    internal delegate uint ReadInformation(ref NativeRuntimeInvokeInformation info);
    internal sealed class AfterAdmission
    {
        private readonly IntPtr _target, _original, _entry;
        private readonly ulong _epoch;
        private readonly ReadInformation _read;
        private readonly Func<IntPtr, bool> _matchesTick;
        private readonly Action _verifyReady;
        private readonly Action<NativeRuntimeInvokeFrame, long> _after;
        private readonly Action<Exception> _fault;
        private readonly Action? _verifyOwnership;
        private readonly Action<RuntimeEntryMeasurement>? _measure;
        private readonly Func<long> _timestamp;
        private readonly bool _fullAuditOnTick;
        private IntPtr _tickMethod;
        private int _unknown, _dispatching, _hostThreadId, _reporting;
        private long _hostTickSequence;

        internal AfterAdmission(IntPtr target, IntPtr original, IntPtr entry, ulong epoch, ReadInformation read,
            Func<IntPtr, bool> matchesTick, Action verifyReady,
            Action<NativeRuntimeInvokeFrame, long> after, Action<Exception> fault,
            RuntimeExecutionOptions? options = null, Action? verifyOwnership = null,
            Action<RuntimeEntryMeasurement>? measure = null, Func<long>? timestamp = null)
        {
            if (target == IntPtr.Zero || original == IntPtr.Zero || entry == IntPtr.Zero || epoch == 0)
                throw new ArgumentException("Runtime-invoke admission requires the retained native channel identity.");
            ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(matchesTick);
            ArgumentNullException.ThrowIfNull(verifyReady); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(fault);
            _target = target; _original = original; _entry = entry; _epoch = epoch;
            _read = read; _matchesTick = matchesTick; _verifyReady = verifyReady; _after = after; _fault = fault;
            _fullAuditOnTick = options?.FullAuditOnTick == true; _verifyOwnership = verifyOwnership;
            _measure = measure; _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        }
        internal bool Unknown => Volatile.Read(ref _unknown) != 0;
        internal void MarkUnknown() => Interlocked.Exchange(ref _unknown, 1);
        internal void Reject(Exception error)
        {
            MarkUnknown();
            try { _fault(error); } catch { /* No diagnostic exception crosses the native observer. */ }
        }
        internal unsafe void ObserveNative(IntPtr pointer, IntPtr user)
        {
            try
            {
                if (Unknown) return;
                if (pointer == IntPtr.Zero || user != IntPtr.Zero)
                    throw new UnsupportedSignatureException("Runtime-invoke After frame or user is invalid.");
                Observe(*(NativeRuntimeInvokeFrame*)pointer);
            }
            catch (Exception error) { Reject(error); }
        }
        internal void Observe(NativeRuntimeInvokeFrame frame, uint? currentThreadId = null, long? entryStarted = null)
        {
            if (Unknown) return;
            var boundMethod = Volatile.Read(ref _tickMethod);
            var validFrame = frame.IsValid;
            // The steady-state observer sees every native invocation. Drop
            // known non-Ticks and same-thread recursion before timing or calling
            // through to native code. A matching foreign-thread Tick is checked.
            if (validFrame && (boundMethod != IntPtr.Zero && boundMethod != frame.Method
                || Volatile.Read(ref _dispatching) != 0
                && unchecked((uint)Volatile.Read(ref _hostThreadId)) == frame.OsThreadId)) return;

            var started = _measure is null ? 0 : entryStarted ?? _timestamp();
            var outcome = RuntimeEntryOutcome.Ignored;
            long hostStarted = 0, sequence = 0;
            var dispatchOwner = false;
            try
            {
                if (!validFrame || frame.OsThreadId != (currentThreadId ?? GetCurrentThreadId()))
                    throw new UnsupportedSignatureException("Runtime-invoke After frame differs from its native calling thread.");
                VerifyActiveChannel();
                if (boundMethod == IntPtr.Zero && !_matchesTick(frame.Method)) return;
                VerifyBoundThread(frame.OsThreadId);
                if (!frame.CompletedNormally) { outcome = RuntimeEntryOutcome.AbnormalReturn; return; }
                // Startup and explicit Developer diagnostics perform the full
                // publication audit. Standard admission checks live ownership.
                _verifyOwnership?.Invoke();
                if (_fullAuditOnTick) _verifyReady();
                VerifyActiveChannel();
                _verifyOwnership?.Invoke();
                if (Unknown || Interlocked.CompareExchange(ref _dispatching, 1, 0) != 0) return;
                dispatchOwner = true;
                if (Unknown) return;
                boundMethod = Volatile.Read(ref _tickMethod);
                if (boundMethod != IntPtr.Zero && boundMethod != frame.Method) return;
                VerifyBoundThread(frame.OsThreadId);
                if (boundMethod == IntPtr.Zero)
                {
                    Volatile.Write(ref _hostThreadId, unchecked((int)frame.OsThreadId));
                    Volatile.Write(ref _tickMethod, frame.Method);
                }
                sequence = Interlocked.Increment(ref _hostTickSequence);
                if (sequence <= 0) throw new InvalidOperationException("The monotonic host Tick sequence is exhausted.");
                if (_measure is not null) hostStarted = _timestamp();
                outcome = RuntimeEntryOutcome.Dispatched;
                _after(frame, sequence);
            }
            catch (Exception error) { outcome = RuntimeEntryOutcome.Rejected; Reject(error); }
            finally
            {
                // Reporting is observational: a slow/throwing consumer cannot
                // change native completion, grant admission or poison the lane.
                // No measurement callback/serialization cost is hidden in the
                // host-only duration; its delivery is outside this sample.
                if (_measure is not null && outcome != RuntimeEntryOutcome.Ignored
                    && Interlocked.CompareExchange(ref _reporting, 1, 0) == 0)
                {
                    try
                    {
                        var completed = _timestamp();
                        var admission = Math.Max(0, (hostStarted == 0 ? completed : hostStarted) - started);
                        var host = hostStarted == 0 ? 0 : Math.Max(0, completed - hostStarted);
                        _measure(new(sequence, outcome, admission * 1000d / Stopwatch.Frequency,
                            host * 1000d / Stopwatch.Frequency, Math.Max(0, completed - started) * 1000d / Stopwatch.Frequency,
                            _fullAuditOnTick));
                    }
                    catch { /* Measurement failures do not alter the execution contract. */ }
                    finally { Volatile.Write(ref _reporting, 0); }
                }
                if (dispatchOwner) Volatile.Write(ref _dispatching, 0);
            }
        }

        private void VerifyActiveChannel()
        {
            var info = NewInformation();
            if (_read(ref info) != 0 || !MatchesInformation(info, _target, _original, _entry, _epoch, true)
                || info.InFlight == 0)
                throw new UnsupportedSignatureException("Runtime-invoke After has no known active permanent native entry.");
        }
        private void VerifyBoundThread(uint threadId)
        {
            var hostThread = unchecked((uint)Volatile.Read(ref _hostThreadId));
            if (hostThread != 0 && hostThread != threadId)
                throw new InvalidOperationException("WrongThread: the runtime Tick requires its bound game main thread.");
        }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
