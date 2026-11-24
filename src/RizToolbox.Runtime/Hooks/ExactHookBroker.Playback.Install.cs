using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackOwner
    {
        internal void Install()
        {
            try
            {
                PrepareInstall(); CommitInstall(); ArmInstall();
                VerifyCurrent(_module, TargetAddress, _target.MethodInfo);
            }
            catch (Exception error)
            {
                Fault("GetTime broker installation or readback failed: " + error.Message); throw;
            }
        }
        internal void PrepareInstall()
        {
                _group?.RequireNoInstallationAudit();
                // Prove the helper's executable image before invoking even its
                // layout/entry/configuration exports. First snapshots confer
                // no authority on preexisting code modifications.
                _helper = NativePlaybackExecutableSeal.Capture(NativeLibrary.Load(Path.GetFullPath(_helperPath)), _helperHash);
                _bridge = new NativePlaybackSingleReturnBridge(_helperPath, _helperHash, entry =>
                {
                    CurrentImage.Verify(); _dobby.Verify(); _method.Verify(); _source!.Verify();
                    _original = _engine.Prepare(_target, entry);
                    CurrentImage.Verify(); _dobby.Verify(); _method.Verify(); _source.Verify();
                    if (_original == TargetAddress || _original == entry || _original == IntPtr.Zero)
                        throw new UnsupportedSignatureException("Dobby did not return a distinct original GetTime relocation.");
                    CaptureRelocation(); _dobbyOriginal = _original; CreateOriginalThunk();
                    _unwind = NativePlaybackTrampolineUnwind.Register(_original, IntPtr.Add(_original, 4096)); return _original;
                }, ReadClock, NotifyFault, Before, After);
                if (AllocationBase(EntryAddress) != _helper.Module)
                    throw new UnsupportedSignatureException("The actual Single entry differs from the prevalidated helper image.");
                _helper.Verify();
                _relocation!.Verify(); CurrentImage.Verify(); _dobby.Verify(); _source!.Verify();
        }
        internal void CommitInstall()
        {
                _group?.RequireNoInstallationAudit();
                CurrentImage.Verify(); _dobby.Verify(); _method.Verify(); _source!.Verify();
                var protection = _source.StartProtection;
                try { _engine.Commit(_target); }
                finally
                {
                    if (!VirtualProtect(TargetAddress, 6, protection, out _)
                        || !FlushInstructionCache((IntPtr)(-1), TargetAddress, 6))
                        throw new UnsupportedSignatureException("GetTime commit did not restore source protection and flush its exact instruction span.");
                }
                CaptureInstalledSource();
                Volatile.Write(ref _committed, 1);
        }
        internal void ArmInstall() { _group?.RequireNoInstallationAudit(); Volatile.Write(ref _active, 1); }
        private void CaptureRelocation()
        {
            // Exactly MOV [RSP+8],RBX; PUSH RDI. No byte scan establishes
            // semantics for an unknown opcode, RIP operand or branch.
            var prefix = new byte[] { 0x48, 0x89, 0x5c, 0x24, 0x08, 0x57 };
            if (!_originalBody.AsSpan(0, 6).SequenceEqual(prefix))
                throw new UnsupportedSignatureException("GetTime does not have the supported exact six-byte PC-independent prologue.");
            _relocation = NativePlaybackExecutableSeal.RangeSeal.Capture(_original, 20, AllocationBase(_original), false, true);
            if (!_relocation.Bytes.AsSpan(0, 6).SequenceEqual(prefix)
                || !AbsoluteJump(_relocation.Bytes, 6, out var continuation) || continuation != IntPtr.Add(TargetAddress, 6))
                throw new UnsupportedSignatureException("Dobby original is not the exact proved MOV/PUSH plus target+6 continuation.");
        }
        private void CaptureInstalledSource()
        {
            var actual = NativePlaybackExecutableSeal.RangeSeal.Capture(TargetAddress, OriginalBodyLength, _module, true, true);
            if (actual.Bytes[0] == 0xe9)
            {
                PatchedByteCount = 5;
                var jump = (IntPtr)checked(TargetAddress.ToInt64() + 5 + BitConverter.ToInt32(actual.Bytes, 1));
                if (jump != EntryAddress)
                {
                    _relay = NativePlaybackExecutableSeal.RangeSeal.Capture(jump, 14, IntPtr.Zero, false, true);
                    if (!AbsoluteJump(_relay.Bytes, 0, out var destination) || destination != EntryAddress)
                        throw new UnsupportedSignatureException("The actual Dobby relay does not reach the exact native Single entry.");
                }
            }
            else if (actual.Bytes[0] == 0xff && actual.Bytes[1] == 0x25 && BitConverter.ToInt32(actual.Bytes, 2) != 0)
            {
                PatchedByteCount = 6;
                var slot = (IntPtr)checked(TargetAddress.ToInt64() + 6 + BitConverter.ToInt32(actual.Bytes, 2));
                _relay = NativePlaybackExecutableSeal.RangeSeal.Capture(slot, 8, IntPtr.Zero, false);
                if ((IntPtr)BitConverter.ToInt64(_relay.Bytes, 0) != EntryAddress)
                    throw new UnsupportedSignatureException("The actual six-byte Dobby detour slot does not hold the exact Single entry.");
            }
            else throw new UnsupportedSignatureException("The actual Dobby source detour does not reach the exact native Single entry.");
            if (!actual.Bytes.AsSpan(PatchedByteCount).SequenceEqual(_originalBody.AsSpan(PatchedByteCount)))
                throw new UnsupportedSignatureException("Dobby changed GetTime outside its actual native patch span.");
            var relocation = _relocation!.Bytes;
            if (!AbsoluteJump(relocation, relocation.Length - 14, out var continuation)
                || continuation.ToInt64() - TargetAddress.ToInt64() < PatchedByteCount)
                throw new UnsupportedSignatureException("The original relocation returns inside the installed branch.");
            _source = actual;
        }
        private void CreateOriginalThunk()
        {
            // Windows treats Dobby's FF25 continuation as an epilogue while
            // PUSH RDI is outstanding. Use an independently proved equivalent
            // volatile-R11 tail, retaining the actual Dobby relocation intact.
            var code = new byte[19]; _originalBody.AsSpan(0, 6).CopyTo(code);
            code[6] = 0x49; code[7] = 0xbb;
            BitConverter.GetBytes(IntPtr.Add(TargetAddress, 6).ToInt64()).CopyTo(code, 8);
            code[16] = 0x41; code[17] = 0xff; code[18] = 0xe3;
            _original = VirtualAlloc(IntPtr.Zero, 8192, 0x3000, 4);
            if (_original == IntPtr.Zero) throw new UnsupportedSignatureException("The private original GetTime thunk could not be allocated.");
            Marshal.Copy(code, 0, _original, code.Length);
            if (!VirtualProtect(_original, 4096, 0x20, out _) || !FlushInstructionCache((IntPtr)(-1), _original, (nuint)code.Length))
                throw new UnsupportedSignatureException("The private original GetTime thunk was not sealed RX and flushed.");
            _originalThunk = NativePlaybackExecutableSeal.RangeSeal.Capture(_original, code.Length, _original, false, true);
        }
        private static bool AbsoluteJump(byte[] bytes, int offset, out IntPtr destination)
        {
            destination = IntPtr.Zero;
            if (offset < 0 || bytes.Length - offset < 14 || bytes[offset] != 0xff || bytes[offset + 1] != 0x25
                || BitConverter.ToInt32(bytes, offset + 2) != 0) return false;
            destination = (IntPtr)BitConverter.ToInt64(bytes, offset + 6); return destination != IntPtr.Zero;
        }
        private static IntPtr AllocationBase(IntPtr address)
        {
            if (VirtualQuery(address, out var value, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0 || value.AllocationBase == IntPtr.Zero)
                throw new UnsupportedSignatureException("Playback executable allocation is unavailable.");
            return value.AllocationBase;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
        { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
        [DllImport("kernel32")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint length);
        [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint bytes, uint allocationType, uint protection);
        [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint bytes, uint protection, out uint old);
        [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint bytes);
    }
}
