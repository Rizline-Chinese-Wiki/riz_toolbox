namespace RizToolbox.Runtime.Hooks;

/// <summary>The production owner must bind promotion to its private current
/// native operation and already-proved local parent or listener responsibility.</summary>
internal interface INativeTweenSourceOperationOwner : INativeTweenSourceOwner
{
    void VerifyTweenSourceOperationPromotion(NativeOperationFrame frame, NativeRuntime.RootHandle root);
    void VerifyTweenSourceOperationManagerPromotion(NativeOperationFrame frame,
        NativeRuntime.RootHandle managerRoot, NativeRuntime.RootHandle tweenRoot);
}

internal sealed partial class NativeTweenSource
{
    private static readonly Dictionary<(IntPtr Helper, IntPtr Module, ulong Epoch), SourceReferenceHub> ReferenceHubs = new();
    private NativeTweenSourceReferences? _references;

    /// <summary>Extends ownership from one exact already-rooted Before input.
    /// A copied journal address cannot enter this path. The new anchor applies
    /// only after its current native journal watermark, never retroactively.</summary>
    internal void PromoteCurrentOperationTweenRoot(NativeTweenSourceRootSet roots,
        NativeRuntime.RootHandle root, NativeOperationFrame frame)
    {
        _verify(); ArgumentNullException.ThrowIfNull(root); VerifySet(roots); RequireKnown();
        roots.RequireEnrollmentOpen();
        roots.Owner.VerifyTweenSourceOwner();
        try
        {
            var identity = ExactHookBroker.BorrowedReference(frame, root.Target);
            ExactHookBroker.OperationRootReceipt? receipt = null;
            if (Production)
            {
                if (roots.Owner is not INativeTweenSourceOperationOwner actualOwner)
                    throw new InvalidOperationException("The production source owner has no private native operation promotion authority.");
                actualOwner.VerifyTweenSourceOperationPromotion(frame, root);
                receipt = ExactHookBroker.CaptureCurrentOperationRoot(frame, root, actualOwner, production: true);
            }
            if (roots.TryGetAnchorKind(identity, out var anchorKind))
            {
                if (anchorKind != NativeTweenSourceObjectKind.Tween)
                    throw new UnsupportedSignatureException("The operation root already owns an incompatible source anchor role.");
                return;
            }
            var watermark = ReadInformation();
            // Include rows not yet consumed by this reader. Root allocation or
            // later ownership cannot repair a prior unowned Play/Execute scope.
            if (watermark.Events < roots.LastReadSequence
                || watermark.Events - roots.LastReadSequence > watermark.RetainedRecords)
                throw new UnsupportedSignatureException("Dynamic source promotion lost its continuous native journal interval.");
            var unread = watermark.Events - roots.LastReadSequence;
            for (ulong offset = 1; offset <= unread; ++offset)
            {
                var sequence = checked(roots.LastReadSequence + offset);
                var row = new NativeTweenSourceRecord { Version = 1, Size = 192 };
                if (_native.ReadRecord(_epoch, sequence, ref row) != 0 || !RecordShape(row, _epoch, sequence))
                    throw new UnsupportedSignatureException("Dynamic source promotion found a missing or unknown earlier source row.");
                _production?.VerifyRecordBinding(row); roots.ObserveUnownedTweenOperation(row, _issuer);
            }
            if (roots.HasUnownedTweenOperation(identity))
                throw new UnsupportedSignatureException("The dynamic Tween already executed outside this owner's first covered source interval.");

            // Reserve responsibility before class inspection. No Root is called:
            // this is the backend's same live native-input handle.
            var entry = roots.ReserveOperationAnchor(identity, NativeTweenSourceObjectKind.Tween,
                root, frame, watermark.Events, receipt, null, null, _issuer);
            if (_validate(root, NativeTweenSourceObjectKind.Tween) != identity || root.Target != identity)
                throw new UnsupportedSignatureException("The operation source root changed its exact Tween identity or class.");
            receipt?.VerifyCurrent(); roots.Owner.VerifyTweenSourceOwner();
            if (Production) ((INativeTweenSourceOperationOwner)roots.Owner).VerifyTweenSourceOperationPromotion(frame, root);
            entry.OperationAnchor!.Complete(_issuer);
        }
        catch { Fault(); throw; }
    }

