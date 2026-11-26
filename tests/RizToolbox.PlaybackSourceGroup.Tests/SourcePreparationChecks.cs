using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;

internal static partial class SourceGroupChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BindLower(IntPtr address);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ReadReservedEntry(uint kind, uint slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConfigureReservedEntry(
        IntPtr hub, uint kind, uint slot, uint role, IntPtr target, IntPtr original);

    private static FieldInfo PreparationField(Type owner, string name)
        => owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private sealed record SourcePreparationAudit(string Role, long GroupAudits, long FullImageAudits);

    private static int RunSourcePreparation(string[] args)
    {
        var mode = args[0];
        Check(args.Length == 6 && OperatingSystem.IsWindows() && IntPtr.Size == 8
            && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero,
            "Expected Windows x64 mode, owned wrapper and lower images, GetTime helper, source helper and Dobby.");
        Check(HashFile(args[1]) == OwnedPlaybackFixtureProfile.WrapperSha256 && HashFile(args[2]) == OwnedPlaybackFixtureProfile.LowerSha256,
            "The test must load the fixed two-image owned profile.");
        var wrapper = NativeLibrary.Load(Path.GetFullPath(args[1]));
        var lower = NativeLibrary.Load(Path.GetFullPath(args[2]));
        Check(Export<BindLower>(wrapper, "StockFixtureBindLowerPitch")(NativeLibrary.GetExport(lower, "StockLowerSetPitch")) == 1
            && Export<BindLower>(wrapper, "StockFixtureBindLowerTime")(NativeLibrary.GetExport(lower, "StockLowerSetTime")) == 1,
            "The owned wrapper did not bind both fixed lower functions.");
        var targets = PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(wrapper, lower);
        var broker = new ExactHookBroker(Path.GetFullPath(args[5]), new Main(), new Logger());
        using var pending = broker.BeginPlaybackSourceGroupPreparation(targets,
            Path.GetFullPath(args[3]), HashFile(args[3]), Path.GetFullPath(args[4]), HashFile(args[4]),
            () => throw new InvalidOperationException("Dormant preparation cannot read Clock."), _ => { }, _ => { });
        var group = (ExactHookBroker.PlaybackSourceInstallGroup)PreparationField(pending.GetType(), "_group").GetValue(pending)!;
        var generation = PreparationField(group.GetType(), "_installationGeneration");
        long ReadGeneration() => (long)generation.GetValue(group)!;
        var activeAudit = PreparationField(group.GetType(), "_installationAudit");
        long ReadActiveAudit() => (long)activeAudit.GetValue(group)!;
        var owners = targets.Roles.Select(group.SourceOwner).Where(owner => !owner.IsNativePitchPairMember).ToArray();
        Check(owners.Length == 19 && owners[0].Role == NativePlaybackSourceRole.SetPitch && targets.SourceRoleCount == 22,
            "The test needs all nineteen ordinary roles and the native family.");
        pending.PrepareNext(); // GetTime
        pending.PrepareNext(); // The actual three-member native family
        Check(broker.ReadActualPlaybackDobbyCallCounts() == (4, 0), "GetTime and the native family must remain prepared and dormant.");

        if (mode == "prepare-source-audits")
        {
            var measurements = new List<SourcePreparationAudit>();
            foreach (var owner in owners)
            {
                var calls = broker.ReadActualPlaybackDobbyCallCounts();
                var before = ReadGeneration();
                var images = NativePlaybackExecutableSeal.ReadThreadFullImageAudits();
                pending.PrepareNext();
                measurements.Add(new(owner.Role.ToString(), ReadGeneration() - before,
                    NativePlaybackExecutableSeal.ReadThreadFullImageAudits() - images));
                Check(broker.ReadActualPlaybackDobbyCallCounts() == (calls.Prepared + 1, 0)
                    && owner.ConfiguredChannel.IsConfigured && !owner.IsCommitted && ReadActiveAudit() == 0,
                    "Each frame must prepare and configure its exact source once, outside a completed audit.");
            }
            pending.PrepareNext(); // Configure the shared native relationships
            Check(pending.IsPrepared && broker.ReadActualPlaybackDobbyCallCounts() == (23, 0),
                "All twenty-three physical entries must remain dormant at the end of preparation.");
            Check(measurements.All(value => value.GroupAudits == 3), "Every source needs fresh frame, post-reserve and post-Prepare group audits.");
            Check(measurements.All(value => value.FullImageAudits <= 55),
                $"The source evidence must reuse each pure image audit. Maximum observed per role: {measurements.Max(value => value.FullImageAudits)}.");
            PlaybackSourceGroupLease? lease = null;
            while (lease is null) lease = pending.CommitNext();
            using (lease)
            {
                Check(lease.IsActive && lease.GetTimeLease.IsActive && !lease.ProductionAdmission
                    && broker.ReadActualPlaybackDobbyCallCounts() == (23, 23), "The actual complete group must still commit and arm once.");
                lease.VerifyCurrent();
            }
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-playback-source-preparation", mode, passed = true, checks = _checks,
                sourceSteps = measurements.Count, fullImageAudits = measurements.Sum(value => value.FullImageAudits), measurements,
                actualDobbyPrepareCount = 23, actualDobbyCommitCount = 23, gameCodeExecuted = false, productionAdmission = false }));
            return 0;
        }

        Check(mode is "prepare-source-after-reserve" or "prepare-source-after-prepare"
            or "prepare-source-after-configure" or "prepare-source-next-frame", "Unknown source preparation audit mode.");
        var source = owners[0];
        var name = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(targets.PitchInstance.MethodInfo).Name;
        var originalByte = Marshal.ReadByte(name);
        var originalBody = Bytes(source.TargetAddress, source.OriginalBodyLength);
        var fault = new SourcePreparationMutation(mode, broker, source, name, originalByte, ReadActiveAudit);
        if (mode == "prepare-source-next-frame") pending.PrepareNext();
        var beforeFault = ReadGeneration();
        var interval = mode == "prepare-source-after-prepare" ? beforeFault + 3 : beforeFault + 2;
        try
        {
            if (mode == "prepare-source-next-frame") fault.ChangeName();
            else
            {
                var hub = (NativePlaybackStockSourceHub)PreparationField(group.GetType(), "_hub").GetValue(group)!;
                fault.Attach(hub, interval);
            }
            Reject(pending.PrepareNext, "A real metadata name change must stop the next preparation boundary.");
        }
        finally { fault.Restore(); }
        var finalRead = mode is "prepare-source-after-configure" or "prepare-source-next-frame";
        var configurations = source.ConfiguredChannel.IsConfigured;
        var total = broker.ReadActualPlaybackDobbyCallCounts();
        Check(fault.Changed && fault.AuditAtMutation == (finalRead ? 0 : interval), "The test changed the member at the wrong checkpoint.");
        Check(!pending.IsOpen && !group.IsActive && ReadActiveAudit() == 0 && total == (mode == "prepare-source-after-reserve" ? 4 : 5, 0)
            && configurations == finalRead && owners[1].EntryAddress == IntPtr.Zero,
            "The refusal must retain only physical work before the changed boundary and close the dormant preparation.");
        Check(Bytes(source.TargetAddress, source.OriginalBodyLength).AsSpan().SequenceEqual(originalBody), "A failed preparation changed its executable source entry.");
        Reject(pending.PrepareNext, "Restoring the bytes revived a failed preparation.");
        Reject(() => pending.CommitNext(), "Restoring the bytes allowed a failed preparation to commit.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-playback-source-preparation", mode, passed = true, checks = _checks,
            checkpointAudits = ReadGeneration() - beforeFault, fault.AuditAtMutation, configuredBeforeRefusal = configurations,
            actualDobbyPrepareCount = total.Prepared, actualDobbyCommitCount = total.Committed,
            gameCodeExecuted = false, productionAdmission = false }));
        return 0;
    }

    private sealed class SourcePreparationMutation
    {
        private readonly string _mode;
        private readonly ExactHookBroker _broker;
        private readonly ExactHookBroker.PlaybackSourceOwner _source;
        private readonly IntPtr _name;
        private readonly byte _original;
        private readonly Func<long> _readAudit;
        private FieldInfo? _field;
        private NativePlaybackStockSourceHub? _hub;
        private object? _originalDelegate;
        private ReadReservedEntry? _entry;
        private ConfigureReservedEntry? _configure;
        private long _interval;
        internal bool Changed { get; private set; }
        internal long AuditAtMutation { get; private set; }

        internal SourcePreparationMutation(string mode, ExactHookBroker broker, ExactHookBroker.PlaybackSourceOwner source,
            IntPtr name, byte original, Func<long> readAudit)
            => (_mode, _broker, _source, _name, _original, _readAudit) = (mode, broker, source, name, original, readAudit);

        internal void Attach(NativePlaybackStockSourceHub hub, long interval)
        {
            _hub = hub;
            _interval = interval;
            string method;
            if (_mode == "prepare-source-after-configure")
            {
                _field = PreparationField(hub.GetType(), "_configure");
                _configure = Export<ConfigureReservedEntry>(hub.NativeLibraryHandle, "RtPlaybackSourceConfigure");
                method = nameof(Configure);
            }
            else
            {
                _field = PreparationField(hub.GetType(), "_entry");
                _entry = Export<ReadReservedEntry>(hub.NativeLibraryHandle, "RtPlaybackSourceGetEntry");
                method = nameof(ReadEntry);
            }
            _originalDelegate = _field.GetValue(hub);
            _field.SetValue(hub, Delegate.CreateDelegate(_field.FieldType, this, method));
        }

        public IntPtr ReadEntry(uint kind, uint slot)
        {
            var entry = _entry!(kind, slot);
            // The lower image is read after the wrapper image. Change the real
            // source's metadata name after its comparison in the wrapper.
            if (!Changed && kind == (uint)NativePlaybackSourceKind.IcallSingle1 && _readAudit() == _interval)
            {
                Check(_broker.ReadActualPlaybackDobbyCallCounts() == (_mode == "prepare-source-after-prepare" ? 5 : 4, 0)
                    && !_source.ConfiguredChannel.IsConfigured, "The fault must run between the reserved entry, Dobby Prepare and ConfigureOriginal.");
                ChangeName();
            }
            return entry;
        }

        public int Configure(IntPtr hub, uint kind, uint slot, uint role, IntPtr target, IntPtr original)
        {
            var result = _configure!(hub, kind, slot, role, target, original);
            if (result == 1 && role == (uint)_source.Role && !Changed) ChangeName();
            return result;
        }

        internal void ChangeName()
        {
            AuditAtMutation = _readAudit();
            Changed = true;
            Write(_name, (byte)(_original ^ 1));
        }

        internal void Restore()
        {
            _field?.SetValue(_hub, _originalDelegate);
            if (Changed) Write(_name, _original);
        }
    }
}
