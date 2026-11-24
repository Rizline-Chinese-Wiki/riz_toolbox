using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeEngineTransportFirstInstall
{
    private int _activityOwnerThread;
    private NativeEngineRuntimeCoordinator? _activityCoordinator;
    private byte[]? _activityExpectedPublication, _activityPublication;
    private IntPtr _activityPublicationBuffer;

    // Only this concrete coordinator can execute this fixed synchronous read.
    // There is no caller delegate, logger, await, AsyncLocal or permission DTO.
    // The monitor and explicit thread check keep nested bridge verification
    // inside this one read; no successful result is reused by the next call.
    // Performance follow-up is deferred while functional wiring is completed.
    // The owned r7 fixture measured about 6.6 ms and 188 KB per aggregate read;
    // this is not a verified per-frame budget or a production-game benchmark.
    // Avoid multiplying aggregate reads in new callers. Any later optimization
    // must retain live byte/protection/RF/graph/Kernel checks and sticky Unknown.
    internal ExactHookBroker.ManagedBoundaryActivity ReadVerifiedActivity(NativeEngineRuntimeCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        lock (_gate)
        {
            var previousDepth = _verificationDepth;
            var previousThread = _activityOwnerThread;
            var previousCoordinator = _activityCoordinator;
            try
            {
                if (Unknown || _activityOwnerThread != 0 || _activityCoordinator is not null
                    || _adoption != 2 || !_attached.All(value => value))
                    throw new UnsupportedSignatureException("V5 activity requires a completed retained adoption and one nonreentrant synchronous reader.");
                _activityOwnerThread = Environment.CurrentManagedThreadId;
                _activityCoordinator = coordinator;
                _verificationDepth = checked(previousDepth + 1);
                VerifyActivityLiveBoundary();
                var activity = coordinator.ReadPublishedActivityCore(this);
                if (!activity.Known || activity.InFlight < 0 || Unknown)
                    throw new UnsupportedSignatureException("Actual V5 activity or its retained coordinator became Unknown.");
                VerifyActivityLiveBoundary();
                return activity;
            }
            catch { MarkUnknown(); throw; }
            finally
            {
                _verificationDepth = previousDepth;
                _activityCoordinator = previousCoordinator;
                _activityOwnerThread = previousThread;
            }
        }
    }

    // Internal synchronous composition only. No token, cross-thread permission,
    // cached Ready result or lifetime extension leaves this call. The monitor
    // serializes this owner's managed readers, not execution of native code.
    // Both boundaries retain complete native Verify + independent file/image/
    // source/RF checks. Nested bridge calls keep checking live publication and
    // Kernel identities, source/relay bytes, unwind registrations and dependency
    // graph rather than hashing the same five held files dozens of
    // times while collecting one aggregate result. Native activity remains live.
    internal T VerifyOperation<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            if (_activityOwnerThread != 0 || _activityCoordinator is not null)
            {
                MarkUnknown();
                throw new UnsupportedSignatureException("An activity read cannot authorize adoption, sharing or a mutation operation.");
            }
            VerifyReady();
            var previousDepth = _verificationDepth;
            _verificationDepth = checked(previousDepth + 1);
            T result;
            try
            {
                result = operation();
            }
            catch
            {
                MarkUnknown();
                throw;
            }
            finally { _verificationDepth = previousDepth; }
            VerifyReady();
            return result;
        }
    }

    private void VerifyLivePublication()
    {
        if (_activityOwnerThread != 0)
        {
            RequireActivityThread();
            // Each bridge immediately reads its real GetInfo and verifies its
            // exact Kernel tuple/counters. Recheck the entire live publication
            // here; the containing activity checks the full graph/code/RF and
            // all twelve Kernel tuples on both of its synchronous boundaries.
            VerifyActivityPublication();
            return;
        }
        if (Unknown || !Read().SameBytes(_publication))
            throw new UnsupportedSignatureException("The live V5 publication changed inside a verified operation.");
        foreach (var module in _modules) module.VerifyCurrent();
        VerifyShape(); VerifyModulesAndGraph(); VerifyTypedAndCoverage();
        VerifySitesAndResources(); VerifyRuntimeFunctions();
        VerifyFamilies(out _);
    }

    private void RequireActivityThread()
    {
        if (Unknown || _activityOwnerThread != Environment.CurrentManagedThreadId
            || _activityCoordinator is null || !System.Threading.Monitor.IsEntered(_gate))
            throw new UnsupportedSignatureException("V5 activity verification escaped its exact synchronous owner thread.");
    }

    internal void RequireActivityReader(NativeEngineRuntimeCoordinator coordinator)
    {
        RequireActivityThread();
        if (!ReferenceEquals(_activityCoordinator, coordinator))
            throw new UnsupportedSignatureException("V5 activity belongs to a different retained coordinator.");
    }

    private void VerifyActivityPublication()
    {
        RequireActivityThread();
        if (_activityExpectedPublication is null)
        {
            _activityExpectedPublication = _publication.CopyBytes();
            _activityPublication = new byte[_activityExpectedPublication.Length];
            // The publication owner is permanently rooted. This fixed scratch
            // buffer has the same lifetime and never escapes to another owner.
            _activityPublicationBuffer = Marshal.AllocHGlobal(_activityExpectedPublication.Length);
            Marshal.Copy(_activityExpectedPublication, 0, _activityPublicationBuffer, _activityExpectedPublication.Length);
        }
        Marshal.WriteInt32(_activityPublicationBuffer, 5);
        Marshal.WriteInt32(_activityPublicationBuffer, 4, _activityExpectedPublication.Length);
        // GetInfo is nonwaiting. A busy/invalid result is not a successful idle
        // reading, and activity never waits behind a complete file-hash Verify.
        if (_read(_activityPublicationBuffer) != 0)
            throw new UnsupportedSignatureException("The actual V5 activity publication is busy or unavailable.");
        Marshal.Copy(_activityPublicationBuffer, _activityPublication!, 0, _activityExpectedPublication.Length);
        if (!_activityPublication!.AsSpan().SequenceEqual(_activityExpectedPublication))
            throw new UnsupportedSignatureException("The complete live V5 publication changed during activity collection.");
    }

    private void VerifyActivityLiveBoundary()
    {
        RequireActivityThread();
        VerifyActivityPublication();
        try
        {
            foreach (var module in _modules) module.BeginActivityBoundary(this);
            foreach (var module in _modules) module.VerifyCurrent();
            VerifyShape(); VerifyModulesAndGraph(); VerifyTypedAndCoverage();
            VerifySitesAndResources(); VerifyRuntimeFunctions(); VerifyFamilies(out _);
            VerifyActivityPublication();
        }
        finally { foreach (var module in _modules) module.EndActivityBoundary(); }
    }
}