    /// <summary>An Add/Execute receiver becomes an ownership anchor only when
    /// its exact current reference argument already belongs to this owner.
    /// Queue contents remain retained diagnostics and gain no Tween ownership.</summary>
    internal void PromoteCurrentOperationManagerRoot(NativeTweenSourceRootSet roots,
        NativeRuntime.RootHandle managerRoot, NativeRuntime.RootHandle tweenRoot, NativeOperationFrame frame)
    {
        _verify(); ArgumentNullException.ThrowIfNull(managerRoot); ArgumentNullException.ThrowIfNull(tweenRoot);
        VerifySet(roots); RequireKnown(); roots.Owner.VerifyTweenSourceOwner();
        roots.RequireEnrollmentOpen();
        try
        {
            var manager = managerRoot.Target; var tween = tweenRoot.Target;
            if (!frame.IsValid || frame.Kind != NativeOperationKind.Reference1 || manager != frame.Self || tween != frame.Arg0
                || !roots.HasTweenOwnershipAnchor(tween.ToInt64()))
                throw new InvalidOperationException("Manager source promotion requires its exact current receiver and already-owned Tween reference argument.");
            ExactHookBroker.OperationRootReceipt? managerReceipt = null, tweenReceipt = null;
            if (Production)
            {
                if (roots.Owner is not INativeTweenSourceOperationOwner actualOwner)
                    throw new InvalidOperationException("The production Manager source has no private operation promotion owner.");
                actualOwner.VerifyTweenSourceOperationManagerPromotion(frame, managerRoot, tweenRoot);
                managerReceipt = ExactHookBroker.CaptureCurrentOperationRoot(frame, managerRoot, actualOwner, production: true);
                tweenReceipt = ExactHookBroker.CaptureCurrentOperationRoot(frame, tweenRoot, actualOwner, production: true);
            }
            if (roots.TryGetAnchorKind(manager, out var kind))
            {
                if (kind != NativeTweenSourceObjectKind.Manager)
                    throw new UnsupportedSignatureException("The current Manager receiver already owns another source role.");
                return;
            }
            var watermark = ReadInformation();
            if (watermark.Events < roots.LastReadSequence || watermark.Events - roots.LastReadSequence > watermark.RetainedRecords)
                throw new UnsupportedSignatureException("Manager source promotion lost its continuous native journal interval.");
            var entry = roots.ReserveOperationAnchor(manager, NativeTweenSourceObjectKind.Manager,
                managerRoot, frame, watermark.Events, managerReceipt, tweenRoot, tweenReceipt, _issuer);
            if (_validate(managerRoot, NativeTweenSourceObjectKind.Manager) != manager || managerRoot.Target != manager
                || _validate(tweenRoot, NativeTweenSourceObjectKind.Tween) != tween || tweenRoot.Target != tween)
                throw new UnsupportedSignatureException("The current Manager source receiver or owned argument changed its exact class or identity.");
            managerReceipt?.VerifyCurrent(); tweenReceipt?.VerifyCurrent(); roots.Owner.VerifyTweenSourceOwner();
            if (Production) ((INativeTweenSourceOperationOwner)roots.Owner).VerifyTweenSourceOperationManagerPromotion(frame, managerRoot, tweenRoot);
            entry.OperationAnchor!.Complete(_issuer);
        }
        catch { Fault(); throw; }
    }

