using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal readonly record struct NativeVector3BufferSlice(int Index, int Component, int Components,
    NativeVector3BufferBits Value);

internal sealed record NativeVector3BufferSliceWriteObservation(NativeRootedVector3Buffer Before,
    NativeRootedVector3Buffer After, NativeVector3BufferSlice Slice)
{
    internal bool StoreEntered => true;
    internal bool AtomicConditionalWrite => false;
}

internal sealed partial class NativeRuntime
{
    private static void ValidateLineVectorSlice(NativeRootedVector3Buffer buffer, NativeVector3BufferSlice slice)
    {
        if (slice.Index < 0 || slice.Index >= buffer.Length
            || !(slice.Component == 0 && slice.Components == 2 || slice.Component == 2 && slice.Components == 1))
            throw new ArgumentOutOfRangeException(nameof(slice), "The original producer array store is an in-range XY or Z word group.");
    }

    internal static NativeRootedVector3Buffer PatchLineVector3BufferSlice(NativeRootedVector3Buffer before,
        NativeVector3BufferSlice slice)
    {
        ArgumentNullException.ThrowIfNull(before); ValidateLineVectorSlice(before, slice);
        if (before.Items.Count != before.Length) throw new InvalidDataException("The expected array view is incomplete.");
        var items = before.Items.ToArray(); var previous = items[slice.Index];
        items[slice.Index] = slice.Component == 0
            ? new(slice.Value.X, slice.Value.Y, previous.Z) : new(previous.X, previous.Y, slice.Value.Z);
        return new(before.Array, before.Class, before.ElementClass, before.Length, Array.AsReadOnly(items));
    }

    /// <summary>A selected word group in one original physical array element.
    /// It does not rewrite an earlier prefix or replace the shared array. The
    /// full surrounding array, including tail/aliases, remains conditional.</summary>
    internal NativeVector3BufferSliceWriteObservation WriteRootedLineVector3BufferSlice(RootHandle original,
        IntPtr exactArrayClass, NativeRootedVector3Buffer expected, NativeVector3BufferSlice slice,
        Action guard, Action verifySource, Action beforeStore)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(verifySource); ArgumentNullException.ThrowIfNull(beforeStore);
        ValidateLineVectorSlice(expected, slice);
        var expectedBits = CopyLineVectorBits(expected.Items, guard);
        if (expectedBits.Length != expected.Length) throw new InvalidDataException("The complete original array expectation is absent.");
        var target = PatchLineVector3BufferSlice(expected, slice);
        var entered = false; NativeVector3BufferSliceWriteObservation? completed = null;
        try
        {
            return WithLineVector3Buffer(original, exactArrayClass, expected.Length, guard, buffer =>
            {
                if (buffer.Array != expected.Array || buffer.Class != expected.Class
                    || buffer.ElementClass != expected.ElementClass || buffer.Length != expected.Length)
                    throw new NativeVector3BufferConflict("The selected element belongs to another original array/shape.");
                void RequireExpected()
                {
                    buffer.Storage(); verifySource(); guard();
                    RequireLineVectorBits(buffer.Read().Items, expectedBits);
                }
                RequireExpected();
                var width = checked(slice.Components * 4);
                var bytes = new byte[width];
                if (slice.Component == 0)
                {
                    BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), slice.Value.X);
                    BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), slice.Value.Y);
                }
                else BitConverter.TryWriteBytes(bytes, slice.Value.Z);
                var destination = IntPtr.Add(buffer.Data, checked(slice.Index * 12 + slice.Component * 4));
                VerifyLineVectorBufferWritable(destination, width);
                beforeStore(); guard(); RequireExpected();
                VerifyLineVectorBufferWritable(destination, width);
                entered = true;
                Marshal.Copy(bytes, 0, destination, width);
                guard();
                var after = buffer.Read(); RequireLineVectorBits(after.Items, target.Items);
                return completed = new(expected, after, slice);
            });
        }
        catch (Exception error)
        {
            error.Data["LineVector3SliceOriginalRoot"] = original;
            error.Data["LineVector3SliceStoreEntered"] = entered;
            error.Data["LineVector3SliceBefore"] = expected;
            error.Data["LineVector3SliceTarget"] = target;
            error.Data["LineVector3SliceSelection"] = slice;
            if (completed is not null) error.Data["LineVector3SliceWriteObservation"] = completed;
            throw;
        }
    }
}
