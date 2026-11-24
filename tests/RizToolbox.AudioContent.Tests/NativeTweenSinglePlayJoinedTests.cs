using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class NativeTweenTypedPlayNativeReaderTests
{
    private static readonly List<object> JoinedPermanent = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint JoinedConfigure(IntPtr invoke, IntPtr play, uint skipPost);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void JoinedPoints(IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void JoinedVoid(IntPtr self, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void JoinedSingle(IntPtr self, float value, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint JoinedOperationInfo(uint kind, uint slot, ref NativeOperationBootstrapInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint JoinedSingleInfo(ref NativeTweenSingleEventInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint JoinedCurrent(IntPtr actual, ref NativeTweenSingleEventFrame copy, ref NativeEventMidFrame machine);

    internal static int RunJoined(string[] args)
    {
        try
        {
            if (args.Length != 3 || !new[] { "identity", "instant", "duplicates", "withdraw", "unknown", "missing-post", "copied-read", "activity-fault" }.Contains(args[1]))
                throw new ArgumentException("Usage: --single-play-joined identity|instant|duplicates|withdraw|unknown|missing-post|copied-read|activity-fault absolute-owned-helper.dll");
            var path = Path.GetFullPath(args[2]);
            JoinedCheck(Path.IsPathFullyQualified(args[2]) && File.Exists(path), "A fresh compiled owned helper is required.");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            JoinedCheck(GetJoinedModule("GameAssembly.dll") == IntPtr.Zero, "A game module was already loaded.");
            using var fixture = new JoinedFixture(path, args[1]);
            fixture.Run();
            JoinedCheck(hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                && GetJoinedModule("GameAssembly.dll") == IntPtr.Zero, "A native input changed or a game module was loaded.");
            var executedParent = args[1] is not ("identity" or "activity-fault");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "single-native-delivery-play-effects-joined", mode = args[1], passed = true,
                helperSha256 = hash, nativeOriginals = fixture.Originals, nativeListeners = fixture.Listeners, nativePlays = fixture.Plays,
                fixture.Deliveries, fixture.ConsumerReads, fixture.WaitedForFirstDuplicateExit, fixture.CompletedSecondDuplicateExit,
                fixture.PrivateLedgerBorrowed, fixture.NativeBeforeReadReject, fixture.NativeCopiedReadRejected,
                fixture.NativeCopiedReadInvalidatedTransfer,
                actualNativeSingleProducerExecuted = executedParent,
                actualNativeTlsReadCurrentVerified = executedParent, actualBridgeDeliveryAndPhysicalScopeVerified = executedParent,
                productionSingleDispatchReaderExecuted = executedParent, productionPlayConsumerReaderExecuted = executedParent,
                privatePlayTransferVerified = args[1] is "instant" or "duplicates" or "withdraw" or "copied-read",
                distinctDuplicateQueueExitsVerified = args[1] is "duplicates" or "withdraw", controlledSourceAdmissionAndDeath = executedParent,
                productionFirstInstallVerified = false, productionGameObjectsVerified = false, productionReadyIssued = false,
                fixtureOwnedSecImageCodeExecuted = executedParent, installed = false, gameExecuted = false }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void JoinedCheck(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T JoinedExport<T>(IntPtr helper, string name) where T : System.Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(helper, name));
    private sealed class JoinedLogger : ILogger
    { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class JoinedNeverInstall : INativeDetourEngine
    { public System.Delegate Install(VerifiedHookTarget target, System.Delegate replacement) => throw new InvalidOperationException("A controlled existing channel cannot install a hook."); }

    private sealed class JoinedFixture : IDisposable
    {
        private readonly string _path, _mode;
        private readonly IntPtr _helper;
        private readonly Fixture _metadata;
        private readonly TweenCompletionFixture _work;
        private readonly JoinedVoid _playCallback;
        private readonly JoinedSingle _entry;
        private readonly JoinedOperationInfo _operationInfo;
        private readonly JoinedSingleInfo _singleInfo;
        private readonly JoinedCurrent _current;
        private readonly NativeTweenSingleEventBridge _bridge;
        private readonly NativeTweenSingleEventSubscriptions<NativeTweenSingleEventBridge.Delivery>.Subscription _subscription;
        private readonly ExactHookBroker.Binding _binding;
        private readonly NativeOperationChannelIdentity _operation;
        private Exception? _callbackFailure;
        private string? _verificationFailure;
        private int _verificationAttempts;
        private NativeTweenSingleEventBridge.Delivery? _saved;
        private readonly List<NativeTweenSingleEventBridge.Delivery> _deliveries = new();
        private bool _withdrew;
        internal int Deliveries, ConsumerReads;
        internal bool WaitedForFirstDuplicateExit, CompletedSecondDuplicateExit, PrivateLedgerBorrowed, NativeCopiedReadRejected;
        internal bool NativeCopiedReadInvalidatedTransfer;
        internal uint NativeBeforeReadReject;
        internal ulong Originals => unchecked((ulong)Marshal.ReadInt64(NativeLibrary.GetExport(_helper, "RtSinglePlayFixture_Originals")));
        internal ulong Listeners => unchecked((ulong)Marshal.ReadInt64(NativeLibrary.GetExport(_helper, "RtSinglePlayFixture_Listeners")));
        internal ulong Plays => unchecked((ulong)Marshal.ReadInt64(NativeLibrary.GetExport(_helper, "RtSinglePlayFixture_Plays")));
        internal JoinedFixture(string path, string mode)
        {
            (_path, _mode) = (path, mode); _helper = NativeLibrary.Load(path); JoinedPermanent.Add(_helper);
            _operationInfo = JoinedExport<JoinedOperationInfo>(_helper, "RizOperationBootstrap_GetInfo");
            _singleInfo = JoinedExport<JoinedSingleInfo>(_helper, "RizTweenSingleEventContext_GetInfo");
            _current = JoinedExport<JoinedCurrent>(_helper, "RizTweenSingleEventContext_ReadCurrent");
            _metadata = new(nativeInvoke: NativeLibrary.GetExport(_helper, "RtSinglePlayFixture_Invoke"),
                nativePlay: NativeLibrary.GetExport(_helper, "RtSinglePlayFixture_Play"));
            _metadata.InitializePlayConsumer(); _metadata.InitializeJoinedList();
            _work = new(typed: true, playing: new long[] { 101, 0, 0, 0 }, working: 1,
                prepare: backend => {
                    backend.UseNativeIdentities(_metadata.PlayObject.ToInt64(), _metadata.JoinedEvent.ToInt64(), _metadata.Play.Pointer);
                    var eventId = _metadata.JoinedEvent.ToInt64();
                    var execList = new NativeSingleExecutingList(eventId, _metadata.JoinedList.ToInt64(),
                        _metadata.JoinedArray.ToInt64(), 1, 1, new[] { _metadata.Invokable.ToInt64(), 0L, 0L, 0L });
                    backend.TypedEventGraph = backend.TypedEventGraph! with {
                        Event = eventId,
                        PersistentGroupCalls = backend.TypedEventGraph.PersistentGroupCalls with { Event = eventId },
                        PersistentCalls = backend.TypedEventGraph.PersistentCalls with { Event = eventId },
                        RuntimeCalls = execList,
                        ExecutingCalls = execList
                    };
                });
            _work.Backend.NativePlayReader = (owner, context, frame, target) => {
                ++ConsumerReads;
                return NativeTweenTypedPlayConsumerReader.Read(_metadata.Api, _metadata.Module, frame, target,
                    new("joined.play", _metadata.Play.Entry, _metadata.Play.Pointer, _metadata.TweenClass, ExactHookAbi.OperationVoid0),
                    _metadata.TweenClass, _metadata.DelegatesField, _metadata.TargetField,
                    _metadata.Root, _metadata.Reference, owner.OwnsTweenSource, _work.Main.VerifyAccess);
            };
            _playCallback = OnPlay; JoinedPermanent.Add(_playCallback);
            JoinedCheck(JoinedExport<JoinedConfigure>(_helper, "RtSinglePlayFixture_Configure")(_metadata.InvokableInvoke.Entry,
                Marshal.GetFunctionPointerForDelegate(_playCallback), mode == "missing-post" ? 1u : 0u) == 0, "Owned native configure failed.");
            var info = OperationInfo();
            _operation = new(NativeOperationKind.Single1, 31, info.Target, info.Original, info.Entry, info.Epoch);
            var points = Marshal.AllocHGlobal(64);
            NativeTweenSingleEventIdentity identity;
            try
            {
                JoinedExport<JoinedPoints>(_helper, "RtSinglePlayFixture_Points")(points);
                identity = new(1, info.Epoch,
                    JoinedExport<FuncEntry>(_helper, "RizTweenSingleEventContext_GetMidEntry")(),
                    JoinedExport<FuncEntry>(_helper, "RizTweenSingleEventContext_GetPreEntry")(),
                    JoinedExport<FuncEntry>(_helper, "RizTweenSingleEventContext_GetPostEntry")(),
                    Marshal.ReadIntPtr(points), Marshal.ReadIntPtr(points, 8), Marshal.ReadIntPtr(points, 16), Marshal.ReadIntPtr(points, 24),
                    Marshal.ReadIntPtr(points, 32), Marshal.ReadIntPtr(points, 40), Marshal.ReadIntPtr(points, 48), Marshal.ReadIntPtr(points, 56));
            }
            finally { Marshal.FreeHGlobal(points); }
            if (mode == "identity")
            {
                var invalidRefused = false;
                try { NativeTweenSingleEventBridge.CreateControlledForTests(IntPtr.Zero, identity, VerifyFixture); }
                catch (ArgumentException) { invalidRefused = true; }
                JoinedCheck(invalidRefused && SingleInfo().Attached == 0, "An absent controlled helper reached native Attach.");
                invalidRefused = false;
                try { NativeTweenSingleEventBridge.CreateControlledForTests(_helper, identity with { Epoch = 2 }, VerifyFixture); }
                catch (UnsupportedSignatureException) { invalidRefused = true; }
                JoinedCheck(invalidRefused && SingleInfo().Attached == 0, "A copied wrong-epoch identity reached native Attach.");
            }
            _bridge = NativeTweenSingleEventBridge.CreateControlledForTests(_helper, identity, VerifyFixture);
            _subscription = _bridge.Subscribe(Observe, Fault);
            var broker = new ExactHookBroker(new JoinedNeverInstall(), _work.Main, new JoinedLogger());
            var target = new VerifiedHookTarget("joined.single", info.Target, new(900), _metadata.EventClass, ExactHookAbi.OperationSingle1Void);
            _binding = broker.BindConfiguredNativeOperationObservation(target, _operation,
                () => NativeBootstrappedOperationBridge.AdoptBootstrap(path, _operation, new JoinedLogger()),
                Capture, Complete, (_, reason) => _callbackFailure ??= new InvalidOperationException(reason));
            _entry = Marshal.GetDelegateForFunctionPointer<JoinedSingle>(_operation.Entry);
            JoinedPermanent.Add(this);
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FuncEntry();
        private NativeOperationBootstrapInformation OperationInfo()
        {
            var result = new NativeOperationBootstrapInformation { Version = 1, Size = 96 };
            JoinedCheck(_operationInfo((uint)NativeOperationKind.Single1, 31, ref result) == 0, "Native operation information failed."); return result;
        }
        private NativeTweenSingleEventInformation SingleInfo()
        {
            var result = new NativeTweenSingleEventInformation { Version = 1, Size = 144 };
            JoinedCheck(_singleInfo(ref result) == 0, "Native Single information failed."); return result;
        }
        private void VerifyFixture()
        {
            Interlocked.Increment(ref _verificationAttempts);
            _work.Main.VerifyAccess();
            JoinedCheck(NativeLibrary.GetExport(_helper, "RtSinglePlayFixture_Configure") != IntPtr.Zero
                && GetJoinedModule("GameAssembly.dll") == IntPtr.Zero, "Controlled fixture identity changed.");
            if (_verificationFailure is { } detail)
                throw new InvalidOperationException(detail, new IOException("Nested Single verification failure."));
        }
        private IDisposable? Capture(NativeOperationFrame frame)
        {
            if (!_work.Owner.HasTypedEventIdentity(frame.Self)) return null;
            var physical = _binding.CaptureSingleSubscriberParent(frame, _bridge.Identity.Epoch);
            var loan = _bridge.BorrowParent(_subscription, physical);
            try
            {
                var context = _work.Owner.CaptureTyped(frame) ?? throw new InvalidOperationException("Actual native Single lost its owned typed membership.");
                return new NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery>(context, frame, loan);
            }
            catch { loan.CaptureFault("Joined fixture capture failed."); throw; }
        }
        private void Complete(IDisposable context, NativeOperationFrame frame, NativeOperationExitFlags flags)
            => ((NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery>)context).Complete(frame, flags, _work.Owner.CompleteTyped);
        private void Observe(NativeTweenSingleEventBridge.Delivery delivery)
        {
            ++Deliveries; _saved = delivery; _deliveries.Add(delivery); delivery.VerifyCurrent();
            JoinedCheck(_binding.WithCurrentSingleContext(delivery, context => {
                var scope = context as NativeTweenSingleEventScope<NativeTweenSingleEventBridge.Delivery>
                    ?? throw new InvalidOperationException("Actual delivery did not find the retained physical scope.");
                NativeTweenSingleDeliveryReader.Observe(_work.Owner, scope, delivery, _metadata.Api, _metadata.Module,
                    new("joined.event", _operation.Target, new(900), _metadata.EventClass, ExactHookAbi.OperationSingle1Void),
                    pointer => { _metadata.Root(pointer.ToInt64()); return pointer.ToInt64(); }, _metadata.Root, _work.Main.VerifyAccess);
            }), "A live native delivery lost its exact current broker context.");
        }
        private void Fault(string reason)
        { _binding?.SingleEventFault(reason); _work.Owner.ObserveTypedSourceFault(reason); }
        private void OnPlay(IntPtr self, IntPtr method)
        {
            try
            {
                JoinedCheck(self == _metadata.PlayObject && method == _metadata.Play.Pointer, "Native closed delegate did not invoke its exact TweenInfo.Play target and MethodInfo.");
                if (_withdrew) return;
                var play = _work.Begin(NativeTweenOperation.Play);
                if (_mode is "duplicates" or "withdraw")
                    for (var index = 0; index != 2; ++index)
                    {
                        var add = _work.Begin(NativeTweenOperation.AddTweenToWaitingList);
                        _work.Backend.SetReady(index == 0 ? new[] { self.ToInt64(), 0L, 0L, 0L }
                            : new[] { self.ToInt64(), self.ToInt64(), 0L, 0L }, index + 1, index + 2);
                        _work.End(add);
                    }
                _work.End(play);
                if (_mode == "withdraw")
                {
                    _subscription.Dispose(); _binding.Dispose(); _withdrew = true;
                    JoinedCheck(!_subscription.IsDrained, "Withdrawal erased a native child and its captured parent before Return.");
                }
            }
            catch (Exception error) { _callbackFailure ??= error; }
        }
        internal void Run()
        {
            JoinedCheck(!_bridge.Production, "A controlled bridge claimed production authority.");
            var refused = false;
            try { _bridge.RequireProduction(); } catch (InvalidOperationException) { refused = true; }
            JoinedCheck(refused, "The production backend gate accepted a controlled bridge.");
            refused = false;
            try { NativeTweenSingleEventBridge.AdoptReady(); } catch (UnsupportedSignatureException) { refused = true; }
            JoinedCheck(refused && SingleInfo().ParentsEntered == 0, "Controlled creation populated the production Ready singleton.");
            if (_mode == "identity")
            {
                _subscription.Dispose(); _binding.Dispose();
                JoinedCheck(_subscription.IsDrained && _bridge.ReadActivity() is { InFlight: 0, Known: true }
                    && Originals == 0 && Listeners == 0 && Plays == 0 && _work.Backend.Frees == 0,
                    "Controlled adoption executed an original, retained a false parent or released owner roots.");
                return;
            }
            if (_mode == "activity-fault")
            {
                CheckActivityFailure();
                return;
            }
            if (_mode == "unknown") Marshal.WriteByte(_metadata.Delegate, 0x70, 1);
            var update = _work.Begin(NativeTweenOperation.Update);
            var call = _work.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            _work.Publish(call); var execute = _work.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            _entry(_metadata.JoinedEvent, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), new(900));
            if (_callbackFailure is { } error) throw new InvalidOperationException("Joined native callback failed.", error);
            JoinedCheck(Originals == 1 && Listeners == 1 && Plays == 1 && Deliveries == 3, "Actual native producer/original/delivery counts differ.");
            JoinedCheck(_saved is not null && _deliveries.All(delivery => delivery.Closed), "A delivery escaped its synchronous loan.");
            foreach (var delivery in _deliveries)
            {
                refused = false; try { delivery.VerifyCurrent(); } catch (InvalidOperationException) { refused = true; }
                JoinedCheck(refused, "A stale live Delivery was usable after its native callback.");
            }
            var native = SingleInfo();
            JoinedCheck(native.ParentsEntered == 1 && native.ParentsExited == 1 && native.ChildrenEntered == 1 && native.ChildrenExited == 1
                && native.ParentsInFlight == 0 && native.ChildrenInFlight == 0, "Native parent/child responsibility did not pair.");
            var dispatch = _work.Owner.ReadTypedExecutingLists().Single().Dispatches.Single();
            if (_mode == "missing-post")
            {
                JoinedCheck(dispatch.Abnormal && dispatch.Transfer is null && dispatch.Pending.Contains("missing-post")
                    && ConsumerReads == 1 && native.MissingPosts == 1 && native.Unknown == 1,
                    "Missing native post was treated as completed Play effects.");
            }
            else
                JoinedCheck(dispatch.Returned && ConsumerReads == (_mode == "unknown" ? 1 : 2) && native.Unknown == 0 && native.MissingPosts == 0
                    && (dispatch.Transfer is null) == (_mode == "unknown"), "Actual native return did not preserve the precise reader/transfer result: "
                        + JsonSerializer.Serialize(new { dispatch, ConsumerReads, native.Unknown, native.MissingPosts }));
            _work.End(execute); var after = call; after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired;
            _work.Publish(after); _work.Clear(update, 0); _work.EndUpdate(update, new long[4], 0);
            _work.ConfirmDeaths();
            if (_mode is "duplicates" or "withdraw")
            {
                JoinedCheck(dispatch.Transfer is { Occurrences.Count: 3 } && dispatch.Transfer.Occurrences.Distinct().Count() == 3
                    && !_work.Borrow(), "Transfer merged duplicate queue occurrences or completed them on native return/death.");
                DrainOne(2); WaitedForFirstDuplicateExit = !_work.Borrow();
                JoinedCheck(WaitedForFirstDuplicateExit, "One duplicate queue exit completed the other occurrence.");
                DrainOne(1); CompletedSecondDuplicateExit = PrivateLedgerBorrowed = _work.Borrow();
                JoinedCheck(CompletedSecondDuplicateExit, "Every exact queue occurrence exited but the transferred ledger remained pending.");
            }
            else
            {
                PrivateLedgerBorrowed = _work.Borrow();
                JoinedCheck(PrivateLedgerBorrowed == (_mode is "instant" or "copied-read"),
                    "Unknown/missing-post effects or an exact instant transfer has the wrong completion result.");
            }
            if (!_withdrew) { _subscription.Dispose(); _binding.Dispose(); }
            JoinedCheck(_subscription.IsDrained && _work.Backend.Frees == 0 && _work.Backend.Native.Unregisters == 0,
                "The joined path leaked its parent loan or released owner roots/admission from a transfer.");
            if (_mode == "withdraw")
            {
                var beforeCount = Deliveries;
                _entry(_metadata.JoinedEvent, 0.5f, new(900));
                // The original remains callable; it cannot borrow a withdrawn run again.
                JoinedCheck(Deliveries == beforeCount && Originals == 2, "A withdrawn subscriber captured another native parent.");
            }
            NativeBeforeReadReject = SingleInfo().Unknown;
            if (_mode == "copied-read")
            {
                // Native rejection is intentionally destructive and owns a separate process.
                var copied = _saved!.Observation.Frame; var machine = new NativeEventMidFrame { Version = 1, Size = 432 };
                var memory = Marshal.AllocHGlobal(200);
                try { Marshal.StructureToPtr(copied, memory, false); NativeCopiedReadRejected = _current(memory, ref copied, ref machine) == 3; }
                finally { Marshal.FreeHGlobal(memory); }
                JoinedCheck(NativeBeforeReadReject == 0 && NativeCopiedReadRejected && SingleInfo().Unknown == 1,
                    "A copied frame bypassed native TLS ReadCurrent or erased sticky unknown responsibility.");
                _work.Owner.ObserveTypedSourceFault("The actual controlled native producer rejected a copied ReadCurrent frame.");
                var invalidated = _work.Owner.ReadTypedExecutingLists().Single().Dispatches.Single();
                NativeCopiedReadInvalidatedTransfer = invalidated.Transfer is null && invalidated.Pending.Length != 0;
                JoinedCheck(NativeCopiedReadInvalidatedTransfer, "A source fault left a blank successful-looking row after invalidating its private Play transfer.");
            }
        }
        private void CheckActivityFailure()
        {
            var faults = new List<string>();
            using var observer = _bridge.Subscribe(_ => throw new InvalidOperationException("Activity read delivered a Single callback."), faults.Add);
            JoinedCheck(_bridge.ReadActivity() is { InFlight: 0, Known: true }, "A healthy Single activity read was rejected.");
            var attemptsBeforeFailure = _verificationAttempts;

            _verificationFailure = "First Single verification failure.";
            JoinedCheck(_bridge.ReadActivity() is { InFlight: 0, Known: false }, "Failed Single verification published known activity.");
            _verificationFailure = "Later Single verification failure.";
            JoinedCheck(_bridge.ReadActivity() is { InFlight: 0, Known: false }, "Repeated failure changed unknown activity.");
            _verificationFailure = null;
            JoinedCheck(_bridge.ReadActivity() is { InFlight: 0, Known: false }, "A successful reread cleared sticky unknown activity.");
            var worker = Task.Run(_bridge.ReadActivity).GetAwaiter().GetResult();
            JoinedCheck(worker is { InFlight: 0, Known: false } && _verificationAttempts == attemptsBeforeFailure + 4,
                "Single activity stopped using the existing verifier and its thread guard.");

            JoinedCheck(faults.Count == 1 && !observer.ReadActivity().Known && !_subscription.ReadActivity().Known,
                "Single activity fault did not reach the existing subscriptions exactly once.");
            var first = faults[0];
            JoinedCheck(first.StartsWith("Single producer activity is unknown during producer verification.", StringComparison.Ordinal)
                && first.Contains("System.InvalidOperationException: First Single verification failure.", StringComparison.Ordinal)
                && first.Contains("System.IO.IOException: Nested Single verification failure.", StringComparison.Ordinal)
                && first.Contains(nameof(VerifyFixture), StringComparison.Ordinal)
                && !first.Contains("Later Single verification failure.", StringComparison.Ordinal),
                "First Single verification failure lost its stage or original exception: " + first);
            var native = SingleInfo();
            JoinedCheck(native is { Attached: 1, Unknown: 0, ParentsEntered: 0, ChildrenEntered: 0 },
                "A managed validation refusal changed the controlled native producer.");
        }

        private void DrainOne(int count)
        {
            var tween = _metadata.PlayObject.ToInt64(); var update = _work.Begin(NativeTweenOperation.Update); var version = 5 - count;
            var pop = _work.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: count - 1, count: count, version: version);
            _work.Pair(pop, count: count - 1, version: version + 1, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
            _work.Pair(_work.Before(update, NativeTweenSourceSite.PlayingInsertFront, 303, 303, destination: 0,
                count: 4, version: 0, occurrence: pop.Transaction)); _work.Clear(update, 0);
            _work.Backend.SetReady(count == 2 ? new[] { tween, 0L, 0L, 0L } : new long[4], count - 1, version + 1);
            _work.EndUpdate(update, new long[4], 0);
        }
        public void Dispose() { _metadata.Dispose(); _work.Dispose(); }
    }

    private sealed partial class Fixture
    {
        internal IntPtr JoinedEvent, JoinedList, JoinedArray, JoinedElementClass, JoinedArrayClass;
        internal void InitializeJoinedList()
        {
            JoinedElementClass = ClassOf(AddType("UnityEngine.Events.BaseInvokableCall", 18, false, 8));
            _classes[InvokableClass].Parent = JoinedElementClass;
            JoinedArrayClass = ClassOf(AddType("UnityEngine.Events.BaseInvokableCall[]", 29, false, 8));
            AddField(ListClass, "_items", _classes[JoinedArrayClass].Type, 0x10);
            AddField(ListClass, "_size", IntType, 0x18); AddField(ListClass, "_version", IntType, 0x1c);
            JoinedEvent = Allocate(0x80); Marshal.WriteIntPtr(JoinedEvent, EventClass);
            JoinedList = Allocate(0x80); Marshal.WriteIntPtr(JoinedList, ListClass);
            JoinedArray = Allocate(64); Marshal.WriteIntPtr(JoinedArray, JoinedArrayClass); Marshal.WriteIntPtr(JoinedArray, 24, new(4));
            Marshal.WriteIntPtr(JoinedArray, 32, Invokable); Marshal.WriteIntPtr(JoinedList, 0x10, JoinedArray);
            Marshal.WriteInt32(JoinedList, 0x18, 1); Marshal.WriteInt32(JoinedList, 0x1c, 1); Marshal.WriteIntPtr(JoinedEvent, 0x10, JoinedList);
            Bind("il2cpp_class_get_rank", "Flags", nameof(JoinedRank));
            Bind("il2cpp_class_get_element_class", "Ptr1", nameof(JoinedElement));
            Bind("il2cpp_array_element_size", "ArrayElementSize", nameof(JoinedElementSize));
            Bind("il2cpp_array_length", "ArraySize", nameof(JoinedArrayLength));
            Bind("il2cpp_array_get_byte_length", "ArraySize", nameof(JoinedArrayBytes));
            Bind("il2cpp_object_get_size", "ArraySize", nameof(JoinedObjectSize));
            Bind("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(JoinedHeader));
        }
        private uint JoinedRank(IntPtr cls) => cls == JoinedArrayClass ? 1u : 0u;
        private IntPtr JoinedElement(IntPtr cls) => cls == JoinedArrayClass ? JoinedElementClass : IntPtr.Zero;
        private int JoinedElementSize(IntPtr cls) => cls == JoinedArrayClass ? 8 : 0;
        private uint JoinedArrayLength(IntPtr value) => value == JoinedArray ? 4u : 0u;
        private uint JoinedArrayBytes(IntPtr value) => value == JoinedArray ? 32u : 0u;
        private uint JoinedObjectSize(IntPtr value) => value == JoinedArray ? 64u : 128u;
        private uint JoinedHeader() => 32;
    }
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetJoinedModule(string name);
}