    internal sealed class OperationRootAnchor
    {
        private readonly NativeTweenSource _reader;
        private readonly NativeTweenSourceRootSet _roots;
        private readonly object _issuer;
        private readonly ExactHookBroker.OperationRootReceipt? _receipt;
        private readonly ExactHookBroker.OperationRootReceipt? _tweenReceipt;
        private readonly NativeRuntime.RootHandle? _issuingTweenRoot;
        private readonly NativeOperationFrame _frame;
        private readonly IntPtr _identity;
        private readonly IntPtr _issuingTween;
        private readonly NativeTweenSourceObjectKind _kind;
        private bool _complete;
        private bool _withdrawn;
        private NativeRuntime.RootHandle? _root;
        internal NativeRuntime.RootHandle Root => !_withdrawn && _root is { } retained ? retained
            : throw new TargetExpiredException("The source operation anchor released its root borrowing interval.");
        internal ulong AfterSequence { get; }
        internal OperationRootAnchor(NativeTweenSource reader, NativeTweenSourceRootSet roots,
            NativeTweenSourceObjectKind kind, NativeRuntime.RootHandle root, NativeOperationFrame frame, ulong watermark,
            ExactHookBroker.OperationRootReceipt? receipt, NativeRuntime.RootHandle? issuingTweenRoot,
            ExactHookBroker.OperationRootReceipt? tweenReceipt, object issuer)
        {
            if (!reader.IsIssuer(issuer)) throw new InvalidOperationException("Foreign operation source anchor issuer.");
            (_reader, _roots, _root, _frame, AfterSequence, _receipt, _issuer, _identity)
                = (reader, roots, root, frame, watermark, receipt, issuer, root.Target);
            (_kind, _issuingTweenRoot, _tweenReceipt, _issuingTween)
                = (kind, issuingTweenRoot, tweenReceipt, issuingTweenRoot?.Target ?? IntPtr.Zero);
        }
        internal void Complete(object issuer)
        { if (!ReferenceEquals(issuer, _issuer)) throw new InvalidOperationException("Foreign operation anchor completion issuer."); _complete = true; }
        internal bool Ready => _complete;
        internal void VerifyRetained(NativeTweenSource reader, NativeTweenSourceRootSet roots, IntPtr identity)
        {
            if (_withdrawn || roots.Retired || !ReferenceEquals(reader, _reader) || !ReferenceEquals(roots, _roots) || !reader.IsIssuer(_issuer) || !_complete
                || _identity != identity || Root.Target != identity || ExactHookBroker.BorrowedReference(_frame, identity) != identity
                || reader.Production != (_receipt?.Production == true))
                throw new UnsupportedSignatureException("A dynamic source ownership anchor lost its private retained operation receipt.");
            if (_receipt is not null)
            {
                _receipt.VerifyRetained();
                if (!ReferenceEquals(_receipt.Root, Root) || !ReferenceEquals(_receipt.Owner, roots.Owner))
                    throw new UnsupportedSignatureException("A dynamic source anchor crossed its exact retained owner or input root.");
            }
            if (_kind == NativeTweenSourceObjectKind.Manager)
            {
                if (_issuingTweenRoot is null || _issuingTween == IntPtr.Zero || _issuingTweenRoot.Target != _issuingTween
                    || _frame.Kind != NativeOperationKind.Reference1 || _frame.Self != identity || _frame.Arg0 != _issuingTween
                    || !roots.HasTweenOwnershipAnchor(_issuingTween.ToInt64()) || reader.Production != (_tweenReceipt?.Production == true))
                    throw new UnsupportedSignatureException("A Manager source anchor lost its exact retained owned Tween argument.");
                if (_tweenReceipt is not null)
                {
                    _tweenReceipt.VerifyRetained();
                    if (!ReferenceEquals(_tweenReceipt.Root, _issuingTweenRoot) || !ReferenceEquals(_tweenReceipt.Owner, roots.Owner))
                        throw new UnsupportedSignatureException("A Manager source anchor crossed its original owned argument root or owner.");
                }
            }
            else if (_kind != NativeTweenSourceObjectKind.Tween || _issuingTweenRoot is not null || _tweenReceipt is not null)
                throw new UnsupportedSignatureException("A source ownership anchor changed its exact operation role.");
        }
        internal void Withdraw(object issuer)
        { if (!ReferenceEquals(issuer, _issuer) || _withdrawn) throw new InvalidOperationException("A source operation anchor cannot be withdrawn twice."); _withdrawn = true; _root = null; }
    }

