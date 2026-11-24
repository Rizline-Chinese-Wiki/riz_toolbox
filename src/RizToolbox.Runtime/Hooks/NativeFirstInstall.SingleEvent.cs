using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeFirstInstall
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ReadSingleEntryNative();

    private static void VerifySingleEventMemory(NativeFirstInstallInformationV7 packet, IntPtr helper, IntPtr module)
    {
        var value = packet.SingleEvent;
        if (!NativeSingleEventPublication.HasShape(value, packet.Listener.Common, packet.Row25))
            throw new UnsupportedSignatureException("The Single event publication has no complete current identity.");
        foreach (var entry in new[] {
            ("RizTweenSingleEventContext_GetMidEntry", value.MidEntry),
            ("RizTweenSingleEventContext_GetPreEntry", value.PreEntry),
            ("RizTweenSingleEventContext_GetPostEntry", value.PostEntry) })
        {
            if (!NativeLibrary.TryGetExport(helper, entry.Item1, out var getter))
                throw new UnsupportedSignatureException("The loaded helper has no complete Single event producer.");
            HookTargetValidation.VerifyExecutable(getter, helper);
            if (Marshal.GetDelegateForFunctionPointer<ReadSingleEntryNative>(getter)() != entry.Item2)
                throw new UnsupportedSignatureException("A Single event entry differs from its native publication.");
            HookTargetValidation.VerifyExecutable(entry.Item2, helper);
        }
        HookTargetValidation.VerifyReadable(value.Allocation, 4096);
        VerifyProtection(value.Allocation, 0x20);
        VerifyProtection(IntPtr.Add(value.Allocation, 4095), 0x20);
        if (VirtualQuery(value.Allocation, out var allocation, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
            || allocation.AllocationBase != value.Allocation || allocation.State != 0x1000 || allocation.Type != 0x20000)
            throw new UnsupportedSignatureException("The Single event allocation lost its retained private memory identity.");

        // Reconstruct every byte independently, including CC padding. Reading
        // only code headers would miss altered xdata, RF rows or unused bytes.
        var expected = Enumerable.Repeat((byte)0xcc, 4096).ToArray();
        var unwind = Convert.FromHexString("21000A0000E4070000C4080000740F0000640E0000340D00F00FBC006A10BC00A0E3D401");
        void Int32At(byte[] bytes, int offset, long number)
            => BitConverter.GetBytes(checked((int)number)).CopyTo(bytes, offset);
        void UInt32At(byte[] bytes, int offset, ulong number)
            => BitConverter.GetBytes(checked((uint)number)).CopyTo(bytes, offset);
        void Branch(byte[] bytes, int offset, IntPtr instruction, byte opcode, IntPtr target)
        {
            bytes[offset] = opcode;
            Int32At(bytes, offset + 1, target.ToInt64() - instruction.ToInt64() - 5);
        }
        void Relay(int offset, IntPtr target)
        {
            expected[offset] = 0xff; expected[offset + 1] = 0x25;
            Array.Clear(expected, offset + 2, 4);
            BitConverter.GetBytes(target.ToInt64()).CopyTo(expected, offset + 6);
        }
        Relay(0x500, value.MidEntry); Relay(0x520, value.PreEntry); Relay(0x540, value.PostEntry);
        var loop = IntPtr.Add(module, 0xbc1090);
        for (var index = 0; index < 3; ++index)
        {
            var site = NativeSingleEventPublication.Site(value, index); var offset = index * 0x100;
            Branch(expected, offset, site.PostStub, 0xe8, value.PostRelay);
            expected[offset + 5] = 0xff; expected[offset + 6] = 0xc6;
            expected[offset + 7] = 0x8b; expected[offset + 8] = 0xce;
            expected[offset + 9] = 0x0f; expected[offset + 10] = 0x84;
            Int32At(expected, offset + 11, loop.ToInt64() - site.PostStub.ToInt64() - 15);
            expected[offset + 15] = 0x0f; expected[offset + 16] = 0x85;
            Int32At(expected, offset + 17, loop.ToInt64() - site.PostStub.ToInt64() - 21);
            unwind.CopyTo(expected, offset + 0x40);
            var baseAddress = value.FunctionTableBase;
            UInt32At(expected, 0x380 + index * 12, unchecked((ulong)site.PostStub.ToInt64()) - baseAddress);
            UInt32At(expected, 0x384 + index * 12, unchecked((ulong)site.PostStub.ToInt64()) - baseAddress + 21);
            UInt32At(expected, 0x388 + index * 12, unchecked((ulong)site.UnwindInfo.ToInt64()) - baseAddress);
        }
        var actual = new byte[4096]; Marshal.Copy(value.Allocation, actual, 0, actual.Length);
        if (!actual.SequenceEqual(expected))
            throw new UnsupportedSignatureException("The retained Single event block changed code, xdata, RF or padding bytes.");
        for (var index = 0; index < 7; ++index)
        {
            var span = NativeSingleEventPublication.Span(value, index);
            HookTargetValidation.VerifyReadable(span.Pc, span.Width);
            VerifyProtection(span.Pc, span.Protection);
            VerifyProtection(IntPtr.Add(span.Pc, checked((int)span.Width - 1)), span.Protection);
            var patch = Enumerable.Repeat((byte)0x90, checked((int)span.Width)).ToArray();
            var call = index == 0 || index % 2 == 1;
            // Six-byte original memory CALLs retain their exact source+6
            // return PC by placing the replacement CALL after a leading NOP.
            var start = index is 1 or 5 ? 1 : 0;
            var target = index == 0 ? value.MidRelay : call ? value.PreRelay
                : NativeSingleEventPublication.Site(value, (index - 2) / 2).PostStub;
            Branch(patch, start, IntPtr.Add(span.Pc, start), call ? (byte)0xe8 : (byte)0xe9, target);
            var bytes = new byte[patch.Length]; Marshal.Copy(span.Pc, bytes, 0, bytes.Length);
            if (!bytes.SequenceEqual(patch))
                throw new UnsupportedSignatureException("A Single event source span changed its exact published patch.");
        }
        // Bytes are validated before asking the OS to follow a CHAININFO row.
        for (var index = 0; index < 3; ++index)
        {
            var site = NativeSingleEventPublication.Site(value, index);
            foreach (var boundary in new[] { 0, 5, 7, 9, 15 })
            {
                var function = RtlLookupFunctionEntry(unchecked((ulong)IntPtr.Add(site.PostStub, boundary).ToInt64()), out var imageBase, IntPtr.Zero);
                if (function == IntPtr.Zero || imageBase != value.FunctionTableBase)
                    throw new UnsupportedSignatureException("A Single event post boundary lost its OS unwind registration.");
                HookTargetValidation.VerifyReadable(function, 12);
                for (var part = 0; part < 3; ++part)
                    if (Marshal.ReadInt32(function, part * 4) != BitConverter.ToInt32(expected, 0x380 + index * 12 + part * 4))
                        throw new UnsupportedSignatureException("The OS Single event RF differs from the sealed publication.");
            }
        }
    }
}
