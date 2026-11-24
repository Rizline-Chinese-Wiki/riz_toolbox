using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackLowerPitchTarget
{
    /// <summary>Fixed stock scheduling path below the existing PlayScheduled
    /// source and engine-pitch owners. This is supporting evidence only: it
    /// neither installs a hook nor issues a source or native-write authority.</summary>
    internal sealed class ScheduledPitchPreflight
    {
        private const uint WrapperRva = 0x15116d0;
        private const uint CacheRva = 0x1fb0660;
        private const uint NativePlayRva = 0x1d7ae0;
        private const uint SchedulerRva = 0xbb74c0;
        private const uint ReapplyRva = 0xbb6f20;
        private const uint EngineRva = 0xbbad20;
        private const int WrapperLength = 96;
        private const int NativePlayLength = 305;
        private const int SchedulerLength = 2072;
        private const int ReapplyBodyLength = 196;

        private readonly NativePlaybackLowerPitchTarget _lower;
        private readonly NativePlaybackExecutableSeal.RangeSeal _wrapperTail, _cache;
        private readonly List<NativePlaybackExecutableSeal.RangeSeal> _fixed = new();
        private readonly List<NativePlaybackSourceTrampolineProof.SourceFunctionSeal> _functions = new();

        internal IntPtr PlayCache { get; }
        internal IntPtr NativePlayTarget { get; }
        internal IntPtr ReapplyBody { get; }
        internal IntPtr ExactEngineCallReturn { get; }
        internal IntPtr ScheduledTarget { get; }
        internal IntPtr EngineTarget { get; }
        internal uint ReapplyLength => ReapplyBodyLength;

        internal static ScheduledPitchPreflight CreateStock(EnginePitchPreflight engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (engine.OwnedFixture || engine.Parent.OwnedFixture)
                throw new UnsupportedSignatureException("Only the pinned stock two-image engine receipt has a scheduled pitch path.");
            return new(engine);
        }

        private ScheduledPitchPreflight(EnginePitchPreflight engine)
        {
            _lower = engine.Parent;
            var game = _lower.WrapperModule;
            var unity = _lower.Module;
            ScheduledTarget = IntPtr.Add(game, checked((int)WrapperRva));
            PlayCache = IntPtr.Add(game, checked((int)CacheRva));
            NativePlayTarget = IntPtr.Add(unity, checked((int)NativePlayRva));
            ReapplyBody = IntPtr.Add(unity, checked((int)ReapplyRva));
            EngineTarget = IntPtr.Add(unity, checked((int)EngineRva));
            ExactEngineCallReturn = IntPtr.Add(unity, 0xbb6f4e);
            var stock = _lower.StockIssuer;
            if (_lower.WrapperImageSha256 != PlaybackSourceGroupTargets.StockImageSha256
                || _lower.ImageSha256 != UnityPlayerSha256 || stock is null || stock.Module != game
                || stock.PlayScheduled.Address != ScheduledTarget
                || engine.Module != unity || engine.Address != EngineTarget)
                throw new UnsupportedSignatureException("The scheduled pitch path is not bound to the fixed stock source and engine owners.");
            HookTargetValidation.VerifyExecutable(ScheduledTarget, game);
            HookTargetValidation.VerifyExecutable(NativePlayTarget, unity);
            HookTargetValidation.VerifyExecutable(ReapplyBody, unity);

            // The source group owns the first six bytes of this 96-byte
            // wrapper after installation. Only the canonical file supplies
            // its complete hash; our live seal starts after that prefix.
            var wrapper = _lower._wrapperFile.CanonicalBytes(WrapperRva, WrapperLength);
            RequireHash(wrapper, "a85d60f8b5c097c377d0b998c4ea6bd90ce2a51f0b7226b8612ffa1cc2582cef",
                "PlayScheduled wrapper");
            NativePlaybackSourcePrologue.ForRole(NativePlaybackSourceRole.PlayScheduled).RequirePrefix(wrapper);
            _wrapperTail = _lower._wrapperFile.CaptureCanonical(WrapperRva + 6, WrapperLength - 6, executable: true);
            RequireFunctions(_lower._wrapperFile, game, new[]
            {
                new FunctionSpec(0x15116d0, 0x15116e5, 0x2382d34, 0x1ce50c8, "0106020006520230"),
                new FunctionSpec(0x15116e5, 0x151171c, 0x2382d40, 0x1db1ae0,
                    "2105020005680200d0165101e5165101c850ce01", 0),
                new FunctionSpec(0x151171c, 0x1511722, 0x2382d4c, 0x1db1af4,
                    "21000000d0165101e5165101c850ce01", 0)
            });
            _functions.Add(new(ScheduledTarget, 82, 6, NativePlaybackSourceRole.PlayScheduled));
            RequireRipReference(_lower._wrapperFile, 0x15116de, "488b05", CacheRva);
            RequireRipReference(_lower._wrapperFile, 0x15116f6, "488d0d", 0x18550d0);
            RequireCall(_lower._wrapperFile, 0x15116fd, 0x265060);
            RequireRipReference(_lower._wrapperFile, 0x1511702, "488905", CacheRva);
            RequireBytes(_lower._wrapperFile, 0x1511719, "48ffe0");
            CaptureReadOnly(_lower._wrapperFile, 0x18550d0,
                Encoding.ASCII.GetBytes("UnityEngine.AudioSource::Play(System.Double)\0"));

            // The lower receipt already seals the complete 302-entry tables
            // and registrar. Bind this additional index to its exact name and
            // native target without recapturing those complete parent ranges.
            RequireRegistration();

            PinCode(_lower._lowerFile, NativePlayRva, NativePlayLength,
                "08a66176098adfd22b902b5cecffead98f44e93c950dc48a27d312cbe9406429", "native Play");
            RequireFunctions(_lower._lowerFile, unity, new[]
            {
                new FunctionSpec(0x1d7ae0, 0x1d7c11, 0x1d36dd4, 0x1ac9dac,
                    "01150600156806000d3411000dd20650")
            });
            _functions.Add(new(NativePlayTarget, NativePlayLength, 1));
            RequireBytes(_lower._lowerFile, 0x1d7b8e, "488b4810");
            RequireBytes(_lower._lowerFile, 0x1d7bb3, "488b4810");
            RequireBytes(_lower._lowerFile, 0x1d7bbf, "0f28ce");
            RequireCall(_lower._lowerFile, 0x1d7bc2, SchedulerRva);

            PinCode(_lower._lowerFile, SchedulerRva, SchedulerLength,
                "b434597b2511a8df92eeac732311b08252c0f53d6e44f282aa574539a0964363", "native scheduler");
            RequireFunctions(_lower._lowerFile, unity, new[]
            {
                new FunctionSpec(0xbb74c0, 0xbb74f9, 0x1db5800, 0x1b66914,
                    "01200700206810001201260007d0053004500000"),
                new FunctionSpec(0xbb74f9, 0xbb758e, 0x1db580c, 0x1b66928,
                    "210804000874250004642a00c074bb00f974bb001469b601", 0),
                new FunctionSpec(0xbb758e, 0xbb76ff, 0x1db5818, 0x1b66940,
                    "2117040017f4220008e42300f974bb008e75bb002869b601", 1),
                new FunctionSpec(0xbb76ff, 0xbb7ca4, 0x1db5824, 0x1b66958,
                    "2108020008c424008e75bb00ff76bb004069b601", 2),
                new FunctionSpec(0xbb7ca4, 0xbb7cb4, 0x1db5830, 0x1b6696c,
                    "210000008e75bb00ff76bb004069b601", 2),
                new FunctionSpec(0xbb7cb4, 0xbb7cc4, 0x1db583c, 0x1b6697c,
                    "21000000f974bb008e75bb002869b601", 1),
                new FunctionSpec(0xbb7cc4, 0xbb7cd8, 0x1db5848, 0x1b6698c,
                    "21000000c074bb00f974bb001469b601", 0)
            }, requireThreeDeepChain: true);
            _functions.Add(new(IntPtr.Add(unity, checked((int)SchedulerRva)), SchedulerLength, 1));
            RequireCall(_lower._lowerFile, 0xbb76b1, ReapplyRva);
            RequireBranch(_lower._lowerFile, 0xbb76f0, 0xbb7ca4);
            RequireCall(_lower._lowerFile, 0xbb7ba5, ReapplyRva);

            PinCode(_lower._lowerFile, ReapplyRva, ReapplyBodyLength,
                "10f5a0b0acfa8814f9898d6ea5487900fad3f69aaf43014ff553d3ae2a6d3b1a", "native pitch reapply");
            RequireFunctions(_lower._lowerFile, unity, new[]
            {
                new FunctionSpec(0xbb6f20, 0xbb6fe4, 0x1db577c, 0x1ac7b88,
                    "0106020006320230")
            });
            _functions.Add(new(ReapplyBody, ReapplyBodyLength, 1));
            RequireBytes(_lower._lowerFile, 0xbb6f26, "488bd9");
            RequireBytes(_lower._lowerFile, 0xbb6f3e, "f30f108bf4010000");
            RequireBytes(_lower._lowerFile, 0xbb6f46, "488bcb");
            RequireCall(_lower._lowerFile, 0xbb6f49, EngineRva);

            _lower._wrapperFile.RequireZeroFill(CacheRva, IntPtr.Size);
            _cache = NativePlaybackExecutableSeal.RangeSeal.Capture(PlayCache, IntPtr.Size, game, true);
            if ((_cache.StartProtection & 0xff) is not (4 or 8))
                throw new UnsupportedSignatureException("The fixed PlayScheduled cache is not writable image data.");
            VerifySupportingEvidence();
        }

        /// <summary>Verify only this path's file, untouched code, metadata and
        /// naturally mutable cache. Never call engine or lower verification
        /// from here: the engine supporting verifier calls this method.</summary>
        internal void VerifySupportingEvidence()
        {
            VerifySupportingEvidenceCore(auditFiles: true);
        }
        internal void VerifyOperationSupportingEvidence()
        {
            VerifySupportingEvidenceCore(auditFiles: false);
        }
        private void VerifySupportingEvidenceCore(bool auditFiles)
        {
            var stock = _lower.StockIssuer;
            if (_lower.OwnedFixture || _lower.WrapperImageSha256 != PlaybackSourceGroupTargets.StockImageSha256
                || _lower.ImageSha256 != UnityPlayerSha256
                || stock is null || stock.Module != _lower.WrapperModule
                || stock.PlayScheduled.Address != ScheduledTarget
                || EngineTarget != IntPtr.Add(_lower.Module, checked((int)EngineRva)))
                throw new UnsupportedSignatureException("The scheduled pitch support lost its exact stock two-image identity.");
            if (auditFiles) { _lower._wrapperFile.Verify(); _lower._lowerFile.Verify(); }
            _wrapperTail.Verify();
            foreach (var seal in _fixed) seal.Verify();
            foreach (var functions in _functions) functions.Verify();
            _cache.VerifyMemoryIdentity();
            var before = Marshal.ReadIntPtr(PlayCache);
            var after = Marshal.ReadIntPtr(PlayCache);
            if (before != after || before != IntPtr.Zero && before != NativePlayTarget)
                throw new UnsupportedSignatureException("The PlayScheduled resolver cache is not zero or its exact registered native Play entry.");
        }

        private void RequireRegistration()
        {
            const uint functionSlotRva = 0x1935e50;
            const uint nameSlotRva = 0x19367c0;
            if (!_lower._lowerFile.HasDir64(functionSlotRva) || !_lower._lowerFile.HasDir64(nameSlotRva))
                throw new UnsupportedSignatureException("The scheduled Play registration pair lost its original DIR64 records.");
            var function = CaptureReadOnly(_lower._lowerFile, functionSlotRva, 8);
            var name = CaptureReadOnly(_lower._lowerFile, nameSlotRva, 8);
            var registered = IntPtr.Add(_lower.Module, 0x1932570);
            CaptureReadOnly(_lower._lowerFile, 0x1932570,
                Encoding.ASCII.GetBytes("UnityEngine.AudioSource::Play\0"));
            if ((IntPtr)BitConverter.ToInt64(function.Bytes, 0) != NativePlayTarget
                || (IntPtr)BitConverter.ToInt64(name.Bytes, 0) != registered)
                throw new UnsupportedSignatureException("Audio registration index 40 no longer binds the fixed native Play entry and name.");
        }

        private NativePlaybackExecutableSeal.RangeSeal CaptureReadOnly(PinnedImageFile file, uint rva, int length)
        {
            var seal = file.CaptureCanonical(rva, length);
            if (seal.StartProtection != 2)
                throw new UnsupportedSignatureException("Scheduled pitch metadata lost its read-only image protection.");
            _fixed.Add(seal);
            return seal;
        }

        private void CaptureReadOnly(PinnedImageFile file, uint rva, byte[] expected)
        {
            if (!CaptureReadOnly(file, rva, expected.Length).Bytes.AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("A fixed scheduled pitch registration or resolver name changed.");
        }

        private void PinCode(PinnedImageFile file, uint rva, int length, string hash, string name)
        {
            RequireHash(file.CanonicalBytes(rva, length), hash, name);
            _fixed.Add(file.CaptureCanonical(rva, length, executable: true));
        }

        private static void RequireHash(byte[] bytes, string hash, string name)
        {
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The complete fixed " + name + " fingerprint changed.");
        }

        private static void RequireBytes(PinnedImageFile file, uint rva, string hex)
        {
            var expected = Convert.FromHexString(hex);
            if (!file.CanonicalBytes(rva, expected.Length).AsSpan().SequenceEqual(expected))
                throw new UnsupportedSignatureException("A fixed scheduled pitch instruction changed.");
        }

        private static void RequireRipReference(PinnedImageFile file, uint site, string opcode, uint target)
        {
            var prefix = Convert.FromHexString(opcode);
            var bytes = file.CanonicalBytes(site, prefix.Length + 4);
            if (!bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix)
                || checked((long)site + bytes.Length + BitConverter.ToInt32(bytes, prefix.Length)) != target)
                throw new UnsupportedSignatureException("A fixed PlayScheduled RIP-relative resolver reference changed.");
        }

        private static void RequireCall(PinnedImageFile file, uint site, uint target)
            => RequireRelativeBranch(file, site, target, 0xe8);

        private static void RequireBranch(PinnedImageFile file, uint site, uint target)
            => RequireRelativeBranch(file, site, target, 0xe9);

        private static void RequireRelativeBranch(PinnedImageFile file, uint site, uint target, byte opcode)
        {
            var bytes = file.CanonicalBytes(site, 5);
            if (bytes[0] != opcode || checked((long)site + 5 + BitConverter.ToInt32(bytes, 1)) != target)
                throw new UnsupportedSignatureException("A fixed scheduled pitch direct CALL or branch changed.");
        }

        private void RequireFunctions(PinnedImageFile file, IntPtr module, FunctionSpec[] specs,
            bool requireThreeDeepChain = false)
        {
            var programs = new byte[specs.Length][];
            for (var at = 0; at < specs.Length; ++at)
            {
                var spec = specs[at];
                if (spec.Begin >= spec.End || at > 0 && specs[at - 1].End != spec.Begin)
                    throw new UnsupportedSignatureException("A fixed scheduled pitch runtime-function group is not contiguous.");
                var rf = file.CaptureCanonical(spec.Pdata, 12);
                if (BitConverter.ToUInt32(rf.Bytes, 0) != spec.Begin
                    || BitConverter.ToUInt32(rf.Bytes, 4) != spec.End
                    || BitConverter.ToUInt32(rf.Bytes, 8) != spec.Unwind)
                    throw new UnsupportedSignatureException("A fixed scheduled pitch runtime-function tuple changed.");
                var start = IntPtr.Add(module, checked((int)spec.Begin));
                var end = IntPtr.Add(module, checked((int)spec.End - 1));
                var expectedRf = IntPtr.Add(module, checked((int)spec.Pdata));
                if (RtlLookupFunctionEntry(unchecked((ulong)start.ToInt64()), out var firstImage, IntPtr.Zero) != expectedRf
                    || firstImage != unchecked((ulong)module.ToInt64())
                    || RtlLookupFunctionEntry(unchecked((ulong)end.ToInt64()), out var lastImage, IntPtr.Zero) != expectedRf
                    || lastImage != unchecked((ulong)module.ToInt64()))
                    throw new UnsupportedSignatureException("The OS scheduled pitch runtime-function lookup lost its fixed image owner.");
                var program = Convert.FromHexString(spec.UnwindHex);
                var unwind = file.CaptureCanonical(spec.Unwind, program.Length);
                if (!unwind.Bytes.AsSpan().SequenceEqual(program))
                    throw new UnsupportedSignatureException("A complete fixed scheduled pitch unwind program changed.");
                _fixed.Add(rf); _fixed.Add(unwind);
                programs[at] = program;
            }
            var greatestDepth = 0;
            for (var at = 0; at < specs.Length; ++at)
                greatestDepth = Math.Max(greatestDepth, VerifyUnwindChain(specs, programs, at));
            if (requireThreeDeepChain && (specs.Length != 7 || greatestDepth != 3))
                throw new UnsupportedSignatureException("The native scheduler lost its complete seven-RF, three-deep unwind chain.");
        }

        private static int VerifyUnwindChain(FunctionSpec[] specs, byte[][] programs, int index)
        {
            var spec = specs[index];
            var program = programs[index];
            var flags = program[0] >> 3;
            var minimum = checked(4 + ((program[2] * 2 + 3) & ~3));
            var length = checked(minimum + (spec.ChainParent >= 0 ? 12 : (flags & 3) != 0 ? 4 : 0));
            if ((program[0] & 7) != 1 || program.Length != length
                || ((flags & 4) != 0) != (spec.ChainParent >= 0))
                throw new UnsupportedSignatureException("A fixed scheduled pitch unwind header or complete record length changed.");
            if (spec.ChainParent < 0) return 0;
            if (spec.ChainParent >= index)
                throw new UnsupportedSignatureException("A fixed scheduled pitch unwind chain is cyclic or forward-linked.");
            var parent = specs[spec.ChainParent];
            if (BitConverter.ToUInt32(program, program.Length - 12) != parent.Begin
                || BitConverter.ToUInt32(program, program.Length - 8) != parent.End
                || BitConverter.ToUInt32(program, program.Length - 4) != parent.Unwind)
                throw new UnsupportedSignatureException("A fixed scheduled pitch recursive CHAININFO tuple changed.");
            var depth = 1 + VerifyUnwindChain(specs, programs, spec.ChainParent);
            if (depth > 3)
                throw new UnsupportedSignatureException("A fixed scheduled pitch unwind chain exceeded its reviewed depth.");
            return depth;
        }

        private readonly record struct FunctionSpec(uint Begin, uint End, uint Pdata, uint Unwind,
            string UnwindHex, int ChainParent = -1);
    }
}