    private void AttachProductionReferences(NativeRuntime runtime, NativeFirstInstall.TweenSourceReady ready)
    {
        _verify(); ready.VerifyReady();
        var key = (ready.HelperModule, ready.Module, ready.GetSourceEpoch());
        if (key.Item3 != _epoch) throw new UnsupportedSignatureException("Source journal and synchronous references have different actual epochs.");
        SourceReferenceHub hub;
        lock (ProcessOwners)
        {
            if (!ReferenceHubs.TryGetValue(key, out hub!))
            {
                hub = new SourceReferenceHub();
                // Keep failed attachment responsibility too. A subsequent reader
                // cannot replace the native once-only observer after a failure.
                ReferenceHubs.Add(key, hub);
            }
        }
        hub.Bind(runtime, ready, _verify);
        _references = hub.Owner;
        if (!_references.Production || _references.Unknown) throw new UnsupportedSignatureException("The actual source reference observer is unavailable.");
        _referenceRegistration = _references.RegisterReader(this, _issuer); _referenceHub = hub; hub.Add(this);
    }

    internal void BindControlledReferencesForTests(NativeTweenSourceReferences references)
    {
        _verify(); ArgumentNullException.ThrowIfNull(references);
        if (Production || references.Production || _references is not null || references.Unknown)
            throw new InvalidOperationException("Only an unbound controlled reader can select its controlled source reference observer.");
        _references = references;
        _referenceRegistration = references.RegisterReader(this, _issuer);
    }

    /// <summary>Only a private receipt in its real callback scope can extend a
    /// root set. A copied journal row can never request a late Root allocation.</summary>
    internal void ObserveSynchronousReferences(NativeTweenSourceReferences.Receipt receipt)
    {
        _verify(); ArgumentNullException.ThrowIfNull(receipt);
        var observer = _references ?? throw new InvalidOperationException("No exact source reference observer belongs to this reader.");
        receipt.VerifyCurrent(observer);
        if (_referenceRegistration is null || !receipt.IncludesConsumer(_referenceRegistration)) return;
        if (Volatile.Read(ref _unknown) != 0) return;
        try
        {
            if (receipt.Origin.SourceEpoch != _epoch || receipt.Production != Production)
                throw new UnsupportedSignatureException("A synchronous source receipt crossed this reader's actual producer.");
            if (!receipt.Known)
            {
                Fault(); return; // The observer already retains every new root.
            }
            var rows = receipt.ScopeReferences;
            var relevant = _sets.Where(set => set.Ready && set.EnrollmentOpen && receipt.IncludesConsumer(_referenceRegistration, set) && rows.Any(row =>
                row.Identity != IntPtr.Zero && set.TryGetAnchorKind(row.Identity, out var kind)
                && (row.Role == NativeTweenSourceReferenceRole.Manager && kind == NativeTweenSourceObjectKind.Manager
                    || row.Role == NativeTweenSourceReferenceRole.Tween && kind == NativeTweenSourceObjectKind.Tween))).ToArray();
            // Scope history identifies the original pre-retained owner only.
            // It cannot replay an earlier capture as this notice's new root.
            var captures = receipt.References;
            if (relevant.Length == 0 || captures.Count == 0) return;

            // Native source locks are released for this callback. All references
            // in this notice were already rooted and class-checked by the bridge.
            // Record the real journal watermark now: even a genuine later root
            // must never authorize an older, already-published source row.
            var watermark = ReadInformation().Events;
            foreach (var set in relevant)
            {
                set.Owner.VerifyTweenSourceOwner();
                foreach (var row in captures)
                {
                    if (row.Identity == IntPtr.Zero) continue;
                    var root = row.Root ?? throw new TargetExpiredException("The synchronous observer lost a returned independent root.");
                    if (set.TryGetKind(row.Identity, out var existing))
                    {
                        if (existing != row.Kind) throw new UnsupportedSignatureException("One actual source reference changed its retained object class.");
                        continue;
                    }
                    var entry = set.ReserveSynchronousRoot(row.Identity, row.Kind, watermark, receipt, row, _issuer);
                    entry.ReferenceBorrow = observer.BorrowRoot(receipt, row, _referenceRegistration, set, _issuer);
                    // Share an already-held strong handle, never an identity
                    // cache or a second root request after the actual callback.
                    set.RecordRoot(entry, root, _issuer);
                }
            }
        }
        catch { Fault(); throw; }
    }

