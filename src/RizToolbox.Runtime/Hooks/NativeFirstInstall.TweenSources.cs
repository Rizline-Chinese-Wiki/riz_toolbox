using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenPublishedSourceSite
{
    internal uint Site, BeginRva, EndRva, ParentRva;
    internal IntPtr SourcePc, PreRelay, PostRelay, PreReturnPc, PostReturnPc, Continuation, DeadContinuation, UnwindInfo;
    internal uint Width, Protection;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourcePublication
{
    internal uint Version, Size, Prepared, Published, Unknown, SiteCount, BindingCount, FunctionCount;
    internal ulong Epoch, PublishedMask;
    internal IntPtr ModuleBase, Allocation, FunctionTable, Entry;
    internal ulong AllocationSize, Reserved;
    internal NativeTweenPublishedSourceSite Row0, Row1, Row2, Row3, Row4, Row5, Row6, Row7, Row8, Row9, Row10;
    internal NativeTweenPublishedSourceSite Row11, Row12, Row13, Row14, Row15, Row16, Row17, Row18, Row19, Row20, Row21;
    internal readonly NativeTweenPublishedSourceSite Row(uint site) => site switch {
        0 => Row0, 1 => Row1, 2 => Row2, 3 => Row3, 4 => Row4, 5 => Row5, 6 => Row6, 7 => Row7,
        8 => Row8, 9 => Row9, 10 => Row10, 11 => Row11, 12 => Row12, 13 => Row13, 14 => Row14,
        15 => Row15, 16 => Row16, 17 => Row17, 18 => Row18, 19 => Row19, 20 => Row20, 21 => Row21,
        _ => throw new ArgumentOutOfRangeException(nameof(site)) };
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSourceMetadataBinding
{
    internal uint Version, Size;
    internal IntPtr TweenClass, ManagerClass, CachedPointerField;
}

internal sealed partial class NativeFirstInstall
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadTweenSourcesNative(ref NativeTweenSourcePublication value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BindTweenSourceMetadataNative(ref NativeTweenSourceMetadataBinding value);
    private readonly object _tweenSourceIssuer = new();
    private NativeTweenSourcePublication? _tweenSourceIdentity;
    private TweenSourceReady? _tweenSourceReady;
    internal ulong RetainedTweenSourceEpoch => _tweenSourceIdentity?.Epoch
        ?? throw new UnsupportedSignatureException("The actual producer has no retained Tween source epoch.");

    // This class can only be issued by the actual already-loaded producer. Its
    // copied native packet is checked evidence; it cannot create another owner.
    internal sealed partial class TweenSourceReady
    {
        private readonly NativeFirstInstall _producer;
        internal TweenSourceReady(NativeFirstInstall producer, object issuer)
        {
            if (!ReferenceEquals(issuer, producer._tweenSourceIssuer)) throw new InvalidOperationException("Foreign Tween source publication issuer.");
            _producer = producer; VerifyReady();
        }
        internal IntPtr HelperModule => _producer.HelperModule;
        internal IntPtr Module => _producer.Module;
        internal void VerifyReady() => _producer.VerifyReady();
        internal bool IsIssuedBy(NativeFirstInstall producer) => ReferenceEquals(_producer, producer);

        internal void VerifyRecordBinding(NativeTweenSourceRecord record)
        {
            VerifyReady(); var packet = _producer._tweenSourceIdentity
                ?? throw new UnsupportedSignatureException("The source producer has no retained publication identity.");
            VerifyRecordIdentity(record, packet.Epoch, packet.Row((uint)record.Site));
        }

        // A bounded journal read checks the full managed publication at its two
        // boundaries. Each row still reads the native publication afresh. That
        // export checks current V7, relay bytes, unwind and source spans in the
        // actual producer. The row also gets its independent managed site check.
        internal void VerifyAvailableRecordBinding(NativeTweenSourceRecord record, NativeTweenSource.AvailableRead scope)
        {
            scope.VerifyRecordOwner(this);
            ulong epoch;
            NativeTweenPublishedSourceSite row;
            try
            {
                _producer.VerifyRuntimeChannelOwnership();
                var publication = _producer.ReadTweenSourcePublication();
                epoch = publication.Epoch;
                row = _producer.VerifyTweenSourceSiteMemory(publication, (uint)record.Site);
            }
            catch (Exception error)
            {
                // A failed native source export has no V7 diagnostic of its own.
                // Read the producer's original first failure before retaining it here.
                VerifyReady();
                _producer.RememberFailure(nameof(VerifyAvailableRecordBinding), error.Message);
                throw;
            }
            VerifyRecordIdentity(record, epoch, row);
        }

        private static void VerifyRecordIdentity(NativeTweenSourceRecord record, ulong epoch, NativeTweenPublishedSourceSite row)
        {
            var returned = record.Stage == NativeTweenSourceStage.After ? row.PostReturnPc : row.PreReturnPc;
            if (record.Epoch != epoch || record.SourcePc != row.SourcePc || record.RelayReturnPc != returned || returned == IntPtr.Zero)
                throw new UnsupportedSignatureException("A native source record crossed its actual published epoch, source site or relay return PC.");
        }
        internal void BindMetadata(NativeTweenSourceMetadata metadata, IntPtr cachedPointerField)
        {
            ArgumentNullException.ThrowIfNull(metadata); VerifyReady();
            var address = NativeLibrary.GetExport(HelperModule, "RizFirstInstall_BindTweenSourceMetadata");
            HookTargetValidation.VerifyExecutable(address, HelperModule);
            var binding = new NativeTweenSourceMetadataBinding { Version = 1, Size = 32,
                TweenClass = metadata.TweenClass, ManagerClass = metadata.ManagerClass, CachedPointerField = cachedPointerField };
            var expected = binding;
            var result = Marshal.GetDelegateForFunctionPointer<BindTweenSourceMetadataNative>(address)(ref binding);
            if (result is not (0 or 4) || !binding.Equals(expected))
                throw new UnsupportedSignatureException("Actual Tween source metadata binding failed: " + result);
            VerifyReady();
        }
    }
    internal TweenSourceReady RequireTweenSourceReady()
    {
        VerifyReady(); return _tweenSourceReady ??= new(this, _tweenSourceIssuer);
    }
    private NativeTweenSourcePublication ReadTweenSourcePublication()
    {
        if (Marshal.SizeOf<NativeTweenPublishedSourceSite>() != 88 || Marshal.SizeOf<NativeTweenSourcePublication>() != 2032
            || Marshal.OffsetOf<NativeTweenSourcePublication>(nameof(NativeTweenSourcePublication.Row0)).ToInt32() != 96
            || Marshal.SizeOf<NativeTweenSourceMetadataBinding>() != 32)
            throw new UnsupportedSignatureException("Actual Tween source publication layout differs.");
        var address = NativeLibrary.GetExport(HelperModule, "RizFirstInstall_GetTweenSourceInfoV1");
        HookTargetValidation.VerifyExecutable(address, HelperModule);
        var value = new NativeTweenSourcePublication { Version = 1, Size = 2032 };
        if (Marshal.GetDelegateForFunctionPointer<ReadTweenSourcesNative>(address)(ref value) != 0
            || value.Version != 1 || value.Size != 2032 || value.Prepared != 1 || value.Published != 1 || value.Unknown != 0
            || value.SiteCount != 22 || value.BindingCount != 43 || value.FunctionCount is < 22 or > 128
            || value.Epoch == 0 || value.PublishedMask != (1UL << 22) - 1 || value.ModuleBase != Module
            || value.Allocation == IntPtr.Zero || value.AllocationSize is < 4096 or > 1024 * 1024
            || value.FunctionTable == IntPtr.Zero || value.Entry == IntPtr.Zero || value.Reserved != 0)
            throw new UnsupportedSignatureException("The actual native producer has no complete Tween source publication.");
        if (_tweenSourceIdentity is { } prior && !prior.Equals(value))
            throw new UnsupportedSignatureException("The actual immutable Tween source publication changed.");
        return value;
    }

    private void VerifyTweenSourceMemory()
    {
        var value = ReadTweenSourcePublication();
        HookTargetValidation.VerifyExecutable(value.Entry, HelperModule);
        HookTargetValidation.VerifyReadable(value.Allocation, checked((uint)value.AllocationSize));
        VerifyProtection(value.Allocation, 0x20);
        HookTargetValidation.VerifyReadable(value.FunctionTable, checked(value.FunctionCount * 12));
        for (uint site = 0; site < 22; ++site)
            VerifyTweenSourceSiteMemory(value, site);
        _tweenSourceIdentity ??= value;
    }

    private NativeTweenPublishedSourceSite VerifyTweenSourceSiteMemory(NativeTweenSourcePublication value, uint site)
    {
        bool InBlock(IntPtr pointer) => pointer.ToInt64() >= value.Allocation.ToInt64()
            && unchecked((ulong)(pointer.ToInt64() - value.Allocation.ToInt64())) < value.AllocationSize;
        var row = value.Row(site); var declared = TweenSourceDeclaration(site);
        if (row.Site != site || row.BeginRva != declared.Begin || row.EndRva != declared.End
            || row.ParentRva != declared.Parent || row.Width != declared.End - declared.Begin
            || row.SourcePc != IntPtr.Add(Module, checked((int)declared.Begin))
            || row.Continuation != IntPtr.Add(Module, checked((int)declared.End))
            || row.DeadContinuation != (declared.Dead == 0 ? IntPtr.Zero : IntPtr.Add(Module, checked((int)declared.Dead)))
            || !InBlock(row.PreRelay) || !InBlock(row.PreReturnPc) || !InBlock(row.UnwindInfo)
            || (site == 13 ? row.PostRelay != IntPtr.Zero || row.PostReturnPc != IntPtr.Zero : !InBlock(row.PostRelay) || !InBlock(row.PostReturnPc))
            || !NativeListenerPublication.ExecutableProtection(row.Protection))
            throw new UnsupportedSignatureException("A Tween source row differs from its fixed actual declaration.");
        HookTargetValidation.VerifyReadable(row.SourcePc, row.Width); VerifyProtection(row.SourcePc, row.Protection);
        if (!RelativeBranch(row.SourcePc, 0xe9, row.PreRelay))
            throw new UnsupportedSignatureException("A Tween source does not reach its actual permanent relay.");
        for (uint offset = 5; offset < row.Width; ++offset)
            if (Marshal.ReadByte(row.SourcePc, checked((int)offset)) != 0x90)
                throw new UnsupportedSignatureException("A Tween source lost its complete NOP-padded publication.");
        var function = RtlLookupFunctionEntry(unchecked((ulong)row.PreRelay.ToInt64()), out var image, IntPtr.Zero);
        if (function == IntPtr.Zero || image != unchecked((ulong)Module.ToInt64()))
            throw new UnsupportedSignatureException("A Tween relay lost its registered actual function table.");
        HookTargetValidation.VerifyReadable(function, 12);
        if (image + unchecked((uint)Marshal.ReadInt32(function)) != unchecked((ulong)row.PreRelay.ToInt64())
            || image + unchecked((uint)Marshal.ReadInt32(function, 8)) != unchecked((ulong)row.UnwindInfo.ToInt64()))
            throw new UnsupportedSignatureException("A Tween relay differs from its registered unwind identity.");
        return row;
    }
    internal static (uint Begin, uint End, uint Parent, uint Dead) TweenSourceDeclaration(uint site) => site switch {
        0 => (0x398f18,0x398f1d,0x398d60,0x398f00), 1 => (0x3996b8,0x3996bd,0x398d60,0x3996a0),
        2 => (0x3995bb,0x3995d0,0x398d60,0), 3 => (0x399d97,0x399dac,0x398d60,0),
        4 => (0x3997e1,0x3997f6,0x398d60,0), 5 => (0x399815,0x399829,0x398d60,0),
        6 => (0x39966a,0x39967f,0x398d60,0), 7 => (0x399e97,0x399e9c,0x398d60,0),
        8 => (0x399f7d,0x399f82,0x398d60,0), 9 => (0x399fd8,0x399fdd,0x398d60,0),
        10 => (0x399f98,0x399f9d,0x398d60,0), 11 => (0x399ffa,0x399fff,0x398d60,0),
        12 => (0x39a02c,0x39a031,0x398d60,0), 13 => (0x399634,0x399641,0x398d60,0x39964e),
        14 => (0x3995da,0x3995df,0x398d60,0), 15 => (0x399785,0x39978a,0x398d60,0),
        16 => (0x399db6,0x399dbb,0x398d60,0), 17 => (0x399565,0x39956a,0x398d60,0),
        18 => (0x399d41,0x399d46,0x398d60,0), 19 => (0x3957e8,0x3957ed,0x3956f0,0),
        20 => (0x395d50,0x395d55,0x3956f0,0), 21 => (0x3984c2,0x3984c7,0x396e40,0),
        _ => throw new ArgumentOutOfRangeException(nameof(site)) };
}
