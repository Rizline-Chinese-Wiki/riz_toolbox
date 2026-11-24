using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Lives inside a process-owned native graph. Records inputs before entering
/// an explicit runtime publication call, whose reservation rejection leaves them owned
/// by its caller. An unknown call never clears or retries these bounded records.</summary>
internal sealed class NativePublicationArgumentRecords
{
    private const int MaximumArguments = 128;
    private readonly IMainThreadDispatcher _main;
    private readonly NativeRuntime.Arg?[] _arguments = new NativeRuntime.Arg?[MaximumArguments];
    private int _count;
    private bool _pending, _unknown;
    internal int Count { get { _main.VerifyAccess(); return _count; } }
    internal bool CleanupUnknown => _unknown;
    internal NativePublicationArgumentRecords(IMainThreadDispatcher main) => _main = main;
    internal void Begin(NativeRuntime.Arg[] arguments)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(arguments);
        if (_pending || _unknown) throw new InvalidOperationException("Publication inputs are still in flight or have an unknown outcome.");
        if (arguments.Length > MaximumArguments || arguments.Any(argument => argument is null))
            throw new ArgumentException("Publication inputs require at most 128 nonnull records.", nameof(arguments));
        _pending = true;
        for (var i = 0; i < arguments.Length; i++) _arguments[_count++] = arguments[i];
    }
    internal void Complete()
    {
        _main.VerifyAccess();
        if (!_pending || _unknown) throw new InvalidOperationException("Only a successfully returned publication call can complete its inputs.");
        // The runtime consumed every input before returning successfully. Do not free
        // them a second time or use disposal no-ops as evidence of an unknown free.
        Array.Clear(_arguments, 0, _count); _count = 0; _pending = false;
    }
    internal void Fail()
    {
        _main.VerifyAccess();
        if (!_pending) throw new InvalidOperationException("No publication call owns these input records.");
        _unknown = true;
    }
}