    private sealed class SourceReferenceHub
    {
        private readonly List<NativeTweenSource> _readers = new();
        private readonly int _managedThread = Environment.CurrentManagedThreadId;
        private readonly uint _nativeThread = NativeTweenSourceReferences.CurrentThreadId();
        private NativeTweenSourceReferences? _owner;
        private int _attempted;
        internal NativeTweenSourceReferences Owner
        {
            get { VerifyThread(); return _owner ?? throw new UnsupportedSignatureException("The process source reference attachment did not complete."); }
        }
        internal void Bind(NativeRuntime runtime, NativeFirstInstall.TweenSourceReady ready, Action verify)
        {
            VerifyThread(); verify();
            if (Interlocked.CompareExchange(ref _attempted, 1, 0) != 0)
            { if (Owner.Unknown) throw new UnsupportedSignatureException("The once-only process source observer is unknown."); return; }
            _owner = NativeTweenSourceReferences.BindProduction(runtime, ready, verify, Observe);
        }
        internal void Add(NativeTweenSource reader)
        {
            VerifyThread();
            if (_readers.Contains(reader)) throw new InvalidOperationException("The source reader was registered twice.");
            _readers.Add(reader);
        }
        internal void Remove(NativeTweenSource reader)
        {
            VerifyThread(); if (!_readers.Remove(reader)) throw new InvalidOperationException("The exact source-hub reader was already withdrawn.");
        }
        private void Observe(NativeTweenSourceReferences.Receipt receipt)
        {
            VerifyThread();
            receipt.VerifyCurrent(Owner);
            foreach (var reader in _readers.ToArray()) reader.ObserveSynchronousReferences(receipt);
        }
        private void VerifyThread()
        {
            if (Environment.CurrentManagedThreadId != _managedThread || NativeTweenSourceReferences.CurrentThreadId() != _nativeThread)
                throw new InvalidOperationException("The process source reference hub belongs to its first actual Unity lifecycle OS thread.");
        }
    }
}

internal sealed partial class NativeTweenSourceRootSet
{
    private readonly HashSet<IntPtr> _unownedTweenOperations = new();

    internal void ObserveUnownedTweenOperation(NativeTweenSourceRecord row, object issuer)
    {
        Verify(issuer);
        if (row.Tween == IntPtr.Zero || row.Operation is not (NativeTweenSourceOperation.Play or NativeTweenSourceOperation.Execute)
            || TryGetAnchorKindAt(row.Tween, row.EventSequence, out var kind) && kind == NativeTweenSourceObjectKind.Tween) return;
        if (_unownedTweenOperations.Count >= 32768 && !_unownedTweenOperations.Contains(row.Tween))
            throw new UnsupportedSignatureException("The complete unowned source-call scalar history exceeded its bound.");
        _unownedTweenOperations.Add(row.Tween);
    }

    internal bool HasUnownedTweenOperation(IntPtr identity) => _unownedTweenOperations.Contains(identity);

