using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Offline native owned-call lane. The private fences below can be issued only from
/// actual native Read/Before/Returned results. This does not observe direct game calls or hooks.
/// There is intentionally no production constructor or caller-supplied observation interface.</summary>
internal sealed class NativePlaybackOwnedCallTransport : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Snapshot
    {
        internal uint Version, Size;
        internal ulong Epoch, Source, Player, SourceSequence, PitchSequence, OccurrenceSequence;
        internal ulong ScheduleSequence, ScheduleOccurrence, StockPitchSequence, AttemptSequence;
        internal uint ActiveOccurrence, OccurrenceKind, OccurrenceStatus, ScheduleReturned;
        internal uint StockPitchReturned, AttemptReturned, PitchActive, Reserved;
        internal double ScheduleDsp, BeforeDsp, AfterDsp;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Callback(IntPtr context, uint operation, ulong argument, out double result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create(Callback callback, IntPtr context, ulong source, ulong player);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadNative(IntPtr instance, out Snapshot snapshot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RunNative(IntPtr instance, uint kind, float delay, out Snapshot snapshot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WriteNative(IntPtr instance, ulong epoch, ulong source, ulong occurrence, ulong pitchSequence, float pitch, out Snapshot snapshot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ClipNative(IntPtr instance, ulong clip, out Snapshot snapshot);
    private sealed class CallFrame { internal ExceptionDispatchInfo? Failure; }

    private readonly NativePlaybackActualSource _source;
    private readonly long _sourceAddress, _playerAddress;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Callback _callback;
    private readonly Destroy _destroy;
    private readonly ReadNative _read;
    private readonly RunNative _run;
    private readonly WriteNative _write;
    private readonly ClipNative _clip;
    private readonly Stack<CallFrame> _calls = new();
    private IntPtr _library, _instance;
    private ulong _epoch;
    private double? _lastObservedDsp;
    private bool _disposed;

    private NativePlaybackOwnedCallTransport(string libraryPath, NativePlaybackActualSource source, NativePlaybackBinding binding)
    {
        _source = source; _sourceAddress = binding.AudioSource; _playerAddress = binding.Player; _callback = InvokeActual;
        _library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        try
        {
            var create = Export<Create>("RizPlaybackOwned_Create"); _destroy = Export<Destroy>("RizPlaybackOwned_Destroy");
            _read = Export<ReadNative>("RizPlaybackOwned_Read"); _run = Export<RunNative>("RizPlaybackOwned_Run");
            _write = Export<WriteNative>("RizPlaybackOwned_WritePitch"); _clip = Export<ClipNative>("RizPlaybackOwned_SetClip");
            _instance = create(_callback, IntPtr.Zero, (ulong)_sourceAddress, (ulong)_playerAddress);
            if (_instance == IntPtr.Zero) throw new InvalidOperationException("The controlled native owned-call producer was not created.");
            var initial = ReadSnapshot(); _epoch = initial.Epoch;
        }
        catch
        {
            if (_instance != IntPtr.Zero) Export<Destroy>("RizPlaybackOwned_Destroy")(_instance);
            NativeLibrary.Free(_library); _instance = _library = IntPtr.Zero; throw;
        }
    }

    internal static NativePlaybackOwnedCallTransport CreateControlledForTests(string libraryPath,
        NativePlaybackActualSource source, NativePlaybackBinding binding)
        => new(libraryPath, source, binding);

    /// <summary>A non-copyable private origin/fence, not a data record accepted from a caller.</summary>
    internal sealed class Fence
    {
        private readonly NativePlaybackOwnedCallTransport _owner;
        private readonly Snapshot _snapshot;
        private Fence(NativePlaybackOwnedCallTransport owner, Snapshot snapshot) => (_owner, _snapshot) = (owner, snapshot);
        internal ulong Epoch => _snapshot.Epoch;
        internal ulong SourceSequence => _snapshot.SourceSequence;
        internal ulong PitchSequence => _snapshot.PitchSequence;
        internal ulong OccurrenceSequence => _snapshot.OccurrenceSequence;
        internal ulong ScheduleSequence => _snapshot.ScheduleSequence;
        internal ulong ScheduleOccurrence => _snapshot.ScheduleOccurrence;
        internal ulong StockPitchSequence => _snapshot.StockPitchSequence;
        internal uint Kind => _snapshot.OccurrenceKind;
        internal bool Returned => _snapshot.OccurrenceStatus == 2 && _snapshot.ActiveOccurrence == 0;
        internal bool ScheduleReturned => _snapshot.ScheduleReturned == 1;
        internal bool StockPitchReturned => _snapshot.StockPitchReturned == 1;
        internal bool Busy => _snapshot.ActiveOccurrence != 0 || _snapshot.PitchActive != 0;
        internal double ScheduleDsp => _snapshot.ScheduleDsp;
        internal double BeforeDsp => _snapshot.BeforeDsp;
        internal double AfterDsp => _snapshot.AfterDsp;
        internal bool BelongsTo(NativePlaybackOwnedCallTransport owner) => ReferenceEquals(_owner, owner);
        internal static Fence CaptureActual(NativePlaybackOwnedCallTransport owner) => new(owner, owner.ReadSnapshot());
        internal static Result RunActual(NativePlaybackOwnedCallTransport owner, uint kind, float delay)
        {
            owner.Open(); var frame = new CallFrame(); owner._calls.Push(frame);
            try { var code = owner._run(owner._instance, kind, delay, out var actual); return Complete(owner, code, actual, frame); }
            finally { owner._calls.Pop(); }
        }
        internal Result WriteActual(float pitch)
        {
            _owner.RequireFence(this); var frame = new CallFrame(); _owner._calls.Push(frame);
            try
            {
                var code = _owner._write(_owner._instance, Epoch, SourceSequence, OccurrenceSequence, PitchSequence, pitch, out var actual);
                var result = Complete(_owner, code, actual, frame);
                // Own exactly the native Before attempt, including callback failure. A reentrant
                // foreign setter can advance the global sequence, but cannot become this ticket.
                actual.PitchSequence = actual.AttemptSequence;
                return new(new(_owner, actual), result.Failure);
            }
            finally { _owner._calls.Pop(); }
        }
        internal static Result ClipActual(NativePlaybackOwnedCallTransport owner, IntPtr clip)
        {
            owner.Open(); var frame = new CallFrame(); owner._calls.Push(frame);
            try { var code = owner._clip(owner._instance, (ulong)clip.ToInt64(), out var actual); return Complete(owner, code, actual, frame); }
            finally { owner._calls.Pop(); }
        }
        private static Result Complete(NativePlaybackOwnedCallTransport owner, int code, Snapshot actual, CallFrame frame)
        {
            if (code == 0) throw new GameplayStateException(ErrorCode.Conflict, "The exact native owned-call entry or conditional fence was refused before invocation.");
            owner.Validate(actual);
            if (code is not (1 or 2)) throw new InvalidOperationException("Unknown controlled native owned-call completion code.");
            return new(new(owner, actual), frame.Failure ?? (code == 2
                ? ExceptionDispatchInfo.Capture(new GameplayStateException(ErrorCode.ObservationUnavailable, "The native owned call did not return completely.")) : null));
        }
    }

    internal sealed record Result(Fence Fence, ExceptionDispatchInfo? Failure)
    {
        internal void Rethrow() => Failure?.Throw();
    }
    internal Fence Capture() => Fence.CaptureActual(this);
    internal void Verify(Fence expected, bool pitch = true)
    {
        RequireFence(expected); var actual = ReadSnapshot();
        if (actual.SourceSequence != expected.SourceSequence || actual.OccurrenceSequence != expected.OccurrenceSequence
            || pitch && actual.PitchSequence != expected.PitchSequence || actual.ActiveOccurrence != 0 || actual.PitchActive != 0)
            throw new GameplayStateException(ErrorCode.Conflict, "The native owned-call source/occurrence/writer fence changed, including an equal-value or ABA write.");
    }
    internal Result Run(uint kind, float delay) => Fence.RunActual(this, kind, delay);
    internal Result Write(Fence expected, float pitch)
    {
        RequireFence(expected); return expected.WriteActual(pitch);
    }
    internal Result SetClip(IntPtr clip) => Fence.ClipActual(this, clip);
    private int InvokeActual(IntPtr context, uint operation, ulong argument, out double result)
    {
        result = 0;
        try
        {
            if (_calls.Count == 0 || context != IntPtr.Zero) throw new InvalidOperationException("No exact native owned-call frame is active.");
            _source.VerifyGraph();
            var number = BitConverter.Int64BitsToDouble(unchecked((long)argument));
            switch (operation)
            {
                case 1: result = _source.ReadActive() ? 1 : 0; break;
                case 2: result = _source.ReadPlaying() ? 1 : 0; break;
                case 3:
                    result = _source.ReadDsp();
                    if (!double.IsFinite(result) || result < 0 || _lastObservedDsp is double previous && result < previous)
                        throw new GameplayStateException(ErrorCode.ObservationUnavailable, "An actual native owned DSP callback became nonfinite, negative, or regressed.");
                    _lastObservedDsp = result; break;
                case 4: result = _source.ReadMedia(); break;
                case 5: _source.InvokeAudioPause(); break;
                case 6: _source.InvokeStop(); break;
                case 7: _source.InvokeTime((float)number); break;
                case 8: _source.InvokePitch((float)number); break;
                case 9: _source.InvokeSchedule(number); break;
                case 10: _source.InvokeClip(new(unchecked((long)argument))); break;
                default: throw new InvalidOperationException("Unknown controlled native source callback.");
            }
            return 1;
        }
        catch (Exception error)
        {
            if (_calls.Count != 0) _calls.Peek().Failure ??= ExceptionDispatchInfo.Capture(error);
            return 0; // Managed exceptions never cross the unmanaged callback boundary.
        }
    }
    private Snapshot ReadSnapshot()
    {
        Open(); if (_read(_instance, out var actual) != 1) throw new GameplayStateException(ErrorCode.TargetExpired, "The native owned-call producer cannot be read.");
        Validate(actual); return actual;
    }
    private void Validate(Snapshot actual)
    {
        if (Marshal.SizeOf<Snapshot>() != 144 || actual.Version != 1 || actual.Size != 144 || actual.Epoch == 0
            || _epoch != 0 && actual.Epoch != _epoch || actual.Source != (ulong)_sourceAddress || actual.Player != (ulong)_playerAddress
            || actual.SourceSequence == 0 || actual.PitchSequence == 0 || actual.OccurrenceSequence == 0 || actual.Reserved != 0)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The actual native owned-call identity/layout/epoch changed.");
    }
    private void RequireFence(Fence fence)
    {
        Open(); ArgumentNullException.ThrowIfNull(fence);
        if (!fence.BelongsTo(this)) throw new GameplayStateException(ErrorCode.TargetExpired, "The private native writer fence belongs to another producer.");
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    private void Open()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackOwnedCallTransport));
        if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "The controlled native owned-call source is main-thread bound.");
    }
    public void Dispose()
    {
        if (_disposed) return; Open();
        if (_calls.Count != 0 || _destroy(_instance) != 1) throw new InvalidOperationException("The native owned-call producer is still active.");
        _disposed = true; _instance = IntPtr.Zero; NativeLibrary.Free(_library); _library = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }
}
