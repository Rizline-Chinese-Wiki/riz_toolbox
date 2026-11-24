using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

internal static partial class NativeFirstInstallMappedRegression
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ActualSingleProducerInfo
    {
        internal uint Version, Size, Configured, Attached, Unknown, ParentCapacity;
        internal ulong Epoch, ParentEpoch, ParentsEntered, ParentsExited, ParentsInFlight, Mids;
        internal ulong ChildrenEntered, ChildrenExited, ChildrenInFlight, CallbackFaults, RejectedReads, MissingPosts;
        internal ulong RetainedParents, RetainedChildren, Overflow;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadSingleInfoNative(ref ActualSingleProducerInfo info);

    private static object ValidateSingleEventPublication(NativeFirstInstall producer, IntPtr helper, IntPtr module,
        Action<bool, string> check)
    {
        var value = producer.SingleEventPublication;
        var parent = producer.OperationPublication.Row25;
        check(Marshal.SizeOf<NativeTweenSingleEventPublication>() == 416 && Marshal.SizeOf<ActualSingleProducerInfo>() == 144
            && NativeSingleEventPublication.HasShape(value, producer.OperationPublication.Listener.Common, parent),
            "The actual Single publication has no complete current managed layout and parent identity.");
        var read = Export<ReadSingleInfoNative>(helper, "RizTweenSingleEventContext_GetInfo");
        var native = new ActualSingleProducerInfo { Version = 1, Size = 144 };
        check(read(ref native) == 0 && native.Configured == 1 && native.Attached == 0 && native.Unknown == 0
            && native.ParentCapacity == 128 && native.Epoch == value.Epoch && native.ParentEpoch == parent.Epoch,
            "The actual Single producer lane differs from its pre-init publication.");
        check(native.ParentsEntered == 0 && native.ParentsExited == 0 && native.ParentsInFlight == 0 && native.Mids == 0
            && native.ChildrenEntered == 0 && native.ChildrenExited == 0 && native.ChildrenInFlight == 0
            && native.CallbackFaults == 0 && native.RejectedReads == 0 && native.MissingPosts == 0
            && native.RetainedParents == 0 && native.RetainedChildren == 0 && native.Overflow == 0,
            "A mapped read-only adoption unexpectedly executed the Single source or a callback.");
        var block = Bytes(value.Allocation, 4096);
        var spans = Enumerable.Range(0, 7).Select(i => NativeSingleEventPublication.Span(value, i)).ToArray();
        var source = spans.Select(span => Bytes(span.Pc, checked((int)span.Width))).ToArray();
        // The production owner independently reconstructs all 4096 bytes and
        // the seven patches, then checks the OS's 15 actual RF lookups.
        producer.VerifyReady();
        check(value.Equals(producer.SingleEventPublication) && block.SequenceEqual(Bytes(value.Allocation, 4096)),
            "Single CLR adoption changed the retained native publication block.");
        for (var i = 0; i < 7; ++i)
            check(source[i].SequenceEqual(Bytes(spans[i].Pc, checked((int)spans[i].Width))),
                "Single CLR adoption changed an already published source span.");
        return new {
            capabilityVersion = value.CapabilityVersion, byteSize = value.ByteSize, phase = value.Phase, unknown = value.Unknown,
            operationId = value.OperationId, kind = value.Kind, slot = value.Slot, expectedWrites = value.ExpectedWrites,
            epoch = value.Epoch, parentEpoch = value.ParentEpoch, parentRowEpoch = parent.Epoch,
            configuredMask = value.ConfiguredMask, publishedMask = value.PublishedMask,
            attemptedWrites = value.AttemptedWrites, completedWrites = value.CompletedWrites,
            registeredFunctionCount = value.RegisteredFunctionCount, lookupChecks = value.LookupChecks, boundaryChecks = value.BoundaryChecks,
            spanRvas = spans.Select(span => checked((uint)(span.Pc.ToInt64() - module.ToInt64()))).ToArray(),
            spanWidths = spans.Select(span => span.Width).ToArray(),
            stubSizes = Enumerable.Range(0, 3).Select(i => NativeSingleEventPublication.Site(value, i).StubSize).ToArray(),
            unwindSizes = Enumerable.Range(0, 3).Select(i => NativeSingleEventPublication.Site(value, i).UnwindSize).ToArray(),
            blockBytesVerified = block.Length, sourceBytesVerified = source.Sum(bytes => bytes.Length),
            observationPhase = "before-managed-observer-adoption",
            nativeLaneConfigured = native.Configured, nativeObserverAttached = native.Attached,
            nativeParentsEntered = native.ParentsEntered, nativeChildrenEntered = native.ChildrenEntered,
            actualGameCallbacksVerified = false
        };
    }

    private static object ValidateSingleEventBridgeAdoption(NativeFirstInstall producer, IntPtr helper,
        Action<bool, string> check)
    {
        var first = NativeTweenSingleEventBridge.AdoptReady();
        var second = NativeTweenSingleEventBridge.AdoptReady();
        var initial = first.ReadActivity();
        check(ReferenceEquals(first, second) && initial is { Known: true, InFlight: 0 },
            "The actual Single CLR bridge did not share one known idle process observer.");
        var read = Export<ReadSingleInfoNative>(helper, "RizTweenSingleEventContext_GetInfo");
        ActualSingleProducerInfo Read()
        {
            var value = new ActualSingleProducerInfo { Version = 1, Size = 144 };
            check(read(ref value) == 0, "The adopted Single producer information became unreadable.");
            return value;
        }
        var before = Read();
        check(before.Configured == 1 && before.Attached == 1 && before.Unknown == 0 && before.ParentCapacity == 128
            && before.Epoch == producer.SingleEventPublication.Epoch && before.ParentEpoch == producer.OperationPublication.Row25.Epoch,
            "The real Single bridge Attach did not preserve the exact native capacity and publication epochs.");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var after = Read(); var activity = first.ReadActivity();
        check(after.Equals(before) && activity is { Known: true, InFlight: 0 }
            && ReferenceEquals(first, NativeTweenSingleEventBridge.AdoptReady()),
            "CLR collection replaced the permanent Single observer or changed native execution responsibility.");
        check(after.ParentsEntered == 0 && after.ChildrenEntered == 0 && after.CallbackFaults == 0
            && after.RejectedReads == 0 && after.MissingPosts == 0 && after.RetainedParents == 0 && after.RetainedChildren == 0,
            "Read-only Single observer adoption unexpectedly executed game code or faulted.");
        return new { parentCapacity = after.ParentCapacity, epoch = after.Epoch, parentEpoch = after.ParentEpoch,
            attached = after.Attached, sharedOwner = ReferenceEquals(first, second), activityKnown = activity.Known,
            inFlight = activity.InFlight, survivedClrCollection = after.Equals(before),
            nativeParentsEntered = after.ParentsEntered, nativeChildrenEntered = after.ChildrenEntered,
            actualGameCallbacksVerified = false };
    }
}