    internal Entry ReserveOperationAnchor(IntPtr identity, NativeTweenSourceObjectKind kind,
        NativeRuntime.RootHandle root, NativeOperationFrame frame, ulong watermark,
        ExactHookBroker.OperationRootReceipt? receipt, NativeRuntime.RootHandle? issuingTweenRoot,
        ExactHookBroker.OperationRootReceipt? tweenReceipt, object issuer)
    {
        Verify(issuer);
        RequireEnrollmentOpen();
        if (_entries.Count >= 32768 && !_entries.ContainsKey(identity))
            throw new UnsupportedSignatureException("The complete operation source root responsibility exceeded its bound.");
        if (!_entries.TryGetValue(identity, out var entry))
        {
            entry = ReserveRoot(identity, kind, issuer);
            entry.Retention = NativeTweenSourceRootRetention.OperationBorrow;
            RecordRoot(entry, root, issuer);
            // This is a borrowed operation root, not an initial preactivation
            // root. Neither relevance nor token retention applies to older rows.
            entry.ReferenceAfterSequence = watermark;
        }
        if (entry.Kind != kind || kind is not (NativeTweenSourceObjectKind.Tween or NativeTweenSourceObjectKind.Manager) || entry.OperationAnchor is not null)
            throw new InvalidOperationException("The operation source anchor repeats or changes a retained source role.");
        entry.OperationAnchor = new NativeTweenSource.OperationRootAnchor(Reader, this, kind, root, frame,
            watermark, receipt, issuingTweenRoot, tweenReceipt, issuer);
        return entry;
    }
    internal Entry ReserveSynchronousRoot(IntPtr identity, NativeTweenSourceObjectKind kind,
        ulong watermark, NativeTweenSourceReferences.Receipt receipt, NativeTweenSourceReferences.RootedReference capture, object issuer)
    {
        Verify(issuer);
        RequireEnrollmentOpen();
        if (_entries.Count >= 32768) throw new UnsupportedSignatureException("The complete synchronous source root responsibility exceeded its bound.");
        if (!receipt.References.Contains(capture) || capture.Identity != identity || capture.Kind != kind)
            throw new InvalidOperationException("A dynamic source root requires its actual current notice occurrence.");
        var entry = ReserveRoot(identity, kind, issuer);
        entry.Retention = NativeTweenSourceRootRetention.ReferenceHubBorrow;
        entry.ReferenceAfterSequence = watermark; entry.ReferenceReceipt = receipt; entry.ReferenceCapture = capture; return entry;
    }

    internal bool TryGetAnchorKind(IntPtr identity, out NativeTweenSourceObjectKind kind)
    {
        if (!Retired && !Releasing && _entries.TryGetValue(identity, out var entry) && entry.Root is not null
            && (entry.InitialOwnershipAnchor || entry.OperationAnchor is { Ready: true }))
        { kind = entry.Kind; return true; }
        kind = default; return false;
    }

    internal bool TryGetAnchorKindAt(IntPtr identity, ulong eventSequence, out NativeTweenSourceObjectKind kind)
    {
        if (!Retired && !Releasing && _entries.TryGetValue(identity, out var entry) && entry.Root is not null
            && (entry.InitialOwnershipAnchor || entry.OperationAnchor is { Ready: true } anchor && eventSequence > anchor.AfterSequence))
        { kind = entry.Kind; return true; }
        kind = default; return false;
    }

    internal bool HasTweenOwnershipAnchor(long identity)
        => TryGetAnchorKind(new IntPtr(identity), out var kind) && kind == NativeTweenSourceObjectKind.Tween;

    internal bool HasRetainedObjectAt(IntPtr identity, ulong eventSequence)
        => !Retired && !Releasing && _entries.TryGetValue(identity, out var entry) && entry.Root is not null
            && (entry.ReferenceReceipt is null && entry.OperationAnchor is null || eventSequence > entry.ReferenceAfterSequence);
}
