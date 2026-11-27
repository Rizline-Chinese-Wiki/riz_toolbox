namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        // Only fixed code, graph, protocol and ticket queries belong in this bracket.
        // It has no callback, sample, writer, original, or Clock API. Callers
        // must finish it before consuming audio/DSP/media or issuing an action.
        internal SourceVerificationScope BeginSourceVerification()
            => SourceVerificationScope.Create(this);

        internal sealed class SourceVerificationScope : IDisposable
        {
            private readonly SourceVerificationScope _registered;
            private readonly PlaybackSourceInstallGroup _group;
            private readonly VerificationTraversal _traversal;
            private readonly int _thread;
            private bool _closed;

            private SourceVerificationScope(PlaybackSourceInstallGroup group, VerificationTraversal traversal)
                => (_registered, _group, _traversal, _thread) = (this, group, traversal, Environment.CurrentManagedThreadId);

            internal static SourceVerificationScope Create(PlaybackSourceInstallGroup group)
            {
                System.Threading.Monitor.Enter(group._verifyGate);
                try { System.Threading.Monitor.Enter(Gate); }
                catch { System.Threading.Monitor.Exit(group._verifyGate); throw; }
                VerificationTraversal? traversal = null;
                try
                {
                    group.VerifyTraversalVerificationIssuer();
                    traversal = VerificationTraversal.Create(group);
                    group.VerifyCurrentBody(traversal);
                    return new(group, traversal);
                }
                catch (Exception error)
                {
                    try { traversal?.Dispose(); }
                    finally
                    {
                        try { group.Fault("The source verification entry lost its physical authority: " + error.Message); }
                        finally
                        {
                            System.Threading.Monitor.Exit(Gate);
                            System.Threading.Monitor.Exit(group._verifyGate);
                        }
                    }
                    throw;
                }
            }

            private void VerifyContext()
            {
                if (_closed || !ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId)
                    throw new UnsupportedSignatureException("A source verification cannot be copied, retained, or used off its owner thread.");
                _traversal.VerifyIssuer(_group);
            }

            internal bool MatchesStockOriginalBodies(NativeRuntime.NativePlaybackStockTargets stock,
                ReadOnlySpan<(IntPtr Address, string Hash, int Length)> bodies)
            {
                VerifyContext();
                if (!_group._targets.IdentifiesStock(stock))
                    throw new UnsupportedSignatureException("The group lease has a different runtime-issued stock target receipt.");
                _traversal.VerifySupportingIssuer(stock);

                // Check every distinct GameAssembly owner inside the full group
                // checks at entry and exit. The lower group members still have
                // their own fixed bodies and evidence in both group checks.
                var addresses = _group._sources.Values.Where(owner => owner.MemberModule == stock.Module)
                    .Select(owner => owner.TargetAddress).Append(_group._getTime!.TargetAddress).ToHashSet();
                if (bodies.Length != addresses.Count) return false;
                foreach (var body in bodies)
                    if (!addresses.Remove(body.Address) || !_group.MatchesOriginalBodyPin(body.Address, body.Hash, body.Length))
                        return false;
                VerifyContext();
                return true;
            }

            internal void RequireStockProtocol(NativeRuntime.NativePlaybackStockTargets stock)
            {
                VerifyContext();
                _traversal.VerifySupportingIssuer(stock);
                if (!_group._targets.IdentifiesStock(stock) || _group.OwnedFixture || _group._foundation is null
                    || _group.FoundationEpoch == 0 || !_group._targets.HasUnityBacking
                    || !_group._targets.HasLowerTime || !_group._targets.HasEnginePitch
                    || !_group._targets.HasProgressResource || _group._targets.SourceRoleCount != 22)
                    throw new UnsupportedSignatureException("Formal fresh capture requires this exact stock group and its retained foundation membership.");
                _group._getTime!.RequireFreshSourceProtocol();
                _group._hub!.RequireFreshSourceProtocol();
                VerifyContext();
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
                if (owner is null || receipt is null || !ReferenceEquals(_group._stockFreshOwner, owner)
                    || !ReferenceEquals(_group._stockFreshReceipt, receipt))
                    throw new UnsupportedSignatureException("The fresh source does not own this group's sole registered native ticket.");
                // This is a read of the issued identity, never Issue,
                // Claim, Arm, writer scheduling, or an AudioSource sample.
                var actual = receipt.Read();
                _group._hub!.VerifyActivePlayerWatch(owner);
                VerifyContext();
                return actual;
            }

            public void Dispose()
            {
                if (_closed) return;
                // Reject a foreign exit before it can release the real locks.
                if (!ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId)
                    throw new UnsupportedSignatureException("Only the actual owner thread can end its source verification.");
                try
                {
                    try
                    {
                        VerifyContext();
                        // Repeat every member/page/byte check after the fixed
                        // queries. The same traversal's full foundation, target,
                        // Dobby, helper and ordinary exits still run below.
                        _group.VerifyCurrentBody(_traversal);
                    }
                    finally { _traversal.Dispose(); }
                }
                catch (Exception error)
                {
                    _group.Fault("The source verification exit lost its physical authority: " + error.Message);
                    throw;
                }
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
    internal ExactHookBroker.PlaybackSourceInstallGroup.SourceVerificationScope BeginSourceVerification()
    {
        VerifyOperationLease();
        return _owner.BeginSourceVerification();
    }
}
