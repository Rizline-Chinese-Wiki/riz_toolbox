#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal readonly HashSet<string> MissingExports = new(StringComparer.Ordinal);
    internal readonly List<string> RequestedExports = new();
    internal uint ExtraFieldFlags { get; set; }
    internal IntPtr FieldParentClass { get; set; }
    internal int FieldParentQueries, ObjectFieldWrites, ValueFieldWrites;
    internal IntPtr LastWrittenOwner, LastWrittenField, LastWrittenObject;
    internal byte[] LastWrittenBytes = Array.Empty<byte>();
    internal bool AllFieldOwnersRooted = true, AllFieldReferencesRooted = true;
    internal bool ThrowOnFieldWrite { get; set; }
    internal Action<string>? BeforeExport { get; set; }
    internal int FieldWrites => ObjectFieldWrites + ValueFieldWrites;

    private IntPtr ExportPointer(string name)
    {
        BeforeExport?.Invoke(name);
        if (MissingExports.Contains(name) || !_exports.TryGetValue(name, out var export))
            throw new EntryPointNotFoundException("Synthetic absent export: " + name);
        return Marshal.GetFunctionPointerForDelegate(export);
    }

    private IntPtr FieldParent(IntPtr field)
    {
        FieldParentQueries++;
        if (field != Field) return IntPtr.Zero;
        return FieldParentClass == IntPtr.Zero ? TypeClass(Receiver) : FieldParentClass;
    }

    private void ObserveFieldWrite(IntPtr instance, IntPtr field)
    {
        LastWrittenOwner = instance; LastWrittenField = field;
        AllFieldOwnersRooted &= _handles.Values.Contains(instance);
        if (ThrowOnFieldWrite) throw new InvalidOperationException("Synthetic native field setter failure.");
    }

    private void SetFieldObject(IntPtr instance, IntPtr field, IntPtr value)
    {
        ObjectFieldWrites++;
        LastWrittenObject = value;
        AllFieldReferencesRooted &= value == IntPtr.Zero || _handles.Values.Contains(value);
        ObserveFieldWrite(instance, field);
    }

    private void SetFieldValue(IntPtr instance, IntPtr field, IntPtr valueBuffer)
    {
        ValueFieldWrites++;
        LastWrittenBytes = new byte[_types[FieldType].Size];
        Marshal.Copy(valueBuffer, LastWrittenBytes, 0, LastWrittenBytes.Length);
        ObserveFieldWrite(instance, field);
    }
}
