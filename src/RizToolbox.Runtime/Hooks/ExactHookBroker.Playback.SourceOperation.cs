namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        private long _sourceOperationChecks;
        internal bool OwnsOperationEngine(DobbyDetourEngine engine) => ReferenceEquals(_engine, engine);

        // This contract verifies the current source operation, never the
        // installation image. Fixed graph/ticket queries are its only API;
        // audio/DSP/media reads, native writers and Clock consumers must open
        // their own operation after this bracket has actually ended.
        internal SourceOperationScope BeginSourceOperation(object owner,
            NativeRuntime.NativePlaybackStockTargets stock, NativePlaybackSingleReturnBridge.FreshSourceReceipt receipt)
            => SourceOperationScope.Create(this, owner, stock, receipt);

        internal void VerifySourceOperationChannel(PlaybackSourceOwner member)
        {
            VerifyOperationThread(); ArgumentNullException.ThrowIfNull(member);
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                try
                {
                    if (!_sources.TryGetValue(member.Role, out var actual) || !ReferenceEquals(actual, member))
                        throw new UnsupportedSignatureException("This running source operation does not own the group's actual member.");
                    member.VerifyOperationChannel(this, _hub!);
                    VerifyOperationGroupOwnership();
                }
                catch (Exception error) { Fault("The running source member lost its authority: " + error.Message); throw; }
            }
        }

        internal sealed class SourceOperationScope : IDisposable
        {
            private readonly SourceOperationScope _registered;
            private readonly PlaybackSourceInstallGroup _group;
            private readonly object _owner;
            private readonly NativeRuntime.NativePlaybackStockTargets _stock;
            private readonly NativePlaybackSingleReturnBridge.FreshSourceReceipt _receipt;
            private readonly int _thread;
            private readonly long _generation;
            private bool _closed;

            private SourceOperationScope(PlaybackSourceInstallGroup group, object owner,
                NativeRuntime.NativePlaybackStockTargets stock, NativePlaybackSingleReturnBridge.FreshSourceReceipt receipt)
                => (_registered, _group, _owner, _stock, _receipt, _thread, _generation)
                    = (this, group, owner, stock, receipt, Environment.CurrentManagedThreadId, _referenceCodeGeneration);

            internal static SourceOperationScope Create(PlaybackSourceInstallGroup group, object owner,
                NativeRuntime.NativePlaybackStockTargets stock, NativePlaybackSingleReturnBridge.FreshSourceReceipt receipt)
            {
                group.VerifyOperationThread(); ArgumentNullException.ThrowIfNull(owner);
                ArgumentNullException.ThrowIfNull(stock); ArgumentNullException.ThrowIfNull(receipt);
                System.Threading.Monitor.Enter(group._verifyGate);
                try { System.Threading.Monitor.Enter(Gate); }
                catch { System.Threading.Monitor.Exit(group._verifyGate); throw; }
                try
                {
                    Interlocked.Increment(ref group._sourceOperationChecks);
                    var operation = new SourceOperationScope(group, owner, stock, receipt);
                    operation.VerifyBoundary();
                    return operation;
                }
                catch
                {
                    System.Threading.Monitor.Exit(Gate);
                    System.Threading.Monitor.Exit(group._verifyGate);
                    throw;
                }
            }

            private void VerifyContext()
            {
                if (_closed || !ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId
                    || !System.Threading.Monitor.IsEntered(Gate) || !System.Threading.Monitor.IsEntered(_group._verifyGate)
                    || _generation != _referenceCodeGeneration)
                    throw new UnsupportedSignatureException("A source operation cannot be copied, retained, or detached from its owner thread and broker generation.");
                VerifyPhysicalOwnership();
                if (!_group._targets.IdentifiesStock(_stock) || _group.OwnedFixture || _group._foundation is null
                    || _group.FoundationEpoch == 0 || !_group._targets.HasUnityBacking || !_group._targets.HasLowerTime
                    || !_group._targets.HasEnginePitch || !_group._targets.HasProgressResource || _group._targets.SourceRoleCount != 22
                    || !ReferenceEquals(_group._stockFreshOwner, _owner) || !ReferenceEquals(_group._stockFreshReceipt, _receipt))
                    throw new UnsupportedSignatureException("The source operation lost its exact stock group, foundation or sole claimed ticket owner.");
                _receipt.VerifyClaimedOperationRegistration();
            }

            private void VerifyPhysicalOwnership()
            {
                // Logical withdrawal rejects this operation without converting
                // a normal source/run transition into a sticky physical fault.
                if (!_group.IsActive)
                    throw new UnsupportedSignatureException("The source operation's retained group is no longer active.");
                try { _group.VerifyOperationGroupOwnership(); }
                catch (Exception error)
                {
                    _group.Fault("The source operation lost its physical group ownership: " + error.Message);
                    throw;
                }
            }

            private void VerifyPhysicalChannels()
            {
                try
                {
                    _ = _group._getTime!.ReadOperationPolicy(_group);
                    // These four registered channels own graph lifetime changes.
                    // This checks their retained physical identity, not every role.
                    foreach (var role in new[] { NativePlaybackSourceRole.PlayerConstructor,
                        NativePlaybackSourceRole.ProgressMusicPlayer, NativePlaybackSourceRole.ProgressClip,
                        NativePlaybackSourceRole.SetClip })
                        _group.SourceOwner(role).VerifyOperationChannel(_group, _group._hub!);
                }
                catch (Exception error)
                {
                    _group.Fault("The source operation lost a physical lifetime channel: " + error.Message);
                    throw;
                }
            }

            private void VerifyBoundary()
            {
                VerifyContext();
                VerifyPhysicalChannels();
                // A superseded ticket or inactive backing expires this logical
                // source. It does not fault an otherwise healthy installation.
                _ = _receipt.Read();
                _group._hub!.VerifyOperationActivePlayerWatch(_owner);
                VerifyContext();
            }

            internal void RequireStockProtocol(NativeRuntime.NativePlaybackStockTargets stock)
            {
                VerifyContext();
                if (!ReferenceEquals(stock, _stock))
                    throw new UnsupportedSignatureException("The running source query supplied another stock protocol identity.");
            }

            internal void VerifyReceivers(NativeRuntime api, IntPtr player, IntPtr source, IntPtr progress, IntPtr getTimeMethod)
            {
                VerifyContext();
                _group.VerifyReceiverProjection(api, player, source, progress, getTimeMethod);
                VerifyContext();
            }

            internal NativePlaybackSingleReturnBridge.FreshSourceInformation VerifyFreshTicket(object owner,
                NativeRuntime.NativePlaybackStockTargets stock, NativePlaybackSingleReturnBridge.FreshSourceReceipt receipt)
            {
                RequireStockProtocol(stock);
                if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(receipt, _receipt))
                    throw new UnsupportedSignatureException("The running source query supplied another private ticket owner.");
                var actual = _receipt.Read();
                _group._hub!.VerifyOperationActivePlayerWatch(_owner);
                VerifyContext();
                return actual;
            }

            public void Dispose()
            {
                if (_closed) return;
                if (!ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId)
                    throw new UnsupportedSignatureException("Only the actual owner thread can end its source operation.");
                try { VerifyBoundary(); }
                finally
                {
                    _closed = true;
                    System.Threading.Monitor.Exit(Gate);
                    System.Threading.Monitor.Exit(_group._verifyGate);
                }
            }
        }
    }
}

internal sealed partial class PlaybackSourceGroupLease
{
    internal ExactHookBroker.PlaybackSourceInstallGroup.SourceOperationScope BeginSourceOperation(object owner,
        NativeRuntime.NativePlaybackStockTargets stock, NativePlaybackSingleReturnBridge.FreshSourceReceipt receipt)
    {
        VerifyOperationLease();
        return _owner.BeginSourceOperation(owner, stock, receipt);
    }
}
