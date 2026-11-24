using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeTweenSingleEventBridge
{
    private GraphRead? _graphRead;

    // This authority is passed explicitly to the fixed synchronous graph
    // reader. Ordinary callbacks, mutation paths and the next read get their
    // own full publication audit. Each scan checks all live physical proof on
    // entry and exit, including when its projection fails.
    internal T ReadVerifiedGraph<T>(Func<GraphRead, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_producer is null || !ReferenceEquals(Volatile.Read(ref _owner), this))
            throw new UnsupportedSignatureException("A graph scan requires the actual registered Single producer.");
        var scope = new GraphRead(this, _producer);
        if (Interlocked.CompareExchange(ref _graphRead, scope, null) is not null)
            throw new UnsupportedSignatureException("A Single graph scan cannot be reentered or shared between threads.");
        try
        {
            _verifyPublication();
            scope.Open();
            T result;
            try
            {
                scope.RequireKnown();
                result = read(scope);
            }
            catch (Exception readFailure)
            {
                try { VerifyExit(); }
                catch (Exception verificationFailure)
                { throw new AggregateException("The graph scan and its publication exit check failed.", readFailure, verificationFailure); }
                throw;
            }
            VerifyExit();
            return result;
        }
        finally { Volatile.Write(ref _graphRead, null); }

        void VerifyExit() { _verifyPublication(); scope.RequireKnown(); }
    }

    internal sealed class GraphRead
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint ReadSourceActivity(ref NativeTweenSourceInformation value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint ReadRetirementActivity(ref RetirementActivity value);

        [StructLayout(LayoutKind.Sequential)]
        private struct RetirementActivity
        {
            internal uint Version, Size, Unknown, ActiveCut;
            internal ulong Generation, Reservations, Entered, Exited, Cuts, Commits;
        }

        private readonly NativeTweenSingleEventBridge _bridge;
        private readonly NativeFirstInstall _producer;
        private readonly int _managedThread = Environment.CurrentManagedThreadId;
        private readonly uint _osThread = NativeTweenSourceReferences.CurrentThreadId();
        private readonly Action _verifyLive;
        private ReadSourceActivity _source = null!;
        private ReadRetirementActivity _retirement = null!;
        private ulong _sourceEpoch;
        private uint? _metadataBound;

        internal GraphRead(NativeTweenSingleEventBridge bridge, NativeFirstInstall producer)
        {
            (_bridge, _producer) = (bridge, producer);
            _verifyLive = VerifyLive;
        }

        internal void Open()
        {
            VerifyOwner(_bridge);
            if (Marshal.SizeOf<RetirementActivity>() != 64 || Marshal.SizeOf<NativeTweenSourceInformation>() != 96)
                throw new UnsupportedSignatureException("The live Tween activity layout differs.");
            _sourceEpoch = _producer.RetainedTweenSourceEpoch;
            _source = Export<ReadSourceActivity>("RizTweenSourceContext_GetInfo");
            _retirement = Export<ReadRetirementActivity>("RizTweenRetirement_GetInfo");
        }

        internal ExactHookBroker.ManagedBoundaryActivity ReadActivity(NativeTweenSingleEventBridge bridge)
        {
            VerifyOwner(bridge);
            return _bridge.ReadActivity(_verifyLive);
        }

        internal void RequireKnown()
        {
            if (ReadActivity(_bridge) is not { Known: true, InFlight: >= 0 })
                throw new UnsupportedSignatureException("The actual Single/Tween source activity is unknown during graph observation.");
        }

        private void VerifyOwner(NativeTweenSingleEventBridge bridge)
        {
            if (!ReferenceEquals(_bridge, bridge) || !ReferenceEquals(Volatile.Read(ref _bridge._graphRead), this)
                || _managedThread != Environment.CurrentManagedThreadId || _osThread != NativeTweenSourceReferences.CurrentThreadId())
                throw new UnsupportedSignatureException("The Tween graph scan escaped its exact synchronous owner.");
        }

        private void VerifyLive()
        {
            _producer.VerifyRuntimeChannelOwnership();
            var source = new NativeTweenSourceInformation { Version = 1, Size = 96 };
            if (_source(ref source) != 0 || source.Version != 1 || source.Size != 96 || source.Configured != 1
                || source.Unknown != 0 || source.Faults != 0 || source.BindingCount != NativeTweenSource.BindingCount
                || source.JournalCapacity != NativeTweenSource.JournalCapacity || source.Epoch != _sourceEpoch
                || source.MetadataBound > 1 || _metadataBound.HasValue && source.MetadataBound != _metadataBound.Value)
                throw new UnsupportedSignatureException("The actual Tween source became unknown or changed its retained authority.");
            _metadataBound ??= source.MetadataBound;
            // The actual FirstInstall producer also raises this native sticky
            // fault when its publication becomes Unknown. No prior activity
            // sample or successful completion is returned by this check.
            var retirement = new RetirementActivity { Version = 1, Size = 64 };
            if (_retirement(ref retirement) != 0 || retirement.Version != 1 || retirement.Size != 64
                || retirement.Unknown != 0 || retirement.ActiveCut != 0
                || retirement.Entered < retirement.Exited || retirement.Entered - retirement.Exited != retirement.Reservations)
                throw new UnsupportedSignatureException("The actual Tween source retirement state is unknown or unavailable to this graph scan.");
        }

        private T Export<T>(string name) where T : Delegate
        {
            var address = NativeLibrary.GetExport(_producer.HelperModule, name);
            HookTargetValidation.VerifyExecutable(address, _producer.HelperModule);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
    }
}
